using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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

    public static void SaveIcoFile(string path, int[]? sizes = null)
    {
        sizes ??= [16, 24, 32, 48, 64, 128, 256];
        var pngBytesList = new List<byte[]>();
        foreach (var s in sizes)
        {
            using var bmp = CreateBitmap(s);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngBytesList.Add(ms.ToArray());
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICONDIR
        bw.Write((ushort)0); // Reserved
        bw.Write((ushort)1); // Type: 1 = ICO
        bw.Write((ushort)sizes.Length); // Image count

        var offset = 6 + (sizes.Length * 16);
        for (var i = 0; i < sizes.Length; i++)
        {
            var s = sizes[i];
            var data = pngBytesList[i];
            bw.Write((byte)(s >= 256 ? 0 : s)); // Width
            bw.Write((byte)(s >= 256 ? 0 : s)); // Height
            bw.Write((byte)0); // Colors
            bw.Write((byte)0); // Reserved
            bw.Write((ushort)1); // Planes
            bw.Write((ushort)32); // Bit count
            bw.Write((uint)data.Length); // Bytes in resource
            bw.Write((uint)offset); // Offset
            offset += data.Length;
        }

        foreach (var data in pngBytesList)
        {
            bw.Write(data);
        }
    }

    public static Bitmap CreateBitmap(int size)
    {
        var scale = size / 32f;
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Clear transparent
        g.Clear(System.Drawing.Color.Transparent);

        // 1. Dark Rounded Badge
        var badgeRect = new RectangleF(1f * scale, 1f * scale, size - 2f * scale, size - 2f * scale);
        using var badgePath = CreateRoundedRectanglePath(badgeRect, 6f * scale);
        using var badgeBrush = new SolidBrush(System.Drawing.Color.FromArgb(245, 15, 23, 42)); // Slate 900
        using var borderPen = new Pen(System.Drawing.Color.FromArgb(200, 51, 65, 85), Math.Max(1f, 1.2f * scale)); // Slate 700
        g.FillPath(badgeBrush, badgePath);
        g.DrawPath(borderPen, badgePath);

        // 2. Circular Quota Gauge Arc (75% filled, Neon Cyan to Green)
        var arcRect = new RectangleF(3.5f * scale, 3.5f * scale, size - 7f * scale, size - 7f * scale);
        using var arcTrackPen = new Pen(System.Drawing.Color.FromArgb(60, 30, 41, 59), Math.Max(1f, 2f * scale));
        g.DrawArc(arcTrackPen, arcRect, 0, 360);

        using var arcActivePen = new Pen(System.Drawing.Color.FromArgb(255, 56, 189, 248), Math.Max(1.2f, 2.2f * scale));
        arcActivePen.StartCap = LineCap.Round;
        arcActivePen.EndCap = LineCap.Round;
        g.DrawArc(arcActivePen, arcRect, 135, 260);

        // 3. CLI Prompt Glyphs (> _)
        using var promptPen = new Pen(System.Drawing.Color.FromArgb(255, 56, 189, 248), Math.Max(1.2f, 2.2f * scale));
        promptPen.StartCap = LineCap.Round;
        promptPen.EndCap = LineCap.Round;
        promptPen.LineJoin = LineJoin.Round;

        // Arrow '>'
        var p1 = new PointF(10f * scale, 11.5f * scale);
        var p2 = new PointF(16.5f * scale, 16f * scale);
        var p3 = new PointF(10f * scale, 20.5f * scale);
        g.DrawLines(promptPen, [p1, p2, p3]);

        // Underscore '_' in emerald green
        using var cursorPen = new Pen(System.Drawing.Color.FromArgb(255, 52, 211, 153), Math.Max(1.2f, 2.2f * scale));
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
