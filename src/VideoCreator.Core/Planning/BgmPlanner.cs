using VideoCreator.Audio;
using VideoCreator.Models;

namespace VideoCreator.Planning;

/// <summary>
/// Scene-matched BGM and speech-keyed ducking (body timeline; Renderer maps to the final file through InsertMap).
/// <list type="bullet">
/// <item>Scenes: the body is split only where the video itself changes scene (role change, scene-change card, chapter; cross-fade / source jump as weaker cues),
/// never into scenes shorter than <see cref="MinSceneSec"/>, so the music does not hop.</item>
/// <item>Choice: each scene's measured energy (speech rate, voice level, laughter) is matched to each track's measured energy (tempo, attacks, brightness) by
/// dynamic programming that also charges every change of track (<see cref="SwitchCost"/>).</item>
/// <item>Level: music in pauses sits <see cref="AudioSpec.BgmGapLu"/> below the speech; while speaking it is lowered so speech − music ≥ <see cref="AudioSpec.BgmSpeechLd"/>
/// (research floor 10 LU, Torcoli et al. JAES 2019), deeper where the voice is quiet or the song is loud. Ducking follows the ASR words with look-ahead
/// (<see cref="AttackSec"/>) and bridges gaps under <see cref="HoldSec"/> so the music does not pump between words.</item>
/// </list>
/// </summary>
public static class BgmPlanner
{
    public const double MinSceneSec = 20, HoldSec = 0.8, AttackSec = 0.25, ReleaseSec = 0.6, MuteRampSec = 0.05;
    public const double CrossfadeSec = 2.0, SameTrackFadeSec = 0.3, SwitchCost = 0.15;
    public const double MaxRideDb = 10, MaxDuckDb = 30, LocalClampDb = 8;
    public const double ResearchFloorLd = 10, MinTrimDb = -12, MaxTrimDb = 6, MinGapLu = -30, MaxGapLu = 0, MaxSpeechLd = 30;
    public const double FadeInSec = 0.4, FadeOutSec = 1.0;

    /// <summary>Inserts the body music stops under (bumpers and cards with their own music) — the same rule as Renderer.FinalAudio.</summary>
    public static bool Mutes(Insert i) => i.Kind != "card" || i.Bgm != null;

    static double R(double x, int d = 3) => Math.Round(x, d);

    // ---------------- speech (where to duck) ----------------

    readonly record struct Run(double B0, double B1, double M0, double M1);

    /// <summary>Speech on the body timeline from the ASR words (or, without a transcript, the non-silent parts), joined across gaps under <see cref="HoldSec"/>.
    /// LocalDb = that stretch's voice level relative to all the speech in the edit (from the 100 ms loudness signal).</summary>
    public static List<DuckSpan> SpeechSpans(IReadOnlyList<Clip> clips, Transcript tr, SignalSet sig)
    {
        var runs = new List<Run>(); double off = 0;
        var words = tr.Words.OrderBy(w => w.T0).ToList();
        foreach (var c in clips)
        {
            double m0 = c.MasterIn, m1 = c.MasterOut, sp = c.Speed > 0 ? c.Speed : 1, o = off;
            double B(double m) => o + (Math.Clamp(m, m0, m1) - m0) / sp;
            if (words.Count > 0)
            {
                int lo = 0, hi = words.Count;
                while (lo < hi) { int mid = (lo + hi) / 2; if (words[mid].T1 <= m0) lo = mid + 1; else hi = mid; }
                for (int i = lo; i < words.Count && words[i].T0 < m1; i++)
                {
                    var w = words[i];
                    if (w.T1 > m0) runs.Add(new Run(B(w.T0), B(w.T1), Math.Max(w.T0, m0), Math.Min(w.T1, m1)));
                }
            }
            else if (sig.Loudness100Ms.Count > 0)
            {
                double t = m0;
                foreach (var s in sig.Silences.Where(s => s.T1 > m0 && s.T0 < m1).OrderBy(s => s.T0))
                {
                    if (s.T0 > t) runs.Add(new Run(B(t), B(s.T0), t, s.T0));
                    t = Math.Max(t, s.T1);
                }
                if (t < m1) runs.Add(new Run(B(t), B(m1), t, m1));
            }
            off += c.Length;
        }
        var merged = new List<(double B0, double B1, List<(double, double)> M)>();
        foreach (var r in runs.OrderBy(r => r.B0))
        {
            if (merged.Count > 0 && r.B0 - merged[^1].B1 < HoldSec)
            {
                var last = merged[^1]; last.M.Add((r.M0, r.M1));
                merged[^1] = (last.B0, Math.Max(last.B1, r.B1), last.M);
            }
            else merged.Add((r.B0, r.B1, [(r.M0, r.M1)]));
        }
        double overall = Level(sig, merged.SelectMany(m => m.M));
        return merged.Where(m => m.B1 - m.B0 > 0.05).Select(m =>
        {
            double l = Level(sig, m.M);
            double local = double.IsNaN(l) || double.IsNaN(overall) ? 0 : Math.Clamp(l - overall, -LocalClampDb, LocalClampDb);
            return new DuckSpan(R(m.B0), R(m.B1), R(local, 1), 0);
        }).ToList();
    }

