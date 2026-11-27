using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AquilaLocal;

/// <summary>
/// Plays the picture-based avatar. A base state (idle, talking, thinking, listening) loops; a gesture
/// plays once on top of it and then the base state resumes. Missing states fall back to idle.
/// </summary>
public sealed class SpriteAvatarView : Grid
{
	private readonly Image image = new Image
	{
		Stretch = Stretch.Uniform,
		HorizontalAlignment = HorizontalAlignment.Center,
		VerticalAlignment = VerticalAlignment.Bottom
	};

	/// <summary>Plays an animation that is a video clip instead of pictures (muted; the avatar never makes sound).</summary>
	private readonly MediaElement video = new MediaElement
	{
		LoadedBehavior = MediaState.Manual,
		UnloadedBehavior = MediaState.Close,
		Stretch = Stretch.Uniform,
		IsMuted = true,
		Volume = 0.0,
		ScrubbingEnabled = false,
		HorizontalAlignment = HorizontalAlignment.Center,
		VerticalAlignment = VerticalAlignment.Bottom,
		Visibility = Visibility.Collapsed
	};

	/// <summary>Her daydream: a picture (or its clip) she made while you were away, shown in place of the avatar.</summary>
	private readonly Image daydream = new Image
	{
		Stretch = Stretch.Uniform,
		HorizontalAlignment = HorizontalAlignment.Center,
		VerticalAlignment = VerticalAlignment.Center,
		Visibility = Visibility.Collapsed
	};

	private readonly MediaElement daydreamVideo = new MediaElement
	{
		LoadedBehavior = MediaState.Manual,
		UnloadedBehavior = MediaState.Close,
		Stretch = Stretch.Uniform,
		IsMuted = true,
		Volume = 0.0,
		HorizontalAlignment = HorizontalAlignment.Center,
		VerticalAlignment = VerticalAlignment.Center,
		Visibility = Visibility.Collapsed
	};

