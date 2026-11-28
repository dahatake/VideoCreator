using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VideoCreator.Tests;

/// <summary>Every API error has the same JSON shape the UI reads — also the ones ASP.NET Core answers without a body in production.</summary>
public sealed class ApiErrorTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-apierr-" + Guid.NewGuid().ToString("N")[..8]);
    WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");   // malformed bodies only become exceptions in Development
            b.UseSetting("VideoCreator:DataDir", Path.Combine(_root, "data"));
            b.UseSetting("VideoCreator:ProjectsDir", Path.Combine(_root, "projects"));
            b.UseSetting("VideoCreator:OpenBrowser", "false");
        });
        _http = _app.CreateClient(new() { BaseAddress = new Uri("http://127.0.0.1") });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    async Task<string?> ErrorCode(HttpResponseMessage r)
    {
        Assert.Equal("application/json", r.Content.Headers.ContentType?.MediaType);
        var error = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error");
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        return error.GetProperty("code").GetString();
    }

    HttpRequestMessage Post(string path, string json) => new(HttpMethod.Post, path)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
        Headers = { { "X-VideoCreator", "1" } },
    };

    [Fact]
    public async Task Bodyless_api_errors_answer_in_the_api_error_shape()
    {
        foreach (var body in new[] { "{bad", """{"name": 5}""" })
        {
            using var r = await _http.SendAsync(Post("/api/projects", body), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("E-VALIDATION", await ErrorCode(r));
        }
        using (var r = await _http.GetAsync("/api/no-such-thing", Ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal("E-NOT-FOUND", await ErrorCode(r));
        }
        using (var r = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/presets") { Headers = { { "X-VideoCreator", "1" } } }, Ct))
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, r.StatusCode);
            Assert.Equal("E-METHOD", await ErrorCode(r));
        }
        using (var r = await _http.PostAsync("/api/projects", JsonContent.Create(new { name = "x" }), Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("E-CSRF", await ErrorCode(r));
        }

        // outside /api nothing changes: a missing page stays a plain 404
        using var page = await _http.GetAsync("/no-such-page.js", Ct);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.NotEqual("application/json", page.Content.Headers.ContentType?.MediaType);
    }
}
