using System.Collections.Generic;

namespace AquilaLocal;

public sealed class MemoryCatalog
{
	public int Version { get; set; } = 1;

	public List<MemoryEntry> Entries { get; set; } = new List<MemoryEntry>();
}
