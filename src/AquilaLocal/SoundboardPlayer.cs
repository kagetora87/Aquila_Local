using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace AquilaLocal;

/// <summary>Plays soundboard sounds one after another (on the UI thread, like the rest of the app's audio).</summary>
public sealed class SoundboardPlayer
{
	/// <summary>No soundboard sound plays longer than this.</summary>
	public static readonly TimeSpan MaxLength = TimeSpan.FromSeconds(45);

	private MediaPlayer? player;

	private CancellationTokenSource? current;

	public bool Playing => current != null;

	/// <summary>Plays the files in order. Starting a new set stops the one before.</summary>
	public async Task PlayAsync(IReadOnlyList<string> files, double volume, CancellationToken ct = default(CancellationToken))
	{
		if (files.Count == 0)
		{
			return;
		}
		Stop();
		CancellationTokenSource mine = CancellationTokenSource.CreateLinkedTokenSource(ct);
		current = mine;
		try
		{
			foreach (string file in files)
			{
				if (mine.IsCancellationRequested)
				{
					break;
				}
				await PlayOneAsync(file, volume, mine.Token);
			}
		}
		finally
		{
			if (current == mine)
			{
				current = null;
			}
			mine.Dispose();
		}
	}

	private async Task PlayOneAsync(string file, double volume, CancellationToken ct)
	{
		MediaPlayer media = new MediaPlayer { Volume = Math.Clamp(volume, 0.0, 1.0) };
		player = media;
		TaskCompletionSource done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		string? problem = null;
		media.MediaEnded += delegate
		{
			done.TrySetResult();
		};
		media.MediaFailed += delegate(object? sender, ExceptionEventArgs e)
		{
			problem = e.ErrorException?.Message ?? "it couldn't be played";
			done.TrySetResult();
		};
		using (ct.Register(() => done.TrySetResult()))
		{
			media.Open(new Uri(file, UriKind.Absolute));
			media.Play();
			await Task.WhenAny(done.Task, Task.Delay(MaxLength, CancellationToken.None));
		}
		media.Stop();
		media.Close();
		if (player == media)
		{
			player = null;
		}
		if (problem != null)
		{
			throw new System.IO.IOException("The sound \"" + System.IO.Path.GetFileName(file) + "\" couldn't be played: " + problem);
		}
	}

	public void Stop()
	{
		current?.Cancel();
		player?.Stop();
	}
}
