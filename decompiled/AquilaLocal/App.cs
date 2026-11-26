using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AquilaLocal;

public class App : Application
{
	[Serializable]
	[CompilerGenerated]
	private sealed class _003C_003Ec
	{
		public static readonly _003C_003Ec _003C_003E9 = new _003C_003Ec();

		public static DispatcherUnhandledExceptionEventHandler _003C_003E9__0_0;

		internal void _003COnStartup_003Eb__0_0(object _, DispatcherUnhandledExceptionEventArgs args)
		{
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			MessageBox.Show(args.Exception.Message, "Aquila Local", (MessageBoxButton)0, (MessageBoxImage)16);
			args.Handled = true;
		}
	}

	private bool _contentLoaded;

	protected override void OnStartup(StartupEventArgs e)
	{
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected O, but got Unknown
		Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "1", EnvironmentVariableTarget.Process);
		((Application)this).OnStartup(e);
		object obj = _003C_003Ec._003C_003E9__0_0;
		if (obj == null)
		{
			DispatcherUnhandledExceptionEventHandler val = delegate(object _, DispatcherUnhandledExceptionEventArgs args)
			{
				//IL_0013: Unknown result type (might be due to invalid IL or missing references)
				MessageBox.Show(args.Exception.Message, "Aquila Local", (MessageBoxButton)0, (MessageBoxImage)16);
				args.Handled = true;
			};
			_003C_003Ec._003C_003E9__0_0 = val;
			obj = (object)val;
		}
		((Application)this).DispatcherUnhandledException += (DispatcherUnhandledExceptionEventHandler)obj;
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
		((Window)new MainWindow(dataRoot, (num2 >= 0 && num2 + 1 < e.Args.Length) ? Path.GetFullPath(e.Args[num2 + 1]) : null)).Show();
	}

	private async Task VerifyAndExit(string[] args)
	{
		string directory = ((args.Length > 1) ? args[1] : Path.Combine(AppContext.BaseDirectory, "verification"));
		Directory.CreateDirectory(directory);
		try
		{
			await Verification.RunAsync(directory, (args.Length > 2) ? args[2] : null);
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyFeaturesAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await FeatureVerification.RunAsync(directory, Path.GetFullPath(args[2]), args.Contains("--gpu"));
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyMemoryAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await MemoryVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyEmbeddingsAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await EmbeddingVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyThinkingAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await ReasoningVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyPersonalitiesAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await PersonalityVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyAvatarAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await AvatarVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyConversationsAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await ConversationVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyAvatarLibraryAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await AvatarLibraryVerification.RunAsync(directory);
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyWebAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await WebVerification.RunAsync(directory, Path.GetFullPath(args[2]));
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyInferenceAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await InferenceVerification.RunAsync(directory, Path.GetFullPath(args[2]), (args.Length > 3) ? Path.GetFullPath(args[3]) : null);
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyDeliveryAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await DeliveryVerification.RunAsync(directory, Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyKittenAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await KittenVerification.RunAsync(directory, Path.GetFullPath(args[2]), Path.GetFullPath(args[3]), args.Contains("--gpu"));
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	private async Task VerifyVoiceAndExit(string[] args)
	{
		string directory = Path.GetFullPath(args[1]);
		Directory.CreateDirectory(directory);
		try
		{
			await VoiceVerification.RunAsync(directory, Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
			((Application)this).Shutdown(0);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
			((Application)this).Shutdown(1);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.31.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri uri = new Uri("/Aquila Local;component/app.xaml", UriKind.Relative);
			Application.LoadComponent((object)this, uri);
		}
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.31.0")]
	public static void Main()
	{
		App app = new App();
		app.InitializeComponent();
		((Application)app).Run();
	}
}
