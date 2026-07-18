using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace TachoGraphStudio.App.Controls;

// 列リサイズ用スプリッタの掴み領域(#25)。必要なのは 1 箇所の水平リサイズのみのため、
// CommunityToolkit.Sizers への依存を増やさず自前実装とする。
// ドラッグ処理は配置側(MainWindow)が Pointer イベントで行う
public sealed partial class SplitterThumb : Grid
{
    public SplitterThumb()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}
