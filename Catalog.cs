using MessagePack;
using System.Collections;

namespace WebNet.CatalogServer
{
    [MessagePackObject]
    public class Catalog : IEnumerable<CatalogItem>
    {
        private readonly List<CatalogItem> items;

        public Catalog() :
            base()
        {
            this.items = [];
        }

        public IEnumerator<CatalogItem> GetEnumerator()
        {
            return this.items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        [IgnoreMember] public int Count => this.items.Count;

        [Key(0)] public Guid Id { get; set; } = Guid.NewGuid();
        [Key(1)] public string Name { get; set; } = string.Empty;

        public void Add(CatalogItem item)
        {
            this.items.Add(item);
        }

        public void Clear() => this.items.Clear();

        public void Remove(CatalogItem item)
        {
            this.items.Remove(item);
        }
    }
}
