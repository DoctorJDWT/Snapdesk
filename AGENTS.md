# Snapdesk agent guide

WinUI 3 desktop shell (`LayoutProfiles.WinUI/`) plus a Python bridge (`src/layout_manager.py`) for saving and restoring window layouts on Windows.

## Key paths

| Path | Purpose |
|------|---------|
| `LayoutProfiles.WinUI/` | WinUI 3 app (`Snapdesk.exe`) |
| `src/layout_manager.py` | Python CLI / layout engine |
| `invoke_python.ps1` | App-root marker; launches Python scripts |
| `launchers/` | Dev build/run wrappers |
| `scripts/` | Publish, installer, asset scripts |
| `dist/Snapdesk/` | Published install payload |
| `%LOCALAPPDATA%/LayoutProfiles/` | User profiles (`profiles/`) and `settings.json` |
| `%LOCALAPPDATA%/Programs/Snapdesk/` | Installed app location |

## Build and run

```powershell
dotnet build LayoutProfiles.WinUI/LayoutProfiles.WinUI.csproj -c Release -p:Platform=x64
```

In Cursor: **Ctrl+Shift+B** builds; **F5** / task **run-snapdesk-release** launches the dev build. See `.vscode/tasks.json` and `.vscode/launch.json`.

## PythonBridge contract

`LayoutProfiles.WinUI/Services/PythonBridge.cs` invokes `invoke_python.ps1` with `src/layout_manager.py` args. Keep subcommands aligned:

| Subcommand | Args |
|------------|------|
| `save` | `save NAME [--window EXE\|TITLE]...` |
| `restore` | `restore --profile-path PATH` |
| `update` | `update --profile-path PATH` |
| `edit` | `edit --profile-path PATH NEWNAME [--window EXE\|TITLE]...` |
| `list-windows` | `list-windows` (JSON array on stdout) |

After changing either side, run `pytest`.

## Dev commands

```powershell
pip install -r requirements.txt -r requirements-dev.txt
pytest
ruff check src tests
```

Config: `pyproject.toml` (pytest + ruff).

## Release

Follow `.cursor/rules/release-after-changes.mdc`: bump patch tag, `scripts\Build-Installer.ps1`, publish GitHub release with `dist\Snapdesk-Setup.zip`.

## Notes

- Tests assume Windows (win32 imports at module load).
- GitHub MCP is optional in Cursor settings for release/issue automation.
