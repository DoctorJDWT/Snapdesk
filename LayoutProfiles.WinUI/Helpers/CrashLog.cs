using System.Reflection;
using System.Text;
using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Xaml.Markup;

namespace LayoutProfiles.WinUI.Helpers;

internal static class CrashLog
{
    private static readonly string LastErrorPath = Path.Combine(
        RepoPaths.LayoutProfilesDataDir,
        "snapdesk-last-error.txt");

    private static readonly string XamlDebugPath = Path.Combine(
        RepoPaths.LayoutProfilesDataDir,
        "snapdesk-xaml-debug.log");

    public static void Write(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(RepoPaths.LayoutProfilesDataDir);
            var text = FormatEntry(context, ex);
            File.WriteAllText(LastErrorPath, text);
            File.AppendAllText(XamlDebugPath, text);
        }
        catch
        {
            // ignore logging failures
        }
    }

    public static void WriteDiagnostic(string context, string detail)
    {
        try
        {
            Directory.CreateDirectory(RepoPaths.LayoutProfilesDataDir);
            var text =
                $"[{DateTimeOffset.Now:u}] {context}{Environment.NewLine}" +
                detail + Environment.NewLine +
                FormatAssemblyIdentity() + Environment.NewLine;
            File.AppendAllText(XamlDebugPath, text);
        }
        catch
        {
            // ignore
        }
    }

    public static string FormatEntry(string context, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTimeOffset.Now:u}] {context}");
        sb.Append(FormatAssemblyIdentity());
        sb.AppendLine();
        sb.Append(FormatExceptionChain(ex));
        return sb.ToString();
    }

    public static string FormatExceptionChain(Exception ex)
    {
        var sb = new StringBuilder();
        var depth = 0;
        for (var current = ex; current is not null; current = current.InnerException, depth++)
        {
            if (depth > 0)
            {
                sb.AppendLine($"--- InnerException #{depth} ({current.GetType().FullName}) ---");
            }

            sb.AppendLine($"{current.GetType().FullName}: {current.Message}");
            sb.AppendLine($"HRESULT: 0x{current.HResult:X8} ({current.HResult})");
            AppendXamlParseDetails(sb, current);
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                sb.AppendLine(current.StackTrace);
            }

            if (current is AggregateException agg)
            {
                var i = 0;
                foreach (var inner in agg.InnerExceptions)
                {
                    sb.AppendLine($"--- Aggregate inner #{i++} ---");
                    sb.Append(FormatExceptionChain(inner));
                }
            }
        }

        return sb.ToString();
    }

    private static void AppendXamlParseDetails(StringBuilder sb, Exception ex)
    {
        if (ex is not XamlParseException)
        {
            return;
        }

        TryAppendProperty(sb, ex, "LineNumber");
        TryAppendProperty(sb, ex, "LinePosition");
        TryAppendProperty(sb, ex, "XamlSource");
        TryAppendProperty(sb, ex, "XamlFileName");
        TryAppendProperty(sb, ex, "XamlBinaryFileOffset");
        TryAppendProperty(sb, ex, "XamlTypeName");
        TryAppendProperty(sb, ex, "XamlMemberName");
    }

    private static void TryAppendProperty(StringBuilder sb, object target, string propertyName)
    {
        try
        {
            var prop = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop is null)
            {
                return;
            }

            var value = prop.GetValue(target);
            if (value is null)
            {
                return;
            }

            sb.AppendLine($"{propertyName}: {value}");
        }
        catch
        {
            // ignore reflection failures
        }
    }

    public static string FormatAssemblyIdentity()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetName();
            var exe = Environment.ProcessPath ?? "(unknown)";
            var fileVersion = "(unknown)";
            try
            {
                if (!string.IsNullOrEmpty(Environment.ProcessPath))
                {
                    fileVersion = System.Diagnostics.FileVersionInfo
                        .GetVersionInfo(Environment.ProcessPath)
                        .FileVersion ?? "(unknown)";
                }
            }
            catch
            {
                // ignore
            }

            DateTimeOffset? buildUtc = null;
            try
            {
                if (!string.IsNullOrEmpty(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
                {
                    buildUtc = File.GetLastWriteTimeUtc(Environment.ProcessPath);
                }
            }
            catch
            {
                // ignore
            }

            return
                $"Assembly: {name.Name} v{name.Version}{Environment.NewLine}" +
                $"FileVersion: {fileVersion}{Environment.NewLine}" +
                $"Exe: {exe}{Environment.NewLine}" +
                $"BuildUtc: {(buildUtc.HasValue ? buildUtc.Value.ToString("u") : "(unknown)")}{Environment.NewLine}";
        }
        catch (Exception ex)
        {
            return $"Assembly identity unavailable: {ex.Message}{Environment.NewLine}";
        }
    }
}
