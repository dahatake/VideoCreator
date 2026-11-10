using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

public sealed class VersionTests
{
    [Fact]
    public void Assemblies_share_the_central_version_and_file_metadata()
    {
        var props = XDocument.Load(Path.Combine(Fixtures.RepoRoot, "Directory.Build.props"));
        var prefix = Assert.Single(props.Descendants("VersionPrefix")).Value;
        Assert.Matches(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", prefix);
        Assert.Matches($"^{System.Text.RegularExpressions.Regex.Escape(prefix)}(-[0-9A-Za-z.-]+)?$", AppVersion.Version);
        Assert.Equal(AppVersion.InformationalVersion.Split('+', 2)[0], AppVersion.Version);

        foreach (var assembly in new[] { typeof(AppVersion).Assembly, typeof(Project).Assembly })
        {
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            Assert.NotNull(info);
            Assert.Equal(AppVersion.InformationalVersion, info.InformationalVersion);
            Assert.Equal(new Version(prefix + ".0"), assembly.GetName().Version);
            var file = FileVersionInfo.GetVersionInfo(assembly.Location);
            Assert.Equal(prefix + ".0", file.FileVersion);
            Assert.Equal(AppVersion.InformationalVersion, file.ProductVersion);
        }
    }

    [Fact]
    public async Task Health_reports_the_running_build_version_without_changing_existing_fields()
    {
        var root = Path.Combine(Path.GetTempPath(), "vc-version-" + Guid.NewGuid().ToString("N"));
        Store? store = null;
        try
        {
            await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("VideoCreator:DataDir", Path.Combine(root, "data"));
                b.UseSetting("VideoCreator:ProjectsDir", Path.Combine(root, "projects"));
                b.UseSetting("VideoCreator:OpenBrowser", "false");
                b.UseSetting("VideoCreator:LocalOnly", "true");
                b.UseSetting("VideoCreator:Asr:Provider", "none");
            });
            using var http = app.CreateClient(new() { BaseAddress = new Uri("http://127.0.0.1") });
            store = app.Services.GetRequiredService<Runtime>().Store;
            var health = await http.GetFromJsonAsync<JsonElement>("/api/health", TestContext.Current.CancellationToken);
            Assert.Equal(AppVersion.Version, health.GetProperty("version").GetString());
            Assert.Equal(AppVersion.InformationalVersion, health.GetProperty("informational_version").GetString());
            Assert.NotEmpty(health.GetProperty("ffmpeg").GetString()!);
            Assert.Equal(JsonValueKind.Array, health.GetProperty("missing_filters").ValueKind);
            Assert.True(health.GetProperty("presets").GetInt32() > 0);
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
