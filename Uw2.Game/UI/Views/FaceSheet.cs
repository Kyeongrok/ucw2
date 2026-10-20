using System.Windows.Media;
using System.Windows.Media.Imaging;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 얼굴 판(<c>kao/faces.png</c>)을 WPF 그림으로 — 시나리오 대사 창이 얼굴 하나씩 잘라 쓴다.
/// 빛깔은 바다 화면 빛깔표(<see cref="GamePalette.SeaScreen"/>)다.
/// </summary>
public sealed class FaceSheet
{
    private readonly byte[] _pixels;
    private readonly int _width;
    private readonly uint[] _palette = GamePalette.SeaScreen().Bgra;
    private readonly Dictionary<int, ImageSource> _cache = [];

    private readonly byte[] _finds;
    private readonly int _findsWidth;

    public FaceSheet(byte[] pixels, int width, byte[]? finds = null, int findsWidth = 0)
    {
        _pixels = pixels; _width = width; _finds = finds ?? []; _findsWidth = findsWidth;
    }

    /// <summary>발견물 그림은 얼굴 번호 이 뒤로 이어 붙인다(원본 KAO 의 128 ~ 255).</summary>
    public const int FindFirst = KaoImages.FaceCount;

    /// <summary>얼굴 하나 — 판 밖이면 null. <see cref="FindFirst"/> 부터는 발견물 그림(48 x 48)이다.</summary>
    public ImageSource? Get(int face)
    {
        if (face < 0 || face >= FindFirst + KaoImages.FindCount) return null;
        if (_cache.TryGetValue(face, out var hit)) return hit;
        bool find = face >= FindFirst;
        int k = find ? face - FindFirst : face;
        int w = find ? KaoImages.FindW : KaoImages.FaceW, h = find ? KaoImages.FindH : KaoImages.FaceH;
        int sx = k % AssetPack.KaoCols * w, sy = k / AssetPack.KaoCols * h;
        var src = find ? _finds : _pixels;
        int width = find ? _findsWidth : _width;
        if (width <= 0 || (sy + h) * width > src.Length) return null;
        var bgra = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bgra[y * w + x] = _palette[src[(sy + y) * width + sx + x]] | 0xFF000000;
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();
        return _cache[face] = bmp;
    }
}
