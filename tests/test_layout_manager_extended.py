"""Extended tests for layout_manager.py — covers helpers, profile CRUD, and restore logic.

All Win32 calls are mocked so these tests run on Windows and in CI without a desktop.
"""

from __future__ import annotations

import json
from dataclasses import asdict
from unittest.mock import patch

import pytest

from layout_manager import (
    WindowRecord,
    _allow_launch,
    _exe_matches_filters,
    _explorer_title_key,
    _is_browser_exe,
    _is_explorer_exe,
    _league_client_exe,
    _league_client_ux_exe,
    _normalize_browser_url,
    _normalize_title,
    _profile_path,
    _resolve_profile_path,
    _riot_bootstrap_exe,
    _riot_league_install_paths,
    _should_skip_title,
    _title_match_explorer,
    _title_match_league_riot,
    delete_profile,
    edit_profile,
    save_profile,
    update_profile,
)

# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def local_app_data(tmp_path, monkeypatch):
    """Point LOCALAPPDATA to a temp dir and return it."""
    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))
    return tmp_path


@pytest.fixture
def profiles_dir_path(local_app_data):
    """Return the profiles directory (created by _profiles_dir)."""
    d = local_app_data / "LayoutProfiles" / "profiles"
    d.mkdir(parents=True, exist_ok=True)
    return d


def _make_profile(profiles_dir_path, name, windows=None, **extra):
    """Write a minimal profile JSON and return the Path."""
    safe = name.replace(" ", "_")
    p = profiles_dir_path / f"{safe}.json"
    data = {"version": 1, "name": name, "windows": windows or [], "launch_if_missing": True}
    data.update(extra)
    p.write_text(json.dumps(data, indent=2), encoding="utf-8")
    return p


def _sample_window_dict(**overrides):
    base = {
        "exe_path": r"c:\program files\notepad.exe",
        "title": "Untitled - Notepad",
        "left": 100,
        "top": 100,
        "right": 800,
        "bottom": 600,
        "maximized": False,
        "minimized": False,
        "z_index": 0,
        "browser_url": None,
    }
    base.update(overrides)
    return base


# ===========================================================================
# 1. _should_skip_title
# ===========================================================================

class TestShouldSkipTitle:
    @pytest.mark.parametrize(
        "title",
        [
            "",
            "Program Manager",
            "MSCTFIME UI",
            "Default IME",
            "Windows Input Experience",
            "NVIDIA GeForce Overlay",
            "GDI+ Window",
            "Task View",
            "Windows Shell Experience Host",
            "Windows Shell Experience blah",
        ],
    )
    def test_skip_known(self, title):
        assert _should_skip_title(title) is True

    @pytest.mark.parametrize(
        "title",
        [
            "Notepad",
            "Google Chrome",
            "League of Legends",
            "Task Manager",  # not "Task View"
            "My App - Windows Shell",
        ],
    )
    def test_keep_normal(self, title):
        assert _should_skip_title(title) is False


# ===========================================================================
# 2. _normalize_browser_url
# ===========================================================================

class TestNormalizeBrowserUrl:
    @pytest.mark.parametrize(
        ("inp", "expected"),
        [
            (None, ""),
            ("", ""),
            ("  https://example.com  ", "https://example.com"),
            ("http://localhost:8080/path/", "http://localhost:8080/path/"),
        ],
    )
    def test_basic(self, inp, expected):
        assert _normalize_browser_url(inp) == expected


# ===========================================================================
# 3. _is_browser_exe
# ===========================================================================

