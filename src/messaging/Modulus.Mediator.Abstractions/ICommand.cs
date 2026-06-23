namespace Modulus.Mediator.Abstractions;

using Modulus.Core.Abstractions.Common;

/// <summary>Marker for commands that return a value.</summary>
public interface ICommand<TResponse> { }

/// <summary>Marker for fire-and-forget commands (no return value).</summary>
public interface ICommand : ICommand<Unit> { }

/// <summary>Marker for queries (read-only, no side effects).</summary>
public interface IQuery<TResponse> { }