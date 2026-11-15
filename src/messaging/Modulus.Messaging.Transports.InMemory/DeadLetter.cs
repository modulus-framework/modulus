namespace Modulus.Messaging.Transports.InMemory;

/// <summary>A message parked in an <c>_error</c> or <c>_skipped</c> queue.</summary>
/// <param name="Envelope">The message as it was when it was parked.</param>
/// <param name="Exception">The failure, for <c>_error</c>; null for <c>_skipped</c>.</param>
public sealed record DeadLetter(MessageEnvelope Envelope, Exception? Exception);
