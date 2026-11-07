using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Signals;

namespace VideoCreator.Planning;

/// <summary>Non-speech loud bursts (laughter, applause, impacts): loud windows outside word times. Verified in spikes/10-sync.cs.</summary>
public static class NonSpeech
{
    public static List<Interval> Bursts(SignalSet sig, Transcript tr, double minLen = 0.5)
    {
        var res = new List<Interval>();
        if (sig.Loudness100Ms.Count == 0 || tr.Words.Count == 0) return res;
        double thr = Math.Max(-45, sig.MedianLoudness - 8);
        Interval? cur = null;
        for (int i = 0; i < sig.Loudness100Ms.Count; i++)
        {
            double t0 = i * 0.1, t1 = t0 + 0.1;
            bool loud = sig.Loudness100Ms[i] > thr && !tr.Words.Any(w => w.T1 + 0.15 > t0 && w.T0 - 0.15 < t1);
            if (loud) cur = cur == null ? new Interval(t0, t1) : cur with { T1 = t1 };
            else if (cur != null) { if (res.Count > 0 && cur.T0 - res[^1].T1 <= 0.3) res[^1] = res[^1] with { T1 = cur.T1 }; else res.Add(cur); cur = null; }
        }
        if (cur != null) res.Add(cur);
        return res.Where(x => x.Length >= minLen).ToList();
    }
}

/// <summary>P7 multi-camera switching (FR-SWT). Master audio time is the spine; each camera is read at master + SyncOffset.</summary>
public static class SwitchPlanner
{
    sealed record Turn(double T0, double T1, int Speaker);

    public static int SpeakerOf(SourceAsset a) => a.Speaker ?? a.Role switch { AssetRole.Main => 1, AssetRole.CamA => 1, AssetRole.CamB => 2, _ => 0 };