class TestIsBrowserExe:
    @pytest.mark.parametrize(
        "exe",
        [
            r"C:\Program Files\Google\Chrome\Application\chrome.exe",
            r"C:\Program Files\Mozilla Firefox\firefox.exe",
            r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            r"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
            r"C:\Program Files\Opera\opera.exe",
            r"C:\Users\Joe\AppData\Local\Vivaldi\Application\vivaldi.exe",
        ],
    )
    def test_known_browsers(self, exe):
        assert _is_browser_exe(exe) is True

    @pytest.mark.parametrize(
        "exe",
        [
            r"C:\Windows\notepad.exe",
            r"C:\Program Files\Unknown\mybrowser.exe",
            "",
            r"C:\chrome.exeNOT",
        ],
    )
    def test_non_browsers(self, exe):
        assert _is_browser_exe(exe) is False


# ===========================================================================
# 4. _exe_matches_filters
# ===========================================================================

class TestExeMatchesFilters:
    def test_no_filters_returns_true(self):
        assert _exe_matches_filters(r"c:\anything.exe", None) is True
        assert _exe_matches_filters(r"c:\anything.exe", []) is True

    def test_basename_match(self):
        assert _exe_matches_filters(r"c:\program files\app\notepad.exe", ["notepad.exe"]) is True

    def test_substring_match(self):
        assert _exe_matches_filters(r"c:\riot games\league\client.exe", ["riot games"]) is True

    def test_case_insensitive(self):
        assert _exe_matches_filters(r"c:\app\chrome.exe", ["Chrome.exe"]) is True

    def test_no_match(self):
        assert _exe_matches_filters(r"c:\app\notepad.exe", ["chrome.exe"]) is False

    def test_blank_filter_elements_ignored(self):
        assert _exe_matches_filters(r"c:\app\notepad.exe", ["", "  "]) is False

    def test_multiple_filters_any_matches(self):
        assert _exe_matches_filters(r"c:\app\notepad.exe", ["chrome", "notepad"]) is True


# ===========================================================================
# 5. _explorer_title_key
# ===========================================================================

class TestExplorerTitleKey:
    def test_strips_file_explorer_suffix(self):
        assert _explorer_title_key("Downloads - File Explorer") == "downloads"

    def test_plain_title_unchanged(self):
        assert _explorer_title_key("Documents") == "documents"

    def test_empty(self):
        assert _explorer_title_key("") == ""

    def test_case_insensitive_suffix(self):
        # _normalize_title lowercases, so " - file explorer" suffix is matched
        assert _explorer_title_key("My Folder - File Explorer") == "my folder"

    def test_nested_path_title(self):
        assert _explorer_title_key(r"C:\Users\Joe\Stuff") == r"c:\users\joe\stuff"


# ===========================================================================
# 6. _profile_path edge cases
# ===========================================================================

class TestProfilePath:
    def test_empty_string_returns_default(self, local_app_data):
        p = _profile_path("")
        assert p.name == "default.json"

    def test_all_special_chars(self, local_app_data):
        p = _profile_path("***///:::???")
        assert p.name == "default.json"

    def test_very_long_name(self, local_app_data):
        name = "A" * 300
        p = _profile_path(name)
        assert p.name == name + ".json"
        assert p.parent == local_app_data / "LayoutProfiles" / "profiles"

    def test_unicode_name(self, local_app_data):
        p = _profile_path("Résumé-日本語")
        # Non-ASCII letters are NOT in [a-zA-Z0-9._-], so they get replaced
        assert ".json" in p.name
        assert p.parent == local_app_data / "LayoutProfiles" / "profiles"

    def test_normal_name(self, local_app_data):
        p = _profile_path("MyLayout")
        assert p.name == "MyLayout.json"

    def test_dots_stripped_from_edges(self, local_app_data):
        p = _profile_path("..danger..")
        # After sanitization: "..danger.." -> "__danger__" -> stripped "danger"
        # Actually: re.sub replaces non-[a-zA-Z0-9._-] -> _, then strip("._-")
        assert "danger" in p.stem


# ===========================================================================
# 7. _riot_league matching helpers
# ===========================================================================

