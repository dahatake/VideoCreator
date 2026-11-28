using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using VideoCreator.Models;

namespace VideoCreator.Media;

public sealed class SlideDeckException(string message) : Exception(message);

/// <summary>What a screen recording shows over time: which deck slide (when a deck is given) and where a full-screen app hides the taskbar.</summary>
public sealed record SlideAnalysis(List<SlideSpan> Spans, List<Interval> FullScreen);

/// <summary>A scene candidate with the reason it is (not) suggested: slide = shows deck slides, fullscreen = a full-screen app (no taskbar), screen = other.</summary>
public sealed record PipSceneCandidate(double T0, double T1, string Kind, bool Suggested, List<int> Slides, double Coverage);

/// <summary>
/// The original slides of a presentation recorded on screen. A .pptx is rendered page by page by PowerPoint itself (identical layout and fonts),
/// else by LibreOffice (free, Windows/macOS/Linux; fonts and layout may differ slightly); a .zip of exported page images works with neither. Frames of the recording are matched to the pages by normalised
/// cross-correlation of 64×36 luma thumbnails of the shared-content box — slideshow frames correlate ≥ 0.95 with their page and ≤ 0.3 with others.
/// </summary>
public static class SlideDecks
{
    public const int RenderWidth = 3840;
    public static readonly string[] DeckExtensions = [".pptx", ".ppsx", ".ppt", ".pps", ".zip"];
    static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];
    public const string PowerPointMissing =
        "PowerPoint も LibreOffice も見つからないため、スライドを画像にできません。LibreOffice（無料）を入れるか、PowerPoint の［ファイル］→［エクスポート］で PNG（全スライド）を書き出し、そのフォルダーを zip にして追加してください";

    public static bool PowerPointAvailable => OperatingSystem.IsWindows() && Type.GetTypeFromProgID("PowerPoint.Application") != null;

    /// <summary>soffice and the Python with the UNO bridge that drives it (LibreOffice bundles one on Windows and macOS), or null when not installed.
    /// VIDEOCREATOR_SOFFICE points at soffice of a portable or non-standard install.</summary>
    public static (string Soffice, string Python)? LibreOffice()
    {
        var env = Environment.GetEnvironmentVariable("VIDEOCREATOR_SOFFICE");
        IEnumerable<string> candidates = !string.IsNullOrEmpty(env) ? [env]
            : OperatingSystem.IsWindows() ? new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                .Select(f => Path.Combine(Environment.GetFolderPath(f), "LibreOffice", "program", "soffice.exe"))
            : OperatingSystem.IsMacOS() ? ["/Applications/LibreOffice.app/Contents/MacOS/soffice"]
            : ["/usr/bin/soffice", "/usr/lib/libreoffice/program/soffice", "/opt/libreoffice/program/soffice"];
        foreach (var soffice in candidates.Where(File.Exists))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(soffice))!;
            var python = new[] { Path.Combine(dir, "python.exe"), Path.Combine(dir, "..", "Resources", "python"), Path.Combine(dir, "python") }.FirstOrDefault(File.Exists)
                ?? (OperatingSystem.IsLinux() && File.Exists("/usr/bin/python3") ? "/usr/bin/python3" : null);   // Linux: the python3-uno package
            if (python != null) return (Path.GetFullPath(soffice), Path.GetFullPath(python));
        }
        return null;
    }

    /// <summary>Renders <paramref name="source"/> (.pptx etc. or a .zip of page images) into <paramref name="outDir"/> (replaced) as slide-NNN.png;
    /// <paramref name="renderer"/> "libreoffice" skips PowerPoint even when it is installed.</summary>
    public static async Task<SlideDeck> RenderAsync(FFmpeg ff, string source, string outDir, CancellationToken ct, string? renderer = null)
    {
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (!DeckExtensions.Contains(ext)) throw new SlideDeckException("スライドは PowerPoint ファイル（.pptx）か、スライドを書き出した画像の zip を指定してください");
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        string kind;
        if (ext == ".zip") { await FromZipAsync(ff, source, outDir, ct); kind = "images"; }
        else if (PowerPointAvailable && renderer != "libreoffice") { await PowerPointAsync(source, outDir, ct); kind = "powerpoint"; }
        else if (LibreOffice() is { } lo) { await LibreOfficeAsync(lo, source, outDir, ct); kind = "libreoffice"; }
        else throw new SlideDeckException(PowerPointMissing);
        // slide-NNN.png, or slide-NNN-sKK.png per click build
        var slides = Directory.GetFiles(outDir, "slide-*.png").Order(StringComparer.Ordinal)
            .Select(p => (p, name: Path.GetFileNameWithoutExtension(p), size: PngSize(p)))
            .Select(x => new SlideImage(int.Parse(x.name[6..9], CultureInfo.InvariantCulture), x.p, x.size.W, x.size.H,
                x.name.Length > 11 ? int.Parse(x.name[11..], CultureInfo.InvariantCulture) : 0)).ToList();
        if (slides.Count == 0) throw new SlideDeckException("スライドの画像を作れませんでした（スライドがない、または zip に PNG/JPEG がありません）");
        return new SlideDeck { Path = source, Sha256 = await MediaProbe.Sha256Async(source, ct), Source = kind, Slides = slides };
    }

    /// <summary>Click builds rendered per slide at most (a longer build keeps its first steps and the final state).</summary>
    public const int MaxSteps = 30;

    // PowerPoint's own renderer through COM, in a separate Windows PowerShell so a hung Office never blocks the app; quits only an instance it started.
    // Slide.Export draws every shape (the editing view). For a slide with click builds, the state after each click is rendered instead: an effect
    // shows or hides its shape (or text paragraph) through a "set visibility" behaviour (type 8, property 8; To 1/0, emphasis has none), clicks are
    // the OnPageClick (1) triggers; With/AfterPrevious effects join the current step. The presentation is opened read-only and never saved.
    const string Script = """
        param([string]$In, [string]$Out, [int]$Width, [int]$MaxSteps)
        $ErrorActionPreference = 'Stop'
        $running = @(Get-Process POWERPNT -ErrorAction SilentlyContinue).Count -gt 0
        $app = New-Object -ComObject PowerPoint.Application
        try {
          $p = $app.Presentations.Open($In, -1, 0, 0)
          try {
            $h = [int]([Math]::Round($Width * $p.PageSetup.SlideHeight / $p.PageSetup.SlideWidth / 2) * 2)
            foreach ($s in $p.Slides) {
              $name = Join-Path $Out ('slide-{0:D3}' -f $s.SlideIndex)
              $fx = @(); $clicks = 0
              $seq = $s.TimeLine.MainSequence
              for ($i = 1; $i -le $seq.Count; $i++) {
                $e = $seq.Item($i)
                if ($e.Timing.TriggerType -eq 1) { $clicks++ }
                $vis = $null
                for ($j = 1; $j -le $e.Behaviors.Count; $j++) {
                  $b = $e.Behaviors.Item($j)
                  if ($b.Type -eq 8 -and $b.SetEffect.Property -eq 8) { $vis = ([string]$b.SetEffect.To) -ne '0' }
                }
                if ($vis -ne $null) {
                  $para = 0; try { $para = [int]$e.Paragraph } catch { }
                  $fx += New-Object PSObject -Property @{ Step = $clicks; Shape = $e.Shape; Para = $para; Key = ('{0}/{1}' -f $e.Shape.Id, $para); Vis = $vis }
                }
              }
              if ($fx.Count -eq 0) { $s.Export("$name.png", 'PNG', $Width, $h); continue }
              $first = @{}; $alpha = @{}
              foreach ($f in $fx) {
                if (-not $first.ContainsKey($f.Key)) {
                  $first[$f.Key] = $f
                  if ($f.Para -gt 0) { $t = $f.Shape.TextFrame2.TextRange.Paragraphs($f.Para).Font.Fill.Transparency; $alpha[$f.Key] = [single]$(if ($t -ge 0 -and $t -lt 1) { $t } else { 0 }) }
                }
              }
              if ($clicks -eq 0) { $states = @(0) } elseif ($clicks -le $MaxSteps) { $states = 0..$clicks } else { $states = @(0..($MaxSteps - 2)) + $clicks }
              foreach ($k in $states) {
                $state = @{}
                foreach ($key in $first.Keys) { $state[$key] = -not $first[$key].Vis }
                foreach ($f in $fx) { if ($f.Step -le $k) { $state[$f.Key] = $f.Vis } }
                foreach ($key in $first.Keys) {
                  $f = $first[$key]
                  if ($f.Para -gt 0) { $f.Shape.TextFrame2.TextRange.Paragraphs($f.Para).Font.Fill.Transparency = [single]$(if ($state[$key]) { $alpha[$key] } else { 1 }) }
                  else { $f.Shape.Visible = $(if ($state[$key]) { -1 } else { 0 }) }
                }
                if ($clicks -eq 0) { $file = "$name.png" } else { $file = '{0}-s{1:D2}.png' -f $name, ($k + 1) }
                $s.Export($file, 'PNG', $Width, $h)
              }
            }
          } finally { $p.Close() }
        } finally {
          if (-not $running) { $app.Quit() }
          [void][Runtime.InteropServices.Marshal]::ReleaseComObject($app)
        }
        """;

    static async Task PowerPointAsync(string source, string outDir, CancellationToken ct)
    {
        if (!PowerPointAvailable) throw new SlideDeckException(PowerPointMissing);
        var script = Path.Combine(Path.GetTempPath(), $"vc-slides-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(script, Script, System.Text.Encoding.ASCII, ct);
        try
        {
            await RunConverterAsync(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-In", Path.GetFullPath(source), "-Out", Path.GetFullPath(outDir),
                 "-Width", RenderWidth.ToString(CultureInfo.InvariantCulture), "-MaxSteps", MaxSteps.ToString(CultureInfo.InvariantCulture)],
                "PowerPoint", "（パスワード付き・破損・別のダイアログ表示中など）", ct);
        }
        finally { File.Delete(script); }
    }

    // LibreOffice through UNO, with a private profile (never touches the user's) on a copy of the deck (no lock file beside the original).
    // Click builds as in the PowerPoint script: the main sequence's groups that start on a click (Event trigger ON_NEXT = 9) count steps, and the
    // effects' "set Visibility" nodes show or hide a shape or one paragraph (ParagraphTarget). A hidden paragraph keeps its line and numbering:
    // transparent text and bullet colour (transparency in the high byte), so later list numbers stay as in the slideshow.
    const string UnoScript = """
        import sys, os, time, subprocess, uno
        from com.sun.star.beans import PropertyValue
        soffice, src, out, width, max_steps = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), int(sys.argv[5])
        pipe = 'vcslides%d' % os.getpid()
        profile = uno.systemPathToFileUrl(os.path.join(out, '_profile'))
        proc = subprocess.Popen([soffice, '-env:UserInstallation=' + profile, '--headless', '--invisible', '--nologo', '--norestore', '--nodefault', '--nolockcheck',
                                 '--accept=pipe,name=%s;urp;' % pipe], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

        def prop(n, v):
            p = PropertyValue(); p.Name = n; p.Value = v; return p

        def children(node):
            if not hasattr(node, 'createEnumeration'): return []
            e = node.createEnumeration(); r = []
            while e.hasMoreElements(): r.append(e.nextElement())
            return r

        def node_type(node):
            for u in (node.UserData or ()):
                if u.Name == 'node-type': return u.Value
            return None

        def sets(node, acc):
            if getattr(node, 'AttributeName', None) == 'Visibility' and node.supportsService('com.sun.star.animations.AnimateSet'):
                t = node.Target
                if hasattr(t, 'Paragraph') and hasattr(t, 'Shape'): acc.append((t.Shape, int(t.Paragraph), bool(node.To)))
                elif t is not None: acc.append((t, -1, bool(node.To)))
            for c in children(node): sets(c, acc)
            return acc

        def paragraphs(shape):
            e = shape.Text.createEnumeration(); r = []
            while e.hasMoreElements(): r.append(e.nextElement())
            return r

        def show_paragraph(shape, p, rules, visible):
            shape.Text.createTextCursorByRange(p).CharTransparence = 0 if visible else 100
            if rules is None or rules.Count == 0: return
            if visible:
                p.NumberingRules = rules; return
            hidden = p.NumberingRules; lvl = p.NumberingLevel if isinstance(p.NumberingLevel, int) and p.NumberingLevel > 0 else 0
            props = list(hidden.getByIndex(lvl))
            for x in props:
                if x.Name == 'BulletColor': x.Value = -16777216
            hidden.replaceByIndex(lvl, uno.Any('[]com.sun.star.beans.PropertyValue', tuple(props)))
            p.NumberingRules = hidden

        def key(shape, para): return (shape.Name + '#' + str(shape.ZOrder), para)

        ctx = None
        try:
            local = uno.getComponentContext()
            resolver = local.ServiceManager.createInstanceWithContext('com.sun.star.bridge.UnoUrlResolver', local)
            for _ in range(240):
                try:
                    ctx = resolver.resolve('uno:pipe,name=%s;urp;StarOffice.ComponentContext' % pipe); break
                except Exception:
                    if proc.poll() is not None: break
                    time.sleep(0.5)
            if ctx is None: raise SystemExit('LibreOffice did not start')
            smgr = ctx.ServiceManager
            desktop = smgr.createInstanceWithContext('com.sun.star.frame.Desktop', ctx)
            doc = desktop.loadComponentFromURL(uno.systemPathToFileUrl(src), '_blank', 0, (prop('Hidden', True),))
            if doc is None: raise SystemExit('LibreOffice could not open the presentation')
            try:
                pages = doc.DrawPages
                height = int(round(width * pages.getByIndex(0).Height / pages.getByIndex(0).Width / 2) * 2)
                fdata = uno.Any('[]com.sun.star.beans.PropertyValue', (prop('PixelWidth', width), prop('PixelHeight', height)))
                exporter = smgr.createInstanceWithContext('com.sun.star.drawing.GraphicExportFilter', ctx)
                def export(page, file):
                    exporter.setSourceDocument(page)
                    exporter.filter((prop('URL', uno.systemPathToFileUrl(file)), prop('MediaType', 'image/png'), prop('FilterData', fdata)))
                for i in range(pages.Count):
                    page = pages.getByIndex(i)
                    name = os.path.join(out, 'slide-%03d' % (i + 1))
                    fx = []; clicks = 0
                    main = [c for c in children(page.AnimationNode) if node_type(c) == 4]
                    for group in (children(main[0]) if main else []):
                        if getattr(group.Begin, 'Trigger', None) == 9: clicks += 1
                        for shape, para, vis in sets(group, []): fx.append((clicks, shape, para, vis))
                    if not fx:
                        export(page, name + '.png'); continue
                    first = {}; targets = {}; rules = {}
                    for step, shape, para, vis in fx:
                        k = key(shape, para)
                        targets.setdefault(k, (shape, para)); first.setdefault(k, vis)
                    for k, (shape, para) in targets.items():
                        ps = paragraphs(shape) if para >= 0 else []
                        if 0 <= para < len(ps): rules[k] = ps[para].NumberingRules
                    if clicks == 0: states = [0]
                    elif clicks <= max_steps: states = list(range(clicks + 1))
                    else: states = list(range(max_steps - 1)) + [clicks]
                    for kk in states:
                        state = {k: not v for k, v in first.items()}
                        for step, shape, para, vis in fx:
                            if step <= kk: state[key(shape, para)] = vis
                        for k, (shape, para) in targets.items():
                            if para < 0: shape.Visible = state[k]
                            else:
                                ps = paragraphs(shape)
                                if para < len(ps): show_paragraph(shape, ps[para], rules.get(k), state[k])
                        export(page, name + '.png' if clicks == 0 else '%s-s%02d.png' % (name, kk + 1))
            finally:
                doc.close(True)
        finally:
            try:
                if ctx is not None: desktop.terminate()
            except Exception: pass
            try: proc.wait(30)
            except Exception: proc.kill()
        """;

    static async Task LibreOfficeAsync((string Soffice, string Python) lo, string source, string outDir, CancellationToken ct)
    {
        var copy = Path.Combine(outDir, "_deck" + Path.GetExtension(source).ToLowerInvariant());
        var script = Path.Combine(outDir, "_slides.py");
        File.Copy(source, copy, true);
        await File.WriteAllTextAsync(script, UnoScript, System.Text.Encoding.ASCII, ct);
        try
        {
            await RunConverterAsync(lo.Python, [script, lo.Soffice, Path.GetFullPath(copy), Path.GetFullPath(outDir),
                RenderWidth.ToString(CultureInfo.InvariantCulture), MaxSteps.ToString(CultureInfo.InvariantCulture)], "LibreOffice", "（パスワード付き・破損など）", ct);
        }
        finally
        {
            File.Delete(copy); File.Delete(script);
            try { Directory.Delete(Path.Combine(outDir, "_profile"), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Runs a slide converter in its own process (a hung Office never blocks the app), killed after 10 minutes.</summary>
    static async Task RunConverterAsync(string exe, string[] args, string app, string causes, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi) ?? throw new SlideDeckException($"{app} を起動できませんでした");
        var err = proc.StandardError.ReadToEndAsync(ct);
        _ = proc.StandardOutput.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try { await proc.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch (InvalidOperationException) { }
            ct.ThrowIfCancellationRequested();
            throw new SlideDeckException($"{app} の画像書き出しが 10 分で終わりませんでした。PowerPoint で PNG を書き出し、zip にして追加してください");
        }
        if (proc.ExitCode != 0)
            throw new SlideDeckException($"{app} でスライドを画像にできませんでした{causes}。PowerPoint で PNG を書き出し、zip にして追加してください: " + FFmpeg.Tail(await err));
    }

    /// <summary>Limits on what a zip of slide images may inflate to, counted on the bytes actually read (the sizes a zip declares are not trusted).</summary>
    public const long MaxZipImageBytes = 64L << 20, MaxZipTotalBytes = 1L << 30;

    static async Task FromZipAsync(FFmpeg ff, string zip, string outDir, CancellationToken ct)
    {
        var tmp = Path.Combine(outDir, "_zip");
        Directory.CreateDirectory(tmp);
        try
        {
            using var z = ZipFile.OpenRead(zip);
            // natural order of the names: PowerPoint exports "Slide1.PNG … Slide10.PNG"
            var entries = z.Entries.Where(e => e.Length > 0 && ImageExtensions.Contains(Path.GetExtension(e.Name).ToLowerInvariant()) && !e.FullName.Contains("__MACOSX"))
                .OrderBy(e => NaturalKey(e.FullName), StringComparer.Ordinal).ToList();
            if (entries.Count > 500) throw new SlideDeckException("zip の画像が多すぎます（500 枚まで）");
            long total = 0;
            var buffer = new byte[1 << 16];
            for (int i = 0; i < entries.Count; i++)
            {
                var src = Path.Combine(tmp, $"{i + 1:D3}{Path.GetExtension(entries[i].Name).ToLowerInvariant()}");   // fixed local names: entry paths never reach the file system
                await using (var input = entries[i].Open())
                await using (var output = File.Create(src))
                {
                    long size = 0;
                    for (int n; (n = await input.ReadAsync(buffer, ct)) > 0;)
                    {
                        size += n; total += n;
                        if (size > MaxZipImageBytes) throw new SlideDeckException($"zip の画像 {entries[i].FullName} が大きすぎます（1 枚 {MaxZipImageBytes >> 20} MB まで）");
                        if (total > MaxZipTotalBytes) throw new SlideDeckException($"zip の画像が合計で大きすぎます（{MaxZipTotalBytes >> 30} GB まで）");
                        await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    }
                }
                var r = await ff.RunFfmpegAsync(["-v", "error", "-nostdin", "-y", "-i", src, "-frames:v", "1", "-update", "1", Path.Combine(outDir, $"slide-{i + 1:D3}.png")], ct);
                File.Delete(src);
                if (r.ExitCode != 0) throw new SlideDeckException($"zip の画像 {entries[i].FullName} を読めません: " + FFmpeg.Tail(r.StdErr));
            }
        }
        catch (InvalidDataException ex) { throw new SlideDeckException("zip を読めません（壊れているか、zip ではありません）: " + ex.Message); }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    static string NaturalKey(string s) => System.Text.RegularExpressions.Regex.Replace(s.ToLowerInvariant(), @"\d+", m => m.Value.PadLeft(8, '0'));

    static (int W, int H) PngSize(string png)
    {
        Span<byte> h = stackalloc byte[24];
        using var fs = File.OpenRead(png);
        fs.ReadExactly(h);
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(h[16..20]), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(h[20..24]));
    }

    // ---- matching ----

    const int GW = 640, GH = 360, SW = 64, SH = 36;
    const double Fps = 2;
    /// <summary>Measured on a meeting recording of a 25-page deck: slideshow frames 0.95–0.99 (next best ≤ 0.29), transitions 0.73–0.78, editor/desktop ≤ 0.30.</summary>
    public const double MatchScore = 0.85, MatchMargin = 0.2;
    /// <summary>Slides this similar to each other (animation steps, repeated title pages) do not count as a rival match.</summary>
    const double DuplicateScore = 0.95;
    /// <summary>A transition between two matched slides shorter than this is split between them.</summary>
    const double BridgeGap = 1.5;

    /// <summary>The 64×36 thumbnail of a 640×360 gray frame's rectangle, zero-mean and unit-length (null when flat: nothing to correlate).</summary>
    public static float[]? Signature(byte[] gray, int x0, int y0, int x1, int y1)
    {
        var v = new float[SW * SH];
        double w = (x1 - x0) / (double)SW, h = (y1 - y0) / (double)SH;
        for (int cy = 0; cy < SH; cy++)
        {
            int ya = y0 + (int)(cy * h), yb = Math.Max(ya + 1, y0 + (int)((cy + 1) * h));
            for (int cx = 0; cx < SW; cx++)
            {
                int xa = x0 + (int)(cx * w), xb = Math.Max(xa + 1, x0 + (int)((cx + 1) * w));
                long sum = 0;
                for (int y = ya; y < yb; y++) for (int x = xa; x < xb; x++) sum += gray[y * GW + x];
                v[cy * SW + cx] = sum / (float)((yb - ya) * (xb - xa));
            }
        }
        double mean = v.Average(), norm = 0;
        foreach (var x in v) norm += (x - mean) * (x - mean);
        if (norm < 1e-3 * v.Length) return null;
        float inv = (float)(1 / Math.Sqrt(norm));
        for (int i = 0; i < v.Length; i++) v[i] = (float)((v[i] - mean) * inv);
        return v;
    }

    static double Dot(float[] a, float[] b) { double s = 0; for (int i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }

    public static async Task<float[]?> SlideSignatureAsync(FFmpeg ff, string image, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var r = await ff.RunFfmpegAsync(["-v", "error", "-nostdin", "-i", image, "-frames:v", "1", "-vf", $"scale={GW}:{GH}:flags=area,format=gray", "-f", "rawvideo", "-pix_fmt", "gray", "-"], ct, stdout: ms);
        if (r.ExitCode != 0 || ms.Length < GW * GH) throw new SlideDeckException($"スライド画像 {Path.GetFileName(image)} を読めません: " + FFmpeg.Tail(r.StdErr));
        return Signature(ms.ToArray(), 0, 0, GW, GH);
    }

    /// <summary>One pass over the recording (2 fps, streamed): slide spans (with a deck) and full-screen intervals. Cached per recording and deck.</summary>
    public static async Task<SlideAnalysis> AnalyzeAsync(FFmpeg ff, SourceAsset screen, string cacheDir, CancellationToken ct)
    {
        var cache = Path.Combine(cacheDir, $"pip-slides.v2.{(screen.Deck?.Sha256 is { } s ? s[..16] : "none")}.json");
        if (File.Exists(cache)) return Json.Deserialize<SlideAnalysis>(await File.ReadAllTextAsync(cache, ct));
        var sigs = new List<(int Number, int Step, float[] Sig)>();
        foreach (var slide in screen.Deck?.Slides ?? [])
            if (await SlideSignatureAsync(ff, slide.Path, ct) is { } sig) sigs.Add((slide.Number, slide.Step, sig));
        var sink = new MatchSink(sigs);
        var r = await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-nostdin", "-i", screen.Path, "-an",
            "-vf", $"fps={Fps.ToString(CultureInfo.InvariantCulture)},scale={GW}:{GH}:flags=area,format=gray", "-f", "rawvideo", "-pix_fmt", "gray", "-"], ct, stdout: sink);
        if (r.ExitCode != 0) throw new FFmpegException("スライドとの照合に失敗しました: " + FFmpeg.Tail(r.StdErr));
        var res = new SlideAnalysis(Spans(sink.Frames, screen.Media.Duration), FullScreen(sink.Frames, screen.Media.Duration));
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(cache, Json.Serialize(res), ct);
        return res;
    }

    /// <summary>Per sampled frame: the best slide (0 = none), its correlation, and the bottom strip of the content (a desktop's taskbar), or null when dark.</summary>
    public sealed record FrameMatch(double T, int Slide, double Score, byte[]? Strip, int Step = 0);

    public static List<SlideSpan> Spans(IReadOnlyList<FrameMatch> frames, double duration)
    {
        double half = 0.5 / Fps;
        var runs = new List<SlideSpan>();
        foreach (var f in frames.Where(f => f.Slide > 0))
        {
            if (runs.Count > 0 && runs[^1].Number == f.Slide && runs[^1].Step == f.Step && f.T - half - runs[^1].T1 < 1e-6) runs[^1] = runs[^1] with { T1 = f.T + half, Score = Math.Min(runs[^1].Score, f.Score) };
            else runs.Add(new SlideSpan(Math.Max(0, f.T - half), f.T + half, f.Slide, f.Score, f.Step));
        }
        var spans = new List<SlideSpan>();
        foreach (var s in runs)
        {
            if (spans.Count > 0 && s.T0 - spans[^1].T1 <= BridgeGap)
            {
                if (spans[^1].Number == s.Number && spans[^1].Step == s.Step) { spans[^1] = spans[^1] with { T1 = s.T1, Score = Math.Min(spans[^1].Score, s.Score) }; continue; }
                double mid = (spans[^1].T1 + s.T0) / 2;
                spans[^1] = spans[^1] with { T1 = mid };
                spans.Add(s with { T0 = mid });
                continue;
            }
            spans.Add(s);
        }
        return spans.Select(s => new SlideSpan(Math.Round(s.T0, 3), Math.Round(Math.Min(duration, s.T1), 3), s.Number, Math.Round(s.Score, 3), s.Step)).Where(s => s.T1 - s.T0 >= 0.5).ToList();
    }

    /// <summary>
    /// Frames whose content lacks the bottom strip most frames share (the desktop's taskbar): a full-screen app such as a slideshow. Nothing when the
    /// recording has no persistent strip (an auto-hidden taskbar, a recording of one window).
    /// </summary>
    public static List<Interval> FullScreen(IReadOnlyList<FrameMatch> frames, double duration)
    {
        var lit = frames.Where(f => f.Strip != null).ToList();
        if (lit.Count < 4) return [];
        int n = lit[0].Strip!.Length;
        var reference = new byte[n];
        for (int i = 0; i < n; i++) reference[i] = lit.Select(f => f.Strip![i]).Order().ElementAt(lit.Count / 2);
        double Diff(byte[] s) { double d = 0; for (int i = 0; i < n; i++) d += Math.Abs(s[i] - reference[i]); return d / n; }
        if (lit.Count(f => Diff(f.Strip!) <= 12) < 0.3 * lit.Count) return [];
        double half = 0.5 / Fps;
        var res = new List<Interval>();
        foreach (var f in lit.Where(f => Diff(f.Strip!) > 40))
        {
            if (res.Count > 0 && f.T - half - res[^1].T1 <= 1) res[^1] = res[^1] with { T1 = f.T + half };
            else res.Add(new Interval(Math.Max(0, f.T - half), f.T + half));
        }
        return res.Where(r => r.T1 - r.T0 >= 1).Select(r => new Interval(Math.Round(r.T0, 3), Math.Round(Math.Min(duration, r.T1), 3))).ToList();
    }

    /// <summary>Scenes (asset time) with suggestions: ≥ 60 % slides (trimmed to the slides) with a deck, else ≥ 60 % full-screen.</summary>
    public static List<PipSceneCandidate> Suggest(IReadOnlyList<Interval> scenes, SlideAnalysis a, bool hasDeck)
    {
        var res = new List<PipSceneCandidate>();
        foreach (var s in scenes)
        {
            double len = s.T1 - s.T0;
            var over = a.Spans.Where(p => p.T1 > s.T0 && p.T0 < s.T1).ToList();
            double slide = over.Sum(p => Math.Min(s.T1, p.T1) - Math.Max(s.T0, p.T0)) / len;
            // a span edge is uncertain by half a sample (0.25 s): list only slides seen for at least one full sample interval
            var shown = over.Where(p => Math.Min(s.T1, p.T1) - Math.Max(s.T0, p.T0) >= 0.5 - 1e-9).Select(p => p.Number).Distinct().ToList();
            if (hasDeck && slide >= 0.6)
            {
                double t0 = Math.Max(s.T0, over.Min(p => p.T0)), t1 = Math.Min(s.T1, over.Max(p => p.T1));
                res.Add(new PipSceneCandidate(Math.Round(t0, 3), Math.Round(t1, 3), "slide", true, shown, Math.Round(slide, 2)));
                continue;
            }
            double full = a.FullScreen.Where(p => p.T1 > s.T0 && p.T0 < s.T1).Sum(p => Math.Min(s.T1, p.T1) - Math.Max(s.T0, p.T0)) / len;
            bool fs = !hasDeck && full >= 0.6;
            res.Add(new PipSceneCandidate(s.T0, s.T1, fs ? "fullscreen" : "screen", fs, shown, Math.Round(hasDeck ? slide : full, 2)));
        }
        return res;
    }

    /// <summary>The slide shown at asset time <paramref name="t"/>, if any.</summary>
    public static SlideImage? At(SourceAsset screen, double t) =>
        screen.Deck == null ? null : screen.SlideSpans.FirstOrDefault(s => t >= s.T0 && t < s.T1) is { } span ? screen.Deck.Slides.FirstOrDefault(x => x.Number == span.Number && x.Step == span.Step) : null;

    sealed class MatchSink(List<(int Number, int Step, float[] Sig)> slides) : Stream
    {
        public readonly List<FrameMatch> Frames = [];
        readonly byte[] _cur = new byte[GW * GH];
        int _fill, _n;
        // which images are not rivals: build steps of one slide, near-duplicate pages
        readonly bool[,] _dup = Dups(slides);

        static bool[,] Dups(List<(int Number, int Step, float[] Sig)> s)
        {
            var d = new bool[s.Count, s.Count];
            for (int i = 0; i < s.Count; i++) for (int j = 0; j < s.Count; j++) d[i, j] = s[i].Number == s[j].Number || Dot(s[i].Sig, s[j].Sig) >= DuplicateScore;
            return d;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int k = Math.Min(count, _cur.Length - _fill);
                Buffer.BlockCopy(buffer, offset, _cur, _fill, k);
                _fill += k; offset += k; count -= k;
                if (_fill == _cur.Length) { Frame(); _fill = 0; }
            }
        }

        void Frame()
        {
            double t = Math.Round(_n++ / Fps, 3);
            int x0 = 0, y0 = 0, x1 = GW, y1 = GH;
            if (ScreenScenes.ContentBox(_cur) is { } b) (x0, y0, x1, y1) = ((int)(b.X0 * GW), (int)(b.Y0 * GH), (int)(b.X1 * GW), (int)(b.Y1 * GH));
            var sig = Signature(_cur, x0, y0, x1, y1);
            int best = -1; double bs = -1;
            if (sig != null)
                for (int i = 0; i < slides.Count; i++) { double c = Dot(sig, slides[i].Sig); if (c > bs) (best, bs) = (i, c); }
            int number = 0, step = 0;
            if (best >= 0 && bs >= MatchScore)
            {
                double rival = -1;
                for (int i = 0; i < slides.Count; i++) if (!_dup[best, i]) rival = Math.Max(rival, Dot(sig!, slides[i].Sig));
                if (bs - rival >= MatchMargin) (number, step) = (slides[best].Number, slides[best].Step);
            }
            Frames.Add(new FrameMatch(t, number, Math.Round(Math.Max(0, bs), 3), Strip(x0, y0, x1, y1), step));
        }

        /// <summary>The bottom 5 % of the content as 64×2 mean luma, or null for a dark frame.</summary>
        byte[]? Strip(int x0, int y0, int x1, int y1)
        {
            long all = 0;
            for (int i = 0; i < _cur.Length; i += 7) all += _cur[i];
            if (all / (double)((_cur.Length + 6) / 7) <= ScreenScenes.DarkLuma) return null;
            int ys = y1 - Math.Max(2, (y1 - y0) / 20);
            var s = new byte[SW * 2];
            for (int r = 0; r < 2; r++)
            {
                int ya = ys + r * (y1 - ys) / 2, yb = Math.Max(ya + 1, ys + (r + 1) * (y1 - ys) / 2);
                for (int c = 0; c < SW; c++)
                {
                    int xa = x0 + c * (x1 - x0) / SW, xb = Math.Max(xa + 1, x0 + (c + 1) * (x1 - x0) / SW);
                    long sum = 0;
                    for (int y = ya; y < yb; y++) for (int x = xa; x < xb; x++) sum += _cur[y * GW + x];
                    s[r * SW + c] = (byte)(sum / ((yb - ya) * (xb - xa)));
                }
            }
            return s;
        }

        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }
}
