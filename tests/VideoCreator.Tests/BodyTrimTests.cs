using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Planning;
using VideoCreator.Qa;
using VideoCreator.Rendering;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

public sealed class BodyTrimTests
{
    static SourceAsset Asset(string id = "main", double duration = 20) => new()
    {
        Id = id, ProjectId = "p", Path = "", Sha256 = new string('a', 64),
        Role = AssetRole.Main, Media = Fixtures.Landscape(duration),
    };

    static Transcript Speech() => new()
    {
        Provider = "fixture", Duration = 20,
        Words = [new("w1", 3, 5, "最初", 1), new("w2", 12, 15, "最後", 1)],
        Sentences = [new(0, 3, 5, "最初の発話。", 1, 0, 0), new(1, 12, 15, "結論。", 1, 1, 1)],
    };

    [Fact]
    public void Suggestions_protect_all_speech_and_automatic_requires_black_and_silence()
    {
        var sig = new SignalSet { Silences = [new(0, 3), new(15, 20)], Blacks = [new(0, 4), new(14, 20)] };
        var result = BodyTrimmer.Analyze(Asset(), Speech(), sig);
        Assert.Equal(new Interval(2.75, 15.5), result.Suggested);
        Assert.Equal(result.Suggested, result.Automatic);
        Assert.Equal(new Interval(0, 20), BodyTrimmer.Analyze(Asset(), Speech(), sig with { Blacks = [] }).Automatic);
        Assert.Equal(new Interval(0, 20), BodyTrimmer.Analyze(Asset(), Speech(), sig with { Silences = [] }).Automatic);
        // Even when the detectors say black/silent for too long, transcript evidence protects the first and last utterance.
        Assert.Equal(result.Suggested, BodyTrimmer.Analyze(Asset(), Speech(), new()
        {
            Silences = [new(0, 8), new(9, 20)], Blacks = [new(0, 8), new(9, 20)],
        }).Automatic);
    }

    [Fact]
    public void Ambiguous_recording_and_internal_pauses_are_not_removed_and_edges_are_independent()
    {
        var noSpeech = new Transcript { Provider = "none", Duration = 20 };
        var allBlack = new SignalSet { Silences = [new(0, 20)], Blacks = [new(0, 19.97)] };
        Assert.Equal(new Interval(0, 20), BodyTrimmer.Analyze(Asset(), noSpeech, allBlack).Automatic);
        var sig = new SignalSet { Silences = [new(0, 2), new(7, 9)], Blacks = [new(0, 2), new(7, 9)] };
        Assert.Equal(new Interval(1.75, 20), BodyTrimmer.Analyze(Asset(), Speech(), sig).Automatic);
        Assert.Equal(new Interval(0, 20), BodyTrimmer.Analyze(Asset(), Speech(), sig with { Blacks = [new(7, 9)] }).Automatic);
    }

    [Fact]
    public void One_visible_frame_between_black_runs_is_kept_but_blackdetects_last_frame_end_reaches_the_edge()
    {
        const double frame = 1 / 30.0;
        // blackdetect reports a run ending at EOF at the last frame's start, one proxy frame before the duration
        var sig = new SignalSet { Silences = [new(0, 6), new(15, 20)], Blacks = [new(0, 2), new(2 + frame, 4), new(14, 20 - frame)] };
        Assert.Equal(new Interval(1.75, 15.5), BodyTrimmer.Analyze(Asset(), Speech(), sig).Automatic);
    }

