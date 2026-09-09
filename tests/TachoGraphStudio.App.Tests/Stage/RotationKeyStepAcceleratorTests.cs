using TachoGraphStudio.App.Stage;

namespace TachoGraphStudio.App.Tests.Stage;

// カーソルキーでの回転補正(#133)のステップ幅。初期値・加速・上限・リセットの契約を固定する
public sealed class RotationKeyStepAcceleratorTests
{
    [Fact]
    public void NextStep_FirstPressUsesInitialStep()
    {
        Assert.Equal(RotationKeyStepAccelerator.InitialStep, new RotationKeyStepAccelerator().NextStep());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(60)]
    public void NextStep_StaysWithinInitialAndMaxStep(int presses)
    {
        RotationKeyStepAccelerator accelerator = new();

        double step = 0;
        for (int press = 0; press < presses; press++)
        {
            step = accelerator.NextStep();
        }

        Assert.InRange(step, RotationKeyStepAccelerator.InitialStep, RotationKeyStepAccelerator.MaxStep);
    }

    // 押しっぱなしの間はステップが戻らない(縮まると操作感が不安定になる)
    [Fact]
    public void NextStep_NeverDecreasesWhileHeld()
    {
        RotationKeyStepAccelerator accelerator = new();

        double previous = accelerator.NextStep();
        for (int press = 0; press < 60; press++)
        {
            double step = accelerator.NextStep();
            Assert.True(step >= previous, $"{press} 回目でステップが縮みました: {previous} → {step}");
            previous = step;
        }
    }

    [Fact]
    public void NextStep_AcceleratesWhileHeld()
    {
        RotationKeyStepAccelerator accelerator = new();

        double first = accelerator.NextStep();
        for (int press = 0; press < 9; press++)
        {
            accelerator.NextStep();
        }

        Assert.True(accelerator.NextStep() > first);
    }

    [Fact]
    public void NextStep_ReachesMaxStepWhileHeld()
    {
        RotationKeyStepAccelerator accelerator = new();

        double step = 0;
        for (int press = 0; press < 60; press++)
        {
            step = accelerator.NextStep();
        }

        Assert.Equal(RotationKeyStepAccelerator.MaxStep, step);
    }

    [Fact]
    public void Reset_RestartsFromInitialStep()
    {
        RotationKeyStepAccelerator accelerator = new();
        for (int press = 0; press < 30; press++)
        {
            accelerator.NextStep();
        }

        accelerator.Reset();

        Assert.Equal(RotationKeyStepAccelerator.InitialStep, accelerator.NextStep());
    }

    // Slider の刻み(0.1 度)に乗らないステップだと角度が半端な値になる
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(20)]
    public void NextStep_IsRoundedToSliderStep(int presses)
    {
        RotationKeyStepAccelerator accelerator = new();

        double step = 0;
        for (int press = 0; press < presses; press++)
        {
            step = accelerator.NextStep();
        }

        Assert.Equal(step, Math.Round(step, 1));
    }
}
