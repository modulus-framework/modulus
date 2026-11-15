using System.Text.RegularExpressions;

namespace Modulus.Messaging.Outbox.Npgsql;

/// <summary>Settings of the Npgsql outbox.</summary>
public sealed partial class NpgsqlOutboxOptions
{
    /// <summary>Name of the outbox table, a plain SQL identifier.</summary>
    public string Table { get; set; } = "messaging_outbox";

    /// <summary>How long the dispatcher waits when the outbox is empty.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Messages claimed per dispatch round.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>How long a claim lasts. A dispatcher that dies mid-batch loses its claim after this.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>False when the host runs <see cref="NpgsqlOutboxDispatch"/> itself, for example in a dedicated job.</summary>
    public bool RunDispatcher { get; set; } = true;

    /// <summary>Creates the table and indexes when the dispatcher starts. Turn it off when a migration owns the schema.</summary>
    public bool CreateSchema { get; set; } = true;

    internal void Validate()
    {
        if (!Identifier().IsMatch(Table))
        {
            throw new ArgumentException("Table must be a plain SQL identifier (letters, digits, underscores).", nameof(Table));
        }

        if (PollInterval <= TimeSpan.Zero || LeaseDuration <= TimeSpan.Zero || BatchSize <= 0)
        {
            throw new ArgumentException("Outbox poll interval, lease duration and batch size must be positive.");
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex Identifier();
}