    /// <summary>Power mean (dBFS) of the 100 ms loudness windows inside the master-time ranges; NaN when none is above −70.</summary>
    public static double Level(SignalSet sig, IEnumerable<(double T0, double T1)> ranges)
    {
        var l = sig.Loudness100Ms; if (l.Count == 0) return double.NaN;
        double sum = 0; int n = 0; int lastIdx = -1;
        foreach (var (a, b) in ranges.OrderBy(r => r.T0))
        {
            for (int i = Math.Max(lastIdx + 1, (int)Math.Floor(a / 0.1)); i < l.Count && (i + 0.5) * 0.1 < b; i++)
            {
                if ((i + 0.5) * 0.1 < a) continue;
                lastIdx = i;
                if (l[i] > -70) { sum += Math.Pow(10, l[i] / 10); n++; }
            }
        }
        return n == 0 ? double.NaN : 10 * Math.Log10(sum / n);
    }

    // ---------------- scenes ----------------

    public sealed record SceneSpan(double T0, double T1, string StartReason);

    static string SceneRole(Clip c) => c.Role is "body" or "reaction" or "wide" ? "body" : c.Role;
    static string RoleName(string r) => r switch { "teaser" => "冒頭の見どころ", "recap" => "前回の振り返り", "next" => "次回予告", _ => "本編" };

    /// <summary>Scene boundaries the video itself has, strongest first, never closer than <paramref name="minScene"/> to each other or to either end.</summary>
    public static List<SceneSpan> Scenes(IReadOnlyList<Clip> clips, IReadOnlyList<Insert> inserts, SeriesInfo? series, double minScene = MinSceneSec)
    {
        var starts = new double[clips.Count + 1];
        for (int i = 0; i < clips.Count; i++) starts[i + 1] = starts[i] + clips[i].Length;
        double total = starts[^1];
        var cand = new List<(double At, int Strength, string Why)>();
        for (int i = 1; i < clips.Count; i++)
        {
            string a = SceneRole(clips[i - 1]), b = SceneRole(clips[i]);
            double jump = Math.Abs(clips[i].MasterIn - clips[i - 1].MasterOut);
            if (a != b) cand.Add((starts[i], 2, $"{RoleName(a)}から{RoleName(b)}へ"));
            else if (clips[i].TransitionSec > 0) cand.Add((starts[i], 1, "場面の切り替え（クロスフェード）"));
            else if (jump > InsertPlanner.JumpSec) cand.Add((starts[i], 1, $"元動画で {jump:F0} 秒先へ飛ぶ所"));
        }
        foreach (var ins in inserts.Where(i => i.Enabled && i.Kind == "card" && !i.Id.StartsWith("tsukkomi", StringComparison.Ordinal)))
            cand.Add((ins.At, 2, "場面転換カード"));
        if (series != null)
            foreach (var ch in series.Chapters)
            {
                var snap = starts.Where(s => Math.Abs(s - ch.Start) <= InsertPlanner.SnapSec).OrderBy(s => Math.Abs(s - ch.Start)).Cast<double?>().FirstOrDefault() ?? ch.Start;
                cand.Add((snap, 2, $"章「{ch.Title}」"));
            }
        var kept = new List<(double At, string Why)>();
        foreach (var c in cand.OrderByDescending(c => c.Strength).ThenBy(c => c.At))
        {
            if (c.At < minScene - 1e-6 || total - c.At < minScene - 1e-6) continue;
            if (kept.Any(k => Math.Abs(k.At - c.At) < minScene - 1e-6)) continue;
            kept.Add((c.At, c.Why));
        }
        kept = kept.OrderBy(k => k.At).ToList();
        var res = new List<SceneSpan>(); double t0 = 0; string why = "冒頭";
        foreach (var k in kept) { res.Add(new SceneSpan(R(t0), R(k.At), why)); t0 = k.At; why = k.Why; }
        res.Add(new SceneSpan(R(t0), R(total), why));
        return res;
    }