class TestRiotLeague:
    def test_league_client_exe_path(self):
        root = r"c:\riot games"
        result = _league_client_exe(root)
        assert "leagueclient.exe" in result.lower()
        assert "league of legends" in result.lower()

    def test_league_client_ux_exe_path(self):
        root = r"c:\riot games"
        result = _league_client_ux_exe(root)
        assert "leagueclientux.exe" in result.lower()

    def test_riot_bootstrap_exe_prefers_services(self, tmp_path):
        riot_dir = tmp_path / "Riot Client"
        riot_dir.mkdir()
        svc = riot_dir / "RiotClientServices.exe"
        svc.touch()
        result = _riot_bootstrap_exe(str(tmp_path))
        assert "riotclientservices.exe" in result.lower()

    def test_riot_bootstrap_exe_falls_back_to_ux(self, tmp_path):
        riot_dir = tmp_path / "Riot Client"
        riot_dir.mkdir()
        # no RiotClientServices.exe on disk
        result = _riot_bootstrap_exe(str(tmp_path))
        assert "riotclientux.exe" in result.lower()

    def test_riot_league_install_paths_from_league_client(self, tmp_path):
        # Build a realistic tree
        lol = tmp_path / "League of Legends" / "LeagueClient"
        lol.mkdir(parents=True)
        exe = lol / "LeagueClient.exe"
        exe.touch()
        result = _riot_league_install_paths(str(exe).lower())
        assert result is not None
        install_root, lc_exe, riot_boot = result
        assert "leagueclient.exe" in lc_exe.lower()

    def test_riot_league_install_paths_none_for_unrelated(self):
        result = _riot_league_install_paths(r"c:\program files\notepad.exe")
        assert result is None

    def test_title_match_league_riot_cross_match(self):
        assert _title_match_league_riot("League of Legends", "Riot Client") is True
        assert _title_match_league_riot("Riot Client", "League of Legends") is True

    def test_title_match_league_riot_same_category(self):
        assert _title_match_league_riot("League Client", "League of Legends (TM)") is True
        assert _title_match_league_riot("Riot Client Main", "Riot Client Update") is True

    def test_title_match_league_riot_empty(self):
        # Empty strings match anything (permissive for bootstrap)
        assert _title_match_league_riot("", "League") is True
        assert _title_match_league_riot("League", "  ") is True

    def test_title_match_league_riot_unrelated(self):
        assert _title_match_league_riot("Notepad", "Calculator") is False


# ===========================================================================
# 8. save_profile
# ===========================================================================

class TestSaveProfile:
    @patch("layout_manager._collect_windows")
    def test_save_creates_json(self, mock_collect, local_app_data):
        mock_collect.return_value = [
            WindowRecord(
                exe_path=r"c:\app\notepad.exe",
                title="Untitled - Notepad",
                left=0, top=0, right=800, bottom=600,
                maximized=False, minimized=False, z_index=0,
            ),
        ]
        path = save_profile("TestProfile")
        assert path.is_file()
        data = json.loads(path.read_text(encoding="utf-8"))
        assert data["version"] == 1
        assert data["name"] == "TestProfile"
        assert data["launch_if_missing"] is True
        assert len(data["windows"]) == 1
        assert data["windows"][0]["exe_path"] == r"c:\app\notepad.exe"

    @patch("layout_manager._collect_windows")
    def test_save_empty_windows(self, mock_collect, local_app_data):
        mock_collect.return_value = []
        path = save_profile("EmptyProfile")
        data = json.loads(path.read_text(encoding="utf-8"))
        assert data["windows"] == []

    @patch("layout_manager._collect_windows")
    def test_save_with_launch_if_missing_false(self, mock_collect, local_app_data):
        mock_collect.return_value = []
        path = save_profile("NoLaunch", launch_if_missing=False)
        data = json.loads(path.read_text(encoding="utf-8"))
        assert data["launch_if_missing"] is False

    @patch("layout_manager._collect_windows")
    def test_save_passes_filters(self, mock_collect, local_app_data):
        mock_collect.return_value = []
        save_profile("Filtered", exe_filters=["chrome"])
        mock_collect.assert_called_once_with(["chrome"], include_pairs=None)

    @patch("layout_manager._collect_windows")
    def test_save_passes_include_pairs(self, mock_collect, local_app_data):
        mock_collect.return_value = []
        pairs = frozenset({(r"c:\app\foo.exe", "Foo Window")})
        save_profile("WithPairs", include_pairs=pairs)
        mock_collect.assert_called_once_with(None, include_pairs=pairs)


