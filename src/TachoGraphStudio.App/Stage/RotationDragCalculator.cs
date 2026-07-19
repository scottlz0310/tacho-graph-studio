namespace TachoGraphStudio.App.Stage;

// マウスドラッグの座標から回転角を求める WinUI 非依存の計算器。
public sealed class RotationDragCalculator
{
    private const double AngleStep = 0.5;

    private readonly double _initialAngle;
    private readonly double _startPointerAngle;
    private readonly double _centerX;
    private readonly double _centerY;

    public RotationDragCalculator(
        double initialAngle,
        double startPointerX,
        double startPointerY,
        double centerX,
        double centerY)
    {
        _initialAngle = initialAngle;
        _centerX = centerX;
        _centerY = centerY;
        _startPointerAngle = GetPointerAngle(startPointerX, startPointerY);
    }

    public double Calculate(double pointerX, double pointerY)
    {
        double pointerDelta = Normalize(GetPointerAngle(pointerX, pointerY) - _startPointerAngle);
        double angle = Normalize(_initialAngle + pointerDelta);
        return Normalize(Math.Round(angle / AngleStep, MidpointRounding.AwayFromZero) * AngleStep);
    }

    private double GetPointerAngle(double pointerX, double pointerY) =>
        Math.Atan2(pointerY - _centerY, pointerX - _centerX) * 180.0 / Math.PI;

    private static double Normalize(double angle)
    {
        double normalized = angle % 360.0;
        if (normalized > 180.0)
        {
            normalized -= 360.0;
        }
        else if (normalized < -180.0)
        {
            normalized += 360.0;
        }

        return normalized;
    }
}
