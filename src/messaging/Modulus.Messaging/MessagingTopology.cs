namespace Modulus.Messaging;

/// <summary>The endpoints this bus receives, and what handles and parks their messages (FR-23).</summary>
/// <param name="Endpoints">Each endpoint with a consumer, in name order.</param>
public sealed record MessagingTopology(IReadOnlyList<EndpointTopology> Endpoints);

/// <summary>One endpoint: its consumers and message types, and the queues its parked messages go to.</summary>
/// <param name="Name">The endpoint (queue) name.</param>
/// <param name="ErrorEndpoint">Where failed messages are parked: <c>Name_error</c>.</param>
/// <param name="SkippedEndpoint">Where unhandled and expired messages are parked: <c>Name_skipped</c>.</param>
/// <param name="Consumers">The consumer types on this endpoint.</param>
/// <param name="MessageTypes">The message types the endpoint's consumers take.</param>
public sealed record EndpointTopology(
    string Name,
    string ErrorEndpoint,
    string SkippedEndpoint,
    IReadOnlyList<string> Consumers,
    IReadOnlyList<string> MessageTypes);

/// <summary>Describes the bus's topology without touching the transport (FR-23).</summary>
public interface IMessagingTopology
{
    MessagingTopology DescribeTopology();
}