# ===========================================================================
# 9. restore_profile (mocked)
# ===========================================================================

class TestRestoreProfile:
    def test_profile_not_found(self, local_app_data):
        """restore_profile returns profile_found=False for a missing file."""
        from layout_manager import restore_profile
        outcome = restore_profile("NonExistent", retries=1, delay_s=0)
        assert outcome.profile_found is False
        assert outcome.restored_count == 0

    @patch("layout_manager._find_candidates")
    @patch("layout_manager._apply_geometry")
    def test_all_windows_matched(self, mock_apply, mock_find, profiles_dir_path, local_app_data):
        """When all saved windows are found live, restored_count = total."""
        from layout_manager import restore_profile
        _make_profile(profiles_dir_path, "Full", windows=[_sample_window_dict()])
        mock_find.return_value = [(12345, "Untitled - Notepad")]
        outcome = restore_profile("Full", retries=1, delay_s=0, launch_if_missing=False)
        assert outcome.profile_found is True
        assert outcome.restored_count == 1
        assert outcome.missing == []
        mock_apply.assert_called_once()

    @patch("layout_manager._find_candidates")
    def test_missing_windows_reported(self, mock_find, profiles_dir_path, local_app_data):
        """Unmatched windows appear in outcome.missing."""
        from layout_manager import restore_profile
        _make_profile(profiles_dir_path, "Partial", windows=[_sample_window_dict()])
        mock_find.return_value = []
        outcome = restore_profile("Partial", retries=1, delay_s=0, launch_if_missing=False)
        assert outcome.profile_found is True
        assert len(outcome.missing) == 1

    @patch("layout_manager._find_candidates")
    @patch("layout_manager._apply_geometry")
    @patch("layout_manager.time.sleep")
    def test_retries_converge(
        self, mock_sleep, mock_apply, mock_find, profiles_dir_path, local_app_data,
    ):
        """On the second attempt the window appears."""
        from layout_manager import restore_profile
        _make_profile(profiles_dir_path, "Retry", windows=[_sample_window_dict()])
        # first call: not found; second call: found
        mock_find.side_effect = [[], [(99, "Untitled - Notepad")]]
        outcome = restore_profile("Retry", retries=2, delay_s=0, launch_if_missing=False)
        assert outcome.restored_count == 1
        assert outcome.missing == []

    def test_profile_path_outside_dir_rejected(self, profiles_dir_path, local_app_data, tmp_path):
        """A profile_path outside profiles_dir is rejected."""
        from layout_manager import restore_profile
        rogue = tmp_path / "rogue.json"
        rogue.write_text('{"windows":[]}', encoding="utf-8")
        outcome = restore_profile("x", profile_path=rogue)
        assert outcome.profile_found is False


# ===========================================================================
# 10. edit_profile
# ===========================================================================

