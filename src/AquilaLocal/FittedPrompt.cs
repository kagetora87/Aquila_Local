using System.Text.Json.Nodes;

namespace AquilaLocal;

internal sealed record FittedPrompt(JsonArray Messages, int OmittedPairs);
