using Modulus.Core.Abstractions.Common;
using Modulus.Mediator.Abstractions;

namespace ProcureFlow.Modules.Identity.Application;

/// <summary>Command: NotifyUserCreated</summary>
public sealed record NotifyUserCreatedCommand : ICommand<Unit>;
