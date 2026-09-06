using System.Runtime.InteropServices;
using Uw2.Support.Local.Formats;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Uw2.Game.UI.Rendering;

/// <summary>
/// 세계지도를 Direct3D 11 로 그린다. 칸 하나가 화면 몇 픽셀이든 픽셀 셰이더가 그때 칩에서
/// 점을 뽑으므로 확대해도 원본 그림이 그대로 나온다.
/// </summary>
/// <remarks>
/// 3편 <c>CdsHelper.Game/UI/Rendering/MapD3DRenderer</c> 의 얼개를 그대로 옮겼다. 2편 쪽이
/// 오히려 가볍다 — 칩 지도가 2160x1080 이고 칩이 256장, 색이 열여섯이다.
///
/// <para>텍스처 넷</para>
/// <list type="bullet">
///   <item>칩 지도 — 2160x1080 <c>R8_UINT</c>. 칩 번호 한 바이트씩
///         (<see cref="ChipMap.Expand"/> 가 낸 것).</item>
///   <item>칩 그림 — <c>R8_UINT</c>. 16x16 칩을 가로 열여섯씩 늘어놓은 것.</item>
///   <item>팔레트 — 16x1 BGRA.</item>
///   <item>민색표 — 256x1 BGRA. 갈래를 눈으로 가르려고 칩 번호마다 한 색을 둔 것이다.</item>
/// </list>
///
/// 셰이더 본문은 ASCII 로만 적는다. 한글 주석을 넣으면 컴파일이 깨진다 — D3DCompile 이
/// 원본을 cp949 로 받는데 한글 음절의 끝바이트가 <c>0x5C</c>('\')인 것이 많아 줄 끝에
/// 오면 줄이음으로 먹힌다. 설명은 여기 바깥에 둔다.
/// </remarks>
public sealed unsafe class SeaMapRenderer : IDisposable
{
    /// <summary>칩 아틀라스 한 줄에 놓는 칩 수.</summary>
    public const int AtlasCols = 16;

    /// <summary>민색으로 칠할 때 쓰는 값 수.</summary>
    public const int FlatColors = 256;

