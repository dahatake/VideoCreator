using System.Globalization;
using VideoCreator.Media;
using VideoCreator.Models;

namespace VideoCreator.Audio;

/// <summary>
/// BGM loops and sound effects are SYNTHESISED with FFmpeg (no third-party audio), so there is no licence to track
/// (PRD RK-08, FR-AUD-05). Recipes are the ones verified in spikes/08-audio.cs.
/// </summary>
public sealed class AudioLibrary(FFmpeg ff, string dir)
{
    const string Recipe = "v1";
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    readonly SemaphoreSlim _gate = new(1, 1);
    bool _ready;

    public sealed record Spec(string Id, string Name, int Bpm, int Bars, double[] Roots, int[] Arp, double Hat, double Bass);

    public static readonly IReadOnlyList<Spec> TrackSpecs =
    [
        new("upbeat", "アップテンポ（120 BPM）", 120, 8, [55, 43.65, 65.41, 49], [0, 7, 12, 7], 0.12, 0.25),
        new("chill", "チル（90 BPM）", 90, 6, [55, 49, 43.65, 49], [0, 3, 7, 3], 0.05, 0.22),
        new("hype", "ハイプ（140 BPM）", 140, 8, [65.41, 55, 49, 55], [0, 12, 7, 12], 0.16, 0.28),
    ];

    public static readonly IReadOnlyDictionary<string, (string Expr, double Dur)> SfxSpecs = new Dictionary<string, (string, double)>
    {
        ["pop"] = ("0.7*sin(2*PI*(900-500*t/0.12)*t)*exp(-t*28)", 0.15),
        ["ding"] = ("0.5*(sin(2*PI*1760*t)+0.4*sin(2*PI*3520*t))*exp(-t*6)", 0.9),
        ["whoosh"] = ("0.6*(random(0)*2-1)*sin(PI/2*min(1,t/0.06))*exp(-t*5)", 0.45),
        ["boom"] = ("0.9*sin(2*PI*(70-30*t)*t)*exp(-t*4.5)+0.3*(random(0)*2-1)*exp(-t*14)", 1.2),
        ["hit"] = ("0.8*(random(0)*2-1)*exp(-t*55)+0.4*sin(2*PI*180*t)*exp(-t*30)", 0.2),
    };

    public IReadOnlyList<BgmTrack> Tracks => TrackSpecs.Select(s => new BgmTrack
    {
        Id = s.Id, Name = s.Name, Path = TrackPath(s.Id), Bpm = s.Bpm, Phase = 0, Source = "library", RightsConfirmed = true,
    }).ToList();

    public string TrackPath(string id) => Path.Combine(dir, $"bgm_{id}.wav");
    public string SfxPath(string kind) => Path.Combine(dir, $"sfx_{kind}.wav");
    public static bool IsSfx(string kind) => SfxSpecs.ContainsKey(kind);

    public BgmTrack? Find(string? id) => id == null ? null : Tracks.FirstOrDefault(t => t.Id == id);

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (_ready) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_ready) return;
            Directory.CreateDirectory(dir);
            var stamp = Path.Combine(dir, ".recipe");
            bool fresh = File.Exists(stamp) && File.ReadAllText(stamp) == Recipe
                && TrackSpecs.All(s => File.Exists(TrackPath(s.Id))) && SfxSpecs.Keys.All(k => File.Exists(SfxPath(k)));
            if (!fresh)
            {
                await Task.WhenAll(TrackSpecs.Select(s => MakeTrackAsync(s, ct)).Concat(SfxSpecs.Select(kv => MakeSfxAsync(kv.Key, kv.Value.Expr, kv.Value.Dur, ct))));
                await File.WriteAllTextAsync(stamp, Recipe, ct);
            }
            _ready = true;
        }
        finally { _gate.Release(); }
    }

    static string F(double x) => x.ToString("0.####", IC);

    async Task MakeTrackAsync(Spec s, CancellationToken ct)
    {
        double B = 60.0 / s.Bpm;
        string bf = "(" + string.Join("", s.Roots.Select((r, i) => $"if(eq(mod(floor(t/{F(4 * B)}),{s.Roots.Length}),{i}),{F(r)},")) + F(s.Roots[^1]) + new string(')', s.Roots.Length) + ")";
        string ar = "(" + string.Join("", s.Arp.Select((o, i) => $"if(eq(mod(floor(t/{F(B / 2)}),{s.Arp.Length}),{i}),{o},")) + s.Arp[^1] + new string(')', s.Arp.Length) + ")";
        string kick = $"0.9*sin(2*PI*(50+120*exp(-mod(t,{F(B)})*25))*mod(t,{F(B)}))*exp(-mod(t,{F(B)})*7)";
        string hh = $"{F(s.Hat)}*(random(0)*2-1)*exp(-mod(t+{F(B / 2)},{F(B)})*70)";
        string bs = $"{F(s.Bass)}*sin(2*PI*{bf}*t)*(0.6+0.4*exp(-mod(t,{F(B)})*3))";
        string pad = $"0.05*(sin(2*PI*{bf}*4*t)+sin(2*PI*{bf}*4*1.1892*t)+sin(2*PI*{bf}*4*1.4983*t))";
        string mel = $"0.09*sin(2*PI*{bf}*8*pow(2,{ar}/12)*t)*exp(-mod(t,{F(B / 2)})*9)";
        var tmp = TrackPath(s.Id) + ".tmp.wav";
        await ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"aevalsrc='{kick}+{hh}+{bs}+{pad}+{mel}':s=48000:d={F(s.Bars * 4 * B)}", "-ac", "2", "-af", "alimiter=limit=0.9", tmp], ct);
        File.Move(tmp, TrackPath(s.Id), true);
    }

    async Task MakeSfxAsync(string kind, string expr, double dur, CancellationToken ct)
    {
        var tmp = SfxPath(kind) + ".tmp.wav";
        await ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"aevalsrc='{expr}':s=48000:d={F(dur)}", "-ac", "2", tmp], ct);
        File.Move(tmp, SfxPath(kind), true);
    }
}

