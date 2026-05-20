"""
Save and restore Windows desktop layouts across multiple monitors.

Requires: pip install -r requirements.txt

Examples:
  python src/layout_manager.py save League
  python src/layout_manager.py restore League
  python src/layout_manager.py update League
  python src/layout_manager.py update --profile-path "%LOCALAPPDATA%\\LayoutProfiles\\profiles\\League.json"
  python src/layout_manager.py edit "New Name" --profile-path "%LOCALAPPDATA%\\LayoutProfiles\\profiles\\League.json"
  python src/layout_manager.py edit "League" --profile-path "..." --replace-windows
  python src/layout_manager.py list
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any

import win32con
import win32gui
import win32process
from ctypes import byref, create_unicode_buffer, sizeof, windll, wintypes


kernel32 = windll.kernel32
user32 = windll.user32

PROCESS_QUERY_LIMITED_INFORMATION = 0x1000


def _init_dpi_awareness() -> None:
    """
    Match how Windows reports window rects across monitors (mixed DPI).
    Safe to call more than once.
    """
    if sys.platform != "win32":
        return
    if getattr(_init_dpi_awareness, "_done", False):
        return
    try:
        try:
            shcore = windll.shcore
            shcore.SetProcessDpiAwareness(2)  # PROCESS_PER_MONITOR_DPI_AWARE
        except Exception:
            try:
                user32.SetProcessDPIAware()
            except Exception:
                pass
    except Exception:
        pass
    setattr(_init_dpi_awareness, "_done", True)


_init_dpi_awareness()


@dataclass
class RestoreOutcome:
    """Result of restore_profile (for CLI and GUI)."""

    profile_found: bool
    restored_count: int
    missing: list[str]  # human-readable lines
    profile_path: Path | None = None
    started_exes: list[str] = field(default_factory=list)


@dataclass
class UpdateOutcome:
    """Result of update_profile (for CLI and GUI)."""

    profile_found: bool
    updated_count: int
    missing_count: int
    missing: list[str]  # human-readable lines for windows not found live
    profile_path: Path | None = None


@dataclass
class EditOutcome:
    """Result of edit_profile (rename and/or replace window list)."""

    profile_found: bool
    profile_path: Path | None = None
    renamed: bool = False
    windows_replaced: bool = False
    window_count: int = 0
    error: str | None = None


@dataclass
class WindowRecord:
    exe_path: str
    title: str
    left: int
    top: int
    right: int
    bottom: int
    maximized: bool
    minimized: bool
    z_index: int


def _profiles_dir() -> Path:
    base = Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData" / "Local"))
    d = base / "LayoutProfiles" / "profiles"
    d.mkdir(parents=True, exist_ok=True)
    return d


def profiles_dir() -> Path:
    """Directory containing ``*.json`` profile files."""
    return _profiles_dir()


def _profile_path(name: str) -> Path:
    safe = re.sub(r"[^a-zA-Z0-9._-]+", "_", name).strip("._-") or "default"
    return _profiles_dir() / f"{safe}.json"


def _get_exe_path(pid: int) -> str:
    h = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not h:
        return ""
    try:
        buf = create_unicode_buffer(wintypes.MAX_PATH)
        size = wintypes.DWORD(sizeof(buf))
        if kernel32.QueryFullProcessImageNameW(h, 0, buf, byref(size)):
            return buf.value
    finally:
        kernel32.CloseHandle(h)
    return ""


def _is_alt_tab_window(hwnd: int) -> bool:
    if not win32gui.IsWindowVisible(hwnd):
        return False
    if win32gui.GetWindow(hwnd, win32con.GW_OWNER):
        return False
    ex = win32gui.GetWindowLong(hwnd, win32con.GWL_EXSTYLE)
    if ex & win32con.WS_EX_TOOLWINDOW:
        return False
    if ex & win32con.WS_EX_NOACTIVATE:
        return False
    style = win32gui.GetWindowLong(hwnd, win32con.GWL_STYLE)
    if style & win32con.WS_CHILD:
        return False
    return True


def _should_skip_title(title: str) -> bool:
    if not title:
        return True
    skip = {
        "Program Manager",
        "MSCTFIME UI",
        "Default IME",
        "Windows Input Experience",
        "NVIDIA GeForce Overlay",
        "GDI+ Window",
    }
    if title in skip:
        return True
    if title == "Task View" or title.startswith("Windows Shell Experience"):
        return True
    return False


# Shell / system processes only. Do not add game launchers here (e.g. LeagueClient.exe,
# RiotClientServices.exe); those must remain launchable for layout restore.
# ``explorer.exe`` is allowed: restore opens File Explorer windows when missing.
_LAUNCH_DENY = frozenset(
    {
        "sihost.exe",
        "searchhost.exe",
        "taskhostw.exe",
        "runtimebroker.exe",
        "dllhost.exe",
        "ctfmon.exe",
        "dwm.exe",
        "csrss.exe",
        "smss.exe",
        "lsass.exe",
        "winlogon.exe",
        "fontdrvhost.exe",
        "applicationframehost.exe",
    }
)


def _allow_launch(exe_path: str) -> bool:
    exe_path = (exe_path or "").strip()
    if not exe_path:
        return False
    if not os.path.isfile(exe_path):
        return False
    base = os.path.basename(exe_path).lower()
    return base not in _LAUNCH_DENY


def launch_executable(exe_path: str, *, window_title: str | None = None) -> bool:
    """
    Start a program by full path (Windows). Returns True if a process was started.
    Skips system/shell executables in ``_LAUNCH_DENY``.

    Uses ``subprocess.Popen`` first; if that fails, falls back to ``ShellExecute`` (same
    as double‑click), which some Riot / League installs handle more reliably.

    For File Explorer, pass ``window_title`` so restore can open the matching folder/shell
    location (and multiple Explorer rows can each start a window).
    """
    if not _allow_launch(exe_path):
        return False
    if _is_explorer_exe(exe_path) and window_title:
        return launch_explorer_for_title(window_title)
    argv = [exe_path]
    if not argv:
        return False
    cwd = os.path.dirname(exe_path) or None
    flags = 0
    if sys.platform == "win32":
        flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP  # type: ignore[attr-defined]
    try:
        subprocess.Popen(
            argv,
            cwd=cwd,
            close_fds=True,
            creationflags=flags,
        )
        return True
    except OSError:
        pass
    if sys.platform == "win32":
        try:
            import win32api
            import win32con

            rc = win32api.ShellExecute(0, "open", exe_path, None, cwd, win32con.SW_SHOWNORMAL)
            if rc > 32:
                return True
        except Exception:
            pass
    return False


def _exe_matches_filters(exe_lower: str, filters: list[str] | None) -> bool:
    if not filters:
        return True
    name = os.path.basename(exe_lower)
    for f in filters:
        fl = f.lower().strip()
        if not fl:
            continue
        if fl in exe_lower or fl == name:
            return True
    return False


def _normalize_exe_path(p: str) -> str:
    """Lowercase + ``normpath`` so saved JSON, install heuristics, and live PIDs agree."""
    s = (p or "").strip()
    if not s:
        return ""
    return os.path.normpath(s).lower()


_LEAGUE_RIOT_CLIENT_BASES = frozenset(
    {
        "leagueclient.exe",
        "leagueclientux.exe",
        "riotclientservices.exe",
        "riotclientux.exe",
    }
)


def _collect_windows(
    exe_filters: list[str] | None = None,
    *,
    include_pairs: frozenset[tuple[str, str]] | None = None,
) -> list[WindowRecord]:
    _init_dpi_awareness()
    records: list[WindowRecord] = []
    current_pid = os.getpid()

    def enum_cb(hwnd: int, _: Any) -> None:
        if not _is_alt_tab_window(hwnd):
            return
        title = win32gui.GetWindowText(hwnd)
        if _should_skip_title(title):
            return
        _, pid = win32process.GetWindowThreadProcessId(hwnd)
        if pid == current_pid:
            return
        exe = _get_exe_path(pid)
        if not exe:
            return
        exe_lower = _normalize_exe_path(exe)
        if not _exe_matches_filters(exe_lower, exe_filters):
            return
        key = (exe_lower, title)
        if include_pairs is not None and key not in include_pairs:
            return
        rect = win32gui.GetWindowRect(hwnd)
        left, top, right, bottom = rect
        width = right - left
        height = bottom - top
        if width < 40 or height < 40:
            return
        placement = win32gui.GetWindowPlacement(hwnd)
        show_cmd = placement[1]
        maximized = show_cmd == win32con.SW_SHOWMAXIMIZED
        minimized = show_cmd == win32con.SW_SHOWMINIMIZED
        records.append(
            WindowRecord(
                exe_path=exe_lower,
                title=title,
                left=left,
                top=top,
                right=right,
                bottom=bottom,
                maximized=maximized,
                minimized=minimized,
                z_index=len(records),
            )
        )

    win32gui.EnumWindows(enum_cb, None)
    return records


def list_pickable_windows(exe_filters: list[str] | None = None) -> list[WindowRecord]:
    """Windows that can appear in the save picker (same rules as a full save with no row selection)."""
    return _collect_windows(exe_filters, include_pairs=None)


def save_profile(
    name: str,
    exe_filters: list[str] | None = None,
    *,
    launch_if_missing: bool = True,
    include_pairs: frozenset[tuple[str, str]] | None = None,
) -> Path:
    windows = _collect_windows(exe_filters, include_pairs=include_pairs)
    path = _profile_path(name)
    payload = {
        "version": 1,
        "name": name,
        "windows": [asdict(w) for w in windows],
        "launch_if_missing": bool(launch_if_missing),
    }
    path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    return path


def profile_launch_if_missing(name: str) -> bool:
    """Return the saved ``launch_if_missing`` flag for a profile (default True)."""
    path = _profile_path(name)
    if not path.is_file():
        return True
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return True
    return bool(data.get("launch_if_missing", True))


def _normalize_title(s: str) -> str:
    return " ".join(s.lower().split())


def _title_match(saved: str, actual: str) -> bool:
    if saved == actual:
        return True
    sa, sb = _normalize_title(saved), _normalize_title(actual)
    if sa == sb:
        return True
    if sa and sb.startswith(sa):
        return True
    if sa and sa in sb:
        return True
    return False


_EXPLORER_BASENAME = "explorer.exe"
_EXPLORER_TITLE_SUFFIX = " - file explorer"

# Normalized Explorer sidebar labels -> ``explorer.exe`` shell: argument.
_EXPLORER_SHELL_BY_LABEL: dict[str, str] = {
    "home": "shell:Home",
    "gallery": "shell:Gallery",
    "one drive": "shell:OneDrive",
    "onedrive": "shell:OneDrive",
    "this pc": "shell:MyComputerFolder",
    "recycle bin": "shell:RecycleBinFolder",
    "network": "shell:NetworkPlacesFolder",
    "control panel": "shell:ControlPanelFolder",
    "desktop": "shell:Desktop",
    "downloads": "shell:Downloads",
    "documents": "shell:Personal",
    "pictures": "shell:My Pictures",
    "music": "shell:My Music",
    "videos": "shell:My Video",
}


def _is_explorer_exe(exe_path: str) -> bool:
    return os.path.basename(_normalize_exe_path(exe_path)).lower() == _EXPLORER_BASENAME


def _explorer_title_key(title: str) -> str:
    t = _normalize_title(title)
    if t.endswith(_EXPLORER_TITLE_SUFFIX):
        return t[: -len(_EXPLORER_TITLE_SUFFIX)].strip()
    return t


def _explorer_launch_argv(exe_path: str, title: str) -> list[str]:
    """Build argv for a new File Explorer window (shell is usually already running)."""
    exe = (exe_path or "").strip()
    if not exe:
        return []
    key = _explorer_title_key(title)
    shell = _EXPLORER_SHELL_BY_LABEL.get(key)
    if shell:
        return [exe, shell]
    folder = _known_folder_path_for_explorer_title(title)
    if folder:
        return [exe, folder]
    return [exe]


def _title_match_explorer(saved: str, actual: str) -> bool:
    """File Explorer titles drift (folder name vs 'Folder - File Explorer')."""
    if _title_match(saved, actual):
        return True
    sk, ak = _explorer_title_key(saved), _explorer_title_key(actual)
    if sk == ak:
        return True
    if sk and ak.startswith(sk):
        return True
    if ak and sk.startswith(ak):
        return True
    return False


def _explorer_equivalent_exes(saved_exe: str) -> frozenset[str]:
    out: set[str] = set()
    norm = _normalize_exe_path(saved_exe)
    if norm:
        out.add(norm)
    windir = os.environ.get("WINDIR", r"C:\Windows")
    for rel in ("explorer.exe", os.path.join("System32", "explorer.exe")):
        p = _normalize_exe_path(os.path.join(windir, rel))
        if os.path.isfile(p):
            out.add(p)
    return frozenset(out)


def _explorer_exe_path() -> str:
    windir = os.environ.get("WINDIR", r"C:\Windows")
    for rel in (os.path.join("System32", "explorer.exe"), "explorer.exe"):
        p = os.path.join(windir, rel)
        if os.path.isfile(p):
            return p
    return os.path.join(windir, "explorer.exe")


def _known_folder_path_for_explorer_title(title: str) -> str | None:
    raw = (title or "").strip().strip('"')
    if len(raw) >= 3 and raw[1] == ":" and os.path.isdir(raw):
        return os.path.normpath(raw)
    key = _explorer_title_key(title)
    if not key:
        return None
    home = os.path.expanduser("~")
    folders = {
        "documents": os.path.join(home, "Documents"),
        "downloads": os.path.join(home, "Downloads"),
        "desktop": os.path.join(home, "Desktop"),
        "pictures": os.path.join(home, "Pictures"),
        "music": os.path.join(home, "Music"),
        "videos": os.path.join(home, "Videos"),
        "home": home,
    }
    path = folders.get(key)
    if path and os.path.isdir(path):
        return path
    return None


def launch_explorer_for_title(title: str) -> bool:
    """Open a File Explorer window for a saved title (known folder, path, or shell alias)."""
    if sys.platform != "win32":
        return False
    explorer = _explorer_exe_path()
    if not os.path.isfile(explorer):
        return False
    argv = _explorer_launch_argv(explorer, title)
    try:
        subprocess.Popen(argv, close_fds=True)
        return True
    except OSError:
        return False


def _riot_bootstrap_exe(install_root: str) -> str:
    """Prefer ``RiotClientServices.exe`` under ``install_root/Riot Client/``; else ``RiotClientUx.exe``."""
    riot_dir = os.path.join(install_root, "Riot Client")
    riot_svc = os.path.normpath(os.path.join(riot_dir, "RiotClientServices.exe")).lower()
    if os.path.isfile(riot_svc):
        return riot_svc
    riot_ux = os.path.normpath(os.path.join(riot_dir, "RiotClientUx.exe")).lower()
    return riot_ux


def _league_client_exe(install_root: str) -> str:
    return os.path.normpath(
        os.path.join(install_root, "League of Legends", "LeagueClient", "LeagueClient.exe")
    ).lower()


def _league_client_ux_exe(install_root: str) -> str:
    return os.path.normpath(
        os.path.join(install_root, "League of Legends", "LeagueClientUx", "LeagueClientUx.exe")
    ).lower()


def _riot_league_install_root_from_exe_in_lol_tree(exe_path_lower: str) -> str | None:
    """
    Walk parents until ``.../League of Legends/<anything>/file.exe``; return the Riot-style
    install root (parent of the ``League of Legends`` folder), or None.
    """
    cur = _normalize_exe_path(exe_path_lower)
    while True:
        par = os.path.dirname(cur)
        if not par or par == cur:
            return None
        if os.path.basename(par).lower() == "league of legends":
            return _normalize_exe_path(os.path.dirname(par))
        cur = par


def _riot_league_install_paths(exe_path_lower: str) -> tuple[str, str, str] | None:
    """
    From ``LeagueClient.exe`` / ``LeagueClientUx.exe`` (under ``.../League of Legends/...``) or
    ``RiotClientServices.exe`` / ``RiotClientUx.exe`` (under ``.../Riot Client/``), return
    ``(install_root, league_client_exe, riot_bootstrap_exe)`` as normalized lower paths, or None.

    ``riot_bootstrap_exe`` prefers ``RiotClientServices.exe``; if missing, uses ``RiotClientUx.exe``
    under the same ``Riot Client`` folder when that file exists.

    **Path cases:** (1) League binaries under ``<root>/League of Legends/LeagueClient/`` or
    ``.../LeagueClientUx/``; (2) Riot bootstrap exes under ``<root>/Riot Client/``; ``<root>`` is
    typically ``.../Riot Games`` so the other tree is the sibling under the same root.
    """
    exe_norm = _normalize_exe_path(exe_path_lower)
    base = os.path.basename(exe_norm).lower()
    parent = os.path.dirname(exe_norm)

    if base in ("leagueclient.exe", "leagueclientux.exe"):
        install_root = _riot_league_install_root_from_exe_in_lol_tree(exe_norm)
        if not install_root:
            return None
        league_client = _league_client_exe(install_root)
        riot_bootstrap = _riot_bootstrap_exe(install_root)
        return install_root, league_client, riot_bootstrap

    if base in ("riotclientservices.exe", "riotclientux.exe"):
        install_root = _normalize_exe_path(os.path.dirname(parent))
        league_client = _league_client_exe(install_root)
        riot_bootstrap = _riot_bootstrap_exe(install_root)
        return install_root, league_client, riot_bootstrap
    return None


def _riot_league_all_equivalent_exes_on_disk(install_root: str) -> set[str]:
    """
    Every on-disk League/Riot client exe under ``install_root`` we should treat as the same app
    for restore matching (bootstrap vs LeagueClient vs LeagueClientUx, plus sibling exes in the
    default folders when present).
    """
    root = _normalize_exe_path(install_root)
    out: set[str] = set()
    for p in (
        _league_client_exe(root),
        _league_client_ux_exe(root),
        _riot_bootstrap_exe(root),
    ):
        if p and os.path.isfile(p):
            out.add(_normalize_exe_path(p))
    riot_dir = os.path.join(root, "Riot Client")
    for extra in ("RiotClientServices.exe", "RiotClientUx.exe"):
        p = _normalize_exe_path(os.path.join(riot_dir, extra))
        if os.path.isfile(p):
            out.add(p)
    lol = os.path.join(root, "League of Legends")
    for sub in ("LeagueClient", "LeagueClientUx"):
        d = os.path.join(lol, sub)
        if not os.path.isdir(d):
            continue
        try:
            for name in os.listdir(d):
                if not name.lower().endswith(".exe"):
                    continue
                fp = _normalize_exe_path(os.path.join(d, name))
                if os.path.isfile(fp):
                    out.add(fp)
        except OSError:
            continue
    return out


def _equivalent_exe_paths_for_saved(saved_exe_path: str) -> frozenset[str]:
    """
    Exe paths that may own the same logical window after a cold start (Riot bootstrap).
    Keeps matching narrow: only League/Riot Client pairs under the same Riot Games root.
    """
    norm_saved = _normalize_exe_path(saved_exe_path)
    out: set[str] = {norm_saved} if norm_saved else set()
    trio = _riot_league_install_paths(norm_saved)
    if not trio:
        return frozenset(out)
    install_root, _, _ = trio
    out.update(_riot_league_all_equivalent_exes_on_disk(install_root))
    return frozenset(out)


def _league_riot_same_install(saved_exe_path: str, live_exe_path: str) -> bool:
    """
    True when both exes resolve to the same Riot-style install root and ``live`` is a known
    League/Riot client binary (basename allow-list). Handles symlink / ``QueryFullProcessImageName``
    spelling vs saved JSON without listing every path variant in ``allowed_exes``.
    """
    live = _normalize_exe_path(live_exe_path)
    if os.path.basename(live).lower() not in _LEAGUE_RIOT_CLIENT_BASES:
        return False
    ts = _riot_league_install_paths(saved_exe_path)
    tl = _riot_league_install_paths(live)
    return bool(ts and tl and ts[0] == tl[0])


def _launch_paths_for_record(rec_exe_path: str) -> list[str]:
    """
    Return executables to try when a saved window is missing.

    For League / Riot installs under the same root: **Riot bootstrap first**
    (``RiotClientServices.exe`` or ``RiotClientUx.exe``), then ``LeagueClient.exe`` if
    present, then ``LeagueClientUx.exe`` under ``League of Legends/LeagueClientUx/``, then
    the saved primary path if it is another on-disk launcher (deduped). The UX client alone
    is often not enough without the bootstrap / main client.
    """
    primary = (rec_exe_path or "").strip()
    if not primary:
        return []
    trio = _riot_league_install_paths(_normalize_exe_path(primary))
    if not trio:
        return [primary]
    install_root, league_client, riot_bootstrap = trio
    league_ux = _league_client_ux_exe(install_root)
    paths: list[str] = []
    if os.path.isfile(riot_bootstrap):
        paths.append(riot_bootstrap)
    if os.path.isfile(league_client):
        paths.append(league_client)
    if os.path.isfile(league_ux):
        paths.append(league_ux)
    pl = _normalize_exe_path(primary)
    seen = {_normalize_exe_path(p) for p in paths}
    if pl not in seen and os.path.isfile(primary):
        paths.append(primary)
    return paths


def _title_match_league_riot(saved: str, actual: str) -> bool:
    """
    LoL / Riot Client titles drift (bootstrap vs client). Only used when the saved row
    is clearly a League or Riot Client executable under _equivalent_exe_paths_for_saved.
    """
    sl, al = saved.lower(), actual.lower()
    if not saved.strip() or not actual.strip():
        return True
    league_token = "league" in sl or "pvp.net" in sl
    riot_token = "riot" in sl or "riot client" in sl
    league_a = "league" in al or "pvp.net" in al
    riot_a = "riot" in al or "riot client" in al
    if league_token and riot_a:
        return True
    if riot_token and league_a:
        return True
    if league_token and league_a:
        return True
    if riot_token and riot_a:
        return True
    return False


def _resolve_profile_path(name: str, profile_path: Path | None) -> Path | None:
    """Resolve and validate a profile JSON path (must live under ``profiles_dir()``)."""
    if profile_path is not None:
        path = Path(profile_path).expanduser()
        try:
            path = path.resolve()
        except OSError:
            return None
        try:
            path.relative_to(_profiles_dir().resolve())
        except ValueError:
            return None
        return path
    if not (name or "").strip():
        return None
    return _profile_path(name)


def _load_profile_windows(path: Path) -> tuple[dict[str, Any], list[WindowRecord]] | None:
    if not path.is_file():
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return None
    saved: list[WindowRecord] = []
    for w in data.get("windows", []):
        saved.append(
            WindowRecord(
                exe_path=_normalize_exe_path(str(w["exe_path"])),
                title=str(w["title"]),
                left=int(w["left"]),
                top=int(w["top"]),
                right=int(w["right"]),
                bottom=int(w["bottom"]),
                maximized=bool(w.get("maximized", False)),
                minimized=bool(w.get("minimized", False)),
                z_index=int(w.get("z_index", 0)),
            )
        )
    return data, saved


def _geometry_from_hwnd(hwnd: int, rec: WindowRecord) -> WindowRecord:
    """Copy current screen geometry from a live window into a record (keeps exe/z_index)."""
    rect = win32gui.GetWindowRect(hwnd)
    left, top, right, bottom = rect
    placement = win32gui.GetWindowPlacement(hwnd)
    show_cmd = placement[1]
    maximized = show_cmd == win32con.SW_SHOWMAXIMIZED
    minimized = show_cmd == win32con.SW_SHOWMINIMIZED
    title = win32gui.GetWindowText(hwnd) or rec.title
    return WindowRecord(
        exe_path=rec.exe_path,
        title=title,
        left=left,
        top=top,
        right=right,
        bottom=bottom,
        maximized=maximized,
        minimized=minimized,
        z_index=rec.z_index,
    )


def update_profile(
    name: str = "",
    *,
    profile_path: Path | None = None,
) -> UpdateOutcome:
    """
    Refresh saved positions for windows already in a profile.

    Only updates geometry for profile rows that match a live window via ``_find_candidates``.
    Unmatched rows are left unchanged (not removed).
    """
    _init_dpi_awareness()
    path = _resolve_profile_path(name, profile_path)
    if path is None:
        return UpdateOutcome(
            profile_found=False,
            updated_count=0,
            missing_count=0,
            missing=[],
            profile_path=profile_path,
        )
    loaded = _load_profile_windows(path)
    if loaded is None:
        return UpdateOutcome(
            profile_found=False,
            updated_count=0,
            missing_count=0,
            missing=[],
            profile_path=path,
        )
    data, saved = loaded
    updated = 0
    missing_lines: list[str] = []
    new_windows: list[dict[str, Any]] = []

    for rec in saved:
        cands = _find_candidates(rec)
        if cands:
            hwnd = cands[0][0]
            new_windows.append(asdict(_geometry_from_hwnd(hwnd, rec)))
            updated += 1
        else:
            new_windows.append(asdict(rec))
            missing_lines.append(f"{rec.exe_path} | {rec.title}")

    data["windows"] = new_windows
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")
    return UpdateOutcome(
        profile_found=True,
        updated_count=updated,
        missing_count=len(missing_lines),
        missing=missing_lines,
        profile_path=path,
    )


def _find_candidates(
    saved: WindowRecord,
    *,
    exclude_hwnds: frozenset[int] | None = None,
) -> list[tuple[int, str]]:
    out: list[tuple[int, str]] = []
    excluded = exclude_hwnds or frozenset()
    explorer_row = _is_explorer_exe(saved.exe_path)
    allowed_exes = (
        _explorer_equivalent_exes(saved.exe_path)
        if explorer_row
        else _equivalent_exe_paths_for_saved(saved.exe_path)
    )
    league_riot_row = (
        os.path.basename(_normalize_exe_path(saved.exe_path)).lower() in _LEAGUE_RIOT_CLIENT_BASES
    )

    def enum_cb(hwnd: int, _: Any) -> None:
        if hwnd in excluded:
            return
        if not _is_alt_tab_window(hwnd):
            return
        _, pid = win32process.GetWindowThreadProcessId(hwnd)
        exe = _normalize_exe_path(_get_exe_path(pid))
        if not exe:
            return
        exe_ok = exe in allowed_exes or (
            league_riot_row and _league_riot_same_install(saved.exe_path, exe)
        )
        if not exe_ok:
            return
        title = win32gui.GetWindowText(hwnd)
        if explorer_row:
            if _title_match_explorer(saved.title, title):
                out.append((hwnd, title))
            return
        if _title_match(saved.title, title):
            out.append((hwnd, title))
            return
        if league_riot_row and _title_match_league_riot(saved.title, title):
            out.append((hwnd, title))

    win32gui.EnumWindows(enum_cb, None)
    return out


def _apply_geometry_via_placement(hwnd: int, rec: WindowRecord) -> bool:
    """File Explorer (CabinetWClass) is more reliable with SetWindowPlacement than MoveWindow."""
    try:
        placement = list(win32gui.GetWindowPlacement(hwnd))
        placement[1] = win32con.SW_SHOWMAXIMIZED if rec.maximized else win32con.SW_SHOWNORMAL
        placement[4] = (rec.left, rec.top, rec.right, rec.bottom)
        win32gui.SetWindowPlacement(hwnd, tuple(placement))
        return True
    except Exception:
        return False


def _apply_geometry(hwnd: int, rec: WindowRecord) -> None:
    _init_dpi_awareness()
    if win32gui.IsIconic(hwnd):
        win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
        time.sleep(0.05)
    if rec.maximized:
        win32gui.ShowWindow(hwnd, win32con.SW_MAXIMIZE)
        return
    w = rec.right - rec.left
    h = rec.bottom - rec.top
    use_placement = False
    try:
        if win32gui.GetClassName(hwnd) == "CabinetWClass":
            use_placement = _apply_geometry_via_placement(hwnd, rec)
    except Exception:
        use_placement = False
    if use_placement:
        if rec.minimized:
            win32gui.ShowWindow(hwnd, win32con.SW_MINIMIZE)
        return
    # MoveWindow uses outer rect in screen coords; applying twice helps some
    # DPI / cross-monitor + Electron (Discord) clients that resize after first move.
    try:
        win32gui.MoveWindow(hwnd, rec.left, rec.top, w, h, True)
        time.sleep(0.04)
        win32gui.MoveWindow(hwnd, rec.left, rec.top, w, h, True)
    except Exception:
        if _apply_geometry_via_placement(hwnd, rec):
            pass
        else:
            flags = win32con.SWP_NOZORDER | win32con.SWP_FRAMECHANGED
            win32gui.SetWindowPos(
                hwnd,
                0,
                rec.left,
                rec.top,
                w,
                h,
                flags,
            )
    if rec.minimized:
        win32gui.ShowWindow(hwnd, win32con.SW_MINIMIZE)


def restore_profile(
    name: str,
    retries: int | None = None,
    delay_s: float | None = None,
    *,
    launch_if_missing: bool | None = None,
    profile_path: Path | None = None,
) -> RestoreOutcome:
    """
    Load layout from ``profile_path`` when given (GUI / exact file match); otherwise from
    ``_profile_path(name)``. ``launch_if_missing=None`` reads the flag from the JSON file.
    """
    _init_dpi_awareness()
    if profile_path is not None:
        path = Path(profile_path).expanduser()
        try:
            path = path.resolve()
        except OSError:
            return RestoreOutcome(
                profile_found=False,
                restored_count=0,
                missing=[],
                profile_path=path,
                started_exes=[],
            )
        try:
            path.relative_to(_profiles_dir().resolve())
        except ValueError:
            return RestoreOutcome(
                profile_found=False,
                restored_count=0,
                missing=[],
                profile_path=path,
                started_exes=[],
            )
    else:
        path = _profile_path(name)
    if not path.is_file():
        return RestoreOutcome(
            profile_found=False,
            restored_count=0,
            missing=[],
            profile_path=path,
            started_exes=[],
        )
    data = json.loads(path.read_text(encoding="utf-8"))
    raw_windows = data.get("windows", [])
    do_launch = (
        bool(data.get("launch_if_missing", True))
        if launch_if_missing is None
        else bool(launch_if_missing)
    )

    if retries is None:
        retries = 10 if do_launch else 4
    if delay_s is None:
        delay_s = 0.55 if do_launch else 0.35

    saved: list[WindowRecord] = []
    for w in raw_windows:
        saved.append(
            WindowRecord(
                exe_path=_normalize_exe_path(str(w["exe_path"])),
                title=str(w["title"]),
                left=int(w["left"]),
                top=int(w["top"]),
                right=int(w["right"]),
                bottom=int(w["bottom"]),
                maximized=bool(w.get("maximized", False)),
                minimized=bool(w.get("minimized", False)),
                z_index=int(w.get("z_index", 0)),
            )
        )
    total = len(saved)
    saved.sort(key=lambda r: r.z_index, reverse=True)

    started_order: list[str] = []
    launched_exes_total: set[str] = set()
    launched_explorer_keys: set[str] = set()

    for attempt in range(retries):
        missing: list[WindowRecord] = []
        used_hwnds: set[int] = set()
        for rec in saved:
            cands = _find_candidates(rec, exclude_hwnds=frozenset(used_hwnds))
            if cands:
                hwnd = cands[0][0]
                used_hwnds.add(hwnd)
                _apply_geometry(hwnd, rec)
                continue
            if do_launch:
                if _is_explorer_exe(rec.exe_path):
                    launch_key = _explorer_title_key(rec.title) or rec.title
                    if launch_key not in launched_explorer_keys:
                        if launch_explorer_for_title(rec.title):
                            launched_explorer_keys.add(launch_key)
                            started_order.append(f"{_explorer_exe_path()} | {rec.title}")
                else:
                    for launch_path in _launch_paths_for_record(rec.exe_path):
                        lp = launch_path.lower()
                        if _allow_launch(launch_path) and lp not in launched_exes_total:
                            if launch_executable(launch_path):
                                launched_exes_total.add(lp)
                                started_order.append(launch_path)
                                break
            missing.append(rec)

        if not missing:
            return RestoreOutcome(
                profile_found=True,
                restored_count=total,
                missing=[],
                profile_path=path,
                started_exes=started_order,
            )
        if attempt < retries - 1:
            time.sleep(delay_s)
        saved = missing

    lines = [f"{rec.exe_path} | {rec.title}" for rec in saved]
    return RestoreOutcome(
        profile_found=True,
        restored_count=total - len(saved),
        missing=lines,
        profile_path=path,
        started_exes=started_order,
    )


def _parse_window_pair_arg(raw: str) -> tuple[str, str]:
    if "|" not in raw:
        raise ValueError(f"Expected exe_path|title, got: {raw!r}")
    exe, title = raw.split("|", 1)
    return (_normalize_exe_path(exe.strip()), title.strip())


def edit_profile(
    *,
    profile_path: Path | str,
    new_name: str,
    replace_windows: bool = False,
    window_pairs: frozenset[tuple[str, str]] | None = None,
) -> EditOutcome:
    """
    Rename a profile and/or replace its window list with the current desktop.

    Updates the JSON ``name`` field and renames the file when the sanitized
    filename changes. ``replace_windows`` captures all pickable windows now
    (same rules as ``save_profile``) while preserving ``launch_if_missing``.
    """
    path_in = Path(profile_path).expanduser()
    try:
        path = path_in.resolve()
    except OSError:
        return EditOutcome(profile_found=False, profile_path=path_in, error="Invalid profile path.")
    try:
        path.relative_to(_profiles_dir().resolve())
    except ValueError:
        return EditOutcome(
            profile_found=False,
            profile_path=path,
            error="Profile path is outside the profiles directory.",
        )
    if not path.is_file():
        return EditOutcome(profile_found=False, profile_path=path)

    name = (new_name or "").strip()
    if not name:
        return EditOutcome(profile_found=True, profile_path=path, error="Profile name cannot be empty.")

    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return EditOutcome(profile_found=False, profile_path=path)

    old_display = str(data.get("name", path.stem))
    renamed = name != old_display
    windows_replaced = False

    if replace_windows:
        windows = _collect_windows()
        data["windows"] = [asdict(w) for w in windows]
        windows_replaced = True
    elif window_pairs is not None:
        live = _collect_windows(include_pairs=window_pairs)
        live_by_key = {(w.exe_path, w.title): w for w in live}
        saved_by_key: dict[tuple[str, str], WindowRecord] = {}
        loaded = _load_profile_windows(path)
        if loaded:
            for rec in loaded[1]:
                saved_by_key[(rec.exe_path, rec.title)] = rec
        merged: list[WindowRecord] = []
        for key in window_pairs:
            if key in live_by_key:
                merged.append(live_by_key[key])
            elif key in saved_by_key:
                merged.append(saved_by_key[key])
        data["windows"] = [asdict(w) for w in merged]
        windows_replaced = True

    data["name"] = name
    data.setdefault("version", 1)
    data.setdefault("launch_if_missing", True)

    target = _profile_path(name)
    try:
        target = target.resolve()
    except OSError:
        target = _profile_path(name)

    if target != path and target.is_file():
        return EditOutcome(
            profile_found=True,
            profile_path=path,
            error=f"A profile named '{name}' already exists.",
        )

    target.write_text(json.dumps(data, indent=2), encoding="utf-8")
    if target != path:
        try:
            path.unlink()
        except OSError:
            pass

    window_count = len(data.get("windows", []))
    return EditOutcome(
        profile_found=True,
        profile_path=target,
        renamed=renamed,
        windows_replaced=windows_replaced,
        window_count=window_count,
    )


def delete_profile(profile_path: Path | str) -> None:
    """
    Remove a profile JSON file. Only deletes paths that resolve under ``profiles_dir()``
    (same ``resolve()`` + ``relative_to`` guard as ``restore_profile`` with ``profile_path``).

    Raises:
        ValueError: If the path is invalid or resolves outside the profiles directory.

    No-op if the file does not exist after resolution.
    """
    path = Path(profile_path).expanduser()
    try:
        path = path.resolve()
    except OSError as e:
        raise ValueError(f"Invalid profile path: {profile_path!r}") from e
    try:
        path.relative_to(_profiles_dir().resolve())
    except ValueError as e:
        raise ValueError("Profile path is outside the profiles directory.") from e
    if not path.is_file():
        return
    path.unlink()


def list_profile_rows() -> list[tuple[str, int, Path]]:
    """Return rows (restore_name, window_count, file_path) sorted by display name."""
    d = _profiles_dir()
    rows: list[tuple[str, int, Path]] = []
    for f in sorted(d.glob("*.json")):
        try:
            data = json.loads(f.read_text(encoding="utf-8"))
            display = str(data.get("name", f.stem))
            n = len(data.get("windows", []))
        except (json.JSONDecodeError, OSError):
            display, n = f.stem, 0
        rows.append((display, n, f))
    rows.sort(key=lambda r: r[0].lower())
    return rows


def list_profiles() -> None:
    d = _profiles_dir()
    rows = list_profile_rows()
    if not rows:
        print(f"No profiles in {d}")
        return
    for name, n, _ in rows:
        print(f"{name}\t{n} windows")


def main() -> None:
    p = argparse.ArgumentParser(description="Save/restore Windows layouts across monitors.")
    sub = p.add_subparsers(dest="cmd", required=True)

    sp = sub.add_parser("save", help="Capture current layout")
    sp.add_argument("name", help='Profile name, e.g. "League"')
    sp.add_argument(
        "--exe",
        action="append",
        metavar="FRAGMENT",
        help="Only include windows whose executable path contains this fragment (repeatable). "
        'Example: --exe LeagueClient --exe "Riot Client"',
    )
    sp.add_argument(
        "--window",
        action="append",
        metavar="EXE|TITLE",
        help="Include only this window (repeatable). Format: normalized exe path|title",
    )

    rp = sub.add_parser("restore", help="Apply a saved layout")
    rp.add_argument(
        "name",
        nargs="?",
        default="",
        help="Profile name (optional when --profile-path is set)",
    )
    rp.add_argument(
        "--profile-path",
        dest="profile_path",
        metavar="PATH",
        help="Exact profile JSON under the profiles directory (GUI / WinUI)",
    )
    rp.add_argument(
        "--retries",
        type=int,
        default=None,
        help="Locate/move attempts (default: longer when launching apps)",
    )
    rp.add_argument(
        "--delay",
        type=float,
        default=None,
        help="Seconds between retries (default: longer when launching apps)",
    )
    rp.add_argument(
        "--no-launch",
        action="store_true",
        help="Do not start missing programs; only move windows that already exist",
    )

    up = sub.add_parser(
        "update",
        help="Refresh saved positions for windows already in a profile (no new apps)",
    )
    up.add_argument(
        "name",
        nargs="?",
        default="",
        help="Profile name (optional when --profile-path is set)",
    )
    up.add_argument(
        "--profile-path",
        dest="profile_path",
        metavar="PATH",
        help="Exact profile JSON under the profiles directory (GUI / WinUI)",
    )

    ep = sub.add_parser("edit", help="Rename a profile and/or replace its window list")
    ep.add_argument(
        "--profile-path",
        dest="profile_path",
        required=True,
        metavar="PATH",
        help="Exact profile JSON under the profiles directory (GUI / WinUI)",
    )
    ep.add_argument("name", help="New display name for the profile")
    ep.add_argument(
        "--replace-windows",
        action="store_true",
        help="Replace saved windows with the current desktop layout (all pickable windows)",
    )
    ep.add_argument(
        "--window",
        action="append",
        metavar="EXE|TITLE",
        help="Set saved windows to these selections (repeatable). Format: exe path|title",
    )

    sub.add_parser("list", help="List saved profiles")

    sub.add_parser("list-windows", help="List pickable windows as JSON (GUI)")

    ap = sub.add_parser("path", help="Show where profiles are stored")

    dp = sub.add_parser("delete", help="Delete a saved profile JSON")
    dp.add_argument(
        "--profile-path",
        dest="profile_path",
        required=True,
        metavar="PATH",
        help="Exact profile JSON under the profiles directory (GUI / WinUI)",
    )

    args = p.parse_args()
    if args.cmd == "save":
        include_pairs: frozenset[tuple[str, str]] | None = None
        if args.window:
            include_pairs = frozenset(_parse_window_pair_arg(w) for w in args.window)
        out = save_profile(args.name, exe_filters=args.exe, include_pairs=include_pairs)
        n = len(json.loads(out.read_text(encoding="utf-8"))["windows"])
        print(f"Saved '{args.name}' ({n} windows) -> {out}")
    elif args.cmd == "restore":
        profile_path = Path(args.profile_path).expanduser() if args.profile_path else None
        if profile_path is not None:
            try:
                profile_path = profile_path.resolve()
            except OSError:
                pass
        name = (args.name or "").strip()
        if profile_path is None and not name:
            rp.error("restore requires a profile name or --profile-path")
        if profile_path is not None and not name:
            try:
                data = json.loads(profile_path.read_text(encoding="utf-8"))
                name = str(data.get("name", profile_path.stem))
            except (json.JSONDecodeError, OSError):
                name = profile_path.stem
        outcome = restore_profile(
            name,
            retries=args.retries,
            delay_s=args.delay,
            launch_if_missing=False if args.no_launch else None,
            profile_path=profile_path,
        )
        if not outcome.profile_found:
            print(f"Profile not found: {outcome.profile_path}", file=sys.stderr)
            sys.exit(1)
        if outcome.started_exes:
            uniq = []
            for p in outcome.started_exes:
                if p not in uniq:
                    uniq.append(p)
            print(f"Started {len(uniq)} program(s).")
        if outcome.missing:
            print(
                "Some windows were not found (start apps first, then retry):",
                file=sys.stderr,
            )
            for line in outcome.missing:
                print(f"  - {line}", file=sys.stderr)
        else:
            print(f"Restored profile '{args.name}' ({outcome.restored_count} windows).")
    elif args.cmd == "update":
        profile_path = Path(args.profile_path).expanduser() if args.profile_path else None
        if profile_path is not None:
            try:
                profile_path = profile_path.resolve()
            except OSError:
                pass
        name = (args.name or "").strip()
        if profile_path is None and not name:
            up.error("update requires a profile name or --profile-path")
        outcome = update_profile(name, profile_path=profile_path)
        if not outcome.profile_found:
            print(f"Profile not found: {outcome.profile_path}", file=sys.stderr)
            sys.exit(1)
        display = name
        if profile_path is not None and not display:
            try:
                data = json.loads(profile_path.read_text(encoding="utf-8"))
                display = str(data.get("name", profile_path.stem))
            except (json.JSONDecodeError, OSError):
                display = profile_path.stem
        if outcome.missing_count:
            print(
                f"Updated '{display}' ({outcome.updated_count} refreshed, "
                f"{outcome.missing_count} not found).",
            )
            print("Windows not found (kept previous positions):", file=sys.stderr)
            for line in outcome.missing:
                print(f"  - {line}", file=sys.stderr)
        else:
            print(
                f"Updated '{display}' ({outcome.updated_count} window"
                f"{'s' if outcome.updated_count != 1 else ''} refreshed).",
            )
    elif args.cmd == "list-windows":
        rows = list_pickable_windows()
        print(
            json.dumps(
                [{"exe_path": w.exe_path, "title": w.title} for w in rows],
                ensure_ascii=True,
            )
        )
    elif args.cmd == "edit":
        profile_path = Path(args.profile_path).expanduser()
        try:
            profile_path = profile_path.resolve()
        except OSError:
            pass
        window_pairs: frozenset[tuple[str, str]] | None = None
        if args.window:
            window_pairs = frozenset(_parse_window_pair_arg(w) for w in args.window)
        if args.replace_windows and window_pairs is not None:
            ep.error("Use either --replace-windows or --window, not both")
        outcome = edit_profile(
            profile_path=profile_path,
            new_name=args.name,
            replace_windows=bool(args.replace_windows),
            window_pairs=window_pairs,
        )
        if not outcome.profile_found:
            print(f"Profile not found: {outcome.profile_path}", file=sys.stderr)
            sys.exit(1)
        if outcome.error:
            print(outcome.error, file=sys.stderr)
            sys.exit(1)
        parts: list[str] = []
        if outcome.renamed:
            parts.append("renamed")
        if outcome.windows_replaced:
            parts.append(f"replaced ({outcome.window_count} windows)")
        detail = ", ".join(parts) if parts else "unchanged"
        print(f"Edited profile '{args.name}' ({detail}).")
    elif args.cmd == "list":
        list_profiles()
    elif args.cmd == "path":
        print(profiles_dir())
    elif args.cmd == "delete":
        profile_path = Path(args.profile_path).expanduser()
        try:
            profile_path = profile_path.resolve()
        except OSError as e:
            print(f"Invalid profile path: {args.profile_path!r}", file=sys.stderr)
            sys.exit(1)
        try:
            delete_profile(profile_path)
        except ValueError as e:
            print(str(e), file=sys.stderr)
            sys.exit(1)
        print(f"Deleted profile: {profile_path}")


if __name__ == "__main__":
    main()
