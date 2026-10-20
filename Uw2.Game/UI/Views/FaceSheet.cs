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

    public FaceSheet(byte[] pixels, int width) { _pixels = pixels; _width = width; }

    /// <summary>얼굴 하나 — 판 밖이면 null.</summary>
    public ImageSource? Get(int face)
    {
        if (face < 0 || face >= KaoImages.FaceCount) return null;
        if (_cache.TryGetValue(face, out var hit)) return hit;
        var (sx, sy) = AssetPack.FaceAt(face);
        int w = KaoImages.FaceW, h = KaoImages.FaceH;
        if (_width <= 0 || (sy + h) * _width > _pixels.Length) return null;
        var bgra = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bgra[y * w + x] = _palette[_pixels[(sy + y) * _width + sx + x]] | 0xFF000000;
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();
        return _cache[face] = bmp;
    }
}
