using System.Drawing;

namespace VolumeMixer.Infrastructure;

internal static class TrayIconFactory
{
    /// <summary>Alto-falante simples desenhado em 16x16; evita asset binário.</summary>
    public static Icon Create()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(Color.FromArgb(0, 120, 215));
        // caixa do alto-falante
        g.FillPolygon(brush, new[] { new Point(2, 6), new Point(6, 6), new Point(10, 2), new Point(10, 14), new Point(6, 10), new Point(2, 10) });
        // ondas
        using var pen = new Pen(brush, 1.6f);
        g.DrawArc(pen, 10, 4, 4, 8, -60, 120);
        g.DrawArc(pen, 12, 2, 6, 12, -60, 120);
        return Icon.FromHandle(bmp.GetHicon());
    }
}