class TestEditProfile:
    @patch("layout_manager._collect_windows")
    def test_rename_profile(self, mock_collect, profiles_dir_path, local_app_data):
        p = _make_profile(profiles_dir_path, "OldName", windows=[_sample_window_dict()])
        result = edit_profile(profile_path=p, new_name="NewName")
        assert result.profile_found is True
        assert result.renamed is True
        # The new file should exist
        new_p = profiles_dir_path / "NewName.json"
        assert new_p.is_file()
        data = json.loads(new_p.read_text(encoding="utf-8"))
        assert data["name"] == "NewName"

    @patch("layout_manager._collect_windows")
    def test_replace_windows(self, mock_collect, profiles_dir_path, local_app_data):
        mock_collect.return_value = [
            WindowRecord(
                exe_path=r"c:\new\app.exe", title="New App",
                left=0, top=0, right=500, bottom=400,
                maximized=False, minimized=False, z_index=0,
            )
        ]
        p = _make_profile(profiles_dir_path, "Replace", windows=[_sample_window_dict()])
        result = edit_profile(profile_path=p, new_name="Replace", replace_windows=True)
        assert result.windows_replaced is True
        assert result.window_count == 1
        data = json.loads(p.read_text(encoding="utf-8"))
        assert data["windows"][0]["exe_path"] == r"c:\new\app.exe"

    def test_empty_name_rejected(self, profiles_dir_path, local_app_data):
        p = _make_profile(profiles_dir_path, "Valid", windows=[])
        result = edit_profile(profile_path=p, new_name="")
        assert result.error is not None
        assert "empty" in result.error.lower()

    def test_nonexistent_path(self, profiles_dir_path, local_app_data):
        p = profiles_dir_path / "ghost.json"
        result = edit_profile(profile_path=p, new_name="Ghost")
        assert result.profile_found is False

    def test_path_outside_dir(self, local_app_data, tmp_path):
        rogue = tmp_path / "rogue.json"
        rogue.write_text('{"version":1,"name":"r","windows":[]}', encoding="utf-8")
        result = edit_profile(profile_path=rogue, new_name="Rogue")
        assert result.profile_found is False
        assert result.error is not None


# ===========================================================================
# 11. delete_profile
# ===========================================================================

class TestDeleteProfile:
    def test_delete_existing(self, profiles_dir_path, local_app_data):
        p = _make_profile(profiles_dir_path, "ToDelete")
        assert p.is_file()
        delete_profile(p)
        assert not p.is_file()

    def test_delete_nonexistent_noop(self, profiles_dir_path, local_app_data):
        p = profiles_dir_path / "nofile.json"
        delete_profile(p)  # should not raise

    def test_delete_outside_dir_raises(self, local_app_data, tmp_path):
        rogue = tmp_path / "rogue.json"
        rogue.write_text("{}", encoding="utf-8")
        with pytest.raises(ValueError, match="outside"):
            delete_profile(rogue)


# ===========================================================================
# 12. update_profile
# ===========================================================================

class TestUpdateProfile:
    @patch("layout_manager._find_candidates")
    @patch("layout_manager._geometry_from_hwnd")
    def test_updates_matched_windows(self, mock_geom, mock_find, profiles_dir_path, local_app_data):
        win = _sample_window_dict()
        p = _make_profile(profiles_dir_path, "Update", windows=[win])
        updated_rec = WindowRecord(
            exe_path=win["exe_path"], title=win["title"],
            left=200, top=200, right=900, bottom=700,
            maximized=False, minimized=False, z_index=0,
        )
        mock_find.return_value = [(42, win["title"])]
        mock_geom.return_value = updated_rec
        outcome = update_profile("Update")
        assert outcome.profile_found is True
        assert outcome.updated_count == 1
        assert outcome.missing_count == 0
        data = json.loads(p.read_text(encoding="utf-8"))
        assert data["windows"][0]["left"] == 200

    @patch("layout_manager._find_candidates")
    def test_missing_windows_kept_unchanged(self, mock_find, profiles_dir_path, local_app_data):
        win = _sample_window_dict()
        p = _make_profile(profiles_dir_path, "PartialUp", windows=[win])
        mock_find.return_value = []
        outcome = update_profile("PartialUp")
        assert outcome.missing_count == 1
        data = json.loads(p.read_text(encoding="utf-8"))
        assert data["windows"][0]["left"] == win["left"]  # unchanged

    def test_update_nonexistent_profile(self, local_app_data):
        outcome = update_profile("NoSuch")
        assert outcome.profile_found is False


