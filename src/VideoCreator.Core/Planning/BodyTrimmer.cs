using VideoCreator.Models;

namespace VideoCreator.Planning;

/// <summary>Measured boundary suggestions; automatic cuts require both silence and black picture.</summary>
public sealed record BodyTrimAnalysis(string AssetId, double Duration, Interval Suggested, Interval Automatic,
    string? FirstSpeech, string? LastSpeech, List<string> Notes, double SoundBefore = 0, double SoundAfter = 0);

public static class BodyTrimmer
{
    public static SourceAsset? Main(IReadOnlyList<SourceAsset> assets) =>
        assets.FirstOrDefault(a => a.Role == AssetRole.Main && a.Media.Width > 0)
        ?? AssetRoles.Presenter(assets) ?? AssetRoles.Master(assets);

    public static bool Valid(BodyTrim trim, SourceAsset main) =>
        trim.AssetId == main.Id && trim.Mode is "manual" or "auto" &&
        double.IsFinite(trim.Start) && double.IsFinite(trim.End) &&
        trim.Start >= 0 && trim.End <= main.Media.Duration && trim.End - trim.Start >= 0.1;

    public static BodyTrimAnalysis Analyze(SourceAsset main, Transcript tr, SignalSet sig)
    {
        double duration = main.Media.Duration;
        // ASR times may overrun the container by a few ms; clamp them so the last utterance stays protected.
        var words = tr.Words.Select(w => (T0: w.T0, T1: w.T1)).Concat(tr.Sentences.Select(s => (T0: s.T0, T1: s.T1)))
            .Where(w => double.IsFinite(w.T0) && double.IsFinite(w.T1) && w.T1 > w.T0 && w.T1 > 0 && w.T0 < duration)
            .Select(w => (T0: Math.Max(0, w.T0), T1: Math.Min(duration, w.T1))).ToList();
        var notes = new List<string>();
        // A recording without an audio track is silent throughout, so its fully black edges carry nothing.
        IReadOnlyList<Interval> silences = main.Media.AudioChannels == 0 ? [new(0, duration)] : sig.Silences.OrderBy(s => s.T0).ToList();
        double first = words.Count > 0 ? words.Min(w => w.T0) : 0;
        double last = words.Count > 0 ? words.Max(w => w.T1) : duration;
        // Recognizers time word edges loosely (talk.mp4: the last word ends 0.44s after the sound does). For the manual suggestion,
        // move an edge to where measured silence ends/starts, but only if no recognized word or sentence would be cut by the move.
        double speechStart = first, speechEnd = last;
        if (words.Count > 0 && silences.FirstOrDefault(s => s.T0 <= first && first < s.T1) is { } head &&
            words.Where(w => w.T0 < head.T1).All(w => w.T1 > head.T1))
            speechStart = head.T1;
        if (words.Count > 0 && silences.FirstOrDefault(s => s.T0 < last && last <= s.T1) is { } tail &&
            words.Where(w => w.T1 > tail.T0).All(w => w.T0 < tail.T0))
            speechEnd = tail.T0;
        double soundStart = silences.Any(s => s.T0 <= speechStart && speechStart < s.T1) ? speechStart
            : silences.Where(s => s.T1 <= speechStart).Select(s => s.T1).DefaultIfEmpty(0).Max();
        double soundEnd = silences.Any(s => s.T0 < speechEnd && speechEnd <= s.T1) ? speechEnd
            : silences.Where(s => s.T0 >= speechEnd).Select(s => s.T0).DefaultIfEmpty(duration).Min();
        var suggested = new Interval(Math.Max(0, speechStart - 0.25), Math.Min(duration, speechEnd + 0.5));
        var protect = new Interval(Math.Max(0, first - 0.25), Math.Min(duration, last + 0.5));
        double soundBefore = words.Count > 0 ? Math.Round(speechStart - soundStart, 1) : 0;
        double soundAfter = words.Count > 0 ? Math.Round(soundEnd - speechEnd, 1) : 0;
        if (words.Count == 0)
            notes.Add("発話時刻がないため、本編の意味を判断できません。映像と音声を確認して手動で指定してください。");
        else
            notes.Add("最初と最後の発話（音声の実測で境界を補正）に前0.25秒・後0.5秒の余裕を残した手動候補です。準備会話・実演・結論はプレビューで確認してください。");
        if (soundBefore >= 1 || soundAfter >= 1)
            notes.Add($"最初の発話の前に{soundBefore:0.#}秒、最後の発話の後に{soundAfter:0.#}秒、途切れない音があります（音楽・拍手・雑音など）。必要なら範囲を広げてください。");

        // Only continuous evidence touching an edge can remove that edge. Internal pauses are never removed.
        // The edge may sit one proxy frame (constant 30 fps) away: blackdetect ends a run at EOF at the last frame's start.
        // Inside a run nothing is bridged, so a single visible frame between black runs is kept.
        const double edgeSlack = 1 / 30.0 + 0.005;
        double Head(IReadOnlyList<Interval> spans)
        {
            double end = 0; bool edge = true;
            foreach (var span in spans.OrderBy(x => x.T0))
            {
                if (span.T0 > end + (edge ? edgeSlack : 1e-6)) break;
                end = Math.Max(end, Math.Min(duration, span.T1)); edge = false;
            }
            return end;
        }
        double Tail(IReadOnlyList<Interval> spans) =>
            duration - Head(spans.Select(x => new Interval(duration - x.T1, duration - x.T0)).ToList());
        // A recording without an audio track is silent throughout (see above), so only black picture decides its edges.
        if (main.Media.AudioChannels == 0)
            notes.Add("音声トラックがないため、端から連続する完全な黒画面だけを自動削除の対象にします。");
        double start = Math.Max(0, Math.Min(Head(silences), Head(sig.Blacks)) - 0.25);
        double end = Math.Min(duration, Math.Max(Tail(silences), Tail(sig.Blacks)) + 0.5);
        // Automatic removal keeps every recognized word as timed, without the manual suggestion's snapping.
        if (words.Count > 0) { start = Math.Min(start, protect.T0); end = Math.Max(end, protect.T1); }
        // An entirely silent/black recording is not an identified main body.
        if (end - start < 0.1 || Math.Min(Head(silences), Head(sig.Blacks)) >= duration - 0.1)
        {
            start = 0; end = duration;
            notes.Add("本編を特定できないため自動削除しません。");
        }
        notes.Add(start > 0 || end < duration
            ? "自動候補は端から連続する黒画面と無音の一致部分だけを除きます。発話・映像のある無音は残します。"
            : "黒画面と無音が一致する安全な端部がないため、自動では全体を保持します。");
        // Millisecond values for display and storage, rounded outward so rounding never narrows the body.
        static Interval Outward(Interval r, double duration) => new(
            Math.Max(0, Math.Floor(r.T0 * 1000 + 1e-6) / 1000), Math.Min(duration, Math.Ceiling(r.T1 * 1000 - 1e-6) / 1000));
        return new(main.Id, duration, Outward(suggested, duration), Outward(new(start, end), duration),
            tr.Sentences.OrderBy(s => s.T0).FirstOrDefault()?.Text,
            tr.Sentences.OrderBy(s => s.T1).LastOrDefault()?.Text, notes, soundBefore, soundAfter);
    }

