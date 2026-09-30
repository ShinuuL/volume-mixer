using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace VolumeMixer.Infrastructure;

/// <summary>
/// Arte do ícone desenhada em código (vetorial, qualquer tamanho): três faders de
/// mixer. Fonte única para o ícone do app (.ico), da bandeja e da extensão.
/// Coordenadas em fração do lado (0..1) — nada de bitmap fixo.
/// </summary>
public static class IconArt
{
    // Faders: posição X do trilho e Y (centro) do botão, em fração do lado.
    private static readonly (float X, float Knob)[] Faders = { (0.30f, 0.63f), (0.50f, 0.37f), (0.70f, 0.52f) };
    private const float TrackTop = 0.22f, TrackBottom = 0.78f;

    private static readonly Color GradientStart = Color.FromArgb(0x2B, 0x7F, 0xFF);
    private static readonly Color GradientEnd = Color.FromArgb(0x7B, 0x3F, 0xE4);
    private static readonly Color MuteRed = Color.FromArgb(0xE8, 0x3B, 0x3F);

    /// <summary>Ícone do app: quadrado arredondado em degradê + faders brancos.</summary>
    public static Bitmap RenderAppIcon(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Prepare(bmp);
        var s = (float)size;

        using (var bg = RoundedRect(0, 0, s, s, s * 0.22f))
        using (var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s), GradientStart, GradientEnd))
            g.FillPath(brush, bg);

        DrawFaders(g, s, Color.White, trackAlpha: 110, trackWidth: 0.07f, knobWidth: 0.19f, knobHeight: 0.10f);
        return bmp;
    }

    /// <summary>Ícone da bandeja: só o glifo (sem fundo), na cor do tema da barra
    /// de tarefas; no mudo fica apagado com um traço vermelho.</summary>
    public static Bitmap RenderTrayIcon(int size, bool lightTaskbar, bool muted)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Prepare(bmp);
        var s = (float)size;
        var fg = lightTaskbar ? Color.FromArgb(0x1F, 0x1F, 0x1F) : Color.White;
        if (muted) fg = Color.FromArgb(120, fg);

        // Em 16–24px o glifo ocupa o quadro inteiro e os traços são mais grossos.
        DrawFaders(g, s, fg, trackAlpha: muted ? 60 : 120, trackWidth: 0.09f, knobWidth: 0.19f, knobHeight: 0.14f,
                   inset: -0.12f);

        if (muted)
        {
            using var pen = new Pen(MuteRed, Math.Max(1.5f, s * 0.10f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, s * 0.14f, s * 0.14f, s * 0.86f, s * 0.86f);
        }
        return bmp;
    }

    private static void DrawFaders(Graphics g, float s, Color fg, int trackAlpha, float trackWidth,
                                   float knobWidth, float knobHeight, float inset = 0f)
    {
        // inset < 0 expande o desenho para as bordas (usado na bandeja).
        float Map(float v) => (0.5f + (v - 0.5f) * (1 - 2 * inset)) * s;

        using var track = new SolidBrush(Color.FromArgb(trackAlpha, fg));
        using var knob = new SolidBrush(fg);
        var tw = trackWidth * s * (1 - 2 * inset);
        var kw = knobWidth * s * (1 - 2 * inset);
        var kh = knobHeight * s * (1 - 2 * inset);
        // Em 16–32px alinha tudo à grade de pixels (hinting): sem isso trilhos de
        // ~1,5px caem entre dois pixels e o ícone fica borrado.
        var snap = s <= 32;
        if (snap)
        {
            tw = Math.Max(1, MathF.Round(tw));
            kw = Math.Max(3, MathF.Round(kw));
            kh = Math.Max(2, MathF.Round(kh));
        }
        float Px(float v) => snap ? MathF.Round(v) : v;

        foreach (var (x, k) in Faders)
        {
            var cx = Map(x);
            var top = Px(Map(TrackTop));
            using (var t = RoundedRect(Px(cx - tw / 2), top, tw, Px(Map(TrackBottom)) - top, snap ? 0 : tw / 2))
                g.FillPath(track, t);
            using var kp = RoundedRect(Px(cx - kw / 2), Px(Map(k) - kh / 2), kw, kh, snap ? kh / 3f : kh / 2.4f);
            g.FillPath(knob, kp);
        }
    }

    private static Graphics Prepare(Bitmap bmp)
    {
        var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);
        return g;
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        var d = r * 2;
        var p = new GraphicsPath();
        if (d <= 0.01f) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static byte[] ToPng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Grava um .ico multi-resolução com entradas PNG (Vista+).</summary>
    public static void WriteIco(string path, IEnumerable<int> sizes)
    {
        var images = sizes.Select(sz => { using var b = RenderAppIcon(sz); return (sz, ToPng(b)); }).ToList();
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (sz, png) in images)
        {
            w.Write((byte)(sz >= 256 ? 0 : sz)); w.Write((byte)(sz >= 256 ? 0 : sz));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(png.Length); w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) w.Write(png);
    }

    /// <summary>Regenera todos os ícones versionados (ver README):
    /// <c>VolumeMixer.exe --export-icons &lt;raiz-do-repo&gt;</c>.</summary>
    public static void ExportAll(string repoRoot)
    {
        WriteIco(Path.Combine(repoRoot, "VolumeMixer", "Assets", "VolumeMixer.ico"), new[] { 16, 20, 24, 32, 40, 48, 64, 256 });
        using (var big = RenderAppIcon(512))
            File.WriteAllBytes(Path.Combine(repoRoot, "VolumeMixer", "Assets", "icon-512.png"), ToPng(big));
        var extIcons = Path.Combine(repoRoot, "browser-extension", "icons");
        Directory.CreateDirectory(extIcons);
        foreach (var sz in new[] { 16, 32, 48, 128 })
        {
            using var b = RenderAppIcon(sz);
            File.WriteAllBytes(Path.Combine(extIcons, $"icon-{sz}.png"), ToPng(b));
        }
        foreach (var (name, light, muted) in new[] { ("tray-dark", false, false), ("tray-light", true, false), ("tray-muted", false, true) })
        {
            using var b = RenderTrayIcon(64, light, muted);
            File.WriteAllBytes(Path.Combine(repoRoot, "VolumeMixer", "Assets", $"{name}-preview.png"), ToPng(b));
        }
    }
}
