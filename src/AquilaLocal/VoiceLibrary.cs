using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AquilaLocal;

public sealed class VoiceLibrary
{
	private sealed record Profile(string Name, string Transcript);

	private const int Rate = 24000;

	private const int MaxBytes = 25165824;

	private const string SampleFile = "reference.wav";

	private const string ProfileFile = "profile.json";

	private readonly string root;

	public VoiceLibrary(string dataRoot)
	{
		root = Path.GetFullPath(Path.Combine(dataRoot, "voice-samples"));
	}

	public IReadOnlyList<VoiceReference> List()
	{
		if (!Directory.Exists(root))
		{
			return Array.Empty<VoiceReference>();
		}
		return (from path in Directory.EnumerateDirectories(root)
			select Find(Path.GetFileName(path))).OfType<VoiceReference>().OrderBy<VoiceReference, string>((VoiceReference voice) => voice.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
	}

	public VoiceReference? Find(string id)
	{
		if (!Guid.TryParseExact(id, "N", out var result))
		{
			return null;
		}
		string path = Path.Combine(root, result.ToString("N"));
		string text = Path.Combine(path, "profile.json");
		string text2 = Path.Combine(path, "reference.wav");
		if (!File.Exists(text) || !File.Exists(text2))
		{
			return null;
		}
		try
		{
			if (new FileInfo(text).Length > 16000)
			{
				return null;
			}
			Profile profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(text));
			if ((object)profile == null)
			{
				return null;
			}
			ValidateMetadata(profile.Name, profile.Transcript);
			return new VoiceReference(result.ToString("N"), profile.Name, profile.Transcript, text2);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	public VoiceReference Prepare(string file, string name, string transcript)
	{
		name = name.Trim();
		transcript = transcript.Trim();
		ValidateMetadata(name, transcript);
		if (!File.Exists(file) || !Path.GetExtension(file).Equals(".wav", StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length > 25165824)
		{
			throw new ArgumentException("Choose a WAV file up to 24 MB.");
		}
		using WaveFileReader waveFileReader = new WaveFileReader(file);
		double totalSeconds = waveFileReader.TotalTime.TotalSeconds;
		bool flag = ((totalSeconds < 5.0 || totalSeconds > 30.0) ? true : false);
		bool flag2 = flag;
		if (!flag2)
		{
			int channels = waveFileReader.WaveFormat.Channels;
			bool flag3 = ((channels < 1 || channels > 2) ? true : false);
			flag2 = flag3;
		}
		bool flag4 = flag2;
		if (!flag4)
		{
			int channels = waveFileReader.WaveFormat.SampleRate;
			bool flag3 = ((channels < 8000 || channels > 96000) ? true : false);
			flag4 = flag3;
		}
		if (flag4)
		{
			throw new ArgumentException("Choose 5 to 30 seconds of mono or stereo speech recorded at 8–96 kHz.");
		}
		ISampleProvider sampleProvider = waveFileReader.ToSampleProvider();
		if (sampleProvider.WaveFormat.Channels == 2)
		{
			sampleProvider = new StereoToMonoSampleProvider(sampleProvider);
		}
		if (sampleProvider.WaveFormat.SampleRate != 24000)
		{
			sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 24000);
		}
		float[] array = new float[720001];
		int num;
		int i;
		for (i = 0; i < array.Length; i += num)
		{
			num = sampleProvider.Read(array, i, array.Length - i);
			if (num == 0)
			{
				break;
			}
		}
		i = Math.Min(i, 720000);
		if (i < 120000 || !array.AsSpan(0, i).ContainsAnyExcept(0f))
		{
			throw new ArgumentException("The recording needs at least five seconds of audible speech.");
		}
		double num2 = 0.0;
		for (int j = 0; j < i; j++)
		{
			if (!float.IsFinite(array[j]))
			{
				throw new ArgumentException("The WAV contains invalid audio samples.");
			}
			num2 += (double)(array[j] * array[j]);
		}
		if (Math.Sqrt(num2 / (double)i) < 0.001)
		{
			throw new ArgumentException("The recording is too quiet to use as a reference.");
		}
		string id = Guid.NewGuid().ToString("N");
		string text = DraftFolder(id);
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, "reference.wav");
		try
		{
			using WaveFileWriter waveFileWriter = new WaveFileWriter(text2, new WaveFormat(24000, 16, 1));
			waveFileWriter.WriteSamples(array, 0, i);
			return new VoiceReference(id, name, transcript, text2);
		}
		catch
		{
			RemoveFiles(text);
			throw;
		}
	}

	public VoiceReference Save(VoiceReference reference)
	{
		ValidateMetadata(reference.Name, reference.Transcript);
		string text = DraftFolder(reference.Id);
		if (!File.Exists(Path.Combine(text, "reference.wav")))
		{
			throw new IOException("The voice sample must be imported again.");
		}
		File.WriteAllText(Path.Combine(text, "profile.json"), JsonSerializer.Serialize(new Profile(reference.Name, reference.Transcript)));
		string text2 = Path.Combine(root, reference.Id);
		Directory.Move(text, text2);
		return reference with
		{
			AudioFile = Path.Combine(text2, "reference.wav")
		};
	}

	public void Discard(VoiceReference draft)
	{
		RemoveFiles(DraftFolder(draft.Id));
	}

	public void Delete(string id)
	{
		if (!Guid.TryParseExact(id, "N", out var result))
		{
			throw new ArgumentException("Invalid voice profile.");
		}
		RemoveFiles(Path.Combine(root, result.ToString("N")));
	}

	private string DraftFolder(string id)
	{
		if (!Guid.TryParseExact(id, "N", out var result))
		{
			throw new ArgumentException("Invalid voice profile.");
		}
		return Path.Combine(root, ".draft-" + result.ToString("N"));
	}

	private static void RemoveFiles(string folder)
	{
		if (Directory.Exists(folder))
		{
			if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
			{
				throw new IOException("The voice folder is a link. Remove it manually.");
			}
			File.Delete(Path.Combine(folder, "profile.json"));
			File.Delete(Path.Combine(folder, "reference.wav"));
			Directory.Delete(folder);
		}
	}

	private static void ValidateMetadata(string name, string transcript)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Length > 60 || name.Any(char.IsControl))
		{
			throw new ArgumentException("Enter a name up to 60 characters for this voice.");
		}
		if (string.IsNullOrWhiteSpace(transcript) || transcript.Length > 1000)
		{
			throw new ArgumentException("Enter the words spoken in the reference clip, up to 1,000 characters.");
		}
	}
}
