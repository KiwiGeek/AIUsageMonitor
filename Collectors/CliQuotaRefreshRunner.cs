using System.Diagnostics;
using System.IO;

namespace AIUsageMonitor.Collectors;

internal static class CliQuotaRefreshRunner
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(45);

    public static Task<CliQuotaRefreshResult> RefreshCodexAsync(CancellationToken cancellationToken)
    {
        var workingDirectory = EnsureWorkingDirectory("codex");
        var args = new[]
        {
            "--disable",
            "plugins",
            "--disable",
            "apps",
            "--disable",
            "browser_use",
            "--disable",
            "browser_use_external",
            "--disable",
            "computer_use",
            "--disable",
            "image_generation",
            "--disable",
            "multi_agent",
            "--disable",
            "tool_search",
            "--disable",
            "workspace_dependencies",
            "--disable",
            "shell_snapshot",
            "--disable",
            "shell_tool",
            "--disable",
            "hooks",
            "--disable",
            "personality",
            "-c",
            "model=\"gpt-5.4-mini\"",
            "-c",
            "model_reasoning_effort=\"low\"",
            "--ask-for-approval",
            "never",
            "exec",
            "--ignore-user-config",
            "--skip-git-repo-check",
            "--sandbox",
            "read-only",
            "--cd",
            workingDirectory,
            "--ignore-rules",
            "--color",
            "never",
            "Reply exactly OK. Do not inspect files or run commands."
        };

        return RunAsync("codex", args, workingDirectory, cancellationToken);
    }

    public static Task<CliQuotaRefreshResult> RefreshClaudeAsync(CancellationToken cancellationToken)
    {
        var workingDirectory = EnsureWorkingDirectory("claude");
        var args = new[]
        {
            "--system-prompt",
            "Reply exactly OK.",
            "--tools",
            string.Empty,
            "--output-format",
            "json",
            "--permission-mode",
            "dontAsk",
            "--print",
            "OK"
        };

        return RunAsync("claude", args, workingDirectory, cancellationToken);
    }

    private static async Task<CliQuotaRefreshResult> RunAsync(
        string command,
        IReadOnlyList<string> args,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var commandPath = FindCommandPath(command);
        if (string.IsNullOrWhiteSpace(commandPath))
        {
            return CliQuotaRefreshResult.Missing($"{command} command was not found on PATH.");
        }

        var startInfo = BuildStartInfo(commandPath, args, workingDirectory);
        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return CliQuotaRefreshResult.Failed($"Could not start {command}: {ex.Message}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CommandTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return CliQuotaRefreshResult.Failed($"{command} quota refresh timed out after {CommandTimeout.TotalSeconds:0} seconds.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = SummarizeOutput(stdout, stderr);

        if (process.ExitCode == 0)
        {
            return CliQuotaRefreshResult.Success();
        }

        var message = string.IsNullOrWhiteSpace(output)
            ? $"{command} quota refresh failed with exit code {process.ExitCode}."
            : $"{command} quota refresh failed with exit code {process.ExitCode}: {output}";

        return LooksQuotaExhausted(output)
            ? CliQuotaRefreshResult.Exhausted(message)
            : CliQuotaRefreshResult.Failed(message);
    }

    private static ProcessStartInfo BuildStartInfo(
        string commandPath,
        IReadOnlyList<string> args,
        string workingDirectory)
    {
        var extension = Path.GetExtension(commandPath);
        var fileName = commandPath;
        var processArgs = new List<string>();

        if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            fileName = FindCommandPath("pwsh") ?? FindCommandPath("powershell") ?? "powershell.exe";
            processArgs.Add("-NoProfile");
            processArgs.Add("-ExecutionPolicy");
            processArgs.Add("Bypass");
            processArgs.Add("-File");
            processArgs.Add(commandPath);
        }

        processArgs.AddRange(args);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["TERM"] = "dumb";

        foreach (var arg in processArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    private static string? FindCommandPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", ".ps1", string.Empty }
            : new[] { string.Empty };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, command + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string EnsureWorkingDirectory(string provider)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AIUsageMonitor", provider);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string SummarizeOutput(string stdout, string stderr)
    {
        var text = string.Join(
            Environment.NewLine,
            new[] { stdout, stderr }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

        if (text.Length <= 500)
        {
            return text;
        }

        return text[..500] + "...";
    }

    private static bool LooksQuotaExhausted(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var lower = output.ToLowerInvariant();
        return lower.Contains("429", StringComparison.Ordinal) ||
            lower.Contains("too many requests", StringComparison.Ordinal) ||
            lower.Contains("rate limit", StringComparison.Ordinal) ||
            lower.Contains("usage limit", StringComparison.Ordinal) ||
            lower.Contains("limit reached", StringComparison.Ordinal) ||
            lower.Contains("maximum usage", StringComparison.Ordinal) ||
            lower.Contains("quota", StringComparison.Ordinal) ||
            lower.Contains("exhausted", StringComparison.Ordinal);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal sealed record CliQuotaRefreshResult(
    bool CommandFound,
    bool Succeeded,
    bool IsQuotaExhausted,
    string Message)
{
    public static CliQuotaRefreshResult Success() => new(true, true, false, string.Empty);

    public static CliQuotaRefreshResult Missing(string message) => new(false, false, false, message);

    public static CliQuotaRefreshResult Failed(string message) => new(true, false, false, message);

    public static CliQuotaRefreshResult Exhausted(string message) => new(true, false, true, message);
}