/// <summary>Tempo and beat phase from the onset envelope (verified on the library tracks in spikes/08-audio.cs: ±0.4 BPM, ≤10 ms).</summary>
public static class TempoEstimator
{
    public static (double Bpm, double Phase) Estimate(short[] pcm16k)
    {
        const int hop = 160;                       // 10 ms @ 16 kHz
        int n = pcm16k.Length / hop;
        if (n < 400) return (0, 0);
        var env = new double[n];
        for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < hop; j++) { double v = pcm16k[i * hop + j] / 32768.0; s += v * v; } env[i] = Math.Sqrt(s / hop); }
        var onset = new double[n];
        for (int i = 1; i < n; i++) onset[i] = Math.Max(0, env[i] - env[i - 1]);
        double C(int lag) { double acc = 0; for (int i = lag; i < n; i++) acc += onset[i] * onset[i - lag]; return acc / (n - lag); }
        double best = 0; int bestLag = 0;
        for (int lag = 33; lag <= 86; lag++)       // 70–180 BPM
        {
            double bpmL = 6000.0 / lag, prior = bpmL is >= 90 and <= 150 ? 1.0 : 0.85;   // resolves octave ambiguity
            double v = C(lag) * prior;
            if (v > best) { best = v; bestLag = lag; }
        }
        if (bestLag == 0) return (0, 0);
        double y0 = C(bestLag - 1), y1 = C(bestLag), y2 = C(bestLag + 1);
        double frac = (y0 - y2) / (2 * (y0 - 2 * y1 + y2));
        double period = bestLag + (double.IsFinite(frac) ? Math.Clamp(frac, -0.5, 0.5) : 0);

        // refine period and phase over the whole excerpt with a comb sum on the smoothed onset envelope
        var sm = new double[n];
        for (int i = 1; i < n - 1; i++) sm[i] = 0.25 * onset[i - 1] + 0.5 * onset[i] + 0.25 * onset[i + 1];
        double Comb(double per, double ph) { double s = 0; for (double k = ph; k < n - 1; k += per) { int i = (int)k; double f = k - i; s += sm[i] * (1 - f) + sm[i + 1] * f; } return s; }
        double bestPer = period, bestPh = 0, bestScore = -1;
        for (double per = period * 0.988; per <= period * 1.012; per += period * 0.0002)
            for (double ph = 0; ph < per; ph += 0.25)
            {
                double sc = Comb(per, ph);
                if (sc > bestScore) { bestScore = sc; bestPer = per; bestPh = ph; }
            }
        return (6000.0 / bestPer, bestPh / 100.0);
    }

    public static async Task<(double Bpm, double Phase)> AnalyzeFileAsync(FFmpeg ff, string file, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-i", file, "-vn", "-t", "90", "-ac", "1", "-ar", "16000", "-f", "s16le", "-"], ct, stdout: ms);
        var pcm = new short[(int)ms.Length / 2];
        Buffer.BlockCopy(ms.GetBuffer(), 0, pcm, 0, pcm.Length * 2);
        return Estimate(pcm);
    }
}
