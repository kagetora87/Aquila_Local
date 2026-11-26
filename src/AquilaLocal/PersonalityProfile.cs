using System;

namespace AquilaLocal;

public sealed class PersonalityProfile
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = "New profile";

	public string Style { get; set; } = "";

	public string Examples { get; set; } = "";

	public bool LearnMemories { get; set; } = true;
}
