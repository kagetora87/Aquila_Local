using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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

	public CompanionHost(string dataRoot, string? embeddingsDirectory = null)
	{
		Registry = new PersonaRegistry(Path.Combine(dataRoot, "personas"));
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