    [Fact]
    public void Manual_suggestion_snaps_asr_word_edges_to_measured_sound_and_keeps_sound_continuing_the_speech()
    {
        // The recognizer timed the last word 0.4s past the measured end of sound (spikes/media/talk.mp4: 124.19 vs 123.75).
        var late = Speech() with { Words = [new("w1", 3, 5, "最初", 1), new("w2", 12, 15.4, "最後", 1)], Sentences = [] };
        var sig = new SignalSet { Silences = [new(0, 3), new(15, 20)], Blacks = [new(0, 4), new(14, 20)] };
        var result = BodyTrimmer.Analyze(Asset(), late, sig);
        Assert.Equal(new Interval(2.75, 15.5), result.Suggested);
        Assert.Equal(new Interval(2.75, 15.9), result.Automatic); // automatic removal still never touches a recognized word
        // Sound joined to the speech (music, applause or room noise: RMS cannot tell) is reported for review, not guessed.
        var music = BodyTrimmer.Analyze(Asset(), Speech(), new SignalSet { Silences = [new(0, 1), new(17, 20)] });
        Assert.Equal(new Interval(2.75, 15.5), music.Suggested);
        Assert.Equal((2, 2), (music.SoundBefore, music.SoundAfter));
        Assert.Equal((0, 0), (result.SoundBefore, result.SoundAfter));
        // A word lying wholly inside measured silence is not moved: the recognizer may hear speech below -40 dBFS.
        var quiet = BodyTrimmer.Analyze(Asset(), Speech(), new SignalSet { Silences = [new(0, 6), new(15, 20)] });
        Assert.Equal(new Interval(2.75, 15.5), quiet.Suggested);
    }

    [Fact]
    public void A_last_word_timed_slightly_past_the_end_is_still_protected()
    {
        var tr = Speech() with { Words = [new("w1", 3.12, 5, "最初", 1), new("w2", 12, 20.02, "最後", 1)], Sentences = [] };
        var result = BodyTrimmer.Analyze(Asset(), tr, new SignalSet { Silences = [new(0, 3.2)], Blacks = [new(0, 4)] });
        Assert.Equal(new Interval(2.95, 20), result.Suggested);
        Assert.Equal(new Interval(2.87, 20), result.Automatic);
    }

