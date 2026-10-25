using System.Diagnostics.Metrics;
using FluentAssertions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Mediator.Abstractions.Attributes;
using Modulus.Mediator.Behaviors;
using Modulus.Observability;
using Xunit;

namespace Modulus.Mediator.Tests;

/// <summary>The mediator's permission decisions are counted: a refused command and an allowed one each move a counter.</summary>
[Trait("Category", "Unit")]
public sealed class AuthorizationMetersTests
{
    [RequirePermission("reports:export")]
    private sealed record Export;

    private sealed class Anonymous : ICurrentUser
    {
        public Guid? UserId => null;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => false;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class Holder : ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public string? UserName => "u";
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => permission == "reports:export";
        public IReadOnlyList<string> Permissions => ["reports:export"];
    }

    [Fact]
    public async Task A_refused_command_counts_as_denied()
    {
        var denied = await CountAsync(ModulusMeters.AuthorizationDenied, async () =>
        {
            var act = () => new AuthorizationBehavior<Export, int>(new Anonymous())
                .HandleAsync(new Export(), () => Task.FromResult(1), CancellationToken.None);
            await act.Should().ThrowAsync<UnauthorizedException>();
        });

        // The meter is process-wide, so other tests running in parallel may add to it: the check is that the refusal was counted.
        denied.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task An_allowed_command_counts_as_allowed()
    {
        var allowed = await CountAsync(ModulusMeters.AuthorizationAllowed, async () =>
        {
            await new AuthorizationBehavior<Export, int>(new Holder())
                .HandleAsync(new Export(), () => Task.FromResult(1), CancellationToken.None);
        });

        allowed.Should().BeGreaterThan(0);
    }

    /// <summary>Sums what <paramref name="counter"/> records while <paramref name="action"/> runs.</summary>
    private static async Task<long> CountAsync(Counter<long> counter, Func<Task> action)
    {
        var total = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument, counter))
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => total += value);
        listener.Start();

        await action();
        return total;
    }
}
