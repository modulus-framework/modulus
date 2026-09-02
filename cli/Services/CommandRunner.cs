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
            return action();
        }
        catch (Exception ex) when (ex is
            ArgumentException or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            DirectoryNotFoundException)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] {0}", Markup.Escape(ex.Message));
            return 1;
        }
    }

    public static int Run(Func<Task<int>> action)
    {
        try
        {
            return action().GetAwaiter().GetResult();
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
            return 1;
        }

        AnsiConsole.MarkupLine("[red]Unexpected error:[/] {0}", Markup.Escape(ex.Message));
        return 1;
    }
}
