// Calibration aid: onset-strength statistics of BGM files (are attacks distinguishable from a sustained pad?).
#:property PublishAot=false
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using VideoCreator.Audio;
using VideoCreator.Media;

var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "run.cmd"))) root = Path.GetDirectoryName(root)!;
var ff = FFmpeg.Locate(start: root);
foreach (var f in args)
{
    using var ms = new MemoryStream();
    await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-ss", "2", "-t", "60", "-i", f, "-ac", "1", "-ar", "16000", "-f", "s16le", "-"], CancellationToken.None, stdout: ms);
    var pcm = new short[(int)ms.Length / 2]; Buffer.BlockCopy(ms.GetBuffer(), 0, pcm, 0, pcm.Length * 2);
    var (rate, c) = BgmAnalyzer.Features(pcm);
    var flux = BgmAnalyzer.Flux(pcm);
    var sorted = flux.OrderBy(x => x).ToArray();
    double P(double q) => sorted[(int)(q * (sorted.Length - 1))];
    var (bpm, _) = TempoEstimator.Estimate(pcm);
    Console.WriteLine($"{Path.GetFileName(f)}: onsets {rate:F2}/s centroid {c:F0} Hz bpm {bpm:F1} | flux p50 {P(0.5):F1} p90 {P(0.9):F1} p99 {P(0.99):F1} max {sorted[^1]:F1}");
}
