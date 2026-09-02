using System.Diagnostics;
using Spectre.Console;

namespace Modulus.Cli.Services;

/// <summary>
/// Central UX surface for the CLI: TTY-aware interactive prompts,
/// coloured output, dry-run-aware filesystem writes, and a status
/// spinner wrapper for long-running operations.
///
/// <para><b>Backward-compatibility contract.</b> Every existing CLI
/// invocation keeps working unchanged: when stdin is redirected (CI,
/// scripts, pipes) no prompt is ever shown — the call falls back to the
/// supplied default or throws a clear "missing required argument" error.
/// Interactive prompts fire only on a real terminal.</para>
/// </summary>
internal static class Ux
{
    // ── Global flags (set by each command's Execute from its settings) ──
    public static bool DryRun { get; set; }
    public static bool Force { get; set; }
    public static bool Verbose { get; set; }
    public static bool Quiet { get; set; }

    /// <summary>
    /// Resets all global flags. Each command sets its own at start; this
    /// keeps state from leaking between invocations in the same process
    /// (the test harness, future REPL, etc.).
    /// </summary>
    public static void Reset()
    {
        DryRun = false;
        Force = false;
        Verbose = false;
        Quiet = false;
    }

    /// <summary>
    /// True when stdin is attached to a real terminal (we may prompt).
    /// False when stdin is a pipe/redirect (CI, scripts) — must not block.
    /// </summary>
    public static bool IsInteractive => !Console.IsInputRedirected;

    // ── Output ────────────────────────────────────────────────────────
    public static void Info(string message, string? detail = null)
    {
        if (Quiet) return;
        if (detail is null)
            AnsiConsole.MarkupLine("[grey]>[/] {0}", message);
        else
            AnsiConsole.MarkupLine("[grey]>[/] {0} [grey]{1}[/]", message, detail);
    }

    public static void Success(string message, string? detail = null)
    {
        if (Quiet) return;
        if (detail is null)
            AnsiConsole.MarkupLine("[green]✓[/] {0}", message);
        else
            AnsiConsole.MarkupLine("[green]✓[/] {0} [grey]{1}[/]", message, detail);
    }

    public static void Warning(string message)
    {
        if (Quiet) return;
        AnsiConsole.MarkupLine("[yellow]![/] {0}", message);
    }

    public static void Error(string message)
        => AnsiConsole.MarkupLine("[red]✗[/] {0}", message);

    public static void Detail(string message)
    {
        if (Quiet || !Verbose) return;
        AnsiConsole.MarkupLine("[grey]  {0}[/]", message);
    }

    public static void DryRunNote(string message)
    {
        if (!DryRun) return;
        AnsiConsole.MarkupLine("[blue]DRY-RUN[/] [grey]{0}[/]", message);
    }

    // ── Interactive prompts (TTY-aware) ───────────────────────────────

    /// <summary>
    /// Prompts for a string when interactive; otherwise returns
    /// <paramref name="fallback"/>. Use for optional args the user may
    /// prefer to type at a prompt rather than on the command line.
    /// </summary>
    public static string AskOrFallback(string prompt, string fallback, string? defaultValue = null)
    {
        if (!IsInteractive) return fallback;
        var text = new TextPrompt<string>(prompt)
            .DefaultValue(defaultValue ?? fallback)
            .AllowEmpty();
        return AnsiConsole.Prompt(text).Trim();
    }

    /// <summary>
    /// Prompts for a required string when interactive; throws
    /// <see cref="InvalidOperationException"/> otherwise (so the command
    /// surfaces a clean error via <see cref="CommandRunner"/>).
    /// </summary>
    public static string AskRequired(string prompt, string? defaultValue = null, string? ciHint = null)
    {
        if (!IsInteractive)
            throw new InvalidOperationException(
                "Missing required argument and stdin is not interactive (CI mode). " +
                (ciHint ?? "Pass the argument on the command line."));
        var text = new TextPrompt<string>(prompt);
        if (defaultValue is not null)
            text.DefaultValue(defaultValue);
        text.Validate(v => !string.IsNullOrWhiteSpace(v)
            ? ValidationResult.Success()
            : ValidationResult.Error("[red]Value cannot be empty.[/]"));
        return AnsiConsole.Prompt(text).Trim();
    }

