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
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] {0}", ex.Message);
            return 1;
        }
    }
}