    /// <summary>Master-time ranges a body-time span covers.</summary>
    static List<(double T0, double T1)> MasterRanges(IReadOnlyList<Clip> clips, double t0, double t1)
    {
        var res = new List<(double, double)>(); double off = 0;
        foreach (var c in clips)
        {
            double a = Math.Max(t0, off), b = Math.Min(t1, off + c.Length);
            if (b > a) res.Add((c.MasterIn + (a - off) * c.Speed, c.MasterIn + (b - off) * c.Speed));
            off += c.Length;
        }
        return res;
    }

    /// <summary>
    /// Energy 0..1 of a scene on a fixed scale (speech rate 3→9 characters per second of speaking (Japanese conversation is about 5–7), voice level −6→+6 dB against the whole talk,
    /// laughter 0→6 per minute), so the scenes of a calm talk stay calm; <see cref="Assign"/> adds a quarter of how it stands among this edit's scenes.
    /// </summary>
    public static (double Energy, string Text) SceneEnergy(IReadOnlyList<Clip> clips, double t0, double t1, Transcript tr, SignalSet sig, IReadOnlyList<Interval> laughs, IReadOnlyList<DuckSpan> speech, double refLevel)
    {
        static double C(double x) => Math.Clamp(x, 0, 1);
        var ranges = MasterRanges(clips, t0, t1);
        bool In(double m) => ranges.Any(r => m >= r.T0 && m < r.T1);
        var words = tr.Words.Where(w => In((w.T0 + w.T1) / 2)).ToList();
        int chars = words.Sum(w => w.Text.Length);
        double speak = speech.Sum(s => Math.Max(0, Math.Min(t1, s.T1) - Math.Max(t0, s.T0)));
        double rate = speak >= 1 ? chars / speak : 0;
        double level = Level(sig, words.Select(w => (w.T0, w.T1)));
        double loud = double.IsNaN(level) || double.IsNaN(refLevel) ? 0 : level - refLevel;
        double minutes = Math.Max(1.0 / 60, ranges.Sum(r => r.T1 - r.T0) / 60);
        int laughN = laughs.Count(l => In((l.T0 + l.T1) / 2));
        double rateTerm = tr.Words.Count > 0 ? C((rate - 3) / 6) : 0.5;
        double e = 0.45 * rateTerm + 0.35 * C((loud + 6) / 12) + 0.20 * C(laughN / minutes / 6);
        var text = (tr.Words.Count > 0 ? $"話す速さ {rate:F1} 字/秒・" : "") + $"声 {(loud >= 0 ? "+" : "")}{loud:F1} dB・笑い {laughN} 回";
        return (R(e), text);
    }

    // ---------------- choosing tracks ----------------

    public static string SceneWord(double e) => e < 0.35 ? "落ち着いた" : e < 0.6 ? "ふつうの" : "にぎやかな";
    public static string TrackWord(double e) => e < 0.35 ? "ゆったりした" : e < 0.6 ? "ほどよいテンポの" : "アップテンポの";

    /// <summary>Three quarters the fixed-scale energy, one quarter how it stands among the others (z-score, spread floored at 0.1 so near-equal values
    /// are not stretched apart): a slightly livelier stretch of a calm talk stays calm.</summary>
    static double[] Blend(IReadOnlyList<double> abs)
    {
        if (abs.Count < 2) return abs.ToArray();
        double mean = abs.Average(), sd = Math.Max(0.1, Math.Sqrt(abs.Average(x => (x - mean) * (x - mean))));
        return abs.Select(x => 0.75 * x + 0.25 * Math.Clamp(0.5 + 0.25 * (x - mean) / sd, 0, 1)).ToArray();
    }

