namespace TachoGraphStudio.App.Stage;

// 回転補正(FR-06)をカーソルキーで操作するときのステップ幅(#133)。
// 1 回押下はマウスドラッグ(RotationDragCalculator の 0.5 度)より細かく決められるようにし、
// 押しっぱなしの連続押下では段階的に加速して大きく回せるようにする。
// WinUI 非依存にして、ステップの決まり方をテストで固定する
public sealed class RotationKeyStepAccelerator
{
    // 1 回押下のステップ。Slider の SmallChange / StepFrequency と一致させる
    public const double InitialStep = 0.1;

    // 連続押下時の上限。これより速いと目的の角度を通り過ぎて合わせにくい
    public const double MaxStep = 5.0;

    // Windows の既定キーリピート(毎秒 30 回前後)で、押し続けてから 0.5 秒ほどで上限に達する倍率
    private const double AccelerationFactor = 1.3;

    private int _repeatCount;

    /// <summary>次の 1 押下ぶんのステップ幅(度)を返し、加速段階を 1 つ進める。</summary>
    public double NextStep()
    {
        double step = Math.Min(MaxStep, InitialStep * Math.Pow(AccelerationFactor, _repeatCount));
        _repeatCount++;

        // Slider の刻みと同じ 0.1 度単位へ丸め、角度が半端な値にならないようにする
        return Math.Round(step, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>キーを離したときに呼び、次の押下を初期ステップへ戻す。</summary>
    public void Reset() => _repeatCount = 0;
}
