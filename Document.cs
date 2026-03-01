using MessagePack;

namespace WebNet.CatalogServer
{
    [MessagePackObject]
    public record Document
    {
        [Key(0)] public Guid DocumentId { get; set; } = Guid.NewGuid();
        [Key(1)] public Dictionary<string, string> Properties { get; set; } = [];
    }
}