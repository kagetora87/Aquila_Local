using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
				lock (tail)
				{
					tail.Enqueue(text.Trim());
					while (tail.Count > 12)
					{
						tail.Dequeue();
					}
				}
				(int Step, int Of)? p = PictureCommand.Progress(text);
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
				string said;
				lock (tail)
				{
					said = string.Join(" | ", tail);
				}
				throw new IOException("The image engine failed (exit " + process.ExitCode + "): " + said.Truncate(600));
			}
		}
		finally
		{
			one.Release();
		}
	}
}
