using System.Text;

namespace Uw2.Support.Local.Formats;

/// <summary>
/// 발견물 글 <c>COLONY.DAT</c> — 177바이트 x 100. +0 이름(15, cp949, 끝 0) · +15 한 바이트 · +16 풀이(끝 0, <c>\n</c> 줄 바꿈).
/// </summary>
/// <remarks>원본은 찾은 뒤 <c>0x0041AB30</c> 이 번호 x 0xB1 자리를 읽어 그림(KAO 128 + 번호)과 함께 띄운다.</remarks>
public sealed class ColonyText
{
    public const int RecordSize = 0xB1, NameSize = 15, TextAt = 16;

    private readonly (string Name, string Text)[] _records;

    private ColonyText((string, string)[] records) => _records = records;

    public int Count => _records.Length;

    public string Name(int i) => i >= 0 && i < _records.Length ? _records[i].Name : "";

    public string Text(int i) => i >= 0 && i < _records.Length ? _records[i].Text : "";

    public static ColonyText Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static ColonyText FromBytes(byte[] d)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        string Z(int at, int len) => cp949.GetString(d, at, len).Split('\0')[0];
        var records = new (string, string)[d.Length / RecordSize];
        for (int i = 0; i < records.Length; i++)
        {
            int o = i * RecordSize;
            records[i] = (Z(o, NameSize), Z(o + TextAt, RecordSize - TextAt));
        }
        return new ColonyText(records);
    }
}
