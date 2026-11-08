using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Asr;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Qa;

namespace VideoCreator.Tests;

public sealed class PrdVerificationTests
{
    [Theory]
    [InlineData(5, 0.7)]
    [InlineData(0.4, 0.4)]
    public void Caption_extension_is_bounded_and_unresolved_speed_is_warned_without_deleting_text(double clipEnd, double expectedEnd)
    {
        const string text = "あいうえおかきくけこさしすせ";
        var tr = new Transcript
        {
            Provider = "fixture", Duration = 5,
            Words = [new Word("w1", 0, 0.2, text, 0)],
            Sentences = [new Sentence(0, 0, 0.2, text, 0, 0, 0)],
        };
        var clips = new List<Clip>
        {
            new() { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = clipEnd },
            new() { ClipId = "c2", AssetId = "a1", SrcIn = 6, SrcOut = 7 },
        };
        var preset = Fixtures.Presets["P4"];
        var telops = TelopBuilder.Build(clips, tr, preset, "", "", [], []);
        var caption = Assert.Single(telops);
        Assert.Equal(text, caption.Text);
        Assert.InRange(caption.End - tr.Words[0].T1, 0, TelopBuilder.CaptionHoldMax + 0.001);
        Assert.Equal(expectedEnd, caption.End, 3);
        Assert.Equal(0.5, TelopBuilder.CaptionHoldMax);
        var plan = new EditPlan { EditplanId = "ep", ProjectId = "p", Timeline = clips, Preset = preset.Ref, Telops = telops, Audio = new AudioSpec { Master = "a1" } };
        var check = Assert.Single(QaEngine.PlanChecks(new QaContext(plan, preset, tr, ["a1"], [])), q => q.CheckId == "S-01");
        Assert.Equal(QaStatus.Warn, check.Status);
    }

    [Fact]
    public async Task LocalOnly_forces_no_llm_and_discloses_asr_and_authentication_before_generation()
    {
        var root = Path.Combine(Path.GetTempPath(), "vc-prd-" + Guid.NewGuid().ToString("N"));
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("VideoCreator:DataDir", Path.Combine(root, "data"));
            b.UseSetting("VideoCreator:ProjectsDir", Path.Combine(root, "projects"));
            b.UseSetting("VideoCreator:OpenBrowser", "false");
            b.UseSetting("VideoCreator:Port", "0");
            b.UseSetting("VideoCreator:LocalOnly", "true");
            b.UseSetting("VideoCreator:Asr:Endpoint", "https://fixture-user:fixture-password@speech.example.invalid/path?token=fixture-secret");
            b.UseSetting("VideoCreator:Llm:Provider", "azure-openai");
            b.UseSetting("VideoCreator:Llm:Endpoint", "https://llm.example.invalid/");
        });
        app.UseKestrel();
        var rt = app.Services.GetRequiredService<Runtime>();
        try
        {
            using var http = app.CreateClient();
            http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
            var settings = await http.GetFromJsonAsync<JsonElement>("/api/settings", TestContext.Current.CancellationToken);
            Assert.Equal("none", settings.GetProperty("llm_provider").GetString());
            Assert.Null(rt.Llm);
            Assert.NotNull(rt.Asr);
            Assert.Equal("false", Environment.GetEnvironmentVariable("AZURE_CORE_COLLECT_TELEMETRY"));
            var transmission = settings.GetProperty("transmission");
            Assert.True(transmission.GetProperty("llm_disabled").GetBoolean());
            Assert.Equal("https://speech.example.invalid", transmission.GetProperty("asr_endpoint").GetString());
            Assert.DoesNotContain("fixture-password", transmission.GetRawText());
            Assert.DoesNotContain("fixture-secret", transmission.GetRawText());
            Assert.Contains("Entra ID", transmission.GetProperty("authentication").GetString());
            Assert.False(transmission.TryGetProperty("llm_destination", out _));
            using var denied = await http.PutAsJsonAsync("/api/settings", new { llm_provider = "azure-openai" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.Null(rt.Llm);
        }
        finally
        {
            await app.DisposeAsync();
            rt.Store.Dispose();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Speech_client_sends_audio_only_to_configured_endpoint_with_a_mock_token()
    {
        var file = Path.Combine(Path.GetTempPath(), "vc-speech-" + Guid.NewGuid().ToString("N") + ".flac");
        var handler = new SpeechCapture();
        using var http = new HttpClient(handler);
        var token = new FixedCredential();
        try
        {
            await File.WriteAllBytesAsync(file, [0x66, 0x4c, 0x61, 0x43], TestContext.Current.CancellationToken);
            var asr = new AzureFastTranscriber(new Uri("https://speech.example.invalid/"), token, http);
            var transcript = await asr.TranscribeAsync(file, 125, [], 4, TestContext.Current.CancellationToken);
            Assert.NotEmpty(transcript.Words);
            Assert.Equal(1, handler.Requests);
            Assert.Equal("speech.example.invalid", handler.Endpoint?.Host);
            Assert.Equal("/speechtotext/transcriptions:transcribe", handler.Endpoint?.AbsolutePath);
            Assert.Equal("audio/flac", handler.AudioContentType);
            Assert.DoesNotContain("video/mp4", handler.Body);
            Assert.Equal(["https://cognitiveservices.azure.com/.default"], token.Scopes);
        }
        finally { File.Delete(file); }
    }

    sealed class FixedCredential : TokenCredential
    {
        public string[] Scopes { get; private set; } = [];
        public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct)
        {
            Scopes = context.Scopes;
            return new AccessToken("fixture-token", DateTimeOffset.UtcNow.AddHours(1));
        }
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) => ValueTask.FromResult(GetToken(context, ct));
    }

    sealed class SpeechCapture : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public Uri? Endpoint { get; private set; }
        public string Body { get; private set; } = "";
        public string? AudioContentType { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Endpoint = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(ct);
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            var audio = Assert.Single(form, part => part.Headers.ContentDisposition?.Name?.Trim('"') == "audio");
            AudioContentType = audio.Headers.ContentType?.MediaType;
            Assert.Equal("fixture-token", request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(await File.ReadAllTextAsync(Path.Combine(Fixtures.Dir, "talk_transcript.json"), ct)) };
        }
    }
}