	private readonly Border daydreamLabel = new Border
	{
		Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x0A, 0x10, 0x18)),
		CornerRadius = new CornerRadius(10.0),
		Padding = new Thickness(8.0, 3.0, 8.0, 3.0),
		Margin = new Thickness(8.0),
		HorizontalAlignment = HorizontalAlignment.Left,
		VerticalAlignment = VerticalAlignment.Top,
		Visibility = Visibility.Collapsed,
		Child = new TextBlock { Text = "\u2601 Daydreaming", FontSize = 11.0, Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xE2, 0xEE)) }
	};

	/// <summary>True while her daydream is showing instead of the avatar.</summary>
	public bool Daydreaming => daydream.Visibility == Visibility.Visible || daydreamVideo.Visibility == Visibility.Visible;

	private const string EmptyHint = "No avatar pictures yet.\nOpen Avatar… to add some.";

	/// <summary>Video animations Windows couldn't play; they're skipped until the folder is reloaded.</summary>
	private readonly HashSet<string> brokenVideos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly TextBlock hint = new TextBlock
	{
		Text = EmptyHint,
		TextAlignment = TextAlignment.Center,
		TextWrapping = TextWrapping.Wrap,
		Foreground = new SolidColorBrush(Color.FromRgb(151, 168, 187)),
		FontSize = 11.0,
		VerticalAlignment = VerticalAlignment.Center,
		HorizontalAlignment = HorizontalAlignment.Center
	};

	private readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render);

	private readonly Dictionary<string, BitmapSource[]> cache = new Dictionary<string, BitmapSource[]>(StringComparer.OrdinalIgnoreCase);

	private readonly Random random = new Random();

	private List<SpriteAnimation> animations = new List<SpriteAnimation>();

	private SpriteKind baseState = SpriteKind.Idle;

	private SpriteAnimation? playing;

	private bool playingGesture;

	private int frame;

	private int loopsSinceVariation;

	/// <summary>Gestures matched while another was playing; they play next, in order.</summary>
	private readonly Queue<SpriteAnimation> queued = new Queue<SpriteAnimation>();

	private const int MaxQueued = 2;

	public string Root { get; private set; } = "";

	public IReadOnlyList<SpriteAnimation> Animations => animations;

	/// <summary>Name of the animation currently on screen (for the Animations window and tests).</summary>
	public string Current => playing?.Name ?? "";

	public event Action<string>? AnimationStarted;

	public SpriteAvatarView()
	{
		RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
		base.Children.Add(image);
		base.Children.Add(video);
		base.Children.Add(hint);
		RenderOptions.SetBitmapScalingMode(daydream, BitmapScalingMode.HighQuality);
		base.Children.Add(daydream);
		base.Children.Add(daydreamVideo);
		base.Children.Add(daydreamLabel);
		daydreamVideo.MediaEnded += delegate
		{
			daydreamVideo.Position = TimeSpan.Zero;
			daydreamVideo.Play();
		};
		daydreamVideo.MediaFailed += delegate
		{
			// The clip won't play: the still picture stays.
			daydreamVideo.Stop();
			daydreamVideo.Visibility = Visibility.Collapsed;
			if (daydream.Source != null)
			{
				daydream.Visibility = Visibility.Visible;
			}
		};
		timer.Tick += delegate
		{
			Advance();
		};
		video.MediaOpened += delegate
		{
			if (playing != null)
			{
				video.SpeedRatio = Math.Clamp(playing.Meta.Speed, 0.25, 3.0);
			}
		};
		video.MediaEnded += delegate
		{
			if (playing != null && playing.IsVideo)
			{
				CycleEnded();
			}
		};
		video.MediaFailed += delegate(object? _, ExceptionRoutedEventArgs e)
		{
			SpriteAnimation? failed = playing;
			if (failed == null || !failed.IsVideo)
			{
				return;
			}
			// Fail loud: say which clip and why, right where the avatar should be.
			brokenVideos.Add(failed.Folder);
			video.Stop();
			video.Visibility = Visibility.Collapsed;
			hint.Text = "Couldn't play the video for \u201C" + failed.Name + "\u201D:\n" + (e.ErrorException?.Message ?? "Windows couldn't open it.") + "\nMP4 (H.264) plays on every PC; WebM needs the Web Media Extensions from the Microsoft Store.";
			hint.Visibility = Visibility.Visible;
			playing = null;
			queued.Clear();
			StartBase();
		};
	}

	public void Load(string root)
	{
		Root = root;
		animations = SpriteLibrary.Load(root);
		cache.Clear();
		queued.Clear();
		brokenVideos.Clear();
		TidyCopies();
		video.Stop();
		video.Source = null;
		video.Visibility = Visibility.Collapsed;
		hint.Text = EmptyHint;
		hint.Visibility = ((animations.Count == 0) ? Visibility.Visible : Visibility.Collapsed);
		playingGesture = false;
		StartBase();
	}

	/// <summary>Shows her daydream picture (and its clip, looping and muted, when there is one) in place of the avatar.</summary>
	public void ShowDaydream(string picture, string clip)
	{
		BitmapImage bitmap = new BitmapImage();
		bitmap.BeginInit();
		bitmap.StreamSource = new MemoryStream(File.ReadAllBytes(picture));
		bitmap.CacheOption = BitmapCacheOption.OnLoad;
		bitmap.EndInit();
		bitmap.Freeze();
		daydream.Source = bitmap;
		daydreamVideo.Stop();
		daydreamVideo.Source = null;
		if (clip.Length > 0)
		{
			daydreamVideo.Source = new Uri(clip, UriKind.Absolute);
			daydreamVideo.Visibility = Visibility.Visible;
			daydream.Visibility = Visibility.Collapsed;
			daydreamVideo.Play();
		}
		else
		{
			daydreamVideo.Visibility = Visibility.Collapsed;
			daydream.Visibility = Visibility.Visible;
		}
		daydreamLabel.Visibility = Visibility.Visible;
		image.Opacity = 0.0;
		this.video.Opacity = 0.0;
	}

	public void ClearDaydream()
	{
		daydreamVideo.Stop();
		daydreamVideo.Source = null;
		daydreamVideo.Visibility = Visibility.Collapsed;
		daydream.Source = null;
		daydream.Visibility = Visibility.Collapsed;
		daydreamLabel.Visibility = Visibility.Collapsed;
		image.Opacity = 1.0;
		video.Opacity = 1.0;
	}

	public SpriteAnimation? Find(string name)
	{
		return animations.FirstOrDefault((SpriteAnimation a) => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Maps the app's states ("idle", "thinking", "speaking", "listening", …) onto animations.</summary>
	public void SetState(string state)
	{
		SpriteKind spriteKind = state switch
		{
			"thinking" => SpriteKind.Thinking,
			"speaking" => SpriteKind.Talking,
			"talking" => SpriteKind.Talking,
			"listening" => SpriteKind.Listening,
			_ => SpriteKind.Idle,
		};
		if (spriteKind != baseState || playing == null)
		{
			baseState = spriteKind;
			SpriteAnimation? target = ForState(spriteKind);
			// A repeating gesture keeps going until she does something new that has its own animation
			// (thinking, talking); you typing, going back to idle, or a state that would only fall back to idle, doesn't stop it.
			bool repeatingGesture = playingGesture && playing != null && playing.Kind == SpriteKind.Gesture && playing.Loops;
			if (repeatingGesture)
			{
				if ((spriteKind == SpriteKind.Thinking || spriteKind == SpriteKind.Talking) && target != null && target.Kind == spriteKind)
				{
					queued.Clear();
					StartBase();
				}
			}
			else if (!playingGesture && (playing == null || target != playing))
			{
				// Typing or clicking flips between states; when they share an animation, keep it running instead of restarting it.
				StartBase();
			}
		}
	}

	public bool PlayGesture(string name)
	{
		SpriteAnimation spriteAnimation = Find(name);
		if (spriteAnimation == null)
		{
			return false;
		}
		if (playingGesture && playing != null && playing.Kind == SpriteKind.Gesture && !playing.Loops)
		{
			// Let the current gesture finish; play this one right after it.
			if (playing != spriteAnimation && queued.Count < MaxQueued && !queued.Contains(spriteAnimation))
			{
				queued.Enqueue(spriteAnimation);
			}
			return true;
		}
		playingGesture = !spriteAnimation.Loops || spriteAnimation.Kind == SpriteKind.Gesture;
		Start(spriteAnimation);
		return true;
	}

	/// <summary>Plays any animation by name, for previews in the Animations window.</summary>
	public void Preview(string name)
	{
		SpriteAnimation spriteAnimation = Find(name);
		if (spriteAnimation != null)
		{
			playingGesture = true;
			Start(spriteAnimation);
		}
	}

	private SpriteAnimation? ForState(SpriteKind kind)
	{
		List<SpriteAnimation> list = Playable().Where((SpriteAnimation a) => a.Kind == kind).ToList();
		if (list.Count == 0 && kind != SpriteKind.Idle)
		{
			return ForState(SpriteKind.Idle);
		}
		if (list.Count == 0)
		{
			return Playable().FirstOrDefault();
		}
		// The plainest name ("idle") is the main loop; others ("idle blink") are variations.
		return list.OrderBy((SpriteAnimation a) => a.Name.Length).First();
	}

	private IEnumerable<SpriteAnimation> Playable()
	{
		return animations.Where((SpriteAnimation a) => !brokenVideos.Contains(a.Folder));
	}

	private void StartBase()
	{
		playingGesture = false;
		loopsSinceVariation = 0;
		SpriteAnimation spriteAnimation = ForState(baseState);
		if (spriteAnimation == null)
		{
			timer.Stop();
			video.Stop();
			video.Visibility = Visibility.Collapsed;
			image.Source = null;
			playing = null;
			return;
		}
		Start(spriteAnimation);
	}

	private void Start(SpriteAnimation animation)
	{
		if (brokenVideos.Contains(animation.Folder))
		{
			return;
		}
		playing = animation;
		frame = 0;
		if (animation.IsVideo)
		{
			timer.Stop();
			image.Source = null;
			string? copy = PlayableCopy(animation);
			if (copy == null)
			{
				return;
			}
			Uri uri = new Uri(copy);
			if (video.Source == null || video.Source != uri)
			{
				video.Source = uri;
			}
			else
			{
				video.Position = TimeSpan.Zero;
			}
			video.Visibility = Visibility.Visible;
			video.SpeedRatio = Math.Clamp(animation.Meta.Speed, 0.25, 3.0);
			video.Play();
		}
		else
		{
			video.Stop();
			video.Visibility = Visibility.Collapsed;
			timer.Interval = TimeSpan.FromSeconds(1.0 / animation.Fps);
			Show();
			timer.Start();
		}
		this.AnimationStarted?.Invoke(animation.Name);
	}

	private void Show()
	{
		if (playing != null)
		{
			BitmapSource[] array = Frames(playing);
			image.Source = ((array.Length != 0) ? array[Math.Min(frame, array.Length - 1)] : null);
		}
	}

	private void Advance()
	{
		if (playing == null)
		{
			timer.Stop();
			return;
		}
		BitmapSource[] array = Frames(playing);
		frame++;
		if (frame < array.Length)
		{
			Show();
			return;
		}
		CycleEnded();
	}

	/// <summary>The animation reached its last picture, or its clip ended: loop it, or move on.</summary>
	private void CycleEnded()
	{
		if (playing == null)
		{
			return;
		}
		if (playingGesture && playing.Loops && queued.Count == 0 && playing.Kind == SpriteKind.Gesture)
		{
			// "Repeat while active" on a gesture: loop it straight away, no idle in between.
			RestartCurrent();
			return;
		}
		if (playingGesture || !playing.Loops)
		{
			if (queued.Count > 0)
			{
				playingGesture = true;
				Start(queued.Dequeue());
				return;
			}
			StartBase();
			return;
		}
		frame = 0;
		// While idle, now and then play another idle set (a blink, a stretch) once for variety.
		if (baseState == SpriteKind.Idle && ++loopsSinceVariation >= 3)
		{
			List<SpriteAnimation> list = Playable().Where((SpriteAnimation a) => a.Kind == SpriteKind.Idle && a != playing).ToList();
			if (list.Count > 0 && random.NextDouble() < 0.35)
			{
				playingGesture = true;
				Start(list[random.Next(list.Count)]);
				return;
			}
		}
		RestartCurrent();
	}

	private void RestartCurrent()
	{
		if (playing == null)
		{
			return;
		}
		frame = 0;
		if (playing.IsVideo)
		{
			video.Position = TimeSpan.Zero;
			video.Play();
			return;
		}
		Show();
	}

	/// <summary>
	/// Windows keeps a playing video file open, which would stop the Animations window replacing or
	/// deleting it. So clips play from a private copy in the temp folder, named by the file's size and date
	/// so a replaced clip gets a fresh copy.
	/// </summary>
	private string? PlayableCopy(SpriteAnimation animation)
	{
		try
		{
			FileInfo fileInfo = new FileInfo(animation.Video!);
			string text = Path.Combine(Path.GetTempPath(), "AquilaLocal", "avatar-video");
			Directory.CreateDirectory(text);
			string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fileInfo.FullName.ToLowerInvariant() + "|" + fileInfo.Length + "|" + fileInfo.LastWriteTimeUtc.Ticks))).Substring(0, 16);
			string text2 = Path.Combine(text, key + fileInfo.Extension.ToLowerInvariant());
			if (!File.Exists(text2))
			{
				File.Copy(fileInfo.FullName, text2 + ".part", overwrite: true);
				File.Move(text2 + ".part", text2, overwrite: true);
				File.SetLastWriteTimeUtc(text2, DateTime.UtcNow);
			}
			return text2;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			brokenVideos.Add(animation.Folder);
			hint.Text = "Couldn't read the video for \u201C" + animation.Name + "\u201D:\n" + ex.Message;
			hint.Visibility = Visibility.Visible;
			StartBase();
			return null;
		}
	}

	/// <summary>Removes temp copies of clips that are no longer used (ones still playing elsewhere are skipped).</summary>
	private static void TidyCopies()
	{
		try
		{
			string text = Path.Combine(Path.GetTempPath(), "AquilaLocal", "avatar-video");
			if (!Directory.Exists(text))
			{
				return;
			}
			foreach (string item in Directory.EnumerateFiles(text))
			{
				if (File.GetLastWriteTimeUtc(item) < DateTime.UtcNow.AddDays(-7))
				{
					try
					{
						File.Delete(item);
					}
					catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
					{
					}
				}
			}
		}
		catch (Exception ex2) when (ex2 is IOException || ex2 is UnauthorizedAccessException)
		{
		}
	}

	private BitmapSource[] Frames(SpriteAnimation animation)
	{
		if (cache.TryGetValue(animation.Folder, out BitmapSource[] value))
		{
			return value;
		}
		value = animation.Frames.Select(LoadFrame).Where((BitmapSource b) => b != null).ToArray();
		cache[animation.Folder] = value;
		return value;
	}

	private static BitmapSource? LoadFrame(string path)
	{
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
			bitmapImage.DecodePixelHeight = 640;
			bitmapImage.UriSource = new Uri(path);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return bitmapImage;
		}
		catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is ArgumentException || ex is UnauthorizedAccessException)
		{
			return null;
		}
	}
}
