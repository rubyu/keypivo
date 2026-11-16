using System.Drawing;

namespace Keypivo.Core;

public static class WindowPlacement
{
    public static Point Calculate(Rectangle workingArea, Size windowSize, DisplayPosition position, int margin = 16)
    {
        var x = position == DisplayPosition.Center
            ? workingArea.Left + (workingArea.Width - windowSize.Width) / 2
            : workingArea.Right - windowSize.Width - margin;
        var y = position == DisplayPosition.Center
            ? workingArea.Top + (workingArea.Height - windowSize.Height) / 2
            : workingArea.Bottom - windowSize.Height - margin;
        return new(
            Math.Clamp(x, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - windowSize.Width)),
            Math.Clamp(y, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - windowSize.Height)));
    }
}
