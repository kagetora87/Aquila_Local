namespace AquilaLocal.Companion.Priming;

/// <summary>What the user kept from a draft, after reviewing and editing it.</summary>
public sealed class PrimeChoice
{
    /// <summary>Where it came from, in a few words (file names), for her records.</summary>
    public string Source { get; set; } = "";

    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Occupation { get; set; } = "";
    public string Relationship { get; set; } = "";
    public string Tagline { get; set; } = "";

    /// <summary>The personality sheet to use; empty leaves the current one alone.</summary>
    public string Personality { get; set; } = "";

    public List<DriveSuggestion> Drives { get; set; } = new();
    public List<MemorySuggestion> Memories { get; set; } = new();

    /// <summary>The conversation to remember word for word (empty: don't).</summary>
    public List<(string User, string Reply)> Recall { get; set; } = new();

    public StateBlock MemoryBlock() => new()
    {
        Facts = Memories.Where(m => m.Layer == MemoryLayers.Facts).Select(m => m.Text.Trim()).Where(t => t.Length > 0).ToList(),
        Feelings = Memories.Where(m => m.Layer == MemoryLayers.Feelings).Select(m => m.Text.Trim()).Where(t => t.Length > 0).ToList(),
        Hooks = Memories.Where(m => m.Layer == MemoryLayers.Hooks).Select(m => m.Text.Trim()).Where(t => t.Length > 0).ToList()
    };
}

/// <summary>Puts what the user approved into a companion being created, or into one who already exists.</summary>
public static class PrimeApplier
{
    /// <summary>Fills Create Companion's draft. Nothing is saved until they're brought to life.</summary>
    public static void ToDraft(CompanionDraft draft, PrimeChoice c)
    {
        if (c.Name.Trim().Length > 0)
        {
            draft.Name = c.Name.Trim();
        }
        if (c.Age >= PersonaConfig.MinAge && c.Age <= PersonaConfig.MaxAge)
        {
            draft.Age = c.Age;
        }
        if (c.Occupation.Trim().Length > 0)
        {
            draft.Occupation = c.Occupation.Trim();
        }
        if (c.Relationship.Trim().Length > 0)
        {
            draft.Relationship = c.Relationship.Trim();
        }
        if (c.Tagline.Trim().Length > 0)
        {
            draft.Tagline = c.Tagline.Trim();
        }
        if (c.Personality.Trim().Length > 0)
        {
            draft.Personality = c.Personality.Trim().Length > CompanionDraft.MaxPersonalityChars ? c.Personality.Trim()[..CompanionDraft.MaxPersonalityChars] : c.Personality.Trim();
        }
        var drives = draft.Drives.ToList();
        Shape(drives, c.Drives, setInitial: true);
        draft.Drives = drives;
        draft.PrimedMemory = c.MemoryBlock();
        draft.PrimedRecall = c.Recall.ToList();
        draft.PrimedFrom = c.Source.Trim();
    }

    /// <summary>
    /// Primes a companion who already exists: her sheet and drive settings go into <paramref name="config"/> (the
    /// caller saves it afterwards), her drive levels and the approved memories into her store, all logged with
    /// the reason. Memory stays append-only: memories are added, never replacing anything. Returns what changed.
    /// </summary>
    public static List<string> ToPersona(PersonaConfig config, PersonaStore store, PrimeChoice c, Func<string, float[]>? embed, DateTimeOffset now)
    {
        // Vectors first: if the embedding model fails, nothing has been written yet.
        List<float[]?> vectors = Vectors(c, embed);
        string why = "primed from " + (c.Source.Trim().Length > 0 ? c.Source.Trim() : "a saved chat") + ", approved by you";
        var done = new List<string>();
        if (c.Personality.Trim().Length > 0 && c.Personality.Trim() != config.PersonalitySheet.Trim())
        {
            config.PersonalitySheet = c.Personality.Trim();
            done.Add("new personality sheet");
        }
        List<string> before = config.Drives.Select(d => d.Name).ToList();
        Shape(config.Drives, c.Drives, setInitial: false);
        config.ValidateShape();
        if (config.Drives.Count != before.Count)
        {
            done.AddRange(store.ReshapeDrives(config, new Dictionary<string, string>(), now));
        }
        Dictionary<string, DriveValue> now_ = store.ReadDrives(config.Drives.Select(d => d.Name));
        var changes = new List<DriveChange>();
        foreach (DriveSuggestion s in c.Drives.Where(s => s.New == null && before.Contains(s.Name)))
        {
            DriveSpec spec = config.Drive(s.Name);
            double v = DriveEngine.Clamp(spec, s.Level);
            if (Math.Abs(v - now_[s.Name].Value) > 0.05)
            {
                changes.Add(new DriveChange(s.Name, now_[s.Name].Value, v, "operator: " + why));
                done.Add($"{s.Name} set to {v:0.0}");
            }
        }
        store.Apply(changes, now);
        int added = 0;
        foreach (MemorySuggestion m in c.Memories.Where(m => m.Text.Trim().Length > 0))
        {
            store.AddMemory(m.Layer, m.Text.Trim(), MemoryAuthors.Operator, why, now);
            added++;
        }
        if (added > 0)
        {
            done.Add(added == 1 ? "1 memory added" : $"{added} memories added");
        }
        if (c.Recall.Count > 0)
        {
            int n = store.AddImportedExchanges(c.Source, c.Recall, vectors, now);
            done.Add($"{n} moments of the conversation remembered word for word");
        }
        return done;
    }

    /// <summary>Stores the saved conversation word for word in her recall memory.</summary>
    public static int Remember(PersonaStore store, PrimeChoice c, Func<string, float[]>? embed, DateTimeOffset now)
    {
        return store.AddImportedExchanges(c.Source, c.Recall, Vectors(c, embed), now);
    }

    private static List<float[]?> Vectors(PrimeChoice c, Func<string, float[]>? embed) =>
        c.Recall.Select(p => embed?.Invoke(VectorMemory.TextOf(p.User, p.Reply))).ToList();

    /// <summary>Starting levels and weights for drives she has; new drives added (at most eight in all).</summary>
    private static void Shape(List<DriveSpec> drives, IEnumerable<DriveSuggestion> kept, bool setInitial)
    {
        foreach (DriveSuggestion s in kept)
        {
            DriveSpec? d = drives.FirstOrDefault(x => x.Name == s.Name);
            if (d != null)
            {
                d.Weight = Math.Clamp(s.Weight, 0, 10);
                if (setInitial)
                {
                    d.Initial = Math.Clamp(s.Level, d.Floor, d.Ceiling);
                }
            }
            else if (s.New != null && drives.Count < Companion.Drives.MaxDrives)
            {
                s.New.Initial = Math.Clamp(s.Level, s.New.Floor, s.New.Ceiling);
                s.New.Weight = Math.Clamp(s.Weight, 0, 10);
                drives.Add(s.New);
            }
        }
    }
}
