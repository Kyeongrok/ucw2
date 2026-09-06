namespace Uw2.Support.Local.Formats;

/// <summary>
/// 대항해시대2 의 <c>.LZW</c> 묶음. 껍데기와 알맹이를 다 푼다.
/// </summary>
/// <remarks>
/// Win95 이식판 <c>KOUKAI2.EXE</c>(32비트 PE, ImageBase <c>0x400000</c>)를 읽어 옮긴 것이다.
/// DOS <c>MAIN.EXE</c> 는 16비트 볼랜드라 손이 많이 가는데, 이식판은 평범한 PE 라 3편
/// <c>CDS_95.EXE</c> 뜯던 방식이 그대로 통했다. <b>데이터는 두 판이 같다.</b>
///
/// <para>껍데기</para>
/// <code>
///   0x000  4     매직 "LS10" 또는 "LS11" (짜임은 같다)
///   0x004  12    0 으로 채움
///   0x010  256   글자표 — 0~255 가 한 번씩 든 순열, 빈도순
///   0x110  12N   청크 목록 {packedSize, unpackedSize, fileOffset} 빅엔디안 32비트
///          4     0 으로 끝맺음
/// </code>
/// 청크 수는 세지 않고 <b>셈한다</b>(<c>0x0041C109</c>) — <c>(첫 offset - 0x110) / 12</c> 이고
/// 끝맺음 4바이트가 나머지로 떨어진다.
///
/// <para>알맹이</para>
/// 이름은 LZW 지만 <b>LZW 가 아니다.</b> 사전을 키우지 않는다. 엘리아스 감마꼴 부호로
/// 심볼을 읽는 LZ77 이다(<c>0x0041C240</c> · <c>0x0041C290</c>).
///
/// 자세한 것은 볼트 <c>Project/uw2/분석/2.분석-LS10·LS11 압축 포맷</c>.
/// </remarks>
public static class LsArchive
{
    /// <summary>머리(매직 + 채움 + 글자표) 크기. 청크 목록이 여기서 시작한다.</summary>
    public const int HeaderSize = 0x110;

    /// <summary>글자표가 놓인 자리와 크기.</summary>
    public const int TableOffset = 16, TableSize = 256;

    /// <summary>청크 목록 한 칸의 크기(빅엔디안 32비트 셋).</summary>
    public const int EntrySize = 12;

    /// <summary>청크 한 칸.</summary>
    /// <param name="PackedSize">눌린 크기. <see cref="UnpackedSize"/> 와 같으면 안 눌린 것이다.</param>
    /// <param name="UnpackedSize">푼 크기.</param>
    /// <param name="FileOffset">파일 첫머리부터의 자리.</param>
    public readonly record struct Entry(int PackedSize, int UnpackedSize, int FileOffset);

    /// <summary>묶음 하나를 통째로 풀어 청크 목록을 낸다.</summary>
    public static byte[][] ReadAll(string path) => ReadAll(File.ReadAllBytes(path));

    /// <inheritdoc cref="ReadAll(string)"/>
    public static byte[][] ReadAll(ReadOnlySpan<byte> file)
    {
        var entries = ReadDirectory(file, out var table);
        var chunks = new byte[entries.Length][];
        for (int i = 0; i < entries.Length; i++)
            chunks[i] = ReadChunk(file, entries[i], table);
        return chunks;
    }

    /// <summary>청크 목록과 글자표만 읽는다. 알맹이는 안 푼다.</summary>
    public static Entry[] ReadDirectory(ReadOnlySpan<byte> file, out byte[] table)
    {
        if (file.Length < HeaderSize + EntrySize || file[0] != (byte)'L' || file[1] != (byte)'S')
            throw new InvalidDataException("LS10/LS11 묶음이 아닙니다");

        table = file.Slice(TableOffset, TableSize).ToArray();

        // 게임이 하는 셈 그대로다(0x0041C109). 첫 청크 자리에서 목록 길이를 되짚는다.
        int first = BigEndian(file, HeaderSize + 8);
        int count = (first - HeaderSize) / EntrySize;
        if (count <= 0 || count > 0x4000)
            throw new InvalidDataException($"청크 수가 이상합니다 ({count})");

        var entries = new Entry[count];
        for (int i = 0; i < count; i++)
        {
            int at = HeaderSize + EntrySize * i;
            entries[i] = new Entry(BigEndian(file, at), BigEndian(file, at + 4), BigEndian(file, at + 8));
        }
        return entries;
    }