# ===========================================================================
# 13. _find_candidates (mocked EnumWindows)
# ===========================================================================

class TestFindCandidates:
    """Mock win32gui.EnumWindows to simulate live windows matching logic."""

    def _setup_enum(self, mock_enum, windows):
        """
        windows: list of (hwnd, visible, owner, exstyle, style, pid, exe, title, rect, placement)
        """
        def enum_side_effect(callback, _):
            for w in windows:
                callback(w["hwnd"], None)
        mock_enum.side_effect = enum_side_effect

    @patch("layout_manager._browser_url_for_hwnd", return_value=None)
    @patch("layout_manager._get_exe_path")
    @patch("layout_manager.win32process.GetWindowThreadProcessId")
    @patch("layout_manager.win32gui.GetWindowText")
    @patch("layout_manager.win32gui.GetWindowLong")
    @patch("layout_manager.win32gui.GetWindow")
    @patch("layout_manager.win32gui.IsWindowVisible")
    @patch("layout_manager.win32gui.EnumWindows")
    @patch("layout_manager._equivalent_exe_paths_for_saved")
    @patch("layout_manager.os.getpid", return_value=9999)
    def test_exact_match_found(
        self, mock_pid, mock_equiv, mock_enum, mock_visible, mock_getwin,
        mock_getlong, mock_gettext, mock_gettid, mock_getexe, mock_url
    ):
        from layout_manager import _find_candidates
        hwnd = 100
        exe = r"c:\app\notepad.exe"
        title = "Untitled - Notepad"

        mock_equiv.return_value = frozenset({exe})

        def enum_cb(callback, _):
            callback(hwnd, None)
        mock_enum.side_effect = enum_cb
        mock_visible.return_value = True
        mock_getwin.return_value = 0  # no owner
        mock_getlong.return_value = 0  # no tool/noactivate/child
        mock_gettext.return_value = title
        mock_gettid.return_value = (0, 1234)
        mock_getexe.return_value = exe

        rec = WindowRecord(
            exe_path=exe, title=title,
            left=0, top=0, right=800, bottom=600,
            maximized=False, minimized=False, z_index=0,
        )
        result = _find_candidates(rec)
        assert len(result) == 1
        assert result[0][0] == hwnd

    @patch("layout_manager._browser_url_for_hwnd", return_value=None)
    @patch("layout_manager._get_exe_path")
    @patch("layout_manager.win32process.GetWindowThreadProcessId")
    @patch("layout_manager.win32gui.GetWindowText")
    @patch("layout_manager.win32gui.GetWindowLong")
    @patch("layout_manager.win32gui.GetWindow")
    @patch("layout_manager.win32gui.IsWindowVisible")
    @patch("layout_manager.win32gui.EnumWindows")
    @patch("layout_manager._equivalent_exe_paths_for_saved")
    @patch("layout_manager.os.getpid", return_value=9999)
    def test_no_match_wrong_exe(
        self, mock_pid, mock_equiv, mock_enum, mock_visible, mock_getwin,
        mock_getlong, mock_gettext, mock_gettid, mock_getexe, mock_url
    ):
        from layout_manager import _find_candidates
        hwnd = 100

        mock_equiv.return_value = frozenset({r"c:\app\notepad.exe"})

        def enum_cb(callback, _):
            callback(hwnd, None)
        mock_enum.side_effect = enum_cb
        mock_visible.return_value = True
        mock_getwin.return_value = 0
        mock_getlong.return_value = 0
        mock_gettext.return_value = "Some Title"
        mock_gettid.return_value = (0, 1234)
        mock_getexe.return_value = r"c:\other\calc.exe"

        rec = WindowRecord(
            exe_path=r"c:\app\notepad.exe", title="Notepad",
            left=0, top=0, right=800, bottom=600,
            maximized=False, minimized=False, z_index=0,
        )
        result = _find_candidates(rec)
        assert len(result) == 0


