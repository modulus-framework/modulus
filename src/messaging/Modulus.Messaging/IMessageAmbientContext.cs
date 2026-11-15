namespace Modulus.Messaging;

/// <summary>
/// Sets the ambient state of an outgoing message from the caller's scope: for example the tenant of the current
/// request, so a message sent from it carries that tenant (FR-21). The bus calls it around each publish, send and
/// schedule made through the scoped <see cref="IBus"/> or <see cref="IMessageScheduler"/>. The returned scope restores
/// the previous state when disposed; return null to leave the state unchanged.
/// </summary>
public interface IMessageAmbientContext
{
    IDisposable? Enter(IServiceProvider scope);
}
