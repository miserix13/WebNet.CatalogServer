namespace WebNet.CatalogServer.Tests;

using BeetleX.Buffers;
using MessagePack;
using Xunit;

public sealed class TransportHardeningTests
{
    [Fact]
    public async Task TcpFrameCodec_RoundTrip_Succeeds()
    {
        await using var stream = new MemoryStream();
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        await TcpFrameCodec.WriteFrameAsync(stream, payload);
        stream.Position = 0;

        var read = await TcpFrameCodec.ReadFrameAsync(stream, maxFrameBytes: 1024);
        Assert.NotNull(read);
        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task TcpFrameCodec_InvalidLength_Throws()
    {
        await using var stream = new MemoryStream();
        var header = new byte[4];
        var length = 9999;
        if (BitConverter.IsLittleEndian)
        {
            length = BitHelper.SwapInt32(length);
        }

        BitHelper.Write(header, 0, length);
        await stream.WriteAsync(header);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => TcpFrameCodec.ReadFrameAsync(stream, maxFrameBytes: 128));
    }

    [Fact]
    public void RequestRateLimiter_ThrottlesBurst()
    {
        var limiter = new RequestRateLimiter(maxRequestsPerSecond: 2, maxBurst: 2);
        var now = DateTimeOffset.UtcNow;

        Assert.True(limiter.TryConsume(now));
        Assert.True(limiter.TryConsume(now.AddMilliseconds(10)));
        Assert.False(limiter.TryConsume(now.AddMilliseconds(20)));
        Assert.True(limiter.TryConsume(now.AddSeconds(2)));
    }

    [Fact]
    public async Task Dispatcher_RejectsUnknownCommand()
    {
        var storage = new Storage(new FileStoragePersistenceAdapter(Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"), "store.mpk")));
        var server = new Server(storage, new AllowAllTokenAuthorizer(), new AllowAllClientCertificateValidator());
        var dispatcher = new TcpCommandDispatcher(server);

        var request = new RequestEnvelope(Guid.NewGuid(), CommandKind.Unknown, [1], DateTimeOffset.UtcNow);
        var wire = new WireRequest(request, Token: "dev-token", ClientCertificateThumbprint: "dev-thumbprint", Subject: "test", Roles: ["admin"]);
        var bytes = MessagePackSerializer.Serialize(wire);

        await Assert.ThrowsAsync<InvalidDataException>(() => dispatcher.DispatchAsync(bytes));
    }

    [Fact]
    public async Task Dispatcher_RejectsInvalidToken()
    {
        var storage = new Storage(new FileStoragePersistenceAdapter(Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"), "store.mpk")));
        var server = new Server(storage, new AllowAllTokenAuthorizer(), new AllowAllClientCertificateValidator());
        var dispatcher = new TcpCommandDispatcher(server);

        var payload = MessagePackSerializer.Serialize(new HealthRequest());
        var request = new RequestEnvelope(Guid.NewGuid(), CommandKind.Health, payload, DateTimeOffset.UtcNow);
        var wire = new WireRequest(request, Token: "", ClientCertificateThumbprint: "dev-thumbprint", Subject: "test", Roles: ["admin"]);
        var bytes = MessagePackSerializer.Serialize(wire);

        await Assert.ThrowsAsync<InvalidDataException>(() => dispatcher.DispatchAsync(bytes));
    }
}
