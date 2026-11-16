using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Keypivo.Core;

namespace Keypivo;

internal sealed class TrayIcons : IDisposable
{
    private readonly Dictionary<ConnectionState, Icon> _icons = [];
    public Icon For(ConnectionState state)
    {
        if (_icons.TryGetValue(state, out var icon)) return icon;
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var background = new SolidBrush(StatePresentation.Color(state));
        graphics.FillEllipse(background, 1, 1, 30, 30);
        using var pen = new Pen(Color.White, 3.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(pen, 10, 8, 10, 24);
        graphics.DrawLines(pen, [new Point(22, 8), new Point(10, 17), new Point(22, 24)]);
        var handle = bitmap.GetHicon();
        try
        {
            using var nativeIcon = Icon.FromHandle(handle);
            icon = (Icon)nativeIcon.Clone();
        }
        finally { DestroyIcon(handle); }
        _icons.Add(state, icon);
        return icon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        foreach (var icon in _icons.Values) icon.Dispose();
        _icons.Clear();
    }
}
