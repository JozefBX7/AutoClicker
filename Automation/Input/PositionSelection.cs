namespace AutoClicker;

internal readonly record struct PositionSelection(bool FixedPosition, int X, int Y)
{
    internal static PositionSelection FromPickedPoint(int x, int y) => new(true, x, y);
}