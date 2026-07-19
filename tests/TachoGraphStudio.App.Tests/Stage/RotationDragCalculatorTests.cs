using TachoGraphStudio.App.Stage;

namespace TachoGraphStudio.App.Tests.Stage;

public sealed class RotationDragCalculatorTests
{
    [Theory]
    [InlineData(1.0, 0.0, 0.0, 1.0, 90.0)]
    [InlineData(0.0, 1.0, -1.0, 0.0, 90.0)]
    [InlineData(-1.0, 0.0, 0.0, -1.0, 90.0)]
    [InlineData(0.0, -1.0, 1.0, 0.0, 90.0)]
    [InlineData(1.0, 0.0, 0.0, -1.0, -90.0)]
    public void Calculate_AcrossQuadrantsReturnsSignedClockwiseDelta(
        double startX,
        double startY,
        double currentX,
        double currentY,
        double expected)
    {
        RotationDragCalculator calculator = new(0.0, startX, startY, 0.0, 0.0);

        Assert.Equal(expected, calculator.Calculate(currentX, currentY));
    }

    [Theory]
    [InlineData(170.0, 0.0, 20.0, -170.0)]
    [InlineData(-170.0, 0.0, -20.0, 170.0)]
    [InlineData(0.0, 170.0, -170.0, 20.0)]
    [InlineData(0.0, -170.0, 170.0, -20.0)]
    [InlineData(180.0, 0.0, 0.0, 180.0)]
    [InlineData(-180.0, 0.0, 0.0, -180.0)]
    public void Calculate_AtSignedBoundaryWrapsWithinContractRange(
        double initialAngle,
        double startPointerAngle,
        double pointerAngle,
        double expected)
    {
        RotationDragCalculator calculator = new(
            initialAngle,
            PointX(startPointerAngle),
            PointY(startPointerAngle),
            0.0,
            0.0);

        Assert.Equal(expected, calculator.Calculate(PointX(pointerAngle), PointY(pointerAngle)));
    }

    [Theory]
    [InlineData(45.0, 0.0, 45.0)]
    [InlineData(45.0, 90.0, 135.0)]
    [InlineData(-45.0, -90.0, -135.0)]
    public void Calculate_PreservesInitialAngleAndAddsPointerDelta(
        double initialAngle,
        double pointerAngle,
        double expected)
    {
        RotationDragCalculator calculator = new(initialAngle, 1.0, 0.0, 0.0, 0.0);

        Assert.Equal(expected, calculator.Calculate(PointX(pointerAngle), PointY(pointerAngle)));
    }

    [Theory]
    [InlineData(10.24, 10.0)]
    [InlineData(10.26, 10.5)]
    [InlineData(-10.24, -10.0)]
    [InlineData(-10.26, -10.5)]
    public void Calculate_RoundsToHalfDegree(double pointerAngle, double expected)
    {
        RotationDragCalculator calculator = new(0.0, 1.0, 0.0, 0.0, 0.0);

        Assert.Equal(expected, calculator.Calculate(PointX(pointerAngle), PointY(pointerAngle)));
    }

    private static double PointX(double angle) => Math.Cos(angle * Math.PI / 180.0);

    private static double PointY(double angle) => Math.Sin(angle * Math.PI / 180.0);
}
