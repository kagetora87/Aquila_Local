namespace AquilaLocal;

public sealed record InferenceGpu(string Device, string Name, string AdapterKey, long DedicatedBytes, long FreeBytes);
