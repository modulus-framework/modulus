namespace Modulus.Messaging.Sagas;

/// <summary>The one configured instance of a state machine. A machine holds no per-instance state, so one is enough.</summary>
internal static class SagaMachine<TSaga, TMachine>
    where TSaga : SagaInstance, new()
    where TMachine : SagaStateMachine<TSaga>, new()
{
    internal static readonly TMachine Instance = Create();

    private static TMachine Create()
    {
        var machine = new TMachine();
        machine.Validate();
        return machine;
    }
}