    public static List<Clip> Plan(IReadOnlyList<Clip> speech, Transcript tr, IReadOnlyList<SourceAsset> assets, SourceAsset master, Preset p, IReadOnlyList<Interval> bursts, List<string> notes)
    {
        var sw = p.Switching ?? new PresetSwitching();
        var cams = assets.Where(a => SpeakerOf(a) > 0).GroupBy(SpeakerOf).ToDictionary(g => g.Key, g => g.First());
        var wide = assets.FirstOrDefault(a => a.Role == AssetRole.Wide);
        var clips = new List<Clip>(); int ci = 0; double outT = 0, nextWide = sw.WideEverySec;

        SourceAsset Cam(int speaker) => cams.TryGetValue(speaker, out var a) ? a : wide ?? master;
        Clip Make(SourceAsset cam, double m0, double m1, string role, double zoom = 1.0)
        {
            double off = cam.Id == master.Id ? 0 : cam.SyncOffset;
            double s0 = m0 + off, s1 = m1 + off;
            if (s0 < 0 || s1 > cam.Media.Duration + 0.01) { cam = master; s0 = m0; s1 = m1; notes.Add($"{m0:F1}s は同期後のカメラ映像が範囲外のため、基準映像を使いました"); }
            return new Clip { ClipId = $"c{++ci}", AssetId = cam.Id, SrcIn = Math.Round(s0, 3), SrcOut = Math.Round(s1, 3), AudioIn = Math.Round(m0, 3), Role = role, Zoom = zoom, Reframe = new ReframeSpec { Mode = cam.Media.Aspect < 0.9 ? "fit" : "blur_bg" } };
        }

        foreach (var sc in speech)
        {
            double m0 = sc.MasterIn, m1 = sc.MasterOut;
            string baseRole = sc.Role == "teaser" ? "teaser" : "body";
            // speaker turns from sentences, clipped to this speech clip
            var turns = new List<Turn>();
            foreach (var s in tr.Sentences.Where(s => s.T1 > m0 && s.T0 < m1).OrderBy(s => s.T0))
            {
                double a = Math.Max(m0, s.T0), b = Math.Min(m1, s.T1);
                if (turns.Count > 0 && turns[^1].Speaker == s.Speaker) turns[^1] = turns[^1] with { T1 = b };
                else turns.Add(new Turn(a, b, s.Speaker));
            }
            if (turns.Count == 0) { turns.Add(new Turn(m0, m1, 0)); }
            // cover gaps: the first turn starts at the clip start, the last ends at its end, turns are contiguous
            for (int i = 0; i < turns.Count; i++)
            {
                double a = i == 0 ? m0 : turns[i].T0, b = i == turns.Count - 1 ? m1 : turns[i + 1].T0;
                turns[i] = new Turn(a, Math.Max(a, b), turns[i].Speaker);
            }
            // absorb turns shorter than the minimum hold (back-channels) into the previous (or next) turn
            for (bool again = true; again && turns.Count > 1;)
            {
                again = false;
                for (int i = 0; i < turns.Count; i++)
                {
                    if (turns[i].T1 - turns[i].T0 >= sw.MinHoldSec) continue;
                    int j = i > 0 ? i - 1 : i + 1; int lo = Math.Min(i, j), hi = Math.Max(i, j);
                    var keep = turns[j];
                    turns[lo] = new Turn(turns[lo].T0, turns[hi].T1, keep.Speaker); turns.RemoveAt(hi);
                    again = true; break;
                }
            }
            for (int ti = 0; ti < turns.Count; ti++)
            {
                var t = turns[ti];
                var cam = Cam(t.Speaker);
                var pieces = new List<(double a, double b, SourceAsset cam, string role)> { (t.T0, t.T1, cam, baseRole) };
                // reaction shot of the other speaker on a non-speech burst (laughter) inside this turn
                var other = cams.Where(kv => kv.Key != t.Speaker).Select(kv => kv.Value).FirstOrDefault();
                foreach (var b in bursts.Where(b => b.T0 >= t.T0 + sw.MinHoldSec && b.T0 + sw.ReactionSec <= t.T1 - sw.MinHoldSec))
                {
                    if (other == null) break;
                    var last = pieces[^1];
                    if (b.T0 - last.a < sw.MinHoldSec) continue;
                    pieces[^1] = (last.a, b.T0, last.cam, baseRole);
                    pieces.Add((b.T0, b.T0 + sw.ReactionSec, other, "reaction"));
                    pieces.Add((b.T0 + sw.ReactionSec, last.b, last.cam, baseRole));
                }
                foreach (var pc in pieces)
                {
                    double a = pc.a;
                    // opening wide shot and a periodic wide shot (cadence for the viewer)
                    if (wide != null && !clips.Any(c => c.Role == "body") && sw.WideOpenSec > 0 && pc.b - a > sw.WideOpenSec + sw.MinHoldSec)
                    {
                        clips.Add(Make(wide, a, a + sw.WideOpenSec, "wide")); outT += sw.WideOpenSec; a += sw.WideOpenSec; notes.Add("冒頭に引きの映像を挿入");
                    }
                    else if (wide != null && outT >= nextWide && pc.role == "body" && pc.b - a > 2.0 + sw.MinHoldSec)
                    {
                        clips.Add(Make(wide, a, a + 2.0, "wide")); outT += 2.0; a += 2.0; nextWide = outT + sw.WideEverySec;
                    }
                    if (pc.b - a > 0.05) { clips.Add(Make(pc.cam, a, pc.b, pc.role)); outT += pc.b - a; }
                }
            }
        }
        // consecutive clips on the same camera and contiguous in master time are merged (no visible cut)
        var merged = new List<Clip>();
        foreach (var c in clips)
        {
            if (merged.Count > 0 && merged[^1].AssetId == c.AssetId && merged[^1].Role == c.Role && Math.Abs(merged[^1].MasterOut - c.MasterIn) < 0.002 && Math.Abs(merged[^1].SrcOut - c.SrcIn) < 0.002)
                merged[^1] = merged[^1] with { SrcOut = c.SrcOut };
            else merged.Add(c);
        }
        return merged.Select((c, i) => c with { ClipId = $"c{i + 1}" }).ToList();
    }
}

