namespace AquilaLocal.Companion;

/// <summary>
/// Thrown whenever a persona's drive state cannot be read exactly as stored. There is deliberately no
/// fallback: running on default drives would silently turn the persona into a stranger.
/// </summary>
public sealed class DriveStateUnavailableException : Exception
{
    public string PersonaId { get; }

    public DriveStateUnavailableException(string personaId, string reason, Exception? inner = null)
        : base($"Can't read {personaId}'s drive state: {reason} Aquila stopped instead of guessing her drives, so nothing was sent.", inner)
    {
        PersonaId = personaId;
    }
}

/// <summary>A persona's config file or the registry is missing, malformed or still has placeholders.</summary>
public sealed class PersonaConfigException : Exception
{
    public PersonaConfigException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Thrown when a persona's appearance card is missing, unreadable or too long. Like her drives, there is no
/// fallback: a persona who doesn't know her own body would invent one.
/// </summary>
public sealed class AppearanceCardUnavailableException : Exception
{
    public string PersonaId { get; }

    public AppearanceCardUnavailableException(string personaId, string reason, Exception? inner = null)
        : base($"Can't read {personaId}'s appearance card: {reason} Aquila stopped instead of guessing what she looks like, so nothing was sent.", inner)
    {
        PersonaId = personaId;
    }
}

/// <summary>A scene can't do what was asked (unknown scene, too many people, someone not in it). Shown as is.</summary>
public sealed class SceneException : Exception
{
    public SceneException(string message, Exception? inner = null) : base(message, inner) { }
}
