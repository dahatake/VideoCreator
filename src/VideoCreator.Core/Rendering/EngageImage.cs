using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using OpenAI.Images;
using System.Text.Json;
using VideoCreator.Selection;
using VideoCreator.Media;
using VideoCreator.Pipeline;

namespace VideoCreator.Rendering;

public interface IImageGenerator
{
    Task<byte[]> GenerateAsync(string prompt, CancellationToken ct);
}

#pragma warning disable OPENAI001
/// <summary>
/// OpenAI-compatible Images API on Microsoft Foundry. The 5:1 banner is requested natively first (FLUX: width ≤ 1440, multiples of 32);
/// models that only accept fixed sizes (GPT Image) reject it, and then the landscape size is used and center-cropped.
/// </summary>
public sealed class FoundryImageGenerator(Uri endpoint, string deployment, AuthenticationPolicy auth, HttpClient? http = null) : IImageGenerator
{
    public static readonly (int Width, int Height)[] Sizes = [(1440, 288), (1536, 1024)];
    static readonly HttpClient Download = new() { Timeout = TimeSpan.FromMinutes(2) };

    readonly ImageClient _client = new(deployment, auth, new OpenAIClientOptions
    {
        Endpoint = endpoint, NetworkTimeout = TimeSpan.FromMinutes(10),
        Transport = http == null ? null : new HttpClientPipelineTransport(http),
    });

    public async Task<byte[]> GenerateAsync(string prompt, CancellationToken ct)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                GeneratedImage image = await _client.GenerateImageAsync(prompt, new ImageGenerationOptions
                {
                    Size = new GeneratedImageSize(Sizes[i].Width, Sizes[i].Height),
                }, ct);
                if (image.ImageBytes?.ToArray() is { Length: > 0 } bytes) return bytes;
                if (image.ImageUri is { Scheme: "https" } uri) return await (http ?? Download).GetByteArrayAsync(uri, ct);
                throw new PipelineException("E-IMAGE-GENERATION", "画像モデルから画像データが返りませんでした。画像生成モデル（GPT Image / FLUX）のデプロイを指定してください");
            }
            catch (ClientResultException ex) when (ex.Status is 400 or 422 && i + 1 < Sizes.Length)
            {
                // size not supported by this model: try the next size
            }
            catch (ClientResultException ex)
            {
                throw new PipelineException("E-IMAGE-GENERATION", $"画像生成に失敗しました（HTTP {ex.Status}）。Foundry の画像モデルのデプロイ名・アクセス権・クォータ・コンテンツ制限を確認してください");
            }
            catch (HttpRequestException ex)
            {
                throw new PipelineException("E-IMAGE-GENERATION", "生成画像をダウンロードできませんでした: " + ex.Message);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new PipelineException("E-IMAGE-GENERATION", "画像生成がタイムアウトしました。接続先とネットワークを確認してください");
            }
        }
    }
}
#pragma warning restore OPENAI001

/// <param name="Prompt">The specified prompt: the Viva Engage post, the fixed scene and the art direction.</param>
/// <param name="ModelPrompt">What was sent to the image model (an English visual rendering of <paramref name="Prompt"/> when an LLM is available).</param>
public sealed record EngageImageProvenance(string Prompt, string Post, string Deployment, int Width, int Height, string ModelPrompt = "");

public static class EngageImage
{
    public const string Scene = "MCと社員が、このトピックについてワクワクしながら話している";
    const int MaxModelPrompt = 3000;

    public static string Prompt(string post) => post + "\n\n" + Scene + """


アニメ調のイラスト。Viva Engage 用の横長バナー（最終サイズ 1000x200 px、縦横比 5:1）。
画像の上下は切り取られることがあるため、MCと社員の顔・表情・会話の様子、およびトピックを表す要素を上下の中央付近に収める。
文字・ロゴ・透かしは描かない。投稿文は題材であり、画像内に書き写さない。
""";

    const string RewriteSystem = """
You write prompts for a text-to-image model that understands English only.
Rewrite the Japanese request into one English paragraph (at most 90 words) that describes only what is visible in the image.
Start with exactly: "2D Japanese anime illustration, cel shading, clean line art, flat vivid colors." — the picture must look like an anime, not 3D or photorealistic.
Keep every requirement: an MC (show host) and company employees talking excitedly about the topic of the post;
a very wide 5:1 panoramic banner with all faces and key elements in the vertical middle.
Show the topic through scenery, simple objects, gestures and expressions. Do not include screens, monitors, holograms, charts, signs,
labels, packaging, documents or anything else that usually carries writing; keep the background simple and softly blurred.
Image models paint words they read, so never use acronyms (such as "AI"), quoted words, brand, product, company or person names, URLs or hashtags;
show technology as a friendly robot or glowing abstract shapes instead.
End with: "No text, no letters, no numbers, no logos, no watermark."
Return JSON {"prompt": "..."}.
""";
    const string RewriteSchema = """{"type":"object","additionalProperties":false,"required":["prompt"],"properties":{"prompt":{"type":"string"}}}""";

    /// <summary>Image models such as FLUX ignore Japanese, so the specified prompt is rendered into English by the configured LLM; without one it is sent as is.</summary>
    public static async Task<string> ModelPromptAsync(IHighlightSelector? llm, string prompt, Action<string>? log, CancellationToken ct)
    {
        if (llm == null) return prompt;
        try
        {
            using var doc = JsonDocument.Parse(await llm.CompleteJsonAsync(RewriteSystem, prompt, "engage_image_prompt", RewriteSchema, ct));
            var text = doc.RootElement.GetProperty("prompt").GetString()?.Trim();
            if (!string.IsNullOrEmpty(text)) return text.Length > MaxModelPrompt ? text[..MaxModelPrompt] : text;
            log?.Invoke("Viva Engage 画像: 英語プロンプトが空のため、指定プロンプトをそのまま使います");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log?.Invoke("Viva Engage 画像: 英語プロンプトを作れなかったため、指定プロンプトをそのまま使います: " + ex.Message);
        }
        return prompt;
    }

    public static async Task<EngageImageProvenance> WriteAsync(FFmpeg ff, IImageGenerator generator, string post, string deployment, string path, CancellationToken ct,
        IHighlightSelector? llm = null, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(post))
            throw new PipelineException("E-IMAGE-GENERATION", "Viva Engage 用の投稿文がないため画像を生成できません");
        var prompt = Prompt(post);
        var modelPrompt = await ModelPromptAsync(llm, prompt, log, ct);
        var bytes = await generator.GenerateAsync(modelPrompt, ct);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".source";
        var output = temp + ".png";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct);
            var result = await ff.RunFfmpegAsync(["-v", "error", "-nostdin", "-y", "-i", temp,
                "-frames:v", "1", "-vf", "scale=1000:200:force_original_aspect_ratio=increase,crop=1000:200,setsar=1",
                "-threads", "1", output], ct);
            if (result.ExitCode != 0 || !File.Exists(output))
                throw new PipelineException("E-IMAGE-GENERATION", "生成画像を 1000x200 px に変換できませんでした: " + FFmpeg.Tail(result.StdErr));
            File.Move(output, path, true);
            return new(prompt, post, deployment, 1000, 200, modelPrompt);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