    /// <summary>
    /// Tracks for the automatic cues (Viterbi over scenes; state = track): cost = |scene energy − track energy| weighted by the scene's length, plus
    /// <see cref="SwitchCost"/> for every change. Cues the user set (Auto = false) keep their track and constrain their neighbours.
    /// </summary>
    public static List<BgmCue> Assign(IReadOnlyList<BgmCue> cues, IReadOnlyList<BgmTrack> pool)
    {
        var tracks = pool.Where(t => t.Analysis != null).GroupBy(t => t.Id).Select(g => g.First()).ToList();
        if (cues.Count == 0 || tracks.Count == 0) return cues.ToList();
        var tScore = Blend(tracks.Select(t => t.Analysis!.Energy).ToList());
        var sScore = Blend(cues.Select(c => c.SceneEnergy).ToList());
        // states: the pool, plus whatever the user fixed (a track outside the pool, or "no music")
        var states = tracks.Select((t, i) => (Id: (string?)t.Id, Score: tScore[i])).ToList();
        foreach (var c in cues.Where(c => !c.Auto && states.All(s => s.Id != c.Track))) states.Add((c.Track, double.NaN));
        double avg = cues.Average(c => Math.Max(0.1, c.T1 - c.T0));
        double Local(int i, int k)
        {
            var c = cues[i];
            if (!c.Auto) return states[k].Id == c.Track ? 0 : double.PositiveInfinity;
            if (double.IsNaN(states[k].Score)) return double.PositiveInfinity;
            return Math.Abs(sScore[i] - states[k].Score) * Math.Max(0.1, c.T1 - c.T0) / avg;
        }
        int n = cues.Count, m = states.Count;
        var cost = new double[n, m]; var back = new int[n, m];
        for (int k = 0; k < m; k++) cost[0, k] = Local(0, k);
        for (int i = 1; i < n; i++)
            for (int k = 0; k < m; k++)
            {
                double best = double.PositiveInfinity; int arg = 0;
                for (int j = 0; j < m; j++)
                {
                    double v = cost[i - 1, j] + (j == k ? 0 : SwitchCost);
                    if (v < best) { best = v; arg = j; }
                }
                cost[i, k] = best + Local(i, k); back[i, k] = arg;
            }
        var path = new int[n];
        double end = double.PositiveInfinity;
        for (int k = 0; k < m; k++) if (cost[n - 1, k] < end) { end = cost[n - 1, k]; path[n - 1] = k; }
        for (int i = n - 1; i > 0; i--) path[i - 1] = back[i, path[i]];
        var res = new List<BgmCue>();
        for (int i = 0; i < n; i++)
        {
            var c = cues[i];
            if (!c.Auto) { res.Add(c); continue; }
            var t = tracks.First(x => x.Id == states[path[i]].Id);
            int bestAlone = Enumerable.Range(0, tracks.Count).OrderBy(k => Math.Abs(sScore[i] - tScore[k])).First();
            var a = t.Analysis!;
            string reason = tracks.Count == 1
                ? "選べる曲が 1 曲のため、この曲を使います"
                : $"{SceneWord(c.SceneEnergy)}場面（{c.Scene}）に、{TrackWord(a.Energy)}曲（{(a.Bpm > 0 ? $"{a.Bpm:F0} BPM・" : "")}音の立ち上がり {a.OnsetRate:F1} 回/秒・明るさ {a.Brightness:F0} Hz）"
                  + (tracks[bestAlone].Id != t.Id ? "。曲の切り替えを減らすため、前後の場面と同じ曲にしました" : "");
            res.Add(Fill(c with { Track = t.Id, Reason = reason }, t));
        }
        return res;
    }

    /// <summary>Copies the track's playable part and loudness into the cue.</summary>
    public static BgmCue Fill(BgmCue c, BgmTrack? t) => t?.Analysis is { } a
        ? c with { Track = t.Id, In = a.Start, Out = a.End, TrackLufs = a.Lufs, TrackEnergy = a.Energy }
        : c with { Track = null };

    // ---------------- building / recomputing the plan's audio ----------------

