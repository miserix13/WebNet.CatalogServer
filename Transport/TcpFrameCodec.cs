using System.Buffers.Binary;

namespace WebNet.CatalogServer;

public static class TcpFrameCodec
{
    private const int PrefixLength = 4;

    public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken = default)
    {
        Span<byte> header = stackalloc byte[PrefixLength];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);

        await stream.WriteAsync(header.ToArray(), cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<byte[]?> ReadFrameAsync(Stream stream, int maxFrameBytes, CancellationToken cancellationToken = default)
    {
        var header = new byte[PrefixLength];
        var read = await ReadExactlyAsync(stream, header, cancellationToken);
        if (!read)
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > maxFrameBytes)
        {
            throw new InvalidDataException($"Invalid frame length '{length}'.");
        }

        var payload = new byte[length];
        var frameRead = await ReadExactlyAsync(stream, payload, cancellationToken);
        if (!frameRead)
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