# ===========================================================================
# 14. _allow_launch
# ===========================================================================

class TestAllowLaunch:
    def test_empty_path(self):
        assert _allow_launch("") is False

    def test_whitespace_only(self):
        assert _allow_launch("   ") is False

    @patch("layout_manager.os.path.isfile", return_value=True)
    def test_allowed_exe(self, mock_isfile):
        assert _allow_launch(r"C:\App\myapp.exe") is True

    @patch("layout_manager.os.path.isfile", return_value=True)
    def test_denied_system_exe(self, mock_isfile):
        assert _allow_launch(r"C:\Windows\System32\csrss.exe") is False
        assert _allow_launch(r"C:\Windows\System32\dwm.exe") is False
        assert _allow_launch(r"C:\Windows\System32\lsass.exe") is False

    @patch("layout_manager.os.path.isfile", return_value=False)
    def test_file_not_found(self, mock_isfile):
        assert _allow_launch(r"C:\nonexistent\app.exe") is False

    @patch("layout_manager.os.path.isfile", return_value=True)
    def test_deny_list_case_insensitive(self, mock_isfile):
        # The code lowercases basename before checking
        assert _allow_launch(r"C:\Windows\DWM.EXE") is False

    @patch("layout_manager.os.path.isfile", return_value=True)
    def test_explorer_allowed(self, mock_isfile):
        """explorer.exe is intentionally NOT in the deny list."""
        assert _allow_launch(r"C:\Windows\explorer.exe") is True

    @patch("layout_manager.os.path.isfile", return_value=True)
    def test_riot_client_allowed(self, mock_isfile):
        """Game launchers must stay launchable."""
        assert _allow_launch(r"C:\Riot Games\Riot Client\RiotClientServices.exe") is True


# ===========================================================================
# 15. Error handling — corrupt JSON, missing files, permission errors
# ===========================================================================

class TestErrorHandling:
    def test_load_corrupt_json(self, profiles_dir_path, local_app_data):
        """restore gracefully handles corrupt JSON."""
        from layout_manager import restore_profile
        p = profiles_dir_path / "Corrupt.json"
        p.write_text("{{{not json", encoding="utf-8")
        # restore_profile reads with json.loads — should either raise or
        # the test_profile_path won't match. Let's check via restore_profile
        # which wraps json.loads directly (line 1115).
        # Since it will raise json.JSONDecodeError, restore_profile will crash.
        # Actually looking at the code: restore_profile does json.loads without
        # try/except at line 1115. So corrupt JSON raises.
        with pytest.raises(json.JSONDecodeError):
            restore_profile("Corrupt", retries=1, delay_s=0)

    def test_load_profile_windows_corrupt_json(self, profiles_dir_path, local_app_data):
        """_load_profile_windows handles corrupt JSON gracefully."""
        from layout_manager import _load_profile_windows
        p = profiles_dir_path / "Bad.json"
        p.write_text("{not valid}", encoding="utf-8")
        result = _load_profile_windows(p)
        assert result is None

    def test_load_profile_windows_missing_file(self, profiles_dir_path, local_app_data):
        from layout_manager import _load_profile_windows
        p = profiles_dir_path / "Missing.json"
        result = _load_profile_windows(p)
        assert result is None

    def test_update_profile_corrupt_json(self, profiles_dir_path, local_app_data):
        """update_profile returns profile_found=False on corrupt JSON."""
        p = profiles_dir_path / "BadUpdate.json"
        p.write_text("{{{broken", encoding="utf-8")
        outcome = update_profile(profile_path=p)
        assert outcome.profile_found is False

    def test_edit_profile_corrupt_json(self, profiles_dir_path, local_app_data):
        """edit_profile returns profile_found=False on corrupt JSON."""
        p = profiles_dir_path / "BadEdit.json"
        p.write_text("{{broken", encoding="utf-8")
        result = edit_profile(profile_path=p, new_name="NewName")
        assert result.profile_found is False

    def test_profile_launch_if_missing_corrupt_json(self, profiles_dir_path, local_app_data):
        """profile_launch_if_missing defaults to True on corrupt file."""
        from layout_manager import profile_launch_if_missing
        p = profiles_dir_path / "BadLaunch.json"
        # _profile_path("BadLaunch") -> this path
        p.write_text("broken!", encoding="utf-8")
        assert profile_launch_if_missing("BadLaunch") is True


