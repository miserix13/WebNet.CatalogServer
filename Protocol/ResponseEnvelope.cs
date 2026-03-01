using MessagePack;

namespace WebNet.CatalogServer;

[MessagePackObject]
public record ResponseEnvelope(
    [property: Key(0)] Guid RequestId,
    [property: Key(1)] bool IsSuccess,
    [property: Key(2)] string? ErrorCode,
    [property: Key(3)] string? ErrorMessage,
    [property: Key(4)] byte[] Payload,
    [property: Key(5)] DateTimeOffset CreatedAtUtc)
{
    public static ResponseEnvelope Success<T>(Guid requestId, T payload)
    {
        return new ResponseEnvelope(
            requestId,
            true,
            null,
            null,
            MessagePackSerializer.Serialize(payload),
            DateTimeOffset.UtcNow);
    }

    public static ResponseEnvelope Error(Guid requestId, string errorCode, string errorMessage)
    {
        return new ResponseEnvelope(
            requestId,
            false,
            errorCode,
            errorMessage,
            [],
            DateTimeOffset.UtcNow);
    }
}
