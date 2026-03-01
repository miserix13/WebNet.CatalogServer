using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;

namespace WebNet.CatalogServer;

public sealed class AkkaClusterRuntime : IAsyncDisposable
{
    private readonly AkkaClusterOptions options;
    private ActorSystem? system;

    public AkkaClusterRuntime(AkkaClusterOptions options)
    {
        this.options = options;
    }

    public bool IsRunning => this.system is not null;

    public int GetMemberCount()
    {
        if (this.system is null)
        {
            return 0;
        }

        var state = Cluster.Get(this.system).State;
        return state.Members.Count;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (this.system is not null)
        {
            return;
        }

        var seedNodes = this.options.SeedNodes.Count > 0
            ? this.options.SeedNodes
            : [$"akka://{this.options.SystemName}@{this.options.Hostname}:{this.options.Port}"];

        var seeds = string.Join(", ", seedNodes.Select(seed => $"\"{seed}\""));

        var hocon = $@"
akka {{
  actor.provider = cluster
    remote.artery.canonical.hostname = ""{this.options.Hostname}""
  remote.artery.canonical.port = {this.options.Port}
  cluster.seed-nodes = [{seeds}]
    cluster.roles = [""catalog""]
}}";

        var config = ConfigurationFactory.ParseString(hocon);
        this.system = ActorSystem.Create(this.options.SystemName, config);
        _ = Cluster.Get(this.system);

        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (this.system is null)
        {
            return;
        }

        await this.system.Terminate();
        this.system = null;
    }

    public async ValueTask DisposeAsync()
    {
        await this.StopAsync();
    }
}

public sealed record AkkaClusterOptions(
    string SystemName,
    string Hostname,
    int Port,
    IReadOnlyList<string> SeedNodes)
{
    public static AkkaClusterOptions CreateDefault(int clusterPort) => new(
        SystemName: "webnet-catalog",
        Hostname: "127.0.0.1",
        Port: clusterPort,
        SeedNodes: []);
}