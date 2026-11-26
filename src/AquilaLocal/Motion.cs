using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AquilaLocal;

/// <summary>
/// The one place code-driven animation goes through. Honours the Windows "Animation effects"
/// setting: when it is off, everything jumps straight to its final state.
/// </summary>
public static class Motion
{
	public static readonly Duration Fast = new Duration(TimeSpan.FromMilliseconds(120.0));

	public static readonly Duration Base = new Duration(TimeSpan.FromMilliseconds(180.0));

	public static readonly Duration Slow = new Duration(TimeSpan.FromMilliseconds(260.0));

	public static readonly IEasingFunction EaseOut = Freeze(new CubicEase
	{
		EasingMode = EasingMode.EaseOut
	});

	public static readonly IEasingFunction EaseIn = Freeze(new CubicEase
	{
		EasingMode = EasingMode.EaseIn
	});

	public static readonly IEasingFunction EaseInOut = Freeze(new CubicEase
	{
		EasingMode = EasingMode.EaseInOut
	});

	public static bool Enabled { get; private set; } = true;

	private static IEasingFunction Freeze(CubicEase ease)
	{
		ease.Freeze();
		return ease;
	}

	/// <summary>Call once at start-up, before the first window is created.</summary>
	public static void Initialize(Application app)
	{
		Refresh(app);
		SystemParameters.StaticPropertyChanged += delegate(object? _, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "ClientAreaAnimation")
			{
				Refresh(app);
			}
		};
	}

	private static void Refresh(Application app)
	{
		bool flag;
		try
		{
			flag = SystemParameters.ClientAreaAnimation;
		}
		catch
		{
			flag = true;
		}
		Enabled = flag;
		if (!flag)
		{
			// Application.Resources itself wins over merged dictionaries, so templates read zero.
			app.Resources["Motion.Fast"] = new Duration(TimeSpan.Zero);
			app.Resources["Motion.Base"] = new Duration(TimeSpan.Zero);
			app.Resources["Motion.Slow"] = new Duration(TimeSpan.Zero);
		}
		else
		{
			app.Resources.Remove("Motion.Fast");
			app.Resources.Remove("Motion.Base");
			app.Resources.Remove("Motion.Slow");
		}
	}

	public static void Animate(IAnimatable target, DependencyProperty property, double to, Duration duration, IEasingFunction? ease = null, Action? completed = null)
	{
		if (!Enabled || duration.TimeSpan == TimeSpan.Zero)
		{
			target.BeginAnimation(property, null);
			if (target is DependencyObject dependencyObject)
			{
				dependencyObject.SetValue(property, to);
			}
			completed?.Invoke();
			return;
		}
		DoubleAnimation doubleAnimation = new DoubleAnimation(to, duration)
		{
			EasingFunction = ease ?? EaseOut,
			FillBehavior = FillBehavior.HoldEnd
		};
		if (completed != null)
		{
			doubleAnimation.Completed += delegate
			{
				completed();
			};
		}
		target.BeginAnimation(property, doubleAnimation);
	}

	/// <summary>Opacity 0 → 1 and a small rise (Y 8 → 0). For new messages and banners.</summary>
	public static void RiseIn(UIElement element, double distance = 8.0)
	{
		if (!Enabled)
		{
			element.Opacity = 1.0;
			return;
		}
		TranslateTransform translateTransform = new TranslateTransform(0.0, distance);
		element.RenderTransform = translateTransform;
		element.Opacity = 0.0;
		Animate(element, UIElement.OpacityProperty, 1.0, Base, EaseOut);
		Animate(translateTransform, TranslateTransform.YProperty, 0.0, Base, EaseOut, delegate
		{
			element.RenderTransform = Transform.Identity;
		});
	}

	/// <summary>Opacity 0 → 1 and scale 0.98 → 1. For windows and dialogs.</summary>
	public static void FadeScaleIn(UIElement element)
	{
		if (!Enabled)
		{
			return;
		}
		ScaleTransform scaleTransform = new ScaleTransform(0.98, 0.98);
		element.RenderTransformOrigin = new Point(0.5, 0.5);
		element.RenderTransform = scaleTransform;
		element.Opacity = 0.0;
		Animate(element, UIElement.OpacityProperty, 1.0, Base, EaseOut);
		Animate(scaleTransform, ScaleTransform.ScaleXProperty, 1.0, Base, EaseOut);
		Animate(scaleTransform, ScaleTransform.ScaleYProperty, 1.0, Base, EaseOut, delegate
		{
			element.RenderTransform = Transform.Identity;
		});
	}

	public static void FadeIn(UIElement element, Duration? duration = null)
	{
		if (!Enabled)
		{
			element.BeginAnimation(UIElement.OpacityProperty, null);
			element.Opacity = 1.0;
			return;
		}
		element.Opacity = 0.0;
		Animate(element, UIElement.OpacityProperty, 1.0, duration ?? Fast, EaseOut);
	}

	/// <summary>A looping opacity pulse (status dot, waiting dots). Static when motion is off.</summary>
	public static void Pulse(UIElement element, double low, TimeSpan cycle, TimeSpan delay = default(TimeSpan))
	{
		if (!Enabled)
		{
			element.BeginAnimation(UIElement.OpacityProperty, null);
			element.Opacity = 1.0;
			return;
		}
		DoubleAnimation doubleAnimation = new DoubleAnimation(1.0, low, new Duration(TimeSpan.FromTicks(cycle.Ticks / 2)))
		{
			AutoReverse = true,
			RepeatBehavior = RepeatBehavior.Forever,
			BeginTime = delay,
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseInOut
			}
		};
		element.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
	}

	public static void StopPulse(UIElement element)
	{
		element.BeginAnimation(UIElement.OpacityProperty, null);
		element.Opacity = 1.0;
	}
}
