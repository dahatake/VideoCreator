using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>Project rename: shared name validation and that only the display name changes.</summary>
public class ProjectNameTests
{
    [Theory]
    [InlineData("  配信まとめ  ", "配信まとめ")]
    [InlineData("ｶﾞ", "ｶﾞ")]
    [InlineData("e\u0301", "\u00e9")]   // NFC
    public void Valid_names_are_trimmed_and_normalized(string raw, string expected)
    {
        Assert.Null(Api.ValidateProjectName(raw, out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    public void Empty_or_control_characters_are_rejected(string? raw) =>
        Assert.NotNull(Api.ValidateProjectName(raw, out _));

    [Fact]
    public void Length_limit_counts_characters_not_utf16_units()
    {
        Assert.Null(Api.ValidateProjectName(new string('あ', 80), out _));
        Assert.NotNull(Api.ValidateProjectName(new string('あ', 81), out _));
        Assert.Null(Api.ValidateProjectName(string.Concat(Enumerable.Repeat("😀", 80)), out _));
        Assert.NotNull(Api.ValidateProjectName(string.Concat(Enumerable.Repeat("😀", 81)), out _));
    }

    [Fact]
    public void Rename_keeps_directory_and_new_exports_use_the_new_name()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vc-name-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using var store = new Store(Path.Combine(dir, "db.sqlite"));
            var p = new Project { Id = "p_1", Name = "旧名", Directory = Path.Combine(dir, "旧名_1") };
            store.Put(Kinds.Project, p.Id, null, p);
            store.Put(Kinds.Project, p.Id, null, store.Get<Project>(Kinds.Project, p.Id)! with { Name = "新名/../x" });

            var got = store.Get<Project>(Kinds.Project, p.Id)!;
            Assert.Equal("新名/../x", got.Name);
            Assert.Equal(p.Directory, got.Directory);
            Assert.Equal(p.CreatedAt, got.CreatedAt);
            Assert.Single(store.List<Project>(Kinds.Project));

            var plan = new EditPlan { EditplanId = "e", ProjectId = p.Id, Preset = Fixtures.Presets["P4"].Ref, Audio = new AudioSpec { Master = "a1" } };
            var baseName = PipelineRunner.ExportBaseName(got, plan, 1);
            Assert.StartsWith("新名____x_P4_1_", baseName);
            Assert.DoesNotContain(Path.DirectorySeparatorChar, baseName);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
