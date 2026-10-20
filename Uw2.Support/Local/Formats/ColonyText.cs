using System.Text;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// 발견물 글 <c>COLONY.DAT</c> — 177바이트 x 100. +0 이름(15, cp949, 끝 0) · +15 그림 번호 · +16 풀이(끝 0, <c>\n</c> 줄 바꿈).
/// </summary>
/// <remarks>원본은 찾은 뒤 <c>0x0041AB30</c> 이 번호 x 0xB1 자리를 읽어 +15 의 그림과 함께 띄운다.</remarks>
public sealed class ColonyText
{
    public const int RecordSize = 0xB1, NameSize = 15, TextAt = 16;

    private readonly (string Name, string Text, int Picture)[] _records;

    private ColonyText((string, string, int)[] records) => _records = records;

    /// <summary>발견물 그림 번호(KAO 발견물 판, +15 바이트) — 원본 <c>0x0041ABCF</c> 가 <c>0x00434F40</c> 에 넘긴다.</summary>
    public int Picture(int i) => i >= 0 && i < _records.Length ? _records[i].Picture : -1;

    public int Count => _records.Length;

    public string Name(int i) => i >= 0 && i < _records.Length ? _records[i].Name : "";

    public string Text(int i) => i >= 0 && i < _records.Length ? _records[i].Text : "";

    public static ColonyText Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static ColonyText FromBytes(byte[] d)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        string Z(int at, int len) => cp949.GetString(d, at, len).Split('\0')[0];
        var records = new (string, string, int)[d.Length / RecordSize];
        for (int i = 0; i < records.Length; i++)
        {
            int o = i * RecordSize;
            records[i] = (Z(o, NameSize), Z(o + TextAt, RecordSize - TextAt), d[o + NameSize]);
        }
        return new ColonyText(records);
    }
}
