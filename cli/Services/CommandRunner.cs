using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace Modulus.Cli.Services;

/// <summary>
/// Executes a command body with consistent error handling: user-facing
/// validation/lookup failures are printed as red errors and return exit
/// code 1 instead of an unhandled stack trace.
/// </summary>
internal static class CommandRunner
{
    public static int Run(Func<int> action)
    {
        try
        {
            return Report(action(), null);
        }
        catch (Exception ex) when (ex is
            ArgumentException or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            DirectoryNotFoundException)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] {0}", Markup.Escape(ex.Message));
            return Report(1, ex.Message);
        }
    }

    public static int Run(Func<Task<int>> action)
    {
        try
        {
            return Report(action().GetAwaiter().GetResult(), null);
        }
        catch (AggregateException agg) when (agg.InnerException is Exception inner)
        {
            return RunException(inner);
        }
        catch (Exception ex)
        {
            return RunException(ex);
        }
    }

    private static int RunException(Exception ex)
    {
        if (ex is
            ArgumentException or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            DirectoryNotFoundException)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] {0}", Markup.Escape(ex.Message));
            return Report(1, ex.Message);
        }

        AnsiConsole.MarkupLine("[red]Unexpected error:[/] {0}", Markup.Escape(ex.Message));
        return Report(1, ex.Message);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>
    /// Under <c>--json</c>, writes the command's outcome to stdout: <c>success</c>, <c>exitCode</c>, <c>dryRun</c>, the files
    /// written (<c>path</c>, <c>action</c>: created/updated), the error message when it failed, and any <c>result</c>.
    /// </summary>
    private static int Report(int exitCode, string? error)
    {
        if (!Ux.Json)
            return exitCode;

        var document = new
        {
            success = exitCode == 0,
            exitCode,
            dryRun = Ux.DryRun,
            files = Ux.Changes.Select(c => new { path = c.Path, action = c.Action }).ToList(),
            error,
            result = Ux.Result,
        };
        Console.Out.WriteLine(JsonSerializer.Serialize(document, JsonOptions));
        return exitCode;
    }
}
