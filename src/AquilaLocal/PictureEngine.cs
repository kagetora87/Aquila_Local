using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AquilaLocal.Companion.Pictures;

namespace AquilaLocal;

/// <summary>Runs the image engine (stable-diffusion.cpp's sd-cli) for one picture at a time.</summary>
public sealed class PictureEngine
{
	/// <summary>No picture may take longer than this (the best model took about 50 s on the laptop).</summary>
	public static readonly TimeSpan Limit = TimeSpan.FromMinutes(6);

	private readonly SemaphoreSlim one = new SemaphoreSlim(1, 1);

	public PictureEngine(string folder)
	{
		Kit = new PictureKit(folder);
	}

	public PictureKit Kit { get; }

	/// <summary>Wan 2.2 image-to-video, in the same image folder.</summary>
	public VideoKit Video => new VideoKit(Kit.Folder);

	/// <summary>No clip may take longer than this (full quality took 14.5 minutes on the laptop).</summary>
	public static readonly TimeSpan VideoLimit = TimeSpan.FromMinutes(30);

	/// <summary>Draws one picture into job.Output. Progress reports (step, of). Throws with the engine's own words on failure.</summary>
	public Task DrawAsync(string model, PictureJob job, Action<int, int>? progress, CancellationToken ct)
	{
		PictureModelFiles files = Kit.Resolve(model);
		return RunAsync(files.Engine, PictureCommand.Arguments(files, job), job.Output, Limit, "picture", progress, ct);
	}

	/// <summary>Makes one clip into job.Output (fast = the Lightning LoRAs when installed).</summary>
	public Task MakeVideoAsync(VideoJob job, bool fast, Action<int, int>? progress, CancellationToken ct)
	{
		VideoModelFiles files = Video.Resolve();
		return RunAsync(files.Engine, VideoCommand.Arguments(files, job, fast), job.Output, VideoLimit, "video", progress, ct);
	}

	/// <summary>"|#####     | 172/359 - 2.09GB/s" and similar: progress, not news.</summary>
	private static bool IsProgressBar(string line)
	{
		return line.StartsWith("|", StringComparison.Ordinal) || line.Contains("|###", StringComparison.Ordinal) || line.Contains("#|", StringComparison.Ordinal);
	}

	private async Task RunAsync(string engine, List<string> args, string output, TimeSpan limitAfter, string what, Action<int, int>? progress, CancellationToken ct)
	{
		await one.WaitAsync(ct);
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(output)!);
			ProcessStartInfo info = new ProcessStartInfo
			{
				FileName = engine,
				WorkingDirectory = Kit.EngineFolder,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			};
			foreach (string a in args)
			{
				info.ArgumentList.Add(a);
			}
			Queue<string> tail = new Queue<string>();
			void Line(string? text)
			{
				if (string.IsNullOrWhiteSpace(text))
				{
					return;
				}
				// The engine redraws progress bars in place (carriage returns and colour codes): keep only
				// what's left on screen, and leave the bars out of the log so the real error isn't buried.
				string shown = System.Text.RegularExpressions.Regex.Replace(text, "\u001b\\[[0-9;?]*[A-Za-z]", "");
				shown = shown.Split('\r', StringSplitOptions.RemoveEmptyEntries).Select((string x) => x.Trim()).LastOrDefault((string x) => x.Length > 0) ?? "";
				if (shown.Length > 0 && !IsProgressBar(shown))
				{
					lock (tail)
					{
						tail.Enqueue(shown);
						while (tail.Count > 300)
						{
							tail.Dequeue();
						}
					}
				}
				(int Step, int Of)? p = PictureCommand.Progress(shown);
				if (p.HasValue)
				{
					progress?.Invoke(p.Value.Step, p.Value.Of);
				}
			}
			using Process process = new Process { StartInfo = info, EnableRaisingEvents = true };
			process.OutputDataReceived += (object _, DataReceivedEventArgs e) => Line(e.Data);
			process.ErrorDataReceived += (object _, DataReceivedEventArgs e) => Line(e.Data);
			if (!process.Start())
			{
				throw new IOException("The image engine didn't start.");
			}
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
			limit.CancelAfter(limitAfter);
			try
			{
				await process.WaitForExitAsync(limit.Token);
			}
			catch (OperationCanceledException)
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch (InvalidOperationException)
				{
				}
				if (ct.IsCancellationRequested)
				{
					throw;
				}
				throw new IOException($"The {what} took longer than {limitAfter.TotalMinutes:0} minutes and was stopped.");
			}
			if (process.ExitCode != 0 || !File.Exists(output))
			{
				string[] lines;
				lock (tail)
				{
					lines = tail.ToArray();
				}
				// The whole log goes to a file; the message shows its last lines, where the engine says what went wrong.
				string log = Path.Combine(Path.GetDirectoryName(output)!, "engine-error.log");
				try
				{
					File.WriteAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {what}, exit {process.ExitCode}{Environment.NewLine}{Path.GetFileName(engine)} {string.Join(" ", args.Select((string x) => x.Contains(' ') ? "\"" + x + "\"" : x))}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{Environment.NewLine}");
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					log = "";
				}
				string said = string.Join(Environment.NewLine, lines.TakeLast(8));
				if (said.Length > 900)
				{
					said = "…" + said.Substring(said.Length - 900);
				}
				if (process.ExitCode == 0)
				{
					said = "it finished without saving the " + what + "." + Environment.NewLine + said;
				}
				throw new IOException("The image engine failed (exit " + process.ExitCode + "):" + Environment.NewLine + said + ((log.Length > 0) ? (Environment.NewLine + "Full log: " + log) : ""));
			}
		}
		finally
		{
			one.Release();
		}
	}
}
