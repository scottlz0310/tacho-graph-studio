using System.Security.AccessControl;
using System.Security.Principal;

using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.Core.Tests.Templates;

public sealed class FileTemplateStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TachoGraphStudio.Tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ListAsync_MissingDirectoryReturnsEmpty()
    {
        FileTemplateStore store = new(_temporaryDirectory);

        TemplateStoreListResult result = await store.ListAsync();

        Assert.Empty(result.Templates);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task SaveAndListAsync_RoundTripsTemplate()
    {
        FileTemplateStore store = new(_temporaryDirectory);
        ChartTemplate template = CreateTemplate("Yazaki45");

        StoredTemplate stored = await store.SaveAsync(id: null, template);
        TemplateStoreListResult result = await store.ListAsync();

        Assert.Equal("Yazaki45", stored.Id);
        Assert.True(File.Exists(Path.Combine(_temporaryDirectory, "Yazaki45.json")));
        StoredTemplate listed = Assert.Single(result.Templates);
        Assert.Equal(stored.Id, listed.Id);
        // record の Dictionary プロパティは参照比較になるため、正規化された JSON で比較する
        Assert.Equal(
            ChartTemplateSerializer.Serialize(template),
            ChartTemplateSerializer.Serialize(listed.Template));
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData("a/b:c", "a_b_c")]
    [InlineData("  ", "template")]
    [InlineData("name.", "name")]
    [InlineData(" spaced ", "spaced")]
    public async Task SaveAsync_SanitizesGeneratedId(string name, string expectedId)
    {
        FileTemplateStore store = new(_temporaryDirectory);

        StoredTemplate stored = await store.SaveAsync(id: null, CreateTemplate(name));

        Assert.Equal(expectedId, stored.Id);
        Assert.True(File.Exists(Path.Combine(_temporaryDirectory, $"{expectedId}.json")));
    }

    [Fact]
    public async Task SaveAsync_SameNameGeneratesUniqueIds()
    {
        FileTemplateStore store = new(_temporaryDirectory);

        StoredTemplate first = await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));
        StoredTemplate second = await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));

        Assert.Equal("Yazaki45", first.Id);
        Assert.Equal("Yazaki45-2", second.Id);
        TemplateStoreListResult result = await store.ListAsync();
        Assert.Equal(2, result.Templates.Count);
    }

    [Fact]
    public async Task SaveAsync_ExistingIdOverwrites()
    {
        FileTemplateStore store = new(_temporaryDirectory);
        StoredTemplate stored = await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));

        ChartTemplate updated = CreateTemplate("Yazaki45") with { Description = "更新済み" };
        await store.SaveAsync(stored.Id, updated);

        TemplateStoreListResult result = await store.ListAsync();
        StoredTemplate listed = Assert.Single(result.Templates);
        Assert.Equal("更新済み", listed.Template.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(" leading")]
    [InlineData("trailing.")]
    public async Task SaveAsync_InvalidIdThrows(string id)
    {
        FileTemplateStore store = new(_temporaryDirectory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(id, CreateTemplate("Yazaki45")));
    }

    [Fact]
    public async Task SaveAsync_InvalidTemplateThrowsWithoutWriting()
    {
        FileTemplateStore store = new(_temporaryDirectory);
        ChartTemplate invalid = CreateTemplate("Yazaki45") with { ReferenceWidth = 0 };

        await Assert.ThrowsAsync<TemplateFormatException>(
            () => store.SaveAsync(id: null, invalid));

        Assert.False(Directory.Exists(_temporaryDirectory));
    }

    [Fact]
    public async Task SaveAsync_MoveFailureRemovesTemporaryFile()
    {
        FileTemplateStore store = new(_temporaryDirectory);
        // 保存先パスをディレクトリで塞ぎ、Move を失敗させる
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "Yazaki45.json"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => store.SaveAsync("Yazaki45", CreateTemplate("Yazaki45")));

        Assert.Empty(Directory.EnumerateFiles(_temporaryDirectory, "*.tmp"));
    }

    [Fact]
    public async Task ListAsync_CorruptedFileReportsFailureAndKeepsOthers()
    {
        FileTemplateStore store = new(_temporaryDirectory);
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));
        await File.WriteAllTextAsync(
            Path.Combine(_temporaryDirectory, "broken.json"),
            "{ not json");

        TemplateStoreListResult result = await store.ListAsync();

        StoredTemplate listed = Assert.Single(result.Templates);
        Assert.Equal("Yazaki45", listed.Id);
        TemplateLoadFailure failure = Assert.Single(result.Failures);
        Assert.Equal("broken.json", failure.FileName);
        Assert.NotEmpty(failure.Message);
    }

    [Fact]
    public async Task ListAsync_AccessDeniedFileReportsFailureAndKeepsOthers()
    {
        // ACL の読み取り拒否で UnauthorizedAccessException(IOException 非派生)の failure path を再現する
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FileTemplateStore store = new(_temporaryDirectory);
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));
        string deniedPath = Path.Combine(_temporaryDirectory, "denied.json");
        await File.WriteAllTextAsync(deniedPath, "{}");
        FileInfo fileInfo = new(deniedPath);
        FileSecurity security = fileInfo.GetAccessControl();
        FileSystemAccessRule denyRead = new(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.Read,
            AccessControlType.Deny);
        security.AddAccessRule(denyRead);
        fileInfo.SetAccessControl(security);

        try
        {
            TemplateStoreListResult result = await store.ListAsync();

            StoredTemplate listed = Assert.Single(result.Templates);
            Assert.Equal("Yazaki45", listed.Id);
            TemplateLoadFailure failure = Assert.Single(result.Failures);
            Assert.Equal("denied.json", failure.FileName);
            Assert.NotEmpty(failure.Message);
        }
        finally
        {
            security.RemoveAccessRule(denyRead);
            fileInfo.SetAccessControl(security);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteAsync_IsIdempotent(bool saveFirst)
    {
        FileTemplateStore store = new(_temporaryDirectory);
        if (saveFirst)
        {
            await store.SaveAsync("Yazaki45", CreateTemplate("Yazaki45"));
        }

        await store.DeleteAsync("Yazaki45");

        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "Yazaki45.json")));
    }

    [Fact]
    public async Task ExportAllAsync_MissingSourceDirectoryExportsNothing()
    {
        FileTemplateStore store = new(_temporaryDirectory);
        string destination = Path.Combine(_temporaryDirectory, "..", $"export-{Guid.NewGuid():N}");

        TemplateExportResult result = await store.ExportAllAsync(destination);

        Assert.Equal(0, result.ExportedCount);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task ExportAllAsync_WritesTemplatesPreservingFileContent()
    {
        FileTemplateStore store = new(_temporaryDirectory);
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));
        await store.SaveAsync(id: null, CreateTemplate("Task-Meter"));
        string destination = Path.Combine(_temporaryDirectory, "export");

        TemplateExportResult result = await store.ExportAllAsync(destination);

        Assert.Equal(2, result.ExportedCount);
        Assert.Empty(result.Failures);
        foreach (string id in (string[])["Yazaki45", "Task-Meter"])
        {
            string sourcePath = Path.Combine(_temporaryDirectory, $"{id}.json");
            string exportedPath = Path.Combine(destination, $"{id}.json");
            Assert.True(File.Exists(exportedPath));
            Assert.Equal(await File.ReadAllTextAsync(sourcePath), await File.ReadAllTextAsync(exportedPath));
        }
    }

    [Fact]
    public async Task ExportAllAsync_BrokenTemplateIsReportedAndOthersAreExported()
    {
        FileTemplateStore store = new(_temporaryDirectory);
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "broken.json"), "{ not json");
        string destination = Path.Combine(_temporaryDirectory, "export");

        TemplateExportResult result = await store.ExportAllAsync(destination);

        Assert.Equal(1, result.ExportedCount);
        TemplateLoadFailure failure = Assert.Single(result.Failures);
        Assert.Equal("broken.json", failure.FileName);
        Assert.True(File.Exists(Path.Combine(destination, "Yazaki45.json")));
        Assert.False(File.Exists(Path.Combine(destination, "broken.json")));
    }

    [Fact]
    public async Task ExportAllAsync_OverwritesExistingFilesInDestination()
    {
        FileTemplateStore store = new(_temporaryDirectory);
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));
        string destination = Path.Combine(_temporaryDirectory, "export");
        Directory.CreateDirectory(destination);
        string exportedPath = Path.Combine(destination, "Yazaki45.json");
        await File.WriteAllTextAsync(exportedPath, "古いバックアップ");

        TemplateExportResult result = await store.ExportAllAsync(destination);

        Assert.Equal(1, result.ExportedCount);
        Assert.Equal(
            await File.ReadAllTextAsync(Path.Combine(_temporaryDirectory, "Yazaki45.json")),
            await File.ReadAllTextAsync(exportedPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExportAllAsync_BlankDestinationThrows(string destination)
    {
        FileTemplateStore store = new(_temporaryDirectory);

        await Assert.ThrowsAsync<ArgumentException>(() => store.ExportAllAsync(destination));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static ChartTemplate CreateTemplate(string name) => new()
    {
        Name = name,
        Description = "テスト用",
        ReferenceWidth = 1453,
        ReferenceHeight = 1456,
        Fields = new Dictionary<string, TextFieldDefinition>
        {
            ["driver"] = new()
            {
                Position = new TextPosition { XRatio = 0.49, YRatio = 0.41 },
            },
        },
    };
}