/// <summary>P8 time-lapse (FR-SPD): idle stretches are sped up, speech stays 1×; the finished result is shown first as a teaser.</summary>
public static class SpeedPlanner
{
    public static List<Candidate> Candidates(Transcript tr, SignalSet sig, MediaInfo media, Preset p, int count = 3)
    {
        var su = p.Cut.SpeedUp ?? new SpeedUp();
        double target = (p.Target.Min + p.Target.Max) / 2;
        double maxSrc = Math.Min(media.Duration, target * su.MaxFactor * 0.8);
        var list = new List<Candidate>();
        double step = Math.Max(maxSrc / 2, 30);
        for (double s = 0; s < media.Duration - 10 && list.Count < 12; s += step)
        {
            double e = Math.Min(media.Duration, s + maxSrc);
            if (e - s < 10) break;
            var iv = new Interval(Math.Round(s, 1), Math.Round(e, 1));
            double energy = SignalExtractor.EnergyScore(sig, iv), speech = tr.Words.Count(w => w.T0 >= iv.T0 && w.T1 <= iv.T1);
            list.Add(new Candidate
            {
                Id = $"s{list.Count + 1}", Segments = [iv], Source = "speed", Reason = "長い作業・工程を早送りでまとめる区間",
                HookText = "", Headline = "", Scores = new Scores { Signal = Math.Round(energy, 2), Final = Math.Round(energy + Math.Min(5, speech / 40.0), 2) },
            });
            if (e >= media.Duration) break;
        }
        return list.OrderByDescending(c => c.Scores.Final).Take(count).Select((c, i) => c with { Rank = i + 1 }).ToList();
    }

