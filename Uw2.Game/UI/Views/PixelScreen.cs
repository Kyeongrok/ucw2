using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 게임 화면 하나를 점으로 찍는 판. <b>640 x 400</b> 에 그린 뒤 통째로 늘여 띄운다.
/// </summary>
/// <remarks>
/// 원본이 VGA 640x400 이라 그 크기로 그리고 <see cref="BitmapScalingMode.NearestNeighbor"/> 로
/// 늘인다 — 점이 뭉개지지 않아 옛 화면 그대로 보인다. 글자는 게임 글꼴
/// (<see cref="GameFont"/>)을 그대로 찍는다.
/// </remarks>
public sealed class PixelScreen
{
    /// <summary>원본 화면 크기.</summary>
    public const int Width = 640, Height = 400;

    private readonly uint[] _px = new uint[Width * Height];
    private readonly WriteableBitmap _bmp =
        new(Width, Height, 96, 96, PixelFormats.Bgra32, null);

    /// <summary>화면에 붙일 그림. <see cref="Present"/> 를 불러야 갱신된다.</summary>
    public ImageSource Source => _bmp;

    /// <summary>글자를 찍을 글꼴. 없으면 글자가 안 나온다.</summary>
    public GameFont? Font { get; set; }

    /// <summary>온 화면을 한 색으로 지운다.</summary>
    public void Clear(uint bgra)
    {
        for (int i = 0; i < _px.Length; i++) _px[i] = bgra;
    }

    /// <summary>점 하나.</summary>
    public void Dot(int x, int y, uint bgra)
    {
        if ((uint)x < Width && (uint)y < Height) _px[y * Width + x] = bgra;
    }

    /// <summary>속을 채운 네모.</summary>
    public void Fill(int x, int y, int w, int h, uint bgra)
    {
        for (int yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++)
            for (int xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++)
                _px[yy * Width + xx] = bgra;
    }

    /// <summary>테두리만 그린 네모.</summary>
    public void Rect(int x, int y, int w, int h, uint bgra, int thick = 1)
    {
        Fill(x, y, w, thick, bgra);
        Fill(x, y + h - thick, w, thick, bgra);
        Fill(x, y, thick, h, bgra);
        Fill(x + w - thick, y, thick, h, bgra);
    }

    /// <summary>
    /// 게임 첫 화면의 테두리. <b>주황·초록·주황</b> 세 줄에 네 귀 무늬가 붙는다.
    /// </summary>
    public void GameFrame(int x, int y, int w, int h, uint outer, uint middle, uint fill)
    {
        Fill(x, y, w, h, fill);
        Rect(x, y, w, h, outer, 2);
        Rect(x + 2, y + 2, w - 4, h - 4, middle, 2);
        Rect(x + 4, y + 4, w - 8, h - 8, outer, 2);

        // 네 귀 무늬 — 원본의 꽃 모양을 점으로 흉내 낸다.
        foreach (var (cx, cy) in new[] { (x, y), (x + w - 8, y), (x, y + h - 8), (x + w - 8, y + h - 8) })
        {
            Fill(cx, cy, 8, 8, outer);
            Fill(cx + 2, cy + 2, 4, 4, middle);
            Dot(cx + 3, cy + 3, outer);
            Dot(cx + 4, cy + 4, outer);
        }
    }

    /// <summary>
    /// 글줄 하나. <paramref name="scale"/> 로 늘이고 <paramref name="tracking"/> 만큼 띄운다.
    /// </summary>
    public void Text(string text, int x, int y, uint ink, int scale = 1, int tracking = 0)
    {
        if (Font == null) return;
        int cx = x;
        foreach (char c in text)
        {
            for (int gy = 0; gy < GameFont.Height; gy++)
                for (int gx = 0; gx < GameFont.Width; gx++)
                    if (Font.Dot(c, gx, gy))
                        Fill(cx + gx * scale, y + gy * scale, scale, scale, ink);
            cx += GameFont.Width * scale + tracking;
        }
    }

    /// <summary>그 글줄이 차지하는 너비(점).</summary>
    public static int TextWidth(string text, int scale, int tracking) =>
        text.Length * (GameFont.Width * scale + tracking) - tracking;

    /// <summary>가운데 맞춤으로 찍는다.</summary>
    public void TextCentered(string text, int centerX, int y, uint ink, int scale = 1, int tracking = 0) =>
        Text(text, centerX - TextWidth(text, scale, tracking) / 2, y, ink, scale, tracking);

    /// <summary>색인 그림 한 조각을 그대로 옮겨 찍는다.</summary>
    public void Blit(byte[] indices, int srcW, int sx, int sy, int w, int h,
                     int dx, int dy, uint[] palette, int scale = 1)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (sy + y) * srcW + sx + x;
                if (i < 0 || i >= indices.Length) continue;
                Fill(dx + x * scale, dy + y * scale, scale, scale, palette[indices[i] & 0xF]);
            }
    }

    /// <summary>찍은 것을 그림에 올린다.</summary>
    public void Present()
    {
        _bmp.Lock();
        System.Runtime.InteropServices.Marshal.Copy(
            Array.ConvertAll(_px, v => unchecked((int)v)), 0, _bmp.BackBuffer, _px.Length);
        _bmp.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
        _bmp.Unlock();
    }
}
