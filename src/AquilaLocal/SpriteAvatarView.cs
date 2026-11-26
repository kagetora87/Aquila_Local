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

	private readonly TextBlock hint = new TextBlock
	{
		Text = "No avatar pictures yet.\nOpen Avatar… to add some.",
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
		base.Children.Add(hint);
		timer.Tick += delegate
		{
			Advance();
		};
	}

	public void Load(string root)
	{
		Root = root;
		animations = SpriteLibrary.Load(root);
		cache.Clear();
		queued.Clear();
		hint.Visibility = ((animations.Count == 0) ? Visibility.Visible : Visibility.Collapsed);
		playingGesture = false;
		StartBase();
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
			if (!playingGesture)
			{
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
		if (playingGesture && playing != null && playing.Kind == SpriteKind.Gesture)
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
		List<SpriteAnimation> list = animations.Where((SpriteAnimation a) => a.Kind == kind).ToList();
		if (list.Count == 0 && kind != SpriteKind.Idle)
		{
			return ForState(SpriteKind.Idle);
		}
		if (list.Count == 0)
		{
			return animations.FirstOrDefault();
		}
		// The plainest name ("idle") is the main loop; others ("idle blink") are variations.
		return list.OrderBy((SpriteAnimation a) => a.Name.Length).First();
	}

	private void StartBase()
	{
		playingGesture = false;
		loopsSinceVariation = 0;
		SpriteAnimation spriteAnimation = ForState(baseState);
		if (spriteAnimation == null)
		{
			timer.Stop();
			image.Source = null;
			playing = null;
			return;
		}
		Start(spriteAnimation);
	}

	private void Start(SpriteAnimation animation)
	{
		playing = animation;
		frame = 0;
		timer.Interval = TimeSpan.FromSeconds(1.0 / animation.Fps);
		Show();
		timer.Start();
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
			List<SpriteAnimation> list = animations.Where((SpriteAnimation a) => a.Kind == SpriteKind.Idle && a != playing).ToList();
			if (list.Count > 0 && random.NextDouble() < 0.35)
			{
				playingGesture = true;
				Start(list[random.Next(list.Count)]);
				return;
			}
		}
		Show();
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
