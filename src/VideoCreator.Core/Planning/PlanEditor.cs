using VideoCreator.Models;

namespace VideoCreator.Planning;

public sealed record PlanOp(string Op, string? Target = null, string? Text = null, bool? Enabled = null, string? Type = null, string? Edge = null, int? Delta = null);

/// <summary>FR-REV-02..05: pure edits on an EditPlan; every edit is recorded in provenance (AP-1).</summary>
public static class PlanEditor
{
    public const int MaxTelopLength = 40;

    public static EditPlan Apply(EditPlan plan, IEnumerable<PlanOp> ops)
    {
        foreach (var op in ops)
        {
            plan = op.Op switch
            {
                "replace_text" => ReplaceText(plan, op.Target ?? throw new ArgumentException("target required"), op.Text ?? ""),
                "set_enabled" => Update(plan, op.Target!, t => t with { Enabled = op.Enabled ?? true }, "set_enabled", null, (op.Enabled ?? true).ToString()),
                "approve" => Update(plan, op.Target!, t => t with { Approved = true }, "approve", null, "true"),
                "set_type_enabled" => SetType(plan, op.Type ?? throw new ArgumentException("type required"), op.Enabled ?? true),
                "approve_all_tsukkomi" => plan with
                {
                    Telops = plan.Telops.Select(t => t.Type == TelopType.Tsukkomi ? t with { Approved = true } : t).ToList(),
                    Provenance = plan.Provenance with { Edits = [.. plan.Provenance.Edits, new EditRecord(DateTimeOffset.Now, "approve_all_tsukkomi", "tsukkomi", null, "true")] },
                },
                "set_bgm" => plan with
                {
                    Audio = plan.Audio with { Bgm = string.IsNullOrEmpty(op.Target) || op.Target == "none" ? null : op.Target, BgmMutes = string.IsNullOrEmpty(op.Target) || op.Target == "none" ? [] : plan.Audio.BgmMutes },
                    Provenance = plan.Provenance with { Edits = [.. plan.Provenance.Edits, new EditRecord(DateTimeOffset.Now, "set_bgm", "audio", plan.Audio.Bgm, op.Target)] },
                },
                "set_sfx" => plan with
                {
                    Audio = plan.Audio with { SfxEnabled = op.Enabled ?? true },
                    Provenance = plan.Provenance with { Edits = [.. plan.Provenance.Edits, new EditRecord(DateTimeOffset.Now, "set_sfx", "audio", plan.Audio.SfxEnabled.ToString(), (op.Enabled ?? true).ToString())] },
                },
                _ => throw new ArgumentException($"unknown op {op.Op}"),
            };
        }
        return plan;
    }

    static EditPlan ReplaceText(EditPlan plan, string target, string text)
    {
        text = text.Replace("\r", "").Replace("\n", " ").Trim();
        if (text.Length > MaxTelopLength) throw new ArgumentException($"テロップは{MaxTelopLength}文字以内にしてください");
        var old = plan.Telops.FirstOrDefault(t => t.Id == target)?.Text;
        // a human-edited telop counts as reviewed; tsukkomi still needs explicit approval
        return Update(plan, target, t => t with { Text = text, Approved = t.Type == TelopType.Tsukkomi ? t.Approved : true }, "replace_text", old, text);
    }

    static EditPlan Update(EditPlan plan, string target, Func<Telop, Telop> f, string op, string? from, string? to)
    {
        if (plan.Telops.All(t => t.Id != target)) throw new KeyNotFoundException($"telop {target} not found");
        return plan with
        {
            Telops = plan.Telops.Select(t => t.Id == target ? f(t) : t).ToList(),
            Provenance = plan.Provenance with { Edits = [.. plan.Provenance.Edits, new EditRecord(DateTimeOffset.Now, op, target, from, to)] },
        };
    }

    static EditPlan SetType(EditPlan plan, string type, bool enabled)
    {
        if (!Enum.TryParse<TelopType>(type, true, out var tt)) throw new ArgumentException($"unknown telop type {type}");
        return plan with
        {
            Telops = plan.Telops.Select(t => t.Type == tt ? t with { Enabled = enabled } : t).ToList(),
            Provenance = plan.Provenance with { Edits = [.. plan.Provenance.Edits, new EditRecord(DateTimeOffset.Now, "set_type_enabled", type, null, enabled.ToString())] },
        };
    }

    /// <summary>Re-apply earlier text edits / toggles to a rebuilt plan (after a ±1 sentence adjustment), matched by source words.</summary>
    public static EditPlan Reapply(EditPlan rebuilt, EditPlan previous, bool refreshCaptions = false)
    {
        var telops = rebuilt.Telops.Select(t =>
        {
            var old = previous.Telops.FirstOrDefault(o => o.Type == t.Type && (o.SourceWordIds.Count > 0 ? o.SourceWordIds.SequenceEqual(t.SourceWordIds) : o.Text == t.Text || o.Type is TelopType.Hook or TelopType.Headline));
            if (old == null) return t;
            // after a transcript correction the caption text must come from the corrected words; other edits stay
            return t with { Text = refreshCaptions && t.Type == TelopType.Caption ? t.Text : old.Text, Enabled = old.Enabled, Approved = refreshCaptions && t.Type == TelopType.Caption ? t.Approved : old.Approved };
        }).ToList();
        return rebuilt with
        {
            Telops = telops,
            Audio = rebuilt.Audio with { Bgm = previous.Audio.Bgm, SfxEnabled = previous.Audio.SfxEnabled, BgmMutes = previous.Audio.Bgm == null ? [] : rebuilt.Audio.BgmMutes },
            Provenance = previous.Provenance with { Edits = [.. previous.Provenance.Edits] },
        };
    }

    /// <summary>FR-REV-03: move the first/last edge by ±n whole sentences.</summary>
    public static List<Interval> AdjustSegments(IReadOnlyList<Interval> segs, Transcript tr, string edge, int delta)
    {
        var list = segs.ToList();
        if (list.Count == 0 || tr.Sentences.Count == 0 || delta == 0) return list;
        if (edge == "start")
        {
            var first = tr.Sentences.FindIndex(s => s.T1 > list[0].T0 + 0.05);
            int idx = Math.Clamp(first - delta, 0, tr.Sentences.Count - 1);   // delta>0 => earlier start (longer)
            list[0] = new Interval(tr.Sentences[idx].T0, Math.Max(list[0].T1, tr.Sentences[idx].T1));
        }
        else
        {
            var last = tr.Sentences.FindLastIndex(s => s.T0 < list[^1].T1 - 0.05);
            int idx = Math.Clamp(last + delta, 0, tr.Sentences.Count - 1);
            list[^1] = new Interval(Math.Min(list[^1].T0, tr.Sentences[idx].T0), tr.Sentences[idx].T1);
        }
        return list;
    }
}