    public static Interval MasterRange(BodyTrim trim, SourceAsset picture, SourceAsset master)
    {
        if (!Valid(trim, picture)) throw new PlanException("E-TRIM-RANGE", "本編範囲が不正か主動画が変わっています。範囲を指定し直してください。");
        double rate = 1 + picture.SyncDriftPpm * 1e-6;
        if (!double.IsFinite(rate) || rate <= 0) throw new PlanException("E-TRIM-SYNC", "主動画の同期情報が不正です。");
        if (picture.Id != master.Id && picture.SyncMethod is not ("audio" or "audio-map" or "manual"))
            throw new PlanException("E-TRIM-SYNC", "主動画と音声の元の同期を確認し、必要ならずれを手動指定してください。");
        var range = picture.Id == master.Id ? new Interval(trim.Start, trim.End)
            : new Interval(PipPlanner.MasterTime(master, picture, trim.Start), PipPlanner.MasterTime(master, picture, trim.End));
        if (!double.IsFinite(range.T0) || !double.IsFinite(range.T1) || range.T0 < 0 || range.T1 > master.Media.Duration || range.Length < 0.1)
            throw new PlanException("E-TRIM-SYNC", "音声の元が主動画の本編全体を覆っていません。音声の元または本編範囲を変更してください。");
        if (picture.Id != master.Id && picture.SyncMap.Count > 0)
        {
            // A discontinuous map may have no inverse at a proposed boundary.
            if (Math.Abs(picture.CameraTime(range.T0, master.Id) - trim.Start) > 1 / 30.0 ||
                Math.Abs(picture.CameraTime(range.T1, master.Id) - trim.End) > 1 / 30.0)
                throw new PlanException("E-TRIM-SYNC", "編集点が同期マップの不連続部分にあります。編集点を前後へ移動してください。");
        }
        return range;
    }

    public static List<Clip> Clips(Interval range, SourceAsset picture, SourceAsset master)
    {
        var cuts = picture.SyncJumps(range.T0, range.T1).Prepend(range.T0).Append(range.T1).Order().ToList();
        return cuts.Zip(cuts.Skip(1), (lo, hi) => new Clip
        {
            ClipId = $"body_{lo:0.######}", AssetId = picture.Id, AudioIn = lo,
            SrcIn = picture.CameraTime(lo, master.Id),
            SrcOut = picture.CameraTime(lo, master.Id) + hi - lo,
        }).ToList();
    }
}
