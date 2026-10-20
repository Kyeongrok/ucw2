namespace Uw2.Game.Engine.Sea;

/// <summary>
/// 바다의 날씨 — 폭풍 · 무풍 · 안개. <c>KOUKAI2.EXE</c> 의 날씨 바이트 <c>0x0049F598</c> 를 그대로 옮겼다.
/// </summary>
/// <remarks>
/// 날씨 바이트는 이렇게 갈린다.
/// <code>
///   비트 0..3   갈래  1 폭풍 · 2 무풍 · 3 안개 (0 이면 맑음)
///   비트 6      조짐 — 항해사가 「~의 전조입니다」 하고 알렸다
///   비트 7      오늘 들이닥쳤다 — 날 넘김(0x004178C0)이 지운다. 이게 서 있는 날에는 걷히지 않는다
/// </code>
/// 바람을 흔드는 네 시간마다(<c>0x00418F2D</c>) 한 번씩 셈한다.
/// <list type="bullet">
/// <item>맑으면 <c>0x00418F80</c> — 그 자리 바람 바이트 비트 7(날씨 바다)이 서 있고, <c>rand(155)</c> 가
///   운(사람 +0x23, 제독과 동료 가운데 큰 것)+50 보다 크고, 배들의 <c>+0x1D</c> 평균이 <c>rand(5)</c> 보다 작으면
///   해류 바이트 비트 6 · 7 로 갈래를 고른다.
///   <c>0x40</c> 폭풍(<c>rand(7)+3 &lt; 풍속</c>) · <c>0x80</c> 무풍(<c>rand(6) &gt;= 풍속</c>) ·
///   <c>0xC0</c> 안개(<c>rand(10) == 0</c> 이고 <c>rand(6) &gt;= 풍속</c>). 걸리면 조짐(<c>0x00419250</c>).</item>
/// <item>조짐이면 <c>0x004193D0</c> — 들이닥친다(비트 7). 「폭풍우입니다!!」 따위를 알린다.</item>
/// <item>들이닥친 뒤 — 폭풍은 풍속을 7 로 올리고 배를 친다(<c>0x004194D0</c>), 무풍은 풍속을 0 으로 누른다
///   (<c>0x00419890</c>). 오늘 들이닥친 게 아니면 폭풍은 <c>rand(8) == 0</c> 이거나 날씨 바다를 벗어나면,
///   무풍은 <c>rand(20) == 0</c> 이고 그 자리 표 바람이 0 이 아니면, 안개는 <c>rand(30) == 0</c> 이거나
///   날씨 바다를 벗어나면 걷힌다.</item>
/// </list>
/// 날씨가 서 있는 동안은 바람을 흔들지 않는다(<c>0x00418EA2</c>) — 지난 바람에 날씨 비트만 표에서 새로 받는다.
///
/// 폭풍이 배를 치는 셈(<c>0x00419502</c>)은 배마다 <c>k = min(10, (100 - 항해술) / (항해 레벨 / 5 + 3) + 1)</c>,
/// <c>d = k + rand(2)</c>(튼튼한 배가 아니면 x5)만큼 내구를, 그 절반만큼 돛 · 역풍 성능을 깎고 최대 내구를 1 줄인다
/// (선종 내구의 1/4 아래로는 안 내려간다). 내구가 0 이면 난파 — 기함이면 게임이 끝난다.
/// <b>우리 배에는 아직 내구가 없어 이 셈은 옮기지 않았다.</b>
/// </remarks>
public sealed class Weather
{
    public enum Kind { Clear = 0, Storm = 1, Calm = 2, Fog = 3 }

    public Kind Current { get; private set; }

    /// <summary>조짐만 보이고 아직 안 들이닥쳤다(비트 6).</summary>
    public bool Brewing { get; private set; }

    /// <summary>오늘 들이닥쳤다(비트 7). 이 날에는 걷히지 않는다.</summary>
    public bool StruckToday { get; private set; }

    /// <summary>
    /// 배들의 <c>+0x1D</c> 평균 — 날씨가 일 조건에 쓴다. <b>뜻을 못 굳혔다</b>. 우리 배에는 없어 0 으로 둔다.
    /// </summary>
    public int FleetFactor { get; set; }

