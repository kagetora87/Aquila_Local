using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AquilaLocal.Companion;

namespace AquilaLocal;

/// <summary>
/// App side of companion mode: the persona registry in Data\personas, the MiniLM embedder and the
/// engine-backed summary writer. Opened personas are cached, but every turn re-reads her drives from
/// disk, so a missing or damaged store always stops the turn.
/// </summary>
public sealed class CompanionHost : IDisposable
{
	private sealed class MiniLmEmbedder(string directory) : IEmbedder
	{
		private readonly object gate = new object();

		private EmbeddingModel? model;

		public float[] Embed(string text)
		{
			lock (gate)
			{
				model ??= new EmbeddingModel(directory);
				return model.Encode(text.Length > 4000 ? text.Substring(0, 4000) : text, CancellationToken.None);
			}
		}

		public void Dispose()
		{
			lock (gate)
			{
				model?.Dispose();
				model = null;
			}
		}
	}

	/// <summary>Runs the summary pass on the engine's second slot so the chat's prompt cache is untouched.</summary>
	public sealed class EngineGenerator(Engine engine) : ITextGenerator
	{
		public async Task<string> GenerateJsonAsync(string system, string user, int maxTokens, CancellationToken ct)
		{
			JsonObject payload = new JsonObject
			{
				["model"] = engine.Alias,
				["id_slot"] = 1,
				["messages"] = new JsonArray
				{
					new JsonObject { ["role"] = "system", ["content"] = system },
					new JsonObject { ["role"] = "user", ["content"] = user }
				},
				["stream"] = false,
				["temperature"] = 0.3,
				["max_tokens"] = maxTokens,
				["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
				["response_format"] = new JsonObject { ["type"] = "json_object" }
			};
			using HttpResponseMessage response = await engine.PostAsync("v1/chat/completions", payload, stream: false, ct);
			JsonNode? node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
			JsonNode choice = node?["choices"]?.AsArray().FirstOrDefault() ?? throw new IOException("The summary pass returned no answer.");
			if (choice["finish_reason"]?.GetValue<string>() != "stop")
			{
				throw new IOException("The summary pass ran out of room before finishing. The previous summary was kept.");
			}
			return choice["message"]?["content"]?.GetValue<string>() ?? "";
		}
	}

	private readonly MiniLmEmbedder embedder;

	private readonly object gate = new object();

	private readonly Dictionary<string, CompanionRuntime> open = new Dictionary<string, CompanionRuntime>();

	public PersonaRegistry Registry { get; }

	/// <summary>Set when the registry couldn't be read at startup; shown in the Companion window.</summary>
	public string? StartupProblem { get; private set; }

	/// <summary>Multi-persona scenes (Data\scenes): shared world and log, each persona's mind kept separate.</summary>
	public AquilaLocal.Companion.Scenes.SceneLibrary Scenes { get; }

	/// <summary>The soundboard (Data\soundboard): numbered sound files plus soundboard.json.</summary>
	public AquilaLocal.Companion.Sounds.SoundboardLibrary Soundboard { get; }

	/// <summary>What a persona is told about the soundboard; empty when it's off or has no sounds. A broken list throws.</summary>
	public string SoundboardPrompt(bool enabled) => enabled ? AquilaLocal.Companion.Sounds.SoundCues.Prompt(Soundboard.Load()) : "";

	/// <summary>The files of the sounds a reply cues, matched by name, type, description or meaning (unmatched cues are skipped).</summary>
	public List<string> SoundsFor(string reply)
	{
		List<string> names = AquilaLocal.Companion.Sounds.SoundCues.Names(reply);
		if (names.Count == 0)
		{
			return new List<string>();
		}
		AquilaLocal.Companion.Sounds.SoundboardSet set = Soundboard.Load();
		List<string> files = new List<string>();
		foreach (string name in names)
		{
			AquilaLocal.Companion.Sounds.SoundEntry? entry = AquilaLocal.Companion.Sounds.SoundCues.Match(name, set, Meaning);
			if (entry != null && File.Exists(Soundboard.PathOf(entry)))
			{
				files.Add(Soundboard.PathOf(entry));
			}
		}
		return files;
	}

	/// <summary>Sounds a reply cued that match nothing on the soundboard (so nothing played).</summary>
	public List<string> UnknownSounds(string reply)
	{
		if (!AquilaLocal.Companion.Sounds.SoundCues.HasCue(reply))
		{
			return new List<string>();
		}
		return AquilaLocal.Companion.Sounds.SoundCues.Unknown(reply, Soundboard.Load(), Meaning);
	}

