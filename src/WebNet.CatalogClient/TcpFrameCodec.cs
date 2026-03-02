namespace WebNet.CatalogClient;

internal static class TcpFrameCodec
{
    private const int PrefixLength = 4;

    public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var header = BitConverter.GetBytes(payload.Length);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(header);
        }

        await stream.WriteAsync(header.AsMemory(0, PrefixLength), cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<byte[]?> ReadFrameAsync(Stream stream, int maxFrameBytes, CancellationToken cancellationToken)
    {
        var header = new byte[PrefixLength];
        var hasHeader = await ReadExactlyAsync(stream, header, cancellationToken);
        if (!hasHeader)
        {
            return null;
        }

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(header);
        }

        var length = BitConverter.ToInt32(header, 0);
        if (length <= 0 || length > maxFrameBytes)
        {
            throw new InvalidDataException($"Invalid frame length '{length}'.");
        }

        var payload = new byte[length];
        var hasPayload = await ReadExactlyAsync(stream, payload, cancellationToken);
        if (!hasPayload)
        {
            return null;
        }

        return payload;
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}