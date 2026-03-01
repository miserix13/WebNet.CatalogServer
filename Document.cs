namespace WebNet.CatalogServer
{
    public record Document
    {
        public Guid DocumentId { get; set; } = Guid.NewGuid();
    }
}