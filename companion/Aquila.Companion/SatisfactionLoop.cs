namespace AquilaLocal.Companion;

/// <param name="TopDrive">The drive that was on top when the reply was generated (what the injector told her to prioritise).</param>
/// <param name="ReplySimilarityToPast">Highest similarity between the reply and her earlier replies, or null if unknown.</param>
/// <param name="SelfChanges">
/// Wardrobe changes she made herself this turn. Hook point for the satisfaction loop: not scored yet, so a
/// self-chosen outfit can later count toward horizon (an unprompted choice) without changing this signature.
/// </param>
public sealed record TurnObservation(string UserText, string ReplyText, string TopDrive, double? ReplySimilarityToPast, IReadOnlyList<WardrobeEntry>? SelfChanges = null);

public sealed record LoopResult(string Drive, bool Satisfied, double Score, string Reason, List<DriveChange> Changes, List<Milestone> Milestones);

/// <summary>
/// After each reply: did it address the top drive? If so, lower that drive one step; if not, leave it and
/// grow its unsatisfied streak so it weighs heavier next turn. Horizon is scored by milestones instead of
/// completion: every unprompted act, unprompted choice or surprise is a partial reward and an achievement.
/// </summary>
public sealed class SatisfactionLoop(PersonaConfig config, PersonaStore store, ISatisfactionScorer scorer)
{
    public LoopResult Run(TurnObservation obs, DateTimeOffset now)
    {
        Dictionary<string, DriveValue> drives = store.ReadDrives(config.Drives.Select(d => d.Name));
        if (!drives.ContainsKey(obs.TopDrive))
        {
            throw new ArgumentException($"Unknown top drive '{obs.TopDrive}'.");
        }
        var values = drives.ToDictionary(kv => kv.Key, kv => kv.Value.Value);
        var changes = new List<DriveChange>();

        DriveSpec horizon = config.Drive(Drives.Horizon);
        List<Milestone> milestones = scorer.HorizonMilestones(obs.UserText, obs.ReplyText, obs.ReplySimilarityToPast)
            .Select(m => m with { Reward = horizon.SatisfyStep })
            .ToList();
        if (milestones.Count > 0)
        {
            double old = values[Drives.Horizon];
            double v = DriveEngine.Clamp(horizon, old - horizon.SatisfyStep * milestones.Count);
            values[Drives.Horizon] = v;
            changes.Add(new DriveChange(Drives.Horizon, old, v, "milestone: " + string.Join(", ", milestones.Select(m => m.Kind))));
        }

        bool satisfied;
        double score;
        string reason;
        if (obs.TopDrive == Drives.Horizon)
        {
            satisfied = milestones.Count > 0;
            score = milestones.Count;
            reason = satisfied ? "milestones: " + string.Join(", ", milestones.Select(m => m.Kind)) : "no milestone this turn";
        }
        else
        {
            DriveScore s = scorer.Score(obs.TopDrive, obs.UserText, obs.ReplyText);
            (satisfied, score, reason) = (s.Satisfied, s.Score, s.Reason);
            if (satisfied)
            {
                DriveSpec spec = config.Drive(obs.TopDrive);
                double old = values[obs.TopDrive];
                double v = DriveEngine.Clamp(spec, old - spec.SatisfyStep);
                values[obs.TopDrive] = v;
                changes.Add(new DriveChange(obs.TopDrive, old, v, "satisfied: " + reason));
            }
        }

        var streaks = new Dictionary<string, int> { [obs.TopDrive] = satisfied ? 0 : drives[obs.TopDrive].UnmetStreak + 1 };
        store.Apply(changes, now, streaks, milestones);
        return new LoopResult(obs.TopDrive, satisfied, score, reason, changes, milestones);
    }
}
