using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AquilaLocal;

/// <summary>
/// Draws a simple stand-in character so the avatar works before real pictures exist. Every frame is
/// labelled "placeholder" with its animation name and frame number, so it is obvious what is playing.
/// Replace any of them by dragging real pictures onto that animation in the Animations window.
/// </summary>
public static class AvatarPlaceholders
{
	private sealed record Pose(double ArmLeft = -1, double ArmRight = -1, double Mouth = 0.1, double EyeOpen = 1.0, double HeadTilt = 0, double HeadDrop = 0, bool Food = false, bool Bubble = false, double Lean = 0);

	private const int W = 360;

	private const int H = 440;

	public static readonly (string Name, string Description, double Speed)[] Sets = new(string, string, double)[7]
	{
		("idle", "", 0.5),
		("talking", "", 1.4),
		("thinking", "", 0.6),
		("listening", "", 0.6),
		("waving hello", "greeting someone, saying hi, hello, welcome or goodbye", 1.0),
		("eating", "food, eating, having a meal, a snack, tasting something delicious", 0.9),
		("nodding", "agreeing, saying yes, of course, sure, understood, exactly", 1.0)
	};

	/// <summary>Creates any placeholder set that is missing from the folder. Returns how many were made.</summary>
	public static int EnsureIn(string root)
	{
		Directory.CreateDirectory(root);
		int num = 0;
		(string, string, double)[] sets = Sets;
		for (int i = 0; i < sets.Length; i++)
		{
			var (name, description, speed) = sets[i];
			string text = Path.Combine(root, name);
			if (SpriteLibrary.FramesIn(text).Count > 0)
			{
				continue;
			}
			Directory.CreateDirectory(text);
			List<Pose> list = Frames(name);
			for (int j = 0; j < list.Count; j++)
			{
				Save(Draw(name, list[j], j + 1, list.Count), Path.Combine(text, $"{j + 1:00}.png"));
			}
			SpriteLibrary.WriteMeta(text, new SpriteMeta
			{
				Speed = speed,
				Description = description
			});
			num++;
		}
		return num;
	}

	private static List<Pose> Frames(string name)
	{
		return name switch
		{
			"idle" => new List<Pose>
			{
				new Pose(),
				new Pose(HeadDrop: 2.0),
				new Pose(HeadDrop: 3.0, EyeOpen: 0.1),
				new Pose(HeadDrop: 1.0)
			},
			"talking" => new List<Pose>
			{
				new Pose(Mouth: 0.15),
				new Pose(Mouth: 0.8),
				new Pose(Mouth: 0.4),
				new Pose(Mouth: 0.9)
			},
			"thinking" => new List<Pose>
			{
				new Pose(HeadTilt: -6.0, Bubble: true, ArmRight: 0.55),
				new Pose(HeadTilt: -8.0, Bubble: true, ArmRight: 0.6, EyeOpen: 0.6),
				new Pose(HeadTilt: -6.0, Bubble: true, ArmRight: 0.55)
			},
			"listening" => new List<Pose>
			{
				new Pose(HeadTilt: 7.0, Lean: 4.0),
				new Pose(HeadTilt: 9.0, Lean: 6.0),
				new Pose(HeadTilt: 7.0, Lean: 4.0, EyeOpen: 0.15)
			},
			"waving hello" => new List<Pose>
			{
				new Pose(ArmRight: 0.9, Mouth: 0.5),
				new Pose(ArmRight: 1.25, Mouth: 0.6),
				new Pose(ArmRight: 0.9, Mouth: 0.5),
				new Pose(ArmRight: 1.25, Mouth: 0.6),
				new Pose(ArmRight: 0.5, Mouth: 0.3)
			},
			"eating" => new List<Pose>
			{
				new Pose(ArmRight: 0.3, Food: true),
				new Pose(ArmRight: 0.7, Food: true, Mouth: 0.7),
				new Pose(ArmRight: 0.3, Food: true, Mouth: 0.2),
				new Pose(ArmRight: 0.7, Food: true, Mouth: 0.7)
			},
			"nodding" => new List<Pose>
			{
				new Pose(HeadDrop: 0.0, Mouth: 0.3),
				new Pose(HeadDrop: 14.0, Mouth: 0.3),
				new Pose(HeadDrop: 0.0, Mouth: 0.3),
				new Pose(HeadDrop: 14.0, Mouth: 0.3)
			},
			_ => new List<Pose> { new Pose() },
		};
	}

