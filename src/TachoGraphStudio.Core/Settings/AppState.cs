namespace TachoGraphStudio.Core.Settings;

// アプリ状態の永続化モデル(FR-22)。null は「未保存(既定値のまま)」を表し、
// 復元側は各項目を個別に既定へフォールバックできる
public sealed record AppState
{
    public string? OutputDirectory { get; init; }

    // 前回の処理対象日。翌日以降の起動では当日を使うかどうかの判断は復元側が行う
    public DateOnly? LastTargetDate { get; init; }

    // 選択していたチャート紙様式のテンプレート ID(ITemplateStore の ID)
    public string? SelectedTemplateId { get; init; }

    public double? SidebarWidth { get; init; }

    public WindowPlacement? Window { get; init; }
}

// 通常表示時のウィンドウ位置・サイズ(物理ピクセル)と最大化状態
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized);
