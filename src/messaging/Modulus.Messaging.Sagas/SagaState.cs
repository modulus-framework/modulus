namespace Modulus.Messaging.Sagas;

/// <summary>A named state of a saga. Declared by <see cref="SagaStateMachine{TSaga}.State"/> and used for <c>During</c> and <c>TransitionTo</c>.</summary>
public sealed record SagaState(string Name);
