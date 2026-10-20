namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 지리 조사 판 — 세계를 24칩 칸 90 x 45 로 나눈 비트 판(<c>0x0049F040</c>, 한 줄 12바이트).
/// </summary>
/// <remarks>
/// 함대가 새 칸에 들면(<c>0x004339AF</c>) 그 칸을 가운데로 한 3 x 3 을 칠하고(<c>0x00433A30</c>), 새로 칠한 칸마다
/// 모두 센 수(<c>0x0049F028</c>)와 보고 뒤 센 수(<c>0x0049F024</c>)가 오른다. 새 게임은 유럽 언저리(줄 8~17, 칸 4~16)
/// 130 칸을 칠해 두고 시작한다(<c>0x00416A70</c>).
///
/// 학자 「지리 보고」(<c>0x00422150</c>)는 보고 뒤 센 수 x 20 x (5 − 격)을 주고 모험가 명성을 그 수 x 5 올린다.
/// 모두 센 수가 3300 이 넘으면 세계지도를 다 그렸다고(0x1FB) 판을 다 칠한다.
/// </remarks>
public sealed class Survey
{
    public const int Cols = 90, Rows = 45, CellChips = 24, Complete = 0xCE4;

    private readonly bool[] _cells = new bool[Cols * Rows];
    private int _lastCell = -1;

    /// <summary>모두 칠한 칸.</summary>
    public int Total { get; private set; }

    /// <summary>마지막 보고(또는 계약) 뒤 칠한 칸.</summary>
    public int SinceReport { get; set; }

    public bool this[int col, int row] => _cells[row * Cols + col];

    public static Survey NewGame()
    {
        var s = new Survey();
        for (int row = 8; row <= 17; row++)
            for (int col = 4; col <= 16; col++)
                s._cells[row * Cols + col] = true;
        s.Total = 130;
        return s;
    }

    /// <summary>함대 자리(칩)를 본다 — 칸이 바뀌었을 때만 칠하고, 새로 칠한 칸 수를 돌려준다.</summary>
    public int Visit(double chipX, double chipY)
    {
        int col = ((int)Math.Floor(chipX) / CellChips % Cols + Cols) % Cols;
        int row = Math.Clamp((int)Math.Floor(chipY) / CellChips, 0, Rows - 1);
        int cell = row * Cols + col;
        if (cell == _lastCell) return 0;
        _lastCell = cell;
        int added = 0;
        for (int r = row - 1; r <= row + 1; r++)
        {
            if (r < 0 || r >= Rows) continue;
            for (int k = 0; k < 3; k++)
            {
                int c = (col + Cols - 1 + k) % Cols;
                if (_cells[r * Cols + c]) continue;
                _cells[r * Cols + c] = true;
                added++;
            }
        }
        Total += added;
        SinceReport += added;
        return added;
    }

    /// <summary>다 그렸다 — 판을 모두 칠한다(<c>0x004221BB</c>).</summary>
    public void Finish() => Array.Fill(_cells, true);

    public static int Reward(int rank, int cells) => (5 - (rank & 3)) * cells * 20;

    public static int FameGain(int fame, int cells) => Math.Min(Town.Patrons.MaxFame - fame, cells * 5);

    /// <summary>세이브 — 칸은 0 · 1 글로.</summary>
    public SaveGame.SurveyState Export() => new()
    {
        Cells = new string(_cells.Select(c => c ? '1' : '0').ToArray()), Total = Total, SinceReport = SinceReport,
    };

    public static Survey Import(SaveGame.SurveyState? s)
    {
        if (s == null || s.Cells.Length != Cols * Rows) return NewGame();
        var v = new Survey { Total = s.Total, SinceReport = s.SinceReport };
        for (int i = 0; i < v._cells.Length; i++) v._cells[i] = s.Cells[i] == '1';
        return v;
    }
}
