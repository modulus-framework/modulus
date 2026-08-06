using System.ComponentModel;
using Spectre.Console.Cli;

namespace Modulus.Cli.Services;

/// <summary>
/// Base settings class every command inherits. Carries the global
/// quality-of-life flags so <c>--dry-run</c>, <c>--force</c>,
/// <c>--verbose</c>, and <c>--quiet</c> are accepted on every command
/// and threaded into <see cref="Ux"/> at the start of <c>Execute</c>.
/// </summary>
/// <remarks>
/// Flags are parsed by Spectre.Cli per-command (i.e. they appear
/// <i>after</i> the command name: <c>modulus app Foo --dry-run</c>).
/// Each command's <c>Execute</c> calls <see cref="Apply"/> in its first
/// line to populate the <see cref="Ux"/> globals before doing work.
/// </remarks>
internal abstract class ModulusSettings : CommandSettings
{
    [Description("Preview what would happen: report file writes / commands without executing them.")]
    [CommandOption("--dry-run")]
    [DefaultValue(false)]
    public bool DryRun { get; init; }

    [Description("Overwrite existing files without prompting / skip confirmations.")]
    [CommandOption("--force")]
    [DefaultValue(false)]
    public bool Force { get; init; }

    [Description("Show detailed output (child-process stdout, every written file, etc.).")]
    [CommandOption("-v|--verbose")]
    [DefaultValue(false)]
    public bool Verbose { get; init; }

    [Description("Suppress everything but errors and the final summary.")]
    [CommandOption("-q|--quiet")]
    [DefaultValue(false)]
    public bool Quiet { get; init; }

    /// <summary>
    /// Pushes these flag values into the <see cref="Ux"/> globals.
    /// Call at the top of every command's <c>Execute</c>.
    /// </summary>
    public void Apply()
    {
        Ux.Reset();
        Ux.DryRun = DryRun;
        Ux.Force = Force;
        Ux.Verbose = Verbose;
        Ux.Quiet = Quiet;
    }
}
