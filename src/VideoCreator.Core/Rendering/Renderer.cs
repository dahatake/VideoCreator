using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VideoCreator.Audio;
using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Presets;

namespace VideoCreator.Rendering;

public sealed record RenderOutput(string Path, List<string> Commands, double LoudnessI, double TruePeak);

/// <summary>EditPlan -> one FFmpeg run (FR-RND-01..04). Loudness strategy verified in spikes/03-render.cs; audio beds in spike 08, face crop in spike 09, speed ramps in spike 11.</summary>
public sealed partial class Renderer(FFmpeg ff)
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    static string F(double x) => x.ToString("0.###", IC);

    /// <summary>Sidechain ducking tuned in spike 08 (threshold 0.015 / ratio 10 gave only 11.6 dB; these reach ≥ 12 dB).</summary>
    public const string DuckFilter = "sidechaincompress=threshold=0.01:ratio=12:attack=15:release=350";
    public const double SfxBaseDb = -8;

    /// <summary>Maps a BgmTrack id (library or user-supplied) to a file.</summary>
    public Func<string, string?>? BgmResolver { get; init; }
    /// <summary>Maps an SFX kind to a file.</summary>
    public Func<string, string?>? SfxResolver { get; init; }

    public sealed record Inputs(List<string[]> Args, Dictionary<string, int> Index, Dictionary<string, MediaInfo> Media)
    {
        public IEnumerable<string> Flat => Args.SelectMany(a => a);
    }

    public Inputs ResolveInputs(EditPlan plan, IReadOnlyDictionary<string, SourceAsset> assets)
    {
        var args = new List<string[]>(); var idx = new Dictionary<string, int>(); var media = new Dictionary<string, MediaInfo>();
        void AddAsset(string id)
        {
            if (idx.ContainsKey(id)) return;
            if (!assets.TryGetValue(id, out var a)) throw new FFmpegException($"asset {id} not found");
            idx[id] = args.Count; args.Add(["-i", a.Path]); media[id] = a.Media;
        }
        foreach (var id in plan.Timeline.Select(c => c.AssetId).Distinct()) AddAsset(id);
        AddAsset(plan.Audio.Master);
        if (plan.Audio.Bgm is { } bgm)
        {
            var path = BgmResolver?.Invoke(bgm) ?? throw new FFmpegException($"BGM {bgm} が見つかりません");
            if (!File.Exists(path)) throw new FFmpegException($"BGM ファイルがありません: {path}");
            idx["bgm"] = args.Count; args.Add(["-stream_loop", "-1", "-i", path]);
        }
        if (plan.Audio.SfxEnabled)
            foreach (var kind in plan.Audio.Sfx.Select(s => s.Kind).Distinct())
            {
                var path = SfxResolver?.Invoke(kind);
                if (path == null || !File.Exists(path)) continue;
                idx["sfx:" + kind] = args.Count; args.Add(["-i", path]);
            }
        return new Inputs(args, idx, media);
    }

    static string DialogueClip(EditPlan plan, Inputs inp, int i, string label)
    {
        var c = plan.Timeline[i]; var m = inp.Media[plan.Audio.Master]; int k = inp.Index[plan.Audio.Master];
        if (m.AudioChannels > 0 && c.Speed <= 2.001)
            return $"[{k}:a]atrim=start={F(c.MasterIn)}:end={F(c.MasterOut)},asetpts=PTS-STARTPTS{(Math.Abs(c.Speed - 1) > 0.001 ? $",atempo={F(c.Speed)}" : "")},aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[{label}];";
        return $"anullsrc=r=48000:cl=stereo,atrim=duration={F(c.Length)},asetpts=PTS-STARTPTS[{label}];";
    }

    /// <summary>Dialogue → (gain) → BGM bed (loop, phase, fades, mutes, sidechain duck) → SFX bed → [outLabel]. Verified recipes: spike 08.</summary>
    static string MixChain(EditPlan plan, Inputs inp, string inLabel, string outLabel)
    {
        var a = plan.Audio; double total = plan.Duration; var g = new StringBuilder();
        string cur = inLabel;
        if (Math.Abs(a.DialogueGainDb) > 0.01) { g.Append($"[{cur}]volume={F(a.DialogueGainDb)}dB[dg];"); cur = "dg"; }
        if (inp.Index.TryGetValue("bgm", out var bk))
        {
            g.Append($"[{bk}:a]atrim=start={F(a.BgmPhase)},asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume={F(a.BgmGainDb)}dB,atrim=duration={F(total)},asetpts=PTS-STARTPTS," +
                     $"afade=t=in:d=0.4,afade=t=out:st={F(Math.Max(0, total - 1.0))}:d=1.0");
            foreach (var mu in a.BgmMutes) g.Append($",volume=enable='between(t,{F(mu.T0)},{F(mu.T1)})':volume=0");
            g.Append("[bgm];");
            if (a.Duck) { g.Append($"[{cur}]asplit=2[dk][dm];[bgm][dk]{DuckFilter}[bd];[bd][dm]amix=inputs=2:normalize=0:duration=longest[bm];"); }
            else g.Append($"[{cur}][bgm]amix=inputs=2:normalize=0:duration=longest[bm];");
            cur = "bm";
        }
        var sfx = a.SfxEnabled ? a.Sfx.Where(s => inp.Index.ContainsKey("sfx:" + s.Kind)).ToList() : [];
        if (sfx.Count > 0)
        {
            for (int i = 0; i < sfx.Count; i++)
            {
                int ms = (int)Math.Round(sfx[i].T * 1000);
                g.Append($"[{inp.Index["sfx:" + sfx[i].Kind]}:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume={F(SfxBaseDb + sfx[i].GainDb)}dB,adelay={ms}|{ms}[sx{i}];");
            }
            g.Append($"[{cur}]{string.Concat(Enumerable.Range(0, sfx.Count).Select(i => $"[sx{i}]"))}amix=inputs={sfx.Count + 1}:normalize=0:duration=first:dropout_transition=0[sm];");
            cur = "sm";
        }
        g.Append($"[{cur}]anull[{outLabel}];");
        return g.ToString();
    }

    /// <summary>Video+audio filtergraph. Each clip is reframed to the canvas individually so mixed sources concat cleanly.</summary>
    public static string BuildGraph(EditPlan plan, Inputs inp, string? assFile, string fontsDir, bool preview, string? loudnorm)
    {
        int W = plan.Canvas.Width, H = plan.Canvas.Height, fps = plan.Canvas.Fps;
        var g = new StringBuilder(); var labels = new StringBuilder();
        for (int i = 0; i < plan.Timeline.Count; i++)
        {
            var c = plan.Timeline[i]; int k = inp.Index[c.AssetId];
            double ext = i + 1 < plan.Timeline.Count ? plan.Timeline[i + 1].TransitionSec : 0;   // cross-fade overlap taken from this clip's tail
            g.Append($"[{k}:v]trim=start={F(c.SrcIn)}:end={F(c.SrcOut + ext * c.Speed)},setpts=(PTS-STARTPTS)/{F(c.Speed)},fps={fps}");
            bool zoom = c.Zoom > 1.001;
            if (c.Reframe.Mode == "face" && c.Reframe.Track is { Count: > 0 } track)
            {
                g.Append($",crop={c.Reframe.CropW}:{c.Reframe.CropH}:x='{FaceTrack.XExpression(track)}':y={c.Reframe.CropY},scale={W}:{H}:flags=lanczos,setsar=1");
                if (zoom) g.Append($",scale=iw*{F(c.Zoom)}:ih*{F(c.Zoom)},crop={W}:{H}");
                g.Append($"[v{i}];");
            }
            else
            {
                if (zoom) g.Append($",scale=iw*{F(c.Zoom)}:ih*{F(c.Zoom)},crop=iw/{F(c.Zoom)}:ih/{F(c.Zoom)}");
                if (c.Reframe.Mode == "fit")
                    g.Append($",scale={W}:{H}:force_original_aspect_ratio=decrease,pad={W}:{H}:(ow-iw)/2:(oh-ih)/2,setsar=1[v{i}];");
                else
                    g.Append($",split[f{i}][b{i}];[b{i}]scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},boxblur=20:2[bb{i}];" +
                             $"[f{i}]scale={W}:-2[ff{i}];[bb{i}][ff{i}]overlay=(W-w)/2:(H-h)/2{(c.Reframe.FgOffsetY >= 0 ? "+" : "")}{c.Reframe.FgOffsetY},setsar=1[v{i}];");
            }
            g.Append(DialogueClip(plan, inp, i, $"a{i}"));
            labels.Append($"[v{i}][a{i}]");
        }
        if (plan.Timeline.Any(c => c.TransitionSec > 0))
        {
            // video chain with xfade at scene changes; audio is concatenated separately (hard cuts)
            var al = string.Concat(Enumerable.Range(0, plan.Timeline.Count).Select(i => $"[a{i}]"));
            g.Append($"{al}concat=n={plan.Timeline.Count}:v=0:a=1[ca0];");
            for (int i = 0; i < plan.Timeline.Count; i++) g.Append($"[v{i}]settb=AVTB,format=yuv420p[w{i}];");
            string cur = "w0"; double run = plan.Timeline[0].Length;
            for (int i = 1; i < plan.Timeline.Count; i++)
            {
                var c = plan.Timeline[i];
                if (c.TransitionSec > 0) g.Append($"[{cur}][w{i}]xfade=transition=fade:duration={F(c.TransitionSec)}:offset={F(run)}[x{i}];");
                else g.Append($"[{cur}][w{i}]concat=n=2:v=1:a=0[x{i}];");
                cur = $"x{i}"; run += c.Length;
            }
            g.Append($"[{cur}]null[cv];");
        }
        else g.Append($"{labels}concat=n={plan.Timeline.Count}:v=1:a=1[cv][ca0];");
        g.Append(MixChain(plan, inp, "ca0", "ca"));
        g.Append("[cv]");
        if (assFile != null) g.Append($"subtitles={FFmpeg.FilterPath(assFile)}:fontsdir={FFmpeg.FilterPath(fontsDir)},");
        if (preview) g.Append($"scale={W / 2}:{H / 2},");
        g.Append("format=yuv420p[vout];");
        g.Append($"[ca]{loudnorm ?? $"loudnorm=I={F(plan.Audio.TargetLufs)}:TP={F(plan.Audio.TruePeak)}:LRA={F(plan.Audio.Lra)}"},aresample=48000[aout]");
        return g.ToString();
    }

    public static string BuildAudioOnlyGraph(EditPlan plan, Inputs inp, string tail)
    {
        var g = new StringBuilder(); var labels = new StringBuilder();
        for (int i = 0; i < plan.Timeline.Count; i++) { g.Append(DialogueClip(plan, inp, i, $"a{i}")); labels.Append($"[a{i}]"); }
        g.Append($"{labels}concat=n={plan.Timeline.Count}:v=0:a=1[ca0];");
        g.Append(MixChain(plan, inp, "ca0", "ca"));
        g.Append($"[ca]{tail}[m]");
        return g.ToString();
    }

    string[] EncodeArgs(bool preview, string output) => preview
        ? ["-map", "[vout]", "-map", "[aout]", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "30", "-threads", ff.Threads, "-r", "30", "-c:a", "aac", "-b:a", "128k", "-ar", "48000", "-ac", "2", "-movflags", "+faststart", output]
        : ["-map", "[vout]", "-map", "[aout]", "-c:v", "libx264", "-profile:v", "high", "-preset", "veryfast", "-crf", "19", "-threads", ff.Threads, "-r", "30",
           "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", "-movflags", "+faststart", "-fflags", "+bitexact", "-flags:v", "+bitexact", "-flags:a", "+bitexact", "-map_metadata", "-1", output];

    public async Task<RenderOutput> RenderAsync(EditPlan plan, Preset preset, IReadOnlyDictionary<string, SourceAsset> assets, string outFile, bool preview, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(outFile)!; Directory.CreateDirectory(dir);
        var inp = ResolveInputs(plan, assets);
        var cmds = new List<string>();
        string? ass = null;
        if (plan.Telops.Any(t => t.Enabled))
        {
            ass = Path.ChangeExtension(outFile, ".ass");
            await File.WriteAllTextAsync(ass, AssWriter.Write(plan, preset), new UTF8Encoding(false), ct);
        }
        var inArgs = inp.Flat.ToList();
        string? ln = null;
        if (!preview)
        {
            // pass 1: measure the edited audio only (fast)
            var m = await ff.RunFfmpegAsync(["-hide_banner", .. inArgs, "-filter_complex", BuildAudioOnlyGraph(plan, inp, $"loudnorm=I={F(plan.Audio.TargetLufs)}:TP={F(plan.Audio.TruePeak)}:LRA={F(plan.Audio.Lra)}:print_format=json"), "-map", "[m]", "-f", "null", "-"], ct);
            cmds.Add(m.CommandLine);
            string J(string k) => Regex.Match(m.StdErr, $"\"{k}\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            if (J("input_i") is { Length: > 0 } ii && ii != "-inf")
                ln = $"loudnorm=I={F(plan.Audio.TargetLufs)}:TP={F(plan.Audio.TruePeak)}:LRA={F(plan.Audio.Lra)}:measured_I={J("input_i")}:measured_TP={J("input_tp")}:measured_LRA={J("input_lra")}:measured_thresh={J("input_thresh")}:offset={J("target_offset")}:linear=true";
        }
        var tmp = outFile + ".part.mp4";
        var r = await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-y", .. inArgs, "-filter_complex", BuildGraph(plan, inp, ass, ff.FontsDir, preview, ln), .. EncodeArgs(preview, tmp)], ct);
        cmds.Add(r.CommandLine);
        if (r.ExitCode != 0) throw new FFmpegException("レンダリングに失敗しました: " + FFmpeg.Tail(r.StdErr));
        File.Move(tmp, outFile, true);
        if (preview) return new RenderOutput(outFile, cmds, double.NaN, double.NaN);

        // loudnorm can silently fall back to dynamic mode; correct with gain + oversampled limiter (audio only, video copied)
        var (I, TP) = await MeasureLoudnessAsync(outFile, ct);
        for (int pass = 0; pass < 3 && !double.IsNaN(I) && (Math.Abs(I - plan.Audio.TargetLufs) > 0.5 || TP > -1.0); pass++)
        {
            var gain = F(plan.Audio.TargetLufs - I);
            var fix = outFile + $".fix{pass}.mp4";
            var rr = await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-y", "-i", outFile, "-map", "0", "-c:v", "copy",
                "-af", $"aresample=192000,volume={gain}dB,alimiter=limit=0.79:level=false:attack=1:release=50,aresample=48000",
                "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-fflags", "+bitexact", "-flags:a", "+bitexact", "-map_metadata", "-1", fix], ct);
            cmds.Add(rr.CommandLine);
            if (rr.ExitCode != 0) break;
            File.Move(fix, outFile, true);
            (I, TP) = await MeasureLoudnessAsync(outFile, ct);
        }
        return new RenderOutput(outFile, cmds, I, TP);
    }

    public async Task<(double I, double TP)> MeasureLoudnessAsync(string file, CancellationToken ct)
    {
        var r = await ff.RunFfmpegAsync(["-hide_banner", "-i", file, "-vn", "-af", "loudnorm=I=-14:TP=-1.5:print_format=summary", "-f", "null", "-"], ct);
        return (Num(IntegratedRe().Match(r.StdErr)), Num(TruePeakRe().Match(r.StdErr)));
    }

    static double Num(Match m) => m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, IC, out var d) ? d : double.NaN;

    public async Task<List<string>> ThumbnailsAsync(string video, double duration, string prefix, CancellationToken ct)
    {
        var list = new List<string>();
        foreach (var (t, i) in new[] { Math.Min(1.0, duration * 0.1), duration * 0.5, Math.Max(0, duration - 2.0) }.Select((t, i) => (t, i)))
        {
            var f = $"{prefix}_thumb{i + 1}.png";
            var r = await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-y", "-ss", F(t), "-i", video, "-frames:v", "1", f], ct);
            if (r.ExitCode == 0) list.Add(f);
        }
        return list;
    }

    [GeneratedRegex(@"Input Integrated:\s*(-?[\d.]+|-inf)")]
    private static partial Regex IntegratedRe();
    [GeneratedRegex(@"Input True Peak:\s*([-+]?[\d.]+|-inf)")]
    private static partial Regex TruePeakRe();
}
