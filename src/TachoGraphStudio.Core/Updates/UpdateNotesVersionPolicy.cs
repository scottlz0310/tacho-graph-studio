namespace TachoGraphStudio.Core.Updates;

// 更新後の変更点表示(#100)でとる行動
public enum UpdateNotesAction
{
    // 何もしない(同一・過去バージョンでの起動)
    None,

    // 変更点は出さず、基準バージョンだけ記録する(新規インストール)
    RecordVersionOnly,

    // 前回表示バージョンから今回までの変更点を表示する
    Show,
}

// とるべき行動と、変更点の抽出に使う前回表示バージョン
public sealed record UpdateNotesPlan(UpdateNotesAction Action, Version? LastShownVersion);

public static class UpdateNotesVersionPolicy
{
    /// <summary>
    /// 保存済みの表示履歴と現在のバージョンから、変更点表示でとる行動を決める(#137)。
    /// 変更履歴ファイルの読み込みは <see cref="UpdateNotesAction.Show"/> のときだけでよいため、
    /// ここでは読み込まずに判断する。
    /// </summary>
    /// <param name="lastShownVersion">状態ファイルに記録された最後の表示バージョン。未記録なら null。</param>
    /// <param name="hasPersistedState">状態ファイルが存在したか(新規インストールの判別に使う)。</param>
    /// <param name="currentVersion">現在のパッケージバージョン。</param>
    public static UpdateNotesPlan Plan(
        string? lastShownVersion,
        bool hasPersistedState,
        Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        if (IsNewInstallation(lastShownVersion, hasPersistedState))
        {
            // 新規インストールでは変更履歴を表示せず、基準バージョンだけ記録する
            return new UpdateNotesPlan(UpdateNotesAction.RecordVersionOnly, null);
        }

        Version? resolved = ResolveLastShownVersion(lastShownVersion, hasPersistedState);
        return resolved is not null && resolved >= currentVersion
            ? new UpdateNotesPlan(UpdateNotesAction.None, resolved)
            : new UpdateNotesPlan(UpdateNotesAction.Show, resolved);
    }

    /// <summary>
    /// 状態ファイルへ記録する形式へ整える。<see cref="ResolveLastShownVersion"/> が読み戻せる形にする。
    /// </summary>
    public static string FormatVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public static bool IsNewInstallation(string? lastShownVersion, bool hasPersistedState) =>
        lastShownVersion is null && !hasPersistedState;

    public static Version? ResolveLastShownVersion(
        string? lastShownVersion,
        bool hasPersistedState)
    {
        if (lastShownVersion is null)
        {
            // LastShownVersion 導入前の状態ファイルは、v0.1.6 からの更新として扱う
            return hasPersistedState ? new Version(0, 1, 6) : null;
        }

        if (!Version.TryParse(lastShownVersion, out Version? parsed) || parsed is null)
        {
            return null;
        }

        return new Version(
            parsed.Major,
            Math.Max(parsed.Minor, 0),
            Math.Max(parsed.Build, 0));
    }
}
