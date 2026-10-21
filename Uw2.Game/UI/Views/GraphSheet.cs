using System.Windows.Media;
using System.Windows.Media.Imaging;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Views;

/// <summary>
/// <c>GRAPH.DAT</c> 그림(<c>asset/graph/NN.png</c>)을 WPF 그림으로 — 건물 임자 · 배 · 사건 그림이다.
/// 빛깔은 그림마다 제 것(NPK 머리의 열여섯)을 쓴다.
/// </summary>
public sealed class GraphSheet(AssetPack pack)
{
    private readonly Dictionary<int, ImageSource?> _cache = [];

    /// <summary>그림 한 장 — 없으면 null.</summary>
    public ImageSource? Get(int n)
    {
        if (_cache.TryGetValue(n, out var hit)) return hit;
        ImageSource? made = null;
        if (pack.Graph(n) is { } img)
        {
            var bgra = new uint[img.Width * img.Height];
            for (int i = 0; i < bgra.Length; i++)
            {
                int c = img.Pixels[i] * 3;
                bgra[i] = 0xFF000000u | (uint)img.Palette[c] << 16 | (uint)img.Palette[c + 1] << 8 | img.Palette[c + 2];
            }
            var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, bgra, img.Width * 4);
            bmp.Freeze();
            made = bmp;
        }
        return _cache[n] = made;
    }
}