    /// <summary>청크 하나를 푼다(<c>0x0041C300</c>).</summary>
    public static byte[] ReadChunk(ReadOnlySpan<byte> file, Entry e, byte[] table)
    {
        var packed = file.Slice(e.FileOffset, e.PackedSize);

        // 두 크기가 같으면 안 눌린 것이라 그대로 읽는다(0x0041C3F9 의 cmp esi,edi).
        if (e.PackedSize == e.UnpackedSize) return packed.ToArray();

        return Unpack(packed, e.UnpackedSize, table);
    }

    /// <summary>
    /// 비트스트림을 푼다(<c>0x0041C290</c>).
    /// </summary>
    /// <remarks>
    /// 심볼이 256 보다 작으면 글자표를 짚고, 크면 <c>심볼 - 256</c> 칸 뒤를 돌아본다.
    /// 길이는 뒤따르는 심볼 + 3 이다. 베끼기가 <b>한 바이트씩</b>이라 거리보다 길이가 길면
    /// 되풀이 무늬가 된다 — 바다처럼 같은 값이 이어지는 데서 이것이 많이 나온다.
    /// </remarks>
    public static byte[] Unpack(ReadOnlySpan<byte> packed, int want, byte[] table)
    {
        var reader = new BitReader(packed);
        var outBuf = new byte[want];
        int n = 0;

        while (n < want)
        {
            int s = reader.Symbol();
            if (s < 0) throw new InvalidDataException($"스트림이 모자랍니다 ({n}/{want})");

            if (s < 256)
            {
                outBuf[n++] = table[s];
                continue;
            }

            int len = reader.Symbol();
            if (len < 0) throw new InvalidDataException("길이가 모자랍니다");
            len += 3;

            int from = n - (s - 256);
            if (from < 0) throw new InvalidDataException($"거리가 지도 밖입니다 ({s - 256})");
            if (n + len > want) len = want - n;                 // 마지막 조각이 넘칠 수 있다
            for (int i = 0; i < len; i++) outBuf[n + i] = outBuf[from + i];
            n += len;
        }
        return outBuf;
    }

    private static int BigEndian(ReadOnlySpan<byte> d, int at) =>
        (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3];

    /// <summary>
    /// MSB 부터 읽는 비트 읽개(<c>0x0041C1B0</c>)와 심볼 읽개(<c>0x0041C240</c>).
    /// </summary>
    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _d;
        private int _p;
        private byte _cur;
        private int _left;

        public BitReader(ReadOnlySpan<byte> d) { _d = d; _p = 0; _cur = 0; _left = 0; }

        /// <summary>비트 <paramref name="k"/> 개. 스트림이 다하면 -1.</summary>
        public int Bits(int k)
        {
            int v = 0;
            for (int i = 0; i < k; i++)
            {
                if (_left == 0)
                {
                    if (_p >= _d.Length) return -1;
                    _cur = _d[_p++];
                    _left = 8;
                }
                _left--;
                v = (v << 1) | ((_cur >> 7) & 1);
                _cur = (byte)(_cur << 1);
            }
            return v;
        }

        /// <summary>
        /// 심볼 하나. <b>1 이 이어지는 만큼 세고 그만큼 더 읽는다.</b>
        /// </summary>
        /// <remarks>
        /// <code>
        ///   k = 0
        ///   do { b = 비트 1개; k++ } while (b == 1)
        ///   symbol = (1 &lt;&lt; k) - 2 + 비트 k개
        /// </code>
        /// 부호 길이가 <c>2k</c> 비트라 흔한 글자는 두 비트, 드문 글자는 열여섯 비트다.
        /// 그래서 파일마다 빈도순 글자표를 새로 계산해 머리에 박아 둔다.
        /// </remarks>
        public int Symbol()
        {
            int k = 0;
            while (true)
            {
                int b = Bits(1);
                if (b < 0) return -1;
                k++;
                if (b == 0) break;
                if (k > 24) return -1;                          // 망가진 파일에서 안 굳게
            }
            int v = Bits(k);
            return v < 0 ? -1 : (1 << k) - 2 + v;
        }
    }
}