	/// <summary>Similarity of meaning between two short texts (local embeddings); 0 when the embedder isn't available.</summary>
	public double Meaning(string a, string b)
	{
		try
		{
			float[] x = Embed(a);
			float[] y = Embed(b);
			double dot = 0, nx = 0, ny = 0;
			for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
			{
				dot += x[i] * y[i];
				nx += x[i] * x[i];
				ny += y[i] * y[i];
			}
			return nx == 0 || ny == 0 ? 0 : dot / Math.Sqrt(nx * ny);
		}
		catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
		{
			return 0;
		}
	}

	private readonly Dictionary<string, float[]> interests = new Dictionary<string, float[]>();

	public CompanionHost(string dataRoot, string? embeddingsDirectory = null)
	{
		Registry = new PersonaRegistry(Path.Combine(dataRoot, "personas"));
		Scenes = new AquilaLocal.Companion.Scenes.SceneLibrary(Path.Combine(dataRoot, "scenes"));
		Soundboard = new AquilaLocal.Companion.Sounds.SoundboardLibrary(Path.Combine(dataRoot, "soundboard"));
		embedder = new MiniLmEmbedder(embeddingsDirectory ?? Path.Combine(AppContext.BaseDirectory, "embeddings"));
		try
		{
			SeedVicki();
		}
		catch (Exception ex) when (ex is PersonaConfigException || ex is IOException || ex is UnauthorizedAccessException)
		{
			StartupProblem = ex.Message;
		}
	}

	/// <summary>
	/// Registers Vicki on first run. If her config on disk is still a placeholder copy and the built-in seed
	/// has since been completed (and she was never set up), the placeholder copy is replaced (a backup is kept).
	/// </summary>
	private void SeedVicki()
	{
		PersonaConfig seed = VickiSeed.Config();
		if (Registry.Register(seed, VickiSeed.Id, makeActive: false))
		{
			return;
		}
		if (seed.MissingInputs().Count > 0 || !Registry.Load().Personas.Any((PersonaEntry p) => p.Id == VickiSeed.Id))
		{
			return;
		}
		PersonaEntry entry = Registry.Entry(VickiSeed.Id);
		string configPath = Registry.Resolve(entry.ConfigPath);
		if (File.Exists(Registry.Resolve(entry.MemoryPath)))
		{
			return;
		}
		PersonaConfig onDisk = PersonaConfig.Load(configPath);
		if (onDisk.MissingInputs().Count > 0)
		{
			File.Copy(configPath, configPath + ".placeholder-backup", overwrite: true);
			seed.Save(configPath);
		}
	}

	/// <summary>
	/// Adds a new persona from a persona file (the same format as Vicki's). She still has to be set up once
	/// before she can chat. Refuses an id that's already taken, so no one can be overwritten.
	/// </summary>
	public PersonaConfig AddPersonaFromFile(string file)
	{
		PersonaConfig incoming = PersonaConfig.Load(file);
		if (!PersonaRegistry.IsValidId(incoming.Id))
		{
			throw new PersonaConfigException($"The file's id '{incoming.Id}' isn't usable. Use lowercase letters, digits and dashes, such as \"marcus\".");
		}
		if (Registry.Load().Personas.Any((PersonaEntry p) => p.Id == incoming.Id))
		{
			throw new PersonaConfigException($"There's already a persona with the id '{incoming.Id}'. Nothing was changed.");
		}
		if (File.Exists(Registry.Resolve(Path.Combine(incoming.Id, "persona.json"))))
		{
			throw new PersonaConfigException($"A folder for '{incoming.Id}' already exists in {Registry.Root}. Nothing was changed.");
		}
		Registry.Register(incoming, incoming.Id, makeActive: true);
		return incoming;
	}

	/// <summary>Whether this persona has been set up (has her private memory), so she can chat or join a scene.</summary>
	public bool IsSetUp(string personaId)
	{
		try
		{
			return File.Exists(Registry.Resolve(Registry.Entry(personaId).MemoryPath));
		}
		catch (PersonaConfigException)
		{
			return false;
		}
	}

	public float[] Embed(string text) => embedder.Embed(text);

	/// <summary>What a persona is about (her name and personality sheet), as an embedding for "who would react to this".</summary>
	public float[] Interests(CompanionRuntime runtime)
	{
		lock (gate)
		{
			string key = runtime.Config.Id + "|" + runtime.Config.PersonalitySheet.GetHashCode();
			if (!interests.TryGetValue(key, out float[]? v))
			{
				string sheet = runtime.Config.PersonalitySheet;
				v = embedder.Embed(runtime.Config.Name + ". " + (sheet.Length > 2000 ? sheet.Substring(0, 2000) : sheet));
				interests[key] = v;
			}
			return v;
		}
	}

