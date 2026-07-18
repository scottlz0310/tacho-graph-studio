using TachoGraphStudio.App.Templates;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Tests.Templates;

public sealed class TemplateEditorViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesTemplatesAndSelectsFirst()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));
        await store.SaveAsync(id: null, CreateTemplate("Task-Meter"));
        TemplateEditorViewModel editor = new(store);

        await editor.LoadAsync();

        Assert.Equal(["Task-Meter", "Yazaki45"], editor.Templates.Select(item => item.Name));
        Assert.Same(editor.Templates[0], editor.SelectedTemplate);
        Assert.False(editor.IsLoading);
        Assert.False(editor.HasError);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task LoadAsync_ReportsFailuresAsWarning()
    {
        FakeTemplateStore store = new()
        {
            ListFailures = [new TemplateLoadFailure("broken.json", "解析できません")],
        };
        TemplateEditorViewModel editor = new(store);

        await editor.LoadAsync();

        Assert.True(editor.HasLoadWarning);
        Assert.Contains("broken.json", editor.LoadWarningMessage, StringComparison.Ordinal);
        Assert.False(editor.HasError);
    }

    [Fact]
    public async Task LoadAsync_StoreFailureSetsErrorMessage()
    {
        FakeTemplateStore store = new() { NextException = new IOException("ディスクエラー") };
        TemplateEditorViewModel editor = new(store);

        await editor.LoadAsync();

        Assert.True(editor.HasError);
        Assert.Contains("ディスクエラー", editor.ErrorMessage, StringComparison.Ordinal);
        Assert.False(editor.IsLoading);
    }

    [Fact]
    public async Task CreateNew_AddsSeededDirtyTemplateWithUniqueName()
    {
        TemplateEditorViewModel editor = new(new FakeTemplateStore());
        await editor.LoadAsync();

        editor.CreateNew();
        editor.CreateNew();

        Assert.Equal(["新規テンプレート", "新規テンプレート 2"], editor.Templates.Select(item => item.Name));
        TemplateItemViewModel selected = Assert.IsType<TemplateItemViewModel>(editor.SelectedTemplate);
        Assert.True(selected.IsDirty);
        Assert.Null(selected.Id);
        // 標準フィールドキー(#13 の名簿・日付反映が参照する)がシードされる
        Assert.Equal(
            ["date_day", "date_month", "date_year", "driver", "vehicle_no", "vehicle_type"],
            selected.Fields.Select(field => field.Name).Order(StringComparer.Ordinal));
        Assert.True(editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task DuplicateSelected_CopiesCurrentEditsWithNewName()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));
        TemplateEditorViewModel editor = new(store);
        await editor.LoadAsync();
        editor.SelectedTemplate!.Fields[0].XRatio = 0.9;

        editor.DuplicateSelected();

        TemplateItemViewModel copy = Assert.IsType<TemplateItemViewModel>(editor.SelectedTemplate);
        Assert.Equal("Yazaki45 のコピー", copy.Name);
        Assert.Null(copy.Id);
        Assert.True(copy.IsDirty);
        Assert.Equal(0.9, copy.Fields[0].XRatio);
    }

    [Fact]
    public async Task SaveSelectedAsync_PersistsAndClearsDirty()
    {
        FakeTemplateStore store = new();
        TemplateEditorViewModel editor = new(store);
        await editor.LoadAsync();
        editor.CreateNew();

        bool saved = await editor.SaveSelectedAsync();

        Assert.True(saved);
        Assert.Equal("新規テンプレート", editor.SelectedTemplate!.Id);
        Assert.False(editor.SelectedTemplate.IsDirty);
        Assert.False(editor.HasUnsavedChanges);
        Assert.True(store.Saved.ContainsKey("新規テンプレート"));
    }

    [Fact]
    public async Task SaveSelectedAsync_DuplicateFieldNameSetsErrorMessage()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45", "driver", "vehicle_no"));
        TemplateEditorViewModel editor = new(store);
        await editor.LoadAsync();
        editor.SelectedTemplate!.Fields[1].Name = "driver";

        bool saved = await editor.SaveSelectedAsync();

        Assert.False(saved);
        Assert.True(editor.HasError);
        Assert.Contains("driver", editor.ErrorMessage, StringComparison.Ordinal);
        Assert.True(editor.SelectedTemplate.IsDirty);
    }

    [Fact]
    public async Task SaveSelectedAsync_StoreFailureSetsErrorMessage()
    {
        FakeTemplateStore store = new();
        TemplateEditorViewModel editor = new(store);
        await editor.LoadAsync();
        editor.CreateNew();
        store.NextException = new IOException("ディスクエラー");

        bool saved = await editor.SaveSelectedAsync();

        Assert.False(saved);
        Assert.True(editor.HasError);
        Assert.True(editor.SelectedTemplate!.IsDirty);
    }

    [Fact]
    public async Task DeleteSelectedAsync_RemovesSavedTemplateAndSelectsNeighbor()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, CreateTemplate("Task-Meter"));
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));
        TemplateEditorViewModel editor = new(store);
        await editor.LoadAsync();

        await editor.DeleteSelectedAsync();

        Assert.Equal(["Task-Meter"], store.DeletedIds);
        TemplateItemViewModel remaining = Assert.Single(editor.Templates);
        Assert.Equal("Yazaki45", remaining.Name);
        Assert.Same(remaining, editor.SelectedTemplate);
    }

    [Fact]
    public async Task DeleteSelectedAsync_UnsavedTemplateDoesNotCallStore()
    {
        FakeTemplateStore store = new();
        TemplateEditorViewModel editor = new(store);
        await editor.LoadAsync();
        editor.CreateNew();

        await editor.DeleteSelectedAsync();

        Assert.Empty(store.DeletedIds);
        Assert.Empty(editor.Templates);
        Assert.Null(editor.SelectedTemplate);
    }

    [Fact]
    public async Task ImportAsync_SavesAndSelectsImportedTemplate()
    {
        FakeTemplateStore store = new();
        TemplateEditorViewModel editor = new(store);
        await editor.LoadAsync();
        string json = ChartTemplateSerializer.Serialize(CreateTemplate("Yazaki45"));

        bool imported = await editor.ImportAsync("Yazaki45.json", json);

        Assert.True(imported);
        TemplateItemViewModel item = Assert.Single(editor.Templates);
        Assert.Equal("Yazaki45", item.Id);
        Assert.False(item.IsDirty);
        Assert.Same(item, editor.SelectedTemplate);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    public async Task ImportAsync_InvalidJsonSetsErrorMessage(string json)
    {
        TemplateEditorViewModel editor = new(new FakeTemplateStore());
        await editor.LoadAsync();

        bool imported = await editor.ImportAsync("broken.json", json);

        Assert.False(imported);
        Assert.True(editor.HasError);
        Assert.Contains("broken.json", editor.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(editor.Templates);
    }

    [Theory]
    [InlineData("", "フィールド名を入力してください。")]
    [InlineData("  ", "フィールド名を入力してください。")]
    [InlineData("driver", "フィールド名 'driver' は既に存在します。")]
    public async Task AddFieldToSelected_InvalidNameSetsErrorMessage(string name, string expectedMessage)
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45", "driver"));
        TemplateEditorViewModel editor = new(store);
        await editor.LoadAsync();

        TemplateFieldViewModel? added = editor.AddFieldToSelected(name);

        Assert.Null(added);
        Assert.Equal(expectedMessage, editor.ErrorMessage);
        Assert.Single(editor.SelectedTemplate!.Fields);
    }

    [Fact]
    public async Task AddFieldToSelected_TrimsNameAndMarksDirty()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45", "driver"));
        TemplateEditorViewModel editor = new(store);
        await editor.LoadAsync();

        TemplateFieldViewModel? added = editor.AddFieldToSelected(" vehicle_no ");

        Assert.NotNull(added);
        Assert.Equal("vehicle_no", added.Name);
        Assert.True(editor.SelectedTemplate!.IsDirty);
        Assert.False(editor.HasError);
    }

    [Fact]
    public async Task HasUnsavedChanges_NotifiesOnTemplateAndDirtyChanges()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));
        TemplateEditorViewModel editor = new(store);
        await editor.LoadAsync();
        int notificationCount = 0;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TemplateEditorViewModel.HasUnsavedChanges))
            {
                notificationCount++;
            }
        };

        // 追加(CreateNew)で通知
        editor.CreateNew();
        Assert.True(notificationCount > 0);
        Assert.True(editor.HasUnsavedChanges);

        // 保存(IsDirty 変化)で通知
        int beforeSave = notificationCount;
        await editor.SaveSelectedAsync();
        Assert.True(notificationCount > beforeSave);
        Assert.False(editor.HasUnsavedChanges);

        // 既存要素のフィールド編集(IsDirty 変化)で通知
        int beforeEdit = notificationCount;
        editor.SelectedTemplate!.Fields[0].XRatio = 0.9;
        Assert.True(notificationCount > beforeEdit);
        Assert.True(editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task HasUnsavedChanges_NotifiesAfterReloadReplacesItems()
    {
        FakeTemplateStore store = new();
        await store.SaveAsync(id: null, CreateTemplate("Yazaki45"));
        TemplateEditorViewModel editor = new(store);
        await editor.LoadAsync();
        editor.SelectedTemplate!.Name = "編集中";
        Assert.True(editor.HasUnsavedChanges);

        // 再読込(Clear + 再構築)後も新しい要素の IsDirty 変更が通知される
        await editor.LoadAsync();
        Assert.False(editor.HasUnsavedChanges);

        int notificationCount = 0;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TemplateEditorViewModel.HasUnsavedChanges))
            {
                notificationCount++;
            }
        };
        editor.SelectedTemplate!.Name = "再編集";

        Assert.True(notificationCount > 0);
        Assert.True(editor.HasUnsavedChanges);
    }

    private static ChartTemplate CreateTemplate(string name, params string[] fieldNames)
    {
        string[] names = fieldNames.Length == 0 ? ["driver"] : fieldNames;
        return new ChartTemplate
        {
            Name = name,
            Description = "テスト用",
            ReferenceWidth = 1453,
            ReferenceHeight = 1456,
            Fields = names.ToDictionary(
                fieldName => fieldName,
                fieldName => new TextFieldDefinition
                {
                    Position = new TextPosition { XRatio = 0.4, YRatio = 0.5 },
                }),
        };
    }
}