    /// <summary>
    /// The plan's music: scenes → cues (tracks per <paramref name="mode"/>), speech spans, then levels (<see cref="Recompute"/>).
    /// <paramref name="mode"/> scene = choose from <paramref name="pool"/>; single / beat = <paramref name="pool"/>[0] everywhere (beat: one cue from <paramref name="beatPhase"/>).
    /// </summary>
    public static AudioSpec Build(AudioSpec audio, IReadOnlyList<Clip> clips, IReadOnlyList<Insert> inserts, SeriesInfo? series, Transcript tr, SignalSet sig,
        IReadOnlyList<Interval> laughs, string mode, IReadOnlyList<BgmTrack> pool, double beatPhase, double gapLu, double speechLd, List<string> notes)
    {
        var tracks = pool.Where(t => t.Analysis != null).ToList();
        if (tracks.Count == 0) return audio;
        var speech = SpeechSpans(clips, tr, sig);
        var scenes = mode == "beat" ? [new SceneSpan(0, R(clips.Sum(c => c.Length)), "冒頭")] : Scenes(clips, inserts, series);
        double refLevel = Level(sig, tr.Words.Select(w => (w.T0, w.T1)));
        var cues = scenes.Select((s, i) =>
        {
            var (e, text) = SceneEnergy(clips, s.T0, s.T1, tr, sig, laughs, speech, refLevel);
            return new BgmCue { Id = $"bgm{i + 1}", T0 = s.T0, T1 = s.T1, SceneEnergy = e, Scene = (i == 0 ? "" : s.StartReason + "・") + text };
        }).ToList();
        if (mode == "scene") cues = Assign(cues, tracks);
        else
        {
            var t = tracks[0];
            cues = cues.Select(c => Fill(c with { Reason = mode == "beat" ? "拍に合わせて編集した曲" : "全編この曲（生成時の指定）", Offset = mode == "beat" ? beatPhase : 0 }, t)).ToList();
            if (mode == "beat") cues = cues.Select(c => c with { In = 0, Out = t.Analysis!.Duration }).ToList();
        }
        var speechLevel = Level(sig, speech.SelectMany(s => MasterRanges(clips, s.T0, s.T1)));
        var res = audio with
        {
            BgmCues = cues, BgmMode = mode, DuckSpans = speech, BgmGapLu = gapLu, BgmSpeechLd = speechLd,
            Bgm = cues.GroupBy(c => c.Track).Where(g => g.Key != null).OrderByDescending(g => g.Sum(c => c.T1 - c.T0)).Select(g => g.Key).FirstOrDefault(),
            SpeechLufs = double.IsNaN(speechLevel) ? null : R(speechLevel, 1),
        };
        if (mode == "scene")
        {
            int switches = cues.Zip(cues.Skip(1)).Count(p => p.First.Track != p.Second.Track);
            notes.Add($"BGM: {cues.Count} 場面に分け、場面の勢いに合う曲を {tracks.Count} 曲から選びました（曲の切り替え {switches} 回）");
        }
        notes.Add($"BGM の音量: 話している間は声より {speechLd:F0} LU 以上小さく、声のない所は声より {-gapLu:F0} LU 小さく（{speech.Count} か所の発話に合わせて自動で下げます）");
        return res;
    }

    /// <summary>A cue on the final timeline: [Start, End) with cross-fades centred on Start / End, and the track position at Start.</summary>
    public sealed record CueSpan(BgmCue Cue, double Start, double End, double FadeIn, double FadeOut, bool CoherentIn, bool CoherentOut, double Position);