    [Fact]
    public void A_video_without_an_audio_track_loses_only_its_fully_black_edges()
    {
        var mute = Asset() with { Media = Fixtures.Landscape(20) with { AudioChannels = 0 } };
        var result = BodyTrimmer.Analyze(mute, new Transcript { Provider = "none", Duration = 20 }, new SignalSet { Blacks = [new(0, 2), new(17, 20)] });
        Assert.Equal(new Interval(1.75, 17.5), result.Automatic);
        Assert.Equal(new Interval(0, 20), BodyTrimmer.Analyze(mute, new Transcript { Provider = "none", Duration = 20 }, new SignalSet()).Automatic);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    public void Main_video_times_convert_to_the_audio_spine_with_offset_and_drift(double offset)
    {
        var audio = Asset("audio", 30) with { Role = AssetRole.AudioMaster, AudioRef = true };
        var main = Asset() with { SyncOffset = offset, SyncDriftPpm = 100, SyncMethod = "manual" };
        var trim = new BodyTrim(main.Id, 5, 18);
        var range = BodyTrimmer.MasterRange(trim, main, audio);
        Assert.Equal((5 - offset) / 1.0001, range.T0, 6);
        Assert.Equal((18 - offset) / 1.0001, range.T1, 6);
        var clips = PlanBuilder.ApplyDrift(BodyTrimmer.Clips(range, main, audio), [main, audio], audio.Id, 30);
        Assert.Equal(5, clips[0].SrcIn, 6);
        Assert.InRange(Math.Abs(clips[^1].SrcOut - 18), 0, 1 / 30.0);
        Assert.Equal(range.Length, clips.Sum(c => c.Length), 6);
        Assert.All(clips, c => Assert.Equal(main.Id, c.AssetId));
    }

    [Fact]
    public void Missing_audio_coverage_or_unconfirmed_sync_or_changed_main_is_rejected()
    {
        var audio = Asset("audio");
        var main = Asset() with { SyncOffset = 4, SyncMethod = "manual" };
        Assert.Throws<PlanException>(() => BodyTrimmer.MasterRange(new(main.Id, 1, 18), main, audio));
        Assert.Throws<PlanException>(() => BodyTrimmer.MasterRange(new(main.Id, 5, 18), main with { SyncMethod = "none" }, audio));
        Assert.False(BodyTrimmer.Valid(new("old", 5, 18), main));
        Assert.False(BodyTrimmer.Valid(new(main.Id, 18, 5), main));
        Assert.False(BodyTrimmer.Valid(new(main.Id, double.NaN, 18), main));
    }

    [Fact]
    public void Discontinuous_map_splits_source_clips_without_resetting_the_audio_spine()
    {
        var audio = Asset("audio", 30);
        var main = Asset() with { SyncMethod = "audio-map", SyncMap = [new(0, 1), new(10, 1), new(20, 2)] };
        var range = BodyTrimmer.MasterRange(new(main.Id, 6, 19), main, audio);
        var clips = BodyTrimmer.Clips(range, main, audio);
        Assert.Equal(2, clips.Count);
        Assert.Equal(6, clips[0].SrcIn, 6);
        Assert.Equal(19, clips[^1].SrcOut, 6);
        Assert.Equal(clips[0].MasterOut, clips[1].MasterIn);
        Assert.Equal(range.Length, clips.Sum(c => c.Length), 6);
        Assert.Throws<PlanException>(() => BodyTrimmer.MasterRange(new(main.Id, 16.5, 19), main, audio));
    }

    [Fact]
    public async Task Body_plan_keeps_the_whole_interval_not_the_presets_short_highlight_or_internal_silence_cuts()
    {
        var main = Asset(duration: 300);
        var p = Fixtures.Presets["P4"];
        var candidate = new Candidate { Id = "body", Segments = [new(5, 295)] };
        var job = new Job { Id = "j", ProjectId = "p", PresetIds = [p.Id], Options = new() { Bgm = "none", Sfx = false, BodyTrim = new(main.Id, 5, 295) } };
        var (plan, _) = await PlanBuilder.BuildAsync(new(candidate, new() { Provider = "none", Duration = 300 },
            new() { Silences = [new(10, 290)] }, p, [main], main, "p", job, new Dictionary<string, List<FaceSample>>(), null, null),
            TestContext.Current.CancellationToken);
        Assert.Equal(290, plan.Duration);
        Assert.Equal(new Interval(5, 295), plan.BodyRange);
        Assert.Equal(5, plan.Timeline[0].MasterIn);
        Assert.Equal(295, plan.Timeline[^1].MasterOut);
        Assert.All(plan.Timeline, c => Assert.Equal(1, c.Speed));
        Assert.Equal((290d, 290d, double.PositiveInfinity), QaEngine.DurationRange(plan, p));
        Assert.Equal((p.Target.Min, p.Target.Max, 180d), QaEngine.DurationRange(plan with { BodyRange = null }, p));
    }

    [Fact]
    public async Task Late_starting_secondary_follows_the_main_body_without_shortening_it()
    {
        var main = Asset();
        var screen = Asset("screen", 10) with
        {
            Role = AssetRole.Screen, SyncOffset = -5, SyncMethod = "manual", PipScenes = [new(0, 10)],
        };
        var p = Fixtures.Presets["P11"];
        var candidate = new Candidate { Id = "body", Segments = [new(2, 18)] };
        var job = new Job { Id = "j", ProjectId = "p", PresetIds = [p.Id], Options = new() { Bgm = "none", Sfx = false, BodyTrim = new(main.Id, 2, 18) } };
        var (plan, _) = await PlanBuilder.BuildAsync(new(candidate, Speech(), Fixtures.Signals(Speech()), p,
            [main, screen], main, "p", job, new Dictionary<string, List<FaceSample>>(), null, null), TestContext.Current.CancellationToken);
        Assert.Equal(16, plan.Duration, 6);
        Assert.Equal(2, plan.Timeline[0].MasterIn);
        Assert.Equal(18, plan.Timeline[^1].MasterOut, 6);
        var secondary = plan.Timeline.Where(c => c.AssetId == screen.Id).ToList();
        Assert.NotEmpty(secondary);
        Assert.All(secondary, c =>
        {
            Assert.InRange(c.SrcIn, 0, 10);
            Assert.InRange(c.SrcOut, 0, 10);
            Assert.Equal(c.MasterIn - 5, c.SrcIn, 6);
        });
        Assert.Equal(main.Id, plan.Timeline[0].AssetId);
        Assert.Equal(main.Id, plan.Timeline[^1].AssetId);
    }

    [Fact]
    public async Task Real_media_analysis_and_render_preserve_source_bytes_and_audio_and_exact_body_duration()
    {
        var ct = TestContext.Current.CancellationToken;
        var ff = Fixtures.Ff ?? throw new InvalidOperationException("FFmpeg is required");
        var dir = Path.Combine(Path.GetTempPath(), "vc-trim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "source.mp4");
            await ff.RunFfmpegCheckedAsync(["-v", "error", "-y", "-f", "lavfi", "-i",
                "color=c=black:s=640x360:r=30:d=2[head];color=c=red:s=640x360:r=30:d=7[body];color=c=black:s=640x360:r=30:d=3[tail];[head][body][tail]concat=n=3:v=1:a=0",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=7,adelay=2000|2000,apad=whole_dur=12",
                "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-t", "12", source], ct);
            var probe = new MediaProbe(ff);
            var hash = await MediaProbe.Sha256Async(source, ct);
            var main = Asset(duration: 12) with { Path = source, Sha256 = hash, Media = await probe.ProbeAsync(source, ct) };
            using var store = new Store(Path.Combine(dir, "store.db"));
            var runner = new PipelineRunner(new() { Ff = ff, Store = store, Presets = Fixtures.Presets, DataDir = dir });
            var analyzing = runner.AnalyzeBodyAsync(main, ct);
            var previewing = runner.SourcePreviewAsync(main, ct);
            await Task.WhenAll(analyzing, previewing);
            var result = await analyzing;
            Assert.InRange(result.Automatic.T0, 1.5, 1.8);
            Assert.InRange(result.Automatic.T1, 9.4, 9.7);
            var title = Path.Combine(dir, "title.mp4");
            await ff.RunFfmpegCheckedAsync(["-v", "error", "-y", "-i", source,
                "-vf", "drawbox=x=10:y=10:w=8:h=8:color=white:t=fill:enable='lt(t,2)'", "-c:v", "libx264",
                "-preset", "ultrafast", "-c:a", "copy", title], ct);
            var titleAsset = main with { Path = title, Sha256 = await MediaProbe.Sha256Async(title, ct) };
            var titleResult = await runner.AnalyzeBodyAsync(titleAsset, ct);
            Assert.Equal(0, titleResult.Automatic.T0); // A small visible mark on otherwise black silent frames must survive.
            var previewInfo = await probe.ProbeAsync(await runner.SourcePreviewAsync(main, ct), ct);
            Assert.True(previewInfo.AudioChannels > 0);
            var range = new Interval(2, 9);
            var p = Fixtures.Presets["P4"];
            var plan = new EditPlan
            {
                EditplanId = "ep", ProjectId = "p", Preset = p.Ref, Canvas = Canvas.Landscape(),
                Timeline = BodyTrimmer.Clips(range, main, main), BodyRange = range,
                Audio = new() { Master = main.Id, Bgm = null },
            };
            var output = Path.Combine(dir, "body.mp4");
            await new Renderer(ff).RenderAsync(plan, p, new Dictionary<string, SourceAsset> { [main.Id] = main }, output, true, ct);
            var rendered = await probe.ProbeAsync(output, ct);
            Assert.InRange(rendered.Duration, 6.95, 7.1);
            Assert.True(rendered.AudioChannels > 0);
            var sig = await new VideoCreator.Signals.SignalExtractor(ff).ExtractAsync(output, output, 0.5, ct);
            Assert.Empty(sig.Blacks);
            Assert.DoesNotContain(sig.Silences, s => s.Length > 0.5);
            Assert.Equal(hash, await MediaProbe.Sha256Async(source, ct));
            Assert.Equal(main.Media.Duration, (await probe.ProbeAsync(source, ct)).Duration);

            await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("VideoCreator:DataDir", Path.Combine(dir, "app-data"));
                b.UseSetting("VideoCreator:ProjectsDir", Path.Combine(dir, "projects"));
                b.UseSetting("VideoCreator:OpenBrowser", "false");
                b.UseSetting("VideoCreator:Port", "0");
                b.UseSetting("VideoCreator:Asr:Provider", "none");
                b.UseSetting("VideoCreator:Llm:Provider", "none");
            });
            app.UseKestrel();
            using var http = app.CreateClient();
            http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
            var runtime = app.Services.GetRequiredService<Runtime>();
            var project = new Project { Id = "p", Name = "trim", Directory = dir };
            runtime.Store.Put(Kinds.Project, project.Id, null, project);
            runtime.Store.Put(Kinds.Asset, main.Id, project.Id, main);
            runtime.Store.Put(Kinds.Rights, "rights", project.Id, new RightsConfirmation { Id = "rights", ProjectId = project.Id, AssetId = main.Id });
            using var bad = await http.PutAsJsonAsync("/api/projects/p/body-trim", new { mode = "manual", asset_id = main.Id, start = 9, end = 2 }, ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
            using var saved = await http.PutAsJsonAsync("/api/projects/p/body-trim", new { mode = "manual", asset_id = main.Id, start = 2, end = 9 }, ct);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(new BodyTrim(main.Id, 2, 9), runtime.Store.Get<Project>(Kinds.Project, project.Id)!.BodyTrim);
            using var auto = await http.PutAsJsonAsync("/api/projects/p/body-trim", new { mode = "auto", asset_id = main.Id, start = 0, end = 1 }, ct);
            Assert.Equal(HttpStatusCode.OK, auto.StatusCode);
            var applied = runtime.Store.Get<Project>(Kinds.Project, project.Id)!.BodyTrim!;
            Assert.InRange(applied.Start, 1.5, 1.8);
            Assert.InRange(applied.End, 9.4, 9.7);
            using var response = await http.PostAsJsonAsync("/api/projects/p/jobs",
                new { preset_ids = new[] { "P8" }, options = new { bgm = "none", sfx = false } }, ct);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var started = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
            var jobId = started.GetProperty("job_id").GetString()!;
            Job? generated = null;
            for (int i = 0; i < 150; i++)
            {
                generated = runtime.Store.Get<Job>(Kinds.Job, jobId);
                if (generated?.Status is JobStatus.Ready or JobStatus.Failed) break;
                await Task.Delay(200, ct);
            }
            Assert.True(generated?.Status == JobStatus.Ready, generated?.Message);
            Assert.NotNull(generated);
            Assert.Equal(applied, generated.Options.BodyTrim);
            var bodyCandidate = Assert.Single(runtime.Store.List<Candidate>(Kinds.Candidate, jobId));
            var savedPlan = runtime.Store.Get<EditPlan>(Kinds.EditPlan, bodyCandidate.EditPlanId!)!;
            Assert.Equal(applied.End - applied.Start, savedPlan.Duration, 3);
            Assert.Equal(applied.Start, savedPlan.Timeline[0].SrcIn, 3);
            Assert.Equal(applied.End, savedPlan.Timeline[^1].SrcOut, 3);
            using var cleared = await http.PutAsJsonAsync("/api/projects/p/body-trim", new { mode = "off" }, ct);
            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
            Assert.Null(runtime.Store.Get<Project>(Kinds.Project, project.Id)!.BodyTrim);
            Assert.Equal(hash, await MediaProbe.Sha256Async(source, ct));
            await app.DisposeAsync();
            runtime.Store.Dispose();
        }
        finally { Directory.Delete(dir, true); }
    }
}
