using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PressHistory.Services;

internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var accentBrush = new SolidBrush(Color.FromArgb(101, 88, 217));
            graphics.FillEllipse(accentBrush, 1, 1, 30, 30);

            using var sheetBrush = new SolidBrush(Color.White);
            using var sheetPath = CreateRoundedRectangle(new RectangleF(9, 8, 15, 18), 3);
            graphics.FillPath(sheetBrush, sheetPath);

            using var clipBrush = new SolidBrush(Color.FromArgb(101, 88, 217));
            using var clipPath = CreateRoundedRectangle(new RectangleF(12, 5, 9, 6), 2);
            graphics.FillPath(clipBrush, clipPath);

            using var linePen = new Pen(Color.FromArgb(101, 88, 217), 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(linePen, 12, 15, 21, 15);
            graphics.DrawLine(linePen, 12, 19, 21, 19);
        }

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var temporaryIcon = Icon.FromHandle(iconHandle);
            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF rectangle, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
