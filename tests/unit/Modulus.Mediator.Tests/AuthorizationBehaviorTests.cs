using FluentAssertions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Mediator.Abstractions.Attributes;
using Modulus.Mediator.Behaviors;
using Xunit;

namespace Modulus.Mediator.Tests;

/// <summary>The mediator's permission check, including the step-up gate that every entry point shares.</summary>
[Trait("Category", "Unit")]
public sealed class AuthorizationBehaviorTests
{
    [RequirePermission("payments:approve")]
    private sealed record ApprovePayment;

    private sealed record PlainQuery;

    private sealed class FixedUser(params string[] permissions) : ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public string? UserName => "u";
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => permissions.Contains(permission);
        public IReadOnlyList<string> Permissions => permissions;
    }

    private sealed class FixedGate(bool satisfied) : IStepUpGate
    {
        public List<string> Asked { get; } = [];

        public bool IsSatisfied(string permission)
        {
            Asked.Add(permission);
            return satisfied;
        }
    }

    private static Task<int> Next() => Task.FromResult(1);

    [Fact]
    public async Task Step_up_refused_stops_the_command_before_the_handler()
    {
        var gate = new FixedGate(satisfied: false);
        var behavior = new AuthorizationBehavior<ApprovePayment, int>(new FixedUser("payments:approve"), gate);
        var handlerRan = false;

        var act = () => behavior.HandleAsync(new ApprovePayment(), () => { handlerRan = true; return Next(); }, CancellationToken.None);

        var error = await act.Should().ThrowAsync<StepUpRequiredException>();
        error.Which.Permission.Should().Be("payments:approve");
        handlerRan.Should().BeFalse();
        gate.Asked.Should().Equal("payments:approve");
    }

    [Fact]
    public async Task Step_up_satisfied_runs_the_command()
    {
        var behavior = new AuthorizationBehavior<ApprovePayment, int>(new FixedUser("payments:approve"), new FixedGate(satisfied: true));

        (await behavior.HandleAsync(new ApprovePayment(), Next, CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task Without_a_gate_the_permission_alone_decides()
    {
        var behavior = new AuthorizationBehavior<ApprovePayment, int>(new FixedUser("payments:approve"));

        (await behavior.HandleAsync(new ApprovePayment(), Next, CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task Missing_permission_is_still_forbidden_before_step_up_is_asked()
    {
        var gate = new FixedGate(satisfied: true);
        var behavior = new AuthorizationBehavior<ApprovePayment, int>(new FixedUser(), gate);

        await FluentActions.Awaiting(() => behavior.HandleAsync(new ApprovePayment(), Next, CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
        gate.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Commands_without_a_permission_attribute_never_consult_the_gate()
    {
        var gate = new FixedGate(satisfied: false);
        var behavior = new AuthorizationBehavior<PlainQuery, int>(new FixedUser(), gate);

        (await behavior.HandleAsync(new PlainQuery(), Next, CancellationToken.None)).Should().Be(1);
        gate.Asked.Should().BeEmpty();
    }
}
