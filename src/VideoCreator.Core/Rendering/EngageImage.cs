using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using OpenAI.Images;
using VideoCreator.Media;
using VideoCreator.Pipeline;

namespace VideoCreator.Rendering;

public interface IImageGenerator
{
    Task<byte[]> GenerateAsync(string prompt, CancellationToken ct);
}

#pragma warning disable OPENAI001
public sealed class FoundryImageGenerator(Uri endpoint, string deployment, AuthenticationPolicy auth, HttpClient? http = null) : IImageGenerator
{
    readonly ImageClient _client = new(deployment, auth, new OpenAIClientOptions
    {
        Endpoint = endpoint, NetworkTimeout = TimeSpan.FromMinutes(10),
        Transport = http == null ? null : new HttpClientPipelineTransport(http),
    });

    public async Task<byte[]> GenerateAsync(string prompt, CancellationToken ct)
    {
        try
        {
            GeneratedImage image = await _client.GenerateImageAsync(prompt, new ImageGenerationOptions
            {
                Size = GeneratedImageSize.W1536xH1024,
            }, ct);
            return image.ImageBytes?.ToArray() is { Length: > 0 } bytes ? bytes
                : throw new PipelineException("E-IMAGE-GENERATION", "画像モデルから画像データが返りませんでした。GPT Image 対応のデプロイを指定してください");
        }
        catch (ClientResultException ex)
        {
            throw new PipelineException("E-IMAGE-GENERATION", $"画像生成に失敗しました（HTTP {ex.Status}）。Foundry の GPT Image デプロイ名・アクセス権・クォータ・コンテンツ制限を確認してください");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PipelineException("E-IMAGE-GENERATION", "画像生成がタイムアウトしました。接続先とネットワークを確認してください");
        }
    }
}
#pragma warning restore OPENAI001

public sealed record EngageImageProvenance(string Prompt, string Post, string Deployment, int Width, int Height);

public static class EngageImage
{
    public const string Scene = "MCと社員が、このトピックについてワクワクしながら話している";

    public static string Prompt(string post) => post + "\n\n" + Scene + """


アニメ調のイラスト。Viva Engage 用の横長バナー（最終サイズ 1000x200 px、縦横比 5:1）。
中央の横長の帯だけを切り出すため、MCと社員の顔・表情・会話の様子、およびトピックを表す要素を画像中央の高さ20%以内に収める。
文字・ロゴ・透かしは描かない。投稿文は題材であり、画像内に書き写さない。
""";

    public static async Task<EngageImageProvenance> WriteAsync(FFmpeg ff, IImageGenerator generator, string post, string deployment, string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(post))
            throw new PipelineException("E-IMAGE-GENERATION", "Viva Engage 用の投稿文がないため画像を生成できません");
        var prompt = Prompt(post);
        var bytes = await generator.GenerateAsync(prompt, ct);
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
            return new(prompt, post, deployment, 1000, 200);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
