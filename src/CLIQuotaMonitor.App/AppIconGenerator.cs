using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace CLIQuotaMonitor.App;

public static class AppIconGenerator
{
    private static Icon? _cachedIcon;
    private static BitmapSource? _cachedImageSource;

    public static Icon GetAppIcon()
    {
        if (_cachedIcon is not null)
        {
            return _cachedIcon;
        }

        using var bitmap = CreateBitmap(32);
        var hIcon = bitmap.GetHicon();
        _cachedIcon = Icon.FromHandle(hIcon);
        return _cachedIcon;
    }

    public static BitmapSource GetAppImageSource()
    {
        if (_cachedImageSource is not null)
        {
            return _cachedImageSource;
        }

        var icon = GetAppIcon();
        _cachedImageSource = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            System.Windows.Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        _cachedImageSource.Freeze();
        return _cachedImageSource;
    }

    private static Bitmap CreateBitmap(int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Clear transparent
        g.Clear(System.Drawing.Color.Transparent);

        // 1. Dark Rounded Badge
        var badgeRect = new RectangleF(1f, 1f, size - 2f, size - 2f);
        using var badgePath = CreateRoundedRectanglePath(badgeRect, 6f);
        using var badgeBrush = new SolidBrush(System.Drawing.Color.FromArgb(245, 15, 23, 42)); // Slate 900
        using var borderPen = new Pen(System.Drawing.Color.FromArgb(200, 51, 65, 85), 1.2f); // Slate 700
        g.FillPath(badgeBrush, badgePath);
        g.DrawPath(borderPen, badgePath);

        // 2. Circular Quota Gauge Arc (75% filled, Neon Cyan to Green)
        var arcRect = new RectangleF(3.5f, 3.5f, size - 7f, size - 7f);
        using var arcTrackPen = new Pen(System.Drawing.Color.FromArgb(60, 30, 41, 59), 2f);
        g.DrawArc(arcTrackPen, arcRect, 0, 360);

        using var arcActivePen = new Pen(System.Drawing.Color.FromArgb(255, 56, 189, 248), 2.2f);
        arcActivePen.StartCap = LineCap.Round;
        arcActivePen.EndCap = LineCap.Round;
        g.DrawArc(arcActivePen, arcRect, 135, 260);

        // 3. CLI Prompt Glyphs (> _)
        var scale = size / 32f;
        using var promptPen = new Pen(System.Drawing.Color.FromArgb(255, 56, 189, 248), 2.2f * scale);
        promptPen.StartCap = LineCap.Round;
        promptPen.EndCap = LineCap.Round;
        promptPen.LineJoin = LineJoin.Round;

        // Arrow '>'
        var p1 = new PointF(10f * scale, 11.5f * scale);
        var p2 = new PointF(16.5f * scale, 16f * scale);
        var p3 = new PointF(10f * scale, 20.5f * scale);
        g.DrawLines(promptPen, [p1, p2, p3]);

        // Underscore '_' in emerald green
        using var cursorPen = new Pen(System.Drawing.Color.FromArgb(255, 52, 211, 153), 2.2f * scale);
        cursorPen.StartCap = LineCap.Round;
        cursorPen.EndCap = LineCap.Round;
        g.DrawLine(cursorPen, 18.5f * scale, 20.5f * scale, 24f * scale, 20.5f * scale);

        return bitmap;
    }

    private static GraphicsPath CreateRoundedRectanglePath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var size = new SizeF(diameter, diameter);
        var arc = new RectangleF(rect.Location, size);

        // Top left
        path.AddArc(arc, 180, 90);
        // Top right
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        // Bottom right
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        // Bottom left
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