    public static List<Clip> Plan(Interval span, Transcript tr, Preset p, string assetId, MediaInfo media, List<string> notes)
    {
        var su = p.Cut.SpeedUp ?? new SpeedUp();
        double target = (p.Target.Min + p.Target.Max) / 2, minShot = Math.Max(0.5, p.Cut.MinShotSec);
        // every boundary is moved out of a word (H-06): the speech stays intact and the cuts fall in gaps
        double Snap(double t)
        {
            var w = tr.Words.FirstOrDefault(w => t - w.T0 > 0.15 && w.T1 - t > 0.15);
            return w == null ? t : (t - w.T0 <= w.T1 - t ? w.T0 : w.T1);
        }
        double s1 = Snap(span.T1), s0 = Snap(span.T0);
        double tail = Math.Min(su.TailSec, (s1 - s0) / 4), teaser = Math.Min(su.TeaserSec, (s1 - s0) / 8);
        double tailStart = Snap(s1 - tail), teaserStart = Snap(s1 - teaser);
        double SpeechIn(double a, double b) => tr.Words.Where(w => w.T1 > a && w.T0 < b).Sum(w => Math.Min(b, w.T1) - Math.Max(a, w.T0));
        List<(double a, double b, bool speech)> Windows(double from, double to)
        {
            var bounds = new List<double> { from };
            for (double t = from + 1.0; t < to - 0.5; t += 1.0) { var s = Snap(t); if (s > bounds[^1] + 0.2 && s < to - 0.2) bounds.Add(s); }
            bounds.Add(to);
            return bounds.Zip(bounds.Skip(1), (a, b) => (a, b, SpeechIn(a, b) > 0.25 * (b - a))).ToList();
        }
        double bodyBudget = target - (s1 - teaserStart) - (s1 - tailStart);
        List<(double a, double b, bool speech)> win; double factor;
        while (true)
        {
            win = Windows(s0, tailStart);
            double Factor()
            {
                double idle = win.Where(w => !w.speech).Sum(w => w.b - w.a), speechOut = win.Where(w => w.speech).Sum(w => w.b - w.a) / Math.Max(1.0, su.SpeechFactor);
                return idle <= 0 ? 1 : idle / Math.Max(0.5, bodyBudget - speechOut);
            }
            // an idle stretch that would shrink below the minimum shot length stays at normal speed (no flicker between 1x and fast)
            for (int pass = 0; pass < 3; pass++)
            {
                factor = Factor();
                var runs = GroupRuns(win).ToList(); var merged = new List<(double a, double b, bool speech)>();
                foreach (var r in runs) merged.Add(!r.speech && (r.b - r.a) / Math.Max(1, factor) < minShot ? (r.a, r.b, true) : r);
                win = merged.SelectMany(r => win.Where(w => w.a >= r.a - 1e-9 && w.b <= r.b + 1e-9).Select(w => (w.a, w.b, r.speech))).ToList();
            }
            factor = Factor();
            double speechTotal = win.Where(w => w.speech).Sum(w => w.b - w.a) / Math.Max(1.0, su.SpeechFactor);
            if (bodyBudget - speechTotal <= 0.5)
            {   // too much speech to fit: speaking faster is not allowed, so trim the start
                if (tailStart - s0 <= 10) break; s0 = Snap(s0 + 1); continue;
            }
            if (factor <= su.MaxFactor || tailStart - s0 <= 10) break;
            s0 = Snap(s0 + Math.Max(1, (tailStart - s0) * 0.05));
        }
        factor = Math.Clamp(factor, 1.0, su.MaxFactor);
        notes.Add($"待ち時間を最大 {factor:F1} 倍速で圧縮（発話中は {su.SpeechFactor:F1} 倍）");
        var clips = new List<Clip>(); int ci = 0;
        double R(double x) => Math.Round(x, 3);
        var reframe = new ReframeSpec { Mode = media.Aspect < 0.9 ? "fit" : "blur_bg" };
        clips.Add(new Clip { ClipId = $"c{++ci}", AssetId = assetId, SrcIn = R(teaserStart), SrcOut = R(s1), Reframe = reframe, Role = "teaser" });
        foreach (var grp in GroupRuns(win))
        {
            double sp = grp.speech ? su.SpeechFactor : factor;
            clips.Add(new Clip { ClipId = $"c{++ci}", AssetId = assetId, SrcIn = R(grp.a), SrcOut = R(grp.b), Speed = Math.Round(sp, 2), Reframe = reframe });
        }
        clips.Add(new Clip { ClipId = $"c{++ci}", AssetId = assetId, SrcIn = R(tailStart), SrcOut = R(s1), Reframe = reframe });
        return clips;
    }

    static IEnumerable<(double a, double b, bool speech)> GroupRuns(List<(double a, double b, bool speech)> w)
    {
        if (w.Count == 0) yield break;
        var cur = w[0];
        foreach (var x in w.Skip(1))
        {
            if (x.speech == cur.speech) cur = (cur.a, x.b, cur.speech);
            else { yield return cur; cur = x; }
        }
        yield return cur;
    }
}