    /// <summary>제독(과 동료) 가운데 가장 큰 운 — 사람 +0x23. 새 게임이 <c>rand(101)</c> 로 정한다(<c>0x00416A01</c>).</summary>
    public int Luck { get; set; }

    /// <summary>알릴 말이 생기면 부른다. 글은 <c>MESSAGE.DAT</c> 그대로다.</summary>
    public event Action<string>? Said;

    /// <summary>날 넘김 — 오늘 들이닥친 표시를 지운다.</summary>
    public void NewDay() => StruckToday = false;

    /// <summary>
    /// 네 시간마다 한 번. <paramref name="windByte"/> 는 그 자리 바람 표 바이트, <paramref name="currentByte"/> 는
    /// 해류 표 바이트, <paramref name="windSpeed"/> 는 지금(흔든 뒤) 풍속이다.
    /// </summary>
    public void Update(byte windByte, byte currentByte, int windSpeed, int tableWindSpeed, Random rng)
    {
        bool zone = (windByte & 0x80) != 0;

        if (Current == Kind.Clear)
        {
            TryBrew(zone, currentByte, windSpeed, rng);
            return;
        }

        if (Brewing)
        {
            Brewing = false;
            StruckToday = true;
            Said?.Invoke(Current switch
            {
                Kind.Storm => "제독님, 폭풍우입니다!! 키가 전혀 안 듣습니다.",
                Kind.Calm => "제독님, 바람이 완전히 멎었습니다.",
                _ => "제독님, 심한 안개군요. 주위가 보이지 않습니다.",
            });
            return;
        }

        if (StruckToday) return;
        bool clears = Current switch
        {
            Kind.Storm => rng.Next(8) == 0 || !zone,
            Kind.Calm => rng.Next(20) == 0 && tableWindSpeed != 0,
            Kind.Fog => rng.Next(30) == 0 || !zone,
            _ => true,
        };
        if (!clears) return;
        Said?.Invoke(Current switch
        {
            Kind.Storm => "제독님, 겨우 위기를 넘겼습니다.",
            Kind.Calm => "제독님, 바람, 바람이 붑니다.",
            _ => "제독님, 겨우 안개가 걷혔습니다.",
        });
        Current = Kind.Clear;
    }

    private void TryBrew(bool zone, byte currentByte, int windSpeed, Random rng)
    {
        if (!zone) return;
        if (rng.Next(155) <= Luck + 50) return;
        if (FleetFactor >= rng.Next(5)) return;

        Kind kind = (currentByte & 0xC0) switch
        {
            0x40 when rng.Next(7) + 3 < windSpeed => Kind.Storm,
            0x80 when rng.Next(6) >= windSpeed => Kind.Calm,
            0xC0 when rng.Next(10) == 0 && rng.Next(6) >= windSpeed => Kind.Fog,
            _ => Kind.Clear,
        };
        if (kind == Kind.Clear) return;

        Current = kind;
        Brewing = true;
        Said?.Invoke(kind switch
        {
            Kind.Storm => "제독님, 날씨가 이상한데요. 이것은 폭풍우의 전조입니다.",
            Kind.Calm => "제독님, 분위기가 심상치 않습니다. 바람이 멎을지도 모르겠어요.",
            _ => "제독님, 바람이 습합니다. 안개가 낄지도 모르겠습니다.",
        });
    }

    /// <summary>날씨가 바람에 미치는 것 — 폭풍은 7, 무풍은 0. 조짐 동안은 그대로다.</summary>
    public int Apply(int windSpeed) => Brewing ? windSpeed : Current switch
    {
        Kind.Storm => 7,
        Kind.Calm => 0,
        _ => windSpeed,
    };

    /// <summary>상태줄 글.</summary>
    public string Label => Current switch
    {
        Kind.Clear => "",
        Kind.Storm => Brewing ? "폭풍 조짐" : "폭풍",
        Kind.Calm => Brewing ? "무풍 조짐" : "무풍",
        _ => Brewing ? "안개 조짐" : "안개",
    };
}
