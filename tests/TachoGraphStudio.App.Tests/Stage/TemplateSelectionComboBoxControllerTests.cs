using TachoGraphStudio.App.Stage;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Tests.Stage;

public sealed class TemplateSelectionComboBoxControllerTests
{
    private static readonly StoredTemplate Yazaki45 = new("Yazaki45", new ChartTemplate { Name = "Yazaki45" });
    private static readonly StoredTemplate TaskMeter = new("Task-Meter", new ChartTemplate { Name = "Task-Meter" });

    [Fact]
    public void ApplyFromViewModel_SetsComboBoxItemWithoutWritingBack()
    {
        // WinUI の ComboBox は SelectedItem 代入で SelectionChanged を同期的に発火する。
        // setComboBoxSelectedItem 内でその再入を模擬し、VM 駆動の変更が
        // ユーザー選択として誤って書き戻されないことを確認する(#43 レビュー指摘)
        object? comboBoxSelectedItem = null;
        List<StoredTemplate?> writeBackCalls = [];
        TemplateSelectionComboBoxController? controller = null;
        controller = new TemplateSelectionComboBoxController(
            setComboBoxSelectedItem: item =>
            {
                comboBoxSelectedItem = item;
                _ = controller!.OnSelectionChangedAsync(item, item as StoredTemplate);
            },
            selectTemplateForSelectedDisc: template => writeBackCalls.Add(template),
            openEditorAsync: () => Task.CompletedTask);

        controller.ApplyFromViewModel(Yazaki45);

        Assert.Same(Yazaki45, comboBoxSelectedItem);
        Assert.Empty(writeBackCalls);
    }

    [Fact]
    public async Task OnSelectionChangedAsync_RealTemplateWritesBackToViewModel()
    {
        List<StoredTemplate?> writeBackCalls = [];
        TemplateSelectionComboBoxController controller = new(
            setComboBoxSelectedItem: _ => { },
            selectTemplateForSelectedDisc: template => writeBackCalls.Add(template),
            openEditorAsync: () => Task.CompletedTask);

        await controller.OnSelectionChangedAsync(Yazaki45, currentSelectedTemplate: null);

        Assert.Equal([Yazaki45], writeBackCalls);
    }

    [Fact]
    public async Task OnSelectionChangedAsync_EditEntryRevertsSelectionAndOpensEditorWithoutWriteBack()
    {
        List<object?> comboBoxAssignments = [];
        List<StoredTemplate?> writeBackCalls = [];
        int editorOpenCount = 0;
        TemplateSelectionComboBoxController controller = new(
            setComboBoxSelectedItem: item => comboBoxAssignments.Add(item),
            selectTemplateForSelectedDisc: template => writeBackCalls.Add(template),
            openEditorAsync: () =>
            {
                editorOpenCount++;
                return Task.CompletedTask;
            });

        await controller.OnSelectionChangedAsync(TemplateEditEntry.Instance, currentSelectedTemplate: Yazaki45);

        Assert.Equal([Yazaki45], comboBoxAssignments);
        Assert.Empty(writeBackCalls);
        Assert.Equal(1, editorOpenCount);
    }

    [Fact]
    public async Task OnSelectionChangedAsync_AfterApplyFromViewModelGuardIsReleased()
    {
        // ApplyFromViewModel によるガードは一時的なもので、その後の真のユーザー操作は
        // 正しく処理されることを確認する
        List<StoredTemplate?> writeBackCalls = [];
        TemplateSelectionComboBoxController controller = new(
            setComboBoxSelectedItem: _ => { },
            selectTemplateForSelectedDisc: template => writeBackCalls.Add(template),
            openEditorAsync: () => Task.CompletedTask);

        controller.ApplyFromViewModel(Yazaki45);
        await controller.OnSelectionChangedAsync(TaskMeter, currentSelectedTemplate: Yazaki45);

        Assert.Equal([TaskMeter], writeBackCalls);
    }

    [Fact]
    public async Task OnSelectionChangedAsync_NullSelectionWritesBackNull()
    {
        List<StoredTemplate?> writeBackCalls = [];
        TemplateSelectionComboBoxController controller = new(
            setComboBoxSelectedItem: _ => { },
            selectTemplateForSelectedDisc: template => writeBackCalls.Add(template),
            openEditorAsync: () => Task.CompletedTask);

        await controller.OnSelectionChangedAsync(selectedItem: null, currentSelectedTemplate: Yazaki45);

        Assert.Equal([null], writeBackCalls);
    }
}