    //   VS  정점 버퍼 없이 삼각형 하나로 화면을 덮는다.
    //   PS  화면 점 -> 칸 -> 칸 값 -> (민색 또는 칩 안 점 -> 팔레트) 순으로 짚는다.
    //       cell.x 를 MapCells.x 로 나눈 나머지로 접어 경도 -180/180 을 잇는다.
    //       Mode 0 이면 민색표, 1 이면 칩을 쓴다.
    //       배 그림(ShipRect)이 있으면 그 자리는 지도 대신 배를 낸다.
    //       ShipSize.z 가 켜져 있으면 배를 좌우로 뒤집는다(서쪽으로 갈 때).
    private const string ShaderSource = """
        Texture2D<uint>   CellMap : register(t0);
        Texture2D<uint>   Atlas   : register(t1);
        Texture2D<float4> Palette : register(t2);
        Texture2D<float4> Flat    : register(t3);
        Texture2D<float4> Ship    : register(t4);

        cbuffer Frame : register(b0)
        {
            float2 OriginCell;
            float2 CellPerPixel;
            float2 MapCells;
            float  Mode;
            float  AtlasCols;
            float4 ShipRect;
            float4 Grid;
            float4 ShipSize;
            float4 SpriteSrc;
        };

        struct VSOut { float4 pos : SV_Position; };

        VSOut VS(uint id : SV_VertexID)
        {
            VSOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }

        float4 PS(VSOut i) : SV_Target
        {
            if (ShipRect.z > 0)
            {
                float2 s = (i.pos.xy - ShipRect.xy) / ShipRect.zw;
                if (all(s >= 0) && all(s < 1))
                {
                    float2 t = s;
                    if (ShipSize.z > 0.5) t.x = 1.0 - t.x;
                    int2 at = int2(SpriteSrc.xy + t * SpriteSrc.zw);
                    float4 c = Ship.Load(int3(at, 0));
                    if (c.a > 0) return c;
                }
            }

            float2 cell = OriginCell + i.pos.xy * CellPerPixel;
            if (Grid.z > 0.5)
                cell.x = cell.x - floor(cell.x / MapCells.x) * MapCells.x;
            else if (cell.x < 0 || cell.x >= MapCells.x)
                return float4(0.02, 0.03, 0.06, 1);
            if (cell.y < 0 || cell.y >= MapCells.y) return float4(0.02, 0.03, 0.06, 1);

            int2 c = int2(cell);
            uint v = CellMap.Load(int3(c, 0));

            float4 col;
            if (Mode < 0.5)
            {
                col = Flat.Load(int3(int(v), 0, 0));
            }
            else
            {
                int cols = int(AtlasCols);
                int2 org = int2(int(v) % cols, int(v) / cols) * 16;
                float2 f = frac(cell);
                uint p = Atlas.Load(int3(org + int2(f * 16.0), 0));
                col = Palette.Load(int3(int(p), 0, 0));
            }

            if (Grid.x > 0.5)
            {
                float2 g = frac(cell / Grid.y);
                float2 px = CellPerPixel / Grid.y;
                if (g.x < px.x || g.y < px.y) col.rgb = lerp(col.rgb, float3(1, 1, 1), 0.25);
            }
            return col;
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameCb
    {
        public float OriginCellX, OriginCellY;
        public float CellPerPixelX, CellPerPixelY;
        public float MapCellsX, MapCellsY;
        public float Mode;
        public float AtlasCols;
        public float ShipX, ShipY, ShipW, ShipH;
        public float GridOn, GridStep, WrapX, GridPad1;
        public float SpriteW, SpriteH, ShipFlip, ShipPad1;
        public float SrcX, SrcY, SrcW, SrcH;
    }

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _ctx = null!;
    private ID3D11VertexShader _vs = null!;
    private ID3D11PixelShader _ps = null!;
    private ID3D11Buffer _cb = null!;
    private ID3D11ShaderResourceView _cellSrv = null!;
    private ID3D11ShaderResourceView _atlasSrv = null!;
    private ID3D11ShaderResourceView _paletteSrv = null!;
    private ID3D11ShaderResourceView _flatSrv = null!;
    private ID3D11ShaderResourceView _shipSrv = null!;
    private int _atlasCols = AtlasCols;

    /// <summary>스왑체인을 걸 때 쓴다.</summary>
    public ID3D11Device Device => _device;

    /// <summary>칩 그림으로 그릴지(<c>true</c>), 칩 번호마다 민색으로 칠할지(<c>false</c>).</summary>
    /// <remarks>
    /// 칩으로 그리면 게임 화면과 같은 그림이 나온다(<see cref="GamePalette.SeaScreen"/>).
    /// 민색은 갈래를 눈으로 가르려고 남겨 둔 것이다 — 바다·뭍·그 밖이 한 색씩이라
    /// 지형이 한눈에 들어온다.
    /// </remarks>
    public bool UseChips { get; set; } = true;

    /// <summary>격자를 얹을지. 눈으로 자리를 재려고 둔 것이다.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>격자 눈금(칸). 세계지도는 조각 하나(칸 12개 = 칩 24개), 항구는 칩 여덟이다.</summary>
    public float GridStep { get; set; } = WorldMap.Tile * ChipMap.PerCell;

    /// <summary>
    /// 가로로 둘러 감을지. <b>세계지도는 감고(경도 -180/180 잇기) 항구는 안 감는다.</b>
    /// </summary>
    public bool WrapX { get; set; } = true;

    /// <summary>칸 지도의 크기(칩 단위).</summary>
    public int MapW { get; private set; } = ChipMap.Width;
    public int MapH { get; private set; } = ChipMap.Height;

    /// <summary>지금 걸린 칸(칩 번호) 배열. 항구를 보다가 세계지도로 돌아올 때 쓴다.</summary>
    public byte[] Cells { get; private set; } = [];

    /// <summary>
    /// 장치와 텍스처를 올린다. <paramref name="cells"/> 는 <b>칩 번호</b> 배열이다 —
    /// <see cref="ChipMap.Expand"/> 가 낸 2160 x 1080 을 그대로 넣는다.
    /// </summary>
    public void Initialize(byte[] cells, int width, int height)
    {
        MapW = width;
        MapH = height;
        var flags = DeviceCreationFlags.BgraSupport;
        var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        // out 인자의 형을 적어 둬야 한다 — var 로 두면 FeatureLevel 을 내는 오버로드와 헷갈린다.
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels,
                                out ID3D11Device dev, out ID3D11DeviceContext ctx).CheckError();
        _device = dev;
        _ctx = ctx;

        var vsBlob = Compiler.Compile(ShaderSource, "VS", "seamap.hlsl", "vs_4_0");
        var psBlob = Compiler.Compile(ShaderSource, "PS", "seamap.hlsl", "ps_4_0");
        _vs = _device.CreateVertexShader(vsBlob.Span);
        _ps = _device.CreatePixelShader(psBlob.Span);

        _cb = _device.CreateBuffer((uint)Marshal.SizeOf<FrameCb>(), BindFlags.ConstantBuffer,
                                   ResourceUsage.Dynamic, CpuAccessFlags.Write);

        Cells = cells;
        _cellSrv = CreateImmutable(cells, width, height, Format.R8_UInt, 1);
        _flatSrv = CreateImmutable(FlatColors_(), FlatColors, 1, Format.B8G8R8A8_UNorm, sizeof(uint));
        SetPalette(GamePalette.SeaScreen());
        // 빈 아틀라스도 칩 수만큼 잡아 둔다. 작게 잡으면 셰이더가 텍스처 밖을 짚어
        // 죄다 색인 0(검정)이 나온다 — 화면이 까매지던 것이 이것이었다.
        SetChipsCore(new byte[AtlasCols * ChipSheet.Size * (WorldChips.Count / AtlasCols) * ChipSheet.Size],
                     AtlasCols * ChipSheet.Size, WorldChips.Count / AtlasCols * ChipSheet.Size, AtlasCols);
        SetShipSprite(new uint[1], 1, 1);
    }

    /// <summary>
    /// 칩 번호마다 칠할 민색. 바다 칩은 남색, 민뭍 칩은 미색, 나머지(강·산·숲·사막·얼음)는
    /// 붉은 기다 — 지형 갈래를 눈으로 보려고 둔 것이다.
    /// </summary>
    private static uint[] FlatColors_()
    {
        var c = new uint[FlatColors];
        for (int v = 0; v < FlatColors; v++)
        {
            if (v == ChipMap.SeaChip) c[v] = 0xFF17376F;
            else if (v == ChipMap.LandChip) c[v] = 0xFFE2D6B0;
            else
            {
                // 칩 번호가 클수록 짙게 — 갈래를 눈으로 가르라고 둔 것이다.
                double t = Math.Clamp((v - ChipMap.MinChip) / 200.0, 0, 1);
                byte r = (byte)(0xB0 - 0x40 * t);
                byte g = (byte)(0x5C + 0x30 * t);
                byte b = (byte)(0x40 + 0x20 * t);
                c[v] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            }
        }
        return c;
    }

    /// <summary>팔레트를 건다.</summary>
    public void SetPalette(GamePalette palette)
    {
        var pal = new uint[FlatColors];
        for (int i = 0; i < pal.Length; i++) pal[i] = palette.Bgra[i % GamePalette.Count];
        var old = _paletteSrv;
        _paletteSrv = CreateImmutable(pal, FlatColors, 1, Format.B8G8R8A8_UNorm, sizeof(uint));
        old?.Dispose();
    }

    /// <summary>칸(칩 번호) 텍스처를 갈아 끼운다. 세계지도와 항구를 오갈 때 쓴다.</summary>
    public void SetCells(byte[] cells, int width, int height)
    {
        var old = _cellSrv;
        Cells = cells;
        _cellSrv = CreateImmutable(cells, width, height, Format.R8_UInt, 1);
        MapW = width;
        MapH = height;
        old?.Dispose();
    }

    /// <summary>칩 아틀라스를 건다. <paramref name="atlas"/> 는 색인 0~15 한 바이트씩이다.</summary>
    public void SetChips(ChipSheet sheet)
    {
        var atlas = sheet.ToAtlas(AtlasCols, out int w, out int h);
        SetChipsCore(atlas, w, h, AtlasCols);
    }

    private void SetChipsCore(byte[] atlas, int w, int h, int cols)
    {
        var old = _atlasSrv;
        _atlasSrv = CreateImmutable(atlas, w, h, Format.R8_UInt, 1);
        _atlasCols = cols;
        old?.Dispose();
    }

    private int _shipW = 1, _shipH = 1;

    /// <summary>배가 서쪽을 보고 있는지. 참이면 그림을 좌우로 뒤집는다.</summary>
    public bool ShipFacesWest { get; set; }

    private float _frameX, _frameY, _frameW, _frameH;

    /// <summary>
    /// 그림 판에서 한 장만 잘라 쓴다. 사람 그림처럼 여러 장이 한 판에 있을 때다.
    /// </summary>
    public void SpriteFrame(int frame, int w, int h, int cols)
    {
        _frameX = frame % cols * w;
        _frameY = frame / cols * h;
        _frameW = w;
        _frameH = h;
    }

    /// <summary>판 전체를 한 장으로 쓴다.</summary>
    public void SpriteWhole()
    {
        _frameX = _frameY = 0;
        _frameW = _shipW;
        _frameH = _shipH;
    }

    /// <summary>배 그림을 건다. 알파 0 이 비침이다.</summary>
    public void SetShipSprite(ReadOnlySpan<uint> bgra, int width, int height)
    {
        if (bgra.Length < width * height) return;
        var old = _shipSrv;
        _shipSrv = CreateImmutable(bgra.ToArray(), width, height, Format.B8G8R8A8_UNorm, sizeof(uint));
        _shipW = width; _shipH = height;
        SpriteWhole();
        old?.Dispose();
    }

    /// <summary>
    /// 색인 그림을 팔레트로 칠해 배 그림으로 건다. <paramref name="clear"/> 색인은 비침이다.
    /// </summary>
    public void SetShipSprite(byte[] indices, int width, int height, GamePalette palette, byte clear)
    {
        var bgra = new uint[width * height];
        for (int i = 0; i < bgra.Length && i < indices.Length; i++)
            bgra[i] = indices[i] == clear ? 0u : palette.Bgra[indices[i] & 0xF];
        SetShipSprite(bgra, width, height);
    }

    private ID3D11ShaderResourceView CreateImmutable<T>(T[] data, int w, int h, Format fmt, int stride)
        where T : unmanaged
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var desc = new Texture2DDescription
            {
                Width = (uint)w,
                Height = (uint)h,
                MipLevels = 1,
                ArraySize = 1,
                Format = fmt,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.ShaderResource,
            };
            var sub = new SubresourceData(handle.AddrOfPinnedObject(), (uint)(w * stride));
            using var tex = _device.CreateTexture2D(desc, [sub]);
            return _device.CreateShaderResourceView(tex);
        }
        finally { handle.Free(); }
    }

    /// <summary>밖에서 준 대상에 그린다. 스왑체인 백버퍼에 곧바로 그릴 때 쓴다.</summary>
    public void RenderTo(ID3D11RenderTargetView rtv, int width, int height,
                         (double X, double Y) originCell, double cellsPerPixel,
                         (float X, float Y, float W, float H) shipRect)
    {
        var cb = new FrameCb
        {
            OriginCellX = (float)originCell.X,
            OriginCellY = (float)originCell.Y,
            CellPerPixelX = (float)cellsPerPixel,
            CellPerPixelY = (float)cellsPerPixel,
            MapCellsX = MapW,
            MapCellsY = MapH,
            Mode = UseChips ? 1 : 0,
            AtlasCols = _atlasCols,
            ShipX = shipRect.X,
            ShipY = shipRect.Y,
            ShipW = shipRect.W,
            ShipH = shipRect.H,
            GridOn = ShowGrid ? 1 : 0,
            GridStep = GridStep,
            WrapX = WrapX ? 1 : 0,
            SpriteW = _shipW,
            SpriteH = _shipH,
            ShipFlip = ShipFacesWest ? 1 : 0,
            SrcX = _frameX,
            SrcY = _frameY,
            SrcW = _frameW > 0 ? _frameW : _shipW,
            SrcH = _frameH > 0 ? _frameH : _shipH,
        };

        var map = _ctx.Map(_cb, 0, Vortice.Direct3D11.MapMode.WriteDiscard);
        *(FrameCb*)map.DataPointer = cb;
        _ctx.Unmap(_cb, 0);

        _ctx.OMSetRenderTargets(rtv);
        _ctx.RSSetViewport(0, 0, width, height);
        _ctx.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));
        _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _ctx.VSSetShader(_vs);
        _ctx.PSSetShader(_ps);
        _ctx.PSSetConstantBuffer(0, _cb);
        _ctx.PSSetShaderResources(0, [_cellSrv, _atlasSrv, _paletteSrv, _flatSrv, _shipSrv]);
        _ctx.Draw(3, 0);
    }

    public void Dispose()
    {
        _shipSrv?.Dispose();
        _flatSrv?.Dispose();
        _paletteSrv?.Dispose();
        _atlasSrv?.Dispose();
        _cellSrv?.Dispose();
        _cb?.Dispose();
        _ps?.Dispose();
        _vs?.Dispose();
        _ctx?.Dispose();
        _device?.Dispose();
    }
}
