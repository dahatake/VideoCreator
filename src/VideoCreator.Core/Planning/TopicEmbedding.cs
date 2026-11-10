using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text;
using Azure.Core;
using OpenAI;
using OpenAI.Embeddings;
using VideoCreator.Models;

namespace VideoCreator.Planning;

#pragma warning disable OPENAI001

public interface ITextEmbedder
{
    string Model { get; }
    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

/// <summary>Microsoft Foundry embeddings (text-embedding-3-large) with Entra ID, on the same resource and endpoint as the chat model.</summary>
public sealed class AzureOpenAIEmbedder : ITextEmbedder
{
    readonly EmbeddingClient _client;
    public string Model { get; }

    public AzureOpenAIEmbedder(Uri v1Endpoint, string deployment, TokenCredential credential)
    {
        Model = deployment;
        _client = new EmbeddingClient(deployment, new BearerTokenPolicy(credential, "https://ai.azure.com/.default"),
            new OpenAIClientOptions { Endpoint = v1Endpoint, NetworkTimeout = TimeSpan.FromMinutes(2) });
    }

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var all = new List<float[]>();
        for (int i = 0; i < texts.Count; i += 96)
        {
            var batch = texts.Skip(i).Take(96).Select(t => string.IsNullOrWhiteSpace(t) ? "。" : t).ToList();
            var r = await _client.GenerateEmbeddingsAsync(batch, cancellationToken: ct);
            all.AddRange(r.Value.OrderBy(e => e.Index).Select(e => e.ToFloats().ToArray()));
        }
        return [.. all];
    }
}

/// <summary>
/// The meaning-level view of "where does the subject change": each sentence becomes a vector, and a boundary is deep when the average meaning of the
/// seconds before it differs from the seconds after it (TextTiling on embeddings). Unlike word overlap it survives synonyms, katakana/English variants and ASR slips.
/// </summary>
public static class TopicEmbedding
{
    public static readonly double[] Windows = [30, 60, 90];
    /// <summary>Weight of the meaning-level depth when it is blended with the vocabulary-level depth.</summary>
    public const double Weight = 0.7;

    public static string Key(Transcript tr, string model)
    {
        var sb = new StringBuilder(model).Append('|');
        foreach (var s in tr.Sentences) sb.Append(s.Text).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    public static async Task<float[][]?> LoadOrEmbedAsync(ITextEmbedder embedder, Transcript tr, string cacheFile, CancellationToken ct, bool refresh = false)
    {
        var key = Key(tr, embedder.Model);
        try
        {
            if (!refresh && File.Exists(cacheFile))
            {
                var c = Json.Deserialize<EmbeddingCache>(File.ReadAllText(cacheFile));
                if (c.Key == key && c.Vectors.Length == tr.Sentences.Count) return c.Vectors;
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }
        var vecs = await embedder.EmbedAsync(tr.Sentences.Select(s => s.Text).ToList(), ct);
        if (vecs.Length != tr.Sentences.Count) return null;
        File.WriteAllText(cacheFile, Json.Serialize(new EmbeddingCache(key, vecs)));
        return vecs;
    }

    public sealed record EmbeddingCache(string Key, float[][] Vectors);

    static double Cosine(double[] a, double[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na > 0 && nb > 0 ? dot / Math.Sqrt(na * nb) : 0;
    }

    /// <summary>Normalised (0..1) valley depth per boundary (index 0 and n are 0), averaged over <see cref="Windows"/>.</summary>
    public static double[] Depth(Transcript tr, float[][] vecs, double[]? windows = null)
    {
        windows ??= Windows;
        var s = tr.Sentences; int n = s.Count;
        var depth = new double[n + 1];
        if (n < 3 || vecs.Length != n) return depth;
        int dim = vecs[0].Length;
        // prefix sums make the window means O(1)
        var pre = new double[n + 1][];
        pre[0] = new double[dim];
        for (int i = 0; i < n; i++) { pre[i + 1] = new double[dim]; for (int d = 0; d < dim; d++) pre[i + 1][d] = pre[i][d] + vecs[i][d]; }
        double[] Sum(int a, int b) { var r = new double[dim]; for (int d = 0; d < dim; d++) r[d] = pre[b + 1][d] - pre[a][d]; return r; }
        foreach (var w in windows)
        {
            var sim = new double[n + 1];
            for (int i = 1; i < n; i++)
            {
                int a = i - 1; while (a > 0 && s[i].T0 - s[a - 1].T0 <= w) a--;
                int b = i; while (b < n - 1 && s[b + 1].T1 - s[i].T0 <= w) b++;
                sim[i] = Cosine(Sum(a, i - 1), Sum(i, b));
            }
            for (int i = 1; i < n; i++)
            {
                double lp = sim[i], rp = sim[i];
                for (int j = i - 1; j >= 1 && sim[j] >= lp; j--) lp = sim[j];
                for (int j = i + 1; j < n && sim[j] >= rp; j++) rp = sim[j];
                depth[i] += (lp - sim[i] + rp - sim[i]) / windows.Length;
            }
        }
        double max = depth.Max();
        if (max > 0) for (int i = 0; i <= n; i++) depth[i] /= max;
        return depth;
    }

    public static double[] Blend(double[] lexical, double[]? semantic) =>
        semantic == null || semantic.Length != lexical.Length ? lexical : lexical.Select((v, i) => (1 - Weight) * v + Weight * semantic[i]).ToArray();
}
