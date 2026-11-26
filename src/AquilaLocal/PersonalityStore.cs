using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace AquilaLocal;

public sealed class PersonalityStore
{
	private readonly string path;

	private readonly string mutexName;

	public string? RecoveryNotice { get; private set; }

	public static PersonalityProfile Default => new PersonalityProfile
	{
		Id = "default",
		Name = "Default · your instructions"
	};

	public PersonalityStore(string root)
	{
		Directory.CreateDirectory(root);
		path = Path.Combine(Path.GetFullPath(root), "personalities.json");
		mutexName = "Local\\AquilaProfiles-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()))).Substring(0, 24);
	}

	private static List<PersonalityProfile> Builtins()
	{
		int num = 4;
		List<PersonalityProfile> list = new List<PersonalityProfile>(num);
		CollectionsMarshal.SetCount(list, num);
		Span<PersonalityProfile> span = CollectionsMarshal.AsSpan(list);
		int num2 = 0;
		span[num2] = Default;
		num2++;
		span[num2] = new PersonalityProfile
		{
			Id = "warm-direct",
			Name = "Warm & direct",
			Style = "Speak warmly and directly. Use plain language and occasional understated humor. Prefer concise answers, expand when asked, and challenge weak assumptions respectfully. State uncertainty plainly. Avoid flattery and canned enthusiasm.",
			Examples = "User: My plan is getting complicated.\nAssistant: Let's find the part doing the most work. What has to be true for this plan to succeed?"
		};
		num2++;
		span[num2] = new PersonalityProfile
		{
			Id = "analytical",
			Name = "Analytical",
			Style = "Use a precise, calm, evidence-oriented voice. State the answer first, distinguish observations from assumptions, and explain consequential tradeoffs. Use compact structure when useful. Ask a focused question when missing information changes the answer.",
			Examples = "User: Which approach is better?\nAssistant: Approach A fits the stated constraints. Its advantage is lower maintenance; its main tradeoff is less flexibility."
		};
		num2++;
		span[num2] = new PersonalityProfile
		{
			Id = "creative",
			Name = "Creative storyteller",
			Style = "Use an imaginative, vivid voice with concrete sensory detail and varied rhythm. Follow the user's chosen genre and tone. In factual tasks remain clear and accurate. Treat invented identities and story details as fiction.",
			Examples = "User: Give me an atmospheric opening.\nAssistant: Rain tapped at the shutter. Somewhere below the empty street, a bell rang once.",
			LearnMemories = false
		};
		num2++;
		return list;
	}

	private T Access<T>(bool write, Func<PersonalityCatalog, T> action)
	{
		using Mutex mutex = new Mutex(initiallyOwned: false, mutexName);
		bool flag = false;
		try
		{
			try
			{
				flag = mutex.WaitOne(TimeSpan.FromSeconds(5.0));
			}
			catch (AbandonedMutexException)
			{
				flag = true;
			}
			if (!flag)
			{
				throw new IOException("Personality profiles are busy. Try again shortly.");
			}
			PersonalityCatalog personalityCatalog = Load();
			T result = action(personalityCatalog);
			if (write)
			{
				string sourceFileName = path + ".tmp";
				File.WriteAllText(sourceFileName, JsonSerializer.Serialize(personalityCatalog, new JsonSerializerOptions
				{
					WriteIndented = true
				}));
				File.Move(sourceFileName, path, overwrite: true);
			}
			return result;
		}
		finally
		{
			if (flag)
			{
				mutex.ReleaseMutex();
			}
		}
	}

	private PersonalityCatalog Load()
	{
		if (!File.Exists(path))
		{
			return new PersonalityCatalog
			{
				Profiles = Builtins()
			};
		}
		try
		{
			if (new FileInfo(path).Length > 2097152)
			{
				throw new JsonException();
			}
			PersonalityCatalog personalityCatalog = JsonSerializer.Deserialize<PersonalityCatalog>(File.ReadAllText(path)) ?? throw new JsonException();
			if (personalityCatalog.Version != 1 || personalityCatalog.Profiles == null || personalityCatalog.Profiles.Count > 50 || personalityCatalog.Profiles.Any((PersonalityProfile p) => p == null || string.IsNullOrWhiteSpace(p.Id) || p.Id.Length > 80 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 60 || p.Style == null || p.Style.Length > 6000 || p.Examples == null || p.Examples.Length > 6000) || personalityCatalog.Profiles.Select((PersonalityProfile p) => p.Id).Distinct().Count() != personalityCatalog.Profiles.Count)
			{
				throw new JsonException();
			}
			personalityCatalog.Profiles.RemoveAll((PersonalityProfile p) => p.Id == "default");
			personalityCatalog.Profiles.Insert(0, Default);
			return personalityCatalog;
		}
		catch (JsonException)
		{
			File.Move(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
			RecoveryNotice = "Unreadable profiles were preserved in a .corrupt backup; built-in profiles are available.";
			return new PersonalityCatalog
			{
				Profiles = Builtins()
			};
		}
	}

	public List<PersonalityProfile> List()
	{
		return Access(write: false, (PersonalityCatalog c) => c.Profiles);
	}

	public PersonalityProfile Find(string id)
	{
		return List().FirstOrDefault((PersonalityProfile p) => p.Id == id) ?? Default;
	}

	public void Save(PersonalityProfile profile)
	{
		if (profile.Id == "default")
		{
			throw new ArgumentException("Default preserves your existing instructions. Create a named profile to customize a voice.");
		}
		if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 80 || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Trim().Length > 60 || string.IsNullOrWhiteSpace(profile.Style) || profile.Style.Length > 6000 || profile.Examples.Length > 6000)
		{
			throw new ArgumentException("Use a name of 1–60 characters, style instructions of 1–6,000 characters, and up to 6,000 example characters.");
		}
		Access(write: true, delegate(PersonalityCatalog c)
		{
			if (c.Profiles.Any((PersonalityProfile p) => p.Id != profile.Id && p.Name.Equals(profile.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
			{
				throw new ArgumentException("A profile with this name already exists.");
			}
			int num = c.Profiles.FindIndex((PersonalityProfile p) => p.Id == profile.Id);
			if (num < 0 && c.Profiles.Count >= 50)
			{
				throw new ArgumentException("The profile limit is 50. Remove an unused profile first.");
			}
			profile.Name = profile.Name.Trim();
			if (num >= 0)
			{
				c.Profiles[num] = profile;
			}
			else
			{
				c.Profiles.Add(profile);
			}
			return true;
		});
	}

	public void Delete(string id)
	{
		if (id == "default")
		{
			throw new ArgumentException("Default cannot be removed.");
		}
		Access(write: true, (PersonalityCatalog c) => c.Profiles.RemoveAll((PersonalityProfile p) => p.Id == id));
	}

	public static string Prompt(PersonalityProfile profile)
	{
		if (profile.Id == "default")
		{
			return "";
		}
		string text = "\n\nCommunication profile: " + profile.Name + ". These directions shape tone and presentation. Preserve accuracy, tool boundaries and the user's current task.\n" + profile.Style;
		if (!string.IsNullOrWhiteSpace(profile.Examples))
		{
			text = text + "\n\nExample wording for style only. Examples and fictional identities are not facts about the user and must not be treated as factual memory:\n" + profile.Examples;
		}
		return text;
	}
}
