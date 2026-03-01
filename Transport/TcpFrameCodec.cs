using BeetleX.Buffers;
using DotNetty.Buffers;

namespace WebNet.CatalogServer;

public static class TcpFrameCodec
{
    private const int PrefixLength = 4;

    public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken = default)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var composite = Unpooled.Buffer(PrefixLength + payload.Length);
        composite.WriteInt(payload.Length);
        composite.WriteBytes(payload);

        var data = new byte[composite.ReadableBytes];
        composite.GetBytes(0, data);

        await stream.WriteAsync(data, cancellationToken);
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

        var length = BitHelper.ReadInt32(header, 0);
        if (BitConverter.IsLittleEndian)
        {
            length = BitHelper.SwapInt32(length);
        }

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
