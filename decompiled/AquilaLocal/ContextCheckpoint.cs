using System;

namespace AquilaLocal;

public sealed class ContextCheckpoint
{
	public string Summary { get; set; } = "";


	public string ThroughTurnId { get; set; } = "";


	public int CoveredTurns { get; set; }

	public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;


	public string Model { get; set; } = "";


	public int SourceTokens { get; set; }

	public int SummaryTokens { get; set; }
}