	/// <summary>A short look others in a scene can see: a few traits, her identity anchor and what she's wearing.</summary>
	public static string Looks(CompanionRuntime runtime)
	{
		AppearanceCard card = runtime.Store.ReadAppearance();
		List<string> parts = card.Traits.Take(3).Select((TraitEntry t) => t.Name + ": " + (t.Text.Length > 120 ? t.Text.Substring(0, 117) + "..." : t.Text)).ToList();
		parts.Add(card.IdentityAnchor);
		parts.Add("wearing " + ContextInjector.WardrobeText(card.Wardrobe.Wardrobe));
		return string.Join("; ", parts);
	}

	public CompanionRuntime Open(string personaId)
	{
		lock (gate)
		{
			if (!open.TryGetValue(personaId, out CompanionRuntime runtime))
			{
				runtime = CompanionRuntime.Open(Registry, personaId, embedder);
				open[personaId] = runtime;
			}
			return runtime;
		}
	}

	/// <summary>Drops a cached persona after her config or store changed, so the next turn re-opens and re-checks her.</summary>
	public void Forget(string personaId)
	{
		lock (gate)
		{
			open.Remove(personaId);
		}
	}

	public string ConfigPath(string personaId)
	{
		return Registry.Resolve(Registry.Entry(personaId).ConfigPath);
	}

	public string Folder(string personaId)
	{
		return Path.GetDirectoryName(ConfigPath(personaId))!;
	}

	/// <summary>Runs a /tune command for a persona. Config changes take effect on her next turn.</summary>
	public TuningResult Tune(string personaId, string command)
	{
		PersonaStore store = Registry.OpenStore(personaId);
		TuningResult result = PersonaTuning.Run(command, ConfigPath(personaId), store, DateTimeOffset.Now);
		Forget(personaId);
		return result;
	}

	/// <summary>
	/// Replaces a persona's config with a file the user picked. Allowed only before she is set up, so her
	/// identity can't be swapped out from under an existing memory. Keeps a backup of the old file.
	/// </summary>
	public void ImportConfig(string personaId, string file)
	{
		PersonaConfig incoming = PersonaConfig.Load(file);
		if (incoming.Id != personaId)
		{
			throw new PersonaConfigException($"That file is for '{incoming.Id}', not '{personaId}'.");
		}
		if (File.Exists(Registry.Resolve(Registry.Entry(personaId).MemoryPath)))
		{
			throw new PersonaConfigException($"{incoming.Name} is already set up, so her file can't be swapped. Edit her settings instead.");
		}
		string path = ConfigPath(personaId);
		if (File.Exists(path))
		{
			File.Copy(path, path + ".before-import", overwrite: true);
		}
		incoming.Save(path);
		Forget(personaId);
	}

	private readonly Dictionary<string, ImageSource?> avatars = new Dictionary<string, ImageSource?>();

	public const int ProfilePictureSize = 256;

	public string ProfilePicturePath(string personaId)
	{
		return Path.Combine(Folder(personaId), "profile.png");
	}

