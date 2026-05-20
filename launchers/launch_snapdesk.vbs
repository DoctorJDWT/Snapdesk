' Launch Snapdesk WinUI without a console window when possible.
Option Explicit

Dim fso, repoRoot, exePath, exeLegacy, outDir, runCmd

Set fso = CreateObject("Scripting.FileSystemObject")
repoRoot = fso.GetParentFolderName(WScript.ScriptFullName)
repoRoot = fso.GetParentFolderName(repoRoot)

outDir = fso.BuildPath(repoRoot, "LayoutProfiles.WinUI\bin\x64\Release\net8.0-windows10.0.19041.0")
exePath = fso.BuildPath(outDir, "Snapdesk.exe")
exeLegacy = fso.BuildPath(outDir, "LayoutProfiles.WinUI.exe")
runCmd = fso.BuildPath(repoRoot, "launchers\run_winui.cmd")

If fso.FileExists(exePath) Then
    CreateObject("Shell.Application").ShellExecute exePath, "", outDir, "open", 1
ElseIf fso.FileExists(exeLegacy) Then
    CreateObject("Shell.Application").ShellExecute exeLegacy, "", outDir, "open", 1
ElseIf fso.FileExists(runCmd) Then
    CreateObject("Shell.Application").ShellExecute runCmd, "", repoRoot, "open", 0
Else
    CreateObject("WScript.Shell").Popup "Snapdesk launcher not found." & vbCrLf & runCmd, 0, "Snapdesk", 16
    WScript.Quit 1
End If
