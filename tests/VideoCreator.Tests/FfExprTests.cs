using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Models;

namespace VideoCreator.Tests;

/// <summary>FFmpeg rejects expressions deeper than 100 (libavutil/eval.c MAX_DEPTH): a long pan track made a preview fail with
/// "Failed to configure input pad on Parsed_crop_…" followed only by follow-on encoder errors. FFmpeg itself evaluates the expressions here.</summary>
public class FfExprTests
{
    static FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");

    /// <summary>The expression sampled by FFmpeg (aevalsrc, one sample per 1/<paramref name="rate"/> s).</summary>
    static async Task<double[]> Evaluate(string expr, int rate, double dur)
    {
        var graph = Path.Combine(Path.GetTempPath(), $"vc-expr-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(graph, $"aevalsrc=exprs='{expr}':s={rate}:d={dur.ToString(System.Globalization.CultureInfo.InvariantCulture)}[a]", TestContext.Current.CancellationToken);
        try
        {
            using var ms = new MemoryStream();
            var r = await Ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-/filter_complex", graph, "-map", "[a]", "-c:a", "pcm_f64le", "-f", "f64le", "-"], TestContext.Current.CancellationToken, stdout: ms);
            Assert.True(r.ExitCode == 0, FFmpeg.Tail(r.StdErr));
            var b = ms.ToArray(); var v = new double[b.Length / 8];
            Buffer.BlockCopy(b, 0, v, 0, b.Length);
            return v;
        }
        finally { File.Delete(graph); }
    }

    [Fact] // the failing case: ~100 ramps in one clip's track (a 51 s screen clip); FFmpeg must accept the crop and agree with XAt (used by QA S-05) everywhere
    public async Task LongTrackCropExpressionIsAcceptedAndMatchesXAt()
    {
        var keys = new List<TrackPoint>();
        for (int i = 0; i < 600; i++) keys.Add(new TrackPoint(Math.Round(0.5 + i * 0.25, 3), 400 + (i * 37 % 160)));
        keys.Insert(200, keys[200] with { X = 10 });   // a jump: two keys at the same time
        var x = FaceTrack.XExpression(keys);

        var crop = await Ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=s=1280x720:r=30:d=1", "-vf", $"crop=640:360:x='{x}':y=0", "-f", "null", "-"], TestContext.Current.CancellationToken);
        Assert.True(crop.ExitCode == 0, FFmpeg.Tail(crop.StdErr));

        const int rate = 40; double dur = keys[^1].T + 1;
        var v = await Evaluate(x, rate, dur);
        Assert.Equal((int)Math.Round(dur * rate), v.Length);
        for (int n = 0; n < v.Length; n++)
            Assert.True(Math.Abs(v[n] - FaceTrack.XAt(keys, n / (double)rate)) < 0.05, $"t={n / (double)rate}: ffmpeg {v[n]} vs XAt {FaceTrack.XAt(keys, n / (double)rate)}");
    }

    [Fact] // bleep gates and the H-05 frame select are sums of one term per item
    public async Task LongSumIsAccepted()
    {
        var v = await Evaluate(FfExpr.Sum(Enumerable.Range(0, 1000).Select(i => FormattableString.Invariant($"between(t,{i / 100.0:0.00},{i / 100.0 + 0.5:0.00})")).ToList()), 10, 1);
        Assert.Equal(51, v[5], 6);   // t = 0.5: the 51 spans that start at 0.00 … 0.50
    }

    [Fact] // the error shown to the user names the filter that failed, not only the encoders that then had nothing to encode
    public void TailKeepsTheFirstErrorLine()
    {
        var err = string.Join('\n',
            "[Parsed_crop_548 @ 000000803020a080] Failed to configure input pad on Parsed_crop_548",
            "[fc#0 @ 0000008000522240] Error reinitializing filters!",
            "[fc#0 @ 0000008000522240] Task finished with error: Invalid argument",
            "[fc#0 @ 0000008000522240] Terminating thread with error: Invalid argument",
            "[vost#0:0/libx264 @ 0000008001c0d540] [enc:libx264 @ 000000800046f880] Could not open encoder before EOF",
            "[vost#0:0/libx264 @ 0000008001c0d540] Task finished with error: Invalid argument",
            "[aost#0:1/aac @ 0000008001c0c940] [enc:aac @ 000000800046fb40] Could not open encoder before EOF",
            "[aost#0:1/aac @ 0000008001c0c940] Task finished with error: Invalid argument",
            "[out#0/mp4 @ 0000008002398ac0] Nothing was written into output file, because at least one of its streams received no packets.");
        var tail = FFmpeg.Tail(err);
        Assert.StartsWith("[Parsed_crop_548", tail);
        Assert.EndsWith("received no packets.", tail);
        Assert.Equal("a\nb", FFmpeg.Tail("a\nb"));
    }
}
