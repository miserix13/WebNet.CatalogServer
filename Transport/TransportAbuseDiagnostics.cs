namespace WebNet.CatalogServer;

public sealed record TransportAbuseSnapshot(
    long RateLimitedRequests,
    long RejectedConnections,
    long ReadTimeouts,
    long InvalidFrames,
    long InvalidRequests,
    long DispatchErrors,
    long ProtocolDisconnects);

public static class TransportAbuseDiagnostics
{
    private static long rateLimitedRequests;
    private static long rejectedConnections;
    private static long readTimeouts;
    private static long invalidFrames;
    private static long invalidRequests;
    private static long dispatchErrors;
    private static long protocolDisconnects;

    public static void Reset()
    {
        Interlocked.Exchange(ref rateLimitedRequests, 0);
        Interlocked.Exchange(ref rejectedConnections, 0);
        Interlocked.Exchange(ref readTimeouts, 0);
        Interlocked.Exchange(ref invalidFrames, 0);
        Interlocked.Exchange(ref invalidRequests, 0);
        Interlocked.Exchange(ref dispatchErrors, 0);
        Interlocked.Exchange(ref protocolDisconnects, 0);
    }

    public static TransportAbuseSnapshot Snapshot() => new(
        Interlocked.Read(ref rateLimitedRequests),
        Interlocked.Read(ref rejectedConnections),
        Interlocked.Read(ref readTimeouts),
        Interlocked.Read(ref invalidFrames),
        Interlocked.Read(ref invalidRequests),
        Interlocked.Read(ref dispatchErrors),
        Interlocked.Read(ref protocolDisconnects));

    internal static void RecordRateLimitedRequest() => Interlocked.Increment(ref rateLimitedRequests);

    internal static void RecordRejectedConnection() => Interlocked.Increment(ref rejectedConnections);

    internal static void RecordReadTimeout() => Interlocked.Increment(ref readTimeouts);

    internal static void RecordInvalidFrame() => Interlocked.Increment(ref invalidFrames);

    internal static void RecordInvalidRequest() => Interlocked.Increment(ref invalidRequests);

    internal static void RecordDispatchError() => Interlocked.Increment(ref dispatchErrors);

    internal static void RecordProtocolDisconnect() => Interlocked.Increment(ref protocolDisconnects);
}