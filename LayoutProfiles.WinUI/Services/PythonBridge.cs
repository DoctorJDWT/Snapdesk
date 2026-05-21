using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LayoutProfiles.WinUI.Models;

namespace LayoutProfiles.WinUI.Services;

public sealed class PythonBridge
{
    public async Task<PythonRunResult> RunLayoutManagerAsync(
        IReadOnlyList<string> scriptArgs,
        CancellationToken cancellationToken = default)
    {
        var repo = RepoPaths.RepoRoot;
        var ps1 = Path.Combine(repo, "invoke_python.ps1");
        if (!File.Exists(ps1))
        {
            return new PythonRunResult(
                -1,
                "",
                $"invoke_python.ps1 not found under {repo}");
        }

        var scriptRel = File.Exists(Path.Combine(repo, "src", "layout_manager.py"))
            ? Path.Combine("src", "layout_manager.py")
            : "layout_manager.py";

        var argList = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            ps1,
            scriptRel,
        };
        argList.AddRange(scriptArgs);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = repo,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in argList)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            return new PythonRunResult(-1, "", "Failed to start powershell.exe");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw;
        }

        return new PythonRunResult(process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    public Task<PythonRunResult> RestoreProfileAsync(string profilePath, CancellationToken ct = default) =>
        RunLayoutManagerAsync(
            new[]
            {
                "restore",
                "--profile-path",
                profilePath,
            },
            ct);

    public Task<PythonRunResult> SaveProfileAsync(
        string name,
        IReadOnlyList<PickableWindow>? windows = null,
        CancellationToken ct = default)
    {
        var args = new List<string> { "save", name };
        AppendWindowArgs(args, windows);
        return RunLayoutManagerAsync(args, ct);
    }

    public async Task<IReadOnlyList<PickableWindow>> ListPickableWindowsAsync(CancellationToken ct = default)
    {
        var result = await RunLayoutManagerAsync(new[] { "list-windows" }, ct);
        if (!result.Success)
        {
            var err = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(err)
                    ? $"list-windows failed (exit {result.ExitCode})"
                    : err.Trim());
        }

        var json = ExtractJsonPayload(result.StdOut);
        if (string.IsNullOrWhiteSpace(json))
        {
            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                throw new InvalidOperationException(result.StdErr.Trim());
            }

            return Array.Empty<PickableWindow>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("list-windows did not return a JSON array.");
            }

            var list = new List<PickableWindow>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("exe_path", out var exeEl) || exeEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!el.TryGetProperty("title", out var titleEl) || titleEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var exe = exeEl.GetString() ?? string.Empty;
                var title = titleEl.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var browserUrl = el.TryGetProperty("browser_url", out var urlEl)
                    && urlEl.ValueKind == JsonValueKind.String
                    ? urlEl.GetString()
                    : null;
                list.Add(new PickableWindow(exe, title, browserUrl));
            }

            return list;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Could not parse list-windows output: {ex.Message}", ex);
        }
    }

    /// <summary>Strip invoke_python.ps1 banner lines; return the JSON array/object payload.</summary>
    private static string? ExtractJsonPayload(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        var trimmed = stdout.Trim();
        if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('[');
        if (start < 0)
        {
            return null;
        }

        var end = trimmed.LastIndexOf(']');
        if (end <= start)
        {
            return null;
        }

        return trimmed[start..(end + 1)];
    }

    public Task<PythonRunResult> UpdateProfileAsync(string profilePath, CancellationToken ct = default) =>
        RunLayoutManagerAsync(
            new[]
            {
                "update",
                "--profile-path",
                profilePath,
            },
            ct);

    public Task<PythonRunResult> EditProfileAsync(
        string profilePath,
        string newName,
        IReadOnlyList<PickableWindow>? windows = null,
        CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "edit",
            "--profile-path",
            profilePath,
            newName,
        };
        AppendWindowArgs(args, windows);
        return RunLayoutManagerAsync(args, ct);
    }

    private static void AppendWindowArgs(List<string> args, IReadOnlyList<PickableWindow>? windows)
    {
        if (windows is null || windows.Count == 0)
        {
            return;
        }

        foreach (var w in windows)
        {
            args.Add("--window");
            args.Add($"{w.ExePath}|{w.Title}");
        }
    }
}

public sealed record PythonRunResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}
