using MessagePack;

namespace WebNet.CatalogServer;

[MessagePackObject]
public record RequestEnvelope(
    [property: Key(0)] Guid RequestId,
    [property: Key(1)] CommandKind Command,
    [property: Key(2)] byte[] Payload,
    [property: Key(3)] DateTimeOffset CreatedAtUtc)
{
    public static RequestEnvelope FromPayload<T>(Guid requestId, CommandKind command, T payload)
    {
        return new RequestEnvelope(
            requestId,
            command,
            MessagePackSerializer.Serialize(payload),
            DateTimeOffset.UtcNow);
    }
}