    /// <summary>
    /// Shows a single-choice selection list when interactive; otherwise
    /// returns <paramref name="fallback"/>.
    /// </summary>
    public static string SelectOrFallback(string prompt, IEnumerable<string> choices, string fallback)
    {
        var list = choices.ToList();
        if (!IsInteractive) return fallback;
        var promptObj = new SelectionPrompt<string>()
            .Title(prompt)
            .PageSize(10)
            .AddChoices(list)
            .HighlightStyle(Style.Parse("cyan"));
        var picked = AnsiConsole.Prompt(promptObj);
        return picked;
    }

    /// <summary>
    /// Yes/no confirmation when interactive; otherwise returns
    /// <paramref name="nonInteractiveDefault"/> (so <c>--force</c>
    /// short-circuits confirmations on CI).
    /// </summary>
    public static bool Confirm(string prompt, bool nonInteractiveDefault = true)
    {
        if (Force) return true;
        if (!IsInteractive) return nonInteractiveDefault;
        return AnsiConsole.Confirm(prompt, defaultValue: nonInteractiveDefault);
    }

    // ── Spinner / status ──────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="action"/> under a status spinner with the
    /// given label. Output from the action via <see cref="Info"/>/
    /// <see cref="Detail"/> still appears (the status is suppressed while
    /// they write).
    /// </summary>
    public static T Status<T>(string label, Func<T> action)
    {
        if (Quiet || !IsInteractive)
            return action();
        return AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .Start(label, _ => action());
    }

    public static void Status(string label, Action action)
        => Status<object?>(label, () => { action(); return null; });

    /// <summary>
    /// Runs an async <paramref name="action"/> under a status spinner with the
    /// given label. Use this for async operations like network calls.
    /// </summary>
    public static async Task<T> StatusAsync<T>(string label, Func<Task<T>> action)
    {
        if (Quiet || !IsInteractive)
            return await action();
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync(label, _ => action());
    }

    // ── Filesystem (dry-run aware) ────────────────────────────────────

    /// <summary>
    /// Writes a file, creating parent directories as needed. Under
    /// <c>--dry-run</c> nothing is written; the path is reported instead.
    /// </summary>
    public static void WriteFile(string path, string content)
    {
        if (DryRun)
        {
            DryRunNote($"would write [cyan]{path}[/]");
            return;
        }
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Creates a directory. No-op under <c>--dry-run</c> (the writes
    /// that would populate it report themselves).
    /// </summary>
    public static void CreateDirectory(string path)
    {
        if (DryRun)
        {
            DryRunNote($"would create directory [cyan]{path}[/]");
            return;
        }
        Directory.CreateDirectory(path);
    }

    /// <summary>
    /// Deletes a directory recursively. No-op under <c>--dry-run</c>.
    /// </summary>
    public static void DeleteDirectory(string path)
    {
        if (DryRun)
        {
            DryRunNote($"would delete [cyan]{path}[/]");
            return;
        }
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    // ── Process runner (captured/streamed output) ─────────────────────

    /// <summary>
    /// Runs an external process, returning its exit code. Under
    /// <c>--dry-run</c> the command is echoed and 0 is returned without
    /// launching it. With <c>--verbose</c> output is streamed live;
    /// otherwise stdout is suppressed and shown only on failure (stderr
    /// always goes to the console).
    /// </summary>
    public static int RunProcess(
        string fileName,
        string args,
        string workingDir,
        string dryRunLabel)
    {
        if (DryRun)
        {
            DryRunNote($"would run [cyan]{fileName} {args}[/]");
            return 0;
        }

        var psi = new ProcessStartInfo(fileName, args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var stdout = new List<string>();
        var stderr = new List<string>();

        using var proc = new Process { StartInfo = psi };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (Verbose && !Quiet)
                AnsiConsole.MarkupLine("[grey]{0}[/]", Markup.Escape(e.Data));
            else
                stdout.Add(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.Add(e.Data);
            if (!Quiet)
                AnsiConsole.MarkupLine("[red]{0}[/]", Markup.Escape(e.Data));
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        if (proc.ExitCode != 0 && !Verbose && !Quiet)
        {
            // Replay captured stdout on failure so the user sees the real error.
            foreach (var line in stdout)
                AnsiConsole.MarkupLine("[grey]{0}[/]", Markup.Escape(line));
        }

        return proc.ExitCode;
    }
}
