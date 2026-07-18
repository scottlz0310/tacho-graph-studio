namespace TachoGraphStudio.App.Stage;

// サムネイルナビに表示する処理ステータス(FR-04)。Done/Skipped への遷移は保存フロー(issue #14)が行う
public enum DiscStatus
{
    Pending,
    Done,
    Skipped,
}
