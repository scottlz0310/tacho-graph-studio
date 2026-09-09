using Windows.System;

namespace TachoGraphStudio.App.Stage;

// 回転補正(FR-06)をカーソルキーで操作するときの角度計算(#133)。
// Slider は矢印キーで自身の SmallChange 分だけ値を動かすため、ここでは「加速したぶんの差」を返す。
// 1 回押下はマウスドラッグ(RotationDragCalculator の 0.5 度)より細かく決められるようにし、
// 押しっぱなしの連続押下では段階的に加速して大きく回せるようにする。
// XAML ランタイム非依存にして、キーの割り当てと加速の決まり方をテストで固定する
public sealed class RotationKeyStepCalculator
{
    // 1 回押下のステップ。Slider の SmallChange / StepFrequency と一致させる
    public const double InitialStep = 0.1;

    // 連続押下時の上限。これより速いと目的の角度を通り過ぎて合わせにくい
    public const double MaxStep = 5.0;

    // Windows の既定キーリピート(毎秒 30 回前後)で、押し始めから 0.5 秒ほどで上限に達する倍率
    private const double AccelerationFactor = 1.3;

    private int _repeatCount;

    /// <summary>
    /// <paramref name="key"/> の押下に対して、Slider の既定処理へ上乗せする角度(度)を返す。
    /// 回転に使わないキーと、押し直しの 1 回目では 0 を返す(1 回目は Slider 自身の
    /// <see cref="InitialStep"/> だけ動くのが正しいため)。
    /// </summary>
    /// <param name="key">押されたキー。</param>
    /// <param name="isRepeat">押しっぱなしによるキーリピートなら true。</param>
    public double GetExtraDelta(VirtualKey key, bool isRepeat)
    {
        if (Direction(key) is not { } direction)
        {
            return 0;
        }

        if (!isRepeat)
        {
            _repeatCount = 0;
        }

        return direction * (NextStep() - InitialStep);
    }

    /// <summary>キーを離したときに呼び、次の押下を初期ステップへ戻す。</summary>
    public void ReleaseKey(VirtualKey key)
    {
        if (Direction(key) is not null)
        {
            _repeatCount = 0;
        }
    }

    private double NextStep()
    {
        double step = Math.Min(MaxStep, InitialStep * Math.Pow(AccelerationFactor, _repeatCount));
        _repeatCount++;

        // Slider の刻みと同じ 0.1 度単位へ丸め、角度が半端な値にならないようにする
        return Math.Round(step, 1, MidpointRounding.AwayFromZero);
    }

    // 縦 Slider の既定と同じ割り当て(上・右で増加、下・左で減少)
    private static int? Direction(VirtualKey key) => key switch
    {
        VirtualKey.Up or VirtualKey.Right => 1,
        VirtualKey.Down or VirtualKey.Left => -1,
        _ => null,
    };
}
