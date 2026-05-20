# Snapdesk (layout-profiles)

Save and restore window layouts across monitors on Windows.

## Install (no command line)

1. Get **`Snapdesk-Setup.zip`** (from a release) or build it locally (see below).
2. Extract the zip and double-click **`Install Snapdesk.cmd`**.
3. Approve any **winget** prompts if Windows asks (Python 3.12, optional Windows App Runtime).
4. Use the **Snapdesk** desktop shortcut.

**Install location:** `%LOCALAPPDATA%\Programs\Snapdesk\`

From a git checkout with a prior publish, you can also double-click **`Install Snapdesk.cmd`** at the repo root (uses `dist\Snapdesk\` when present).

Optional startup entry when installing from PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Install-Snapdesk.ps1 -AddToStartup
```

Profiles and settings live under `%LOCALAPPDATA%\LayoutProfiles\` (not in the install folder).

## Quick start (developers — use this if you have the project folder)

You do **not** need `Install Snapdesk.cmd` while developing. That installer is for copying a **published** `dist\Snapdesk\` to `%LOCALAPPDATA%\Programs\Snapdesk\`.

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download) and Python 3 with `pip install -r requirements.txt`.
2. **Run from Cursor (recommended):** open this folder as the workspace, choose **Snapdesk (Run app)** in the Run and Debug dropdown, then **F5** or **Ctrl+F5** (builds Release x64 and launches dev `Snapdesk.exe`). **Ctrl+Shift+B** builds only; **Ctrl+Shift+P** → **Tasks: Run Task** → **run-snapdesk-release** builds and starts without the debugger.
3. Or double-click **`Open Snapdesk.cmd`**, or in a terminal: `launchers\run_winui.cmd`. Do **not** use the desktop shortcut while developing — that points at `%LOCALAPPDATA%\Programs\Snapdesk\` from a prior install and will not pick up your latest build.
4. Optional desktop shortcut: `install_desktop_shortcut.cmd` (or `launchers\install_desktop_shortcut.cmd`).

To test the full installer locally, run `scripts\publish_snapdesk.ps1` first until it prints **`Published executable: Snapdesk.exe`**, then `Install Snapdesk.cmd`.

## Layout

| Path | Purpose |
|------|---------|
| `LayoutProfiles.WinUI/` | WinUI 3 desktop app |
| `src/` | Python core (`layout_manager.py`, `app_assetgen.py`) |
| `launchers/` | Build, run, layout CLI, shortcuts, silent `launch_snapdesk.vbs` |
| `scripts/` | PowerShell helpers, publish/installer, asset build |
| `assets/` | Icons (`.ico`, `.ppm`) |
| `invoke_python.ps1` | Finds Python and runs scripts (app root marker for `RepoPaths`) |
| `Install Snapdesk.cmd` | End-user installer (double-click) |
| `dist/Snapdesk/` | Published app output (`publish_snapdesk.ps1`) |

Root keeps **`Open Snapdesk.cmd`**, **`Install Snapdesk.cmd`**, **`layout.cmd`**, and **`install_desktop_shortcut.cmd`**. Everything else lives under `launchers\`.

## CLI

`layout.cmd` runs `src\layout_manager.py` (save / restore / list profiles from the terminal).

## Build

Major changes should go through the **snapdesk-sr-dev** Cursor skill (senior dev audit: paths, build, Python bridge, minimal fixes).

- **WinUI (dev):** `launchers\build_winui.cmd` or `scripts\build_winui.ps1` (writes `build.log` on failure).
- **Publish / installer (maintainer):**
  - `scripts\publish_snapdesk.ps1` — self-contained `dist\Snapdesk\`
  - `scripts\Build-Installer.ps1` — publish + `dist\Snapdesk-Setup.zip`
- **Icons:** `python scripts\build_app_assets.py`
