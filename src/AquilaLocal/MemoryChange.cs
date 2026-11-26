using System.Collections.Generic;

namespace AquilaLocal;

public sealed record MemoryChange(int Saved, int Updated, int Review, int Rejected, IReadOnlyList<string> Ids);