/// <summary>P5 beat-synced montage (FR-BGM-03): every clip is a whole number of beats, so cuts land on the music's grid.</summary>
public static class BeatPlanner
{
    public static List<Clip> Plan(IReadOnlyList<Interval> segments, SignalSet sig, Transcript tr, Preset p, string assetId, MediaInfo media, double bpm, List<string> notes)
    {
        double beat = 60.0 / bpm;
        int minBeats = (int)Math.Ceiling(Math.Max(0.5, p.Cut.MinShotSec) / beat);
        int[] pattern = [4, 2, 2, 4, 2, 2, 2, 4];
        double target = (p.Target.Min + p.Target.Max) / 2;
        int bars = Math.Max(1, (int)Math.Round(target / (4 * beat)));
        int beatsLeft = bars * 4;
        var clips = new List<Clip>(); int ci = 0, pi = 0;
        var reframe = new ReframeSpec { Mode = media.Aspect < 0.9 ? "fit" : "blur_bg" };
        var anchors = new List<double>();
        long cumFrames = 0; int cumBeats = 0; const int fps = 30;
        foreach (var s in segments)
        {
            double t = s.T0;
            // start each clip on a shot boundary or energy peak when one is near, else on the running position
            while (beatsLeft >= minBeats && t < s.T1 - minBeats * beat)
            {
                int k = Math.Max(minBeats, pattern[pi++ % pattern.Length]); k = Math.Min(k, beatsLeft);
                // whole frames, rounded from the cumulative beat position so the cut grid never drifts from the BGM grid
                long frames = (long)Math.Round((cumBeats + k) * beat * fps) - (long)Math.Round(cumBeats * beat * fps);
                double len = frames / (double)fps;
                if (t + len > s.T1) break;
                double a = t;
                var near = sig.Shots.Where(x => x >= t && x <= t + 1.0).Cast<double?>().FirstOrDefault();
                if (near != null && near + len <= s.T1) a = near.Value;
                a = AvoidWordEdges(a, len, tr);
                clips.Add(new Clip { ClipId = $"c{++ci}", AssetId = assetId, SrcIn = Math.Round(a, 3), SrcOut = Math.Round(a + len, 3), Reframe = reframe, Zoom = clips.Count % 2 == 1 ? p.Cut.PunchInScale : 1.0 });
                beatsLeft -= k; cumBeats += k; cumFrames += frames; t = a + len;
            }
        }
        notes.Add($"BGM {bpm:F0} BPM の拍に合わせてカット（{clips.Count} カット、1拍={beat:F2}秒）");
        return clips;
    }

    /// <summary>Shift a clip (both edges together, so its length stays a whole number of beats) off mid-word positions when possible.</summary>
    static double AvoidWordEdges(double a, double len, Transcript tr)
    {
        bool Mid(double t) => tr.Words.Any(w => t - w.T0 > 0.15 && w.T1 - t > 0.15);
        if (!Mid(a) && !Mid(a + len)) return a;
        for (double d = 0.05; d <= 0.5; d += 0.05)
            foreach (var s in new[] { d, -d })
                if (a + s >= 0 && !Mid(a + s) && !Mid(a + s + len)) return a + s;
        return a;
    }
}

/// <summary>
/// FR-SIG-04: laughter = a loud non-speech burst whose envelope pulses at the "ha-ha-ha" rate (≈3–8 Hz).
/// Applause is noise-like (no regular pulsing) and an impact is a single peak, so both are rejected.
/// </summary>
public static class LaughDetector
{
    public static bool IsLaughter(float[] pcm, int rate)
    {
        int hop = rate / 100, n = pcm.Length / hop;          // 10 ms envelope
        if (n < 40) return false;
        var env = new double[n];
        for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < hop; j++) { double v = pcm[i * hop + j]; s += v * v; } env[i] = Math.Sqrt(s / hop); }
        // remove the slow trend (0.5 s moving average), keep the pulsing
        int w = 25; var d = new double[n];
        for (int i = 0; i < n; i++) { int a = Math.Max(0, i - w), b = Math.Min(n - 1, i + w); double m = 0; for (int k = a; k <= b; k++) m += env[k]; m /= b - a + 1; d[i] = env[i] - m; }
        double mean = env.Average(); if (mean < 1e-6) return false;
        double var0 = d.Sum(x => x * x) / n;
        if (Math.Sqrt(var0) / mean < 0.2) return false;       // not pulsing enough
        double best = 0;
        for (int lag = 12; lag <= 35; lag++)                  // 0.12–0.35 s period
        {
            double c = 0; for (int i = lag; i < n; i++) c += d[i] * d[i - lag];
            best = Math.Max(best, c / ((n - lag) * Math.Max(var0, 1e-12)));
        }
        return best >= 0.3;
    }

    public static async Task<List<Interval>> DetectAsync(Media.FFmpeg ff, string audioFile, IReadOnlyList<Interval> bursts, CancellationToken ct)
    {
        var res = new List<Interval>();
        foreach (var b in bursts.Take(60))
        {
            var pcm = await Audio.AudioSync.LoadAsync(ff, audioFile, 8000, 0, ct, b.T0, b.Length);
            if (IsLaughter(pcm, 8000)) res.Add(b);
        }
        return res;
    }
}
