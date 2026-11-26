using System;

namespace AquilaLocal;

public sealed class MemorySource
{
	public string ChatId { get; set; } = "";


	public string TurnId { get; set; } = "";


	public string Quote { get; set; } = "";


	public DateTimeOffset At { get; set; } = DateTimeOffset.Now;

}
