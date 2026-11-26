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

	/// <summary>Draws one picture into job.Output. Progress reports (step, of). Throws with the engine's own words on failure.</summary>
	public async Task DrawAsync(string model, PictureJob job, Action<int, int>? progress, CancellationToken ct)
	{
		PictureModelFiles files = Kit.Resolve(model);
		List<string> args = PictureCommand.Arguments(files, job);
		await one.WaitAsync(ct);
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(job.Output)!);
			ProcessStartInfo info = new ProcessStartInfo
			{
				FileName = files.Engine,
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
			limit.CancelAfter(Limit);
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
				throw new IOException($"The picture took longer than {Limit.TotalMinutes:0} minutes and was stopped.");
			}
			if (process.ExitCode != 0 || !File.Exists(job.Output))
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
