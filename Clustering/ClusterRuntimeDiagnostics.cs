namespace WebNet.CatalogServer;

public sealed record ClusterRuntimeSnapshot(
    bool Enabled,
    bool Running,
    string? SystemName,
    string? Hostname,
    int Port,
    int MemberCount);

public static class ClusterRuntimeDiagnostics
{
    private static readonly Lock Sync = new();

    private static bool enabled;
    private static bool running;
    private static string? systemName;
    private static string? hostname;
    private static int port;
    private static int memberCount;

    public static void Configure(bool enabledCluster, string? configuredSystemName, string? configuredHostname, int configuredPort)
    {
        lock (Sync)
        {
            enabled = enabledCluster;
            systemName = configuredSystemName;
            hostname = configuredHostname;
            port = configuredPort;

            if (!enabledCluster)
            {
                running = false;
                memberCount = 0;
            }
        }
    }

    public static void SetRunning(bool isRunning, int members)
    {
        lock (Sync)
        {
            running = isRunning;
            memberCount = Math.Max(0, members);
        }
    }

    public static ClusterRuntimeSnapshot Snapshot()
    {
        lock (Sync)
        {
            return new ClusterRuntimeSnapshot(enabled, running, systemName, hostname, port, memberCount);
        }
    }
}