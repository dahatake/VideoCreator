using System.Globalization;
using VideoCreator.Audio;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Qa;
using VideoCreator.Rendering;

namespace VideoCreator.Tests;

/// <summary>
/// Scene-matched BGM and speech-keyed ducking (Planning.BgmPlanner, Audio.BgmAnalyzer, Renderer cue bus). The level claims are checked with FFmpeg's own
/// ebur128 meter on rendered audio, and the speech-over-music difference is re-derived independently from the summed mix (mix − dialogue power).
/// </summary>
public class BgmSceneTests : IAsyncLifetime
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-bgm-" + Guid.NewGuid().ToString("N")[..8]);
    CancellationToken Ct => TestContext.Current.CancellationToken;
    FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
    static string F(double x) => x.ToString("0.###", IC);

    public ValueTask InitializeAsync() { Directory.CreateDirectory(_dir); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { try { Directory.Delete(_dir, true); } catch { } return ValueTask.CompletedTask; }

    static readonly SemaphoreSlim LibGate = new(1, 1);
    static IReadOnlyList<BgmTrack>? _lib;
    static string? _libDir;
    async Task<IReadOnlyList<BgmTrack>> LibraryAsync()
    {
        await LibGate.WaitAsync(Ct);
        try
        {
            if (_lib != null && Directory.Exists(_libDir)) return _lib;
            // one synthesised library per analysis version, reused across runs (deterministic recipe)
            _libDir = Path.Combine(Path.GetTempPath(), $"vc-bgmlib-v{BgmAnalyzer.Version}");
            _lib = await new AudioLibrary(Ff, _libDir).MeasuredTracksAsync(Ct);
            return _lib;
        }
        finally { LibGate.Release(); }
    }

    async Task<List<BgmAnalyzer.Frame>> FramesAsync(string file)
    {
        var r = await Ff.RunFfmpegAsync(["-hide_banner", "-nostats", "-i", file, "-af", "ebur128=framelog=info", "-f", "null", "-"], Ct);
        return BgmAnalyzer.ParseFrames(r.StdErr);
    }

    // ---------------- measurement ----------------

    [Fact] // the in-code BS.1770 gating over ebur128's 400 ms blocks reproduces FFmpeg's own integrated loudness and loudnorm's input_i
    public async Task Integrated_loudness_matches_ffmpeg_meters()
    {
        var lib = await LibraryAsync();
        foreach (var t in lib)
        {
            var frames = await FramesAsync(t.Path);
            var r = await Ff.RunFfmpegAsync(["-hide_banner", "-i", t.Path, "-af", "loudnorm=print_format=json", "-f", "null", "-"], Ct);
            double ln = double.Parse(System.Text.RegularExpressions.Regex.Match(r.StdErr, "\"input_i\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value, IC);
            Assert.InRange(BgmAnalyzer.Integrated(frames.Select(f => f.M)) - ln, -0.3, 0.3);
            Assert.InRange(t.Analysis!.Lufs - ln, -0.5, 0.5);
        }
    }

    [Fact] // energy follows what the tracks are: chill (90 BPM, sparse) < upbeat (120) < hype (140, dense, bright)
    public async Task Library_track_energy_is_ordered_by_tempo_and_attacks()
    {
        var lib = (await LibraryAsync()).ToDictionary(t => t.Id, t => t.Analysis!);
        Assert.True(lib["chill"].Energy < lib["upbeat"].Energy && lib["upbeat"].Energy < lib["hype"].Energy,
            string.Join(", ", lib.Select(kv => $"{kv.Key} E={kv.Value.Energy} onset={kv.Value.OnsetRate} c={kv.Value.Brightness}")));
        Assert.True(lib["chill"].OnsetRate < lib["hype"].OnsetRate);
        foreach (var a in lib.Values) { Assert.InRange(a.Start, 0, 0.5); Assert.InRange(a.Duration - a.End, -0.1, 0.6); Assert.NotEmpty(a.ShortTerm); }
    }

    [Fact] // the playable part skips leading silence and the fade tail, so a loop never plays the silence
    public async Task Leading_silence_and_fade_tail_are_not_playable()
    {
        var src = (await LibraryAsync()).First(t => t.Id == "upbeat").Path;
        var padded = Path.Combine(_dir, "padded.wav");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-i", src, "-af", "adelay=3000:all=1,apad=pad_dur=4,afade=t=out:st=17:d=2", padded], Ct);
        var a = await BgmAnalyzer.AnalyzeAsync(Ff, padded, Ct);
        Assert.InRange(a.Start, 2.6, 3.2);
        Assert.InRange(a.End, 17.3, 19.2);
        Assert.InRange(a.Duration, 22.5, 23.5);
    }

    // ---------------- planning ----------------

    static Clip C(string id, double m0, double m1, string role = "body", double speed = 1, double transition = 0) =>
        new() { ClipId = id, AssetId = "a1", SrcIn = m0, SrcOut = m1, Role = role, Speed = speed, TransitionSec = transition };

    static Transcript Words(params (double T0, double T1)[] w) => new()
    {
        Provider = "test", Words = w.Select((x, i) => new Word($"w{i}", x.T0, x.T1, "あ", 1)).ToList(),
    };

    [Fact] // words → spans on the body timeline: gaps < 0.8 s bridged (no pumping between words), clip offsets and speed applied
    public void Speech_spans_follow_words_and_bridge_short_gaps()
    {
        var clips = new List<Clip> { C("c1", 10, 20), C("c2", 50, 70, speed: 2) };
        var tr = Words((11, 12), (12.5, 13), (15, 16), (52, 54), (54.4, 56));
        var spans = BgmPlanner.SpeechSpans(clips, tr, new SignalSet());
        Assert.Equal(3, spans.Count);
        Assert.Equal((1.0, 3.0), (spans[0].T0, spans[0].T1));   // 11–13 bridged across the 0.5 s gap
        Assert.Equal((5.0, 6.0), (spans[1].T0, spans[1].T1));
        Assert.Equal((11.0, 13.0), (spans[2].T0, spans[2].T1)); // clip 2 starts at body 10, 2x speed: 52→11, 56→13
    }

    [Fact] // scenes only at the video's own scene changes, never shorter than MinSceneSec; a short video is one scene
    public void Scenes_respect_minimum_length_and_prefer_strong_boundaries()
    {
        var clips = new List<Clip> { C("t", 300, 305, "teaser"), C("b1", 0, 30), C("b2", 40, 60), C("b3", 60.5, 75), C("b4", 120, 150) };
        // body starts: teaser 0–5, b1 5–35, b2 35–55 (jump 10 s), b3 55–69.5 (no jump), b4 69.5–99.5 (jump 45 s)
        var scenes = BgmPlanner.Scenes(clips, [], null);
        Assert.All(scenes, s => Assert.True(s.T1 - s.T0 >= BgmPlanner.MinSceneSec - 1e-6, $"{s.T0}-{s.T1}"));
        Assert.Contains(scenes, s => Math.Abs(s.T0 - 35) < 1e-6 || Math.Abs(s.T0 - 69.5) < 1e-6);
        Assert.DoesNotContain(scenes, s => Math.Abs(s.T0 - 5) < 1e-6);  // teaser→body is strong but leaves a 5 s scene: refused
        var shortClips = new List<Clip> { C("a", 0, 15), C("b", 100, 130) };
        Assert.Single(BgmPlanner.Scenes(shortClips, [], null));
        var card = new Insert { Id = "card1", Kind = "card", At = 25, Duration = 2.5 };
        var withCard = BgmPlanner.Scenes([C("x", 0, 60)], [card], null);
        Assert.Equal([0.0, 25.0], withCard.Select(s => s.T0));
        Assert.Equal("場面転換カード", withCard[1].StartReason);
    }

    static BgmTrack T(string id, double energy) => new()
    {
        Id = id, Name = id, Path = id + ".wav", Source = "user", RightsConfirmed = true,
        Analysis = new BgmAnalysis { Version = BgmAnalyzer.Version, Duration = 120, Lufs = -14, Start = 0, End = 120, Energy = energy, Bpm = 60 + energy * 100, OnsetRate = 1 + energy * 6, Brightness = 1000 + energy * 2000 },
    };

    static BgmCue Cue(int i, double t0, double t1, double e, bool auto = true, string? track = null) =>
        new() { Id = $"bgm{i}", T0 = t0, T1 = t1, SceneEnergy = e, Auto = auto, Track = track, Scene = "test" };

    [Fact] // energetic scenes get the energetic track; a one-scene blip does not cause two switches; a user's pick is kept and steers its neighbours
    public void Viterbi_matches_energy_and_charges_switches()
    {
        var pool = new[] { T("calm", 0.15), T("mid", 0.5), T("hot", 0.9) };
        var cues = new List<BgmCue> { Cue(1, 0, 30, 0.1), Cue(2, 30, 60, 0.12), Cue(3, 60, 90, 0.85), Cue(4, 90, 120, 0.9) };
        var res = BgmPlanner.Assign(cues, pool);
        Assert.Equal(["calm", "calm", "hot", "hot"], res.Select(c => c.Track));
        Assert.All(res, c => Assert.False(string.IsNullOrEmpty(c.Reason)));
        // a short, slightly livelier scene between two calm ones stays on the calm track (switch cost)
        var blip = BgmPlanner.Assign([Cue(1, 0, 30, 0.1), Cue(2, 30, 50, 0.3), Cue(3, 50, 80, 0.1)], pool);
        Assert.All(blip, c => Assert.Equal("calm", c.Track));
        var fixedMid = BgmPlanner.Assign([Cue(1, 0, 30, 0.1), Cue(2, 30, 60, 0.9, auto: false, track: "mid"), Cue(3, 60, 90, 0.9)], pool);
        Assert.Equal("mid", fixedMid[1].Track);
        Assert.False(fixedMid[1].Auto);
    }

    [Fact] // duck depth = gap + target − local voice level, deeper under a loud passage of the song; levels and trims change it
    public void Recompute_derives_depth_from_target_local_level_and_ride()
    {
        var loudChorus = T("song", 0.5) with
        {
            Analysis = T("song", 0.5).Analysis! with { Lufs = -14, ShortTerm = Enumerable.Range(0, 240).Select(i => i * 0.5 is >= 20 and < 40 ? -8.0 : -15.0).ToList() },
        };
        var a = new AudioSpec
        {
            Master = "a1", Bgm = "song", BgmGapLu = -8, BgmSpeechLd = 14, BgmMode = "single",
            BgmCues = [BgmPlanner.Fill(Cue(1, 0, 60, 0.5, track: "song"), loudChorus)],
            DuckSpans = [new DuckSpan(2, 6, 0, 0), new DuckSpan(10, 12, -4, 0), new DuckSpan(25, 30, 0, 0)],
        };
        var r = BgmPlanner.Recompute(a, [], 60, _ => loudChorus);
        Assert.Equal(6, r.DuckSpans[0].Db, 1);        // −8 + 14 − 0
        Assert.Equal(10, r.DuckSpans[1].Db, 1);       // quieter voice (−4 dB) → 4 dB deeper
        Assert.Equal(12, r.DuckSpans[2].Db, 1);       // chorus 6 LU over the track's loudness → 6 dB more
        var louder = BgmPlanner.Recompute(a with { BgmSpeechLd = 20 }, [], 60, _ => loudChorus);
        Assert.Equal(12, louder.DuckSpans[0].Db, 1);
        Assert.Equal(-14 + -8 + 3 - -14, BgmPlanner.CueGainDb(a, a.BgmCues[0] with { TrimDb = 3 }, -14), 2);
    }

    [Fact] // look-ahead attack, full depth over the speech, release after; mutes are silent; fade in and out
    public void Envelope_ducks_ahead_of_speech_and_mutes()
    {
        var a = new AudioSpec { Master = "a1", DuckSpans = [new DuckSpan(5, 8, 0, 12)], BgmMutes = [new Interval(15, 16)] };
        var g = BgmPlanner.Envelope(a, 30, 28, 1000);
        double Db(double t) => 20 * Math.Log10(Math.Max(1e-9, g[(int)Math.Round(t * 1000)]));
        Assert.Equal(0, Db(3), 1);
        Assert.InRange(Db(5 - BgmPlanner.AttackSec / 2), -7, -5);   // halfway down before the first word
        Assert.Equal(-12, Db(5), 1);
        Assert.Equal(-12, Db(8), 1);
        Assert.Equal(0, Db(8 + BgmPlanner.ReleaseSec + 0.01), 1);
        Assert.Equal(0, g[15500]);
        Assert.Equal(0, g[0]);
        Assert.Equal(0, g[28500]);
        Assert.InRange(g[27500], 0.45, 0.55);
    }

    [Fact] // cues end to end on the final timeline; a track change under a muted card happens inside the card; the same track continues seamlessly
    public void Final_spans_place_crossfades_and_continue_positions()
    {
        var cues = new List<BgmCue>
        {
            Cue(1, 0, 25, 0.1, track: "a") with { In = 1, Out = 11 },
            Cue(2, 25, 50, 0.9, track: "b") with { In = 0, Out = 20 },
            Cue(3, 50, 80, 0.9, track: "b") with { In = 0, Out = 20 },
            Cue(4, 80, 100, 0.1, track: "a") with { In = 1, Out = 11 },
        };
        var card = new Insert { Id = "card1", Kind = "card", At = 25, Duration = 2.5, Bgm = "upbeat", Enabled = true };
        var spans = BgmPlanner.FinalSpans(cues, InsertMap.Active([card]), 102.5);
        Assert.Equal(0, spans[0].Start);
        Assert.Equal(26.25, spans[1].Start, 3);    // centre of the card (25–27.5), fade ≤ card length
        Assert.Equal(2.0, spans[1].FadeIn, 3);
        Assert.True(spans[2].CoherentIn);
        Assert.Equal(BgmPlanner.SameTrackFadeSec, spans[2].FadeIn, 3);
        // b continues: position at 52.5 = position at 26.25 + 26.25 (mod 20)
        Assert.Equal((spans[1].Position + (spans[2].Start - spans[1].Start)) % 20, spans[2].Position, 3);
        // a resumes where it stopped (26.25 s into a 10 s loop → 6.25)
        Assert.Equal(6.25, spans[3].Position, 3);
        Assert.Equal(102.5, spans[^1].End);
    }

    // ---------------- rendering (real FFmpeg) ----------------

    /// <summary>A voice-like signal (180 Hz with harmonics, syllable-rate modulation) only inside <paramref name="speech"/>, plus a matching transcript.</summary>
    async Task<(SourceAsset Asset, Transcript Tr)> TalkAsync(string name, double dur, (double T0, double T1)[] speech)
    {
        // the voice is synthesised here (a gate inside FFmpeg's aevalsrc / volume-enable proved pathologically slow in this build) and muxed with a still picture
        var voice = Path.Combine(_dir, name + ".voice.wav");
        int n = (int)(dur * 48000);
        var pcm = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / 48000.0;
            if (!speech.Any(s => t >= s.T0 && t < s.T1)) continue;
            double v = 0.3 * Math.Sin(2 * Math.PI * 180 * t) + 0.15 * Math.Sin(2 * Math.PI * 360 * t) + 0.1 * Math.Sin(2 * Math.PI * 720 * t)
                     + 0.06 * Math.Sin(2 * Math.PI * 1440 * t) + 0.03 * Math.Sin(2 * Math.PI * 2880 * t);
            pcm[i] = (float)(v * (0.65 + 0.35 * Math.Sin(2 * Math.PI * 4 * t)));
        }
        using (var fs = File.Create(voice)) using (var w = new BinaryWriter(fs))
        {
            w.Write("RIFF"u8); w.Write(36 + n * 4); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((short)3); w.Write((short)1); w.Write(48000); w.Write(48000 * 4); w.Write((short)4); w.Write((short)32);
            w.Write("data"u8); w.Write(n * 4); foreach (var v in pcm) w.Write(v);
        }
        var src = Path.Combine(_dir, name + ".mov");
        // PCM, not AAC: the bundled nightly FFmpeg's AAC encoder at 192 kb/s stalls on a digital-silence → signal onset
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", $"color=c=gray:s=320x180:r=30:d={F(dur)}", "-i", voice,
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "pcm_s16le", "-t", F(dur), src], Ct);
        var words = new List<(double, double)>();
        foreach (var (a, b) in speech) for (double t = a; t < b - 0.05; t += 0.25) words.Add((t, Math.Min(b, t + 0.22)));
        var asset = new SourceAsset { Id = "a1", ProjectId = "p", Path = src, Sha256 = "x", Role = AssetRole.Main, Media = new MediaInfo { Duration = dur, Width = 320, Height = 180, Fps = 30, AudioChannels = 1 } };
        return (asset, Words([.. words]));
    }

    EditPlan Plan(double dur, AudioSpec audio, Preset p, List<Insert>? inserts = null) => new()
    {
        EditplanId = "e", ProjectId = "p", Preset = p.Ref, Audio = audio, Inserts = inserts ?? [],
        Timeline = [new Clip { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = dur }],
    };

    Renderer NewRenderer(IReadOnlyList<BgmTrack> lib) => new(Ff) { BgmResolver = id => lib.FirstOrDefault(t => t.Id == id)?.Path };

    async Task<string> StemAsync(Renderer r, EditPlan plan, Dictionary<string, SourceAsset> assets, string name)
    {
        var inp = r.ResolveInputs(plan, assets, Path.Combine(_dir, name + ".env.wav"));
        var wav = Path.Combine(_dir, name + ".wav");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", .. inp.Flat, "-filter_complex", Renderer.BuildStemGraph(plan, inp), "-map", "[mu]", "-c:a", "pcm_f32le", wav, "-map", "[d]", "-f", "null", "-"], Ct);
        return wav;
    }

    static double RmsDb(float[] pcm, int ch, double t0, double t1)
    {
        int a = (int)(t0 * 48000), b = Math.Min(pcm.Length / ch, (int)(t1 * 48000)); double s = 0;
        for (int i = a; i < b; i++) { double v = pcm[i * ch]; s += v * v; }
        return 10 * Math.Log10(s / Math.Max(1, b - a) + 1e-15);
    }

    static float[] ReadF32(string wav)
    {
        var bytes = File.ReadAllBytes(wav);
        int data = 12; while (data < bytes.Length - 8 && System.Text.Encoding.ASCII.GetString(bytes, data, 4) != "data") data += 8 + BitConverter.ToInt32(bytes, data + 4);
        int len = BitConverter.ToInt32(bytes, data + 4);
        var res = new float[Math.Min(len, bytes.Length - data - 8) / 4];
        Buffer.BlockCopy(bytes, data + 8, res, 0, res.Length * 4);
        return res;
    }

    [Fact] // AT: the rendered music dips BEFORE the first word (look-ahead), sits at the planned depth under speech, and is back up after the release
    public async Task Rendered_music_ducks_ahead_of_speech_by_the_planned_depth()
    {
        var lib = await LibraryAsync();
        var p = Fixtures.Presets["P1"];
        var (asset, tr) = await TalkAsync("talk", 30, [(4, 10), (18, 24)]);
        var assets = new Dictionary<string, SourceAsset> { ["a1"] = asset };
        var clips = new List<Clip> { new() { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = 30 } };
        var audio = BgmPlanner.Build(new AudioSpec { Master = "a1", SfxEnabled = false }, clips, [], null, tr, new SignalSet(), [], "single", [lib.First(t => t.Id == "chill")], 0, -8, 14, []);
        audio = BgmPlanner.Recompute(audio, [], 30, id => lib.FirstOrDefault(t => t.Id == id)) with { SpeechLufs = -20 };
        var plan = Plan(30, audio, p);
        var depth = plan.Audio.DuckSpans[0].Db;
        Assert.InRange(depth, 6, 6 + BgmPlanner.MaxRideDb);
        var r = NewRenderer(lib);
        // the same music without ducking: the per-window difference isolates the gain automation from the beat-to-beat level of the music
        var ducked = ReadF32(await StemAsync(r, plan, assets, "duck"));
        var plain = ReadF32(await StemAsync(r, plan with { Audio = plan.Audio with { Duck = false, DuckSpans = plan.Audio.DuckSpans.Select(d => d with { Db = 0 }).ToList() } }, assets, "plain"));
        double Cut(double t0, double t1) => RmsDb(plain, 2, t0, t1) - RmsDb(ducked, 2, t0, t1);
        Assert.InRange(Cut(5, 9), depth - 1, depth + 1);
        Assert.True(Cut(3.95, 4.0) >= 0.6 * depth, $"look-ahead: {Cut(3.95, 4.0):F1} dB before the first word (depth {depth:F1})");
        Assert.InRange(Cut(3.0, 3.6), -0.5, 0.5);
        Assert.InRange(Cut(11, 16), -0.5, 0.5);    // released within ReleaseSec after the last word (~10 s)
    }

    [Fact] // AT: speech − music measured by the renderer's stems (ebur128) meets the target, and the summed mix gives the same answer independently
    public async Task Speech_over_music_meets_target_on_rendered_stems_and_mix()
    {
        var lib = await LibraryAsync();
        var p = Fixtures.Presets["P1"];
        var (asset, tr) = await TalkAsync("talk2", 40, [(3, 12), (15, 22), (26, 36)]);
        var assets = new Dictionary<string, SourceAsset> { ["a1"] = asset };
        var clips = new List<Clip> { new() { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = 40 } };
        var audio = BgmPlanner.Build(new AudioSpec { Master = "a1", SfxEnabled = false }, clips, [], null, tr, new SignalSet(), [], "single", [lib.First(t => t.Id == "hype")], 0, -6, 14, []);
        audio = BgmPlanner.Recompute(audio, [], 40, id => lib.FirstOrDefault(t => t.Id == id));
        var plan = Plan(40, audio, p);
        var r = NewRenderer(lib);
        var outp = await r.RenderAsync(plan, p, assets, Path.Combine(_dir, "out", "mix.mp4"), preview: true, Ct);
        var rep = outp.Bgm ?? throw new Xunit.Sdk.XunitException("no BGM report");
        Assert.True(rep.Ld >= 14 - 1.5 && rep.Ld >= BgmPlanner.ResearchFloorLd, $"LD {rep.Ld} (p10 {rep.LdP10})");
        Assert.NotNull(rep.GapLu);
        Assert.InRange(rep.GapLu!.Value, -6 - 2, -6 + 2);
        Assert.Equal(QaStatus.Pass, QaEngine.SpeechOverMusic(rep).Status);

        // independent: the summed pre-normalisation mix minus the dialogue alone (power per 400 ms block), integrated over the speech blocks
        var inp = r.ResolveInputs(plan, assets, Path.Combine(_dir, "ind.env.wav"));
        var dm = await Ff.RunFfmpegAsync(["-hide_banner", .. inp.Flat, "-filter_complex", Renderer.BuildDialogueGraph(plan, inp), "-map", "[m]", "-f", "null", "-"], Ct);
        double speechI = double.Parse(System.Text.RegularExpressions.Regex.Matches(dm.StdErr, @"I:\s+(-?[\d.]+) LUFS")[^1].Groups[1].Value, IC);
        var measured = plan with { Audio = plan.Audio with { SpeechLufs = speechI } };
        async Task<List<BgmAnalyzer.Frame>> Render(EditPlan pl, string name)
        {
            var wav = Path.Combine(_dir, name + ".wav");
            await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", .. inp.Flat, "-filter_complex", Renderer.BuildAudioOnlyGraph(pl, inp, "anull"), "-map", "[m]", "-c:a", "pcm_f32le", wav], Ct);
            return await FramesAsync(wav);
        }
        var mix = await Render(measured, "summed");
        var dlg = await Render(measured with { Audio = measured.Audio with { Bgm = null } }, "dialogue");
        var final = Renderer.FinalAudio(measured);
        var idx = Enumerable.Range(0, Math.Min(mix.Count, dlg.Count)).Where(i => final.DuckSpans.Any(s => dlg[i].T - 0.4 >= s.T0 && dlg[i].T <= s.T1)).ToList();
        double P(double l) => Math.Pow(10, l / 10);
        double music = 10 * Math.Log10(idx.Average(i => Math.Max(1e-12, P(mix[i].M) - P(dlg[i].M))));
        double ldIndependent = BgmAnalyzer.Integrated(idx.Select(i => dlg[i].M)) - music;
        Assert.InRange(ldIndependent - rep.Ld, -1.5, 1.5);
    }

    [Fact] // a scene change switches tracks with a cross-fade (no hole), and a short track loops its playable part without gaps
    public async Task Scene_change_crossfades_tracks_and_loops_without_gaps()
    {
        var lib = await LibraryAsync();
        var p = Fixtures.Presets["P1"];
        var (asset, _) = await TalkAsync("quiet", 50, [(0, 0.01)]);
        var assets = new Dictionary<string, SourceAsset> { ["a1"] = asset };
        var chill = lib.First(t => t.Id == "chill"); var hype = lib.First(t => t.Id == "hype");
        var audio = new AudioSpec
        {
            Master = "a1", SfxEnabled = false, Bgm = "chill", BgmMode = "scene", SpeechLufs = -20,
            BgmCues = [BgmPlanner.Fill(Cue(1, 0, 25, 0.1), chill), BgmPlanner.Fill(Cue(2, 25, 50, 0.9), hype)],
        };
        var plan = Plan(50, audio, p);
        var pcm = ReadF32(await StemAsync(NewRenderer(lib), plan, assets, "xfade"));
        // no hole: every 100 ms window from 1 s to 48 s carries music (chill loops at 16 s, hype at 13.7 s)
        double min = double.MaxValue;
        for (double t = 1; t < 48; t += 0.1) min = Math.Min(min, RmsDb(pcm, 2, t, t + 0.1));
        Assert.True(min > -50, $"quietest 100 ms: {min:F1} dB");
        // before the change it is the calm track, after it the energetic one (attack rate measured on the stem)
        short[] Mono(double t0, double t1) => Enumerable.Range((int)(t0 * 48000), (int)((t1 - t0) * 48000)).Where(i => i % 3 == 0).Select(i => (short)Math.Clamp(pcm[i * 2] * 32767 * 4, -32768, 32767)).ToArray();
        var before = BgmAnalyzer.Features(Mono(5, 23)); var after = BgmAnalyzer.Features(Mono(27, 45));
        Assert.True(after.OnsetRate > before.OnsetRate, $"onsets before {before.OnsetRate:F2}/s, after {after.OnsetRate:F2}/s");
    }

    [Fact] // editing on the review screen: a scene's track, its trim, the levels (LD never below the research floor), off/on, back to automatic
    public void Review_edits_change_the_plan_and_are_recorded()
    {
        var pool = new[] { T("calm", 0.15), T("hot", 0.9) };
        var ctx = new BgmContext(id => pool.FirstOrDefault(t => t.Id == id), pool);
        var cues = BgmPlanner.Assign([Cue(1, 0, 30, 0.1), Cue(2, 30, 60, 0.9)], pool);
        var plan = new EditPlan
        {
            EditplanId = "e", ProjectId = "p", Preset = new PresetRef("P1", "1"), Timeline = [C("c", 0, 60)],
            Audio = new AudioSpec { Master = "a1", Bgm = "calm", BgmMode = "scene", BgmCues = cues, DuckSpans = [new DuckSpan(1, 5, 0, 0)] },
        };
        var p1 = PlanEditor.Apply(plan, [new PlanOp("set_bgm_cue", "bgm2", "calm")], ctx);
        Assert.Equal("calm", p1.Audio.BgmCues[1].Track);
        Assert.False(p1.Audio.BgmCues[1].Auto);
        var p2 = PlanEditor.Apply(p1, [new PlanOp("set_bgm_trim", "bgm1", Value: -40), new PlanOp("set_bgm_level", Type: "ld", Value: 4), new PlanOp("set_bgm_level", Type: "gap", Value: -12)], ctx);
        Assert.Equal(BgmPlanner.MinTrimDb, p2.Audio.BgmCues[0].TrimDb);
        Assert.Equal(BgmPlanner.ResearchFloorLd, p2.Audio.BgmSpeechLd);
        Assert.Equal(-12, p2.Audio.BgmGapLu);
        Assert.Equal(0, PlanEditor.RecomputeBgm(p2, ctx).Audio.DuckSpans[0].Db, 1);   // music already 12 LU under the voice: no further ducking for a 10 LU target
        var off = PlanEditor.Apply(p2, [new PlanOp("set_bgm", "none")], ctx);
        Assert.Null(off.Audio.Bgm);
        Assert.Equal(2, off.Audio.BgmCues.Count);
        Assert.False(Renderer.HasCues(off));
        var auto = PlanEditor.Apply(off, [new PlanOp("set_bgm", "scene")], ctx);
        Assert.Equal(["calm", "hot"], auto.Audio.BgmCues.Select(c => c.Track));
        Assert.NotNull(auto.Audio.Bgm);
        Assert.Contains(auto.Provenance.Edits, e => e.Op == "set_bgm_cue");
        Assert.NotEqual(EditPlanFingerprint.Of(plan), EditPlanFingerprint.Of(p2));
        Assert.Throws<KeyNotFoundException>(() => PlanEditor.Apply(plan, [new PlanOp("set_bgm_cue", "bgm1", "missing")], ctx));
    }

    [Fact] // a range adjustment rebuilds the cues; the user's track, trims and levels carry over
    public void Carry_keeps_user_choices_after_rebuild()
    {
        var prev = new AudioSpec
        {
            Master = "a1", Bgm = "calm", BgmGapLu = -12, BgmSpeechLd = 18,
            BgmCues = [Cue(1, 0, 30, 0.1, auto: false, track: "hot") with { TrimDb = -3 }, Cue(2, 30, 60, 0.9, track: "calm") with { TrimDb = 2 }],
        };
        var rebuilt = new AudioSpec { Master = "a1", Bgm = "calm", BgmCues = [Cue(1, 0, 32, 0.1, track: "calm"), Cue(2, 32, 61, 0.9, track: "hot")] };
        var c = BgmPlanner.Carry(rebuilt, prev);
        Assert.Equal("hot", c.BgmCues[0].Track);
        Assert.Equal(-3, c.BgmCues[0].TrimDb);
        Assert.Equal("hot", c.BgmCues[1].Track);       // automatic cue: the new choice, the user's trim
        Assert.Equal(2, c.BgmCues[1].TrimDb);
        Assert.Equal((-12, 18), (c.BgmGapLu, c.BgmSpeechLd));
    }
}