	private static BitmapSource Draw(string name, Pose p, int index, int count)
	{
		DrawingVisual drawingVisual = new DrawingVisual();
		Color color = Color.FromRgb(128, 222, 197);
		SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromRgb(38, 63, 72));
		SolidColorBrush brush = new SolidColorBrush(color);
		SolidColorBrush solidColorBrush2 = new SolidColorBrush(Color.FromRgb(16, 42, 39));
		Pen pen = new Pen(solidColorBrush2, 5.0)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		Pen pen2 = new Pen(brush, 16.0)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			double num = 180.0 + p.Lean;
			double num2 = 150.0 + p.HeadDrop;
			// Body
			drawingContext.DrawRoundedRectangle(solidColorBrush, new Pen(brush, 4.0), new Rect(num - 75.0, 250.0, 150.0, 130.0), 50.0, 50.0);
			// Arms: value is how far the arm is raised (0 = down, 1 = horizontal, >1 = up)
			DrawArm(drawingContext, pen2, new Point(num - 70.0, 285.0), -1.0, (p.ArmLeft < 0.0) ? 0.0 : p.ArmLeft);
			DrawArm(drawingContext, pen2, new Point(num + 70.0, 285.0), 1.0, (p.ArmRight < 0.0) ? 0.0 : p.ArmRight);
			if (p.Food)
			{
				Point point = ArmEnd(new Point(num + 70.0, 285.0), 1.0, p.ArmRight);
				drawingContext.DrawEllipse(new SolidColorBrush(Color.FromRgb(240, 190, 90)), new Pen(solidColorBrush2, 3.0), point, 16.0, 16.0);
			}
			// Head
			drawingContext.PushTransform(new RotateTransform(p.HeadTilt, num, num2));
			drawingContext.DrawEllipse(brush, null, new Point(num, num2), 85.0, 85.0);
			double num3 = Math.Max(2.0, 14.0 * p.EyeOpen);
			drawingContext.DrawEllipse(solidColorBrush2, null, new Point(num - 30.0, num2 - 12.0), 9.0, num3 / 2.0 + 1.0);
			drawingContext.DrawEllipse(solidColorBrush2, null, new Point(num + 30.0, num2 - 12.0), 9.0, num3 / 2.0 + 1.0);
			double num4 = 4.0 + 26.0 * p.Mouth;
			if (p.Mouth <= 0.12)
			{
				drawingContext.DrawLine(pen, new Point(num - 22.0, num2 + 34.0), new Point(num + 22.0, num2 + 34.0));
			}
			else
			{
				drawingContext.DrawEllipse(solidColorBrush2, null, new Point(num, num2 + 36.0), 22.0, num4 / 2.0);
			}
			drawingContext.Pop();
			if (p.Bubble)
			{
				SolidColorBrush brush2 = new SolidColorBrush(Color.FromArgb(220, 231, 237, 245));
				drawingContext.DrawEllipse(brush2, null, new Point(300.0, 60.0), 40.0, 28.0);
				drawingContext.DrawEllipse(brush2, null, new Point(262.0, 100.0), 9.0, 9.0);
				drawingContext.DrawText(Text("…", 30.0, solidColorBrush2), new Point(286.0, 36.0));
			}
			// Label so it's obvious this is a stand-in
			SolidColorBrush solidColorBrush3 = new SolidColorBrush(Color.FromArgb(200, 16, 23, 32));
			drawingContext.DrawRoundedRectangle(solidColorBrush3, null, new Rect(20.0, 392.0, 320.0, 40.0), 8.0, 8.0);
			FormattedText formattedText = Text($"PLACEHOLDER · {name} {index}/{count}", 15.0, Brushes.White);
			drawingContext.DrawText(formattedText, new Point(180.0 - formattedText.Width / 2.0, 402.0));
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(W, H, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		renderTargetBitmap.Freeze();
		return renderTargetBitmap;
	}

	private static Point ArmEnd(Point shoulder, double side, double raise)
	{
		double num = (-90.0 + 90.0 * Math.Clamp(raise, 0.0, 1.6)) * Math.PI / 180.0;
		return new Point(shoulder.X + side * 75.0 * Math.Cos(num), shoulder.Y - 75.0 * Math.Sin(num));
	}

	private static void DrawArm(DrawingContext dc, Pen pen, Point shoulder, double side, double raise)
	{
		dc.DrawLine(pen, shoulder, ArmEnd(shoulder, side, raise));
	}

	private static FormattedText Text(string text, double size, Brush brush)
	{
		return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.0);
	}

	private static void Save(BitmapSource bitmap, string path)
	{
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(bitmap));
		using FileStream stream = File.Create(path);
		pngBitmapEncoder.Save(stream);
	}
}