	/// <summary>Her profile picture (a small round avatar in the chat), or null when none is set.</summary>
	public ImageSource? ProfilePicture(string personaId)
	{
		lock (gate)
		{
			if (avatars.TryGetValue(personaId, out ImageSource? cached))
			{
				return cached;
			}
		}
		ImageSource? image = null;
		try
		{
			string path = ProfilePicturePath(personaId);
			if (File.Exists(path))
			{
				BitmapImage bitmap = new BitmapImage();
				bitmap.BeginInit();
				bitmap.StreamSource = new MemoryStream(File.ReadAllBytes(path));
				bitmap.CacheOption = BitmapCacheOption.OnLoad;
				bitmap.EndInit();
				bitmap.Freeze();
				image = bitmap;
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is PersonaConfigException || ex is ArgumentException || ex is FileFormatException)
		{
		}
		lock (gate)
		{
			avatars[personaId] = image;
		}
		return image;
	}

	/// <summary>Saves a picture as her profile picture: turned upright, cropped to a centred square, 256 px.</summary>
	public void SetProfilePicture(string personaId, string file)
	{
		BitmapFrame frame = BitmapFrame.Create(new MemoryStream(File.ReadAllBytes(file)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
		BitmapSource upright = Attachments.PrepareForVision(frame);
		int side = Math.Min(upright.PixelWidth, upright.PixelHeight);
		BitmapSource square = new CroppedBitmap(upright, new Int32Rect((upright.PixelWidth - side) / 2, (upright.PixelHeight - side) / 2, side, side));
		double scale = (double)ProfilePictureSize / side;
		BitmapSource small = new TransformedBitmap(square, new ScaleTransform(scale, scale));
		PngBitmapEncoder encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(small));
		string path = ProfilePicturePath(personaId);
		string temp = path + ".tmp";
		using (FileStream stream = File.Create(temp))
		{
			encoder.Save(stream);
		}
		File.Move(temp, path, overwrite: true);
		lock (gate)
		{
			avatars.Remove(personaId);
		}
	}

	public void RemoveProfilePicture(string personaId)
	{
		string path = ProfilePicturePath(personaId);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		lock (gate)
		{
			avatars.Remove(personaId);
		}
	}

	// ---- About you: the user's own profile, shared by every companion ---------------------------------

	private string AboutYouPath => Path.Combine(Registry.Root, "about-you.json");

	private string AboutYouPicturePath => Path.Combine(Registry.Root, "about-you-picture.json");

	/// <summary>Your profile, or an empty one. A damaged file is reported, never silently replaced.</summary>
	public AboutYou LoadAboutYou()
	{
		if (!File.Exists(AboutYouPath))
		{
			return new AboutYou();
		}
		try
		{
			return System.Text.Json.JsonSerializer.Deserialize<AboutYou>(File.ReadAllText(AboutYouPath), PersonaConfig.Json) ?? new AboutYou();
		}
		catch (System.Text.Json.JsonException ex)
		{
			throw new PersonaConfigException("Your \"About you\" file (" + AboutYouPath + ") isn't valid: " + ex.Message, ex);
		}
	}

	public void SaveAboutYou(AboutYou about)
	{
		Directory.CreateDirectory(Registry.Root);
		string tmp = AboutYouPath + ".tmp";
		File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(about, PersonaConfig.Json));
		File.Move(tmp, AboutYouPath, overwrite: true);
	}

	/// <summary>Your picture, prepared for the vision model (turned upright and sized), or null when none is set.</summary>
	public Attachment? AboutYouPicture()
	{
		if (!File.Exists(AboutYouPicturePath))
		{
			return null;
		}
		try
		{
			return System.Text.Json.JsonSerializer.Deserialize<Attachment>(File.ReadAllText(AboutYouPicturePath), PersonaConfig.Json);
		}
		catch (System.Text.Json.JsonException ex)
		{
			throw new PersonaConfigException("Your \"About you\" picture (" + AboutYouPicturePath + ") couldn't be read: " + ex.Message, ex);
		}
	}

	public void SetAboutYouPicture(string file)
	{
		Attachment picture = Attachments.Read(file);
		if (picture.Kind != "image")
		{
			throw new ArgumentException("That file isn't a picture.");
		}
		picture.FromProfile = true;
		if (string.IsNullOrEmpty(picture.ContentDigest))
		{
			picture.ContentDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(picture.Base64)));
		}
		Directory.CreateDirectory(Registry.Root);
		string tmp = AboutYouPicturePath + ".tmp";
		File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(picture, PersonaConfig.Json));
		File.Move(tmp, AboutYouPicturePath, overwrite: true);
	}

	public void RemoveAboutYouPicture()
	{
		if (File.Exists(AboutYouPicturePath))
		{
			File.Delete(AboutYouPicturePath);
		}
	}

	public static Summarizer SummarizerFor(CompanionRuntime runtime, Engine engine, int contextTokens)
	{
		return new Summarizer(runtime.Config, runtime.Store, new EngineGenerator(engine))
		{
			MaxSourceChars = Math.Clamp((contextTokens - 6000) * 3, 4000, Summarizer.DefaultMaxSourceChars)
		};
	}

	public void Dispose()
	{
		embedder.Dispose();
	}
}

/// <summary>Your own profile ("About you"). Every companion reads it; it's yours, not part of any persona's memory.</summary>
public sealed class AboutYou
{
	public const int MaxFieldChars = 600;

	public string Nickname { get; set; } = "";
	public string Age { get; set; } = "";
	public string Appearance { get; set; } = "";
	public string Background { get; set; } = "";
	public string Other { get; set; } = "";

	public string Render()
	{
		List<string> lines = new List<string>();
		void Add(string label, string value)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				lines.Add(label + ": " + value.Trim());
			}
		}
		Add("What to call them", Nickname);
		Add("Age", Age);
		Add("Appearance", Appearance);
		Add("Background", Background);
		Add("Also", Other);
		return string.Join("\n", lines);
	}
}
