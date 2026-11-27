using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AquilaLocal;

public partial class App : Application
{

	/// <summary>Writes the whole error (with where it happened) to Data\errors.log, so it can be fixed. Returns a line for the message.</summary>
	private static string LogError(Exception ex)
	{
		try
		{
			string folder = Path.Combine(AppContext.BaseDirectory, "Data");
			Directory.CreateDirectory(folder);
			string path = Path.Combine(folder, "errors.log");
			if (File.Exists(path) && new FileInfo(path).Length > 2_000_000)
			{
				File.Delete(path);
			}
			File.AppendAllText(path, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
			return Environment.NewLine + Environment.NewLine + "The details were saved to Data\\errors.log.";
		}
		catch (Exception logEx) when (logEx is IOException || logEx is UnauthorizedAccessException)
		{
			return "";
		}
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "1", EnvironmentVariableTarget.Process);
		base.OnStartup(e);
		Motion.Initialize(this);
		base.DispatcherUnhandledException += delegate(object _, DispatcherUnhandledExceptionEventArgs args)
		{
			string where = LogError(args.Exception);
			AquilaDialog.Alert(Current?.MainWindow, "Something went wrong", args.Exception.Message + where);
			args.Handled = true;
		};
		if (e.Args.Contains("--verify"))
		{
			VerifyAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-features"))
		{
			VerifyFeaturesAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-memory"))
		{
			VerifyMemoryAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-embeddings"))
		{
			VerifyEmbeddingsAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-thinking"))
		{
			VerifyThinkingAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-personalities"))
		{
			VerifyPersonalitiesAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-avatar"))
		{
			VerifyAvatarAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-web"))
		{
			VerifyWebAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-voice"))
		{
			VerifyVoiceAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-kitten"))
		{
			VerifyKittenAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-delivery"))
		{
			VerifyDeliveryAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-inference"))
		{
			VerifyInferenceAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-avatar-library"))
		{
			VerifyAvatarLibraryAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--verify-conversations"))
		{
			VerifyConversationsAndExit(e.Args);
			return;
		}
		if (e.Args.Contains("--avatar-lifetime-fixture"))
		{
			AvatarVerification.LifetimeFixture(Path.GetFullPath(e.Args[1]));
			return;
		}
		int num = Array.IndexOf(e.Args, "--data-dir");
		string dataRoot = ((num >= 0 && num + 1 < e.Args.Length) ? Path.GetFullPath(e.Args[num + 1]) : null);
		int num2 = Array.IndexOf(e.Args, "--speech-assets");
		new MainWindow(dataRoot, (num2 >= 0 && num2 + 1 < e.Args.Length) ? Path.GetFullPath(e.Args[num2 + 1]) : null).Show();
	}

	private async Task VerifyAndExit(string[] args)
	{
		string directory = ((args.Length > 1) ? args[1] : Path.Combine(AppContext.BaseDirectory, "verification"));
		Directory.CreateDirectory(directory);
		try
		{
			await Verification.RunAsync(directory, (args.Length > 2) ? args[2] : null);
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyFeaturesAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await FeatureVerification.RunAsync(directory, Path.GetFullPath(args[2]), args.Contains("--gpu"));
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyMemoryAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await MemoryVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyEmbeddingsAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await EmbeddingVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyThinkingAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await ReasoningVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyPersonalitiesAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await PersonalityVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyAvatarAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await AvatarVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyConversationsAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await ConversationVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyAvatarLibraryAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await AvatarLibraryVerification.RunAsync(directory);
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyWebAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await WebVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyInferenceAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await InferenceVerification.RunAsync(directory, Path.GetFullPath(args[2]), (args.Length > 3) ? Path.GetFullPath(args[3]) : null);
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyDeliveryAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await DeliveryVerification.RunAsync(directory, Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyKittenAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await KittenVerification.RunAsync(directory, Path.GetFullPath(args[2]), Path.GetFullPath(args[3]), args.Contains("--gpu"));
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}

	private async Task VerifyVoiceAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await VoiceVerification.RunAsync(directory, Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
			Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			Shutdown(1);
		}
	}
}