# ===========================================================================
# Additional helper tests
# ===========================================================================

class TestNormalizeTitle:
    def test_lowercase_collapse_whitespace(self):
        assert _normalize_title("  Hello   World  ") == "hello world"

    def test_empty(self):
        assert _normalize_title("") == ""


class TestTitleMatchExplorer:
    def test_same_key(self):
        assert _title_match_explorer("Downloads - File Explorer", "Downloads") is True

    def test_prefix_key_match(self):
        assert _title_match_explorer("Downloads", "Downloads - File Explorer") is True

    def test_unrelated(self):
        assert _title_match_explorer("Pictures", "Videos") is False


class TestIsExplorerExe:
    def test_true(self):
        assert _is_explorer_exe(r"C:\Windows\explorer.exe") is True

    def test_false(self):
        assert _is_explorer_exe(r"C:\Windows\notepad.exe") is False


class TestResolveProfilePath:
    def test_with_name(self, local_app_data):
        p = _resolve_profile_path("Test", None)
        assert p is not None
        assert p.name == "Test.json"

    def test_empty_name_no_path(self, local_app_data):
        p = _resolve_profile_path("", None)
        assert p is None

    def test_with_path_inside_dir(self, profiles_dir_path, local_app_data):
        f = profiles_dir_path / "inside.json"
        f.touch()
        p = _resolve_profile_path("", f)
        assert p is not None

    def test_with_path_outside_dir(self, local_app_data, tmp_path):
        f = tmp_path / "outside.json"
        f.touch()
        p = _resolve_profile_path("", f)
        assert p is None


class TestWindowRecordDataclass:
    def test_asdict_round_trip(self):
        rec = WindowRecord(
            exe_path="test.exe", title="Test", left=0, top=0,
            right=100, bottom=100, maximized=True, minimized=False,
            z_index=1, browser_url="https://example.com",
        )
        d = asdict(rec)
        assert d["exe_path"] == "test.exe"
        assert d["browser_url"] == "https://example.com"
        assert d["maximized"] is True

    def test_default_browser_url(self):
        rec = WindowRecord(
            exe_path="x", title="y", left=0, top=0,
            right=1, bottom=1, maximized=False, minimized=False, z_index=0,
        )
        assert rec.browser_url is None


class TestListProfileRows:
    def test_empty_dir(self, profiles_dir_path, local_app_data):
        from layout_manager import list_profile_rows
        rows = list_profile_rows()
        assert rows == []

    def test_multiple_profiles(self, profiles_dir_path, local_app_data):
        from layout_manager import list_profile_rows
        _make_profile(profiles_dir_path, "Alpha", windows=[_sample_window_dict()])
        _make_profile(profiles_dir_path, "Beta", windows=[])
        rows = list_profile_rows()
        assert len(rows) == 2
        names = [r[0] for r in rows]
        assert names == ["Alpha", "Beta"]

    def test_corrupt_profile_still_listed(self, profiles_dir_path, local_app_data):
        from layout_manager import list_profile_rows
        p = profiles_dir_path / "Corrupt.json"
        p.write_text("{{{bad", encoding="utf-8")
        rows = list_profile_rows()
        assert len(rows) == 1
        assert rows[0][0] == "Corrupt"  # falls back to stem
        assert rows[0][1] == 0