    /// <summary>
    /// Cues laid end to end on the final timeline (the first from 0, the last to the end). A track change at a muted insert (bumper, card with its own music)
    /// happens entirely under it; elsewhere it is an equal-power cross-fade of <see cref="CrossfadeSec"/>. The same track continuing into the next scene keeps
    /// playing from where it was (a short linear fade between two copies at the same position is seamless); a track that comes back resumes where it stopped.
    /// </summary>
    public static List<CueSpan> FinalSpans(IReadOnlyList<BgmCue> cues, IReadOnlyList<Insert> active, double totalFinal)
    {
        var list = cues.OrderBy(c => c.T0).ToList();
        int n = list.Count;
        if (n == 0) return [];
        var center = new double[n + 1]; var xf = new double[n + 1];
        center[0] = 0; center[n] = totalFinal;
        for (int i = 1; i < n; i++)
        {
            double b = list[i].T0, after = InsertMap.ToFinal(active, b);
            var at = active.Where(x => Math.Abs(x.At - b) < 1e-3).ToList();
            double fade = list[i - 1].Track == list[i].Track ? SameTrackFadeSec : CrossfadeSec;
            if (at.Count > 0 && at.All(Mutes))
            {
                double before = after - at.Sum(x => x.Duration);
                center[i] = (before + after) / 2; fade = Math.Min(fade, after - before);
            }
            else center[i] = after;
            xf[i] = fade;
        }
        for (int i = 1; i < n; i++) xf[i] = Math.Min(xf[i], Math.Min(center[i] - center[i - 1], center[i + 1] - center[i]));
        var res = new List<CueSpan>();
        var resume = new Dictionary<string, double>();
        for (int i = 0; i < n; i++)
        {
            var c = list[i];
            double L = Math.Max(0.5, c.Out - c.In), pos;
            if (c.Track == null) pos = 0;
            else if (i > 0 && list[i - 1].Track == c.Track) pos = Mod(res[i - 1].Position + (center[i] - center[i - 1]), L);
            else pos = resume.TryGetValue(c.Track, out var r) ? r : Mod(c.Offset, L);
            if (c.Track != null) resume[c.Track] = Mod(pos + (center[i + 1] - center[i]), L);
            res.Add(new CueSpan(c, R(center[i]), R(center[i + 1]), i > 0 ? R(xf[i]) : 0, i < n - 1 ? R(xf[i + 1]) : 0,
                i > 0 && list[i - 1].Track == c.Track, i < n - 1 && list[i + 1].Track == c.Track, R(pos)));
        }
        return res;
    }

    static double Mod(double x, double L) => ((x % L) + L) % L;

    /// <summary>File position (seconds) of the cue's track at final time <paramref name="t"/>.</summary>
    public static double TrackPosition(CueSpan s, double t)
    {
        double L = Math.Max(0.5, s.Cue.Out - s.Cue.In);
        return s.Cue.In + Mod(s.Position + (t - s.Start), L);
    }

    public static double TotalFinal(double bodyDuration, IReadOnlyList<Insert> active) => bodyDuration + active.Sum(i => i.Duration);

    /// <summary>
    /// Re-derives every duck depth from the current settings: max(0, gap + target − local voice level) plus the "ride" — how much louder than its own average
    /// the song is wherever it plays under that speech (from the track's short-term loudness), capped at <see cref="MaxRideDb"/>.
    /// </summary>
    public static AudioSpec Recompute(AudioSpec a, IReadOnlyList<Insert> inserts, double bodyDuration, Func<string, BgmTrack?> find)
    {
        if (a.BgmCues.Count == 0) return a;
        var active = InsertMap.Active(inserts);
        var spans = FinalSpans(a.BgmCues, active, TotalFinal(bodyDuration, active));
        var analyses = a.BgmCues.Where(c => c.Track != null).Select(c => c.Track!).Distinct().ToDictionary(id => id, id => find(id)?.Analysis);
        double Ride(DuckSpan d)
        {
            double ride = 0;
            foreach (var (s0, s1) in InsertMap.Span(active, d.T0, d.T1))
                for (double t = s0; t <= s1 + 1e-6; t += 0.5)
                {
                    var cs = spans.LastOrDefault(x => x.Start <= t + 1e-6) ?? spans[0];
                    if (cs.Cue.Track is not { } id || analyses.GetValueOrDefault(id) is not { } an) continue;
                    ride = Math.Max(ride, BgmAnalyzer.ShortTermAt(an, TrackPosition(cs, Math.Min(t, s1))) - cs.Cue.TrackLufs);
                }
            return Math.Clamp(ride, 0, MaxRideDb);
        }
        var duck = a.DuckSpans.Select(d => d with
        {
            Db = a.Duck ? R(Math.Min(MaxDuckDb, Math.Max(0, a.BgmGapLu + a.BgmSpeechLd - d.LocalDb) + Ride(d)), 1) : 0,
        }).ToList();
        return a with { DuckSpans = duck };
    }

