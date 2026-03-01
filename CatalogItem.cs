using MessagePack;

namespace WebNet.CatalogServer
{
    [MessagePackObject]
    public record CatalogItem
    {
        [Key(0)] public Guid Id { get; set; } = Guid.NewGuid();
        [Key(1)] public string Name { get; set; } = string.Empty;
        [Key(2)] public Dictionary<string, string> Properties { get; set; } = [];
        [Key(3)] public string[] Tags { get; set; } = [];
    }
}
