namespace Modulus.Messaging;

/// <summary>A message's body is larger than the bus allows (FR-22). Refused at send, before anything is stored or sent.</summary>
public sealed class MessageTooLargeException : InvalidOperationException
{
    public MessageTooLargeException(Type messageType, int bodyBytes, int limitBytes)
        : base($"Message {messageType.Name} is {bodyBytes} bytes; the bus allows {limitBytes}.")
    {
        MessageType = messageType;
        BodyBytes = bodyBytes;
        LimitBytes = limitBytes;
    }

    public Type MessageType { get; }

    public int BodyBytes { get; }

    public int LimitBytes { get; }
}