    /// <summary>The cue's gain (dB) on the track as recorded: brings it to SpeechLufs + gap level + the user's trim.</summary>
    public static double CueGainDb(AudioSpec a, BgmCue c, double speechLufs) => R(Math.Clamp(speechLufs + a.BgmGapLu + c.TrimDb - c.TrackLufs, -60, 24), 2);

    /// <summary>
    /// Music gain over the final timeline, <paramref name="rate"/> values per second: duck spans (linear-in-dB ramps: <see cref="AttackSec"/> before the
    /// speech, <see cref="ReleaseSec"/> after), mutes (ramped over <see cref="MuteRampSec"/>), fade in at the start, fade out into <paramref name="musicEnd"/>.
    /// <paramref name="finalAudio"/> is Renderer.FinalAudio (spans and mutes already on the final timeline).
    /// </summary>
    public static float[] Envelope(AudioSpec finalAudio, double total, double musicEnd, int rate)
    {
        int n = (int)Math.Ceiling(total * rate) + 1;
        var att = new double[n];
        int Idx(double t) => Math.Clamp((int)Math.Round(t * rate), 0, n - 1);
        foreach (var d in finalAudio.DuckSpans.Where(d => d.Db > 0.01))
        {
            int a0 = Idx(d.T0 - AttackSec), a1 = Idx(d.T0), b1 = Idx(d.T1), b2 = Idx(d.T1 + ReleaseSec);
            for (int i = a0; i < a1; i++) att[i] = Math.Max(att[i], d.Db * (1 - (a1 - i) / (double)Math.Max(1, a1 - a0)));
            for (int i = a1; i <= b1; i++) att[i] = Math.Max(att[i], d.Db);
            for (int i = b1 + 1; i <= b2; i++) att[i] = Math.Max(att[i], d.Db * (1 - (i - b1) / (double)Math.Max(1, b2 - b1)));
        }
        var g = new float[n];
        int ramp = Math.Max(1, (int)Math.Round(MuteRampSec * rate));
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)rate, v = Math.Pow(10, -att[i] / 20);
            if (t < FadeInSec) v *= t / FadeInSec;
            if (t >= musicEnd) v = 0;
            else if (t > musicEnd - FadeOutSec) v *= (musicEnd - t) / FadeOutSec;
            g[i] = (float)v;
        }
        foreach (var m in finalAudio.BgmMutes)
        {
            int m0 = Idx(m.T0), m1 = Idx(m.T1);
            for (int i = Math.Max(0, m0 - ramp); i < m0; i++) g[i] *= (float)((m0 - i) / (double)ramp);
            for (int i = m0; i <= m1; i++) g[i] = 0;
            for (int i = m1 + 1; i <= Math.Min(n - 1, m1 + ramp); i++) g[i] *= (float)((i - m1) / (double)ramp);
        }
        return g;
    }

    /// <summary>Keeps the user's choices (tracks set by hand, trims, levels, on/off) when a plan is rebuilt after a range adjustment.</summary>
    public static AudioSpec Carry(AudioSpec rebuilt, AudioSpec previous)
    {
        if (rebuilt.BgmCues.Count == 0 || previous.BgmCues.Count == 0) return rebuilt;
        BgmCue? Match(BgmCue c) => previous.BgmCues
            .Select(p => (p, ov: Math.Min(c.T1, p.T1) - Math.Max(c.T0, p.T0)))
            .Where(x => x.ov >= 0.5 * (c.T1 - c.T0)).OrderByDescending(x => x.ov).Select(x => x.p).FirstOrDefault();
        var cues = rebuilt.BgmCues.Select(c => Match(c) is { } p
            ? (p.Auto ? c with { TrimDb = p.TrimDb } : c with { Track = p.Track, In = p.In, Out = p.Out, TrackLufs = p.TrackLufs, TrackEnergy = p.TrackEnergy, TrimDb = p.TrimDb, Auto = false, Reason = p.Reason })
            : c).ToList();
        return rebuilt with { BgmCues = cues, Bgm = previous.Bgm == null ? null : rebuilt.Bgm ?? previous.Bgm, BgmGapLu = previous.BgmGapLu, BgmSpeechLd = previous.BgmSpeechLd };
    }
}
