namespace AquilaLocal;

public sealed record MemoryCandidate(string Topic, string Value, string Quote, string Kind, string Scope, double Confidence, string[] Replaces);
