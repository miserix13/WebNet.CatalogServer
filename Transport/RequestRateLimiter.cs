namespace WebNet.CatalogServer;

public sealed class RequestRateLimiter
{
    private readonly int maxRequestsPerSecond;
    private readonly int maxBurst;
    private readonly Queue<DateTimeOffset> timestamps = new();

    public RequestRateLimiter(int maxRequestsPerSecond, int maxBurst)
    {
        if (maxRequestsPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRequestsPerSecond));
        }

        if (maxBurst <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBurst));
        }

        this.maxRequestsPerSecond = maxRequestsPerSecond;
        this.maxBurst = maxBurst;
    }

    public bool TryConsume(DateTimeOffset now)
    {
        var lowerBound = now - TimeSpan.FromSeconds(1);
        while (this.timestamps.Count > 0 && this.timestamps.Peek() < lowerBound)
        {
            _ = this.timestamps.Dequeue();
        }

        if (this.timestamps.Count >= Math.Min(this.maxBurst, this.maxRequestsPerSecond))
        {
            return false;
        }

        this.timestamps.Enqueue(now);
        return true;
    }
}
