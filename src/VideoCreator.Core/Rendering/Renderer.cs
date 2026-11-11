using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VideoCreator.Audio;
using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;

namespace VideoCreator.Rendering;

public sealed record RenderOutput(string Path, List<string> Commands, double LoudnessI, double TruePeak)
{
    /// <summary>Speech-over-music measured on the rendered stems (null when the plan has no scene-matched music).</summary>
    public BgmMixReport? Bgm { get; init; }
}

/// <summary>
/// FFmpeg ebur128 on the dialogue and music stems of the same mix: LD = integrated speech loudness − music loudness over the 400 ms blocks inside speech
/// (Torcoli et al.'s "LD during ducking"), its 10th percentile per block, and the music level in pauses relative to the speech.
/// </summary>
public sealed record BgmMixReport(double SpeechLufs, double Ld, double LdP10, double? GapLu, int SpeechBlocks, double TargetLd, double GapTargetLu);

/// <summary>EditPlan -> one FFmpeg run (FR-RND-01..04). Loudness strategy verified in spikes/03-render.cs; audio beds in spike 08, face crop in spike 09, speed ramps in spike 11.</summary>
public sealed partial class Renderer(FFmpeg ff)
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    static string F(double x) => x.ToString("0.###", IC);

    /// <summary>Sidechain ducking tuned in spike 08 (threshold 0.015 / ratio 10 gave only 11.6 dB; these reach ≥ 12 dB).</summary>
    public const string DuckFilter = "sidechaincompress=threshold=0.01:ratio=12:attack=15:release=350";
    public const double SfxBaseDb = -8;
    /// <summary>Peak of the 伏せ字 tone (≈ −13 dBFS RMS): clearly a bleep, below shouted dialogue.</summary>
    public const double BleepAmplitude = 0.3;

    /// <summary>Maps a BgmTrack id (library or user-supplied) to a file.</summary>
    public Func<string, string?>? BgmResolver { get; init; }
    /// <summary>Maps an SFX kind to a file.</summary>
    public Func<string, string?>? SfxResolver { get; init; }

    public sealed record Inputs(List<string[]> Args, Dictionary<string, int> Index, Dictionary<string, MediaInfo> Media)
    {
        public IEnumerable<string> Flat => Args.SelectMany(a => a);
        /// <summary>Per timeline clip: input index and the source time at which that input starts (non-zero for seeked inputs).</summary>
        public List<(int Index, double Offset)> Video { get; init; } = [];
        public List<(int Index, double Offset)> Audio { get; init; } = [];
        /// <summary>Per timeline clip: the PiP asset's input (Index −1 = no PiP).</summary>
        public List<(int Index, double Offset)> Pip { get; init; } = [];
        /// <summary>Files written for this render (the music gain envelope); the caller deletes them.</summary>
        public List<string> Scratch { get; init; } = [];
    }

    /// <summary>Sample rate of the music gain envelope (1 ms steps; the ramps are ≥ 50 ms).</summary>
    public const int EnvelopeRate = 1000;

    /// <summary>Scene-matched music is on: the plan has cues and BGM is not switched off.</summary>
    public static bool HasCues(EditPlan plan) => plan.Audio.Bgm != null && plan.Audio.BgmCues.Any(c => c.Track != null);

    /// <summary>Where the body music ends (with a fade): the closing's start, or the end of the file.</summary>
    public static double MusicEnd(EditPlan plan)
    {
        var ins = InsertMap.Active(plan);
        return ins.FirstOrDefault(i => i.Kind == "closing") is { } cl ? InsertMap.StartOf(ins, cl) : plan.TotalDuration;
    }

    /// <summary>Stereo 32-bit float WAV (both channels equal: a mono file would be up-mixed at −3 dB).</summary>
    public static void WriteEnvelope(string path, float[] g, int rate)
    {
        using var fs = File.Create(path); using var w = new BinaryWriter(fs);
        int data = g.Length * 8;
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)3); w.Write((short)2); w.Write(rate); w.Write(rate * 8); w.Write((short)8); w.Write((short)32);
        w.Write("data"u8); w.Write(data);
        foreach (var v in g) { w.Write(v); w.Write(v); }
    }

    /// <summary>Series plans are long and play clips out of source order (cold open, recap, next teaser). Sharing one input would make FFmpeg
    /// buffer every decoded frame of the later clips while it seeks forward for an earlier-played one, so each forward-running block of clips
    /// gets its own input, opened with an accurate input seek (verified: the first frame after -ss matches the un-seeked frame's checksum).</summary>
    public const double SeekMargin = 0.5;

    public Inputs ResolveInputs(EditPlan plan, IReadOnlyDictionary<string, SourceAsset> assets, string? envelopeFile = null)
    {
        var args = new List<string[]>(); var idx = new Dictionary<string, int>(); var media = new Dictionary<string, MediaInfo>();
        var video = new List<(int, double)>(); var audio = new List<(int, double)>(); var pip = new List<(int, double)>();
        SourceAsset Asset(string id) => assets.TryGetValue(id, out var a) ? a : throw new FFmpegException($"asset {id} not found");
        void AddAsset(string id)
        {
            if (idx.ContainsKey(id)) return;
            var a = Asset(id);
            idx[id] = args.Count; args.Add(["-i", a.Path]); media[id] = a.Media;
        }
        if (plan.Series == null)
        {
            foreach (var id in plan.Timeline.Select(c => c.AssetId).Distinct()) AddAsset(id);
            AddAsset(plan.Audio.Master);
            foreach (var id in plan.Timeline.Where(c => c.Pip != null).Select(c => c.Pip!.AssetId).Distinct()) AddAsset(id);
            foreach (var c in plan.Timeline) { video.Add((idx[c.AssetId], 0)); audio.Add((idx[plan.Audio.Master], 0)); pip.Add(c.Pip != null ? (idx[c.Pip.AssetId], 0) : (-1, 0)); }
        }
        else
        {
            var open = new Dictionary<string, (int index, double offset, double last)>();
            // key = asset id, or "audio:<id>" / "pip:<id>" for a second, independent chain of blocks on the same file
            (int, double) Block(string key, double t0, double t1)
            {
                if (open.TryGetValue(key, out var b) && t0 >= b.last - 0.01) { open[key] = b with { last = t1 }; return (b.index, b.offset); }
                var a = Asset(key[(key.IndexOf(':') + 1)..]); media[a.Id] = a.Media;
                double seek = Math.Max(0, Math.Round(t0 - SeekMargin, 3));
                var nb = (args.Count, seek, t1);
                args.Add(seek > 0 ? ["-ss", F(seek), "-i", a.Path] : ["-i", a.Path]);
                open[key] = nb;
                return (nb.Item1, nb.Item2);
            }
            foreach (var c in plan.Timeline)
            {
                var v = Block(c.AssetId, c.SrcIn, c.SrcOut);
                video.Add(v);
                // single-source clips read their audio from the same input; other cameras take it from the master's own blocks
                audio.Add(c.AssetId == plan.Audio.Master && c.AudioIn == null ? v : Block("audio:" + plan.Audio.Master, c.MasterIn, c.MasterOut));
                pip.Add(c.Pip is { } p ? Block("pip:" + p.AssetId, p.SrcIn, p.SrcIn + (c.SrcOut - c.SrcIn)) : (-1, 0));
            }
            media[plan.Audio.Master] = Asset(plan.Audio.Master).Media;
        }
        var scratch = new List<string>();
        if (plan.Audio.Bgm is { } bgm && plan.Audio.BgmCues.Count == 0)
        {
            var path = BgmResolver?.Invoke(bgm) ?? throw new FFmpegException($"BGM {bgm} が見つかりません");
            if (!File.Exists(path)) throw new FFmpegException($"BGM ファイルがありません: {path}");
            idx["bgm"] = args.Count; args.Add(["-stream_loop", "-1", "-i", path]);
        }
        if (HasCues(plan))
        {
            foreach (var cue in plan.Audio.BgmCues.Where(c => c.Track != null))
            {
                var path = BgmResolver?.Invoke(cue.Track!) ?? throw new FFmpegException($"BGM {cue.Track} が見つかりません");
                if (!File.Exists(path)) throw new FFmpegException($"BGM ファイルがありません: {path}");
                idx["cue:" + cue.Id] = args.Count; args.Add(["-i", path]);
            }
            var env = envelopeFile ?? Path.Combine(Path.GetTempPath(), $"vc-bgmenv-{Guid.NewGuid():N}.wav");
            WriteEnvelope(env, BgmPlanner.Envelope(FinalAudio(plan), plan.TotalDuration, MusicEnd(plan), EnvelopeRate), EnvelopeRate);
            scratch.Add(env);
            idx["bgmenv"] = args.Count; args.Add(["-i", env]);
        }
        if (plan.Audio.SfxEnabled)
            foreach (var kind in FinalAudio(plan).Sfx.Select(s => s.Kind).Distinct())
            {
                var path = SfxResolver?.Invoke(kind);
                if (path == null || !File.Exists(path)) continue;
                idx["sfx:" + kind] = args.Count; args.Add(["-i", path]);
            }
        foreach (var cardBgm in InsertMap.Active(plan).Where(i => i.Kind == "card" && i.Bgm != null).Select(i => i.Bgm!).Distinct())
        {
            var path = BgmResolver?.Invoke(cardBgm) ?? throw new FFmpegException($"カード BGM {cardBgm} が見つかりません");
            if (!File.Exists(path)) throw new FFmpegException($"カード BGM ファイルがありません: {path}");
            idx["cardbgm:" + cardBgm] = args.Count; args.Add(["-stream_loop", "-1", "-i", path]);
        }
        foreach (var ins in InsertMap.Active(plan).Where(i => i.Path != null))
        {
            // an upscaled cache that has been deleted falls back to the registered file (the scale chain fits any size)
            var path = File.Exists(ins.Path) ? ins.Path! : ins.OriginalPath is { } o && File.Exists(o) ? o : throw new FFmpegException($"{KindName(ins)}のファイルがありません: {ins.Path}");
            idx["ins:" + ins.Id] = args.Count;
            // a card image becomes a still stream of the card's length
            args.Add(ins.Kind == "card" ? ["-loop", "1", "-framerate", plan.Canvas.Fps.ToString(IC), "-t", F(ins.Duration), "-i", path] : ["-i", path]);
        }
        return new Inputs(args, idx, media) { Video = video, Audio = audio, Pip = pip, Scratch = scratch };
    }

    static string KindName(Insert i) => i.Kind switch { "opening" => "オープニング", "closing" => "クロージング", _ => "場面転換カード" };

    /// <summary>The plan's audio on the FINAL timeline: SFX and BGM mutes shifted past inserts, a hit on every card, BGM silent under the bumpers (they bring their own music).</summary>
    public static AudioSpec FinalAudio(EditPlan plan)
    {
        var ins = InsertMap.Active(plan);
        if (ins.Count == 0) return plan.Audio;
        var sfx = plan.Audio.Sfx.Select(s => s with { T = Math.Round(InsertMap.ToFinal(ins, s.T), 3) }).ToList();
        var spans = InsertMap.InsertSpans(ins);
        sfx.AddRange(spans.Where(x => x.Insert.Kind == "card" && x.Insert.Sfx != null).Select(x => new SfxEvent(x.Insert.Sfx!, Math.Round(x.Start, 3), -3)));
        var mutes = plan.Audio.BgmMutes.SelectMany(m => InsertMap.Span(ins, m.T0, m.T1)).Select(x => new Interval(Math.Round(x.Start, 3), Math.Round(x.End, 3))).ToList();
        mutes.AddRange(spans.Where(x => BgmPlanner.Mutes(x.Insert)).Select(x => new Interval(Math.Round(x.Start, 3), Math.Round(x.End, 3))));
        var bleeps = plan.Audio.Bleeps.SelectMany(m => InsertMap.Span(ins, m.T0, m.T1)).Select(x => new Interval(Math.Round(x.Start, 3), Math.Round(x.End, 3))).ToList();
        var duck = plan.Audio.DuckSpans.SelectMany(d => InsertMap.Span(ins, d.T0, d.T1).Select(x => d with { T0 = Math.Round(x.Start, 3), T1 = Math.Round(x.End, 3) })).ToList();
        return plan.Audio with { Sfx = sfx.OrderBy(s => s.T).ToList(), BgmMutes = mutes.OrderBy(m => m.T0).ToList(), Bleeps = bleeps, DuckSpans = duck };
    }

    /// <summary>Playback order: each insert goes before the first clip that starts at (or after) its position.</summary>
    public static List<(int Clip, Insert? Insert)> Sequence(EditPlan plan)
    {
        var ins = InsertMap.Active(plan); var res = new List<(int, Insert?)>(); double off = 0; int j = 0;
        for (int i = 0; i < plan.Timeline.Count; i++)
        {
            while (j < ins.Count && ins[j].At <= off + 0.01) res.Add((-1, ins[j++]));
            res.Add((i, null)); off += plan.Timeline[i].Length;
        }
        while (j < ins.Count) res.Add((-1, ins[j++]));
        return res;
    }

    /// <summary>
    /// Frames per playback item, rounded on the CUMULATIVE planned timeline. Clips are cut at arbitrary times, so each one is a fractional number of frames;
    /// rounding each one by itself (and concat taking the longer of video and audio) used to add ~17 ms per clip — seconds over a long series part —
    /// which would put telops, SFX and inserts out of step with the picture. With these counts every item ends within half a frame of its planned time.
    /// </summary>
    public static int[] FrameCounts(EditPlan plan, List<(int Clip, Insert? Insert)> seq)
    {
        var res = new int[seq.Count]; double acc = 0; int done = 0;
        for (int s = 0; s < seq.Count; s++)
        {
            acc += seq[s].Insert?.Duration ?? plan.Timeline[seq[s].Clip].Length;
            res[s] = Math.Max(1, (int)Math.Round(acc * plan.Canvas.Fps) - done);
            done += res[s];
        }
        return res;
    }

    static long Samples(int frames, int fps) => (long)Math.Round(frames * 48000.0 / fps);

    static string DialogueClip(EditPlan plan, Inputs inp, int i, string label, long? samples = null)
    {
        var c = plan.Timeline[i]; var m = inp.Media[plan.Audio.Master]; var (k, off) = inp.Audio[i];
        var fit = samples is long n ? $",asetpts=N/SR/TB,apad=whole_len={n},atrim=end_sample={n}" : "";
        if (m.AudioChannels > 0 && c.Speed <= 2.001)
            return $"[{k}:a]atrim=start={F(c.MasterIn - off)}:end={F(c.MasterOut - off)},asetpts=PTS-STARTPTS{(Math.Abs(c.Speed - 1) > 0.001 ? $",atempo={F(c.Speed)}" : "")},aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo{fit}[{label}];";
        return $"anullsrc=r=48000:cl=stereo,{(samples is long s ? $"atrim=end_sample={s}" : $"atrim=duration={F(c.Length)}")},asetpts=PTS-STARTPTS[{label}];";
    }

    /// <summary>Dialogue → (gain) → BGM bed → SFX bed → [outLabel]. Verified recipes: spike 08.
    /// <paramref name="a"/> is on the final timeline (<see cref="FinalAudio"/>); the BGM ends with a fade at <paramref name="bgmEnd"/> (the closing's start).
    /// Older plans: one looped track under a sidechain compressor. Scene-matched plans: <see cref="AppendCueBus"/> (gain automation from the transcript).</summary>
    static string MixChain(EditPlan plan, AudioSpec a, double bgmEnd, Inputs inp, string inLabel, string outLabel)
    {
        var g = new StringBuilder();
        string cur = AppendDialogueFx(g, a, inLabel);
        if (inp.Index.TryGetValue("bgm", out var bk))
        {
            g.Append($"[{bk}:a]atrim=start={F(a.BgmPhase)},asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume={F(a.BgmGainDb)}dB,atrim=duration={F(bgmEnd)},asetpts=PTS-STARTPTS," +
                     $"afade=t=in:d=0.4,afade=t=out:st={F(Math.Max(0, bgmEnd - 1.0))}:d=1.0");
            foreach (var mu in a.BgmMutes) g.Append($",volume=enable='between(t,{F(mu.T0)},{F(mu.T1)})':volume=0");
            g.Append("[bgm];");
            if (a.Duck) { g.Append($"[{cur}]asplit=2[dk][dm];[bgm][dk]{DuckFilter}[bd];[bd][dm]amix=inputs=2:normalize=0:duration=longest[bm];"); }
            else g.Append($"[{cur}][bgm]amix=inputs=2:normalize=0:duration=longest[bm];");
            cur = "bm";
        }
        else if (AppendCueBus(g, plan, a, inp, "bgm"))
        {
            g.Append($"[{cur}][bgm]amix=inputs=2:normalize=0:duration=first[bm];");
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

    /// <summary>Dialogue gain and 伏せ字 bleeps; returns the output label.</summary>
    static string AppendDialogueFx(StringBuilder g, AudioSpec a, string inLabel)
    {
        string cur = inLabel;
        if (Math.Abs(a.DialogueGainDb) > 0.01) { g.Append($"[{cur}]volume={F(a.DialogueGainDb)}dB[dg];"); cur = "dg"; }
        if (a.Bleeps.Count > 0)
        {
            // 伏せ字: silence the dialogue and gate a 1 kHz tone into exactly the same spans (before the duck, so the BGM ducks under the bleep too)
            var gate = string.Join("+", a.Bleeps.Select(b => $"between(t,{F(b.T0)},{F(b.T1)})"));
            g.Append($"[{cur}]volume=volume=0:enable='{gate}'[bq];aevalsrc='{F(BleepAmplitude)}*sin(2*PI*1000*t)*gt({gate},0)':s=48000:d={F(a.Bleeps.Max(b => b.T1) + 0.1)},aformat=sample_fmts=fltp:channel_layouts=stereo[bt];" +
                     "[bq][bt]amix=inputs=2:normalize=0:duration=first[bl];");
            cur = "bl";
        }
        return cur;
    }

    /// <summary>
    /// Scene-matched music → [<paramref name="label"/>]: each cue's track (its playable part looped, started at the cue's position, brought to its level
    /// by <see cref="BgmPlanner.CueGainDb"/>, cross-faded at scene changes and delayed to its place), summed, then multiplied by the gain envelope
    /// (ducking, mutes, fades; <see cref="BgmPlanner.Envelope"/>). False when there is nothing to play.
    /// </summary>
    static bool AppendCueBus(StringBuilder g, EditPlan plan, AudioSpec a, Inputs inp, string label)
    {
        if (!HasCues(plan) || !inp.Index.TryGetValue("bgmenv", out var ek)) return false;
        double total = plan.TotalDuration;
        long totalSamples = (long)Math.Round(total * 48000);
        double speech = a.SpeechLufs ?? -23;
        var spans = BgmPlanner.FinalSpans(plan.Audio.BgmCues, InsertMap.Active(plan), total);
        var labels = new List<string>();
        foreach (var s in spans)
        {
            if (s.Cue.Track == null || !inp.Index.TryGetValue("cue:" + s.Cue.Id, out var k)) continue;
            double begin = Math.Max(0, s.Start - s.FadeIn / 2), end = Math.Min(total, s.End + s.FadeOut / 2);
            if (end - begin < 0.05) continue;
            double L = Math.Max(0.5, s.Cue.Out - s.Cue.In), rel = BgmPlanner.TrackPosition(s, begin) - s.Cue.In;
            long loop = (long)Math.Round(L * 48000);
            string l = $"cq{labels.Count}";
            g.Append($"[{k}:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,atrim=start={F(s.Cue.In)}:end={F(s.Cue.Out)},asetpts=PTS-STARTPTS," +
                     $"aloop=loop=-1:size={loop},atrim=start={F(rel)}:duration={F(end - begin)},asetpts=PTS-STARTPTS,volume={F(BgmPlanner.CueGainDb(plan.Audio, s.Cue, speech))}dB");
            if (s.FadeIn > 0 && s.Start - s.FadeIn / 2 >= 0) g.Append($",afade=t=in:st=0:d={F(s.FadeIn)}:curve={(s.CoherentIn ? "tri" : "qsin")}");
            if (s.FadeOut > 0) g.Append($",afade=t=out:st={F(Math.Max(0, end - begin - s.FadeOut))}:d={F(s.FadeOut)}:curve={(s.CoherentOut ? "tri" : "qsin")}");
            long delay = (long)Math.Round(begin * 1000);
            if (delay > 0) g.Append($",adelay=delays={delay}:all=1");
            g.Append($"[{l}];");
            labels.Add(l);
        }
        if (labels.Count == 0) return false;
        string mix = labels.Count == 1 ? $"[{labels[0]}]anull" : $"{string.Concat(labels.Select(x => $"[{x}]"))}amix=inputs={labels.Count}:normalize=0:duration=longest";
        g.Append($"{mix},apad=whole_len={totalSamples},atrim=end_sample={totalSamples}[cbus];");
        g.Append($"[{ek}:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,apad=whole_len={totalSamples},atrim=end_sample={totalSamples}[cenv];");
        g.Append($"[cbus][cenv]amultiply[{label}];");
        return true;
    }

    /// <summary>One clip (or a one-frame freeze of it) reframed to the canvas: face crop, punch-in, fit, blurred background or the screen layout. <paramref name="post"/> runs before the output label.
    /// <paramref name="pipHead"/>: the PiP asset's trimmed stream for <see cref="Clip.Pip"/> (null = no inset, e.g. a freeze frame).</summary>
    static void AppendVideo(StringBuilder g, string head, Clip c, int W, int H, bool half, string id, string label, string post = "", string? pipHead = null)
    {
        g.Append(head);
        bool zoom = c.Zoom > 1.001;
        if (c.Reframe.Mode == "screen")
        {
            AppendScreen(g, c, W, H, half, id, label, post, pipHead);
            return;
        }
        if (c.Reframe.Mode == "face" && c.Reframe.Track is { Count: > 0 } track)
        {
            g.Append($",crop={c.Reframe.CropW}:{c.Reframe.CropH}:x='{FaceTrack.XExpression(track)}':y={c.Reframe.CropY},scale={W}:{H}:flags=lanczos,setsar=1");
            if (zoom) g.Append($",scale=iw*{F(c.Zoom)}:ih*{F(c.Zoom)},crop={W}:{H}");
            g.Append($"{post}[{label}];");
        }
        else
        {
            if (zoom) g.Append($",scale=iw*{F(c.Zoom)}:ih*{F(c.Zoom)},crop=iw/{F(c.Zoom)}:ih/{F(c.Zoom)}");
            if (c.Reframe.Mode == "fit")
                g.Append($",scale={W}:{H}:force_original_aspect_ratio=decrease,pad={W}:{H}:(ow-iw)/2:(oh-ih)/2,setsar=1{post}[{label}];");
            else
                g.Append($",split[f{id}][b{id}];[b{id}]scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},boxblur=20:2[bb{id}];" +
                         $"[f{id}]scale={W}:-2[ff{id}];[bb{id}][ff{id}]overlay=(W-w)/2:(H-h)/2{(c.Reframe.FgOffsetY >= 0 ? "+" : "")}{(half ? c.Reframe.FgOffsetY / 2 : c.Reframe.FgOffsetY)},setsar=1{post}[{label}];");
        }
    }

    /// <summary>Exactly <paramref name="frames"/> frames: a short stream is held on its last frame, a long one is cut.</summary>
    static string Frames(int frames) => $",tpad=stop_mode=clone:stop_duration=1,trim=end_frame={frames},setpts=PTS-STARTPTS";

    /// <summary>
    /// Screen layout: the screen recording's blurred, dimmed copy fills the canvas; the screen itself — zoomed and panned along its track, or whole when
    /// CropW = 0 — fills the band [BoxY, BoxY+BoxH); the PiP is a circle with a white ring. Both masks are computed for ONE frame with geq and looped
    /// (geq per frame would cost ~130 k expression evaluations per frame).
    /// </summary>
    static void AppendScreen(StringBuilder g, Clip c, int W, int H, bool half, string id, string label, string post, string? pipHead)
    {
        int S(int v) => half ? (v / 2) & ~1 : v;
        var r = c.Reframe; int by = S(r.BoxY), bh = S(r.BoxH);
        g.Append($",split[sf{id}][sb{id}];[sb{id}]scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},boxblur=20:2,eq=brightness=-0.12[sbb{id}];");
        if (r.CropW > 0 && r.Track is { Count: > 0 } tx)
            g.Append($"[sf{id}]crop={r.CropW}:{r.CropH}:x='{FaceTrack.XExpression(tx)}':y='{FaceTrack.XExpression(r.TrackY ?? [new TrackPoint(0, r.CropY)])}',scale={W}:{bh}:flags=lanczos,setsar=1[sff{id}];" +
                     $"[sbb{id}][sff{id}]overlay=0:{by}");
        else
            g.Append($"[sf{id}]scale={W}:{bh}:force_original_aspect_ratio=decrease:force_divisible_by=2:flags=lanczos,setsar=1[sff{id}];[sbb{id}][sff{id}]overlay=(W-w)/2:{by}+({bh}-h)/2");
        if (pipHead == null || c.Pip is not { } pip) { g.Append($",setsar=1{post}[{label}];"); return; }
        int d = S(pip.D), b = Math.Max(2, S(pip.Border)), rr = d / 2, rb = rr + b, db = d + 2 * b;
        g.Append($"[sx{id}];");
        g.Append($"{pipHead},crop={pip.CropS}:{pip.CropS}:{pip.CropX}:{pip.CropY},scale={d}:{d}:flags=lanczos,format=yuva420p[pv{id}];");
        g.Append($"color=c=black:s={d}x{d}:r=30,trim=end_frame=1,format=gray,geq=lum='255*lte(hypot(X-{rr}+0.5,Y-{rr}+0.5),{rr})',loop=loop=-1:size=1,setpts=N/30/TB[pm{id}];");
        g.Append($"color=c=white:s={db}x{db}:r=30,trim=end_frame=1,format=yuva420p,geq=lum='p(X,Y)':cb='p(X,Y)':cr='p(X,Y)':a='255*lte(hypot(X-{rb}+0.5,Y-{rb}+0.5),{rb})',loop=loop=-1:size=1,setpts=N/30/TB[pr{id}];");
        g.Append($"[pv{id}][pm{id}]alphamerge[pa{id}];[pr{id}][pa{id}]overlay={b}:{b}:shortest=1[pb{id}];");
        g.Append($"[sx{id}][pb{id}]overlay={S(pip.X)}:{S(pip.Y)}:shortest=1,setsar=1{post}[{label}];");
    }

    /// <summary>
    /// Insert video [iv{j}]: a bumper scaled to the canvas (blurred background when its shape differs, e.g. a 16:9 opening in a 9:16 output),
    /// or a card = the last frame of the previous scene frozen and dimmed, covered full screen by the card image if any (the card text is drawn by libass).
    /// </summary>
    static void AppendInsertVideo(StringBuilder g, EditPlan plan, Inputs inp, List<(int Clip, Insert? Insert)> seq, int s, int j, int frames, int W, int H, int fps, bool half)
    {
        var ins = seq[s].Insert!; string D = F(ins.Duration);
        string fix = Frames(frames);
        if (ins.Kind != "card")
        {
            int k = inp.Index["ins:" + ins.Id];
            g.Append($"[{k}:v]trim=duration={D},setpts=PTS-STARTPTS,fps={fps}");
            double target = (double)W / H, aspect = ins.Height > 0 ? (double)ins.Width / ins.Height : target;
            if (Math.Abs(Math.Log(aspect / target)) < 0.02) g.Append($",scale={W}:{H}:flags=lanczos,setsar=1{fix}[iv{j}];");
            else g.Append($",split[if{j}][ib{j}];[ib{j}]scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},boxblur=20:2[ibb{j}];" +
                          $"[if{j}]scale={W}:{H}:force_original_aspect_ratio=decrease:force_divisible_by=2:flags=lanczos[iff{j}];[ibb{j}][iff{j}]overlay=(W-w)/2:(H-h)/2,setsar=1{fix}[iv{j}];");
            return;
        }
        // freeze source: the clip just before the card (or the first frame of the next one when the card opens the video)
        int prev = seq.Take(s).Where(x => x.Insert == null).Select(x => x.Clip).DefaultIfEmpty(-1).Last();
        int ci = prev >= 0 ? prev : seq.Skip(s).Where(x => x.Insert == null).Select(x => x.Clip).DefaultIfEmpty(-1).First();
        string bg = ins.Path != null || ins.Bgm != null ? $"cb{j}" : $"iv{j}";
        if (ci < 0) g.Append($"color=c=0x202020:s={W}x{H}:r={fps}:d={D},setsar=1{fix}[{bg}];");
        else
        {
            var c = plan.Timeline[ci]; var (k, off) = inp.Video[ci];
            double at = prev >= 0 ? Math.Max(c.SrcIn, c.SrcOut - (ins.Bgm != null ? c.Speed / fps : 0.1)) : c.SrcIn;
            List<TrackPoint>? Hold(List<TrackPoint>? tr) => tr is { Count: > 0 }
                ? [new TrackPoint(0, FaceTrack.XAt(tr, prev >= 0 ? c.Length : 0))] : tr;
            var still = c with { Reframe = c.Reframe with { Track = Hold(c.Reframe.Track), TrackY = Hold(c.Reframe.TrackY) } };
            AppendVideo(g, $"[{k}:v]trim=start={F(at - off)}:end={F(at + 0.1 - off)},setpts=PTS-STARTPTS,fps={fps},trim=end_frame=1", still, W, H, half, $"z{j}", bg,
                $",eq=saturation=0.55:brightness=-0.06,tpad=stop_mode=clone:stop_duration={D}{fix}");
        }
        if (ins.Path != null)
        {
            // full screen: an image of about the canvas shape covers it (≤ ~10 % cropped); any other shape is shown whole over a blurred copy of itself.
            // Transparent parts still show the frozen scene underneath.
            int k = inp.Index["ins:" + ins.Id];
            string fit = $"scale={W}:{H}:force_original_aspect_ratio=increase:flags=lanczos,crop={W}:{H}";
            g.Append($"[{k}:v]trim=duration={D},setpts=PTS-STARTPTS,fps={fps},format=rgba");
            if (CardCovers(ins, W, H)) g.Append($",{fit}[ci{j}];[{bg}][ci{j}]overlay=0:0:eof_action=repeat,setsar=1{fix}[iv{j}];");
            else g.Append($",split[cf{j}][cs{j}];[cs{j}]{fit},boxblur=20:2[cbb{j}];" +
                          $"[cf{j}]scale={W}:{H}:force_original_aspect_ratio=decrease:force_divisible_by=2:flags=lanczos[cff{j}];" +
                          $"[{bg}][cbb{j}]overlay=0:0:eof_action=repeat[cbx{j}];[cbx{j}][cff{j}]overlay=(W-w)/2:(H-h)/2:eof_action=repeat,setsar=1{fix}[iv{j}];");
        }
        else if (ins.Bgm != null)
        {
            // no image: a full-screen panel with a yellow frame just inside the edges
            int inset = Math.Max(4, W / 24);
            g.Append($"[{bg}]drawbox=x=0:y=0:w=iw:h=ih:color=0x352044:t=fill," +
                     $"drawbox=x={inset}:y={inset}:w=iw-{2 * inset}:h=ih-{2 * inset}:color=0xFFE600:t={Math.Max(2, W / 180)}[iv{j}];");
        }
    }

    /// <summary>A card image close enough to the canvas shape to fill it by cropping at most ~10 % (otherwise it is letterboxed over a blurred copy).</summary>
    public static bool CardCovers(Insert card, int w, int h) =>
        card.Width > 0 && card.Height > 0 && Math.Abs(Math.Log((double)card.Width / card.Height / ((double)w / h))) < 0.1;

    static string Silence(long samples, string label) => $"anullsrc=r=48000:cl=stereo,atrim=end_sample={samples},asetpts=PTS-STARTPTS[{label}];";

    /// <summary>
    /// An insert's segment on the dialogue track: a bumper's own music (brought to the loudness target, compensating the dialogue gain applied later),
    /// otherwise silence. Carrying it inside the concat keeps it locked to the bumper's video. Timestamps are rebuilt after resampling:
    /// AAC files start slightly before 0 (encoder priming), and the offset would make concat start the next segment ~0.1 s late.
    /// </summary>
    static string InsertAudio(EditPlan plan, Inputs inp, Insert ins, string label, long samples)
    {
        if (ins.Kind == "card")
        {
            if (ins.Bgm == null) return Silence(samples, label);
            int bk = inp.Index["cardbgm:" + ins.Bgm];
            return $"[{bk}:a]atrim=duration={F(ins.Duration)},asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo," +
                   $"volume={F(ins.GainDb - plan.Audio.DialogueGainDb)}dB,afade=t=in:d=0.08,afade=t=out:st={F(ins.Duration - 0.15)}:d=0.15," +
                   $"asetpts=N/SR/TB,apad=whole_len={samples},atrim=end_sample={samples}[{label}];";
        }
        if (!ins.HasAudio || !inp.Index.TryGetValue("ins:" + ins.Id, out var k)) return Silence(samples, label);
        return $"[{k}:a]atrim=duration={F(ins.Duration)},asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume={F(ins.GainDb - plan.Audio.DialogueGainDb)}dB," +
               $"asetpts=N/SR/TB,apad=whole_len={samples},atrim=end_sample={samples}[{label}];";
    }

    /// <summary>Audio of one playback item, exactly as long as its video.</summary>
    static string ItemAudio(EditPlan plan, Inputs inp, (int Clip, Insert? Insert) item, int j, int frames) => item.Insert == null
        ? DialogueClip(plan, inp, item.Clip, $"a{item.Clip}", Samples(frames, plan.Canvas.Fps))
        : InsertAudio(plan, inp, item.Insert, $"ia{j}", Samples(frames, plan.Canvas.Fps));

    /// <summary>Dialogue in playback order (inserts' own audio or silence between clips) → [ca0].</summary>
    static void AppendDialogue(StringBuilder g, EditPlan plan, Inputs inp, List<(int Clip, Insert? Insert)> seq)
    {
        var frames = FrameCounts(plan, seq);
        var labels = new StringBuilder(); int j = 0;
        for (int s = 0; s < seq.Count; s++)
        {
            g.Append(ItemAudio(plan, inp, seq[s], j, frames[s]));
            labels.Append(seq[s].Insert == null ? $"[a{seq[s].Clip}]" : $"[ia{j++}]");
        }
        g.Append($"{labels}concat=n={seq.Count}:v=0:a=1[ca0];");
    }

    /// <summary>[ca0] → BGM/SFX mix → [outLabel]. The BGM is muted under the bumpers and fades out where the closing starts.</summary>
    static void AppendMix(StringBuilder g, EditPlan plan, Inputs inp, string outLabel) =>
        g.Append(MixChain(plan, FinalAudio(plan), MusicEnd(plan), inp, "ca0", outLabel));

    /// <summary>The edited dialogue alone (gain and bleeps, no music, no SFX, no inserts) through ebur128: its integrated loudness is what the music is set against.</summary>
    public static string BuildDialogueGraph(EditPlan plan, Inputs inp)
    {
        var body = plan with { Inserts = [] };
        var g = new StringBuilder();
        AppendDialogue(g, body, inp, Sequence(body));
        var cur = AppendDialogueFx(g, FinalAudio(body), "ca0");
        g.Append($"[{cur}]ebur128=framelog=quiet[m]");
        return plan.Series != null ? ShareInputs(g.ToString()) : g.ToString();
    }

    /// <summary>The two stems of the mix exactly as rendered (same chains), each through a named ebur128: [d] = dialogue, [mu] = music.</summary>
    public static string BuildStemGraph(EditPlan plan, Inputs inp)
    {
        var g = new StringBuilder();
        var a = FinalAudio(plan);
        AppendDialogue(g, plan, inp, Sequence(plan));
        var cur = AppendDialogueFx(g, a, "ca0");
        g.Append($"[{cur}]ebur128@dlg=framelog=info[d];");
        if (!AppendCueBus(g, plan, a, inp, "mus")) throw new FFmpegException("BGM がありません");
        g.Append("[mus]ebur128@mus=framelog=info[mu]");
        return plan.Series != null ? ShareInputs(g.ToString()) : g.ToString();
    }

    /// <summary>
    /// LD over the 400 ms blocks that lie inside speech (final timeline); music blocks below −70 LUFS count as silence. Pauses = blocks at least
    /// <see cref="BgmPlanner.ReleaseSec"/> away from speech, not muted and before the music's end.
    /// </summary>
    public static BgmMixReport MixReport(EditPlan plan, List<BgmAnalyzer.Frame> dlg, List<BgmAnalyzer.Frame> mus)
    {
        var a = FinalAudio(plan);
        double musicEnd = MusicEnd(plan);
        int n = Math.Min(dlg.Count, mus.Count);
        var speech = a.DuckSpans;
        bool Inside(double t0, double t1) => speech.Any(s => t0 >= s.T0 - 1e-3 && t1 <= s.T1 + 1e-3);
        bool Near(double t0, double t1) => speech.Any(s => t1 > s.T0 - BgmPlanner.AttackSec && t0 < s.T1 + BgmPlanner.ReleaseSec);
        bool Muted(double t0, double t1) => a.BgmMutes.Any(m => t1 > m.T0 - BgmPlanner.MuteRampSec && t0 < m.T1 + BgmPlanner.MuteRampSec);
        var sp = Enumerable.Range(0, n).Where(i => Inside(dlg[i].T - 0.4, dlg[i].T)).ToList();
        double speechI = BgmAnalyzer.Integrated(sp.Select(i => dlg[i].M));
        if (sp.Count == 0 || double.IsNaN(speechI)) return new BgmMixReport(double.NaN, 99, 99, null, 0, plan.Audio.BgmSpeechLd, plan.Audio.BgmGapLu);
        var musicIn = sp.Select(i => mus[i].M).Where(m => m > -70).ToList();
        // music silent under speech (muted / no track in that scene) counts as a large gap
        double musicPower = musicIn.Count == 0 ? double.NegativeInfinity : 10 * Math.Log10(sp.Sum(i => mus[i].M > -70 ? Math.Pow(10, mus[i].M / 10) : 0) / sp.Count);
        double ld = double.IsNegativeInfinity(musicPower) ? 99 : Math.Min(99, speechI - musicPower);
        var per = sp.Where(i => dlg[i].M > speechI - 10).Select(i => dlg[i].M - Math.Max(-70, mus[i].M)).OrderBy(x => x).ToList();
        double p10 = per.Count == 0 ? ld : per[(int)Math.Floor(0.1 * (per.Count - 1))];
        var gap = Enumerable.Range(0, n).Where(i => dlg[i].T <= musicEnd - BgmPlanner.FadeOutSec && dlg[i].T - 0.4 >= BgmPlanner.FadeInSec
            && !Near(dlg[i].T - 0.4, dlg[i].T) && !Muted(dlg[i].T - 0.4, dlg[i].T) && mus[i].M > -70).Select(i => mus[i].M).ToList();
        double? gapLu = gap.Count < 3 ? null : Math.Round(BgmAnalyzer.PowerMean(gap) - speechI, 1);
        return new BgmMixReport(Math.Round(speechI, 1), Math.Round(ld, 1), Math.Round(p10, 1), gapLu, sp.Count, plan.Audio.BgmSpeechLd, plan.Audio.BgmGapLu);
    }

    /// <summary>Video+audio filtergraph. Each clip is reframed to the canvas individually so mixed sources concat cleanly; inserts play between clips.</summary>
    public static string BuildGraph(EditPlan plan, Inputs inp, string? assFile, string fontsDir, bool preview, string? loudnorm)
    {
        int W = plan.Canvas.Width, H = plan.Canvas.Height, fps = plan.Canvas.Fps;
        // long series previews are composed directly at half size (4x fewer pixels to blur); ASS PlayRes keeps the 1080x1920 layout and libass scales it
        bool half = preview && plan.Series != null;
        if (half) { W /= 2; H /= 2; }
        var seq = Sequence(plan);
        var frames = FrameCounts(plan, seq);
        var g = new StringBuilder(); var labels = new StringBuilder();
        int j = 0;
        var vlabel = new List<string>(); var fadeFrames = new List<int>();
        for (int s = 0; s < seq.Count; s++)
        {
            var (i, ins) = seq[s];
            if (ins != null)
            {
                AppendInsertVideo(g, plan, inp, seq, s, j, frames[s], W, H, fps, half);
                vlabel.Add($"iv{j}"); fadeFrames.Add(0);
                labels.Append($"[iv{j}][ia{j}]");
                j++;
                continue;
            }
            var c = plan.Timeline[i]; var (k, off) = inp.Video[i];
            // cross-fade overlap taken from this clip's tail (only when the next thing played is a clip that fades in)
            double ext = s + 1 < seq.Count && seq[s + 1].Insert == null ? plan.Timeline[seq[s + 1].Clip].TransitionSec : 0;
            int extFrames = (int)Math.Round(ext * fps);
            string? pipHead = null;
            if (c.Pip is { } pp && inp.Pip.Count > i && inp.Pip[i].Index >= 0)
            {
                var (pk, poff) = inp.Pip[i];
                pipHead = $"[{pk}:v]trim=start={F(pp.SrcIn - poff)}:end={F(pp.SrcIn + (c.SrcOut - c.SrcIn) + ext * c.Speed - poff)},setpts=(PTS-STARTPTS)/{F(c.Speed)},fps={fps}";
            }
            AppendVideo(g, $"[{k}:v]trim=start={F(c.SrcIn - off)}:end={F(c.SrcOut + ext * c.Speed - off)},setpts=(PTS-STARTPTS)/{F(c.Speed)},fps={fps}", c, W, H, half, i.ToString(IC), $"v{i}",
                Frames(frames[s] + extFrames), pipHead);
            vlabel.Add($"v{i}"); fadeFrames.Add(s > 0 && seq[s - 1].Insert == null ? (int)Math.Round(c.TransitionSec * fps) : 0);
            labels.Append($"[v{i}][a{i}]");
        }
        if (fadeFrames.Any(f => f > 0))
        {
            // video chain with xfade at scene changes; audio is concatenated separately (hard cuts)
            AppendDialogue(g, plan, inp, seq);
            for (int n = 0; n < vlabel.Count; n++) g.Append($"[{vlabel[n]}]settb=AVTB,format=yuv420p[w{n}];");
            string cur = "w0"; int run = frames[0];
            for (int n = 1; n < vlabel.Count; n++)
            {
                if (fadeFrames[n] > 0) g.Append($"[{cur}][w{n}]xfade=transition=fade:duration={F((double)fadeFrames[n] / fps)}:offset={F((double)run / fps)}[x{n}];");
                else g.Append($"[{cur}][w{n}]concat=n=2:v=1:a=0[x{n}];");
                cur = $"x{n}"; run += frames[n];
            }
            g.Append($"[{cur}]null[cv];");
        }
        else
        {
            j = 0;
            for (int s = 0; s < seq.Count; s++) g.Append(ItemAudio(plan, inp, seq[s], seq[s].Insert == null ? j : j++, frames[s]));
            g.Append($"{labels}concat=n={seq.Count}:v=1:a=1[cv][ca0];");
        }
        AppendMix(g, plan, inp, "ca");
        g.Append("[cv]");
        if (assFile != null) g.Append($"subtitles={FFmpeg.FilterPath(assFile)}:fontsdir={FFmpeg.FilterPath(fontsDir)},");
        if (preview && !half) g.Append($"scale={W / 2}:{H / 2},");
        g.Append("format=yuv420p[vout];");
        g.Append($"[ca]{loudnorm ?? $"loudnorm=I={F(plan.Audio.TargetLufs)}:TP={F(plan.Audio.TruePeak)}:LRA={F(plan.Audio.Lra)}"},aresample=48000[aout]");
        return plan.Series != null ? ShareInputs(g.ToString()) : g.ToString();
    }

    /// <summary>Series plans read hundreds of clips from one input; an explicit split/asplit (one decoder output → one graph input) measured ~25% faster
    /// than letting FFmpeg feed the same decoded stream to every clip's graph input. Frames are identical (same decoder, same trims).</summary>
    public static string ShareInputs(string graph)
    {
        var uses = Regex.Matches(graph, @"\[(\d+):([va])\]").GroupBy(m => m.Value).Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.Count());
        if (uses.Count == 0) return graph;
        var next = new Dictionary<string, int>();
        var body = Regex.Replace(graph, @"\[(\d+):([va])\]", m =>
        {
            if (!uses.ContainsKey(m.Value)) return m.Value;
            int n = next.GetValueOrDefault(m.Value) + 1; next[m.Value] = n;
            return $"[in{m.Groups[1].Value}{m.Groups[2].Value}_{n}]";
        });
        var pre = new StringBuilder();
        foreach (var (label, count) in uses.OrderBy(u => u.Key, StringComparer.Ordinal))
        {
            var m = Regex.Match(label, @"\[(\d+):([va])\]");
            pre.Append($"{label}{(m.Groups[2].Value == "v" ? "split" : "asplit")}={count}");
            for (int i = 1; i <= count; i++) pre.Append($"[in{m.Groups[1].Value}{m.Groups[2].Value}_{i}]");
            pre.Append(';');
        }
        return pre + body;
    }

    public static string BuildAudioOnlyGraph(EditPlan plan, Inputs inp, string tail)
    {
        var g = new StringBuilder();
        AppendDialogue(g, plan, inp, Sequence(plan));
        AppendMix(g, plan, inp, "ca");
        g.Append($"[ca]{tail}[m]");
        return plan.Series != null ? ShareInputs(g.ToString()) : g.ToString();
    }

    string[] EncodeArgs(bool preview, string output) => preview
        ? ["-map", "[vout]", "-map", "[aout]", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "30", "-threads", ff.Threads, "-r", "30", "-c:a", "aac", "-b:a", "128k", "-ar", "48000", "-ac", "2", "-movflags", "+faststart", output]
        : ["-map", "[vout]", "-map", "[aout]", "-c:v", "libx264", "-profile:v", "high", "-preset", "veryfast", "-crf", "19", "-threads", ff.Threads, "-r", "30",
           "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", "-movflags", "+faststart", "-fflags", "+bitexact", "-flags:v", "+bitexact", "-flags:a", "+bitexact", "-map_metadata", "-1", output];

    /// <param name="progress">Measured 0..1 across all ffmpeg passes (weighted by typical cost; each pass reports real out_time).</param>
    public async Task<RenderOutput> RenderAsync(EditPlan plan, Preset preset, IReadOnlyDictionary<string, SourceAsset> assets, string outFile, bool preview, CancellationToken ct, IProgress<double>? progress = null)
    {
        var fontFile = plan.Language switch { "zh-CN" => "NotoSansCJKsc-Bold.otf", "ko-KR" => "NotoSansCJKkr-Bold.otf", _ => null };
        bool hasText = plan.Telops.Any(t => t.Enabled) || InsertMap.Active(plan).Any(i => i.Kind == "card" && i.Text.Length > 0);
        if (hasText && fontFile != null && !File.Exists(Path.Combine(ff.FontsDir, fontFile)))
            throw new FFmpegException($"Missing font {fontFile}. Run scripts/get-tools.ps1 before rendering {plan.Language}.");
        var dir = Path.GetDirectoryName(outFile)!; Directory.CreateDirectory(dir);
        var inp = ResolveInputs(plan, assets, outFile + ".bgmenv.wav");
        var cmds = new List<string>();
        var finalDur = InsertMap.ToFinal(plan, plan.Duration, end: true);
        var hasBumperAudio = InsertMap.Active(plan).Any(i => i.Kind != "card" && i.HasAudio);
        var cues = HasCues(plan) && inp.Index.ContainsKey("bgmenv");
        var bumperEnd = hasBumperAudio || cues ? (preview ? 0.15 : 0.1) : 0;
        var measureEnd = preview ? bumperEnd : bumperEnd + 0.2;
        var encodeEnd = preview ? (cues ? 0.95 : 1) : 0.9;
        string? ass = null;
        if (plan.Telops.Any(t => t.Enabled) || InsertMap.Active(plan).Any(i => i.Kind == "card" && i.Text.Length > 0))
        {
            ass = Path.ChangeExtension(outFile, ".ass");
            await File.WriteAllTextAsync(ass, AssWriter.Write(plan, preset), new UTF8Encoding(false), ct);
        }
        var inArgs = inp.Flat.ToList();
        var scratch = new List<string>(inp.Scratch);
        string? ln = null;
        BgmMixReport? report = null;
        try
        {
            // the music is set against the loudness the edited dialogue actually has (measured, not the planning estimate)
            if (cues)
            {
                var dm = await ff.RunFfmpegAsync(["-hide_banner", "-nostats", .. inArgs, .. GraphArgs(BuildDialogueGraph(plan, inp), outFile + ".dgraph.txt", scratch), "-map", "[m]", "-f", "null", "-"], ct,
                    progress: progress.Slice(0, bumperEnd / 2), duration: plan.Duration);
                cmds.Add(dm.CommandLine);
                var di = IntegratedEbuRe().Matches(dm.StdErr);
                if (di.Count > 0 && double.TryParse(di[^1].Groups[1].Value, NumberStyles.Float, IC, out var speechI) && speechI > -70)
                    plan = plan with { Audio = plan.Audio with { SpeechLufs = Math.Round(speechI, 2) } };
            }
            // bumpers play at the loudness the body actually has, so the final whole-file normalisation keeps them level with the speech
            if (hasBumperAudio)
            {
                var body = plan with { Inserts = [] };
                // scene-matched music is laid out for the final timeline; without the inserts it would not line up, so it is left out of this measurement
                if (cues) body = body with { Audio = body.Audio with { Bgm = null } };
                var bm = await ff.RunFfmpegAsync(["-hide_banner", .. inArgs, .. GraphArgs(BuildAudioOnlyGraph(body, inp, "ebur128"), outFile + ".bgraph.txt", scratch), "-map", "[m]", "-f", "null", "-"], ct, progress: progress.Slice(cues ? bumperEnd / 2 : 0, bumperEnd), duration: plan.Duration);
                cmds.Add(bm.CommandLine);
                var mi = IntegratedEbuRe().Matches(bm.StdErr);
                if (mi.Count > 0 && double.TryParse(mi[^1].Groups[1].Value, NumberStyles.Float, IC, out var bodyI) && bodyI > -70)
                    plan = plan with { Inserts = plan.Inserts.Select(i => i.Kind != "card" && i.HasAudio ? i with { GainDb = Math.Round(Math.Clamp(i.GainDb + bodyI - plan.Audio.TargetLufs, -40, 20), 2) } : i).ToList() };
            }
            if (!preview)
            {
                // pass 1: measure the edited audio only (fast)
                var m = await ff.RunFfmpegAsync(["-hide_banner", .. inArgs, .. GraphArgs(BuildAudioOnlyGraph(plan, inp, $"loudnorm=I={F(plan.Audio.TargetLufs)}:TP={F(plan.Audio.TruePeak)}:LRA={F(plan.Audio.Lra)}:print_format=json"), outFile + ".agraph.txt", scratch),                 "-map", "[m]", "-f", "null", "-"], ct, progress: progress.Slice(bumperEnd, measureEnd), duration: finalDur);
                cmds.Add(m.CommandLine);
                string J(string k) => Regex.Match(m.StdErr, $"\"{k}\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                if (J("input_i") is { Length: > 0 } ii && ii != "-inf")
                    ln = $"loudnorm=I={F(plan.Audio.TargetLufs)}:TP={F(plan.Audio.TruePeak)}:LRA={F(plan.Audio.Lra)}:measured_I={J("input_i")}:measured_TP={J("input_tp")}:measured_LRA={J("input_lra")}:measured_thresh={J("input_thresh")}:offset={J("target_offset")}:linear=true";
            }
            var tmp = outFile + ".part.mp4";
            var r = await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-y", .. inArgs, .. GraphArgs(BuildGraph(plan, inp, ass, ff.FontsDir, preview, ln), outFile + ".graph.txt", scratch), .. EncodeArgs(preview, tmp)], ct, progress: progress.Slice(measureEnd, encodeEnd), duration: finalDur);
            cmds.Add(r.CommandLine);
            if (r.ExitCode != 0) throw new FFmpegException("レンダリングに失敗しました: " + FFmpeg.Tail(r.StdErr));
            File.Move(tmp, outFile, true);
            // verification: the same dialogue and music chains rendered as separate stems and measured by ebur128
            if (cues)
            {
                var st = await ff.RunFfmpegAsync(["-hide_banner", "-nostats", .. inArgs, .. GraphArgs(BuildStemGraph(plan, inp), outFile + ".sgraph.txt", scratch),
                    "-map", "[d]", "-f", "null", "-", "-map", "[mu]", "-f", "null", "-"], ct, progress: progress.Slice(encodeEnd, preview ? 1 : encodeEnd), duration: finalDur);
                cmds.Add(st.CommandLine);
                if (st.ExitCode == 0) report = MixReport(plan, BgmAnalyzer.ParseFrames(st.StdErr, "dlg"), BgmAnalyzer.ParseFrames(st.StdErr, "mus"));
            }
        }
        finally { foreach (var f in scratch) try { File.Delete(f); } catch (IOException) { } }
        if (preview) { progress?.Report(1); return new RenderOutput(outFile, cmds, double.NaN, double.NaN) { Bgm = report }; }

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
        progress?.Report(1);
        return new RenderOutput(outFile, cmds, I, TP) { Bgm = report };
    }

    /// <summary>Windows limits a command line to 32,767 characters; long graphs (series parts have hundreds of jump cuts) are passed as a file (FFmpeg 7+: -/option file).</summary>
    public const int MaxInlineGraph = 16_000;

    static string[] GraphArgs(string graph, string file, List<string> scratch)
    {
        if (graph.Length <= MaxInlineGraph) return ["-filter_complex", graph];
        File.WriteAllText(file, graph, new UTF8Encoding(false));
        scratch.Add(file);
        return ["-/filter_complex", file];
    }

    public async Task<(double I, double TP)> MeasureLoudnessAsync(string file, CancellationToken ct)
    {
        var r = await ff.RunFfmpegAsync(["-hide_banner", "-i", file, "-vn", "-af", "loudnorm=I=-14:TP=-1.5:print_format=summary", "-f", "null", "-"], ct);
        return (Num(IntegratedRe().Match(r.StdErr)), Num(TruePeakRe().Match(r.StdErr)));
    }

    static double Num(Match m) => m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, IC, out var d) ? d : double.NaN;

    /// <summary>Thumbnail stills at the given FINAL-timeline times. <paramref name="saturation"/> &gt; 1 grades the still more vivid (variety look: thumbnails measured ≈ 1.7× the video's saturation).</summary>
    public async Task<List<string>> ThumbnailsAsync(string video, IReadOnlyList<double> times, string prefix, double saturation, CancellationToken ct)
    {
        var list = new List<string>();
        for (int i = 0; i < times.Count; i++)
        {
            var f = $"{prefix}_thumb{i + 1}.png";
            List<string> args = ["-hide_banner", "-loglevel", "error", "-y", "-ss", F(times[i]), "-i", video, "-frames:v", "1"];
            if (Math.Abs(saturation - 1) > 0.01) args.AddRange(["-vf", $"eq=saturation={F(saturation)}"]);
            args.Add(f);
            var r = await ff.RunFfmpegAsync(args, ct);
            if (r.ExitCode == 0) list.Add(f);
        }
        return list;
    }

    [GeneratedRegex(@"Input Integrated:\s*(-?[\d.]+|-inf)")]
    private static partial Regex IntegratedRe();
    [GeneratedRegex(@"I:\s+(-?[\d.]+) LUFS")]
    private static partial Regex IntegratedEbuRe();
    [GeneratedRegex(@"Input True Peak:\s*([-+]?[\d.]+|-inf)")]
    private static partial Regex TruePeakRe();
}
