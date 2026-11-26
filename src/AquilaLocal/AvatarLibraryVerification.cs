using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AquilaAvatarProtocol;

namespace AquilaLocal;

public static class AvatarLibraryVerification
{
	public static async Task RunAsync(string folder)
	{
		List<string> checks = new List<string>();
		string library = Path.Combine(folder, "Custom avatars with spaces");
		Directory.CreateDirectory(library);
		string custom = Path.Combine(library, "Mira custom.vrm");
		string guide = AvatarCatalog.Resolve("builtin:guide");
		File.Copy(AvatarCatalog.DefaultPath, custom);
		File.WriteAllText(Path.Combine(library, "ignore.txt"), "Not an avatar");
		AvatarModelInfo mira = AvatarCatalog.Inspect("");
		AvatarModelInfo robot = AvatarCatalog.Inspect("builtin:guide");
		Check(mira.Name == "Mira" && mira.Authors.Contains("rengasato") && mira.Bytes == 17133756, "Bundled default resolves to the exact downloaded Mira model and author");
		Check(mira.Blink && mira.Mouth && robot.Blink && robot.Mouth, "Mira and the preserved guide advertise blink and mouth presets");
		Check(AvatarCatalog.KeyFor(AvatarCatalog.DefaultPath) == "" && AvatarCatalog.KeyFor(guide) == "builtin:guide", "Bundled model selections use portable identities");
		List<AvatarChoice> choices = AvatarCatalog.List(library, custom);
		Check(choices.Count == 3 && choices.Any((AvatarChoice c) => c.Key == custom) && choices.All((AvatarChoice c) => !c.Key.EndsWith(".txt")), "Folder listing includes local VRM files and both built-ins without unrelated files");
		Check(AvatarCatalog.SamePath(AvatarCatalog.Resolve(custom), custom), "External model paths with spaces resolve exactly");
		string savedMissing = Path.Combine(library, "missing.vrm");
		Check(AvatarCatalog.List(library, savedMissing).Any((AvatarChoice c) => c.Key == savedMissing), "A missing saved selection remains visible for correction instead of silently changing characters");
		Storage storage = new Storage(Path.Combine(folder, "data"));
		Settings settings = new Settings
		{
			AvatarFolder = library,
			AvatarModelPath = custom
		};
		storage.SaveSettings(settings);
		Settings settings2 = storage.LoadSettings();
		Check(settings2.AvatarFolder == library && settings2.AvatarModelPath == custom && !settings2.AvatarEnabled, "Folder/model selection persists independently of enabling the avatar");
		Settings settings3 = new Settings
		{
			AvatarModelPath = new string('x', 2049)
		};
		bool rejected = false;
		try
		{
			settings3.Validate();
		}
		catch (ArgumentException)
		{
			rejected = true;
		}
		Check(rejected, "Unbounded avatar paths are rejected by settings validation");
		string bad = Path.Combine(library, "invalid.vrm");
		File.WriteAllText(bad, "This is not a model.");
		rejected = false;
		try
		{
			AvatarModelFile.Inspect(bad);
		}
		catch (ArgumentException)
		{
			rejected = true;
		}
		Check(rejected, "Invalid binary files are rejected before invoking Godot");
		string text = Path.Combine(folder, "old.vrm");
		MetadataFile(text, "{\"extensions\":{\"VRMC_vrm\":{\"specVersion\":\"0.0\"}}}");
		rejected = false;
		try
		{
			AvatarModelFile.Inspect(text);
		}
		catch (ArgumentException ex3)
		{
			rejected = ex3.Message.Contains("VRM 1.0");
		}
		Check(rejected, "VRM 0.x gets an explicit re-export-as-1.0 explanation");
		string text2 = Path.Combine(folder, "external-texture.vrm");
		MetadataFile(text2, "{\"images\":[{\"uri\":\"https://example.invalid/texture.png\"}],\"extensions\":{\"VRMC_vrm\":{\"specVersion\":\"1.0\",\"humanoid\":{\"humanBones\":{\"hips\":{},\"head\":{}}}}}}");
		rejected = false;
		try
		{
			AvatarModelFile.Inspect(text2);
		}
		catch (ArgumentException ex4)
		{
			rejected = ex4.Message.Contains("embedded textures");
		}
		Check(rejected, "External texture paths and URLs are rejected without any fetch");
		AvatarHost host = new AvatarHost(storage.Root);
		try
		{
			await host.StartAsync(new AvatarPresentation(), CancellationToken.None, headless: true);
			Check(host.Connected && host.LastAppliedDetail == "model ready" && AvatarCatalog.SamePath(host.ModelPath, AvatarCatalog.DefaultPath), "Exported child loads the selected default Mira independently");
			int pid = host.ProcessId.Value;
			rejected = false;
			try
			{
				await host.StartAsync(new AvatarPresentation(), CancellationToken.None, headless: true, bad);
			}
			catch (ArgumentException)
			{
				rejected = true;
			}
			Check(rejected && host.ProcessId == pid && host.Connected, "An invalid selection leaves the currently running avatar intact");
			rejected = false;
			try
			{
				await host.StartAsync(new AvatarPresentation(), CancellationToken.None, headless: true, savedMissing);
			}
			catch (FileNotFoundException)
			{
				rejected = true;
			}
			Check(rejected && host.ProcessId == pid && host.Connected, "A missing model leaves the currently running avatar intact");
			await host.StartAsync(new AvatarPresentation(), CancellationToken.None, headless: true, "builtin:guide");
			Check(host.ProcessId != pid && host.Connected && AvatarCatalog.SamePath(host.ModelPath, guide), "Switching back to the guide restarts only the avatar process");
			await host.StartAsync(new AvatarPresentation(), CancellationToken.None, headless: true, custom);
			Check(host.Connected && AvatarCatalog.SamePath(host.ModelPath, custom), "A model selected from an external folder loads in the exported child");
			string[] states = AvatarMessage.States;
			foreach (string state in states)
			{
				long sequence = host.LastAppliedSequence;
				await host.SetStateAsync(state, preview: true, (state == "speaking") ? "smile" : "none");
				await Until(() => host.LastAppliedSequence > sequence && host.LastAppliedState == state);
			}
			Check(ok: true, "All five state commands apply with the folder-loaded Mira model");
			await host.StopAsync();
			Check(!host.Running && host.Port == 0, "Stopping a folder-loaded model releases the child and listener");
			string text3 = File.ReadAllText(Path.Combine(storage.Root, "logs", "avatar.log"));
			Check(text3.Contains("name=Mira") && text3.Contains("name=Aquila") && !text3.Contains("ERROR:") && !text3.Contains("SCRIPT ERROR"), "Mira and guide import without runtime errors or skin/spring failures");
			Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			MainWindow mainWindow = new MainWindow(storage.Root);
			mainWindow.Show();
			TaskCompletionSource closed = new TaskCompletionSource();
			mainWindow.Closed += delegate
			{
				closed.TrySetResult();
			};
			mainWindow.Close();
			await closed.Task.WaitAsync(TimeSpan.FromSeconds(8.0));
			settings2 = storage.LoadSettings();
			Check(settings2.AvatarFolder == library && settings2.AvatarModelPath == custom, "Normal main-window settings saves preserve the chosen avatar folder and file");
			File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
			{
				checks = checks.Count,
				mira = mira,
				robot = robot,
				folderChoices = choices,
				exportedRendererVerified = true
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		finally
		{
			if (host != null)
			{
				await host.DisposeAsync();
			}
		}
		void Check(bool ok, string label)
		{
			if (!ok)
			{
				throw new Exception("Failed: " + label);
			}
			checks.Add("PASS " + label);
			File.WriteAllLines(Path.Combine(folder, "checks.txt"), checks);
		}
		static void MetadataFile(string file, string json)
		{
			byte[] array = Encoding.UTF8.GetBytes(json);
			int num2 = array.Length;
			Array.Resize(ref array, (array.Length + 3) & -4);
			for (int i = num2; i < array.Length; i++)
			{
				array[i] = 32;
			}
			using BinaryWriter binaryWriter = new BinaryWriter(File.Create(file));
			binaryWriter.Write(1179937895u);
			binaryWriter.Write(2u);
			binaryWriter.Write((uint)(20 + array.Length));
			binaryWriter.Write((uint)array.Length);
			binaryWriter.Write(1313821514u);
			binaryWriter.Write(array);
		}
		static async Task Until(Func<bool> condition)
		{
			for (int i = 0; i < 300; i++)
			{
				if (condition())
				{
					return;
				}
				await Task.Delay(40);
			}
			throw new TimeoutException("Avatar library state did not settle.");
		}
	}
}
