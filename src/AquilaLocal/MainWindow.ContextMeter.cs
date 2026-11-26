using System;
using System.Linq;
using System.Windows;

namespace AquilaLocal;

/// <summary>The context meter in the header: how full this conversation's context window is, always visible.</summary>
public partial class MainWindow
{
	private void RefreshContextMeter()
	{
		if (current == null)
		{
			ContextChip.Visibility = Visibility.Collapsed;
			return;
		}
		ContextChip.Visibility = Visibility.Visible;
		long window = Math.Max(1, settings.Context);
		Turn? measured = current.Turns.LastOrDefault((Turn t) => t.ContextTokens > 0);
		long used;
		bool estimate;
		if (measured != null && measured == current.Turns.LastOrDefault((Turn t) => t.Status == "complete"))
		{
			used = measured.ContextTokens;
			estimate = false;
		}
		else if (current.Turns.Count == 0)
		{
			used = 0;
			estimate = false;
		}
		else
		{
			// Older replies didn't record it: a rough count from the text (about 3.5 characters a token, plus the setup).
			used = 1500 + current.Turns.Sum((Turn t) => (long)(t.User.Length + t.Context.Length + t.Answer.Length)) * 2 / 7;
			estimate = true;
		}
		double share = Math.Clamp((double)used / window, 0.0, 1.0);
		ContextFill.Width = 56 * share;
		ContextFill.Background = Ui.Brush(share >= 0.9 ? "Danger" : share >= 0.7 ? "Warning" : "Accent");
		ContextText.Text = $"{(estimate ? "~" : "")}{Short(used)} / {Short(window)} · {share * 100:0}%";
		ContextChip.ToolTip = (estimate ? "About " : "") + $"{used:N0} of {window:N0} tokens in this conversation's context window ({share * 100:0.#}%). " +
			"This is everything the model reads each time it replies: its instructions, memories and the chat so far. " +
			(current.IsCompanion
				? "When it fills up, the oldest messages drop out of what she reads (her long-term memory keeps them)."
				: "When it fills up, older messages drop out or get compacted into a summary.");
	}

	private static string Short(long tokens) => tokens >= 1000 ? $"{tokens / 1000.0:0.#}K" : tokens.ToString();
}
