using System.Text.Json;
using System.Text.RegularExpressions;

namespace AquilaLocal.Companion;

public sealed class PersonaEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Path of persona.json, relative to the registry folder (or absolute).</summary>
    public string ConfigPath { get; set; } = "";
    /// <summary>Path of the persona's private SQLite store (drives, log, vectors, summary), relative or absolute.</summary>
    public string MemoryPath { get; set; } = "";
}

public sealed class RegistryManifest
{
    public int Version { get; set; } = 1;
    public string Active { get; set; } = "";
    public List<PersonaEntry> Personas { get; set; } = new();
}

public sealed record PersonaStatus(PersonaEntry Entry, bool Ready, bool StoreExists, List<string> Problems);

/// <summary>
/// The persona registry: a manifest (registry.json) listing each persona's config path and memory path,
/// plus which one is active. Personas are loaded and switched at runtime through it.
/// </summary>
public sealed class PersonaRegistry
{
    public const string ManifestName = "registry.json";

    public string Root { get; }

    public string ManifestPath => Path.Combine(Root, ManifestName);

    public PersonaRegistry(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public static bool IsValidId(string id) => Regex.IsMatch(id ?? "", "^[a-z0-9][a-z0-9-]{0,39}$");

    public RegistryManifest Load()
    {
        if (!File.Exists(ManifestPath))
        {
            return new RegistryManifest();
        }
        RegistryManifest? m;
        try
        {
            m = JsonSerializer.Deserialize<RegistryManifest>(File.ReadAllText(ManifestPath), PersonaConfig.Json);
        }
        catch (JsonException ex)
        {
            throw new PersonaConfigException($"The persona registry is not valid JSON ({ManifestPath}): {ex.Message}", ex);
        }
        if (m == null || m.Version != 1)
        {
            throw new PersonaConfigException($"The persona registry ({ManifestPath}) is empty or has an unsupported version.");
        }
        Validate(m);
        return m;
    }

    private void Validate(RegistryManifest m)
    {
        var ids = new HashSet<string>();
        var configs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var memories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PersonaEntry e in m.Personas)
        {
            if (!IsValidId(e.Id) || !ids.Add(e.Id))
            {
                throw new PersonaConfigException($"The persona registry has a missing, invalid or repeated id '{e.Id}'.");
            }
            if (string.IsNullOrWhiteSpace(e.ConfigPath) || string.IsNullOrWhiteSpace(e.MemoryPath))
            {
                throw new PersonaConfigException($"Persona '{e.Id}' needs both a configPath and a memoryPath in the registry.");
            }
            if (!configs.Add(Resolve(e.ConfigPath)) || !memories.Add(Resolve(e.MemoryPath)))
            {
                throw new PersonaConfigException($"Persona '{e.Id}' shares a config or memory file with another persona. Every persona needs her own, so memories can't leak between them.");
            }
        }
        if (m.Active.Length > 0 && !ids.Contains(m.Active))
        {
            throw new PersonaConfigException($"The active persona '{m.Active}' isn't listed in the registry.");
        }
    }

    public string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path));

    public PersonaEntry Entry(string id) =>
        Load().Personas.FirstOrDefault(p => p.Id == id) ?? throw new PersonaConfigException($"No persona '{id}' in the registry.");

    public PersonaConfig LoadConfig(string id)
    {
        PersonaEntry e = Entry(id);
        PersonaConfig config = PersonaConfig.Load(Resolve(e.ConfigPath));
        if (config.Id != e.Id)
        {
            throw new PersonaConfigException($"The registry points '{e.Id}' at a config that belongs to '{config.Id}'.");
        }
        return config;
    }

    public PersonaStatus Status(string id)
    {
        PersonaEntry e = Entry(id);
        var problems = new List<string>();
        bool exists = File.Exists(Resolve(e.MemoryPath));
        try
        {
            PersonaConfig config = LoadConfig(id);
            List<string> missing = config.MissingInputs();
            if (missing.Count > 0)
            {
                problems.Add("Still missing: " + string.Join("; ", missing) + ".");
            }
            else if (!exists)
            {
                problems.Add($"{config.Name} hasn't been set up yet.");
            }
        }
        catch (PersonaConfigException ex)
        {
            problems.Add(ex.Message);
        }
        return new PersonaStatus(e, problems.Count == 0, exists, problems);
    }

    /// <summary>Adds a persona (writing her config) if her id isn't registered yet. Returns false if she already was.</summary>
    public bool Register(PersonaConfig config, string folder, bool makeActive)
    {
        config.ValidateShape();
        RegistryManifest m = Load();
        if (m.Personas.Any(p => p.Id == config.Id))
        {
            return false;
        }
        var entry = new PersonaEntry
        {
            Id = config.Id,
            Name = config.Name,
            ConfigPath = Path.Combine(folder, "persona.json"),
            MemoryPath = Path.Combine(folder, "memory.db")
        };
        string configPath = Resolve(entry.ConfigPath);
        if (!File.Exists(configPath))
        {
            config.Save(configPath);
        }
        m.Personas.Add(entry);
        if (makeActive || m.Active.Length == 0)
        {
            m.Active = config.Id;
        }
        Save(m);
        return true;
    }

    public void SetActive(string id)
    {
        RegistryManifest m = Load();
        if (!m.Personas.Any(p => p.Id == id))
        {
            throw new PersonaConfigException($"No persona '{id}' in the registry.");
        }
        m.Active = id;
        Save(m);
    }

    /// <summary>Creates the persona's state store from her config. Only ever called on an explicit request.</summary>
    public PersonaStore Provision(string id, DateTimeOffset now)
    {
        PersonaConfig config = LoadConfig(id);
        return PersonaStore.Create(Resolve(Entry(id).MemoryPath), config, now);
    }

    public PersonaStore OpenStore(string id) => PersonaStore.Open(Resolve(Entry(id).MemoryPath), id);

    private void Save(RegistryManifest m)
    {
        Validate(m);
        Directory.CreateDirectory(Root);
        string tmp = ManifestPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(m, PersonaConfig.Json));
        File.Move(tmp, ManifestPath, overwrite: true);
    }
}
