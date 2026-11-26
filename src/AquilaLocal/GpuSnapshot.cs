using System;
using System.Collections.Generic;

namespace AquilaLocal;

public sealed record GpuSnapshot(IReadOnlyList<GpuAdapter> Adapters, IReadOnlyList<GpuApp> Apps, Dictionary<string, long> LocalUsage, string? Error, DateTimeOffset At)
{
	public Dictionary<string, double> EngineUsage { get; init; } = new Dictionary<string, double>();
}
