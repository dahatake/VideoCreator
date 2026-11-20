// Spike 19: which DirectML adapter does the upscaler run on? Times one 720x1280 -> 4x inference per DirectML device_id (and on CPU),
// so the dGPU-vs-iGPU choice on hybrid laptops is decided by measurement. Usage: dotnet run spikes/19-gpu-adapters.cs
#:property PublishAot=false
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using VideoCreator.Media;

var model = Path.Combine(Directory.GetCurrentDirectory(), "tools", "models", SuperResolution.ModelFile);
const int W = 720, H = 1280;
var rgb = new byte[W * H * 3]; new Random(1).NextBytes(rgb);

void Time(string label, SessionOptions o)
{
    try
    {
        var sw = Stopwatch.StartNew();
        using var s = new InferenceSession(model, o);
        var load = sw.Elapsed.TotalSeconds;
        var up = Probe(s);
        Console.WriteLine($"{label,-14} load {load,5:0.0}s  first {up.first,6:0.00}s  steady {up.steady,6:0.000}s/frame");
    }
    catch (Exception ex) { Console.WriteLine($"{label,-14} unavailable: {ex.GetType().Name} {ex.Message.Split('\n')[0]}"); }
}

(double first, double steady) Probe(InferenceSession s)
{
    var inName = s.InputMetadata.Keys.First(); var outName = s.OutputMetadata.Keys.First();
    var a = new float[3 * H * W]; var b = new float[3 * H * W * 16];
    double first = 0, total = 0; int n = 5;
    for (int i = 0; i < n; i++)
    {
        var sw = Stopwatch.StartNew();
        using var iv = OrtValue.CreateTensorValueFromMemory(a, [1, 3, H, W]);
        using var ov = OrtValue.CreateTensorValueFromMemory(b, [1, 3, H * 4, W * 4]);
        using var ro = new RunOptions();
        s.Run(ro, [inName], [iv], [outName], [ov]);
        if (i == 0) first = sw.Elapsed.TotalSeconds; else total += sw.Elapsed.TotalSeconds;
    }
    return (first, total / (n - 1));
}

foreach (var a in GpuAdapters.List()) Console.WriteLine($"DXGI {a.Index}: {a.Name}  {a.DedicatedMemory / (1 << 20)} MB");
Console.WriteLine($"app picks: {string.Join(" > ", GpuAdapters.Preferred().Select(a => a.Name))}");
using (var sr = new SuperResolution(model)) Console.WriteLine($"SuperResolution device: {sr.Device}");

for (int id = 0; id < 3; id++)
{
    var o = new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
    try { o.AppendExecutionProvider_DML(id); } catch (Exception ex) { Console.WriteLine($"DML device {id}: {ex.Message.Split('\n')[0]}"); continue; }
    Time($"DML device {id}", o);
}

// Face detector (UltraFace 320x240): CPU vs the chosen DirectML adapter
{
    var fm = Path.Combine(Directory.GetCurrentDirectory(), "tools", "models", "ultraface.onnx");
    var t = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(new[] { 1, 3, 240, 320 });
    void Face(string label, SessionOptions o)
    {
        using var s = new InferenceSession(fm, o);
        for (int i = 0; i < 5; i++) s.Run([NamedOnnxValue.CreateFromTensor("input", t)]).Dispose();
        var sw = Stopwatch.StartNew(); for (int i = 0; i < 100; i++) s.Run([NamedOnnxValue.CreateFromTensor("input", t)]).Dispose();
        Console.WriteLine($"face {label,-8} {sw.Elapsed.TotalMilliseconds / 100,7:0.00} ms/frame");
    }
    Face("CPU", new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR });
    var d = new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR, EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
    d.AppendExecutionProvider_DML(GpuAdapters.Preferred()[0].Index);
    Face("DML", d);
}
