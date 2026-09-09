using TachoGraphStudio.App.Stage;

using Windows.System;

namespace TachoGraphStudio.App.Tests.Stage;

// カーソルキーでの回転補正(#133)。キーの割り当て・初期ステップ・加速・上限・リセットを固定する。
// 「Slider 自身が SmallChange 分を動かし、この計算器は差だけを返す」契約が前提
public sealed class RotationKeyStepCalculatorTests
{
    // 押し直しの 1 回目は Slider の SmallChange だけで初期ステップぶん動くため、上乗せは 0
    [Theory]
    [InlineData(VirtualKey.Up)]
    [InlineData(VirtualKey.Down)]
    [InlineData(VirtualKey.Left)]
    [InlineData(VirtualKey.Right)]
    public void GetExtraDelta_FirstPressAddsNothing(VirtualKey key)
    {
        Assert.Equal(0, new RotationKeyStepCalculator().GetExtraDelta(key, isRepeat: false));
    }

    [Theory]
    [InlineData(VirtualKey.Enter)]
    [InlineData(VirtualKey.Space)]
    [InlineData(VirtualKey.A)]
    [InlineData(VirtualKey.PageUp)]
    public void GetExtraDelta_IgnoresKeysNotUsedForRotation(VirtualKey key)
    {
        RotationKeyStepCalculator calculator = new();

        Assert.Equal(0, calculator.GetExtraDelta(key, isRepeat: false));
        Assert.Equal(0, calculator.GetExtraDelta(key, isRepeat: true));
    }

    // 縦 Slider の既定と同じ割り当て(上・右で増加、下・左で減少)
    [Theory]
    [InlineData(VirtualKey.Up, 1)]
    [InlineData(VirtualKey.Right, 1)]
    [InlineData(VirtualKey.Down, -1)]
    [InlineData(VirtualKey.Left, -1)]
    public void GetExtraDelta_UsesSliderKeyDirections(VirtualKey key, int expectedSign)
    {
        RotationKeyStepCalculator calculator = new();
        calculator.GetExtraDelta(key, isRepeat: false);

        double delta = 0;
        for (int repeat = 0; repeat < 10; repeat++)
        {
            delta = calculator.GetExtraDelta(key, isRepeat: true);
        }

        Assert.Equal(expectedSign, Math.Sign(delta));
    }

    [Fact]
    public void GetExtraDelta_AcceleratesWhileRepeating()
    {
        RotationKeyStepCalculator calculator = new();
        calculator.GetExtraDelta(VirtualKey.Up, isRepeat: false);

        double first = calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true);
        for (int repeat = 0; repeat < 9; repeat++)
        {
            calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true);
        }

        Assert.True(calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true) > first);
    }

    // 押しっぱなしの間にステップが縮むと操作感が不安定になる
    [Fact]
    public void GetExtraDelta_NeverDecreasesWhileRepeating()
    {
        RotationKeyStepCalculator calculator = new();

        double previous = calculator.GetExtraDelta(VirtualKey.Up, isRepeat: false);
        for (int repeat = 0; repeat < 60; repeat++)
        {
            double delta = calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true);
            Assert.True(delta >= previous, $"{repeat} 回目でステップが縮みました: {previous} → {delta}");
            previous = delta;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(60)]
    public void GetExtraDelta_KeepsTotalStepWithinMaxStep(int repeats)
    {
        RotationKeyStepCalculator calculator = new();
        calculator.GetExtraDelta(VirtualKey.Up, isRepeat: false);

        double delta = 0;
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            delta = calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true);
        }

        // Slider 自身が動かす初期ステップと合わせた 1 押下ぶんが上限に収まる
        Assert.InRange(
            delta + RotationKeyStepCalculator.InitialStep,
            RotationKeyStepCalculator.InitialStep,
            RotationKeyStepCalculator.MaxStep);
    }

    [Fact]
    public void GetExtraDelta_ReachesMaxStepWhileRepeating()
    {
        RotationKeyStepCalculator calculator = new();
        calculator.GetExtraDelta(VirtualKey.Up, isRepeat: false);

        double delta = 0;
        for (int repeat = 0; repeat < 60; repeat++)
        {
            delta = calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true);
        }

        Assert.Equal(
            RotationKeyStepCalculator.MaxStep - RotationKeyStepCalculator.InitialStep,
            delta,
            precision: 10);
    }

    [Fact]
    public void GetExtraDelta_NonRepeatPressRestartsFromInitialStep()
    {
        RotationKeyStepCalculator calculator = new();
        for (int repeat = 0; repeat < 30; repeat++)
        {
            calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true);
        }

        Assert.Equal(0, calculator.GetExtraDelta(VirtualKey.Up, isRepeat: false));
    }

    [Fact]
    public void ReleaseKey_RestartsFromInitialStep()
    {
        RotationKeyStepCalculator calculator = new();
        for (int repeat = 0; repeat < 30; repeat++)
        {
            calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true);
        }

        calculator.ReleaseKey(VirtualKey.Up);

        Assert.Equal(0, calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true));
    }

    // 回転に使わないキーを離しても加速段階は保持する
    [Fact]
    public void ReleaseKey_IgnoresKeysNotUsedForRotation()
    {
        RotationKeyStepCalculator calculator = new();
        calculator.GetExtraDelta(VirtualKey.Up, isRepeat: false);
        double before = calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true);

        calculator.ReleaseKey(VirtualKey.Enter);

        Assert.True(calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true) >= before);
    }

    // Slider の刻み(0.1 度)に乗らない値だと角度が半端になる
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(20)]
    public void GetExtraDelta_IsRoundedToSliderStep(int repeats)
    {
        RotationKeyStepCalculator calculator = new();
        calculator.GetExtraDelta(VirtualKey.Up, isRepeat: false);

        double delta = 0;
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            delta = calculator.GetExtraDelta(VirtualKey.Up, isRepeat: true);
        }

        // Slider 自身の初期ステップと合わせた 1 押下ぶんが 0.1 度の倍数になる
        double step = delta + RotationKeyStepCalculator.InitialStep;
        Assert.Equal(Math.Round(step, 1), step, precision: 10);
    }
}
