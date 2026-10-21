using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Uw2.Game.Engine;
using Uw2.Game.Engine.Sea;
using Uw2.Game.Engine.Town;
using Uw2.Game.UI.Rendering;
using Uw2.Support.Local.Formats;
using Uw2.Support.Local.Helpers;

namespace Uw2.Game.UI.Views;

/// <summary>
/// 세계지도를 띄우는 창. 끌어 옮기고 휠로 늘이고 줄인다.
/// </summary>
/// <remarks>
/// 배를 움직이는 것은 아직 없다 — 지도가 제대로 서는지 보는 것이 먼저다.
/// 3편 <c>ShipMapWindow</c> 자리다.
/// </remarks>
public sealed class SeaMapWindow : Window
{
    private readonly SeaMapHost _host = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 4, 8, 4) };
    private string _gameDir = "";
    private CheckBox _chipToggle = null!;
    private CheckBox _arrows = null!;

    /// <summary>마지막으로 든 건물 — 상태줄에 적는다. 항구를 옮기면 지운다.</summary>
    private string _doorNote = "";

    /// <summary>건물 번호 3 — 항구, 0 — 교역소(<see cref="PortDoors.Names"/>).</summary>
    private const int NpcBuilding = 7, HarborBuilding = 3, TradeBuilding = 0, ShipyardBuilding = 2, PubBuilding = 1, BankBuilding = 8, InnBuilding = 4, ChurchBuilding = 10, ItemBuilding = 9, PalaceBuilding = 5, GuildBuilding = 6, FortuneBuilding = 11;

    /// <summary>조합 일 갈래 — 0x00458688 부터 다섯(상품수송 · 상품구입 · 편지배달 · 해적퇴치 · 빚수금).</summary>
    private static readonly string[] JobNames = ["상품 수송", "상품 구입", "편지 배달", "해적 퇴치", "빚 수금"];

    /// <summary>이번에 교회에 든 뒤 예배를 드렸는지(<c>0x0049A188</c>).</summary>
    private bool _worshipped;

    /// <summary>항구 「보급」 — 넷 가운데 하나를 단(열 통)으로 싣는다(<see cref="Economy.SupplyPrice"/>).</summary>
    private void ShowSupplies()
    {
        if (_host.Economy is not { } eco) return;
        var p = _host.Player;
        int pick = BuildingMenu.Ask(this, $"보급 — 열 통 값 · 돈 {p.Gold} · 빈 자리 {p.Free}", Enumerable.Range(0, 4)
            .Select(k => $"{Economy.SupplyNames[k],-4} {(k == 0 ? "공짜" : eco.SupplyPrice(_host.CurrentPort, k).ToString()),6}"
                         + $"  (실은 것 {p.Ships.Sum(s => s.Supplies[k])})").ToList());
        if (pick < 0) return;
        int price = eco.SupplyPrice(_host.CurrentPort, pick);
        int most = Math.Min(p.Free / PlayerState.Lot, price > 0 ? p.Gold / price : int.MaxValue);
        // 값 말(0x00422E80) — 물은 「물은 무료입니다.」(0x3E), 식량 · 자재 · 포탄은 「…１통에 금화 %ld닢입니다.」(0x3F ~ 0x41).
        string head = pick == 0 ? MsgOr(0x3E, "물은 무료입니다.") : MsgOr(0x3E + pick, $"{Economy.SupplyNames[pick]}은 １통에 금화 {price}닢입니다.", price);
        int lots = AmountDialog.Ask(this, $"{head}\n{Economy.SupplyNames[pick]} 몇 단(열 통)", most);
        if (lots <= 0) return;
        int bought = p.BuySupply(pick, lots, price);
        _host.SyncLoad();
        Note($" · {Economy.SupplyNames[pick]} {bought * PlayerState.Lot}통을 실었습니다");
    }

    /// <summary>조선소 「함선 건조」 — 지을 수 있는 선종과 값(선종 +10)을 늘어놓는다. 짓기는 아직이다.</summary>
    private void ShowShipyard()
    {
        if (_host.Economy is not { } eco || _host.StartTables is not { } start) return;
        // 맡긴 배가 있으면 그것부터(0x0042894D → 0x00428BA0).
        if (eco.BuildDays(_host.CurrentPort) >= 0 && _host.OrderedHere is { } ordered)
        {
            TakeOrderedShip(eco.BuildDays(_host.CurrentPort), ordered);
            return;
        }
        var types = eco.Buildable(_host.CurrentPort, start.ShipTypes);
        if (types.Count == 0) { Say(0xFB, "우리집은 새 배는 안 만들지."); return; }   // 0x00428982
        string NameOf(int t) => t < start.Templates.Count ? start.Templates[t].Name ?? t.ToString() : t.ToString();
        var p = _host.Player;
        // 함대가 다 찼으면 「다른 배를 내주지 않겠소？」(0xA7) 뒤 내줄 배를 고른다(0x004289D8 → 0x00428A30).
        PlayerState.Ship? traded = null;
        int credit = 0;
        if (p.Ships.Count >= SeaMapHost.MaxShips)
        {
            Say(0xA7, "다른 배를 내주지 않겠소？");
            if (ChooseTradeIn(out credit) is not { } t) return;
            traded = t;
        }
        // 새 배를 맡을 동료가 있어야 한다(0x00429180, 말 0xA6) — 내준 배의 선장도 된다.
        int mate = _host.FreeMate;
        if (mate < 0 && traded is { Captain: >= 0 }) mate = traded.Captain;
        if (_host.HasMates && mate < 0) { Note(M(0xA6, "새 배를 맡을 항해사가 없습니다 — 주점이나 여관에서 동료를 구하세요")); return; }
        Say(0xFC, "호오, 새 배를 만든다니 제법이군.");   // 0x00428A0B
        int type;
        while (true)
        {
            int pick = BuildingMenu.Ask(this, $"함선 건조 — 돈 {p.Gold} · 배 {p.Ships.Count}/{SeaMapHost.MaxShips}",
                types.Select(t => $"{NameOf(t),-10} {start.ShipTypes[t].Price,6}").ToList());
            if (pick < 0) return;
            type = types[pick];
            if (PitchShip(type, building: true)) break;
        }
        int basePrice = start.ShipTypes[type].Price;
        int industry = _host.CurrentPort >= 0 && _host.CurrentPort < eco.States.Count ? eco.States[_host.CurrentPort].Industry : 0;
        int grades = Shipyard.GradeChoices(type, industry);
        // 선체 재질(0x00429A00) — 「선체의 재질은 무엇으로 하겠소？」(0xFE) 뒤 「이 놈으로 좋겠소？」(0xFF), 아니면 다시 고른다.
        int grade;
        while (true)
        {
            grade = BuildingMenu.Ask(this, $"{NameOf(type)} — " + MsgOr(0xFE, "선체의 재질은 무엇으로 하겠소？"),
                Enumerable.Range(0, grades).Select(g => $"등급 {g}  {Shipyard.BuildPrice(basePrice, g),6}").ToList(),
                _host.Graphs?.Get(ShipGraph(type)));
            if (grade < 0) return;
            if (_devQuiet || MessageBox.Show(this, MsgOr(0xFF, "이 놈으로 좋겠소？"), NameOf(type), MessageBoxButton.YesNo) == MessageBoxResult.Yes) break;
        }
        if (Haggle(Shipyard.BuildPrice(basePrice, grade), building: true) is not { } price) return;
        if (p.Gold + credit < price) { Say(0xEF, "손님, 돈이 모자랍니다."); return; }
        if (traded != null && !_host.RemoveShip(p.Ships.IndexOf(traded))) return;
        int days = _host.OrderShip(type, grade);
        if (days < 0) return;
        p.SetGold(p.Gold + credit - price);
        if (_host.OrderedHere is { } made) NameNewShip(made);
        Say(0x101, "좋아, 최고의 배를 만들어 주겠소.");
        Say(0x106, $"배가 완성되는 데 {days}일 걸리지.", days);
        Note($" · {NameOf(type)}{(KoreanBatchim(NameOf(type)) ? "을" : "를")} {price}에 맡겼습니다 — {days}일 뒤");
    }

    /// <summary>
    /// 맡긴 배(<c>0x00428BA0</c>) — 남은 날이 있으면 「배는 아직 완성되지 않았소. %d일 더 기다려 주시오」(0x105). 다 됐으면 「아, 손님！」(0xFA)
    /// 「이 배를 인수하겠소？」(0x107) — 받으면 맡을 동료를 고르고(<c>0x004290D0</c>) 함대에 넣는다. 아니면 「배는 아직 좀더 맡아 두겠소」(0x109).
    /// </summary>
    private void TakeOrderedShip(int days, PlayerState.Ship ship)
    {
        if (days > 0) { Say(0x105, $"배는 아직 완성되지 않았소. {days}일 더 기다려 주시오.", days); return; }
        Say(0xFA, "아, 손님！");
        if (MessageBox.Show(this, MsgOr(0x107, "이 배를 인수하겠소？"), ship.Name, MessageBoxButton.YesNo) != MessageBoxResult.Yes
            || _host.Player.Ships.Count >= SeaMapHost.MaxShips)
        {
            Say(0x109, "배는 아직 좀더 맡아 두겠소.");
            return;
        }
        int captain = -1;
        if (_host.Scenario is { } vm && _host.HasMates)
        {
            var free = Navigators.Mates(vm.Memory).Select(t => t.Person)
                .Where(x => Navigators.Get(vm.Memory, x, Navigators.Status) != Navigators.Captain).ToList();
            if (free.Count == 0) { Note(M(0xA6, "새 배를 맡을 항해사가 없습니다 — 주점이나 여관에서 동료를 구하세요")); Say(0x109, "배는 아직 좀더 맡아 두겠소."); return; }
            int who = BuildingMenu.Ask(this, $"{ship.Name} — 선장", free.Select(_host.PersonName).ToList());
            if (who < 0) { Say(0x109, "배는 아직 좀더 맡아 두겠소."); return; }
            captain = free[who];
        }
        if (!_host.TakeOrdered(captain)) return;
        Note($" · {ship.Name}{(KoreanBatchim(ship.Name) ? "을" : "를")} 받았습니다{(captain >= 0 ? $" · 선장 {_host.PersonName(captain)}" : "")}");
    }

    /// <summary>
    /// 배 값 흥정(<c>0x004295A0</c>) — 「이 배는 금화 %ld닢에 어떻소？」(0xC2, 지으면 0x100)에 그러면 그 값. 아니면 깎는다: 바닥 값은
    /// 값 x (500 − 주인공 매력 +0x22) / 1000. 회계 자리(처지 4)에 동료가 있으면 「틀림없이 금화 %ld닢까지 깍을 수 있을걸요」(0xC4, 바닥 x (95 + x) / 100,
    /// 회계술이 있으면 x = 0, 없으면 rand(10)). 「자, 얼마라면 사겠소？」(0xC3, min(값, 돈)까지) — 바닥 아래면 다섯에 하나 「배의 가치도 모르는 놈은 썩 꺼져！」(0xC5,
    /// 그날 조선소에 못 든다 — 0xF9) 아니면 0xC6. 바닥 이상이면 「손님에겐 못 당하겠네요」(0x35F) 그 값. 안 사면 null.
    /// </summary>
    private int? Haggle(int price, bool building)
    {
        if (_host.Scenario is not { } vm) return price;
        var m = vm.Memory;
        string ask = building ? MsgOr(0x100, $"새 배를 만드는 데는 금화 {price}닢이 들지.", price) : MsgOr(0xC2, $"이 배는 금화 {price}닢에 어떻소？", price);
        var yes = MessageBox.Show(this, ask, "조선소", MessageBoxButton.YesNoCancel);
        if (yes == MessageBoxResult.Cancel) return null;
        if (yes == MessageBoxResult.Yes) return price;
        int floor = price * (500 - HeroField(0x22)) / 1000;
        if (Navigators.InPost(m, 1) is >= 0 and var clerk)
        {
            int x = (Navigators.Get(m, clerk, SkillField) & 2) != 0 ? 0 : _host.Roll(10);
            int hint = (x + 95) * floor / 100;
            Say(0xC4, $"틀림없이 금화 {hint}닢까지 깍을 수 있을걸요.", hint);
        }
        int offer = AmountDialog.Ask(this, MsgOr(0xC3, "자, 얼마라면 사겠소？"), Math.Min(price, _host.Player.Gold));
        if (offer < 0) return null;
        if (offer < floor)
        {
            if (_host.Roll(5) == 0)
            {
                Say(0xC5, "배의 가치도 모르는 놈은 썩 꺼져！");
                _host.ShipyardBanned = true;
            }
            else Say(0xC6, "그렇게는 안되겠소.");
            return null;
        }
        Say(0x35F, "손님에겐 못 당하겠네요.", offer);
        return offer;
    }

    /// <summary>
    /// 고른 선종을 권한다(<c>0x00429257</c> · <c>0x00429B89</c>) — 배 그림과 함께 선종 말(표 <c>0x00458C28</c>, 선종 0 ~ 24 → 0xA8 ~ 0xC0)을
    /// 띄우고 「이 놈을 만들겠소？」(0xFD) · 「이 놈을 사겠소？」(0xC1)를 묻는다. 아니오면 목록으로 돌아간다.
    /// </summary>
    private bool PitchShip(int type, bool building)
    {
        if (_devQuiet) return true;
        var lines = new List<ScenarioVm.Line>();
        if (type is >= 0 and <= 0xC0 - 0xA8 && _host.Messages is { } m && m[0xA8 + type].Length > 0)
            lines.Add(new ScenarioVm.Line("", m[0xA8 + type]));
        lines.Add(new ScenarioVm.Line("", building ? MsgOr(0xFD, "이 놈을 만들겠소？") : MsgOr(0xC1, "이 놈을 사겠소？")));
        return ScenarioDialog.Confirm(this, _host.Faces, lines, _host.Graphs?.Get(ShipGraph(type)));
    }

    /// <summary>산 배에 이름을 붙인다(<c>0x00429800</c>) — 「이제 이 배는 당신 것이오. 배 이름을 붙여 주시오.」(0xC7), 일곱 자까지. 비우면 선종 이름 그대로.</summary>
    private void NameNewShip(PlayerState.Ship ship)
    {
        string? name = TextDialog.Ask(this, MsgOr(0xC7, "이제 이 배는 당신 것이오. 배 이름을 붙여 주시오."), ship.Name, 7);
        if (!string.IsNullOrWhiteSpace(name)) ship.Name = name.Trim();
    }

    /// <summary>조선소 「중고선 구입」 — 항구에 들 때 마련한 다섯 칸(<see cref="Shipyard.EnterPort"/>), 값은 선종 값.</summary>
    private void BuyUsedShip()
    {
        if (_host.StartTables is not { } start || _host.Scenario is not { } vm) return;
        var p = _host.Player;
        var used = Shipyard.Used(vm.Memory, start.ShipTypes.Count);
        if (used.Count == 0) { Note(" · 지금은 중고선이 없습니다"); return; }
        // 함대가 다 찼으면 배 하나를 내주고 그 값의 반을 셈에 넣는다(0x004289D8 → 0x00428A30, 0x004295E9).
        PlayerState.Ship? traded = null;
        int credit = 0;
        if (p.Ships.Count >= SeaMapHost.MaxShips)
        {
            Say(0xA7, "다른 배를 내주지 않겠소？");
            if (ChooseTradeIn(out credit) is not { } t) return;
            traded = t;
        }
        int mate = _host.FreeMate;
        if (mate < 0 && traded is { Captain: >= 0 }) mate = traded.Captain;
        if (_host.HasMates && mate < 0) { Note(M(0xA6, "새 배를 맡을 항해사가 없습니다 — 주점이나 여관에서 동료를 구하세요")); return; }
        string NameOf(int t) => t < start.Templates.Count ? start.Templates[t].Name ?? t.ToString() : t.ToString();
        int slot, type;
        while (true)
        {
            int pick = BuildingMenu.Ask(this, $"중고선 구입 — 돈 {p.Gold}",
                used.Select(u => $"{NameOf(u.Type),-10} {start.ShipTypes[u.Type].Price,6}").ToList());
            if (pick < 0) return;
            (slot, type) = used[pick];
            if (PitchShip(type, building: false)) break;
        }
        if (Haggle(start.ShipTypes[type].Price, building: false) is not { } price) return;
        if (p.Gold + credit < price) { Say(0xEF, "손님, 돈이 모자랍니다."); return; }
        if (traded != null && !_host.RemoveShip(p.Ships.IndexOf(traded))) return;
        if (!_host.AddShip(type, captain: mate)) return;
        p.SetGold(p.Gold + credit - price);
        NameNewShip(p.Ships[^1]);
        Shipyard.TakeUsed(vm.Memory, slot);
        Note($" · 중고 {NameOf(type)}{(KoreanBatchim(NameOf(type)) ? "을" : "를")} {price}에 샀습니다 · 선장 {_host.PersonName(mate)}");
    }

    /// <summary>
    /// 내줄 배를 고른다(<c>0x00428A30</c>) — 「매각할 배를 골라 주시오.」(0xC8), 「이 배로 하겠소？」(0xC9). 기함은 내주지 않는다(원본은 0xD1 · 0xD2 로
    /// 새 기함을 고르게 한다). 셈에 드는 값은 배 값의 반(<see cref="Shipyard.SellPrice"/>)이다.
    /// </summary>
    private PlayerState.Ship? ChooseTradeIn(out int credit)
    {
        credit = 0;
        if (_host.StartTables is not { } start || _host.Economy is not { } eco) return null;
        var p = _host.Player;
        int port = _host.CurrentPort;
        int avg = port >= 0 && port < eco.States.Count ? Shipyard.PriceIndexAverage(eco.States[port].PriceIndex) : 50;
        var ships = p.Ships.Skip(1).ToList();
        int pick = BuildingMenu.Ask(this, MsgOr(0xC8, "매각할 배를 골라 주시오."),
            [.. ships.Select(s => $"{s.Name,-10} {Shipyard.SellPrice(start.ShipTypes[s.Type].Price, s.Grade, avg),7}")]);
        if (pick < 0) return null;
        if (MessageBox.Show(this, MsgOr(0xC9, "이 배로 하겠소？"), ships[pick].Name, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return null;
        credit = Shipyard.SellPrice(start.ShipTypes[ships[pick].Type].Price, ships[pick].Grade, avg);
        return ships[pick];
    }

    /// <summary>조선소 「수리」 — 배를 골라 고친다(<see cref="Shipyard.RepairCost"/>).</summary>
    private void RepairShips()
    {
        if (_host.StartTables is not { } start) return;
        var p = _host.Player;
        // 배를 고르고(0x00429C40) 안 부서졌으면 「이 놈은 부서지지 않았는데.」(0xCC), 아니면 「수리하려면 금화 %u(가) 드는데, 괜찮겠소？」(0xCD) —
        //   아니면 0xCE, 돈이 모자라면 0xCF. 끝나면 목록으로 돌아간다.
        while (true)
        {
            var costs = p.Ships.Select(s => Shipyard.RepairCost(start.ShipTypes[s.Type], s)).ToList();
            int pick = BuildingMenu.Ask(this, $"수리 — 돈 {p.Gold}",
                p.Ships.Select((s, i) => $"{s.Name,-10} 내구 {s.Hull,3}/{s.MaxHull,-3} {(costs[i] > 0 ? costs[i].ToString() : "-"),6}").ToList());
            if (pick < 0) return;
            int cost = costs[pick];
            if (cost == 0) { Say(0xCC, "이 놈은 부서지지 않았는데."); continue; }
            if (MessageBox.Show(this, MsgOr(0xCD, $"수리하려면 금화 {cost}(가) 드는데, 괜찮겠소？", cost), p.Ships[pick].Name, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            { Say(0xCE, "수리하는 것이 좋을 것 같소만."); continue; }
            if (p.Gold < cost) { Say(0xCF, "금화가 부족합니다."); continue; }
            p.SetGold(p.Gold - cost);
            Shipyard.Repair(start.ShipTypes[p.Ships[pick].Type], p.Ships[pick]);
            Note($" · {p.Ships[pick].Name} 수리 {cost}");
        }
    }

    /// <summary>조선소 「매각」 — 배 값의 반(<see cref="Shipyard.SellPrice"/>). 마지막 한 척은 못 판다.</summary>
    private void SellShip()
    {
        if (_host.StartTables is not { } start || _host.Economy is not { } eco) return;
        var p = _host.Player;
        if (p.Ships.Count <= 1) { Note(M(0xD0, "마지막 배는 팔 수 없습니다")); return; }
        int port = _host.CurrentPort;
        int avg = port >= 0 && port < eco.States.Count ? Shipyard.PriceIndexAverage(eco.States[port].PriceIndex) : 50;
        var prices = p.Ships.Select(s => Shipyard.SellPrice(start.ShipTypes[s.Type].Price, s.Grade, avg)).ToList();
        int pick = BuildingMenu.Ask(this, $"매각 — 돈 {p.Gold}",
            p.Ships.Select((s, i) => $"{s.Name,-10} 선원 {s.Crew,3} {prices[i],7}").ToList());
        if (pick < 0) return;
        var ship = p.Ships[pick];
        // 「이 놈이라면 %ld에 인수하지, 괜찮소？」(0xD5) — 아니면 「뭐, 좋을 대로 하시오.」(0xD6) (0x00429E1D)
        if (MessageBox.Show(this, MsgOr(0xD5, $"이 놈이라면 {prices[pick]}에 인수하지, 괜찮소？", prices[pick]), ship.Name, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            Say(0xD6, "뭐, 좋을 대로 하시오.");
            return;
        }
        bool loaded = ship.Load > 0 || ship.Crew > 0;
        if (loaded && MessageBox.Show(this, $"{ship.Name}에 짐이나 선원이 있습니다. 그래도 팔까요?", "매각",
                                      MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (!_host.RemoveShip(pick)) return;
        p.SetGold(p.Gold + prices[pick]);
        Note($" · {ship.Name}{(KoreanBatchim(ship.Name) ? "을" : "를")} {prices[pick]}에 팔았습니다");
    }

    /// <summary>이름 뒤 토씨 — 받침이 있으면 <paramref name="with"/>, 없으면 <paramref name="without"/>.</summary>
    private static string Josa(string name, string with, string without) => name + (KoreanBatchim(name) ? with : without);

    private static bool KoreanBatchim(string s) =>
        s.Length > 0 && s[^1] is >= '가' and <= '힣' && (s[^1] - '가') % 28 != 0;

    /// <summary>
    /// 건물 고르기 — 목록은 <c>MENU.DAT</c> 그대로(<see cref="MenuTable.BuildingList"/>). 된 것은 항구 「출항 · 보급」 ·
    /// 교역소 「구입 · 매각」 · 조선소 「함선 건조」(돈을 내고 짓기, 열 척까지)이고, 나머지는 고르면 「아직」이라고 적는다.
    /// </summary>
    private void EnterBuilding(int b)
    {
        // 건물에 들면 볼일 뒤 시각이 rand(3) + 2 틱 흐른다(0x00431F63 → 0x0041A2C6). 출항했으면 바다 시각이다.
        // 여관에서 묵었으면 흐른 시각은 덮이고 아침 8시다(0x0041A2F2).
        int spent = _host.Roll(3) + 2;
        _slept = false;
        VisitBuilding(b);
        if (_host.CurrentPort >= 0 && !_slept) _host.PassTimeInPort(spent);
        UpdateStatus();
    }

    private bool _slept;

    /// <summary>개발 돌리기 중에는 말 창을 띄우지 않는다.</summary>
    private bool _devQuiet;

    private void VisitBuilding(int b)
    {
        // 그 항구 · 건물에 시나리오 이벤트가 걸려 있으면 그것이 먼저다(0x00431FD9 → 0x0043F8C0).
        var (name, family) = _host.HeroName;
        ScenarioVm.Chooser ask = (before, options) => ScenarioDialog.Ask(this, _host.Faces, before, options);
        if (_host.Scenario is { } vm) vm.Choose = ask;
        if (_host.Common is { } common) common.Choose = ask;
        _host.WriteShipSide();
        // 함께 쓰는 시나리오가 먼저다(0x00431FB5). 그것이 F8 로 볼일을 대신하면 목록은 뜨지 않지만 주인공 시나리오는 돈다.
        bool menu = true;
        if (_host.Common?.Enter(_host.CurrentPort, b, _host.Player.Gold, name, family) is { } shared)
        {
            if (shared.Pending.Count > 0) ScenarioDialog.Play(this, _host.Faces, shared.Pending);
            _host.Apply(shared);
            _host.WriteShipSide();
            menu = !shared.Stopped;
        }
        if (!menu && b != InnBuilding) { Note($" · 「{PortDoors.Names[b]}」 이벤트"); }
        if (_host.Scenario?.Enter(_host.CurrentPort, b, _host.Player.Gold, name, family) is { Lines.Count: > 0 } run)
        {
            ScenarioDialog.Play(this, _host.Faces, run.Pending);
            _host.Apply(run);
            Note($" · 「{PortDoors.Names[b]}」 이벤트"
                 + string.Concat(run.Joined.Select(p => $" · {Josa(_host.PersonName(p), "이", "가")} 동료가 되었습니다")));
            // F8 이 선 이벤트는 건물 볼일을 대신한다. 아니면, 또 여관이면 늘 목록이 이어서 뜬다(0x00431FF9).
            if (run.Stopped && b != InnBuilding) return;
        }
        if (!menu && b != InnBuilding) return;
        if (Watchmen(b)) return;
        // 흥정에서 쫓겨난 날은 조선소에 못 든다(0x0042A948, 말 0xF9).
        if (b == ShipyardBuilding && _host.ShipyardBanned) { Say(0xF9, "네 녀석은 꼴도 보기싫다. 두번 다시 이 곳에 얼굴도 내밀지 말아라."); return; }
        if (b == NpcBuilding) { AtPatron(); return; }
        var items = _host.Menus?.ForBuilding(b) ?? [];
        if (items.Count == 0) return;
        if (b == PalaceBuilding)
        {
            items = items.Take(4).ToList();   // 다섯째 「밀회」는 원본도 안 보인다(0x00427F13 push 4)
            if (!PalaceGate()) return;
        }
        if (b == ChurchBuilding)
        {
            // 모스크(칩 벌 2)는 오스만(나라 2)만, 교회는 오스만이 아닌 이만 받는다(0x00421970, 말 0x322 · 0x5A).
            bool mosque = _host.PortChipSet(_host.CurrentPort) == 2, ottoman = _host.HeroNation == 2;
            if (mosque && !ottoman) { Say(0x322, "당신이 코란을 구하는 마음은 잘 알겠지만, 이교도는 삼가해 주세요."); return; }
            if (!mosque && ottoman) { Say(0x5A, "당신의 깊은 신앙심은 잘 알겠습니다만, 이교도는 삼가해 주십시오."); return; }
            _worshipped = false;
        }
        if (b == PubBuilding) _pubMood = Pub.Mood(HeroField(Navigators.Stats + 6));
        // 교역소는 교역 명성이 1000 이상이면 이름으로 맞는다(0x0042174E — 말 1, 아니면 0).
        if (b == TradeBuilding && _host.Scenario is { } tvm)
        {
            bool famous = Palace.Fames(tvm.Memory, _host.HeroPersonNumber).Trade >= 1000;
            if (famous) Say(1, $"{name} {family}님이 아니십니까？", name, family);
            else Say(0, "어서 오세요！");
        }
        // 들어설 때 인사 — 이벤트 변수 63 이 둘 다 0 일 때(0x0041E856). 은행은 베네치아(13)면 본점(0x61), 아니면 「%s 지점」(0x62).
        bool quiet = (_host.Common?.GetVar(63) ?? 0) == 0 && (_host.Scenario?.GetVar(63) ?? 0) == 0;
        if (quiet && b == BankBuilding)
        {
            if (_host.CurrentPort == 13) Say(0x61, "어서 오세요, 셜록은행 베네치아 본점입니다.");
            else Say(0x62, $"어서 오세요, 셜록은행 {_host.SceneName} 지점입니다.", _host.SceneName);
        }
        if (b == ShipyardBuilding) Say(0x4D, "우리 조선소에 무슨 용건이 있는 거요？");   // 0x0042A980
        // 교회 · 모스크(0x004219E0) — 말은 교회 0x5B ~ 0x60, 모스크는 그 + 0x2C8(0x323 ~ 0x328). 들 때 0x5B, 나갈 때 0x60.
        if (quiet && b == ChurchBuilding)
            Say(0x5B + ChurchLines, IsMosque ? "알라신께 기부하시는 겁니까？" : "어서 오세요, 주님의 품으로！");
        if (quiet && b == InnBuilding) Say(0x42, "피곤하시지요？\n그럼 편히 쉬세요.");     // 0x0041F3AB
        if (quiet && b == GuildBuilding) Say(0x55, "무슨 용건이오？");                    // 0x0041EF8B
        if (quiet && b == PubBuilding) PubGreeting();
        if (quiet && b == ItemBuilding)                                                   // 0x00420270 — 시각 12 ~ 17 틱(4 ~ 6 시)은 비밀 가게
        {
            if (_host.Fleet?.TimeOfDay is >= 12 and < 18) Say(0x2FC, "비밀 아이템 가게에 어서 오세요！\n흔히 볼 수 없는 진귀한 물건이 있지요.");
            else Say(0xEB, "어서 오세요.\n무슨 용건입니까？");
        }
        if (b == HarborBuilding) AtHarbor();
        // 조합 — 깃발 16 · 17 이 둘 다 섰으면 「~왕이 당신을 찾고 있어.」(0x0041E99B)
        if (b == GuildBuilding && _host.Common is { } gc && gc.GetFlag(16) && gc.GetFlag(17) && _host.HeroNation < 6)
            Say(-1, KingCalls(_host.HeroNation) + MsgOr(0x3A4, "당신을 찾고 있어."));
        if (b == FortuneBuilding) Say(0x12A, "점술관에 어서 오세요.");                     // 0x00428402
        // 건물 임자 그림(0x00436620) — 모스크는 교회 자리에 제 그림이 있다.
        var keeper = _host.Graphs?.Get(AssetPack.KeeperGraph(b, _host.PortChipSet(_host.CurrentPort) == 2));
        int pick = BuildingMenu.Ask(this, PortDoors.Names[b], items, keeper);
        if (pick < 0)
        {
            if (b == BankBuilding) Say(0xA4, "감사합니다.");   // 0x0041E8E3
            if (b == ChurchBuilding) Say(0x60 + ChurchLines, IsMosque ? "당신에게 알라신의 은총이 깃드시길！" : "성부 성자 성신의 이름으로 아멘.");
            return;
        }
        if (b == HarborBuilding && pick == 0) { PutToSea(); return; }
        if (b == TradeBuilding && pick == 0) { ShowGoods(); return; }
        if (b == TradeBuilding && pick == 1) { SellGoods(); return; }
        if (b == TradeBuilding && pick == 2) { Invest(industry: false); return; }
        if (b == TradeBuilding && pick == 3) { ShowPriceIndex(); return; }
        if (b == ShipyardBuilding && pick == 5) { Invest(industry: true); return; }
        if (b == ShipyardBuilding && pick == 4) { Refit(); return; }
        if (b == ShipyardBuilding && pick == 0) { ShowShipyard(); return; }
        if (b == ShipyardBuilding && pick == 1) { BuyUsedShip(); return; }
        if (b == ShipyardBuilding && pick == 2) { RepairShips(); return; }
        if (b == ShipyardBuilding && pick == 3) { SellShip(); return; }
        if (b == PubBuilding && pick == 0) { HireCrew(); return; }
        if (b == PubBuilding && pick == 1) { FireCrew(); return; }
        if (b == PubBuilding && pick == 2) { TreatBar(); return; }
        if (b == PubBuilding && pick == 3) { TalkToNavigator(pub: true); return; }
        if (b == PubBuilding && pick == 4) { TalkToBarmaid(); return; }
        if (b == PubBuilding && pick == 5) { Gamble(); return; }
        if (b == InnBuilding && pick == 0) { TalkToNavigator(pub: false); return; }
        if (b == InnBuilding && pick == 1) { ShowSupport(); return; }
        if (b == InnBuilding && pick == 2 && _host.Fleet is { } f)
        {
            f.SleepUntilMorning();
            _slept = true;
            Note($" · 하룻밤 묵었습니다 — {f.Date} {Fleet.Clock(f.TimeOfDay)}");
            return;
        }
        if (b == BankBuilding) { UseBank(pick); return; }
        if (b == ChurchBuilding) { AtChurch(pick); return; }
        if (b == PalaceBuilding)
        {
            AtPalace(pick);
            // 왕궁 일을 마칠 때마다 작위가 9(공작)이고 아직 엔딩 전이면(0x0045A1E0 & 2) 엔딩 0xF(0x00427F4E).
            if (_host.HeroRank == 9 && !_host.StoryOver) OnStoryEnded(0xF);
            return;
        }
        if (b == GuildBuilding && pick == 0) { OfferJobs(); return; }
        if (b == GuildBuilding && pick == 1) { NationInfo(); return; }
        if (b == FortuneBuilding) { TellFortune(pick); return; }
        if (b == ItemBuilding && pick == 0) { BuyItem(); return; }
        if (b == ItemBuilding && pick == 1) { SellItem(); return; }
        if (b == HarborBuilding && pick == 1) { ShowSupplies(); return; }
        if (b == HarborBuilding && pick == 2) { AtDock(); return; }
        _doorNote = $" · 「{items[pick]}」은 아직 없습니다";
        UpdateStatus();
    }

    /// <summary>이 항구에 든 뒤 여급에게 자랑했는지(<c>0x0049F5A4</c> 의 0x40).</summary>
    private bool _boasted;
    private int _boastPort = -1;

    /// <summary>
    /// 주점 「여급과 대화」 — 목록 13(업무정보 · 그녀의취향 · 그녀의연령, <c>0x00426910</c>)과 목록 14(자랑 · 선물, <c>0x004269A0</c>)를
    /// 한 목록으로 늘어놓는다. 업무정보는 <see cref="JobInfo"/>(<c>0x00426410</c>)다.
    /// </summary>
    private const int BarmaidFee = 10;

    private void TalkToBarmaid()
    {
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        int maid = Barmaids.InPort(m, _host.CurrentPort);
        if (maid < 0) { Note(" · 여급이 없습니다"); return; }
        string name = Barmaids.Name(m, maid);
        // 주인이 여급을 소개하고(0x92), 말을 트는 데 금화 10 닢이 든다 — 모자라면 경리주임이 「제독님, 금화가 부족합니다.」(0x93, 0x00426A2D).
        Say(0x92, $"{name}(는) 우리집 간판 아가씨요.", name);
        if (_host.Player.Gold < BarmaidFee) { Say(0x93, "제독님, 금화가 부족합니다."); return; }
        _host.Player.SetGold(_host.Player.Gold - BarmaidFee);
        Say(0x18E, "무엇을 듣고 싶은가요？");
        string[] items = ["업무정보", "그녀의취향", "그녀의연령", "자  랑", "선  물"];
        while (true)
        {
            int pick = BuildingMenu.Ask(this, $"{name} — 호감 {Barmaids.Get(m, maid, Barmaids.Liking)}", items);
            if (pick < 0) return;
            switch (pick)
            {
                case 0: JobInfo(maid); break;
                case 1:
                    if (Barmaids.AskTaste(m, maid, Random.Shared.Next) is { } taste) Say(taste, "당신의 성의가 제일 기뻐요.");
                    else Say(0x193, "후훗！ 그것은 비밀이예요.");
                    break;
                case 2:
                    if (Barmaids.TellAge(m, maid) is { } age) Say(0x194, $"당신에게만 가르쳐 드리죠. {age}살예요.", age);
                    else Say(0x193, "후훗！ 그것은 비밀이예요.");
                    break;
                case 3: BoastTo(maid); break;
                default: GiftTo(maid); break;
            }
        }
    }

    /// <summary>
    /// 여급 「업무정보」(<c>0x00426410</c>) — 맡은 조합 일(SNR0 변수 6, 대상 항구 변수 17, 교역품 변수 18)의 귀띔.
    /// <code>
    ///   1 수송      대상 항구가 여기면 0x249. rand(150) ≤ 호감 + 주인공 +0x22 이면 그 항구의 지방(0x191 · 0x181 + 시장 · 0x192)
    ///   2 · 6 구입  같은 셈으로 — 그 교역품이 특산품이고 경제 ≥ +0x1B x 10 인 첫 항구(0x190), 없으면 파는 시장의 지방(0xCA · 0xCB)
    ///   5 0x24D · 9 0x24A · 10 0x24B · 11 0x24C   그 밖은 0x18F 「모르겠어요」
    /// </code>
    /// 원본은 주인공 +0x31 의 0x10 이면 늘 알려 준다. 3 은 함대 조사(아래)다. 7 · 8(왕궁 · 총독부 편지)은 나라와 항구 자리를 일러 준다.
    /// </summary>
    private void JobInfo(int maid) => JobInfoFor(maid);

    /// <summary>
    /// 여급 함대 조사(일 4 · 12, <c>0x00425FC0</c>) — 맡긴 것이 없으면 나라(0x216) · 함대 갈래(0x217)를 받아 맡기고 걸릴 날을 말한다(0x218).
    /// 날이 남았으면 0x219, 다 지났으면 그 나라 · 갈래의 함대마다 제독과 자리를 알린다(0x21A ~ 0x22A, <c>0x00426160</c>).
    /// 바다에 없는 함대는 「전멸」(0x22C). 하는 일은 움직임 갈래(+0x1B)마다 0x21E · 0x226 · 0x227 · 0x21F · 0x225 · 0x224, 짐이 있으면 0x220.
    /// 날은 바다에서 하루가 갈 때마다 준다.
    /// </summary>
    private void ProbeFleet(int maid)
    {
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        int probe = Barmaids.Get(m, maid, Barmaids.Probe);
        string[] nations = [.. NationNames, "해적"];
        string[] Kinds(int nation) => nation == 6 ? ["카리브해적", "코르세르", "사략선대"] : ["상선대", "경비함대", "외양함대"];
        if (probe == Barmaids.NoProbe)
        {
            // 호감이 80 / ((+0x12 & 1) + 1) 아래면 「안돼요. 바쁜걸요.」(0x215, 0x00426020)
            bool heroFlag = (Navigators.Get(m, _host.HeroPersonNumber, 0x31) & 0x10) != 0;
            if (!heroFlag && Barmaids.Get(m, maid, Barmaids.Liking) < 80 / ((Barmaids.Get(m, maid, Barmaids.Flags) & 1) + 1))
            {
                Say(0x215, "안돼요. 바쁜걸요.");
                return;
            }
            int nation = BuildingMenu.Ask(this, MsgOr(0x216, "어느 나라의 함대를 조사하면 되죠？"), [.. nations]);
            if (nation < 0) return;
            int kind = BuildingMenu.Ask(this, MsgOr(0x217, "어떤 함대인가요？"), [.. Kinds(nation)]);
            if (kind < 0) return;
            int days = Barmaids.Commission(m, maid, nation, kind, (HeroField(0x31) & 0x10) != 0, Random.Shared.Next);
            Say(0x218, $"알았어요. 조사해 두겠어요.\n그렇지만 {days}일 정도 걸릴걸요.", days);
            return;
        }
        if (Barmaids.Get(m, maid, Barmaids.ProbeDays) > 0) { Say(0x219, "죄송해요. 아직 조사가 끝나지 않았어요."); return; }
        int n = probe & 0xF, k = Math.Min(2, probe >> 4);
        string kindName = Kinds(n)[k];
        Say(0x21A, "그렇지, 요전에 의뢰한 조사는 끝났어요.");
        SayText((n == 6 ? "" : MsgOr(0x21B, $"{nations[Math.Min(n, 6)]}의 ", nations[Math.Min(n, 6)])) + MsgOr(0x21C, $"{kindName}에 관한 정보였었죠？", kindName));
        for (int j = Barmaids.ProbeFleets[k]; j < Barmaids.ProbeFleets[k + 1]; j++)
        {
            var fleet = _host.Others.FirstOrDefault(f => f.Index == n * 10 + j);
            int number = j - Barmaids.ProbeFleets[k] + 1;
            if (fleet == null) { Say(0x22C, $"제{number}함대는 전멸했습니다.", number); continue; }
            string admiral = _host.PersonName(fleet.Admiral);
            var (north, lat, east, lon) = Pub.Degrees((int)fleet.X, (int)fleet.Y);
            // 하는 일(0x004262C1, 손표 0x004263E8) — 사람은 성(+0x0D)으로 부른다.
            string PortName(int p) => _host.Ports is { } pts && p >= 0 && p < pts.Ports.Count ? pts.Ports[p].Name : $"#{p}";
            string Family(int p) => _host.PersonName(p).Split(' ').Last();
            string doing = fleet.Mode switch
            {
                0 or 1 or 2 => MsgOr(0x21E, $"{PortName(fleet.Goal)}(를) 향하고 있어요.", PortName(fleet.Goal)),
                3 => MsgOr(0x226, $"{PortName(fleet.Goal)}항에서 숨어 기다리고 있어요.", PortName(fleet.Goal)),
                4 => MsgOr(0x227, "상선대를 무차별 공격하고 있어요."),
                5 or 6 or 7 => MsgOr(0x21F, $"{Family(fleet.Goal)}의 함대를 노리고 있어요.", Family(fleet.Goal)),
                8 => MsgOr(0x225, $"{Family(fleet.Goal)}제독의 함대를 호위하고 있어요.", Family(fleet.Goal)),
                9 => MsgOr(0x224, $"{PortName(fleet.Goal)}항을 경호하고 있어요.", PortName(fleet.Goal)),
                _ => "",
            };
            string cargo = fleet.CargoGood >= 0 && _host.Economy is { } eco && fleet.CargoGood < eco.GoodsNames.Count
                ? "\n" + MsgOr(0x220, $"적하에 {eco.GoodsNames[fleet.CargoGood]}(를) 싣고 있다고 해요.", eco.GoodsNames[fleet.CargoGood]) : "";
            SayText(MsgOr(0x21D, $"{admiral}제독의 함대는 ", admiral) + doing + cargo + "\n"
                    + MsgOr(0x229, "숙련된 항해사의 말대로라면, 현재 위치는 ")
                    + MsgOr(north ? 0x7E : 0x7F, $"{(north ? "북위" : "남위")} {lat}도, ", lat)
                    + MsgOr(east ? 0x80 : 0x81, $"{(east ? "동경" : "서경")} {lon}도", lon)
                    + MsgOr(0x22A, "근처가 아닐까？"));
        }
        Barmaids.EndProbe(m, maid);
    }

    private void JobInfoFor(int maid)
    {
        if (_host.Common is not { } job || _host.Scenario is not { } vm || _host.Economy is not { } eco || _host.Ports is not { } ports)
        {
            Say(0x18F, "죄송해요, 모르겠어요.");
            return;
        }
        int kind = job.GetVar(6), port = job.GetVar(17) & 0xFF, good = job.GetVar(18);
        bool Told() => Random.Shared.Next(150) <= Barmaids.Get(vm.Memory, maid, Barmaids.Liking) + HeroField(Navigators.Stats + 6)
                       || (HeroField(0x31) & 0x10) != 0;
        string Region(int market) => MsgOr(0x181 + market, "");
        switch (kind)
        {
            // 일 4 · 12 는 함대 조사(0x00426428 → 0x00426040).
            case 4 or 12: ProbeFleet(maid); return;
            // 일 3 은 그 항구 자리(0x0042650F) — 「%s항은 」(0x24E) + 북위 · 동경 + 「근처예요.」(0x252).
            case 3 when port < ports.Ports.Count:
            {
                var (north, lat, east, lon) = Pub.Degrees(ports.Ports[port].X, ports.Ports[port].Y);
                SayText(MsgOr(0x24E, $"{ports.Ports[port].Name}항은 ", ports.Ports[port].Name)
                        + MsgOr(north ? 0x7E : 0x7F, $"{(north ? "북위" : "남위")} {lat}도, ", lat)
                        + MsgOr(east ? 0x80 : 0x81, $"{(east ? "동경" : "서경")} {lon}도", lon)
                        + MsgOr(0x252, "근처예요."));
                return;
            }
            case 5: Say(0x24D, "주점 손님 중에 아는 사람이 있을지도 모르죠."); return;
            case 9: Say(0x24A, "그 항구의 교역소나 조선소에 투자하는 것이 좋겠지요."); return;
            case 10: Say(0x24B, "부락 주민들의 힘을 빌리면 발견할 수 있을 지도 모르겠네요."); return;
            // 일 11 희귀품(0x00426474) — 변수 18 이 보물, 18 − 10 이 그 지도. 지도를 가졌으면 「아！ 이것이 %s …」(0x3FA),
            //   보물을 가졌으면 「%s(를) 발견했군요！」(0x3FB) 뒤 그날 처음이면 호감 +10. 아니면 변수 17 이 없을 때 0x24C, 있으면 0x24D.
            case 11:
            {
                var owned = ItemShop.Owned(vm.Memory);
                int treasure = good & 0xFF;
                if (owned.Contains(treasure - 10)) { SayText(MsgOr(0x3FA, "", ItemShop.Get(vm.Memory, treasure - 10).Name)); return; }
                if (owned.Contains(treasure))
                {
                    SayText(MsgOr(0x3FB, "", ItemShop.Get(vm.Memory, treasure).Name));
                    if (_boastPort != _host.CurrentPort) { _boastPort = _host.CurrentPort; _boasted = false; }
                    if (!_boasted) { Barmaids.Admire(vm.Memory, maid); _boasted = true; }
                    return;
                }
                if ((job.GetVar(17) & 0xFF) == 0xFF) Say(0x24C, "조합에 가서 물어 보는 게 어때요");
                else Say(0x24D, "주점 손님 중에 아는 사람이 있을지도 모르죠.");
                return;
            }
            case 1 or 7 or 8 when port == _host.CurrentPort: Say(0x249, "어머, 이 항구에 일 때문에 왔나요？"); return;
            case 7 or 8 when port < ports.Ports.Count && Told():
            {
                // 왕궁 · 총독부 편지(0x0042672B) — 변수 18 이 나라. 4 아래면 「왕궁」(0x24F), 아니면 「총독부」(0x250), 그리고 항구 자리.
                string nation = good >= 0 && good < NationNames.Length ? NationNames[good] : "해적";
                string name = ports.Ports[port].Name;
                var (north, lat, east, lon) = Pub.Degrees(ports.Ports[port].X, ports.Ports[port].Y);
                SayText(MsgOr(good >= 4 ? 0x250 : 0x24F, $"{nation}의 {(good >= 4 ? "총독부" : "왕궁")}은 ", nation)
                        + MsgOr(0x251, $"{name}항요？ ", name)
                        + MsgOr(north ? 0x7E : 0x7F, $"{(north ? "북위" : "남위")} {lat}도, ", lat)
                        + MsgOr(east ? 0x80 : 0x81, $"{(east ? "동경" : "서경")} {lon}도", lon)
                        + MsgOr(0x252, "근처예요."));
                return;
            }
            case 1 when port < ports.Ports.Count && port < eco.States.Count && Told():
            {
                string name = ports.Ports[port].Name;
                SayText(MsgOr(0x191, $"{name}(는) ", name) + Region(eco.States[port].Zone) + MsgOr(0x192, " 지방의 항구라고 항해자가 말했었어요."));
                Note($" · {name} — {Region(eco.States[port].Zone)}");
                return;
            }
            case 2 or 6 when good >= 0 && good < eco.GoodsNames.Count && Told():
            {
                string goods = eco.GoodsNames[good];
                var raw = eco.StateBytes;
                for (int i = 0; i < 40 && i < ports.Ports.Count; i++)
                {
                    int o = i * Economy.StateSize;
                    if (raw[o + 0x1A] == good && BitConverter.ToUInt16(raw, o) >= raw[o + 0x1B] * 10)
                    {
                        Say(0x190, $"{goods}(는) {ports.Ports[i].Name}항에서 팔고 있었다고, 언젠가 교역상인이 말했어요.", goods, ports.Ports[i].Name);
                        return;
                    }
                }
                // 원본은 시장 12 에서 0 으로 훑으며 맞는 것을 덮어써 가장 낮은 번호가 남는다.
                for (int market = 0; market < eco.Markets.Count; market++)
                    if (eco.Markets[market].Goods.Contains(good))
                    {
                        SayText(MsgOr(0xCA, $"{goods}(이라면) ", goods) + Region(market) + MsgOr(0xCB, " 지역에서 팔고 있었다고, 언젠가 교역상인이 말하던데요."));
                        Note($" · {goods} — {Region(market)}");
                        return;
                    }
                break;
            }
        }
        Say(0x18F, "죄송해요, 모르겠어요.");
    }

    /// <summary>「자랑」 — 찾은 발견물 하나를 골라 그림과 함께 보여 준다(<see cref="Barmaids.Boast"/>).</summary>
    private void BoastTo(int maid)
    {
        if (_host.Scenario is not { } vm) return;
        var list = Barmaids.Boastable(vm.Memory);
        if (list.Count == 0) { Say(0x138, "마치 본 것처럼 말하자만 안 속아요."); return; }
        int pick = BuildingMenu.Ask(this, "자랑", list.Select(i => _host.Colony?.Name(i) ?? $"발견물 {i}").ToList());
        if (pick < 0) return;
        int disc = list[pick];
        if (_boastPort != _host.CurrentPort) { _boastPort = _host.CurrentPort; _boasted = false; }   // 다른 항구에 들었다
        int reply = Barmaids.Boast(vm.Memory, maid, disc, ref _boasted);
        var lines = new List<ScenarioVm.Line>
        {
            new(_host.Colony?.Name(disc) ?? "", _host.Colony?.Text(disc) ?? "",
                Face: _host.Colony?.Picture(disc) is >= 0 and var pic ? FaceSheet.FindFirst + pic : -1),
            new("", MsgOr(0x139, "와！ 신기해라！")),
        };
        if (reply >= 0) lines.Add(new("", MsgOr(reply, "")));
        ScenarioDialog.Play(this, _host.Faces, lines);
    }

    /// <summary>「선물」 — 옷 · 악세서리 · 보석(아이템 갈래 10 · 11)을 하나 내준다(<see cref="Barmaids.Gift"/>).</summary>
    private void GiftTo(int maid)
    {
        if (_host.Scenario is not { } vm) return;
        var list = Barmaids.Giftable(vm.Memory);
        if (list.Count == 0)
        {
            int none = (Barmaids.Get(vm.Memory, maid, Barmaids.Flags) & 1) == 0 ? 0x14B : 0x14A;
            Say(none, "어머！ 가지고 싶은 물건이 없는데요.");
            return;
        }
        int pick = BuildingMenu.Ask(this, "선물", list.Select(i => ItemShop.Get(vm.Memory, i).Name).ToList());
        if (pick < 0) return;
        var ids = Barmaids.Gift(vm.Memory, maid, list[pick]);
        ScenarioDialog.Play(this, _host.Faces, [.. ids.Select(id => new ScenarioVm.Line("", MsgOr(id, "")))]);
    }

    /// <summary>
    /// 항 「도크」 — 목록 8(보관 · 배비 · 교환, <c>0x00423990</c>). 맡긴 수(0x114 · 0x115)와 더 맡길 수(0x116)를 먼저 말한다.
    /// </summary>
    private void AtDock()
    {
        var p = _host.Player;
        int port = _host.CurrentPort;
        while (true)
        {
            int here = p.DockedAt(port).Count;
            int room = here + p.DockRoom(port);
            string head = (here > 0 ? MsgOr(0x114, $"현재 도크에 {here}척 보관하고 있소.", here) : MsgOr(0x115, "현재 도크는 텅텅 비어 있소."))
                          + " " + MsgOr(0x116, $"모두 해서 {room}척까지 맡길 수 있소.", room);
            int pick = BuildingMenu.Ask(this, head.Replace('\n', ' '), ["보  관", "배  비", "교  환"]);
            if (pick < 0) return;
            if (pick == 0) StoreShip(port, swap: false);
            else if (pick == 1) DeployShip(port);
            else StoreShip(port, swap: true);
        }
    }

    private string MsgOr(int id, string fallback, params object[] args) =>
        _host.Messages is { } m && m[id].Length > 0 ? m.Format(id, args) : fallback;

    /// <summary>
    /// 「보관」(<c>0x004232F0</c>) · 「교환」(<c>0x00423780</c>) — 기함밖에 없으면 0x117, 도크가 차면 0x120.
    /// 고른 배의 선원이 있으면 해고하느냐(0x119), 없으면 이 배냐(0xC9)를 묻는다. 교환이면 맡긴 배 하나를 대신 꺼낸다.
    /// </summary>
    private void StoreShip(int port, bool swap)
    {
        var p = _host.Player;
        if (p.Ships.Count <= 1) { Say(0x117, "기함밖에 없습니다."); return; }
        var docked = p.DockedAt(port);
        if (swap && docked.Count == 0) { Say(0x11B, "보관하고 있는 배는 없습니다."); return; }
        if (!swap && p.DockRoom(port) == 0) { Say(0x120, "이제 도크는 꽉 차있습니다."); return; }
        int take = -1;
        if (swap)
        {
            take = BuildingMenu.Ask(this, MsgOr(0x11C, "어느 배를 준비하겠습니까？"), docked.Select(ShipLine).ToList());
            if (take < 0) return;
        }
        int pick = BuildingMenu.Ask(this, MsgOr(0x118, "어느 것을 맡길까요？"), p.Ships.Skip(1).Select(ShipLine).ToList());
        if (pick < 0) return;
        var ship = p.Ships[pick + 1];
        string ask = ship.Crew > 0 ? MsgOr(0x119, $"선원 {ship.Crew}명을 해고하게 됩니다만, 좋습니까？", ship.Crew) : MsgOr(0xC9, "이 배로 하겠소？");
        if (MessageBox.Show(this, ask, "도크", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (swap)
        {
            // 맡기는 배를 함대에서 빼고(선장은 쉰다), 그 선장이 꺼내는 배를 맡은 뒤, 뺀 배를 도크에 넣는다 — 도크 수는 그대로다.
            int captain = ship.Captain;
            if (!_host.RemoveShip(pick + 1)) return;
            ship.Crew = 0;
            ship.Captain = -1;
            _host.UndockShip(port, take, captain >= 0 ? captain : _host.FreeMate);
            p.Docked.Add((port, ship));
            Say(0x11F, "그럼 바꿔 넣어 두겠습니다.");
            return;
        }
        if (_host.DockShip(pick + 1, port)) Say(0x11A, "그럼, 맡아 두겠습니다.");
    }

    /// <summary>「배비」(<c>0x004234B0</c>) — 맡긴 배가 없으면 0x11B, 맡을 동료가 없으면 0x11D.</summary>
    private void DeployShip(int port)
    {
        var p = _host.Player;
        var docked = p.DockedAt(port);
        if (docked.Count == 0) { Say(0x11B, "보관하고 있는 배는 없습니다."); return; }
        if (p.Ships.Count >= SeaMapHost.MaxShips) { Note(" · 함대가 다 찼습니다"); return; }
        int pick = BuildingMenu.Ask(this, MsgOr(0x11C, "어느 배를 준비하겠습니까？"), docked.Select(ShipLine).ToList());
        if (pick < 0) return;
        int mate = _host.FreeMate;
        if (mate < 0 && _host.HasMates) { Say(0x11D, "선장이 될 만한 사람은 없습니다만."); return; }
        if (_host.UndockShip(port, pick, mate)) Say(0x11E, "그럼 예항시켜 두겠습니다.");
    }

    private string ShipLine(PlayerState.Ship s) =>
        $"{s.Name}  내구 {s.Hull}/{s.MaxHull} · 선원 {s.Crew} · 짐 {s.Load}/{s.Room}";

    /// <summary>
    /// 「NPC」 — 그 항구의 임자(<see cref="Patrons"/>). 귀족이면 「계약 · 발견물 보고 · 모험소문」(목록 24, <c>0x00421E90</c>),
    /// 학자면 「계약 · 기능 습득 · 지리 보고 · 지도확인」(목록 25). 계약하지 않았으면 보고 · 소문을 못 고르고, 했으면 계약을 못 고른다
    /// (<c>0x0049EF44</c> = 6 · 1).
    /// </summary>
    private void AtPatron()
    {
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        int who = Patrons.InPort(m, _host.CurrentPort);
        if (who < 0) { Say(0x3A5, "제독님, 이 건물엔 열쇠가 잠겨 있어 들어갈 수 없습니다."); return; }   // 0x00422949
        string name = Patrons.Name(m, who);
        int kind = Patrons.KindOf(m, who);
        if (kind is 2 or 3) { TeachSkill(kind == 2); return; }   // 측량술 선생(0x00422590) · 포술 선생(0x00422750)
        if (kind is not (Patrons.Noble or Patrons.Scholar)) { Note($" · {name}"); return; }
        var (hero, family) = _host.HeroName;
        if (kind == Patrons.Scholar) Say(0x1ED, "내게 뭔가 용건이 있습니까？");
        else if (Patrons.IsContracted(m, who)) Say(0x1DE, $"오！ {family}님, 어서 들어오세요.", family);
        else Say(0x1DD, "우리 가문에 무슨 용건이 있으십니까？");
        string[] items = kind == Patrons.Noble ? ["계  약", "발견물 보고", "모험소문"] : ["계  약", "기능 습득", "지리 보고", "지도확인"];
        while (true)
        {
            bool bound = Patrons.IsContracted(m, who);
            var lines = items.Select((t, i) => (i == 0) == bound && !(kind == Patrons.Scholar && i is 1 or 3) ? t + " (안 됨)" : t).ToList();
            int pick = BuildingMenu.Ask(this, name, lines);
            if (pick < 0) return;
            if (lines[pick].EndsWith("(안 됨)")) continue;
            if (kind == Patrons.Noble)
            {
                if (pick == 0) ContractPatron(who, $"{hero} {family}");
                else if (pick == 1) ReportDiscovery(who);
                else TellRumour();
            }
            else
            {
                if (pick == 0) ContractScholar(who);
                else if (pick == 1) LearnCartography();
                else if (pick == 2) ReportSurvey(who);
                else AppraiseMap();
            }
        }
    }

    /// <summary>귀족 「모험소문」 — 부락 하나가 어디쯤 있는지(<see cref="Discoveries.Rumour"/>).</summary>
    private void TellRumour()
    {
        if (_host.Scenario is not { } vm) return;
        if (Discoveries.Rumour(vm.Memory, HeroField(Church.Luck), Random.Shared.Next) is not { } r)
        {
            Say(0x1EB, "정보는 별로 들어와 있지 않군요.");
            return;
        }
        string ew = r.East ? "동경" : "서경", ns = r.North ? "북위" : "남위";
        Say(0x1EC, $"{ew}{r.Lon}도, {ns}{r.Lat}도 근처에 부락이 있다고 하는 소문을 들었는데, 신빙성은 어느 정도일까？", ew, r.Lon, ns, r.Lat);
    }

    /// <summary>학자 「지도확인」 — 감정료 20000 을 내고 지도의 자리를 5 도 단위로 어림해 듣는다(<see cref="Discoveries.Appraise"/>).</summary>
    private void AppraiseMap()
    {
        if (_host.Scenario is not { } vm) return;
        var r = Discoveries.Appraise(vm.Memory, ItemShop.Owned(vm.Memory), Random.Shared.Next);
        if (r is not { } at) { Note(" · 감정할 지도가 없습니다"); return; }
        if (MessageBox.Show(this, MsgOr(0x311, "호오, 보물지도를 감정하시겠습니까？ 감정료는 금괴 ２개입니다만, 좋습니까？"), "지도확인",
                            MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var p = _host.Player;
        if (p.Gold <= Discoveries.AppraiseFee) { Say(0xEF, "손님, 돈이 모자랍니다."); return; }
        p.SetGold(p.Gold - Discoveries.AppraiseFee);
        Say(0x312, "그럼 지도를 보여 주십시오.\n어디 보자……");
        SayText(MsgOr(0x30F, "이 지도의 장소는 대략 ")
                + MsgOr(at.North ? 0x7E : 0x7F, $"{(at.North ? "북위" : "남위")} {at.Lat}도, ", at.Lat)
                + MsgOr(at.East ? 0x80 : 0x81, $"{(at.East ? "동경" : "서경")} {at.Lon}도", at.Lon)
                + MsgOr(0x310, "부근이라고 생각합니다."));
    }

    /// <summary>주인공이 지도작성술을 지녔는지 — 사람 +0x30 의 8(<c>0x00421F87</c>).</summary>
    private const int SkillField = 0x30, Cartography = 8;

    /// <summary>학자 「계약」(<c>0x00421F70</c>) — 지도작성술이 있어야 한다(0x1F0). 하면 보고 뒤 센 칸을 0 으로.</summary>
    private void ContractScholar(int who)
    {
        if (_host.Scenario is not { } vm) return;
        Say(0x1EF, "당신이 우리 지도제작소와 계약을 맺고 싶다는 사람입니까？");
        if ((HeroField(SkillField) & Cartography) == 0) { Say(0x1F0, "지도작성술을 가지고 있지 않군요. 그럼 계약을 맺을 수 없습니다."); return; }
        if (MessageBox.Show(this, MsgOr(0x1F1, "지금부터 답파할 지리 정보에 대해 금화를 지불하도록 하지요."), "계약", MessageBoxButton.YesNo)
            != MessageBoxResult.Yes) { Say(0x1F3, "그래요？ 그럼 마음이 변하면 언제라도 와 주시오."); return; }
        Patrons.Contract(vm.Memory, who, Patrons.Scholar);
        _host.Survey.SinceReport = 0;
        Say(0x1F4, "그럼, 근사한 보고를 기다리고 있겠습니다.");
    }

    /// <summary>
    /// 「기능 습득」(<c>0x00422010</c>) — 항해술 · 지식 · 직감이 다 75 이상이어야 하고(0x1F6), 값은
    /// min(60000, (5000 / (+0x22 / 5 + 1) + 200) x 100).
    /// </summary>
    private void LearnCartography()
    {
        if (_host.Scenario is not { } vm) return;
        Say(0x1F5, "지도작성술을 습득하고 싶은 것이군요.");
        string[] names = ["항해술", "지식", "직감"];
        for (int k = 0; k < 3; k++)
            if (HeroField(Navigators.Stats + 1 + k) < 75) { Say(0x1F6, $"지도작성에는 {names[k]}(가) 꼭 필요하죠.", names[k]); return; }
        int price = Math.Min(60000, (5000 / (HeroField(Navigators.Stats + 6) / 5 + 1) + 200) * 100);
        Say(0x1F7, $"기술을 습득하는 데는 금화 {price}닢이 필요합니다.", price);
        var p = _host.Player;
        if (p.Gold < price) { Say(0x1F8, "금화가 부족한 모양이군요."); return; }
        if (MessageBox.Show(this, MsgOr(0x1F9, "좋겠습니까？"), "기능 습득", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        p.SetGold(p.Gold - price);
        Navigators.Set(vm.Memory, _host.HeroPersonNumber, SkillField, HeroField(SkillField) | Cartography);
        Note(" · 지도작성술을 익혔습니다");
    }

    /// <summary>
    /// 기능을 가르치는 이(「NPC」 임자 갈래 2 · 3) — 측량술(<c>0x00422590</c>, 능력치 항해술 · 지식 · 직감이 70 · 80 · 70 이상, 기능 비트 0x10, 말 0x2EE · 0x2EF · 0x2F0 · 0x1F8 · 0x1F9 · 0x2F1)과
    /// 포술(<c>0x00422750</c>, 통솔 · 지식 · 용기 75 · 65 · 80, 비트 4, 말 0x2E7 · 0x2E8 · 0x2E9 · 0x2EB · 0x2EC · 0x2ED). 값은 지도작성술과 같은
    /// min(60000, (5000 / (매력 / 5 + 1) + 200) x 100)(0x1F7).
    /// </summary>
    private void TeachSkill(bool surveying)
    {
        if (_host.Scenario is not { } vm) return;
        string[] stats = ["통솔력", "항해술", "지식", "직감", "용기", "검술", "매력"];
        int[] which = surveying ? [1, 2, 3] : [0, 2, 4];
        int[] need = surveying ? [70, 80, 70] : [75, 65, 80];
        int bit = surveying ? 0x10 : 0x04;
        string bye() => surveying ? MsgOr(0x2F1, "별들이 항해의 길잡이가 되어 주겠지요.") : MsgOr(0x2ED, "목숨을 소중히 하게.");
        string offer = surveying ? MsgOr(0x2EE, "이 곳에선 측량술을 가르치고 있지요.\n경위도 측량술을 배우고 싶은 것이군요？")
                                 : MsgOr(0x2E7, "이 곳에선 포술을 가르치고 있지.\n포술을 배우고 싶소？");
        if (MessageBox.Show(this, offer.Replace("\\n", "\n"), surveying ? "측량술" : "포술", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            Say(-1, bye());
            return;
        }
        if ((HeroField(SkillField) & bit) != 0)
        {
            Say(surveying ? 0x2EF : 0x2E8, surveying ? "어？ 이미 익히신 것 같은데요." : "뭐야 이거！ 가르칠 필요도 없는 것 같군.");
            Say(-1, bye());
            return;
        }
        for (int k = 0; k < 3; k++)
            if (HeroField(Navigators.Stats + which[k]) < need[k])
            {
                string s = stats[which[k]];
                Say(surveying ? 0x2F0 : 0x2E9, $"{(surveying ? "측량술" : "포술")}을 습득하기 위해선 {s}(가) 필요하지. 능력이 {need[k]}(가) 되면 다시 오시오.", s, need[k]);
                Say(-1, bye());
                return;
            }
        int price = Math.Min(60000, (5000 / (HeroField(Navigators.Stats + 6) / 5 + 1) + 200) * 100);
        Say(0x1F7, $"기술을 습득하는 데는 금화 {price}닢이 필요합니다.", price);
        var p = _host.Player;
        if (p.Gold < price)
        {
            Say(surveying ? 0x1F8 : 0x2EB, surveying ? "금화가 부족한 모양이군요." : "돈도 없는 녀석을 가르칠 만큼 한가하진 않지.");
            Say(-1, bye());
            return;
        }
        if (MessageBox.Show(this, surveying ? MsgOr(0x1F9, "좋겠습니까？") : MsgOr(0x2EC, "좋겠나？"), "기능 습득", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            Say(-1, bye());
            return;
        }
        Say(-1, bye());
        p.SetGold(p.Gold - price);
        Navigators.Set(vm.Memory, _host.HeroPersonNumber, SkillField, HeroField(SkillField) | bit);
        Note(surveying ? " · 측량술을 익혔습니다" : " · 포술을 익혔습니다");
    }

    /// <summary>「지리 보고」(<c>0x00422150</c>) — 보고 뒤 칠한 칸 x 20 x (5 − 격)을 받고 명성이 칸 x 5 오른다.</summary>
    private void ReportSurvey(int who)
    {
        if (_host.Scenario is not { } vm) return;
        var sv = _host.Survey;
        if (sv.SinceReport == 0) { Say(0x1FA, "아니, 조사가 전혀 진척되어 있지 않군요. 더 분발해 주세요."); return; }
        if (sv.Total >= Survey.Complete) { SayWith(63, 0x1FB, "와아！！ 드디어 세계지도를 완성하셨군요！"); sv.Finish(); }
        int rank = vm.Memory.Read(ScenarioMemory.At(Patrons.Table, who, Patrons.Flags), false);
        int reward = Survey.Reward(rank, sv.SinceReport);
        var fp = ScenarioMemory.At(ScenarioMemory.HeroTable, _host.HeroPersonNumber, Patrons.FameField);
        int fame = Survey.FameGain(vm.Memory.Read(fp, true), sv.SinceReport);
        vm.Memory.Write(fp, vm.Memory.Read(fp, true) + fame, true);
        Say(0x1FC, $"수고하셨습니다. 그럼 보수로 금화 {reward}닢을 드리겠습니다.", reward);
        if (sv.Total < Survey.Complete) Say(0x1FD, "또 부탁합니다.");
        _host.Player.SetGold(_host.Player.Gold + reward);
        sv.SinceReport = 0;
        Note($" · 지리 보고 {reward} · 모험가 명성 +{fame}");
    }

    /// <summary>「계약」(<c>0x00421AA0</c>) — 0x1E0 · 0x1E1 을 묻고, 하면 0x1E3, 안 하면 0x1E2.</summary>
    private void ContractPatron(int who, string heroName)
    {
        if (_host.Scenario is not { } vm) return;
        var msg = _host.Messages;
        string ask = (msg?.Format(0x1E0, heroName) ?? $"그대가 계약을 맺고 싶다는 {heroName}씨인가？") + "\n"
                     + (msg?[0x1E1] ?? "다른 귀족에게 발견물을 팔아 넘길 수는 없는데, 괜찮겠소？");
        if (MessageBox.Show(this, ask, "계약", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            Say(0x1E2, "그러한가. 마음이 변하면 다시 찾아오게나.");
            return;
        }
        Patrons.Contract(vm.Memory, who);
        Say(0x1E3, "그럼, 근사한 보고를 기다리고 있겠네.");
    }

    /// <summary>「발견물 보고」(<c>0x00421B30</c>) — 찾은 것을 하나 골라 판다. 돈과 모험가 명성(주인공 표 +4)이 오른다.</summary>
    private void ReportDiscovery(int who)
    {
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        var list = Patrons.Reportable(m);
        if (list.Count == 0) { Say(0x1E4, "흠, 특별히 새로운 발견은 없었던 모양이군."); return; }
        Say(0x1E5, "무엇을 발견하셨나？");
        int pick = BuildingMenu.Ask(this, "발견물 보고", list.Select(i => _host.Colony?.Name(i) ?? $"발견물 {i}").ToList());
        if (pick < 0) return;
        int disc = list[pick];
        string name = _host.Colony?.Name(disc) ?? $"발견물 {disc}";
        int reward = Patrons.Report(m, who, disc, _host.HeroPersonNumber, out int fame, out int message);
        _host.Player.SetGold(_host.Player.Gold + reward);
        _host.WriteShipSide();
        Say(message, $"{name}？ 그럼, 금화 {reward}닢을 받아 주시요.", name, reward);
        Note(M(0x1DC, $"모험가의 명성이 {fame} 높아졌습니다.", fame));
    }

    private void SayText(string text) => ScenarioDialog.Play(this, _host.Faces, [new ScenarioVm.Line("", text)]);

    /// <summary>게임 말 하나를 대사 창에 띄운다 — 말이 없으면 <paramref name="fallback"/>.</summary>
    private void Say(int id, string fallback, params object[] args) => SayWith(-1, id, fallback, args);

    /// <summary>말 한 줄을 <c>GRAPH.DAT</c> 그림 <paramref name="graph"/> 와 함께 띄운다(-1 이면 그림 없이).</summary>
    private void SayWith(int graph, int id, string fallback, params object[] args)
    {
        if (_devQuiet) return;
        string text = _host.Messages is { } m && m[id].Length > 0 ? m.Format(id, args) : fallback;
        ScenarioDialog.Play(this, _host.Faces, [new ScenarioVm.Line("", text)], graph >= 0 ? _host.Graphs?.Get(graph) : null);
    }

    /// <summary>
    /// 점술가(<c>0x00428416</c>) — 복채 50, 인생운은 운(+0x23) / 40 에 따라 말 0x12E 부터 셋, 사업운은 다음 항해 · 전투 레벨까지
    /// 남은 경험(말 0x221 · 0x222, 경험표 <c>0x0041A490</c> = min(레벨, 18)² x 30 − 사람 +0x26 · +0x28).
    /// 연애운은 이 항구 여급의 호감(0x00428290), 동료운은 고른 동료의 운(0x00428340)이다.
    /// </summary>
    private void TellFortune(int pick)
    {
        if (_host.Scenario is not { } vm) return;
        var p = _host.Player;
        if (_host.Messages is { } m && MessageBox.Show(this, m[0x12B], "점술가", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (p.Gold < 50) { Say(0x12C, "돈이 모자랍니다."); return; }
        p.SetGold(p.Gold - 50);
        Say(0x12D, "그럼 점을 치겠습니다.");   // 0x004284A4
        int hero = _host.HeroPersonNumber;
        if (pick == 0)
        {
            int luck = Navigators.Get(vm.Memory, hero, Church.Luck);
            Say(0x12E + Math.Min(2, luck / 40), "평범한 운세인 것 같군요.");
            return;
        }
        if (pick == 1)
        {
            // 다음 레벨까지 — 경험표 0x0041A490 은 min(레벨, 18)² x 30, 경험은 사람 +0x26(항해) · +0x28(전투) 낱말.
            int Need(int level) => Math.Min(level, 18) * Math.Min(level, 18) * 30;
            int Word(int f) => vm.Memory.Read(ScenarioMemory.At(ScenarioMemory.PeopleTable, hero, f), true);
            int nav = Need(Navigators.Get(vm.Memory, hero, Navigators.NavLevel)) - Word(0x26);
            int battle = Need(Navigators.Get(vm.Memory, hero, Navigators.BattleLevel)) - Word(0x28);
            Say(0x221, $"항해 레벨까지 경험 {nav}", nav);
            Say(0x222, $"전투 레벨까지 경험 {battle}", battle);
            // 나라가 있으면(+0x31 & 0xF ≠ 6) 다음 작위까지의 명성도 말한다(0x223).
            // 이미 넉넉하면 「저의 수정구슬엔 당신을 찾는 고귀한 분의 모습이 비치고 있습니다.」(0x2F2, 0x00428279).
            if ((Navigators.Get(vm.Memory, hero, 0x31) & 0xF) != 6)
            {
                int fame = Palace.FameToNextRank(vm.Memory, _host.HeroPersonNumber, _host.HeroRank);
                if (fame > 0) Say(0x223, $"한 단계 위의 작위를 얻기 위해선 {fame} 정도의 명성이 필요하겠지요.", fame);
                else if (_host.HeroRank < 9) Say(0x2F2, "그리고, 저의 수정구슬엔 당신을 찾는 고귀한 분의 모습이 비치고 있습니다.");
            }
            return;
        }
        if (pick == 2)
        {
            // 연애운(0x00428290) — 이 항구 여급 가운데 +0x12 의 8 이 서고 0x40 이 아닌 이. 호감 80 · 50 으로 0x146 · 0x147 · 0x148.
            int maid = Enumerable.Range(0, Barmaids.Count).FirstOrDefault(i =>
                Barmaids.Get(vm.Memory, i, Barmaids.Port) == _host.CurrentPort
                && (Barmaids.Get(vm.Memory, i, Barmaids.Flags) & 0x48) == 0x08, -1);
            if (maid < 0) { Say(0x149, "이 항구에 있는 한, 사랑의 여신이 미소짓는 일은 없겠지요."); return; }
            int liking = Barmaids.Get(vm.Memory, maid, Barmaids.Liking);
            string name = Barmaids.Name(vm.Memory, maid);
            if (liking >= 80) Say(0x146, $"{name}(라고) 하는 여자분이 당신을 사모하고 있는 모양예요.", name);
            else if (liking >= 50) Say(0x147, "계속해서 노력하세요.");
            else Say(0x148, "우선 행동에 옮기세요.");
            return;
        }
        // 동료운(0x00428340) — 0x318 뒤 동료를 고르면 그 운(+0x23) / 25 로 말 0x4E 부터, 이어 호감(+0x2B) / 25 로 0x319 부터
        // (「떠나 버리겠지요」 ~ 「절대적인 충성심」).
        Say(0x318, "점치고 싶은 상대의 이름을 가르쳐 주세요.");
        var mates = Navigators.Mates(vm.Memory).Select(t => t.Person).ToList();
        if (mates.Count == 0) { Note(" · 동료가 없습니다"); return; }
        int who = BuildingMenu.Ask(this, "동료운", mates.Select(_host.PersonName).ToList());
        if (who < 0) return;
        Say(0x4E + Navigators.Get(vm.Memory, mates[who], Church.Luck) / 25, "당신 동료의 운세는 대체로 평범하다고 말씀드릴 수 있겠죠.");
        Say(0x319 + Navigators.Get(vm.Memory, mates[who], Navigators.Liking) / 25, "그는 당신에게 마음을 열기 시작했습니다.");
    }

    /// <summary>
    /// 칙명 귀띔(<c>0x0041EA90</c>) — 칙명 11(희귀품 찾기, 변수 18 아이템)이고 그것(또는 아이템 − 10)을 아직 안 가졌을 때만. 아니면 0x253.
    /// 있는 항구(변수 16)가 비었으면 rand(42) 로 잡는다. 그 항구에 있으면 아는 사람(변수 17 — 비었으면 사람 79 ~ 119 가운데 rand(변수 23 + 1) 이
    /// 0 이면 항구 0 ~ 41 에 있는 이, 아니면 42 위에 있는 이)을 알려 주고(0x254 · 0x255), 아니면 그 항구와 경위도를 알려 준다(0x256 · 0x258 · 0x259).
    /// </summary>
    private void RoyalHint(ScenarioVm job)
    {
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        int item = job.GetVar(18);
        var owned = ItemShop.Owned(m);
        if (job.GetVar(6) != 11 || owned.Contains(item) || owned.Contains(item - 10))
        {
            Say(0x253, "자네, 칙명을 띠고 있는 중이잖나？");
            return;
        }
        if (job.GetVar(16) == 0xFF) job.SetVar(16, _host.Roll(42));
        int at = job.GetVar(16);
        string itemName = ItemShop.Get(m, item).Name;
        if (at == _host.CurrentPort)
        {
            if (job.GetVar(17) == 0xFF)
            {
                bool near = _host.Roll(job.GetVar(23) + 1) == 0;
                for (int guard = 0; guard < 500; guard++)
                {
                    int who = _host.Roll(41) + 0x4F, where = Navigators.Get(m, who, Navigators.Port);
                    if (where == 0xFF || (where < 42) != near) continue;
                    job.SetVar(17, who);
                    break;
                }
            }
            int holder = job.GetVar(17);
            var full = _host.PersonName(holder).Split(' ');
            string first = full[0], family = full.Length > 1 ? full[^1] : "";
            Say(0x254, $"{itemName}(는) {first} {family}(가) 알고 있을걸？", itemName, first, family);
            Say(0x255, "어디에 있냐고？");
            return;
        }
        string portName = _host.Ports is { } pt && at < pt.Ports.Count ? pt.Ports[at].Name : $"#{at}";
        Say(0x256, $"{itemName}？", itemName);
        if (_host.Ports is { } ps && at < ps.Ports.Count)
        {
            var (north, lat, east, lon) = Pub.Degrees(ps.Ports[at].X, ps.Ports[at].Y);
            SayText(MsgOr(0x258, $"{portName}항은 ", portName)
                    + MsgOr(north ? 0x7E : 0x7F, $"{(north ? "북위" : "남위")} {lat}도, ", lat)
                    + MsgOr(east ? 0x80 : 0x81, $"{(east ? "동경" : "서경")} {lon}도", lon)
                    + MsgOr(0x259, "근처야."));
        }
    }

    /// <summary>
    /// 조합 「업무 알선」(<c>0x0041E900</c>) — 일은 함께 쓰는 시나리오(SNR0)가 장 하나로 돌린다.
    /// <code>
    ///   장 0(일 없음)  변수 0 ~ 2 가 고를 일 셋의 갈래, 3 ~ 5 가 그 짝. 고르면 변수 8 = 짝, 절 = 고른 차례 + 1 로
    ///                  조합(건물 6)의 이벤트를 돌리고(0x0041EA50), 끝나면 장 = 변수 6(맡은 일), 절 = 0
    ///                  깃발 16 · 17 이 다 서 있으면 나라 탓에 일을 주지 않는다
    ///   장 ≠ 0         변수 6 이 6 아래면 맡은 일이 있다 — 변수 16 이 이 항구면 말 0x3A3, 아니면 「○○ 의 일이 남았다」(0x152)
    /// </code>
    /// 변수 6 이 6 이상(칙명)이면 <see cref="RoyalHint"/>(0x0041EA90).
    /// </summary>
    private void OfferJobs()
    {
        if (_host.Common is not { } job) { Note(" · 지금은 맡길 일이 없습니다"); return; }
        int port = _host.CurrentPort;
        if (job.Chapter != 0)
        {
            int kind = job.GetVar(6), to = job.GetVar(16);
            if (kind >= 6) { RoyalHint(job); return; }
            string where = _host.Ports is { } pt && to < pt.Ports.Count ? pt.Ports[to].Name : $"#{to}";
            Note(to == port ? " · 맡은 일을 먼저 마치세요" : $" · {where}의 일이 남아 있습니다");
            return;
        }
        if (job.GetFlag(16) && job.GetFlag(17)) { Note(" · 조합이 당신 나라 사람에게는 일을 주지 않습니다"); return; }
        var offers = Enumerable.Range(0, 3).Select(i => job.GetVar(i)).ToList();
        int pick = BuildingMenu.Ask(this, "업무 알선", offers.Select(k => k < JobNames.Length ? JobNames[k] : $"일 {k}").ToList());
        if (pick < 0) return;
        job.SetVar(8, job.GetVar(3 + pick));
        job.Section = pick + 1;
        var (name, family) = _host.HeroName;
        _host.WriteShipSide();
        if (job.Enter(port, GuildBuilding, _host.Player.Gold, name, family) is { } run)
        {
            if (run.Pending.Count > 0) ScenarioDialog.Play(this, _host.Faces, run.Pending);
            _host.Apply(run);
        }
        job.Chapter = job.GetVar(6);
        job.Section = 0;
        string taken = offers[pick] < JobNames.Length ? JobNames[offers[pick]] : "그 일";
        Note(job.Chapter != 0 ? $" · 「{taken}」을 맡았습니다" : " · 일을 맡지 않았습니다");
    }

    /// <summary>왕궁 — 망명은 다른 나라에서, 자금 · 선박원조는 제 나라에서(<see cref="Palace"/>). 알현은 시나리오 몫이다.</summary>
    private void AtPalace(int pick)
    {
        if (_host.Scenario is not { } vm || _host.Ports is not { } ports || _host.CurrentPort < 0
            || _host.CurrentPort >= ports.Ports.Count) return;
        int nation = _host.PortNation(_host.CurrentPort);
        bool ours = nation == (_host.HeroNation & 7);
        if (pick == 1)
        {
            // 망명 — 다른 나라 왕궁에서만 뜻이 있다.
            if (ours) { Note(" · 이미 이 나라를 섬기고 있습니다"); return; }
            string name = nation < NationNames.Length ? NationNames[nation] : "이 나라";
            // 0x00427910 — 「아, %s공！ 잘 오셨네. 가신의 말로는 우리 나라에 충성을 맹세한다고？ 틀림없는가？」(0x1CA), 아니면 0x1CB, 그러면 0x1CC.
            var (hn, _) = _host.HeroName;
            if (MessageBox.Show(this, MsgOr(0x1CA, $"아, {hn}공！ 잘 오셨네. 가신의 말로는 우리 나라에 충성을 맹세한다고？ 틀림없는가？", hn), "망명",
                                MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                Say(0x1CB, "그러하냐？ 정말로 유감이군.");
                return;
            }
            Say(0x1CC, "그러하냐？ 기꺼이 수락하겠다.");
            Palace.Defect(vm.Memory, _host.HeroPersonNumber, nation);
            _homePort = _host.CurrentPort;
            Note($" · {name}의 사람이 되었습니다");
            return;
        }
        if (pick == 0) { Audience(ours); return; }
        if (!ours) { Note(" · 이 나라의 왕궁은 당신을 맞지 않습니다"); return; }
        if (pick == 3)
        {
            // 선박원조(0x00427B20) — 0x1CD 없음 · 0x213 함대 열 척 · 0x214 맡을 동료 없음, 받으면 0x1D0 · 이름(0x22B) · 0x1CF.
            int type = Palace.ShipAid(vm.Memory, nation);
            if (type < 0 || _host.StartTables is not { } st || type >= st.ShipTypes.Count) { Say(0x1CD, "지금 내어 줄 배는 없다고 합니다."); return; }
            if (_host.Player.Ships.Count >= SeaMapHost.MaxShips) { Say(0x213, "그대의 함대의 선박 수는 이미 １０척에 이르고 있어."); return; }
            int mate = _host.FreeMate;
            if (mate < 0) { Say(0x214, "선장으로 임명할 수 있는 동료가 없는 게 아니냐！"); return; }
            string ship = type < st.Templates.Count ? st.Templates[type].Name ?? "" : "";
            if (_host.Player.DockRoom(_host.CurrentPort) == 0) { Say(0x120, "이제 도크는 꽉 차있습니다."); return; }
            Say(0x1D0, $"폐하께선 {ship}급의 선박을 원조해 주시겠다고 합니다.", ship);
            // 받은 배는 이 항구 도크에 든다(0x00427C14) — 이름을 붙이고 「배는 조선소에 맡겨 두겠어요.」(0x228). 「배비」로 꺼낸다.
            if (_host.DockNewShip(type, _host.CurrentPort) is not { } aid) return;
            Palace.TookShip(vm.Memory, nation);
            string? named = TextDialog.Ask(this, MsgOr(0x22B, "배이름을 뭐라고 붙일까요？"), ship, 7);
            if (!string.IsNullOrWhiteSpace(named)) aid.Name = named.Trim();
            Say(0x228, "배는 조선소에 맡겨 두겠어요.");
            Say(0x1CF, "우리 나라를 위해 분발해 주십시오.");
            Note($" · {Josa(aid.Name, "을", "를")} 받아 도크에 맡겼습니다");
            return;
        }
        if (pick != 2) { Note(" · 그것은 아직 없습니다"); return; }
        int gold = _host.Player.Gold;
        int got = Palace.TakeFunds(vm.Memory, nation, ref gold);
        _host.Player.SetGold(gold);
        // 자금원조(0x00427A60) — 0x1CD 없음 · 0x212 돈이 넘침, 받으면 「폐하께선 금화 %d000닢의 원조금을 주시겠다고 합니다.」(0x1CE) · 0x1CF.
        if (got == 0) Say(0x1CD, "지금 내어 줄 자금은 없다고 합니다.");
        else if (got < 0) Say(0x212, "더는 가질 수 없습니다.");
        else
        {
            Say(0x1CE, $"폐하께선 금화 {got / 1000}000닢의 원조금을 주시겠다고 합니다.", got / 1000);
            Say(0x1CF, "우리 나라를 위해 분발해 주십시오.");
        }
    }

    /// <summary>
    /// 남의 나라 항구(<c>0x0043201A</c>) — 항 · 왕궁 · NPC · 교회 말고 건물에 들 때, 그 항구 나라(6 말고)에 대한 주인공 호의(주인공 표 +6 + 나라)가
    /// 80 아래면 「이 곳은 %s의 항구입니다. 부디 조심하세요！」(0x346). 다만 세 번에 한 번, rand(100) 이 호의 + 20 보다 크면 대신 잡으러 온다
    /// (<c>0x00432190</c>): 0x347 · 0x348(성). 능력치 +0x21 x 전투 레벨 / 100 + 능력치 +0x21 이 rand(150) 보다 작으면 잡혀(0x349)
    /// 돈의 3/4 를 내고(0x34D) 그 나라 호의가 20 오른다. 아니면 빠져나온다(0x34A · 0x34B). 잡으러 오면 건물 볼일은 없다(참).
    /// </summary>
    private bool Watchmen(int b)
    {
        if (b is HarborBuilding or PalaceBuilding or NpcBuilding or ChurchBuilding) return false;
        if (_host.Scenario is not { } vm || _host.CurrentPort < 0) return false;
        int nation = _host.PortNation(_host.CurrentPort);
        if (nation is < 0 or >= 6) return false;
        int hero = _host.HeroPersonNumber;
        var favourAt = ScenarioMemory.At(ScenarioMemory.HeroTable, hero, 6 + nation);
        int favour = vm.Memory.Read(favourAt, false);
        if (favour >= 80) return false;
        if (_host.Roll(3) != 0 || _host.Roll(100) <= favour + 20)
        {
            Say(0x346, $"제독님, 이 곳은 {NationNames7[nation]}의 항구입니다.\n부디 조심하세요！", NationNames7[nation]);
            return false;
        }
        var (_, family) = _host.HeroName;
        Say(0x347, "제독님, 수상한 놈들이 접근해 오는데요.");
        Say(0x348, $"네 이놈！ 악명 높은 {family}(렸다). 우리 나라에 진입하다니 대담하구나！", family);
        int wit = Navigators.Get(vm.Memory, hero, 0x21), level = Navigators.Get(vm.Memory, hero, Navigators.BattleLevel);
        if (wit * level / 100 + wit < _host.Roll(150))
        {
            Say(0x349, "아뿔사！ 포위당했다.");
            Say(0x34D, "지금까지 네 놈이 저지른 죄에 대한 벌금으로, 소지금의 ４분의 ３을 몰수하겠다. 더이상 우리 나라에 관여하지 말아라！");
            int gold = _host.Player.Gold;
            _host.Player.SetGold(gold - gold * 3 / 4);
            vm.Memory.Write(favourAt, (favour + 20) & 0xFF, false);
            Note($" · 벌금 {gold * 3 / 4}");
        }
        else
        {
            Say(0x34A, "이런 곳에서 체포될 순 없지.");
            Say(0x34B, "휴우, 간신히 도망쳐 나왔군요.");
        }
        return true;
    }

    /// <summary>왕궁 「알현」 이 시나리오에 넘기는 건물 번호(<c>0x0042785A</c> 의 0x15).</summary>
    private const int AudienceBuilding = 0x15;

    /// <summary>
    /// 왕궁 「알현」(<c>0x00427800</c>) — 작위 · 임무는 시나리오가 한다. 건물 0x15 의 이벤트를 함께 쓰는 것(SNR0)과 주인공 것 차례로 돌리고,
    /// 이벤트가 볼일을 대신하지 않았으면 목록 55(세력상황 · 사략허가서 · 면세증)를 띄운다. 제 나라가 아니면 사략허가서 · 면세증은 못 고른다.
    /// </summary>
    private void Audience(bool ours)
    {
        var (name, family) = _host.HeroName;
        bool menu = true;
        foreach (var vm in new[] { _host.Common, _host.Scenario })
        {
            if (vm?.Enter(_host.CurrentPort, AudienceBuilding, _host.Player.Gold, name, family) is not { } run) continue;
            if (run.Pending.Count > 0) ScenarioDialog.Play(this, _host.Faces, run.Pending);
            _host.Apply(run);
            _host.WriteShipSide();
            if (run.Stopped) menu = false;
        }
        if (!menu) { Note($" · 작위 {_host.HeroRank}"); return; }
        string[] items = ["세력상황", ours ? "사략허가서" : "사략허가서 (안 됨)", "면세증"];
        while (true)
        {
            int pick = BuildingMenu.Ask(this, "알현", items);
            if (pick < 0) return;
            if (pick == 0) ShowInfluence();
            else if (pick == 2) BuyPermit(ours);
            else if (ours) GetLetter();
        }
    }

    /// <summary>
    /// 「사략허가서」(<c>0x004274C0</c>) — 제 나라 왕궁에서만. 이미 가졌으면 0x360, 칸이 차면 0x361. 0x362 뒤 해적 명성을 본다
    /// (<see cref="Palace.LetterRefusal"/>). 되면 0x365 · 0x366(%s공) · 0x367 과 함께 값 없이 준다.
    /// </summary>
    private void GetLetter()
    {
        if (_host.Scenario is not { } vm || _host.Ports is not { } ports) return;
        var m = vm.Memory;
        int item = Palace.LetterOf(_host.PortNation(_host.CurrentPort));
        var owned = ItemShop.Owned(m);
        if (owned.Contains(item)) { Say(0x360, "제독님, 사략허가서는 이미 가지고 있는데요."); return; }
        if (owned.Count >= ItemShop.OwnedSlots) { Say(0x361, "제독님, 아이템이 너무 많습니다. 처분한 다음에 합시다."); return; }
        Say(0x362, "사략허가서를 교부받고 싶습니까？");
        if (Palace.LetterRefusal(m, _host.HeroPersonNumber) is var no && no != 0)
        {
            Say(no, "당신은 해적으로서의 지명도가 부족한 모양이군요.");
            return;
        }
        var (_, family) = _host.HeroName;
        Say(0x365, "그럼 폐하께 모셔다 드리지요.");
        Say(0x366, $"{family}공, 어서 오시오！", family);
        if (ItemShop.Give(m, item)) Say(0x367, "그러면 교부하도록 하지. 자, 받으시오.");
    }

    /// <summary>
    /// 「면세증」(<c>0x00427620</c>) — 이 나라 것을 이미 가졌으면 0x368, 아이템 칸이 차면 0x361. 0x369(반 년 · 4월과 10월 갱신),
    /// 갱신이 가까우면(달 mod 6 &lt; 3) 0x36A 뒤 0x36B 로 묻고 값(0x36D, <see cref="Palace.PermitPrice"/>)을 받는다. 돈이 모자라면 0x36E.
    /// </summary>
    private void BuyPermit(bool ours)
    {
        if (_host.Scenario is not { } vm || _host.Ports is not { } ports || _host.Fleet is not { } f) return;
        var m = vm.Memory;
        int nation = _host.PortNation(_host.CurrentPort);
        int item = Palace.PermitOf(nation);
        var owned = ItemShop.Owned(m);
        if (owned.Contains(item)) { Say(0x368, "제독님, 이 나라의 면세증은 이미 가지고 있는데요."); return; }
        if (owned.Count >= ItemShop.OwnedSlots) { Say(0x361, "제독님, 아이템이 너무 많습니다. 처분한 다음에 합시다."); return; }
        Say(0x369, "면세증의 유효기한은 반 년이고, ４월과 １０월에 갱신하지 않으면 안됩니다.");
        if (f.Month % 6 < 3) Say(0x36A, "지금 교부 받아도, 곧 갱신해야 합니다.");
        if (MessageBox.Show(this, MsgOr(0x36B, "면세증 교부를 신청하시겠습니까？"), "면세증", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        int price = Palace.PermitPrice(ours, _host.HeroRank);
        if (price > 0 && MessageBox.Show(this, MsgOr(0x36D, $"교부에는 금화 {price}닢이 들지. 괜찮소？", price / 10000), "면세증",
                                         MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var p = _host.Player;
        if (price > p.Gold) { Say(0x36E, "제독님, 금화가 부족합니다."); return; }
        if (!ItemShop.Give(m, item)) return;
        p.SetGold(p.Gold - price);
        Say(0x367, "그러면 교부하도록 하지. 자, 받으시오.");
    }

    /// <summary>「세력상황」 — 나라마다 몇 항구가 그 나라 것인지(항구 표 +0x15).</summary>
    private void ShowInfluence()
    {
        // 세력상황(0x00426FB4) — 왕이 「아, %s공！ … 세력상황을 가르쳐 드리겠소.」(0x1C5), 「우리 나라의 동맹항의 총수는 %d이다.」(0x373),
        // 바다 갈래 여덟(말 0x181 ~ 0x188, 항구 형편 +0x1C)을 고를 때마다 그 갈래의 이 나라 항구를 「항구명 지지율 공업가치 상업가치」(0x1DB)로,
        // 없으면 「~에 우리 나라의 항구는 없다.」(0x1C7). 끝나면 0x1C6.
        if (_host.Ports is not { } ports || _host.Economy is not { } eco) return;
        int nation = _host.PortNation(_host.CurrentPort);
        var (name, _) = _host.HeroName;
        Say(0x1C5, $"아, {name}공！ 잘 오셨소이다. 우리 나라의 세력상황을 가르쳐 드리겠소.", name);
        var ours = Enumerable.Range(0, Math.Min(100, eco.States.Count)).Where(p => _host.PortNation(p) == nation).ToList();
        Say(0x373, $"우리 나라의 동맹항의 총수는 {ours.Count}이다.", ours.Count);
        string[] regions = [.. Enumerable.Range(0, 8).Select(k => MsgOr(0x181 + k, $"바다 {k}"))];
        while (true)
        {
            int pick = BuildingMenu.Ask(this, "세력상황", regions);
            if (pick < 0) break;
            var here = ours.Where(p => eco.StateBytes[p * Economy.StateSize + 0x1C] == pick).ToList();
            if (here.Count == 0) { Say(-1, regions[pick] + MsgOr(0x1C7, "에 우리 나라의 항구는 없다.")); continue; }
            var sb = new System.Text.StringBuilder("항구명       지지율 공업가치 상업가치\n");
            foreach (int p in here)
                sb.AppendLine($"{ports.Ports[p].Name,-10} {eco.States[p].Shares[nation],5} {eco.States[p].Industry,8} {eco.States[p].Economy,8}");
            MessageBox.Show(this, sb.ToString(), regions[pick]);
            // 그 갈래의 항로(0x004271A0) — 이어졌으면 「유럽(으로부터의) 수익은 n(이지).」(0x1C9), 끊겼으면 「~의 항로는 두절되어 있다.」(0x1C8).
            string area = MsgOr(0x1D1 + pick, regions[pick]);
            if (Nations.RouteIncome(nation, pick, _host.NationPorts()) is { } income)
                Say(-1, area + MsgOr(0x1C9, $"(으로부터의) 수익은 \n{income}(이지).", income));
            else
                Say(-1, area + MsgOr(0x1C8, "의 항로는 두절되어 있다."));
        }
        Say(0x1C6, "조금은 도움이 되었는지？");
    }

    /// <summary>ITEM 「구입」 — 이 항구 가게의 셋(밤 6 ~ 8 시각이면 넷, <see cref="ItemShop"/>).</summary>
    private void BuyItem()
    {
        if (_host.Scenario is not { } vm || _host.Economy is not { } eco || _host.CurrentPort < 0) return;
        var p = _host.Player;
        var stock = ItemShop.ForSale(eco.StateBytes, _host.CurrentPort, _host.Fleet?.TimeOfDay ?? 0);
        if (stock.Count == 0) { Note(" · 파는 것이 없습니다"); return; }
        var items = stock.Select(i => ItemShop.Get(vm.Memory, i)).ToList();
        // 고리(0x0041FE80) — 돌 때마다 돈이 없으면 0xEC, 스무 개를 가졌으면 0xED 로 끝. 목록 머리는 처음 0xEE, 다음부터 0xF2.
        bool first = true;
        while (true)
        {
            if (p.Gold == 0) { Say(0xEC, "금화를 안 가지고 계신 모양이군요."); return; }
            if (ItemShop.Owned(vm.Memory).Count() >= 20) { Say(0xED, "가질 수 있는 아이템은 ２０까지입니다.\n뭔가 판 다음에 사 주세요."); return; }
            string head = first ? MsgOr(0xEE, "마음에 드는 물건이 있을 겁니다.") : MsgOr(0xF2, "그 밖에 사실 물건이 있습니까？");
            int pick = BuildingMenu.Ask(this, $"{head} — 돈 {p.Gold}", items.Select(it => $"{it.Name,-10} {it.Price,7}").ToList());
            if (pick < 0) return;
            first = false;
            // 고른 것의 풀이와 그림을 보이고 살지 묻는다(0x0041F450).
            ShowItem(items[pick].Index);
            // 「%s(는) 금화 」(0xF0) + 값 + 「닢예요.」(0xF1, 0x0041FF50)
            if (MessageBox.Show(this, MsgOr(0xF0, $"{items[pick].Name}(는) 금화 ", items[pick].Name) + items[pick].Price + MsgOr(0xF1, "닢예요."),
                                "구입", MessageBoxButton.YesNo)
                != MessageBoxResult.Yes) continue;
            int gold = p.Gold;
            var result = ItemShop.Buy(vm.Memory, items[pick].Index, ref gold);
            p.SetGold(gold);
            switch (result)
            {
                case ItemShop.Result.Bought: Note($" · {Josa(items[pick].Name, "을", "를")} {items[pick].Price}에 샀습니다"); break;
                case ItemShop.Result.Owned: Say(0x14C, "같은 아이템을 이미 가지고 계십니다."); break;
                case ItemShop.Result.Poor: Say(0xEF, "손님, 돈이 모자랍니다."); break;
                default: Say(0xED, "가질 수 있는 아이템은 ２０까지입니다.\n뭔가 판 다음에 사 주세요."); return;
            }
        }
    }

    /// <summary>ITEM 「매각」 — 값의 반에서 부르고, 마다하면 운에 따라 매력만큼 올려 부른다.</summary>
    private void SellItem()
    {
        if (_host.Scenario is not { } vm) return;
        // 고리(0x00420060) — 목록 머리는 처음 「무엇을 파시겠습니까？」(0xF4), 다음부터 0xF8. 가진 것이 없어지면 「아이템을 안 가지고 계신 모양이군요.」(0xF3).
        bool first = true;
        while (true)
        {
            var owned = ItemShop.Owned(vm.Memory).Select(i => ItemShop.Get(vm.Memory, i)).ToList();
            if (owned.Count == 0) { Say(0xF3, "아이템을 안 가지고 계신 모양이군요."); return; }
            int pick = BuildingMenu.Ask(this, first ? MsgOr(0xF4, "무엇을 파시겠습니까？") : MsgOr(0xF8, "그 밖에 파실 물건이 있습니까？"),
                                        owned.Select(it => it.Name).ToList());
            if (pick < 0) return;
            first = false;
            SellOneItem(vm, owned[pick]);
        }
    }

    private void SellOneItem(ScenarioVm vm, ItemShop.Item item)
    {
        if ((item.Kind & 0x10) != 0) { Say(0x14D, "장비하고 있는 아이템은 팔 수 없습니다."); return; }
        if (item.Power == 0) { Say(0x3A1, "손님, 그 물건은 매입할 수 없네요."); return; }
        int hero = _host.HeroPersonNumber, offer = ItemShop.Offer(item);
        // 「%s(는) 금화 」(0xF5) + 값 + 「닢에 인수하지요.」(0xF7, 0x00420124). 올려 부르면 「손님에겐 두 손 들었읍니다. 금화 」(0xF6) + 값 + 0xF7.
        string head = MsgOr(0xF5, $"{item.Name}(는) 금화 ", item.Name);
        bool raised = false;
        while (true)
        {
            if (MessageBox.Show(this, head + offer + MsgOr(0xF7, "닢에 인수하지요."), "매각", MessageBoxButton.YesNo)
                == MessageBoxResult.Yes)
            {
                int gold = _host.Player.Gold;
                ItemShop.Sell(vm.Memory, item.Index, offer, ref gold);
                _host.Player.SetGold(gold);
                Note($" · {Josa(item.Name, "을", "를")} {offer}에 팔았습니다");
                return;
            }
            if (raised) return;   // 올려 부른 값도 마다하면 끝(0x004201BE)
            raised = true;
            offer = ItemShop.Haggle(offer, Navigators.Get(vm.Memory, hero, Church.Luck),
                                    Navigators.Get(vm.Memory, hero, Navigators.Stats + 6), _host.Roll);
            if (offer < 0) { Note(" · 흥정이 깨졌습니다"); return; }
            head = MsgOr(0xF6, "손님에겐 두 손 들었읍니다.\n금화 ");
        }
    }

    private bool IsMosque => _host.PortChipSet(_host.CurrentPort) == 2;

    /// <summary>모스크면 교회 말에 더하는 수(<c>0x0049A18C</c> x 0x2C8).</summary>
    private int ChurchLines => IsMosque ? 0x2C8 : 0;

    /// <summary>
    /// 교회 「예배 · 기부」 — 주인공의 운(사람 +0x23)을 올린다(<see cref="Church"/>). 예배 0x5C(<c>0x004217D0</c>),
    /// 기부(<c>0x00421830</c>)는 0x5D 로 묻고, 소지금의 1/10 에 못 미치면 0x5E, 아니면 0x5F. 모스크는 + 0x2C8.
    /// </summary>
    private void AtChurch(int pick)
    {
        if (_host.Scenario is not { } vm) return;
        int hero = _host.HeroPersonNumber;
        var p = _host.Player;
        bool mosque = IsMosque;
        if (pick == 0)
        {
            Say(0x5C + ChurchLines, mosque ? "알라신께 기도합시다." : "주님께 기도드립시다.");
            int gain = _worshipped ? 0 : Church.Worship(vm.Memory, hero, _host.Roll);
            _worshipped = true;
            Note(gain > 0 ? " · 예배를 드렸습니다 · 마음이 맑아졌습니다" : " · 예배를 드렸습니다");
            return;
        }
        int n = AmountDialog.Ask(this, MsgOr(0x5D + ChurchLines, mosque ? "기부해 주시는 겁니까？" : "기부하시겠습니까？") + $" (돈 {p.Gold})", p.Gold);
        if (n <= 0) return;
        int luck = Church.Donate(vm.Memory, hero, p.Gold, n, _host.Roll);
        if (luck < 0) Say(0x5E + ChurchLines, mosque ? "알라신께서는 경건한 회교도를 사랑하신답니다." : "주님께선 금화보다도 경건한 마음을 더 소중히 여기신답니다.");
        else Say(0x5F + ChurchLines, mosque ? "오！ 이렇게 많이 기부해 주시다니……" : "가난한 자는 행복합니다. 천국은 당신을 위해 문을 열어 주겠지요.");
        p.SetGold(p.Gold - n);
        Note(luck < 0 ? $" · {n}을 기부했습니다" : $" · {n}을 기부했습니다 · 신의 가호가 있기를{(luck > 0 ? $"(운 +{luck})" : "")}");
    }

    /// <summary>은행 — 예금 · 출금 · 대출 · 상환(<see cref="Bank"/>). 대출 한도는 작위로 오른다.</summary>
    private void UseBank(int pick)
    {
        var p = _host.Player;
        string[] names = ["예금", "출금", "대출", "상환"];
        if (pick is < 0 or > 3) return;
        int most = pick switch
        {
            0 => Bank.MaxDeposit(p.Gold, p.Balance),
            1 => Bank.MaxWithdraw(p.Gold, p.Balance),
            2 => Bank.MaxLoan(p.Balance, _host.HeroRank),
            _ => Bank.MaxRepay(p.Gold, p.Balance),
        };
        // 출금(0x0041E540) — 잔고가 없으면 0x68, 돈이 599,999,900 을 넘으면 「더이상 인출할 수 없습니다.」(0x6B).
        if (pick == 1 && p.Balance > 0 && p.Gold > PlayerState.MaxGold - 100) { Say(0x6B, "더이상 인출할 수 없습니다."); return; }
        if (most <= 0)
        {
            var (id, text) = pick switch
            {
                0 when p.Balance < 0 => (0x66, "예금은 빌린 돈을 상환한 뒤에 해주십시오."),
                0 when p.Gold <= Bank.DepositFloorGold => (0x64, "저금할 정도의 금화는 없는데요."),
                0 => (0x65, "더이상 예금은 할 수 없습니다."),
                1 => (0x68, "현재 예금된 돈은 없습니다."),
                2 when p.Balance > 0 => (0x6F, "아직 예금잔고가 있는데요."),
                2 => (0x71, "당신의 신용으로는 더이상 대출할 수 없습니다."),
                _ => (0x76, "차입금은 없는데요."),
            };
            Say(id, text);
            return;
        }
        // 대출(0x0041E6BB) — 돈이 백만 닢 이상이면 「대출받을 필요는 없습니다만.」(0x70)을 하고 그래도 이어 간다.
        if (pick == 2 && p.Gold >= 1_000_000) Say(0x70, "대출받을 필요는 없습니다만.");
        // 잔고 · 한도를 먼저 말하고(0x67 · 0x72 · 0x95) 얼마인지 묻는다(0x69 · 0x6C · 0x73 · 0x77) — 0x0041E4AA ~ 0x0041E7D5.
        if (pick == 1) Say(0x67, $"예금잔고는 금화 {p.Balance}닢입니다.", p.Balance);
        else if (pick == 2) Say(0x72, $"대출한도액은 금화 {most}닢입니다.", most);
        else if (pick == 3) Say(0x95, $"차입금은 금화 {-p.Balance}닢입니다.", -p.Balance);
        int ask = pick switch { 0 => 0x69, 1 => 0x6C, 2 => 0x73, _ => 0x77 };
        string fallback = pick switch { 0 => "얼마나 예금하시겠습니까？", 1 => "얼마나 인출하시겠습니까？", 2 => "얼마나 빌리시겠습니까？", _ => "얼마나 상환하시겠습니까？" };
        int n = AmountDialog.Ask(this, MsgOr(ask, fallback, most).Replace("\\n", " ").Replace("\n", " "), most);
        if (n <= 0) return;
        if (pick == 1 && (long)p.Gold + n > PlayerState.MaxGold) { Say(0x6D, "인출할 수 없습니다."); return; }   // 0x0041E5C3
        p.Transfer(pick is 0 or 3 ? n : -n);
        switch (pick)
        {
            case 0: Say(0x6A, $"그럼 금화 {n}닢을 입금시키겠습니다. 이율은 월 3%입니다.", n); break;
            case 1:
                Say(0x6E, $"그럼 금화 {n}닢을 인출해 드리겠습니다.", n);
                Say(0x67, $"예금잔고는 금화 {p.Balance}닢입니다.", p.Balance);   // 0x0041E5EF
                break;
            case 2: Say(0x74, $"그럼 금화 {n}닢을 빌려 드리겠습니다.", n); Say(0x75, "이율은 월 10%니까 계획적으로 이용해 주세요."); break;
            default: Say(0x78, "확실히 돌려받았습니다."); break;
        }
    }

    /// <summary>교역소 「투자」 · 조선소 「공업투자」 — 이달 투자에 더하고 주인공 나라의 몫을 올린다(<see cref="Economy.Invest"/>).</summary>
    /// <remarks>원본은 나라 서울에서는 투자를 받지 않는다(0x00431A70, 나라 표 +0x0A). 원본 셈은 앞 여섯 나라만 본다.</remarks>
    private void Invest(bool industry)
    {
        if (_host.Economy is not { } eco) return;
        int port = _host.CurrentPort;
        if (port < 0 || port >= eco.States.Count) return;
        var p = _host.Player;
        // 투자 말(교역소 0x004212B0 · 조선소 공업투자 0x0042A850 이 같다) — 서울이면 0x2, 이달 몫이 다 찼으면 0x7, 아니면 「어라, 투자해 주시는 겁니까？」(0x8) 뒤 금액.
        //   0 이면 「이 다음부턴 잘 부탁해요.」(0xD), 500 아래 0xE · 10000 아래 0xF · 그 위 0x10.
        if (_host.StartTables is { } st && st.Capitals.Take(6).Contains(port))
        {
            Say(0x2, "이 항구에는 투자할 수 없습니다.");
            return;
        }
        int most = eco.MaxInvest(port, industry, p.Gold);
        if (most <= 0)
        {
            Say(0x7, "더이상 투자하셔도……");
            return;
        }
        Say(0x8, "어라, 투자해 주시는 겁니까？");
        string what = industry ? "공업투자" : "투자";
        int n = AmountDialog.Ask(this, $"{what} — 돈 {p.Gold} · 경제 {eco.States[port].Economy} · 공업 {eco.States[port].Industry}", most);
        if (n <= 0) { Say(0xD, "이 다음부턴 잘 부탁해요."); return; }
        Say(n < 500 ? 0xE : n < 10000 ? 0xF : 0x10, n < 500 ? "아니 겨우 이것뿐입니까？" : n < 10000 ? "정말 고맙습니다." : "이 은혜는 평생 잊지 않겠습니다.");
        int nation = _host.HeroNation;
        int gain = eco.Invest(port, industry, n, nation);
        p.SetGold(p.Gold - n);
        string share = nation < Economy.NoNation ? $" · 우리 몫 {eco.States[port].Shares[nation]}%(+{gain})" : "";
        // 몫이 75 를 넘은 나라로 항구 편이 바뀐다(0x0042A720).
        int was = _host.PortNation(port), now = eco.Allegiance(port);
        if (now != was && _host.Scenario is { } vm)
        {
            _host.SetPortNation(port, now);
            int fame = Palace.Realign(vm.Memory, _host.HeroPersonNumber, nation, was, now, eco.States[port].Economy + eco.States[port].Industry);
            string side = now < NationNames.Length ? NationNames[now] : "어느 나라에도 들지 않은";
            share += $" · {Josa(_host.SceneName, "이", "가")} {side} 편이 되었습니다" + (fame > 0 ? $" · 교역 명성 +{fame}" : "");
        }
        Note($" · {what} {n}{share}");
    }

    /// <summary>
    /// 주점 · 여관 「항해자와 대화」 — 거기 있는 항해자를 골라, 주점은 목록 12(한턱내기 · 항해자정보 · 고용 · 결투),
    /// 여관은 항해자정보 · 고용으로(<see cref="Navigators"/>, 여관 고용은 호감을 보지 않는다 — 0x00426F50). 결투는 아직이다.
    /// </summary>
    private void TalkToNavigator(bool pub)
    {
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        int hero = _host.HeroPersonNumber, port = _host.CurrentPort;
        var here = Navigators.InTown(m, port, hero, pub);
        if (here.Count == 0) { Note($" · 지금 {(pub ? "주점" : "여관")}에 항해자가 없습니다"); return; }
        int who = here.Count == 1 ? 0 : BuildingMenu.Ask(this, "누구와 이야기할까요", here.Select(_host.PersonName).ToList());
        if (who < 0) return;
        int nav = here[who];
        string name = _host.PersonName(nav);
        if (!NavigatorIntro(nav, pub)) return;
        var items = _host.Menus?.Lists is { Count: > 12 } lists ? lists[12].Take(3).ToList() : ["한턱내기", "항해자정보", "고용"];   // 넷째 「결투」는 원본도 안 보인다(0x00425919 push 3)
        if (!pub) items = items.Skip(1).Take(2).ToList();
        while (true)
        {
            // 원본 「항해자정보」 는 소문이다 — 그 사람의 레벨 · 호감은 제목에 둔다(원본에 없는 보기).
            int pick = BuildingMenu.Ask(this, $"{name} — 항해 Lv {Navigators.Get(m, nav, Navigators.NavLevel)} · 전투 Lv {Navigators.Get(m, nav, Navigators.BattleLevel)} · 호감 {Navigators.Get(m, nav, Navigators.Liking)}", items);
            if (pick < 0) return;
            if (!pub) pick++;                                   // 여관은 한턱내기가 없다
            if (pick == 0)
            {
                int cls = _host.Economy is { } eco && port * Economy.StateSize + 0x24 < eco.StateBytes.Length
                    ? eco.StateBytes[port * Economy.StateSize + 0x24] : 0;
                int price = cls < Navigators.TreatPrice.Length ? Navigators.TreatPrice[cls] : 1;
                if (_host.Player.Gold < price) { Say(0x4C, "금화가 부족한 것 같군."); return; }   // 0x00425295
                _host.Player.SetGold(_host.Player.Gold - price);
                // 고맙다는 말(0x00425320) — 기질(+0x2F & 3)이 주인공과 같으면 0x2C, 다르면 0x2D. 해적(함대 60 ~ 69)이면 0x35D · 0x35E.
                bool same = ((Navigators.Get(m, nav, Navigators.Mood) ^ Navigators.Get(m, hero, Navigators.Mood)) & 3) == 0;
                bool pirate = Navigators.Get(m, nav, Navigators.Fleet) is >= 60 and < 70;
                Say((pirate ? 0x35D : 0x2C) + (same ? 0 : 1),
                    pirate ? (same ? "술로 나를 꾀는 건 무리겠지." : "그건 아니겠지.") : (same ? "한턱 내주는 거요？" : "고마워！"));
                int gain = Navigators.Treat(m, nav, hero);
                Note($" · {name}에게 한턱냈습니다({price}) · 호감 +{gain}");
                // 호감이 30 을 넘고 능력 다섯(+0x1C ~ +0x20) 가운데 가장 큰 것이 75 를 넘으면 자랑한다(0x004253CE) —
                // 「나는 이래봬도 」(0x2E) + 「%s에는 자신 있지.」(0x8B). 원본 이름표 0x00457418 은 일본어 그대로라 우리 이름을 쓴다.
                int best = 0;
                for (int k = 1; k < 5; k++)
                    if (Navigators.Get(m, nav, Navigators.Stats + k) > Navigators.Get(m, nav, Navigators.Stats + best)) best = k;
                if (Navigators.Get(m, nav, Navigators.Liking) > 30 && Navigators.Get(m, nav, Navigators.Stats + best) > 75)
                {
                    string[] stats = ["통솔력", "항해술", "지식", "직감", "용기"];
                    Say(-1, MsgOr(0x2E, "나는 이래봬도 ") + MsgOr(0x8B, $"{stats[best]}에는 자신 있지.", stats[best]));
                }
                // 조합 「희귀한 물건」 일(SNR0 변수 6 = 11)에서 지도를 가진 이가 이 사람(변수 17)이면 지도를 판다(0x004252DD).
                if (_host.Common is { Chapter: not 0 } job && job.GetVar(6) == 11 && (job.GetVar(17) & 0xFF) == nav && job.GetVar(18) != 0)
                    SellMap(job.GetVar(18) & 0xFF);
            }
            else if (pick == 1) NavigatorRumour(nav, port);
            else if (pick == 2)
            {
                // 고용(0x004255F3) — 주인공이 「다음 번의 항해에 가담하지 않겠소？」(0x30), 대답은 모두 그 사람 말이다.
                Say(0x30, "다음 번의 항해에 가담하지 않겠소？");
                var answer = Navigators.Ask(m, nav, hero, _host.HeroRank, _host.Roll, out int wage, needLiking: pub);
                switch (answer)
                {
                    case Navigators.Answer.AlreadyBound: Say(0x31, "현재의 직업을 바꿀 마음은 없소."); return;
                    case Navigators.Answer.NoRoom: Say(0x8C, "더이상 동료를 늘일 수 없습니다만."); return;
                    case Navigators.Answer.NotFriendly: Say(0x8D, "당신 배를？ 미안하지만 거절하겠소."); continue;
                    case Navigators.Answer.Refused: Say(0x91, "미안하지만 거절하겠소.\n경험을 더 쌓도록."); return;
                }
                if (MessageBox.Show(this, MsgOr(0x8E, $"한 달에 금화 {wage}닢 준다면 일하겠소.", wage), name, MessageBoxButton.YesNo)
                    != MessageBoxResult.Yes) { Say(0x8F, "유감이군."); return; }
                Say(0x90, "제독, 잘 부탁하오.");
                if (Navigators.Join(m, nav, hero, wage)) Note($" · {Josa(name, "이", "가")} 동료가 되었습니다(급여 {wage})");
                return;
            }
            else { Note(" · 결투는 아직 없습니다"); return; }
        }
    }

    /// <summary>작위 이름(<c>0x00457328</c>, 9 바이트씩) — 0 없음 · 1 사작 … 9 공작.</summary>
    private static readonly string[] Titles = ["", "사작", "준훈작사", "훈작사", "준남작", "남작", "자작", "백작", "후작", "공작"];

    /// <summary>
    /// 왕궁 문(<c>0x00427DC0</c>) — 작위가 없고 나라가 있는 주인공은 남의 나라 왕궁, 또는 제 나라라도 SNR0 깃발(<c>0x0049F3E2</c> &amp; 6,
    /// 깃발 17 · 18)이 없으면 「평민은 왕궁에 못 들어간다. 어서 물러가라！」(0x54). 들면 작위가 있으면 「잘 오셨습니다. %s %s님」(0x1BC, 성 · 작위),
    /// 없으면 0x240. 들어가면 참.
    /// </summary>
    private bool PalaceGate()
    {
        if (PalaceArrest()) return false;
        int rank = _host.HeroRank, nation = _host.HeroNation;
        bool favoured = _host.Common is { } c && (c.GetFlag(17) || c.GetFlag(18));
        if (rank == 0 && nation < 6 && (_host.PortNation(_host.CurrentPort) != nation || !favoured))
        {
            Say(0x54, "평민은 왕궁에 못 들어간다. 어서 물러가라！");
            return false;
        }
        var (_, family) = _host.HeroName;
        if (rank > 0) Say(0x1BC, $"잘 오셨습니다. {family} {Titles[Math.Min(rank, 9)]}님,\n어서 안으로 들어 오세요.", family, Titles[Math.Min(rank, 9)]);
        else Say(0x240, "평민은 왕궁에 들어갈 수 없는 것이 원칙이지만, 폐하께서 당신에게 흥미를 가지고 계시니, 특별히 들여보내 주겠소.");
        return true;
    }

    /// <summary>
    /// 왕궁 체포(<c>0x00427D62</c>) — 그 나라 호의(주인공 표 +6 + 나라)가 80 이하이고 rand(100) 이 그보다 크면 「네 녀석, 악명 높은 %s(이렸다)！」(0x1BB, 성).
    /// 거리 체포와 같은 셈(능력치 +0x21 x 전투 레벨 / 100 + 능력치 +0x21 &lt; rand(150))으로 잡히면(<c>0x00427F90</c>) 0x1BD 뒤 왕의 말 0x1C1 ~ 0x1C4 —
    /// 돈과 은행 잔고를 5 분의 1 로, 호의를 100 으로. 빠져나오면 0x1BE · 0x1C0. 잡으러 왔으면 참.
    /// </summary>
    private bool PalaceArrest()
    {
        if (_host.Scenario is not { } vm || _host.CurrentPort < 0) return false;
        var m = vm.Memory;
        int nation = _host.PortNation(_host.CurrentPort), hero = _host.HeroPersonNumber;
        if (nation >= 6) return false;
        var favourAt = ScenarioMemory.At(ScenarioMemory.HeroTable, hero, 6 + nation);
        int favour = m.Read(favourAt, false);
        if (favour > 0x50 || _host.Roll(100) <= favour) return false;
        var (_, family) = _host.HeroName;
        Say(0x1BB, $"네 녀석, 악명 높은 {family}(이렸다)！ 우리 나라에 침입하다니 간도 큰 놈이군！ 원하는 대로 목을 메달아 주겠다.", family);
        int wit = Navigators.Get(m, hero, 0x21), level = Navigators.Get(m, hero, Navigators.BattleLevel);
        if (wit * level / 100 + wit >= _host.Roll(150))
        {
            Say(0x1BE, "이런 곳에서 붙잡혀서는 말이 안되지.");
            Say(0x1C0, "휴우！ 간신히 도망쳐 나왔군요.");
            return true;
        }
        Say(0x1BD, "아뿔사, 내 목숨도 이제 끝인가！");
        Say(0x1C1, $"호오！ 그대가 {family}인가.", family);
        Say(0x1C2, "원칙으로 하자면 교수형에 처해야겠지만, 특별히 관용을 베풀어 주지.");
        Say(0x1C3, "재산의 ５분의 ４를 몰수하는 것으로, 지금까지의 죄를 특별히 용서해 주마.");
        Say(0x1C4, "그만큼 혼쭐이 났으니, 더이상 우리 나라를 건드리지 마라！");
        var p = _host.Player;
        int kept = p.Gold - p.Gold * 4 / 5;
        p.Transfer(-(p.Balance * 4 / 5));   // 잔고(빚도)를 5 분의 1 로 — 돈은 아래에서 다시 매긴다
        p.SetGold(kept);
        m.Write(favourAt, 100, false);
        return true;
    }

    /// <summary>처지(+0x2E)마다 직함(<c>0x004572D8</c>, 11 바이트씩).</summary>
    private static readonly string[] Ranks = ["", "제독", "선장", "부선장", "경리주임", "수석항해사", "항해사", ""];

    /// <summary>
    /// 항해자와 말을 트는 첫마디 — 주점(<c>0x00425960</c>)은 우리 동료면 0x86 ~ 0x88 가운데 하나로 끝(목록 없음), 해적 함대(60 ~ 69)면 0x35B + rand(2),
    /// 아니면 「여어！ 내게 무슨 용건이 있는 거요？」(0x29) 뒤 제 소개. 여관(<c>0x0041F19D</c>)은 0x48 뒤 제 소개.
    /// 소개는 함대에 들었으면 나라 · 함대 갈래(목록 11, 번호 % 10 이 0 이면 함대 · 1 ~ 4 상선대 · 5 ~ 9 전함대) · 직함으로
    /// 제독이면 0x89(여관 0x49), 아니면 0x8A(0x4A), 함대가 없으면 「항구를 떠도는 철새」(0x2B · 0x4B). 목록을 띄울지 돌려준다.
    /// </summary>
    private bool NavigatorIntro(int nav, bool pub)
    {
        if (_host.Scenario is not { } vm) return true;
        var m = vm.Memory;
        int fleet = Navigators.Get(m, nav, Navigators.Fleet);
        if (pub && fleet != 0xFF && fleet == Navigators.Get(m, _host.HeroPersonNumber, Navigators.Fleet))
        {
            int k = _host.Roll(3);
            Say(0x86 + k, k == 0 ? "제독님, 역시 육지는 좋군요." : k == 1 ? "제독님, 술은 적당히 합시다." : "제독님, 다음 기항지는 어딥니까？");
            return false;
        }
        if (pub && fleet is >= 60 and < 70)
        {
            int k = _host.Roll(2);
            Say(0x35B + k, k == 0 ? "목숨이 아깝거든 썩 꺼져！" : "내게 걸려 살아 남은 놈은 없다.");
            return true;
        }
        Say(pub ? 0x29 : 0x48, "여어！ 내게 무슨 용건이 있는 거요？");
        string name = _host.PersonName(nav);
        if (fleet == 0xFF) { Say(pub ? 0x2B : 0x4B, "나는 항구를 떠도는 철새요."); return true; }
        string[] kinds = _host.Menus?.Lists is { Count: > 0xB } l11 ? [.. l11[0xB]] : ["함대", "상선대", "전함대"];
        string nation = NationNames7[Math.Min(6, fleet / 10)], kind = kinds[fleet % 10 == 0 ? 0 : fleet % 10 / 5 + 1];
        int status = Navigators.Get(m, nav, Navigators.Status);
        string rank = Ranks[Math.Clamp(status, 0, Ranks.Length - 1)];
        string intro = pub
            ? MsgOr(0x2A, "저는 ") + (status == 1 ? MsgOr(0x89, $"{nation} {kind}의 {name}(다).", nation, kind, name)
                                                 : MsgOr(0x8A, $"{nation} {kind}의 {rank}, {name}(다).", nation, kind, rank, name))
            : status == 1 ? MsgOr(0x49, $"나는 {nation} {kind}의 {name}제독이다.", nation, kind, name)
                          : MsgOr(0x4A, $"나는 {nation} {kind}의 {rank}, {name}(이다).", nation, kind, rank, name);
        Say(-1, intro);
        return true;
    }

    /// <summary>
    /// 주점 첫마디(<c>0x00426CF1</c>) — 이 항구에 마음을 준 여급(+0x12 의 8 과 0x40, <c>0x00426E10</c>)이 있으면 그 여급이 「어머！ %s씨. %s(는) 좀 어떠세요？」(0x11),
    /// 없으면 주인이 「여어, 뱃사람！ 우리집 %s(는) 최고요.」(0x12) — 술은 항구 형편 +0x24.
    /// </summary>
    private void PubGreeting()
    {
        if (_host.Scenario is not { } vm || _host.Economy is not { } eco || _host.CurrentPort is < 0 or >= 100) return;
        int kind = eco.StateBytes[_host.CurrentPort * Economy.StateSize + 0x24];
        string drink = PubDrinks[Math.Clamp(kind, 0, PubDrinks.Length - 1)];
        bool heroFlag = (Navigators.Get(vm.Memory, _host.HeroPersonNumber, 0x31) & 0x10) != 0;
        int fond = heroFlag ? -1 : Enumerable.Range(0, Barmaids.Count).FirstOrDefault(i =>
            Barmaids.Get(vm.Memory, i, Barmaids.Port) == _host.CurrentPort && (Barmaids.Get(vm.Memory, i, Barmaids.Flags) & 0x48) == 0x48, -1);
        if (fond >= 0)
        {
            var (name, _) = _host.HeroName;
            Say(0x11, $"어머！ {name}씨.\n{drink}(는) 좀 어떠세요？", name, drink);
        }
        else Say(0x12, $"여어, 뱃사람！\n우리집 {drink}(는) 최고요.", drink);
        // 이어서 이 항구 여급이 인사한다(0x00426EA0, 주인공 +0x31 의 0x10 일 때) — 맡긴 조사가 끝났으면
        // 「요전번 부탁하신 건은 조사해 두었답니다.」(0x133), 아니면 「어머나, 어서 오셔요.」(0x134).
        if (heroFlag && Barmaids.InPort(vm.Memory, _host.CurrentPort) is >= 0 and var maid)
        {
            bool ready = Barmaids.Get(vm.Memory, maid, Barmaids.Probe) != Barmaids.NoProbe && Barmaids.Get(vm.Memory, maid, Barmaids.ProbeDays) == 0;
            Say(ready ? 0x133 : 0x134, ready ? "요전번 부탁하신 건은 조사해 두었답니다." : "어머나, 어서 오셔요.");
        }
    }

    /// <summary>항구 형편 +0x24 갈래마다 주점 술(<c>0x00457148</c>, 11 바이트씩).</summary>
    private static readonly string[] PubDrinks = ["럼주", "와인", "위스키", "브랜디", "진", "맥주", "보드카", "테킬라"];

    /// <summary>
    /// 「항해자정보」(<c>0x00425410</c>) — 조합 「희귀한 물건」 일에서 지도를 가진 이면 지도를 판다(<c>0x00424FD0</c>). 아니면 항구
    /// p = min(99, 여기 + rand(8)) − min(rand(4), p) 에서 일자리 없는 항해자(사람 +0x2D = p, +0x31 &amp; 0x20)를 앞에서부터 찾아
    /// 「%s항에서 %s(라는) 항해사가 일자리를 얻지 못하고 있던데.」(0x23A), 없으면 0x23B. p 가 여기면 「이 항구의 %s(는) 마음에 들었소？」(0x2F, 그 항구 술).
    /// </summary>
    private void NavigatorRumour(int nav, int here)
    {
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        if (_host.Common is { Chapter: not 0 } job && job.GetVar(6) == 11 && (job.GetVar(17) & 0xFF) == nav)
        {
            SellMap(job.GetVar(18) & 0xFF);
            return;
        }
        if (_host.Common is { Chapter: not 0 } debt && debt.GetVar(6) == 5 && (debt.GetVar(17) & 0xFF) == nav && debt.GetVar(18) != 0)
        {
            CollectDebt(debt, nav);
            return;
        }
        int p = Math.Min(0x63, _host.Roll(8) + here);
        p -= Math.Min(_host.Roll(4), p);
        string PortName(int k) => _host.Ports is { } pt && k < pt.Ports.Count ? pt.Ports[k].Name : $"#{k}";
        if (p == here)
        {
            int cls = _host.Economy is { } eco && here < eco.States.Count ? eco.StateBytes[here * Economy.StateSize + 0x24] : 0;
            string drink = PubDrinks[Math.Clamp(cls, 0, PubDrinks.Length - 1)];
            Say(0x2F, $"이 항구의 {drink}(는) 마음에 들었소？", drink);
            return;
        }
        int found = Enumerable.Range(0, Navigators.People)
            .FirstOrDefault(i => Navigators.Get(m, i, Navigators.Port) == p && (Navigators.Get(m, i, Navigators.Kind) & 0x20) != 0, -1);
        if (found >= 0)
        {
            string who = _host.PersonName(found);
            Say(0x23A, $"{PortName(p)}항에서 {who}(라는) 항해사가 일자리를 얻지 못하고 있던데.", PortName(p), who);
        }
        else Say(0x23B, $"{PortName(p)}항엔 괜찮은 녀석이 없었다고 하던데.", PortName(p));
    }

    /// <summary>
    /// 조합 「빚 수금」 일(SNR0 변수 6 = 5)에서 빚진 항해자(변수 17)를 만났다(<c>0x00425120</c>) — 「%s %s씨지？ 셜록은행의 부탁을 받아 수금하러 왔소.」(0x260).
    /// 주인공 운(+0x23)이 rand(100) 보다 작고 셋에 하나면 「어림없지」(0x262) · 「놓치고 말았군」(0x265) — 그 이는 다른 항구로 간다(지금 항구가 42 아래면 rand(42),
    /// 아니면 rand(58) + 42). 아니면 「알았소, 지불하면 되잖소」(0x261) · 0x266 — 변수 18 = 0, 돈 += 변수 19 x 50000.
    /// </summary>
    private void CollectDebt(ScenarioVm job, int nav)
    {
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        string who = _host.PersonName(nav);
        Say(0x260, $"{who}씨지？\n셜록은행의 부탁을 받아 수금하러 왔소.", who, "");
        int luck = Navigators.Get(m, _host.HeroPersonNumber, 0x23);
        if (luck < _host.Roll(100) && _host.Roll(3) == 0)
        {
            Say(0x262, "칵！ 그럼 당신, 빚장이오？\n어림없지.");
            Say(0x265, "제기랄！\n놓치고 말았군.");
            int port = Navigators.Get(m, nav, Navigators.Port);
            Navigators.Set(m, nav, Navigators.Port, port < 0x2A ? _host.Roll(0x2A) : _host.Roll(0x3A) + 0x2A);
            return;
        }
        Say(0x261, "칵！ 그럼 당신, 빚쟁이오？\n알았소, 지불하면 되잖소.");
        Say(0x266, "그렇지, 그렇게 얌전히 지불해 주면 이쪽도 편하고 좋지.");
        job.SetVar(18, 0);
        _host.Player.SetGold(Math.Min(PlayerState.MaxGold, _host.Player.Gold + (short)job.GetVar(19) * 50000));
    }

    /// <summary>나라 이름 — 나라 표 차례(서울 리스본 · 세빌리아 · 이스탄불 · 런던 · 제노바 · 암스테르담).</summary>
    private static readonly string[] NationNames = ["포르투갈", "에스파냐", "오스만 투르크", "잉글랜드", "이탈리아", "네덜란드"];

    /// <summary>여관 「항구지지율」 — 이 항구의 나라 몫 여섯(항구 형편 +8). 원본은 서울에서 이 줄을 막는다(0x0041F3FA).</summary>
    private void ShowSupport()
    {
        int port = _host.CurrentPort;
        if (_host.Economy is not { } eco || port < 0 || port >= eco.States.Count) return;
        if (_host.StartTables is { } st && st.Capitals.Take(6).Contains(port)) { Note(" · 서울에서는 볼 수 없습니다"); return; }
        var shares = eco.States[port].Shares;
        string text = string.Join("\n", shares.Select((v, i) => $"{NationNames[i],-8} {v,3}%"));
        MessageBox.Show(this, $"{_host.SceneName}의 나라 몫\n\n{text}", "항구지지율");
    }

    /// <summary>주점 「선원 고용」 — 올 사람 · 값은 원본 셈(<see cref="Pub"/>), 앞 배부터 빈 자리를 채운다.</summary>
    private void HireCrew()
    {
        var p = _host.Player;
        // 하루에 한 번 — 고용하면 0x0049F5A4 의 1 이 서고(0x00424261) 그날은 목록에서 막힌다(0x00426D83).
        if (_host.CrewHiredToday) { Note(" · 오늘은 이미 선원을 모았습니다 — 내일 다시 오세요"); return; }
        if (p.Vacancy == 0) { Note(M(0x1D, "더 태울 자리가 없습니다")); return; }
        if (p.Gold < Pub.MinGold) { Note(M(0x1E, "돈이 모자랍니다")); return; }
        int port = _host.CurrentPort;
        if (_host.Economy is not { } eco || port < 0 || port >= eco.States.Count || _host.HeroRecord is not { } hero) return;
        int economy = eco.States[port].Economy, price = Pub.Price(economy);
        if (_pubMood < Pub.LowMood)
        {
            Say(0x1F, "선원을 고용하기 위해선 술을 더 내는 것이 좋겠군요.");
            if (MessageBox.Show(this, MsgOr(0x20, "선원을 모으겠습니까？"), "선원 고용", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        }
        // 제독이 외치고(0x21 — 이름 뒤에 작위), 모인 수를 알린다 — 20 명 이상 0x79, 그 아래 0x7A, 없으면 0x7B(0x00424435).
        var (heroName, _) = _host.HeroName;
        string title = Titles[Math.Clamp(_host.HeroRank, 0, Titles.Length - 1)];
        Say(0x21, $"이 중에서 {heroName}{title}의 배를 탈 사람은 없느냐？", heroName, title);
        int available = Pub.AvailableAt(economy, _pubMood, _host.HeroRank);
        if (available == 0) { Say(0x7B, "제독님, 지원자가 한 명도 없었습니다."); return; }
        if (available >= 20) Say(0x79, $"{available}명의 선원이 모였습니다.", available);
        else Say(0x7A, $"제독님, 지원자는 겨우 {available}명뿐이었습니다.", available);
        // 「선원 한 사람당, 금화 %d닢이 필요합니다. 몇명 데리고 갈까요？」(0x22 · 0x7C · 0x7D)
        int most = Pub.MostHirable(available, p.Gold, price, p.Vacancy);
        int n = AmountDialog.Ask(this, MsgOr(0x22, "선원 한 사람당,") + " " + MsgOr(0x7C, $"금화 {price}닢이 필요합니다.", price) + " "
                                       + MsgOr(0x7D, "몇명 데리고 갈까요？"), most);
        if (n <= 0) return;
        n = Math.Min(n, p.Gold / Math.Max(1, price));
        p.SetGold(p.Gold - n * price);
        _host.CrewHiredToday = true;
        int left = PlaceCrew(n);
        if (left > 0) p.SetGold(p.Gold + left * price);           // 태우지 않은 사람 몫은 돌려받는다(0x004246E0)
        _host.SyncLoad();
        Note($" · 선원 {n - left}명 · 금화 {(n - left) * price}");
    }

    /// <summary>
    /// 모은 선원을 배마다 나눠 태운다(<c>0x00424540</c> 고용 쪽) — 배를 고르면 그 배 선원도 무리에 넣고 「%s의 선원을 몇 명으로
    /// 하겠습니까(0-%d)？」(0x359)로 다시 정한다. 늘어난 만큼 사기를 60 쪽으로(60 + (사기 − 60) x 전 선원 / 새 선원).
    /// 무리가 남은 채 물리면 「선원이 남아 있습니다만 좋겠습니까？」(0x23). 무리를 다 태우면 끝낸다. 남은 수를 돌려준다.
    /// </summary>
    private int PlaceCrew(int pool)
    {
        var ships = _host.Player.Ships;
        while (pool > 0)
        {
            int pick = ships.Count == 1 ? 0
                : BuildingMenu.Ask(this, $"선원 배치 — 남은 선원 {pool}명", ships.Select(sh => $"{sh.Name}  선원 {sh.Crew}/{sh.MaxCrew}").ToList());
            if (pick < 0)
            {
                if (MessageBox.Show(this, MsgOr(0x23, "선원이 남아 있습니다만 좋겠습니까？"), "선원 고용", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    return pool;
                continue;
            }
            var ship = ships[pick];
            int before = ship.Crew, total = before + pool, most = Math.Min(ship.MaxCrew, total);
            int keep = AmountDialog.Ask(this, MsgOr(0x359, $"{ship.Name}의 선원을 몇 명으로 하겠습니까(0-{most})？", ship.Name, most), most);
            if (keep < 0)
            {
                if (ships.Count == 1) return pool;
                continue;
            }
            ship.Crew = keep;
            pool = total - keep;
            if (keep > before) ship.Morale = PlayerState.DefaultMorale + (ship.Morale - PlayerState.DefaultMorale) * before / keep;
            else if (keep == 0) ship.Morale = 0;
        }
        return 0;
    }

    /// <summary>교역소 「구입」 — 고른 교역품을 수만큼 산다(<see cref="Economy.ForSale"/> 값, <see cref="PlayerState.Buy"/>).</summary>
    private void ShowGoods()
    {
        if (_host.Economy is not { } eco) return;
        bool permit = _host.Scenario is { } vm && _host.Ports is { } ports && _host.CurrentPort < ports.Ports.Count
                      && ItemShop.Owned(vm.Memory).Contains(Palace.PermitOf(_host.PortNation(_host.CurrentPort)));
        var goods = eco.ForSale(_host.CurrentPort, permit);
        if (goods.Count == 0) { Note(" · 이 항구 교역소에는 파는 것이 없습니다"); return; }
        // 살 수 있으면 주인이 세금을 일러 준다(0x00420300) — 면세증이 있으면 0x2FA, 없으면 「２０％의 구입세」(0x2FB).
        if (permit) Say(0x2FA, "면세증을 가지고 계신 모양이군요. 그럼 면세해 드리겠습니다.");
        else Say(0x2FB, "면세증을 안 가지고 계신 분은, 상품을 구입하실 때 ２０％의 구입세가 붙습니다.");
        var p = _host.Player;
        int pick = BuildingMenu.Ask(this, $"구입 — 돈 {p.Gold} · 빈 자리 {p.Free}",
                                    goods.Select(g => $"{eco.GoodsNames[g.Good],-6}  {g.Price,5}").ToList());
        if (pick < 0) return;
        var (good, price) = goods[pick];
        // 말을 거드는 동료(0x004207B6) — 돈이 한 통 값의 반도 안 되면 「금화가 부족한 것 같습니다만.」(0x16).
        //   아니면 그 갈래 시세(항구 +0xE + 갈래)가 40 이하 0x17 · 60 넘음 0x18 · 그 사이 0x19.
        if (price / 2 > p.Gold) { Say(0x16, "금화가 부족한 것 같습니다만."); return; }
        if (_host.CurrentPort < eco.States.Count)
        {
            int index = eco.States[_host.CurrentPort].PriceIndex[Economy.CategoryOf(good)];
            Say(index <= 40 ? 0x17 : index > 60 ? 0x18 : 0x19,
                index <= 40 ? "이 상품은 지금이 살 찬스군요." : index > 60 ? "좀 비싼 거래군요." : "그저 그만한 가격이군요.");
        }
        // 시세를 말하고(0x96) 마다하면 값을 깎는다(0x00420B70).
        if (MessageBox.Show(this, MsgOr(0x96, $"{eco.GoodsNames[good]} 시세는 금화 {price}닢입니다. 좋습니까？", eco.GoodsNames[good], price),
                            "구입", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            price = Haggle(price);
            if (price <= 0) return;
        }
        int most = Math.Min(p.Free, price > 0 ? p.Gold / price : 0);
        int amount = AmountDialog.Ask(this, $"{eco.GoodsNames[good]} 몇 통", most);
        if (amount <= 0) return;
        int bought = p.Buy(good, amount, price);
        _host.SyncLoad();
        if (bought > 0 && _host.Fleet is { } lf)   // 항해일지 — 교역품 구입(0x00420AA3)
            _host.Log.Add(lf.Date.Year, lf.Date.Month, lf.Date.Day, Logbook.Bought, good, (bought * price) >> 16, (bought * price) & 0xFFFF, bought);
        Note($" · {eco.GoodsNames[good]} {bought}통을 {bought * price}에 샀습니다");
    }

    /// <summary>값 깎기 — 받아들인 한 통 값, 그만두면 0(<see cref="Palace.HaggleFloor"/>).</summary>
    private int Haggle(int price)
    {
        if (_host.Scenario is not { } vm || _host.Ports is not { } ports) return price;
        var m = vm.Memory;
        bool ours = _host.PortNation(_host.CurrentPort) == (_host.HeroNation & 7);
        int floor = Palace.HaggleFloor(price, _host.HeroRank, ours);
        int accept = Palace.HaggleAccept(price, floor);
        bool clerk = (HeroField(SkillField) & 1) != 0
                     || (Navigators.InPost(m, 1) is >= 0 and var c && (Navigators.Get(m, c, SkillField) & 2) != 0);
        while (true)
        {
            int offer = AmountDialog.Ask(this, MsgOr(6, "그럼, 얼마로 하면 사시겠습니까？"), price);
            if (offer <= 0) return 0;
            if (offer >= accept) { Say(0xA, "그럼 그 값에 타협합시다."); return offer; }
            if (accept - floor <= 1) { Say(0xB, "그 값엔 팔 수 없어요. 더이상 값을 깎지 말아 주세요."); continue; }
            int counter = Palace.HaggleCounter(price, floor);
            Say(0xC, $"그 값엔 팔 수 없어요. 금화 {counter}닢에 사주세요.", counter);
            if (!clerk) continue;
            Say(0x353, $"그건 아니겠지. 금화 {floor}닢이라면 꽤 괜찮은 벌이가 아니오？", floor);
            Say(0x354, $"손님에겐 못 당하겠네요. 좋아요, 금화 {floor}닢으로 타협합시다.", floor);
            return floor;
        }
    }

    /// <summary>
    /// 교역소 「시세 · 물가」(<c>0x00421500</c>) — 갈래를 고르면 그 물가(값 지수 + 50, 말 0x97)와 그 갈래 교역품을 이 항구에서 파는 값으로 늘어놓는다.
    /// </summary>
    private void ShowPriceIndex()
    {
        if (_host.Economy is not { } eco || _host.CurrentPort < 0 || _host.CurrentPort >= eco.States.Count) return;
        int port = _host.CurrentPort;
        while (true)
        {
            int cat = BuildingMenu.Ask(this, "시세 · 물가", Economy.CategoryNames);
            if (cat < 0) return;
            int index = eco.States[port].PriceIndex[cat] + 50;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(MsgOr(0x97, $"{Economy.CategoryNames[cat]}의 물가는 {index}％입니다.", Economy.CategoryNames[cat], index));
            for (int g = 0; g < eco.GoodsNames.Count; g++)
                if (Economy.CategoryOf(g) == cat) sb.AppendLine($"{eco.GoodsNames[g],-8} {eco.SellPrice(port, g),6}");
            MessageBox.Show(this, sb.ToString(), "시세 · 물가");
        }
    }

    /// <summary>조선소 「개조」 — 목록 23(선수상 · 무장 · 적재비율변경 · 이름변경, 손표 <c>0x00458CA0</c>) 가운데 된 것.</summary>
    private void Refit()
    {
        int pick = BuildingMenu.Ask(this, "개조", ["선수상", "무장", "적재비율변경", "이름변경"]);
        if (pick == 0) { BuyFigurehead(); return; }
        if (pick == 1) { BuyGuns(); return; }
        pick--;
        if (pick == 1) { Rerate(); return; }
        if (pick != 2) return;
        // 이름변경(<c>0x0042A470</c>) — 배를 고르고(0x111) 0x113 뒤 일곱 자까지 받는다. 값은 받지 않는다.
        var p = _host.Player;
        int which = BuildingMenu.Ask(this, MsgOr(0x111, "어느 부대요？"), p.Ships.Select(sh => sh.Name).ToList());
        if (which < 0) return;
        var ship = p.Ships[which];
        string? name = TextDialog.Ask(this, MsgOr(0x113, "그럼, 배에 새 이름을 붙여 주시오."), ship.Name, 7);
        if (name == null) return;
        ship.Name = name;
        if (which == 0) _host.ShipName = name;
        Note($" · 배 이름을 「{name}」{(KoreanBatchim(name) ? "으로" : "로")} 바꿨습니다");
    }

    /// <summary>조선소 「선수상」(<c>0x00429E70</c>) — 배를 고르고(0x10B) 늘어놓은 것 가운데 하나를 500 x k² 에 단다(<see cref="Shipyard.FigureheadChoices"/>).</summary>
    private void BuyFigurehead()
    {
        if (_host.Economy is not { } eco || _host.CurrentPort < 0 || _host.CurrentPort >= eco.States.Count) return;
        var st = eco.States[_host.CurrentPort];
        var (count, rare) = Shipyard.FigureheadChoices(st.Economy, st.Industry, HeroField(Church.Luck), Random.Shared.Next);
        if (rare) Say(0x10A, "오늘은 진귀한 물건이 있소.");
        var p = _host.Player;
        while (true)
        {
            int which = BuildingMenu.Ask(this, MsgOr(0x10B, "어느 배의 선수상이오？"),
                p.Ships.Select(sh => $"{sh.Name}  {(sh.Figurehead > 0 ? Shipyard.Figureheads[sh.Figurehead] : "없음")}").ToList());
            if (which < 0) return;
            var ship = p.Ships[which];
            int k = BuildingMenu.Ask(this, MsgOr(0x10C, "어느 것으로 하겠소？"),
                [.. Enumerable.Range(1, count).Select(i => $"{Shipyard.Figureheads[i],-6} {Shipyard.FigureheadPrice(i),7}")]) + 1;
            if (k <= 0) continue;
            int price = Shipyard.FigureheadPrice(k);
            if (MessageBox.Show(this, MsgOr(0x10D, $"이 놈이라면 금화 {price}닢에 팔아 주지.\n좋겠소？", price), "선수상",
                                MessageBoxButton.YesNo) != MessageBoxResult.Yes) continue;
            if (price > p.Gold) { Say(0xCF, "금화가 부족합니다."); continue; }
            p.SetGold(p.Gold - price);
            ship.Figurehead = k;
            Note($" · {ship.Name}에 {Shipyard.Figureheads[k]} 선수상을 달았습니다");
            return;
        }
    }

    /// <summary>조선소 「무장」(<c>0x0042A080</c>) — 배를 고르고(0x10E) 대포 갈래 · 문 수를 정해 한 문 값 x 문 수를 낸다(<see cref="Shipyard.GunRoom"/>).</summary>
    private void BuyGuns()
    {
        if (_host.Economy is not { } eco || _host.CurrentPort < 0 || _host.CurrentPort >= eco.States.Count) return;
        var st = eco.States[_host.CurrentPort];
        var (count, rare) = Shipyard.GunChoices(st.Economy, st.Industry, HeroField(Church.Luck), Random.Shared.Next);
        if (rare) Say(0x10A, "오늘은 진귀한 물건이 있소.");
        var p = _host.Player;
        while (true)
        {
            int which = BuildingMenu.Ask(this, MsgOr(0x10E, "어느 부대의 대포요？"),
                p.Ships.Select(sh => $"{sh.Name}  {Shipyard.GunNames[Math.Clamp(sh.GunKind, 0, 7)]} {sh.Mounted}/{sh.Guns}").ToList());
            if (which < 0) return;
            var ship = p.Ships[which];
            int k = BuildingMenu.Ask(this, MsgOr(0x10C, "어느 것으로 하겠소？"),
                [.. Enumerable.Range(1, count).Select(i => $"{Shipyard.GunNames[i],-6} {Shipyard.GunPrice[i],4}")]) + 1;
            if (k <= 0) continue;
            int room = Shipyard.GunRoom(ship, k);
            if (room < 0) { Say(0x3FD, "이 배에는 무기를 실을 수 없소."); continue; }
            if (room == 0) { Say(0x3FC, "이 배에는 이 이상 무기를 실을 수 없겠는데！"); continue; }
            string ask = ship.GunKind == k
                ? MsgOr(0x14E, $"{Shipyard.GunNames[k]}(는) 이미 {ship.Mounted}문 탑재하고 있습니다.\n앞으로 {room}문 탑재할 수 있습니다.", Shipyard.GunNames[k], ship.Mounted, room)
                : MsgOr(0x10F, "몇문 싣겠소？");
            int n = AmountDialog.Ask(this, ask, room);
            if (n <= 0) continue;
            int price = Shipyard.GunPrice[k] * n;
            if (MessageBox.Show(this, MsgOr(0x110, $"{Shipyard.GunNames[k]}(를) {n}문 싣는다면 금화 {price}닢이오.\n좋겠소？", Shipyard.GunNames[k], n, price),
                                "무장", MessageBoxButton.YesNo) != MessageBoxResult.Yes) continue;
            if (price > p.Gold) { Say(0xCF, "금화가 부족합니다."); continue; }
            p.SetGold(p.Gold - price);
            Shipyard.Mount(ship, k, n);
            Note($" · {ship.Name} — {Shipyard.GunNames[k]} {ship.Mounted}문");
            return;
        }
    }

    /// <summary>조선소 「적재비율변경」(<c>0x0042A370</c>) — 값(선종 값)을 내고 선원 자리 · 대포 · 짐칸을 다시 나눈다(<see cref="Shipyard.Rerate"/>).</summary>
    private void Rerate()
    {
        if (_host.StartTables is not { } st) return;
        var p = _host.Player;
        int which = BuildingMenu.Ask(this, MsgOr(0x111, "어느 부대요？"), p.Ships.Select(sh => $"{sh.Name}  선원 자리 {sh.MaxCrew} · 대포 {sh.Guns} · 짐칸 {sh.Room}").ToList());
        if (which < 0) return;
        var ship = p.Ships[which];
        var t = st.ShipTypes[ship.Type];
        int price = t.Price;
        if (MessageBox.Show(this, MsgOr(0x112, $"이 놈의 적재비율을 바꾸려면 금화 {price}닢이 필요하지. 좋겠소？", price), "적재비율변경",
                            MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (price > p.Gold) { Say(0xCF, "금화가 부족합니다."); return; }
        var (lo, hi, gmax) = Shipyard.RatioRange(t);
        int crew = AmountDialog.Ask(this, MsgOr(0x102, "선원은 몇명 탈 수 있게 하겠소？") + $" ({lo} 이상)", Math.Max(lo, hi));
        if (crew < 0) return;
        int guns = gmax > 0 ? AmountDialog.Ask(this, MsgOr(0x103, "무기는 얼마나 실을 수 있게 하겠소？"), gmax) : 0;
        if (guns < 0) return;
        int cargo = t.Capacity - Math.Clamp(crew, lo, Math.Max(lo, hi)) - guns;
        if (MessageBox.Show(this, MsgOr(0x104, "이걸로 좋겠소？") + $"\n선원 자리 {Math.Clamp(crew, lo, Math.Max(lo, hi))} · 대포 {guns} · 짐칸 {cargo}",
                            "적재비율변경", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (!Shipyard.Rerate(ship, t, crew, guns)) { Say(0x14, "그러면 적하가 넘쳐 버리지. 정리한 다음에 다시 와 줘."); return; }
        p.SetGold(p.Gold - price);
        _host.SyncLoad();
        Note($" · {ship.Name} — 선원 자리 {ship.MaxCrew} · 대포 {ship.Guns} · 짐칸 {ship.Room}");
    }

    /// <summary>나라 이름 일곱(<c>0x00457388</c>, 13바이트씩) — 일곱째는 해적이다.</summary>
    private static readonly string[] NationNames7 = ["포르투갈", "에스파니아", "오스만제국", "영국", "이탈리아", "네덜란드", "해적"];

    /// <summary>
    /// 조합 「나라정보」(<c>0x0041EC50</c>) — 나라를 고르고(0xA2) 금화 100 을 내면(0x56 · 아니면 0x57, 모자라면 0xA3)
    /// 나라 표(기억 표 0) +2 의 노리는 나라(0x58 — 7 이상이면 0x355 「네 함대를 노리고 있다」)와 +4 의 상선대 행선지(0x59)를 일러 준다.
    /// </summary>
    private void NationInfo()
    {
        if (_host.Scenario is not { } vm || _host.Ports is not { } ports) return;
        var p = _host.Player;
        int n = BuildingMenu.Ask(this, MsgOr(0xA2, "어느 나라의 정보요？"), NationNames7.Take(6).ToList());
        if (n < 0) return;
        if (MessageBox.Show(this, MsgOr(0x56, "정보를 얻으려면 금화 100닢이 드는데, 지불할 마음은 있는지？"), "나라정보", MessageBoxButton.YesNo)
            != MessageBoxResult.Yes) { Say(0x57, "흥！ 나중에 울지 마라！"); return; }
        if (p.Gold < 100) { Say(0xA3, "돈이 없는 녀석은 돌아가라."); return; }
        p.SetGold(p.Gold - 100);
        var m = vm.Memory;
        int target = m.Read(ScenarioMemory.At(ScenarioMemory.NationTable, n, 2), false);
        int port = m.Read(ScenarioMemory.At(ScenarioMemory.NationTable, n, 4), false);
        var lines = new List<ScenarioVm.Line>
        {
            new("", target < 7 ? MsgOr(0x58, $"{NationNames7[n]}(는) {NationNames7[target]}(를) 노리고 있는 모양이야.", NationNames7[n], NationNames7[target])
                                : MsgOr(0x355, $"조심해！ {NationNames7[n]}(는) 네 함대를 노리고 있다.", NationNames7[n])),
        };
        if (port < ports.Ports.Count) lines.Add(new("", MsgOr(0x59, $"상선대는 {ports.Ports[port].Name}(를) 향하고 있는 모양인데요.", ports.Ports[port].Name)));
        ScenarioDialog.Play(this, _host.Faces, lines);
    }

    /// <summary>
    /// 지도 팔기(<c>0x00424FD0</c>) — 보물 <paramref name="treasure"/>(0x5A + k)의 지도(− 10)를 금화 1000 에 판다(0x25C · 0x25D).
    /// 이미 가졌으면 0x36F, 돈이 모자라면 0x25F, 칸이 차면 0x370. 사면 0x25E 와 함께 그 자리에 0x20 을 세운다(<c>0x004250C9</c>).
    /// </summary>
    private void SellMap(int treasure)
    {
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        int map = treasure - 10;
        var owned = ItemShop.Owned(m);
        if (owned.Contains(map) || owned.Contains(treasure)) { Say(0x36F, "뭐냐！ 용건은 이미 끝났을 텐데. 잘 가게！"); return; }
        string name = ItemShop.Get(m, treasure).Name;
        Say(0x25C, $"{name}？ 지도를 손에 넣어 찾아보았지만, 어디에도 없었어. 이제 포기했네.", name);
        if (MessageBox.Show(this, MsgOr(0x25D, "그렇지, 이 지도를 금화 １０００닢에 팔아 주지. 사겠나？"), "지도", MessageBoxButton.YesNo)
            != MessageBoxResult.Yes) return;
        var p = _host.Player;
        if (p.Gold < 1000) { Say(0x25F, "뭐야, 돈이 없다면 얘기할 필요도 없지."); return; }
        if (!ItemShop.Give(m, map)) { Say(0x370, "당신, 소유하고 있는 아이템이 이미 한도에 이르고 있어."); return; }
        p.SetGold(p.Gold - 1000);
        if (Discoveries.MapTarget(m, map) is >= 0 and var spot)
            m.Write(ScenarioMemory.At(Discoveries.Table, spot, Discoveries.Flags), Discoveries.Get(m, spot).Flags | Discoveries.Found, false);
        Say(0x25E, "자, 지도를 받게. 열심히 찾아 보게나.");
    }

    /// <summary>주점 분위기 S(<c>0x0049A1B0</c>) — 주점에 들 때 매력 / 3 으로 정한다.</summary>
    private int _pubMood;

    /// <summary>
    /// 주점 「한턱내기」(<c>0x00424880</c>) — 술을 내 분위기를 올린다(<see cref="Pub.Treat"/>). 손님이 명성으로 맞은 뒤(<c>0x00424990</c>)
    /// 맡은 일이 없으면 모르는 항구 소문이나 짐 팔 곳 귀띔을 한다(<see cref="Pub.PortRumourFirst"/>).
    /// 그 앞에 운이 좋으면 주인공을 노리는 나라를 일러 준다(<see cref="Pub.Hunter"/>).
    /// </summary>
    private void TreatBar()
    {
        if (_host.Economy is not { } eco || _host.CurrentPort < 0 || _host.CurrentPort >= eco.States.Count) return;
        var p = _host.Player;
        int kind = eco.StateBytes[_host.CurrentPort * Economy.StateSize + 0x24];
        int price = kind < Navigators.TreatPrice.Length ? Navigators.TreatPrice[kind] : 3;
        string drink = kind < Pub.Drinks.Length ? Pub.Drinks[kind] : "술";
        int most = Pub.MostBottles(p.Gold, price);
        if (most <= 0) { Say(0x1E, "금화가 부족해요."); return; }
        int n = AmountDialog.Ask(this, MsgOr(0x13, $"우리집 {drink}(는) 최고요. 한병에 금화 {price}닢인데 몇병 사겠소？", drink, price), most);
        if (n <= 0) return;
        p.SetGold(p.Gold - n * price);
        _pubMood = Pub.Treat(_pubMood, n, eco.States[_host.CurrentPort].Economy, HeroField(Navigators.Stats + 6));
        Note($" · {drink} {n}병 · 분위기 {_pubMood}");
        // 손님들이 주인공 명성으로 맞는다(0x00424990) — 가장 큰 명성 < 1000 이면 0x24, < 5000 이면 0x84(교역상 · 해적 · 모험가), 그 위면 0x85.
        if (_host.Scenario is { } vm)
        {
            var (t, pi, ad) = Palace.Fames(vm.Memory, _host.HeroPersonNumber);
            int best = Math.Max(t, Math.Max(pi, ad));
            string title = best == t ? "교역상" : best == pi ? "해적" : "모험가";
            var (name, family) = _host.HeroName;
            if (best < 1000) Say(0x24, "한턱 써 줘서 고맙소！");
            else if (best < 5000) Say(0x84, $"아, 당신 꽤 유명한 {title} {name} {family}(가) 아니오！", title, name, family);
            else Say(0x85, $"야호, {name} {family}(가) 한턱 낸다니 기쁜데.", name, family);
            // 일이 없고 깃발 16 · 17 가운데 하나가 섰으며 18 이 아니면 왕이 찾는다는 소문(0x00424A57 → 0x00424F80), 깃발 17.
            if (_host.Common is { Chapter: 0 } rc && (rc.GetFlag(16) || rc.GetFlag(17)) && !rc.GetFlag(18) && _host.HeroNation < 6)
            {
                Say(-1, MsgOr(0x199, "그리고 보니, ") + KingCalls(_host.HeroNation) + MsgOr(0x1A0, "당신을 찾고 있다는 소문이 있소."));
                rc.SetFlag(17, true);
            }
            else if (_host.Common is not { Chapter: not 0 })
            {
                int hunter = Pub.Hunter(HeroField(Church.Luck),
                    k => vm.Memory.Read(ScenarioMemory.At(ScenarioMemory.NationTable, k, 3), false), Random.Shared.Next);
                if (hunter >= 0)
                {
                    string nation = hunter < NationNames.Length ? NationNames[hunter] : "해적";
                    SayText(MsgOr(0x25, "그건 그렇고, 당신, ") + MsgOr(0x28, $"{nation}(가) 노리고 있어.", nation));
                }
                else if (Pub.PortRumourFirst(n, eco.States[_host.CurrentPort].Economy, Random.Shared.Next) || !TellCargoTip()) TellPortRumour(vm);
            }
            else TellJobRumour(vm);
        }
    }

    /// <summary>
    /// 맡은 일의 소문(<c>0x00424DD0</c>) — 빚수금(변수 6 = 5)이나 칙명 희귀품(11, 아는 이가 정해졌고 물건 · 지도를 아직 안 가졌을 때)에서,
    /// 이 항구 번호와 그 사람(변수 17) 번호가 3 으로 나눈 나머지가 같으면 그 사람 있는 데를 알려 준다 — 항구에 있으면 0x257 · 0x258 · 0x259,
    /// 바다에 있으면 0x25A · 0x25B.
    /// </summary>
    private void TellJobRumour(ScenarioVm vm)
    {
        if (_host.Common is not { } job) return;
        var m = vm.Memory;
        int kind = job.GetVar(6), who = job.GetVar(17);
        if (kind == 11)
        {
            int item = job.GetVar(18);
            var owned = ItemShop.Owned(m);
            if (who == 0xFF || owned.Contains(item) || owned.Contains(item - 10)) return;
        }
        else if (kind != 5) return;
        if (_host.CurrentPort % 3 != who % 3) return;
        var full = _host.PersonName(who).Split(' ');
        string first = full[0], family = full.Length > 1 ? full[^1] : "";
        int at = Navigators.Get(m, who, Navigators.Port);
        if (at != 0xFF && _host.Ports is { } pt && at < pt.Ports.Count)
        {
            string portName = pt.Ports[at].Name;
            Say(0x257, $"{first} {family}(는) {portName}항에 있지.", first, family, portName);
            var (north, lat, east, lon) = Pub.Degrees(pt.Ports[at].X, pt.Ports[at].Y);
            SayText(MsgOr(0x258, $"{portName}항은 ", portName)
                    + MsgOr(north ? 0x7E : 0x7F, $"{(north ? "북위" : "남위")} {lat}도, ", lat)
                    + MsgOr(east ? 0x80 : 0x81, $"{(east ? "동경" : "서경")} {lon}도", lon)
                    + MsgOr(0x259, "근처야."));
            return;
        }
        Say(0x25A, $"{first} {family}？", first, family);
        Say(0x25B, "어디에 있냐고？");
    }

    /// <summary>짐 팔 곳 귀띔 — 「○○(라면) ○○ 지방에서 팔면 좋지 않을까？」(<see cref="Pub.CargoTip"/>). 짐이 없으면 false.</summary>
    private bool TellCargoTip()
    {
        if (_host.Economy is not { } eco) return false;
        var cargo = _host.Player.Ships.Select(s => s.Cargo.Select(c => c.Good).ToArray()).ToList();
        var (good, market) = Pub.CargoTip(cargo, [.. eco.Markets.Select(m => m.Pays)], Random.Shared.Next);
        if (good < 0 || good >= eco.GoodsNames.Count) return false;
        SayText(MsgOr(0x34E, $"{eco.GoodsNames[good]}(라면) ", eco.GoodsNames[good]) + MsgOr(0x181 + market, "")
                + MsgOr(0x34F, "지방에서 팔면 좋지 않을까？"));
        return true;
    }

    /// <summary>모르는 항구 소문 — 「그건 그렇고, 당신, 북위 ○도, 서경 ○도에 있는 ○○항에 가본 적 있소？」 뒤에 특산품(<c>0x00424C10</c>).</summary>
    private void TellPortRumour(ScenarioVm vm)
    {
        if (_host.Ports is not { } pt || _host.Economy is not { } eco) return;
        bool Known(int port) => port >= pt.Ports.Count || port >= eco.States.Count
            || (vm.Memory.Read(ScenarioMemory.At(ScenarioMemory.PortTable, port, 0x15), false) & 0x10) != 0;
        int to = Pub.RumourPort(_host.CurrentPort, Known, Random.Shared.Next);
        if (to < 0) return;
        var (north, lat, east, lon) = Pub.Degrees(pt.Ports[to].X, pt.Ports[to].Y);
        string head = MsgOr(0x25, "그건 그렇고, 당신, ")
            + MsgOr(north ? 0x7E : 0x7F, $"{(north ? "북위" : "남위")} {lat}도, ", lat)
            + MsgOr(east ? 0x80 : 0x81, $"{(east ? "동경" : "서경")} {lon}도", lon)
            + MsgOr(0x82, $"에 있는 {pt.Ports[to].Name}항에 가본 적 있소？", pt.Ports[to].Name);
        int good = eco.StateBytes[to * Economy.StateSize + 0x1A];
        string tail = good != 0xFF && good < eco.GoodsNames.Count
            ? MsgOr(0x83, $"그 곳은 {eco.GoodsNames[good]}(가) 유명하지.", eco.GoodsNames[good])
            : MsgOr(0x2E6, "마음이 끌리면 가 봐.");
        ScenarioDialog.Play(this, _host.Faces, [new ScenarioVm.Line("", head), new ScenarioVm.Line("", tail)]);
    }

    /// <summary>주점 「도박」(<c>0x00426B20</c>) — 블랙잭 · 다이스. 돈이 없으면 0x3F2.</summary>
    private void Gamble()
    {
        var p = _host.Player;
        if (p.Gold <= 0) { Say(0x3F2, "금화도 없는 놈을 상대할 순 없지."); return; }
        // 도박판 그림(GRAPH 27, 0x00426BF9)을 깔고 고른다.
        int game = BuildingMenu.Ask(this, "도박", ["Black Jack", "Dice"], _host.Graphs?.Get(27));
        if (game == 0) PlayBlackJack();
        else if (game == 1) PlayDice();
    }

    /// <summary>
    /// 다이스 한 자리(<see cref="Dice"/>, <c>0x004079D0</c>) — 걸고 흥정한 뒤 셋이 굴린다. 주인공은 남길 눈 자리(1 ~ 5)를 적어 두 번까지 다시 굴리고,
    /// 남은 같은 눈을 남긴다. 원본은 눈 그림을 눌러 고르고 시작하는 이를 돌려 가며 굴린다.
    /// </summary>
    private void PlayDice()
    {
        var p = _host.Player;
        int start = p.Gold;
        if (p.Gold < Dice.MinBet) { Say(0x3F2, "금화도 없는 놈을 상대할 순 없지."); return; }
        if (MessageBox.Show(this, MsgOr(0x3E9, "당신, 우리들과 다이스를 하겠소？"), "다이스", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Say(0x3EA, "헛, 봉이 또 오는군！");
        string Show(int[] d) => string.Join(" ", d.Select(x => x + 1));
        while (true)
        {
            int most = Math.Min(p.Gold, Dice.MaxBet);
            int bet = AmountDialog.Ask(this, MsgOr(0x3C2, "도박금은 얼마요？") + $" ({Dice.MinBet}-{most})", most);
            if (bet <= 0) break;
            if (bet < Dice.MinBet) { Say(0x3C3, "뭐야 이거！ 애들 장난도 아니구."); continue; }
            if (Dice.Raise(bet, Random.Shared.Next) is not { } raise)
                SayText(MsgOr(0x3E1, "그걸로 좋소.") + "\n" + MsgOr(0x3E2, "나도."));
            else if (MessageBox.Show(this, MsgOr(0x3D9, $"나는 금화 {raise}닢으로 했으면 좋겠는데.", raise), "다이스", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                bet = raise;
                SayText(MsgOr(0x3DA, "당신이 좋다면 난 상관없소.") + "\n" + MsgOr(0x3DB, "그래, 그렇게 나와야지."));
            }
            else
            {
                var (agreed, msg) = Dice.Refuse(bet, raise, Random.Shared.Next);
                bet = agreed;
                SayText(MsgOr(msg, $"도박금은 금화 {bet}닢으로 하지.", bet)
                        + "\n" + MsgOr(msg == 0x3DF ? 0x3E0 : msg == 0x3DD ? 0x3DE : 0x3DB, "좋소."));
            }
            bet = Math.Min(bet, p.Gold);
            p.SetGold(p.Gold - bet);
            var rand = Random.Shared;
            int[] Npc()
            {
                var d = Dice.Roll(new int[Dice.Count], new bool[Dice.Count], rand.Next);
                for (int r = 0; r < Dice.Rerolls; r++) d = Dice.Roll(d, Dice.Keep(d), rand.Next);
                return d;
            }
            int[] a = Npc(), b = Npc();
            var mine = Dice.Roll(new int[Dice.Count], new bool[Dice.Count], rand.Next);
            for (int r = 0; r < Dice.Rerolls; r++)
            {
                string? keep = TextDialog.Ask(this, MsgOr(0x3D8, "이 눈으로 승부를？") + $"  [{Show(mine)}]  남길 자리(1-5, 다 남기면 12345)", "12345", 5);
                if (keep == null || keep.Distinct().Count(c => c is >= '1' and <= '5') == 5) break;
                bool[] k = [.. Enumerable.Range(1, Dice.Count).Select(i => keep.Contains((char)('0' + i)))];
                mine = Dice.Roll(mine, k, rand.Next);
            }
            int result = Dice.Judge(mine, a, b);
            int say = result switch { 0 => 0x3E5, 4 => 0x3E7, 5 or 6 => 0x3E8, _ => 0x3E6 };
            SayText($"갑  {Show(a)}\n을  {Show(b)}\n당신  {Show(mine)}\n\n" + MsgOr(say, result == 0 ? "쳇, 제법인데！" : "도박금은 내가 가지겠어."));
            p.SetGold(Math.Min(PlayerState.MaxGold, p.Gold + Dice.Payout(result, bet)));
            if (p.Gold < Dice.MinBet)
            {
                SayText(MsgOr(0x3EB, "안됐네！ 그렇다고 너무 풀죽진 말게.") + "\n" + MsgOr(0x3EC, "돈이 생기면 또 오게！"));
                return;
            }
            if (MessageBox.Show(this, MsgOr(0x3ED, "또 한판하지 않겠소？"), "다이스", MessageBoxButton.YesNo) != MessageBoxResult.Yes) break;
        }
        if (p.Gold > start + 500) SayText(MsgOr(0x3EE, "어！ 벌써 돌아가는 거요？") + "\n" + MsgOr(0x3EF, "이기고 도망가다니 더러운 놈이다！"));
        else if (p.Gold + 500 < start) SayText(MsgOr(0x3F0, "뭐야, 벌써 그만두는 거야？") + "\n" + MsgOr(0x3F1, "흐흐, 역시 봉이였어！"));
        Note($" · 소지금 {start} → {p.Gold}");
    }

    /// <summary>
    /// 블랙잭 한 자리(<see cref="BlackJack"/>) — 「하겠소？」(0x3D3) 뒤 판마다 걸고 뽑고 가르며, 「한판 더」(0x3D5)를 묻는다.
    /// 끝에 처음보다 500 넘게 땄으면 0x3D6, 500 넘게 잃었으면 0x3D7(<c>0x00406E9B</c>).
    /// </summary>
    private void PlayBlackJack()
    {
        var p = _host.Player;
        int start = p.Gold;
        if (p.Gold < BlackJack.MinBet) { Say(0x3F2, "금화도 없는 놈을 상대할 순 없지."); return; }
        if (MessageBox.Show(this, MsgOr(0x3D3, "당신, 나하고 블랙잭을 하겠소？"), "블랙잭", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        while (true)
        {
            int most = BlackJack.MostBet(p.Gold);
            // 판 옆의 소지금(0x3BF · 0x3C0 — 금화는 1만 나머지, 금괴는 1만 닢 하나)
            string purse = $"소지금 금화 {p.Gold % 10000} · 금괴 {p.Gold / 10000}";
            int bet = AmountDialog.Ask(this, MsgOr(0x3C2, "도박금은 얼마요？") + $" ({BlackJack.MinBet}-{most}) — {purse}", most);
            if (bet <= 0) break;
            if (bet < BlackJack.MinBet) continue;
            p.SetGold(p.Gold - bet);
            // 건 돈을 두고 한마디(0x00407520) — 20 아래 「애들 장난」, 200 넘으면 「정말로 좋겠소？」, 그 밖은 「카드를 돌리겠다」. 판은 그대로 간다.
            if (bet < 20) Say(0x3C3, "뭐야 이거！ 애들 장난도 아니구.");
            else if (bet > 200) Say(0x3C4, "정말로 좋겠소？");
            else Say(0x3C5, "그럼 카드를 돌리겠다.");
            var deck = BlackJack.Shuffle(Random.Shared.Next);
            int next = 4;
            List<int> mine = [deck[0], deck[2]], his = [deck[1], deck[3]];
            string Hand(List<int> h) => string.Join(" ", h.Select(BlackJack.Name));
            while (BlackJack.Total(mine) <= 21 && mine.Count < BlackJack.MaxPlayerCards
                   && MessageBox.Show(this, $"딜러  {BlackJack.Name(his[0])} ??\n당신  {Hand(mine)}\n" + MsgOr(0x3D2, $"합계 {BlackJack.Total(mine)}", BlackJack.Total(mine))
                                      + "\n\n한 장 더 받겠습니까？", "블랙잭", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                mine.Add(deck[next++]);
            while (BlackJack.Total(his) < BlackJack.DealerStands && his.Count < BlackJack.MaxDealerCards) his.Add(deck[next++]);
            var (win, loss, msg) = BlackJack.Settle(mine, his);
            SayText($"딜러  {Hand(his)}  ({BlackJack.Total(his)})\n당신  {Hand(mine)}  ({BlackJack.Total(mine)})\n\n" + MsgOr(msg, win > 0 ? "네가 이겼다." : "그럼 도박금을 가지겠어. 미안！"));
            p.SetGold(BlackJack.Pay(p.Gold, bet, win, loss));
            if (p.Gold < BlackJack.MinBet) { Say(0x3D4, "살다 보면 이런일 저런일이 다 있지.\n너무 풀죽지 말게."); return; }
            if (MessageBox.Show(this, MsgOr(0x3D5, "한판 더 안 하겠소？"), "블랙잭", MessageBoxButton.YesNo) != MessageBoxResult.Yes) break;
        }
        if (p.Gold > start + 500) Say(0x3D6, "앗！ 벌써 돌아가는 거냐？\n이기고 도망가는 건 너무한데！");
        else if (p.Gold + 500 < start) Say(0x3D7, "하하하！ 덕분에 횡재했어. 그럼 또 오시오！");
        Note($" · 소지금 {start} → {p.Gold}");
    }

    /// <summary>
    /// 주점 「선원 해고」(<c>0x00424830</c> → <c>0x00424540</c>) — 배를 골라 「%s의 선원을 몇 명으로 하겠습니까(0-%d)？」(0x359)로
    /// 남길 수를 묻고, 줄었으면 「%d명의 선원을 해고하겠습니다.」(0x35A)를 다짐받는다. 끝나면 배 목록으로 돌아간다.
    /// 선원이 0 이 된 배는 사기도 0 이다.
    /// </summary>
    private void FireCrew()
    {
        var p = _host.Player;
        while (true)
        {
            int pick = BuildingMenu.Ask(this, "선원 해고", p.Ships.Select(sh => $"{sh.Name}  선원 {sh.Crew}/{sh.MaxCrew}").ToList());
            if (pick < 0) return;
            var ship = p.Ships[pick];
            int keep = AmountDialog.Ask(this, MsgOr(0x359, $"{ship.Name}의 선원을 몇 명으로 하겠습니까(0-{ship.Crew})？", ship.Name, ship.Crew),
                                        ship.Crew);
            if (keep < 0 || keep >= ship.Crew) continue;
            if (MessageBox.Show(this, MsgOr(0x35A, $"{ship.Crew - keep}명의 선원을 해고하겠습니다.", ship.Crew - keep), "선원 해고",
                                MessageBoxButton.YesNo) != MessageBoxResult.Yes) continue;
            ship.Crew = keep;
            if (ship.Crew == 0) ship.Morale = 0;
            _host.SyncLoad();
            Note($" · {ship.Name} 선원 {ship.Crew}명");
        }
    }

    /// <summary>교역소 「매각」 — 실은 교역품을 골라 다 판다(<see cref="Economy.SellPrice"/>).</summary>
    private void SellGoods()
    {
        if (_host.Economy is not { } eco) return;
        var cargo = _host.Player.CargoTotals().ToList();
        if (cargo.Count == 0) { Note(" · 팔 교역품이 없습니다"); return; }
        int pick = BuildingMenu.Ask(this, "매각 — 이 항구가 쳐 주는 값",
            cargo.Select(c => $"{eco.GoodsNames[c.Key],-6} {c.Value,4}통  {eco.SellPrice(_host.CurrentPort, c.Key),5}").ToList());
        if (pick < 0) return;
        int good = cargo[pick].Key, price = eco.SellPrice(_host.CurrentPort, good);
        int sold = _host.Player.SellAll(good, price);
        _host.SyncLoad();
        Note($" · {eco.GoodsNames[good]} {sold}통을 {sold * price}에 팔았습니다");
    }

    /// <summary>게임 말 하나를 상태줄 글로 — 줄 바꿈은 띄어 쓰고, 말이 없으면 <paramref name="fallback"/>.</summary>
    private string M(int id, string fallback, params object[] args) =>
        " · " + (_host.Messages is { } m && m[id].Length > 0 ? m.Format(id, args).Replace('\n', ' ') : fallback);

    private void Note(string text)
    {
        _doorNote = text;
        UpdateStatus();
    }

    /// <summary>
    /// 날씨 말 — 전조(<c>0x00419250</c>)는 살피는 이(<see cref="Navigators.Lookout"/>)가 있고 rand(100) ≤ 그이 직감(+0x1F)일 때만,
    /// 지식(+0x1E)이 70 아래면 두 칸 뒤 말(「불길한 예감」 쪽), +0x2F 의 8 이면 한 칸 뒤(말투)다. 걷힘은 그대로다(0x004198E2).
    /// 원본은 창으로 띄우지만 여기서는 상태줄에 적는다.
    /// </summary>
    private string? WeatherLine(int id)
    {
        if (id is Weather.OmenStorm or Weather.OmenCalm or Weather.OmenFog)
        {
            if (_host.Scenario is not { } vm) return null;
            int who = Navigators.Lookout(vm.Memory);
            if (who < 0 || _host.CurrentPort >= 0) return null;
            if (_host.Roll(100) > Navigators.Get(vm.Memory, who, Navigators.Stats + 3)) return null;
            if (Navigators.Get(vm.Memory, who, Navigators.Stats + 2) < 70) id += 2;
            if ((Navigators.Get(vm.Memory, who, Navigators.Mood) & 8) != 0) id++;
        }
        return MsgOr(id, id switch
        {
            Weather.ClearedStorm => "제독님, 겨우 위기를 넘겼습니다.",
            Weather.ClearedCalm => "제독님, 바람, 바람이 붑니다.",
            Weather.ClearedFog => "제독님, 겨우 안개가 걷혔습니다.",
            _ => "제독님, 날씨가 이상한데요.",
        });
    }

    /// <summary>항해사가 마지막으로 한 말(날씨) — 상태줄에 적는다.</summary>
    private string _seaNote = "";
    private ComboBox _portPick = null!;

    public SeaMapWindow()
    {
        Title = "대항해시대2 — 세계지도";
        Width = 1180;
        Height = 700;
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x1A));

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        bar.Children.Add(Button("게임 폴더…", (_, _) => PickFolder()));
        bar.Children.Add(Button("세계지도", (_, _) => { _host.ShowWorld(); _portPick.SelectedIndex = -1; Retitle(); }));
        bar.Children.Add(Button("출항", (_, _) => PutToSea()));
        bar.Children.Add(Button("저장", (_, _) => SaveToSlot()));
        bar.Children.Add(Button("함대", (_, _) => ShowFleetInfo()));
        bar.Children.Add(Button("편성", (_, _) => Organize()));
        bar.Children.Add(Button("지휘", (_, _) => Command()));
        bar.Children.Add(Button("항이동", (_, _) => SailToPort()));
        bar.Children.Add(Button("정박", (_, _) => ToggleAnchor()));
        bar.Children.Add(Button("입항", (_, _) => EnterPortFromSea(-1)));
        bar.Children.Add(Button("상륙", (_, _) => Land()));
        bar.Children.Add(Button("공격", (_, _) => Attack()));
        bar.Children.Add(Button("아이템", (_, _) => UseItem()));

        // 항구 고르개. 표를 못 읽으면 번호만 늘어놓는다.
        _portPick = new ComboBox
        {
            Width = 150,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _portPick.SelectionChanged += (_, _) =>
        {
            if (_portPick.SelectedIndex >= 0 && _host.ShowPort(_portPick.SelectedIndex)) Retitle();
        };
        bar.Children.Add(_portPick);
        bar.Children.Add(Toggle("조각 격자", v => { _host.Renderer.ShowGrid = v; _host.Invalidate(); }));
        _arrows = Toggle("바람·해류", v => { _host.ShowArrows = v; _host.Invalidate(); });
        bar.Children.Add(_arrows);
        _chipToggle = Toggle("칩으로 그리기", v => { _host.Renderer.UseChips = v; _host.Invalidate(); }, true);
        bar.Children.Add(_chipToggle);
        Grid.SetRow(bar, 0);
        grid.Children.Add(bar);

        Grid.SetRow(_host, 1);
        grid.Children.Add(_host);

        _status.Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xB4, 0xC0));
        Grid.SetRow(_status, 2);
        grid.Children.Add(_status);

        Content = grid;

        _host.Painted += UpdateStatus;
        _host.SeaDay += OnSeaDay;
        _host.GameOver += OnGameOver;
        _host.Ambushed += Defend;
        _host.MonsterMet += kind => ScenarioDialog.Play(this, _host.Faces,
            [new ScenarioVm.Line("", MsgOr(0x1FE + kind * 2, "")), new ScenarioVm.Line("", MsgOr(0x1FF + kind * 2, ""))]);
        _host.Wrecked += lost => ScenarioDialog.Play(this, _host.Faces, [.. lost.Select(l => new ScenarioVm.Line("",
            MsgOr(0x166, $"제독님, {l.Ship}호가 난파했습니다.", l.Ship)
            + (l.Captain != null ? MsgOr(0x329, $" {Josa(l.Captain, "은", "는")} 기적적으로 구조되었습니다.", l.Captain) : "")))]);
        _host.Sighted += OnSighted;
        _host.DiseaseCaught += name =>
        {
            _seaNote = name == "쥐 떼" ? M(0x1D9, "쥐 떼가 일었습니다") : M(0x1DA, $"{name}에 걸린 자가 있습니다");
            UpdateStatus();
            // 원본은 그림과 함께 말 창을 띄운다 — 쥐 떼 57(0x004179FF), 괴혈병 59(0x00417B2B).
            if (name == "쥐 떼") SayWith(57, 0x1D9, "제독님, 쥐 떼가 발생하고 있는 모양입니다.");
            else SayWith(59, 0x1DA, "제독님, 선원 중에 괴혈병에 걸린 자가 있는 모양입니다.");
        };
        _host.PortAhead += port => EnterPortFromSea(port);
        // 달마다 급여(0x0042C4E0) — 「제독님, 선원들에게 급료로 금화 %d닢을 지불했습니다.」(0x23E) · 못 냈으면 0x23C.
        _host.WagesPaid += total =>
        {
            if (total > 0) Say(0x23E, $"제독님, 선원들에게 급료로 금화 {total}닢을 지불했습니다.", total);
            else Say(0x23C, "제독님, 선원들의 급료를 지불할 수 없습니다.");
        };
        _host.StoryEnded += OnStoryEnded;
        // 마을 사람과 부딪치면 말을 건다(0x004335F0).
        _host.FolkMet += kind =>
        {
            foreach (var (id, args) in _host.FolkLines(kind, NationNames7)) Say(id, "", args);
        };
        _host.ShipAdrift += name =>
        {
            _seaNote = M(0x150, $"제독님, {name}(가) 보이지 않습니다.", name);
            UpdateStatus();
        };
        _host.FleetReady += f =>
        {
            f.Weather.Said += id =>
            {
                if (WeatherLine(id) is not { } line) return;
                _seaNote = $" · 「{line}」";
                UpdateStatus();
            };
            // 들이닥친 날은 그림 58 과 말(폭풍 0x160 · 무풍 0x162 · 안개 0x164)을 창으로(0x004193D0).
            f.Weather.Struck += kind => SayWith(58, 0x15E + (int)kind * 2, kind switch
            {
                Weather.Kind.Storm => "제독님, 폭풍우입니다！！",
                Weather.Kind.Calm => "제독님, 바람이 완전히 멀었습니다.",
                _ => "제독님, 심한 안개군요.",
            });
        };
        _host.BuildingEntered += (b, open) =>
        {
            _doorNote = open
                ? $" · 「{PortDoors.Names[b]}」에 들어갔습니다"
                : $" · 「{PortDoors.Names[b]}」 — {PortDoors.LockedText}";
            UpdateStatus();
            // 열려 있으면 그 건물의 MENU.DAT 목록을 띄운다 — 그림을 그리는 틈 안이라 한 박자 뒤에 연다.
            if (open) Dispatcher.BeginInvoke(() => EnterBuilding(b));
            // 닫힌 시각이면 「문이 잠겨 있습니다.」 창(0x2FD, 0x00431F5D → 0x00432160).
            else Dispatcher.BeginInvoke(() => Say(0x2FD, PortDoors.LockedText));
        };
        Loaded += (_, _) => OpenBest();
        KeyDown += OnKey;
    }

    private static Button Button(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3) };
        b.Click += click;
        return b;
    }

    private static CheckBox Toggle(string text, Action<bool> set, bool on = false)
    {
        var c = new CheckBox { Content = text, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, IsChecked = on };
        c.Checked += (_, _) => set(true);
        c.Unchecked += (_, _) => set(false);
        return c;
    }

    /// <summary>
    /// 구워 둔 자산이 있으면 그것으로, 없으면 게임 폴더로 연다.
    /// </summary>
    private void OpenBest()
    {
        var dir = AssetPack.FindFolder();
        if (dir != null)
        {
            var pack = AssetPack.Load(dir);
            if (pack != null && _host.StartFromAssets(pack))
            {
                _gameDir = dir;
                _chipToggle.IsChecked = _host.Renderer.UseChips;
                FillPortList();
                _host.FitToWindow();
                _host.Invalidate();
                UpdateStatus();
                return;
            }
        }
        Open(GameFolder.Find());
    }

    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "대항해시대2 폴더" };
        if (dlg.ShowDialog(this) == true) Open(dlg.FolderName);
    }

    private void Open(string? dir)
    {
        if (dir == null) { _status.Text = GameFolder.Explain(); return; }
        if (!GameFolder.IsGameFolder(dir)) { _status.Text = GameFolder.Explain(dir); return; }

        _gameDir = dir;
        if (!_host.Start(dir)) { _status.Text = _host.Status; return; }

        _chipToggle.IsChecked = _host.Renderer.UseChips;
        FillPortList();
        _host.FitToWindow();
        _host.Invalidate();
        UpdateStatus();
    }

    /// <summary>항구 고르개를 채운다. 리스본이 0번이라 그것으로 시작한다.</summary>
    private void FillPortList()
    {
        _portPick.Items.Clear();
        var t = _host.Ports;
        int n = t?.Ports.Count ?? PortMap.Count;
        for (int i = 0; i < n; i++)
            _portPick.Items.Add(t != null && i < t.Ports.Count ? $"{i,3}  {t.Ports[i].Name}" : $"항구 {i}");
        _portPick.SelectedIndex = -1;
    }

    /// <summary>보고 있는 화면 이름을 제목에 얹는다.</summary>
    private void Retitle()
    {
        Title = $"대항해시대2 — {_host.SceneName}";
        _doorNote = "";
        _host.Invalidate();
        UpdateStatus();
    }

    /// <summary>
    /// 그 주인공의 시작 항구를 띄운다. 표에 이름이 있으면 그것으로 찾고, 없으면 번호로 간다.
    /// </summary>
    public void ShowHomePort(int hero)
    {
        // 고향은 SDATA.DAT 주인공 함대의 머문 항구(+0x26) 그대로다 — 에른스트는 암스테르담(33)이다.
        string[] homes = ["리스본", "세빌리아", "제노바", "이스탄불", "암스테르담", "런던"];
        string want = hero >= 0 && hero < homes.Length ? homes[hero] : homes[0];
        _host.SetHero(hero);

        int index = _host.Ports?.ByName(want)?.Index ?? PortTable.Lisbon;
        if (_host.ShowPort(index)) { _portPick.SelectedIndex = index; Retitle(); }

        // 항구를 한 번 보여 주고, 나가면 그 앞바다에서 배가 뜬다.
        _homePort = index;
    }

    private int _homePort = PortTable.Lisbon;

    /// <summary>
    /// 새 게임 이름 짓기(<c>0x00416665</c>) — 「이름을 바꿔 주세요.」(0x3AC) 여섯 자까지, 「좋습니까？」(0x3B3)에 그러면 정한다.
    /// 원본은 아니오면 주인공 고르기로 돌아가는데, 여기서는 다시 묻는다. 비우면 그대로.
    /// </summary>
    public void AskHeroName()
    {
        while (true)
        {
            string? name = TextDialog.Ask(this, MsgOr(0x3AC, "이름을 바꿔 주세요."), _host.HeroName.Name, 6);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (MessageBox.Show(this, $"{name} {_host.HeroName.Family}\n" + MsgOr(0x3B3, "좋습니까？"), "이름", MessageBoxButton.YesNo) != MessageBoxResult.Yes) continue;
            _host.RenameHero(name);
            Retitle();
            return;
        }
    }

    /// <summary>
    /// 「함대」 — 목록 26(자함대정보 · 함선정보 · 인물정보 · 주인공정보 · 적하정보 …) 가운데 넷을 글로 보인다.
    /// 원본은 그림 창(목록 47 의 머리 글)이다.
    /// </summary>
    /// <summary>
    /// 「항해일지」(<c>0x00444690</c>) — 적힌 줄을 날짜와 함께 늘어놓는다(원본은 한 쪽에 일곱 줄). 해전 줄은 남 함대 번호 / 10 이 나라,
    /// 번호 % 10 이 0 이면 함 · 1 ~ 4 상선 · 그 밖 전함(목록 0x34), 60 넘으면 해적 함, 끝은 목록 0x35.
    /// </summary>
    private void ShowLogbook()
    {
        var log = _host.Log.Entries;
        if (log.Count == 0) { Note(" · 항해일지가 비어 있습니다"); return; }
        string[] kinds = _host.Menus?.Lists is { Count: > 0x34 } l34 ? [.. l34[0x34]] : ["함", "상선", "전함"];
        string[] ends = _host.Menus?.Lists is { Count: > 0x35 } l35 ? [.. l35[0x35]] : [", 승리했다", "했지만, 일몰로 인해 무승부다", "했지만,도망칠수밖에 없었다"];
        string PortName(int k) => _host.Ports is { } pt && k >= 0 && k < pt.Ports.Count ? pt.Ports[k].Name : $"#{k}";
        string Line(Logbook.Entry e) => e.Type switch
        {
            Logbook.Arrived => MsgOr(0x16E, $"우리 함대는 {e.A}일간의 항해를 끝내고 {PortName(e.B)}에 기항했다.", e.A, PortName(e.B)),
            Logbook.Departed => MsgOr(0x16F, $"우리 함대는 {e.A}일분의 물자를 갖추어 {PortName(e.B)}항을 떠났다.", e.A, PortName(e.B)),
            Logbook.Battle => MsgOr(0x345, "", NationNames7[Math.Min(6, e.A / 10)],
                                    kinds[e.A >= 60 ? 0 : e.A % 10 == 0 ? 0 : e.A % 10 < 5 ? 1 : 2], ends[Math.Clamp(e.B, 0, ends.Length - 1)]),
            Logbook.Found => MsgOr(0x17D, $"{PortName(e.A)}항을 발견했다.", PortName(e.A)),
            Logbook.Bought => MsgOr(0x180, "", _host.Economy is { } eco && e.A < eco.GoodsNames.Count ? eco.GoodsNames[e.A] : "?", (e.B << 16) + e.C, e.D),
            _ => "",
        };
        var text = new System.Text.StringBuilder();
        foreach (var e in log.Reverse())
            text.AppendLine($"{1501 + e.Year}년 {e.Month + 1}월 {e.Day + 1}일  {Line(e).Replace("\\n", " ").Replace("\n", " ")}");
        MessageBox.Show(this, text.ToString(), "항해일지");
    }

    /// <summary>선종 그림 번호 — 원본은 배 기록 +0x0F(선종)에 28 을 더한다(<c>0x00435300</c>).</summary>
    private static int ShipGraph(int type) => 28 + type;

    /// <summary>기능 이름 — 사람 +0x30 의 0x10 · 8 · 4 · 2 · 1(원본 표 <c>0x004573E8</c> 은 일본어 그대로다).</summary>
    private static readonly string[] SkillNames = ["측량술", "지도작성술", "포술", "회계술", "교섭술"];

    /// <summary>
    /// 인물정보 칸(<c>0x004427DE</c>)의 나머지 — 나이(+0x2A, 0x382), 항해 · 전투 경험(+0x26 · +0x28 낱말, 0x387 · 0x389),
    /// 국적(+0x31 &amp; 0xF, 0x38A), 기능(+0x30, 0x38B).
    /// </summary>
    private string PersonDetail(ScenarioMemory m, int person)
    {
        int Word(int f) => m.Read(ScenarioMemory.At(ScenarioMemory.PeopleTable, person, f), true);
        int age = Navigators.Get(m, person, 0x2A), skills = Navigators.Get(m, person, SkillField);
        int nation = Navigators.Get(m, person, Navigators.Kind) & 0xF;
        var has = SkillNames.Where((_, i) => (skills & (0x10 >> i)) != 0).ToList();
        return $"나이 {age}세 · 항해 경험 {Word(0x26)} · 전투 경험 {Word(0x28)} · 국적 {(nation < NationNames7.Length ? NationNames7[nation] : "없음")}"
               + $" · 기능 {(has.Count > 0 ? string.Join(" · ", has) : "없음")}";
    }

    /// <summary>
    /// 자함대정보 끝 줄(<c>0x00442C60</c>) — 제독(0x397) · 항해 · 전투 레벨(0x398 · 0x399), 그리고 가장 느린 배의 선회력(+4) · 추진력(+5),
    /// 가장 낮은 내구, 선원 · 단 대포 합, 전투력 합(0x39A ~ 0x39C). 배 하나의 전투력은 갑판 선원(300 까지) + 내구 x 2
    /// + (사거리 x 2 + 위력) x min(단 대포, 갑판 선원) / 10.
    /// </summary>
    private void FleetSummary(System.Text.StringBuilder sb)
    {
        var p = _host.Player;
        if (p.Ships.Count == 0) { sb.AppendLine(MsgOr(0x396, "배를 한 척도 가지고 있지 않습니다.")); return; }
        int hero = _host.HeroPersonNumber;
        int Lv(int f) => _host.Scenario is { } vm ? Navigators.Get(vm.Memory, hero, f) : 0;
        // 말 안의 글자 빛깔 꼬리표(C6 · C7 · B0 · B8 …)는 뗀다.
        static string Plain(string s) => System.Text.RegularExpressions.Regex.Replace(s, "[BC][0-9A-F]", "");
        sb.AppendLine(Plain(MsgOr(0x397, $"{_host.PersonName(hero)}제독", _host.PersonName(hero))));
        sb.AppendLine(Plain(MsgOr(0x398, $"항해 레벨:{Lv(Navigators.NavLevel),3}", Lv(Navigators.NavLevel))));
        sb.AppendLine(Plain(MsgOr(0x399, $"전투 레벨:{Lv(Navigators.BattleLevel),3}", Lv(Navigators.BattleLevel))));
        int crew = 0, guns = 0, power = 0, hull = int.MaxValue, slowest = 0, slowSpeed = int.MaxValue;
        var sailing = _host.Fleet?.Ships;
        var wind = _host.Fleet?.LastWind ?? (0, 0);
        int angle = Sailing.AngleOf(wind.Dir, _host.Fleet?.Heading ?? 0);
        for (int i = 0; i < p.Ships.Count; i++)
        {
            var s = p.Ships[i];
            crew += s.Crew;
            guns += s.Mounted;
            int deck = Math.Min(300, (100 - s.SailShare - s.WatchShare) * s.Crew / 100);
            int gk = Math.Clamp(s.GunKind, 0, 7);
            power += deck + s.Hull * 2 + (Battle.GunRange[gk] * 2 + Battle.GunPower[gk]) * Math.Min(s.Mounted, deck) / 10;
            hull = Math.Min(hull, s.Hull);
            int v = sailing != null && i < sailing.Count ? Sailing.ShipSpeed(sailing[i], angle, wind.Speed) : 0;
            if (v < slowSpeed) { slowSpeed = v; slowest = i; }
        }
        var slow = p.Ships[slowest];
        sb.AppendLine(Plain(MsgOr(0x39A, $"함대선회력:{slow.Upwind,3}  선원 총수:{crew,4}명", slow.Upwind, crew)));
        sb.AppendLine(Plain(MsgOr(0x39B, $"함대추진력:{slow.Sail,3}  총포문수 :{guns,4}", slow.Sail, guns)));
        sb.AppendLine(Plain(MsgOr(0x39C, $"함대내구력:{hull,3}  전투력   :{power,5}", hull, power)));
    }

    private void ShowFleetInfo()
    {
        string[] items = ["자함대정보", "인물정보", "주인공정보", "적하정보", "발견물정보", "적하일람", "함선정보", "항해일지", "항해도", "항구맵"];
        int pick = BuildingMenu.Ask(this, "함대", items);
        if (pick < 0) return;
        if (pick is 8 or 9 && _host.Map is { } chart)
        {
            // 「항해도」(0x004437E0) · 「항구맵」(0x00444250) — 조사 판으로 가린 세계지도, 항구맵은 들른 항구를 더한다.
            var ports = pick == 9 && _host.Ports is { } pt
                ? pt.Ports.Where(q => _host.PortVisited(q.Index)).Select(q => (q.Name, q.X, q.Y)).ToList()
                : [];
            (double, double)? at = _host.Fleet is { } fl ? (fl.X, fl.Y) : null;
            SeaChartWindow.Show(this, items[pick], chart, _host.Survey, at, ports);
            return;
        }
        if (pick == 7) { ShowLogbook(); return; }
        if (pick == 5) { CargoAdvice(); return; }
        if (pick == 6)
        {
            // 목록 26 「함선정보」 — 배를 하나 골라 그 배 그림(28 + 선종, 0x00435300)과 기록을 띄운다(0x00441FBF).
            var ships = _host.Player.Ships;
            if (ships.Count == 0) return;
            int k = ships.Count == 1 ? 0 : BuildingMenu.Ask(this, "함선정보", ships.Select(x => x.Name).ToList());
            if (k < 0) return;
            var sh = ships[k];
            string tn = _host.StartTables is { } t0 && sh.Type < t0.Templates.Count ? t0.Templates[sh.Type].Name ?? "" : "";
            // 원본 칸(0x00442140) — 선장(0x375) · 무장(0x377 · 0x378) · 운항/파수/갑판(0x379 · 0x37A) · 운항요원(0x37B) · 적재율(0x37C)
            // · 짐(0x37F). 속도(0x37E)는 바람에 따라 바뀌어 뺐다.
            int gk = Math.Clamp(sh.GunKind, 0, 7);
            int sail = sh.Crew * sh.SailShare / 100, watch = sh.Crew * sh.WatchShare / 100;
            int need = _host.StartTables is { } t1 && sh.Type < t1.ShipTypes.Count ? t1.ShipTypes[sh.Type].RequiredCrew : 0;
            var lines = new List<string>
            {
                $"{sh.Name}호 — {tn} · 등급 {sh.Grade}",
                $"선장: {_host.PersonName(sh.Captain >= 0 ? sh.Captain : _host.HeroPersonNumber)}",
                $"무장: {(sh.Mounted > 0 ? Shipyard.GunNames[gk] : "없음")} {sh.Mounted}문/{sh.Guns}문({sh.Mounted * Shipyard.GunWeight[gk]})",
                $"운항/파수/갑판: {sail}명 {watch}명 {sh.Crew - sail - watch}명",
                $"운항요원: {need}명",
                $"적재율: {(sh.Room > 0 ? sh.Load * 100 / sh.Room : 0)}%",
                $"내구 {sh.Hull}/{sh.MaxHull} · 역풍 {sh.Upwind} · 돛 {sh.Sail}",
            };
            for (int i = 0; i < 4; i++)
                if (sh.Supplies[i] > 0) lines.Add($"{Economy.SupplyNames[i],-9}{sh.Supplies[i],5}");
            if (_host.Economy is { } ge)
                foreach (var (good, amount) in sh.Cargo.Where(c => c.Good >= 0))
                    lines.Add($"{ge.GoodsNames[good],-9}{amount,5}");
            string info = string.Join("\n", lines);
            if (_devQuiet) return;
            ScenarioDialog.Play(this, _host.Faces, [new ScenarioVm.Line("", info)], _host.Graphs?.Get(ShipGraph(sh.Type)));
            return;
        }
        var p = _host.Player;
        var vm = _host.Scenario;
        var st = _host.StartTables;
        var sb = new System.Text.StringBuilder();
        string Captain(PlayerState.Ship s) => _host.PersonName(s.Captain >= 0 ? s.Captain : _host.HeroPersonNumber);
        int Get(int person, int field) => vm != null ? Navigators.Get(vm.Memory, person, field) : 0;
        switch (pick)
        {
            case 0:
                foreach (var s in p.Ships)
                {
                    string type = st != null && s.Type < st.Templates.Count ? st.Templates[s.Type].Name ?? "" : "";
                    sb.AppendLine($"{s.Name} ({type}) — 선장 {Captain(s)}");
                    sb.AppendLine($"    내구 {s.Hull}/{s.MaxHull} · 선원 {s.Crew}/{s.MaxCrew} · 사기 {s.Morale} · 짐 {s.Load}/{s.Room}");
                    if (s.Mounted > 0 || s.Figurehead > 0)
                        sb.AppendLine($"    대포 {(s.Mounted > 0 ? $"{Shipyard.GunNames[Math.Clamp(s.GunKind, 0, 7)]} {s.Mounted}/{s.Guns}(무게 {s.Mounted * Shipyard.GunWeight[Math.Clamp(s.GunKind, 0, 7)]})" : "없음")}"
                                      + (s.Figurehead > 0 ? $" · 선수상 {Shipyard.Figureheads[s.Figurehead]}" : ""));
                }
                if (p.Rats || p.Scurvy) sb.AppendLine((p.Rats ? "쥐 떼 " : "") + (p.Scurvy ? "괴혈병" : ""));
                FleetSummary(sb);
                break;
            case 1:
                if (vm == null) break;
                foreach (var (_, person, wage) in Navigators.Mates(vm.Memory))
                {
                    var stats = Enumerable.Range(0, 7).Select(k => Get(person, Navigators.Stats + k));
                    string role = Get(person, Navigators.Status) == Navigators.Captain ? "선장" : "쉬는 중";
                    sb.AppendLine($"{_host.PersonName(person)} — {role} · 급여 {wage} · 호감 {Get(person, Navigators.Liking)}");
                    sb.AppendLine($"    능력치 {string.Join(" · ", stats)} · 항해 {Get(person, Navigators.NavLevel)} · 전투 {Get(person, Navigators.BattleLevel)}");
                    sb.AppendLine("    " + PersonDetail(vm.Memory, person));
                }
                if (sb.Length == 0) sb.AppendLine("동료가 없습니다.");
                break;
            case 2:
            {
                int hero = _host.HeroPersonNumber;
                var stats = Enumerable.Range(0, 7).Select(k => Get(hero, Navigators.Stats + k));
                string nation = _host.HeroNation < NationNames.Length ? NationNames[_host.HeroNation] : "없음";
                var (trade, pirate, adventure) = vm != null ? Palace.Fames(vm.Memory, _host.HeroPersonNumber) : (0, 0, 0);
                sb.AppendLine($"{_host.PersonName(hero)} — {nation} · 작위 {_host.HeroRank}");
                sb.AppendLine($"명성 교역 {trade} · 해적 {pirate} · 모험 {adventure}");
                sb.AppendLine($"능력치 {string.Join(" · ", stats)}");
                sb.AppendLine($"항해 레벨 {Get(hero, Navigators.NavLevel)} · 전투 레벨 {Get(hero, Navigators.BattleLevel)} · 운 {Get(hero, Church.Luck)}");
                if (vm != null) sb.AppendLine(PersonDetail(vm.Memory, hero));
                // 원본 주인공정보(0x00443230)는 소지금을 금화(나머지 1만) · 금괴(1만 닢 하나)로 가르고(0x38F · 0x390),
                // 나라마다 공헌도(주인공 표 +6 + 나라, 0x393)를 늘어놓는다.
                sb.AppendLine($"소지금 금화 {p.Gold % 10000} · 금괴 {p.Gold / 10000}{BalanceText()}");
                if (vm != null)
                {
                    int hp = _host.HeroPersonNumber;
                    sb.AppendLine("공헌도 " + string.Join(" · ", Enumerable.Range(0, 6).Select(n =>
                        $"{NationNames7[n]} {vm.Memory.Read(ScenarioMemory.At(ScenarioMemory.HeroTable, hp, 6 + n), false)}")));
                    var owned = ItemShop.Owned(vm.Memory).Select(i => ItemShop.Get(vm.Memory, i).Name);
                    sb.AppendLine($"아이템 {string.Join(" · ", owned)}");
                }
                break;
            }
            case 4:
                // 목록 26 「발견물정보」 — 찾은 것과 판 것.
                if (vm == null) break;
                for (int i = 0; i < Discoveries.Count; i++)
                {
                    var d = Discoveries.Get(vm.Memory, i);
                    if (d.IsAbsent || !d.IsFound) continue;
                    bool sold = (d.Flags & Patrons.Reported) != 0;
                    sb.AppendLine($"{_host.Colony?.Name(i) ?? i.ToString()} ({Discoveries.KindNames[d.Kind]}){(sold ? " — 보고함" : "")}");
                }
                if (sb.Length == 0) sb.AppendLine(_host.Messages?[0x372] is { Length: > 0 } none ? none : "발견물이 없습니다.");
                break;
            default:
                if (_host.Economy is not { } eco) break;
                foreach (var s in p.Ships)
                {
                    var cargo = s.Cargo.Where(c => c.Good >= 0).Select(c => $"{eco.GoodsNames[c.Good]} {c.Amount}");
                    sb.AppendLine($"{s.Name}: 물 {s.Supplies[0]} · 식량 {s.Supplies[1]} · 자재 {s.Supplies[2]} · 탄약 {s.Supplies[3]}"
                                  + (cargo.Any() ? " · " + string.Join(" · ", cargo) : ""));
                }
                break;
        }
        MessageBox.Show(this, sb.Length > 0 ? sb.ToString() : "—", items[pick]);
    }

    /// <summary>
    /// 「공격」(<c>0x00409DC0</c>) — 시각이 15 ~ 59 틱이어야 한다(아니면 말 0x30D). 둘레 두 칩 안의 함대를 고르고(없으면 0x30A),
    /// 같은 나라면 0x30E 로 묻는다. 걸 때 말(<c>0x00410CB0</c>): 해적이면 0x32B 뒤 0x32D, 아니면 0x32C. 상선(번호 % 10 이 1 ~ 4)은
    /// 맞는 쪽 세기 x 3/2 가 덮치는 쪽보다 작으면 0x32F 로 배를 세우겠다고 하고 — 받아들이면 싸우지 않고 적 기함 이탈(2)로 끝난다 —
    /// 아니면 0x330, 그 밖의 함대는 0x32E 뒤 싸운다. 세기는 함대마다 (내구 합 + 4) / 5(<c>0x0042ACA0</c>).
    /// </summary>
    private void Attack()
    {
        if (_host.Fleet is not { } f || _host.CurrentPort >= 0 || _host.StartTables == null) return;
        if (f.TimeOfDay is < 15 or >= 60) { Say(0x30D, "야간에는 전쟁을 개시할 수 없습니다."); return; }
        var near = _host.FleetsInReach();
        if (near.Count == 0) { Say(0x30A, "전투 가능한 함대는 없습니다."); return; }
        int pick = BuildingMenu.Ask(this, "공격", [.. near.Select(n => MsgOr(0x376, $"{_host.PersonName(n.Admiral)} 함대", _host.PersonName(n.Admiral)))]);
        if (pick < 0) return;
        var enemy = near[pick];
        if (!enemy.Pirate && enemy.Nation == _host.HeroNation && _host.HeroNation != 6
            && MessageBox.Show(this, MsgOr(0x30E, "같은 나라 사람을 공격할 셈입니까？"), "공격", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        string admiral = _host.PersonName(enemy.Admiral);
        int ours = (_host.Player.Ships.Sum(s => s.Hull) + 4) / 5, theirs = (enemy.Slots.Sum(s => s.Durability) + 4) / 5;
        bool fight = true;
        if (enemy.Pirate)
        {
            Say(0x32B, $"해적 {admiral}！", "", admiral);
            Say(0x32D, "뭘 씨부렁거리고 있냐！");
        }
        else
        {
            Say(0x32C, "거기 있는 함대는 멈춰라！ 멈추지 않으면 공격하겠다！");
            if (enemy.Index % 10 is >= 1 and <= 4)
            {
                if (theirs * 3 / 2 < ours)
                    fight = MessageBox.Show(this, MsgOr(0x32F, "배를 정지시키겠다."), "공격", MessageBoxButton.YesNo) != MessageBoxResult.Yes;
                else Say(0x330, "너 같은 놈에게, 귀중한 돈과 적하를 넘겨줄 순 없다！");
            }
            else Say(0x32E, "우리 함대를 상대하려 하다니, 겁도 없는 녀석이군！");
        }

        var battle = _host.OpenBattle(enemy);
        f.UnderSail = false;
        foreach (var (who, line) in _host.BattleDeserters)
            ScenarioDialog.Play(this, _host.Faces, [new ScenarioVm.Line(who, Deserter(line))]);
        foreach (var (side, ally) in _host.BattleAllies.OrderByDescending(a => a.Key))
        {
            var p = _host.StartTables!.People[Math.Clamp(ally.Admiral, 0, _host.StartTables.People.Count - 1)];
            int id = side == 3 ? 0x30B : 0x30C;
            Say(id, $"{p.Name} {p.Family}(가) {(side == 3 ? "수비측" : "공격측")}의 원군으로서 전투에 가담했습니다.", p.Name, p.Family);
        }
        if (!fight) battle.Result = Engine.Sea.Battle.Outcome.EnemyLeft;
        else BattleWindow.Run(this, battle, _host.Messages, $"해전 — {admiral} 함대", _host.DuelFighter, UseItem);
        f.SetTimeOfDay(battle.Time);
        var spoils = _host.CloseBattle(battle, enemy);
        if (battle.Result is Engine.Sea.Battle.Outcome.EnemySunk or Engine.Sea.Battle.Outcome.DuelWon)
        {
            var st = _host.StartTables!;
            var hp = st.People[Math.Clamp(_host.HeroPersonNumber, 0, st.People.Count - 1)];
            var d = f.Date;
            SayWith(53, 0x341, $"적의 기함은 깃발을 내리고 항복의사를 표시했다. {hp.Name} {hp.Family}(는) {NationNames7[Math.Min(6, enemy.Nation)]}함대를 섬멸한 것이다.",
                hp.Name, hp.Family, NationNames7[Math.Min(6, enemy.Nation)], 1501 + d.Year, d.Month + 1, d.Day + 1);
            string loot = MsgOr(0x230, $"전리품으로서 금화 {spoils.Gold}닢, ", spoils.Gold)
                          + (spoils.Item != null ? MsgOr(0x231, $"그리고 {spoils.Item}을(를) ", spoils.Item) : "")
                          + MsgOr(0x232, "손에 넣었습니다.");
            Say(-1, loot.Trim());
            NoteBooty();
            TakePrizes(spoils.Prizes);
        }
        BattleEnded(battle.Result, spoils);
        if (spoils.Fame > 0) Note($" · 명성 +{spoils.Fame}");
        if (spoils.Stripped == Nations.LostNation) Say(0x238, "국적을 박탈당했습니다.");
        else if (spoils.Stripped == Nations.LostTitle) Say(0x239, "작위를 박탈당했습니다.");
    }

    /// <summary>
    /// 해전 끝 말 — 그림은 일몰 55, 나머지 54(<c>0x0040B9C0</c>), 섬멸은 53. 일몰 「전투는 승부를 가리지 못한 채 일몰을 맞았다.」(0x233), 제 기함 이탈 0x234, 적 기함 퇴각 0x235, 항복 0x236.
    /// 물러났거나 항복했으면 떠난 선장마다 「제독님, %s의 함대가 보이지 않는데요.」(0x317).
    /// </summary>
    private void BattleEnded(int result, SeaMapHost.Spoils spoils)
    {
        switch (result)
        {
            case Engine.Sea.Battle.Outcome.Sunset: SayWith(55, 0x233, "전투는 승부를 가리지 못한 채 일몰을 맞았다."); break;
            case Engine.Sea.Battle.Outcome.PlayerLeft: SayWith(54, 0x234, "기함의 이탈로 인해 전투는 종말로 치달았다."); break;
            case Engine.Sea.Battle.Outcome.EnemyLeft: SayWith(54, 0x235, "적기함의 퇴각으로 인해 전투는 종결되었다."); break;
            case Engine.Sea.Battle.Outcome.Surrendered: SayWith(54, 0x236, "기함의 항복으로 인해 적함대는 활을 거두었다."); break;
        }
        foreach (var who in spoils.Deserted ?? []) Say(0x317, $"제독님, {who}의 함대가 보이지 않는데요.", who);
    }

    /// <summary>떠나는 동료 말(0x313 ~ 0x316) — 첫 줄 「§%s§」 는 말하는 이 자리라 뺀다.</summary>
    private string Deserter(int id)
    {
        string text = MsgOr(id, "제독님, 상대가 너무 나쁩니다.\n나는 더이상 함께 싸울 수 없습니다.").Replace("\\n", "\n");
        int cut = text.IndexOf('\n');
        return text.StartsWith("§") && cut > 0 ? text[(cut + 1)..] : text;
    }

    /// <summary>거둔 보급 · 짐을 알림 줄에 적는다.</summary>
    private void NoteBooty()
    {
        var b = _host.Booty;
        if (b.Water + b.Food + b.Lumber + b.Shot + b.Amount == 0) return;
        string goods = b.Good >= 0 && _host.Economy is { } eco && b.Good < eco.GoodsNames.Count ? $" · {eco.GoodsNames[b.Good]} {b.Amount}" : "";
        Note($" · 거둔 것: 물 {b.Water} · 식량 {b.Food} · 자재 {b.Lumber} · 탄약 {b.Shot}{goods}");
        ShareBooty();
    }

    /// <summary>
    /// 노획 짐 나누기(<c>0x004477C0</c>) — 배를 고르고(취소하면 남은 것이 있으면 「아직 적하가 남아 있습니다만, 좋습니까？」 0xD3, 없으면 0xDB)
    /// 그 배에 실을 것을 고른 뒤(<c>0x00447000</c>) 양을 넣는다(<c>0x00447570</c>). 닫으면 남은 것은 버린다.
    /// </summary>
    private void ShareBooty()
    {
        var left = _host.BootyLeft;
        string goodName = _host.Booty.Good >= 0 && _host.Economy is { } eco && _host.Booty.Good < eco.GoodsNames.Count ? eco.GoodsNames[_host.Booty.Good] : "교역품";
        string[] kinds = ["물", "식량", "자재", "탄약", goodName];
        while (left.Any(x => x > 0))
        {
            var ships = _host.Player.Ships;
            int pick = BuildingMenu.Ask(this, "노획품 — 어느 배에 " + string.Join(" · ", kinds.Select((k, i) => left[i] > 0 ? $"{k} {left[i]}" : null).OfType<string>()),
                [.. ships.Select(s => $"{s.Name}  빈 자리 {s.Free}")]);
            if (pick < 0)
            {
                if (MessageBox.Show(this, MsgOr(0xD3, "아직 적하가 남아 있습니다만, 좋습니까？"), "노획품", MessageBoxButton.YesNo) == MessageBoxResult.Yes) break;
                continue;
            }
            while (true)
            {
                var have = Enumerable.Range(0, 5).Where(k => left[k] > 0).ToList();
                if (have.Count == 0) break;
                int k = BuildingMenu.Ask(this, $"{ships[pick].Name} — 빈 자리 {ships[pick].Free}",
                    [.. have.Select(i => $"{kinds[i]}  {left[i]}  (실을 수 있는 것 {_host.BootyRoom(pick, i)})")]);
                if (k < 0) break;
                int kind = have[k], room = _host.BootyRoom(pick, kind);
                if (room <= 0) { Note($" · {ships[pick].Name}에는 더 실을 수 없습니다"); continue; }
                int amount = AmountDialog.Ask(this, $"{kinds[kind]} — 얼마나 싣겠습니까", room);
                if (amount > 0) _host.LoadBooty(pick, kind, amount);
            }
        }
        Array.Clear(left);
    }

    /// <summary>
    /// 남이 덮쳤다(<c>0x00410DE0</c>) — 해적이면 0x331, 주인공을 노리던 함대면 0x332, 그 밖은 0x333. 부관은 덮친 편 세기 x 3/2 가
    /// 맞는 편보다 작으면 0x334, 아니면 0x335 라고 한다(원본 셈 그대로 — 약한 쪽이 덮칠 때 「위험하다」고 한다).
    /// 목록 0x36: 교전 · 도망(rand(150) &lt; 운 + 항해 레벨 이면 0x336, 아니면 0x338 뒤 싸운다) · 항복(<see cref="Encounter.Surrender"/>).
    /// </summary>
    private void Defend(Engine.Sea.NpcFleet enemy)
    {
        try
        {
            if (_host.Fleet is not { } f || _host.StartTables is not { } st) return;
            var hp = st.People[Math.Clamp(_host.HeroPersonNumber, 0, st.People.Count - 1)];
            if (enemy.Pirate) Say(0x331, "목숨이 아깝거든, 돈과 적하를 놓고 썩 꺼져！");
            else if (enemy.Mode is 5 or 6) Say(0x332, $"{hp.Name} {hp.Family}, 찾고 있었다！", $"{hp.Name} {hp.Family}");
            else Say(0x333, $"우리 나라의 원수 {NationNames7[Math.Min(6, _host.HeroNation)]}의 함대여！", NationNames7[Math.Min(6, _host.HeroNation)]);
            int ours = (_host.Player.Ships.Sum(s => s.Hull) + 4) / 5, theirs = (enemy.Slots.Sum(s => s.Durability) + 4) / 5;
            if (theirs * 3 / 2 < ours) Say(0x334, "제독님, 좀 위험한 상황이군요.");
            else Say(0x335, "저까짓 놈들 반격합시다.");
            int pick = BuildingMenu.Ask(this, enemy.Pirate ? "해적" : "만남", ["교  전", "도  망", "항  복"]);
            if (pick == 1)
            {
                int luck = _host.BattleCaptain(_host.HeroPersonNumber).Stat(7), nav = _host.BattleCaptain(_host.HeroPersonNumber).NavLevel;
                if (_host.Roll(150) < luck + nav) { Say(0x336, "간신히 도망쳐 나왔군요."); return; }
                Say(0x338, "제독님, 도망칠 수 없습니다.");
            }
            else if (pick == 2)
            {
                if (!enemy.Pirate) Say(0x339, "항복하다니 현명하군. 그러면 우리 나라의 법률에 의해, 공의 소지금과 적하 ３분의 ２를 몰수하겠다.");
                else Say(0x33A, "으엑！ 형편없는 수확물이다.");
                int after = Engine.Sea.Encounter.Surrender(_host.Player, enemy.Pirate);
                if (after >= 0) Say(after, "");
                return;
            }
            string admiral = _host.PersonName(enemy.Admiral);
            var battle = _host.OpenBattle(enemy, heroAttacks: false);
            f.UnderSail = false;
            foreach (var (who, line) in _host.BattleDeserters)
                ScenarioDialog.Play(this, _host.Faces, [new ScenarioVm.Line(who, Deserter(line))]);
            BattleWindow.Run(this, battle, _host.Messages, $"해전 — {admiral} 함대", _host.DuelFighter, UseItem);
            f.SetTimeOfDay(battle.Time);
            var spoils = _host.CloseBattle(battle, enemy);
            if (battle.Result is Engine.Sea.Battle.Outcome.EnemySunk or Engine.Sea.Battle.Outcome.DuelWon)
            {
                Say(-1, (MsgOr(0x230, $"전리품으로서 금화 {spoils.Gold}닢, ", spoils.Gold)
                         + (spoils.Item != null ? MsgOr(0x231, $"그리고 {spoils.Item}을(를) ", spoils.Item) : "")
                         + MsgOr(0x232, "손에 넣었습니다.")).Trim());
                NoteBooty();
                TakePrizes(spoils.Prizes);
            }
            BattleEnded(battle.Result, spoils);
            if (spoils.Fame > 0) Note($" · 명성 +{spoils.Fame}");
            if (spoils.Stripped == Nations.LostNation) Say(0x238, "국적을 박탈당했습니다.");
            else if (spoils.Stripped == Nations.LostTitle) Say(0x239, "작위를 박탈당했습니다.");
        }
        finally { _host.AmbushDone(enemy); }
    }

    /// <summary>
    /// 나포(<c>0x0040B320</c>) — 맡을 사람 수 · 남은 배 칸(열 − 척 수) · 후보 가운데 작은 만큼, 0x22E 뒤 배를 고르고 이름(0x22D)을 붙인다.
    /// 고르지 않으면 0x22F 로 그만둘지 묻는다.
    /// </summary>
    private void TakePrizes(List<Engine.Sea.Battle.Ship> prizes)
    {
        var captains = _host.PrizeCaptains();
        int room = Math.Min(Math.Min(captains.Count, prizes.Count), SeaMapHost.MaxShips - _host.Player.Ships.Count);
        while (room > 0 && prizes.Count > 0 && captains.Count > 0)
        {
            int pick = BuildingMenu.Ask(this, MsgOr(0x22E, $"적 선박을 {prizes.Count}척 나포했습니다.", prizes.Count, room).Replace("\n", " "),
                                        [.. prizes.Select(p => $"{p.Name} 선원 {p.Crew} 내구 {p.Hull}/{p.MaxHull}")]);
            if (pick < 0)
            {
                if (MessageBox.Show(this, MsgOr(0x22F, "이것으로 끝내도 좋겠습니까？"), "나포", MessageBoxButton.YesNo) == MessageBoxResult.Yes) return;
                continue;
            }
            var prize = prizes[pick];
            string name = TextDialog.Ask(this, MsgOr(0x22D, "배에 이름을 붙여 주십시오."), prize.Name, 7) ?? prize.Name;
            if (!_host.TakePrize(prize, captains[0], name)) return;
            Note($" · {name} 나포");
            captains.RemoveAt(0);
            prizes.RemoveAt(pick);
            room--;
        }
    }

    /// <summary>
    /// 「상륙」 — 바다 명령 목록 29(<c>0x0041AD60</c>). 함대 둘레에 뭍이 있어야 내린다(아니면 말 0x1B9).
    /// 곁에 이번 게임의 발견물이 있으면 「부락에 상륙하겠습니다」 뒤 목록 35, 없으면 목록 36 이다(<see cref="Discoveries"/>).
    /// </summary>
    /// <summary>상륙한 뭍이 사막 칩인지(<c>0x00458260</c> 이 0) — 물 「탐색」 앞에 묻는다.</summary>
    private bool _desertShore;
    private const byte DesertChip = 0x59;

    private void Land()
    {
        if (_host.Fleet is not { } f || _host.CurrentPort >= 0 || _host.Scenario is not { } vm) return;
        int fx = (int)Math.Floor(f.X), fy = (int)Math.Floor(f.Y);
        bool shore = false;
        for (int dy = -2; dy <= 3 && !shore; dy++)
            for (int dx = -2; dx <= 3 && !shore; dx++)
                if (!f.IsSea(fx + dx, fy + dy)) { shore = true; _desertShore = f.Chip(fx + dx, fy + dy) == DesertChip; }
        if (!shore) { Say(0x1B9, "그곳에는 상륙할 수 없습니다."); return; }

        var spot = Discoveries.Near(vm.Memory, fx, fy);
        var m = _host.Messages;
        string ask = (spot != null ? m?[0x1B7] ?? "부락에 " : "") + (m?[0x1B8] ?? "상륙하겠습니다.\n좋습니까？");
        if (MessageBox.Show(this, ask.Replace("\\n", "\n"), "상륙", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        f.UnderSail = false;
        if (spot is { } s) AtVillage(s.Index);
        else OnShore();
        f.UnderSail = true;
    }

    /// <summary>
    /// 「항이동」(<c>0x00448220</c>) — 측량술(+0x30 &amp; 0x10)을 가진 부선장(처지 3)이 있어야 한다(없으면 0x357, 여자 동료면 0x358).
    /// 들른 적 있는 항구(항구 표 +0x15 &amp; 0x10, <c>0x0043CE30</c>)를 골라 그리로 간다.
    /// </summary>
    private void SailToPort()
    {
        if (_host.Fleet is null || _host.CurrentPort >= 0 || _host.Scenario is not { } vm || _host.Ports is not { } pt) return;
        var m = vm.Memory;
        int mate = Navigators.InPost(m, 0);
        if (mate < 0 || (Navigators.Get(m, mate, SkillField) & 0x10) == 0)
        {
            Say(0x357, "항구이동엔, 측량기능을 가진 부선장이 필요합니다.");
            return;
        }
        var known = Enumerable.Range(0, pt.Ports.Count)
            .Where(p => (m.Read(ScenarioMemory.At(ScenarioMemory.PortTable, p, 0x15), false) & 0x10) != 0).ToList();
        if (known.Count == 0) { Note(" · 아는 항구가 없습니다"); return; }
        int pick = BuildingMenu.Ask(this, "항이동 — 어느 항구로", [.. known.Select(p => pt.Ports[p].Name)]);
        if (pick < 0) return;
        if (!_host.StartAutopilot(known[pick])) { Note(" · 그 항구로 가는 뱃길을 못 찾았습니다"); return; }
        Note($" · {pt.Ports[known[pick]].Name}(으)로 항이동을 시작합니다");
    }

    /// <summary>
    /// 상륙 「대기」(<c>0x0041B1B0</c>) — 뭍에 머물며 닻을 내린다(0x0045A1A8 |= 0x2000 과 0x800 · 0x1000). 날은 흐르고 물 · 식량은 반만 먹는다.
    /// 다시 「정박」 을 풀거나 키를 잡으면 배로 돌아간다.
    /// </summary>
    private void CampAshore()
    {
        if (_host.Fleet is not { } f) return;
        f.Anchored = true;
        f.UnderSail = true;
        _host.Player.Ashore = true;
        Note(" · 뭍에서 대기합니다 — 물 · 식량을 반만 먹습니다");
    }

    /// <summary>「정박」(<c>0x00445690</c>) — 닻을 내리고 올린다. 내린 동안은 서 있지만 날은 흐른다.</summary>
    private void ToggleAnchor()
    {
        if (_host.Fleet is not { } f || _host.CurrentPort >= 0) return;
        f.Anchored = !f.Anchored;
        if (!f.Anchored) _host.Player.Ashore = false;
        if (f.Anchored) f.UnderSail = true;
        Note(f.Anchored ? " · 정박합니다." : " · 이동을 시작합니다.");
        _host.Invalidate();
    }

    /// <summary>
    /// 바다에서 항구에 든다(<c>0x0041A8D0</c>) — 함대 둘레 열두 칸에 항구가 있으면 「%s항에 기항하겠습니다.」(0x16D)를 묻고, 그러면 기항 셈
    /// (<c>0x0041A560</c>, 항해 경험)을 하고 항구를 띄운다. <paramref name="port"/> 가 -1 이면 옆 항구를 찾는다.
    /// </summary>
    private void EnterPortFromSea(int port)
    {
        if (_host.Fleet is not { } f) return;
        if (_host.CurrentPort >= SeaMapHost.SmallPortFirst) { SmallPortMenu(); return; }
        if (_host.CurrentPort >= 0) return;
        if (port < 0) port = _host.PortBeside();
        if (port < 0) { Note(" · 바로 옆에 항구가 없습니다"); return; }
        string name = _host.Ports is { } pt && port < pt.Ports.Count ? pt.Ports[port].Name : $"#{port}";
        if (MessageBox.Show(this, MsgOr(0x16D, $"{name}항에 기항하겠습니다.", name), "입항", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            f.UnderSail = true;
            return;
        }
        var notes = _host.Arrive(port);
        if (port < SeaMapHost.SmallPortFirst) _portPick.SelectedIndex = port;
        Retitle();
        foreach (var line in notes) Say(-1, line);
        if (port >= SeaMapHost.SmallPortFirst) SmallPortMenu();
    }

    /// <summary>
    /// 「항」 건물에 들 때(<c>0x00423CB0</c> — 건물 손표 <c>0x00459BF8</c> 의 넷째) — 남고 싶은 동료를 묻고(<c>0x00423DC1</c>) 왕이 찾는다는 소문(<c>0x00423F1A</c>).
    /// </summary>
    private void AtHarbor()
    {
        int port = _host.CurrentPort;
        string name = _host.SceneName;
        foreach (var (mate, wage) in _host.MatesWantingToStay())
        {
            string who = _host.PersonName(mate);
            Say(0x31E, "제독님, 나는 이 항구에 남고 싶습니다만.");
            // 말을 거드는 동료(0x00436870) 자신이 남겠다면 「제독님, 나를 붙들 생각입니까？」(0x320, 0x00423E61).
            string ask = _host.Scenario is { } vm && Navigators.FirstMate(vm.Memory) == mate
                ? MsgOr(0x320, "제독님, 나를 붙들 생각입니까？")
                : MsgOr(0x31F, $"제독님, {who}(를) 붙들겠습니까？", who);
            bool keep = MessageBox.Show(this, ask, who, MessageBoxButton.YesNo) == MessageBoxResult.Yes
                        && MessageBox.Show(this, MsgOr(0x321, $"그럼, 급료를 금화 {wage + 10}닢으로 올려 주십시오.", wage + 10), who, MessageBoxButton.YesNo) == MessageBoxResult.Yes;
            if (keep) _host.KeepMate(mate);
            else
            {
                _host.MateStays(mate);
                Note($" · {Josa(who, "이", "가")} {name}에 남았습니다");
            }
        }
        // 소문이 없었고 두 변수 판의 0x3F(0x0049F3DE · 0x0049F56E)가 다 0 이면 「여어, 출항하시는 겁니까？」(0x38, 0x00423FB9).
        if (!RoyalSummons(port) && (_host.Common?.GetVar(0x3F) ?? 0) == 0 && (_host.Scenario?.GetVar(0x3F) ?? 0) == 0)
            Say(0x38, "여어, 출항하시는 겁니까？");
    }

    /// <summary>나라마다 「~왕이 」(0x19A + 나라).</summary>
    private string KingCalls(int nation) =>
        MsgOr(0x19A + Math.Clamp(nation, 0, 5), "왕이 ");

    /// <summary>
    /// 「항」 건물 소문(<c>0x00423F1A</c>) — SNR0 일이 없고 깃발 16 · 17 가운데 하나가 섰으며 18 이 아니고 제 나라 항구면
    /// 「그리고 보니, ~왕이 당신을 찾고 있다는 소문이 있소.」(0x199 · 0x19A + 나라 · 0x1A0), 그리고 깃발 17 을 세운다.
    /// </summary>
    private bool RoyalSummons(int port)
    {
        if (_host.Common is not { } c || c.Chapter != 0 || port >= SeaMapHost.SmallPortFirst) return false;
        if (!(c.GetFlag(16) || c.GetFlag(17)) || c.GetFlag(18)) return false;
        int nation = _host.HeroNation;
        if (nation >= 6 || _host.PortNation(port) != nation) return false;
        Say(-1, MsgOr(0x199, "그리고 보니, ") + KingCalls(nation) + MsgOr(0x1A0, "당신을 찾고 있다는 소문이 있소."));
        c.SetFlag(17, true);
        return true;
    }

    /// <summary>
    /// 작은 항구(100 부터) — 목록 57(출항 · 보급 · 명명, 손표 <c>0x004589A0</c>)만 뜬다. 닫으면 「입항」 단추로 다시 연다.
    /// </summary>
    private void SmallPortMenu()
    {
        string[] items = _host.Menus?.Lists is { Count: > 57 } l ? [.. l[57]] : ["출  항", "보  급", "명  명"];
        while (_host.CurrentPort >= SeaMapHost.SmallPortFirst)
        {
            int pick = BuildingMenu.Ask(this, _host.SceneName, items);
            if (pick < 0) return;
            if (pick == 0) { PutToSea(); return; }
            if (pick == 1) ShowSupplies();
            else NamePort();
        }
    }

    /// <summary>
    /// 「명명」(<c>0x00423B90</c>) — 「이 항구에 애칭을 붙여 주는 거요？」(0x39E), 여덟 바이트까지. 같은 이름이 있으면 0x39F, 붙이면 「%s항」(0x3A0).
    /// </summary>
    private void NamePort()
    {
        int port = _host.CurrentPort;
        string? name = TextDialog.Ask(this, MsgOr(0x39E, "이 항구에 애칭을 붙여 주는 거요？"), _host.SceneName, 4);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (!_host.RenamePort(port, name)) { Say(0x39F, "앗！ 같은 이름의 항구가 있는데？ 다른 이름을 붙여 주시오."); return; }
        Say(0x3A0, $"{name}항", name);
        Retitle();
    }

    /// <summary>
    /// 사분의 · 육분의 · 경위의(<c>0x0041FC50</c>) — 항구에서는 「경도·위도의 계측은 배 위에서 해 주세요.」(0x2F4). 주인공이 측량술(+0x30 &amp; 0x10)이 없으면
    /// 「측량술을 가지고 있는 동료를 선택해 주세요.」(0x2F5) — 고른 이가 없으면 0x2F3. 끝내 없으면 주인공이 어림한다(0x2F6 · 0x2F8, 여자면 0x2F7 · 0x2F9 —
    /// 사람 +0x31 &amp; 0x10)이고 값에 rand(100) + 1 이 덧붙는다. 경도 = ((x + 0x7BD) mod 2160, 1080 넘으면 서경 2160 − 그것) / (6 x 힘) x 힘,
    /// 위도 = |640 − y| x 8 / (57 x 힘) x 힘(아이템 +0x12 가 눈금). 「북위 %d도, 」(0x7E · 0x7F) 「동경 %d도」(0x80 · 0x81).
    /// </summary>
    private string? MeasurePosition(ItemShop.Item item, Window owner)
    {
        if (_host.Fleet is not { } f || _host.Scenario is not { } vm) return null;
        if (_host.CurrentPort >= 0) { Say(0x2F4, "경도·위도의 계측은 배 위에서 해 주세요."); return null; }
        var m = vm.Memory;
        const int Surveying = 0x10;
        int error = 0;
        if ((HeroField(SkillField) & Surveying) == 0)
        {
            Say(0x2F5, "측량술을 가지고 있는 동료를 선택해 주세요.");
            var mates = Navigators.Mates(m).Select(t => t.Person).ToList();
            bool found = false;
            while (mates.Count > 0)
            {
                int pick = BuildingMenu.Ask(owner, "측량", [.. mates.Select(_host.PersonName)]);
                if (pick < 0) break;
                if ((Navigators.Get(m, mates[pick], SkillField) & Surveying) != 0) { found = true; break; }
                Say(0x2F3, "그는 측량술을 가지고 있지 않습니다.");
            }
            if (!found)
            {
                bool woman = (Navigators.Get(m, _host.HeroPersonNumber, 0x31) & 0x10) != 0;
                Say(woman ? 0x2F6 : 0x2F7, woman ? "할 수 없군요." : "할 수 없군.");
                Say(woman ? 0x2F8 : 0x2F9, woman ? "뭐, 이 정도랄까？" : "뭐, 이 정도겠지？");
                error = _host.Roll(100) + 1;
            }
        }
        int step = Math.Max(1, item.Power);
        int x = ((int)f.X + 0x7BD) % ChipMap.Width, y = 640 - (int)f.Y;
        bool west = x > ChipMap.Width / 2;
        if (west) x = ChipMap.Width - x;
        int lon = x / (step * 6) * step, lat = Math.Abs(y) * 8 / (step * 57) * step;
        return MsgOr(y >= 0 ? 0x7E : 0x7F, $"{(y >= 0 ? "북위" : "남위")} {lat + error}도, ", lat + error)
               + MsgOr(west ? 0x81 : 0x80, $"{(west ? "서경" : "동경")} {lon + error}도", lon + error);
    }

    /// <summary>
    /// 「아이템」 — 가진 것을 골라 목록 33(장비 · 사용, <c>0x0041F630</c>). 쓰기는 갈래마다 다르다(<c>0x0041F8F0</c>):
    /// 7 사분의 · 육분의 · 경위의는 지금 자리(경위도), 8 의 회중시계는 시각, 9 의 쥐약 · 성스런 향유 · 라임 주스는 쓰고 없어진다.
    /// </summary>
    private void UseItem(Window? owner = null)
    {
        var w = owner ?? this;
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        var owned = ItemShop.Owned(m);
        if (owned.Count == 0) { Note(" · 가진 아이템이 없습니다"); return; }
        int pick = BuildingMenu.Ask(w, "아이템", owned.Select(i =>
            ItemShop.Get(m, i).Name + (ItemShop.IsEquipped(m, i) ? " (장비)" : "")).ToList());
        if (pick < 0) return;
        int item = owned[pick];
        var it = ItemShop.Get(m, item);
        ShowItem(item, w);
        int how = BuildingMenu.Ask(w, it.Name, ["장  비", "사  용"]);
        if (how < 0) return;
        if (how == 0)
        {
            Note(ItemShop.ToggleEquip(m, item) switch
            {
                true => " · " + MsgOr(0x444, "아이템을 장비하겠습니다."),
                false => " · 아이템을 벗습니다",
                null => " · " + MsgOr(0x442, "이 아이템은 장비할 수 없습니다."),
            });
            return;
        }
        var p = _host.Player;
        var f = _host.Fleet;
        string said;
        bool usedUp = false;
        switch (it.Group)
        {
            case 7 when f != null:
            {
                said = MeasurePosition(it, w);
                if (said == null) return;
                break;
            }
            case 8 when item == 0x17 && f != null: said = $"지금은 {Fleet.Clock(f.TimeOfDay)}"; break;
            case 9 when item == 0x29:
                said = p.Rats ? MsgOr(0x44F, "쥐떼가 사라졌다. . .") : MsgOr(0x450, "별다른 일은 없었던 것 같다.");
                p.Rats = false;
                usedUp = true;
                break;
            case 9 when item == 0x2A:
                said = f?.Weather.Calm(_host.Roll) == true
                    ? (f.TimeOfDay is >= 12 and < 60 ? MsgOr(0x452, "폭풍이 그쳤다. . . 지금은 낮이다.") : MsgOr(0x451, "폭풍이 그쳤다. . . 지금은 밤이다."))
                    : MsgOr(0x453, "별다른 일은 없었던 것 같다.");
                usedUp = true;
                break;
            case 9 when item == 0x2B:
                said = p.Scurvy ? MsgOr(0x454, "괴혈병이 나았다. . .") : MsgOr(0x455, "맛있다.");
                p.Scurvy = false;
                usedUp = true;
                break;
            case 12 when Discoveries.MapTarget(m, item) is >= 0 and var target:
            {
                // 보물 지도(0x0041FE60 → 0x00443FE0) — 원본은 그 자리의 지도를 그린다. 우리는 5도 단위 경위도로 일러 준다.
                var (east, lon, north, lat) = Discoveries.Degrees(Discoveries.Get(m, target));
                said = $"{(east ? "동경" : "서경")} {lon}도 · {(north ? "북위" : "남위")} {lat}도 언저리를 가리키고 있다";
                break;
            }
            case var _ when item == 0x19: said = MsgOr(0x44E, "야옹∼"); break;            // 고양이
            default: said = MsgOr(0x450, "별다른 일은 없었던 것 같다."); break;
        }
        if (usedUp) { int none = 0; ItemShop.Sell(m, item, 0, ref none); }
        ScenarioDialog.Play(w, _host.Faces, [new ScenarioVm.Line(it.Name, said)]);
    }

    /// <summary>아이템 하나의 풀이(<c>ITEM.MES</c>)와 그림(KAO 발견물 판 0x55 + 아이템 +0x0F, <c>0x0041F4F4</c>).</summary>
    private void ShowItem(int item, Window? owner = null)
    {
        var w = owner ?? this;
        if (_host.Scenario is not { } vm || _host.ItemNotes?[item] is not { Length: > 0 } note) return;
        int pic = vm.Memory.Read(ScenarioMemory.At(ItemShop.Table, item, 0x0F), false);
        ScenarioDialog.Play(w, _host.Faces,
            [new ScenarioVm.Line(ItemShop.Get(vm.Memory, item).Name, note, Face: pic > 0 ? FaceSheet.FindFirst + 0x55 + pic : -1)]);
    }

    /// <summary>탐색 덤(<c>0x0049A164</c>) — 부락에 내릴 때마다 0 에서 시작하고 「환대」 가 5 씩 올린다.</summary>
    private int _searchBonus;

    /// <summary>부락 — 목록 35(보물탐색 · 환대 · 식량약탈 · 출항 · 대기, <c>0x0041BA90</c>).</summary>
    private void AtVillage(int index)
    {
        _searchBonus = 0;
        string[] items = _host.Menus?.Lists is { Count: > 35 } l35 ? [.. l35[35]] : ["보물탐색", "환  대", "식량약탈", "출  항", "대  기"];
        while (true)
        {
            int pick = BuildingMenu.Ask(this, "부락", items);
            switch (pick)
            {
                case 0: SearchVillage(index); break;
                case 1: Feast(); break;
                case 2: Loot(); break;
                case 3 when ConfirmSail(): Note(" · 배로 돌아왔습니다"); return;
                case 4: CampAshore(); return;
            }
        }
    }

    /// <summary>뭍 — 목록 36(출항 · 응급수리 · 탐색 · 대기, <c>0x0041B090</c>).</summary>
    private void OnShore()
    {
        string[] items = _host.Menus?.Lists is { Count: > 36 } l36 ? [.. l36[36]] : ["출  항", "응급수리", "탐  색", "대  기"];
        while (true)
        {
            int pick = BuildingMenu.Ask(this, "상륙", items);
            if (pick == 1) PatchShip();
            else if (pick == 2) { if (!DigTreasure()) SearchWater(); }
            else if (pick == 3) { CampAshore(); return; }
            else if (pick == 0 && ConfirmSail()) { Note(" · 배로 돌아왔습니다"); return; }
        }
    }

    /// <summary>
    /// 뭍 · 부락의 「출항」(<c>0x0041B210</c>) — 부선장이 「출항하겠습니다.」(0x1A1)라고 다짐받는다. 아니라면 목록이 다시 뜬다.
    /// 원본은 목록을 물려도(오른쪽 단추) 다시 뜬다 — 뭍에서 나가는 길은 출항 · 대기뿐이다.
    /// </summary>
    private bool ConfirmSail() =>
        MessageBox.Show(this, MsgOr(0x1A1, "출항하겠습니다."), "출항", MessageBoxButton.YesNo) == MessageBoxResult.Yes;

    /// <summary>
    /// 「응급수리」(<c>0x0041B240</c>) — 배를 고르고 내구 · 역풍 · 돛 가운데 하나를 자재로 고친다. 상한은 선종 값이다.
    /// 다 멀쩡하면 0x1A2, 자재가 없으면 0x1A3, 그 곳이 멀쩡하면 0x1A4.
    /// </summary>
    private void PatchShip()
    {
        var p = _host.Player;
        var types = _host.StartTables?.ShipTypes;
        (int Upwind, int Sail) Max(PlayerState.Ship s) =>
            types != null && s.Type < types.Count ? (types[s.Type].Upwind, types[s.Type].Sail) : (s.Upwind, s.Sail);
        int pick = BuildingMenu.Ask(this, "응급수리 — 배를 고르세요",
            p.Ships.Select(s => $"{s.Name}  내구 {s.Hull}/{s.MaxHull} · 역풍 {s.Upwind}/{Max(s).Upwind} · 돛 {s.Sail}/{Max(s).Sail}").ToList());
        if (pick < 0) return;
        var ship = p.Ships[pick];
        var (mu, ms) = Max(ship);
        if (ship.Hull >= ship.MaxHull && ship.Upwind >= mu && ship.Sail >= ms) { Say(0x1A2, "이 배는 부서지지 않았습니다만."); return; }
        if (p.Materials == 0) { Say(0x1A3, "자재가 없습니다."); return; }
        int part = BuildingMenu.Ask(this, $"응급수리 — 자재 {p.Materials}", ["내구", "역풍", "돛"]);
        if (part < 0) return;
        int need = part switch
        {
            0 => PlayerState.PatchNeed(ship.Hull, ship.MaxHull),
            1 => PlayerState.PatchNeed(ship.Upwind, mu),
            _ => PlayerState.PatchNeed(ship.Sail, ms),
        };
        if (need == 0) { Say(0x1A4, "수리할 필요가 없습니다."); return; }
        int most = Math.Min(need, p.Materials);
        string ask = _host.Messages is { } m && m[0x1A5].Length > 0 ? m.Format(0x1A5, most) : $"자재를 얼마나 사용하겠습니까？(0-{most})";
        int used = AmountDialog.Ask(this, ask, most);
        if (used <= 0) return;
        p.Patch(ship, part, used, mu, ms);
        _host.SyncLoad();
        Note($" · 자재 {used}로 고쳤습니다 — 내구 {ship.Hull}/{ship.MaxHull} · 역풍 {ship.Upwind} · 돛 {ship.Sail}");
    }

    private int HeroField(int field) =>
        _host.Scenario is { } vm ? Navigators.Get(vm.Memory, _host.HeroPersonNumber, field) : 0;

    /// <summary>식량을 <paramref name="amount"/> 만큼 덜어 낸다 — 앞 배부터. 모자라면 아무것도 안 하고 거짓.</summary>
    private bool TakeFood(int amount)
    {
        var ships = _host.Player.Ships;
        if (ships.Sum(s => s.Supplies[PlayerState.Food]) < amount) return false;
        foreach (var s in ships)
        {
            int k = Math.Min(amount, s.Supplies[PlayerState.Food]);
            s.Supplies[PlayerState.Food] -= k;
            amount -= k;
        }
        _host.SyncLoad();
        return true;
    }

    /// <summary>땅에서 싸운다 — 배마다 선원 x 세기 / 100 이 죽는다(<c>0x0041ACE0</c>, 말 0x1AB).</summary>
    private string LandFight()
    {
        int strength = Discoveries.FightStrength(HeroField(Navigators.Stats + 5), HeroField(Navigators.BattleLevel));
        int dead = 0;
        foreach (var s in _host.Player.Ships)
        {
            int k = s.Crew * strength / 100;
            s.Crew -= k;
            dead += k;
        }
        return M(0x1AB, $"선원 중에서 {dead}명의 사상자가 났습니다.", dead)[3..];
    }

    /// <summary>「보물탐색」(<c>0x0041BB50</c>).</summary>
    private void SearchVillage(int index)
    {
        if (_host.Scenario is not { } vm) return;
        var p = _host.Player;
        var r = Discoveries.Search(vm.Memory, index, HeroField(Church.Luck), HeroField(Navigators.Stats + 3), _searchBonus,
                                   p.Ships.Any(s => s.Supplies[PlayerState.Food] > 0));
        var spot = Discoveries.Get(vm.Memory, index);
        string kind = Discoveries.KindNames[spot.Kind];
        switch (r)
        {
            case Discoveries.Outcome.NoFood: Say(0x1AC, "식량이 없습니다. 수색을 중단합시다."); return;
            case Discoveries.Outcome.AlreadyFound: Say(0x1B0, "색다른 물건은 하나도 없습니다."); return;
            case Discoveries.Outcome.Nothing: Say(0x1AD, "아무것도 발견되지 않았습니다."); return;
        }
        string head = r == Discoveries.Outcome.Attacked
            ? M(0x1AE, $"{kind}에게 습격당했습니다.", kind)[3..] + " " + LandFight()
            : M(0x1AF, $"{kind}(를) 발견했습니다.", kind)[3..];
        string name = _host.Colony?.Name(index) ?? kind;
        string text = _host.Colony?.Text(index) ?? "";
        ScenarioDialog.Play(this, _host.Faces,
        [
            new ScenarioVm.Line("", head),
            new ScenarioVm.Line(name, text.Length > 0 ? text : name,
                                Face: _host.Colony?.Picture(index) is >= 0 and var pic ? FaceSheet.FindFirst + pic : -1),
        ]);
        Note($" · 「{name}」을(를) 찾았습니다");
    }

    /// <summary>「환대」(<c>0x0041BD40</c>) — 식량 20 을 내주고 탐색 덤 +5.</summary>
    private void Feast()
    {
        if (MessageBox.Show(this, _host.Messages?[0x1B1] ?? "현지인을 대접하겠습니까？", "환대", MessageBoxButton.YesNo)
            != MessageBoxResult.Yes) return;
        if (!TakeFood(Discoveries.FeastFood)) { Say(0x1B3, "나누어 줄 정도의 식량은 없습니다."); return; }
        _searchBonus += Discoveries.FeastBonus;
        Say(0x1B2, $"우호도가 {_searchBonus}(가) 되었습니다.", _searchBonus);
    }

    /// <summary>
    /// 「식량약탈」(<c>0x0041BE00</c>) — (rand(5) + 2) x 10 을 싣고, 넷에 하나는 반격을 받는다. 주인공 +0x22 · 운이 2 까지 떨어지고
    /// 탐색 덤은 0 으로 돌아간다.
    /// </summary>
    private void Loot()
    {
        if (MessageBox.Show(this, _host.Messages?[0x1B4] ?? "현지인으로부터 약탈하겠습니까？", "식량약탈", MessageBoxButton.YesNo)
            != MessageBoxResult.Yes) return;
        if (_host.Scenario is not { } vm) return;
        int amount = Discoveries.LootAmount(Random.Shared.Next);
        string text = M(0x1B5, $"{amount}명분의 식량을 약탈했습니다.", amount)[3..];
        if (Random.Shared.Next(4) == 0) text += "\n" + (_host.Messages?[0x1B6] ?? "원주민에게 반격을 받고, ") + LandFight();
        int hero = _host.HeroPersonNumber;
        int charm = Navigators.Get(vm.Memory, hero, Navigators.Stats + 6);
        Navigators.Set(vm.Memory, hero, Navigators.Stats + 6, charm - Math.Clamp(charm - 50, 0, 2));
        int luck = Navigators.Get(vm.Memory, hero, Church.Luck);
        Navigators.Set(vm.Memory, hero, Church.Luck, luck - Math.Min(2, luck));
        _searchBonus = 0;
        _host.Player.BuySupply(PlayerState.Food, amount / PlayerState.Lot, 0);
        _host.SyncLoad();
        ScenarioDialog.Play(this, _host.Faces, [new ScenarioVm.Line("", text)]);
    }

    /// <summary>뭍 「탐색」 — 가진 보물 지도의 자리면 보물을 캔다(말 0x1AF, <see cref="Discoveries.Dig"/>). 캤으면 참.</summary>
    private bool DigTreasure()
    {
        if (_host.Scenario is not { } vm || _host.Fleet is not { } f) return false;
        int spot = Discoveries.TreasureNear(vm.Memory, (int)f.X, (int)f.Y, ItemShop.Owned(vm.Memory));
        if (spot < 0) return false;
        int treasure = Discoveries.Dig(vm.Memory, spot);
        string name = ItemShop.Get(vm.Memory, treasure).Name;
        SayWith(64, 0x1AF, $"{name}(를) 발견했습니다.", name);
        ShowItem(treasure);
        return true;
    }

    /// <summary>
    /// 뭍 「탐색」 — 물을 찾는다(<c>0x0041B8B0</c>). rand(100) &lt; 운이면 찾고(0x1AA) 배마다 빈 자리만큼 물을 채운다.
    /// </summary>
    private void SearchWater()
    {
        // 사막(칩 0x59)에 내렸으면(0x0041AF8D) 「이런 곳에 물이 있을 것 같지는 않습니다만, 일단 찾아보겠습니까？」(0x1A8, 0x0041B8BE)
        if (_desertShore && MessageBox.Show(this, MsgOr(0x1A8, "이런 곳에 물이 있을 것 같지는 않습니다만, 일단 찾아보겠습니까？"), "탐색",
                                            MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (Random.Shared.Next(100) >= HeroField(Church.Luck)) { Say(0x1A9, "물은 발견되지 않았습니다."); return; }
        Say(0x1AA, "깨끗한 물을 발견했습니다.");
        // 배를 골라(목록 0x8B) 「%s호에 물을 얼마나 싣겠습니까？(0-%d)」(0x1A7) — 더 못 실으면 0x1A6(0x0041B950). 물은 열 통이 짐 하나다.
        var ships = _host.Player.Ships;
        int filled = 0;
        while (true)
        {
            int pick = BuildingMenu.Ask(this, "물 — 어느 배에", [.. ships.Select(s => $"{s.Name}  물 {s.Supplies[PlayerState.Water]} · 빈 자리 {s.Free}")]);
            if (pick < 0) break;
            var ship = ships[pick];
            int most = ship.Free * PlayerState.Lot;
            if (most <= 0) { Say(0x1A6, "더이상 실을 수 없습니다."); continue; }
            int k = AmountDialog.Ask(this, MsgOr(0x1A7, $"{ship.Name}호에 물을 얼마나 싣겠습니까？(0-{most})", ship.Name, most), most);
            if (k <= 0) continue;
            ship.Supplies[PlayerState.Water] += k;
            filled += k;
            _host.SyncLoad();
        }
        if (filled > 0) Note($" · 물 {filled}통을 실었습니다");
    }

    /// <summary>
    /// 「승무원편성」 — 목록 27 의 셋째(<c>0x00446310</c>). 배를 골라 선원 수를 정하면 남는 이는 공용으로 모이고, 모자라면 공용에서 태운다.
    /// 끝낼 때 공용에 남은 이가 있으면 0xE7 로 묻고, 예면 내린다.
    /// </summary>
    private void RedistributeCrew()
    {
        var p = _host.Player;
        if (p.Ships.Count <= 1) { Say(0xD9, "함선이 １척밖에 없습니다."); return; }
        int pool = 0, morale = PlayerState.DefaultMorale;
        while (true)
        {
            int pick = BuildingMenu.Ask(this, $"승무원편성 — 남은 선원 {pool}",
                p.Ships.Select(s => $"{s.Name}  선원 {s.Crew}/{s.MaxCrew} · 사기 {s.Morale}").ToList());
            if (pick < 0)
            {
                // 남은 이가 있으면 0xE7 「선원이 남아 있습니다. 좋습니까？」 — 예면 내린다.
                if (pool == 0 || MessageBox.Show(this, MsgOr(0xE7, "선원이 남아 있습니다. 좋습니까？"), "승무원편성", MessageBoxButton.YesNo) == MessageBoxResult.Yes) break;
                continue;
            }
            var ship = p.Ships[pick];
            int most = Math.Min(ship.MaxCrew, ship.Crew + pool);
            string ask = _host.Messages is { } m && m[0xE6].Length > 0 ? m.Format(0xE6, ship.Name, most) : $"{ship.Name} 선원 수(0-{most})";
            int crew = AmountDialog.Ask(this, ask, most);
            if (crew < 0) continue;
            (pool, morale) = p.Recrew(ship, crew, pool, morale);
        }
        _host.SyncLoad();
        Note(pool > 0 ? $" · 선원 {pool}명을 내렸습니다" : " · 승무원을 다시 짰습니다");
    }

    /// <summary>「편성」 — 목록 27(선장변경 · 인사변경 · 승무원편성 · 승무원비율 · 함선처분 · 적하편성, <c>0x00445372</c>) 가운데 된 것.</summary>
    private void Organize()
    {
        int pick = BuildingMenu.Ask(this, "편성", ["선장변경", "승무원편성", "함선처분", "적하편성", "인사변경", "승무원비율"]);
        if (pick == 4) { AppointMates(); return; }
        if (pick == 5) { SetCrewShares(); return; }
        if (pick == 0) ChangeCaptain();
        else if (pick == 1) RedistributeCrew();
        else if (pick == 2) ScuttleShip();
        else if (pick == 3) MoveCargo();
    }

    /// <summary>
    /// 「승무원비율」(<c>0x00446590</c>) — 배마다 선원을 운항 · 파수 · 갑판으로 나눈다(말 0x379)(합 100%). 원본은 단추로 한 칸씩 옮기는 창이고,
    /// 우리는 운항 몫과 파수 몫을 차례로 묻는다(<see cref="PlayerState.Ship.SetShares"/>).
    /// </summary>
    private void SetCrewShares()
    {
        var p = _host.Player;
        int which = BuildingMenu.Ask(this, "승무원비율",
            p.Ships.Select(s => $"{s.Name}  운항 {s.SailShare}% · 파수 {s.WatchShare}% · 갑판 {100 - s.SailShare - s.WatchShare}%").ToList());
        if (which < 0) return;
        var ship = p.Ships[which];
        int sail = AmountDialog.Ask(this, $"{ship.Name} — 운항(돛)에 붙일 선원 몫(%, 지금 {ship.SailShare})", 100);
        if (sail < 0) return;
        int watch = AmountDialog.Ask(this, $"{ship.Name} — 파수(망보기)에 붙일 선원 몫(%, {100 - Math.Clamp(sail, 0, 100)} 까지, 남는 몫은 갑판)", 100 - Math.Clamp(sail, 0, 100));
        if (watch < 0) return;
        ship.SetShares(sail, watch);
        _host.SyncLoad();
        Note($" · {ship.Name} — 운항 {ship.SailShare}% · 파수 {ship.WatchShare}% · 갑판 {100 - ship.SailShare - ship.WatchShare}%");
    }

    /// <summary>「적하편성」 — 배에서 배로 보급 · 교역품을 옮긴다(<see cref="PlayerState.Transship"/>).</summary>
    private void MoveCargo()
    {
        var p = _host.Player;
        if (p.Ships.Count <= 1) { Say(0xD9, "함선이 １척밖에 없습니다."); return; }
        int a = BuildingMenu.Ask(this, "적하편성 — 어느 배에서", p.Ships.Select(ShipLine).ToList());
        if (a < 0) return;
        var from = p.Ships[a];
        var names = _host.Economy?.GoodsNames;
        var items = new List<(int What, string Line)>();
        for (int k = 0; k < PlayerState.SupplyKinds; k++)
            if (from.Supplies[k] > 0) items.Add((k, $"{Economy.SupplyNames[k]} {from.Supplies[k]}"));
        for (int k = 0; k < PlayerState.CargoSlots; k++)
            if (from.Cargo[k].Good >= 0)
                items.Add((PlayerState.SupplyKinds + k, $"{(names != null ? names[from.Cargo[k].Good] : from.Cargo[k].Good.ToString())} {from.Cargo[k].Amount}"));
        if (items.Count == 0) { Note(" · 옮길 짐이 없습니다"); return; }
        int w = BuildingMenu.Ask(this, $"{from.Name} — 무엇을", items.Select(i => i.Line).ToList());
        if (w < 0) return;
        var others = p.Ships.Where(s => s != from).ToList();
        int b = BuildingMenu.Ask(this, "어느 배로", others.Select(ShipLine).ToList());
        if (b < 0) return;
        var to = others[b];
        int what = items[w].What;
        int have = what < PlayerState.SupplyKinds ? from.Supplies[what] : from.Cargo[what - PlayerState.SupplyKinds].Amount;
        int amount = AmountDialog.Ask(this, $"얼마나 옮기겠습니까？(0-{Math.Min(have, to.Free)})", Math.Min(have, to.Free));
        if (amount <= 0) return;
        int moved = PlayerState.Transship(from, to, what, amount);
        _host.SyncLoad();
        Note($" · {from.Name} → {to.Name} {moved}");
    }

    /// <summary>「인사변경」 — 부선장 · 경리주임 · 수석항해사에 동료를 앉힌다(<see cref="Navigators.Appoint"/>).</summary>
    private void AppointMates()
    {
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        var mates = Navigators.Mates(m).Select(t => t.Person).ToList();
        if (mates.Count == 0) { Say(0xE5, "동료가 없습니다."); return; }
        while (true)
        {
            var posts = Navigators.PostNames;
            int post = BuildingMenu.Ask(this, "인사변경", posts.Select((n, i) =>
                $"{n} : {(Navigators.InPost(m, i) is >= 0 and var who ? _host.PersonName(who) : "-")}").ToList());
            if (post < 0) return;
            int pick = BuildingMenu.Ask(this, posts[post], mates.Select(_host.PersonName).ToList());
            if (pick < 0) continue;
            string name = _host.PersonName(mates[pick]);
            if (!Navigators.Appoint(m, mates[pick], post))
                Say(0xE3, $"{name}(는) 선장이므로 {posts[post]}(로) 임명할 수 없습니다.", name, posts[post]);
            else Say(0xE4, $"{name}(를) {posts[post]}(로) 임명했습니다.", name, posts[post]);
        }
    }

    /// <summary>「선장변경」 — 기함 말고 배 하나를 골라 쉬는 동료에게 맡긴다. 맡던 이는 쉰다.</summary>
    private void ChangeCaptain()
    {
        if (_host.Scenario is not { } vm) return;
        var p = _host.Player;
        if (p.Ships.Count <= 1) { Say(0xD9, "함선이 １척밖에 없습니다."); return; }
        int pick = BuildingMenu.Ask(this, "선장변경 — 배",
            p.Ships.Skip(1).Select(sh => $"{sh.Name}  선장 {_host.PersonName(sh.Captain >= 0 ? sh.Captain : _host.HeroPersonNumber)}").ToList());
        if (pick < 0) return;
        var ship = p.Ships[pick + 1];
        var free = Navigators.Mates(vm.Memory).Select(t => t.Person)
            .Where(x => Navigators.Get(vm.Memory, x, Navigators.Status) != Navigators.Captain).ToList();
        if (free.Count == 0) { Say(0xE5, "동료가 없습니다."); return; }
        int who = BuildingMenu.Ask(this, $"{ship.Name} — 새 선장", free.Select(_host.PersonName).ToList());
        if (who < 0) return;
        if (ship.Captain >= 0) Navigators.Command(vm.Memory, ship.Captain, aboard: false);
        ship.Captain = free[who];
        Navigators.Command(vm.Memory, ship.Captain, aboard: true);
        _host.Recaptain(pick + 1);
        string skipper = _host.PersonName(ship.Captain);
        Note($" · {ship.Name}의 선장을 {skipper}{(KoreanBatchim(skipper) ? "으로" : "로")} 바꿨습니다");
    }

    /// <summary>「함선처분」 — 배를 버린다. 기함은 안 된다(0xE9). 짐 · 선원도 함께 잃는다.</summary>
    private void ScuttleShip()
    {
        var p = _host.Player;
        if (p.Ships.Count <= 1) { Say(0xE9, "기함은 처분할 수 없습니다."); return; }
        int pick = BuildingMenu.Ask(this, "함선처분", p.Ships.Skip(1).Select(ShipLine).ToList());
        if (pick < 0) return;
        var ship = p.Ships[pick + 1];
        // 「%s(를) 처분하겠습니다. 좋습니까？」(0xEA, 0x00446D27)
        if (MessageBox.Show(this, MsgOr(0xEA, $"{ship.Name}(를) 처분하겠습니다.\n좋습니까？", ship.Name), "함선처분", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (_host.RemoveShip(pick + 1)) Note($" · {ship.Name}{(KoreanBatchim(ship.Name) ? "을" : "를")} 처분했습니다");
    }

    /// <summary>「지휘」 — 목록 28(감시 · 교섭 · 전투 · 대열변경 · 식량할당 · 급여설정, <c>0x004454A2</c>, 손표 <c>0x00445564</c>).</summary>
    private void Command()
    {
        var names = _host.Menus?.Lists is { Count: > 28 } lists ? lists[28].ToList() : ["감  시", "교  섭", "전  투", "대열변경", "식량할당", "급여설정"];
        int pick = BuildingMenu.Ask(this, "지휘", names);
        switch (pick)
        {
            case 0: Watch(); break;
            case 1: Negotiate(); break;
            case 2: Attack(); break;
            case 3: Formation(); break;
            case 4: SetRations(); break;
            case 5: SetWages(); break;
        }
    }

    /// <summary>
    /// 「대열변경」(<c>0x004413C0</c>) — 한 척이면 0xD9. 1번부터 차례로 「%d번 함선은 어느 것으로 하겠습니까？」(0xDA)를 묻고, 취소하면 바로 앞 고른 것을 무른다.
    /// 다 고르면 「좋습니까？」(0xDB) — 아니면 처음부터. 기함(1번)이 바뀌면 주인공이 새 기함에 옮겨 탄다.
    /// </summary>
    private void Formation()
    {
        var ships = _host.Player.Ships;
        if (ships.Count == 0) return;
        if (ships.Count == 1) { Say(0xD9, "함선이 １척밖에 없습니다."); return; }
        while (true)
        {
            var order = new List<int>();
            while (order.Count < ships.Count)
            {
                var left = Enumerable.Range(0, ships.Count).Where(i => !order.Contains(i)).ToList();
                int no = order.Count + 1;
                int pick = BuildingMenu.Ask(this, MsgOr(0xDA, $"{no}번 함선은 어느 것으로 하겠습니까？").Replace("%d", no.ToString()),
                    [.. left.Select(i => ships[i].Name)]);
                if (pick >= 0) order.Add(left[pick]);
                else if (order.Count == 0) return;
                else order.RemoveAt(order.Count - 1);
            }
            string list = string.Join("\n", order.Select((i, k) => $"{k + 1}. {ships[i].Name}"));
            if (MessageBox.Show(this, list + "\n\n" + MsgOr(0xDB, "좋습니까？"), "대열변경", MessageBoxButton.YesNo) != MessageBoxResult.Yes) continue;
            if (_host.Reorder(order)) Note(" · 대열을 바꿨습니다");
            return;
        }
    }

    /// <summary>
    /// 「감시」(<c>0x00440CA0</c>) — 보이는 함대 하나를 골라 제독 · 나라 · 함대 갈래(목록 39) · 빠르기(<c>0x00418020</c> / 2) ·
    /// 뱃머리(목록 40) · 배들을 보인다. 해적 함대(번호 / 10 = 6, 60 말고)면 동료 가운데 가장 높은 직감(+0x1E)이 rand(90) 보다 크면
    /// 「아무래도 해적 같다」(0x2FE)고 귀띔한다. 원본은 화면의 함대를 누른다 — 우리는 보이는 거리 안의 함대를 늘어놓는다.
    /// </summary>
    private void Watch()
    {
        if (_host.Fleet is not { } f || _host.StartTables is not { } st) return;
        int reach = Math.Max(2, Sighting.Reach(_host.Player.Ships.Select(s => (s.WatchShare, s.Crew)), false));
        var seen = _host.FleetsInReach(reach);
        if (seen.Count == 0) { Say(0xD7, "군함의 모습은 보이지 않습니다."); return; }
        int pick = BuildingMenu.Ask(this, "감시", [.. seen.Select(n => $"{_host.PersonName(n.Admiral)} 함대")]);
        if (pick < 0) return;
        var fleet = seen[pick];
        string[] kinds = _host.Menus?.Lists is { Count: > 39 } l39 ? [.. l39[39]] : ["함대", "상선대", "전함대"];
        string[] dirs = _host.Menus?.Lists is { Count: > 40 } l40 ? [.. l40[40]] : ["북", "북동", "동", "남동", "남", "남서", "서", "북서"];
        int k = fleet.Index % 10 == 0 ? 0 : (fleet.Index % 10 - 1) / 5 + 1;
        string nation = NationNames7[Math.Min(6, fleet.Nation)];
        var ships = fleet.Slots.Select(s => st.GuessType(s) is var t && t < st.Templates.Count ? st.Templates[t].Name ?? "?" : "?").ToList();
        SayText($"{_host.PersonName(fleet.Admiral)}\n{nation} {kinds[Math.Min(k, kinds.Length - 1)]} · 뱃머리 {dirs[fleet.Heading & 7]}\n"
                + string.Join("\n", ships.Chunk(2).Select(c => string.Join(" · ", c))));
        if (fleet.Pirate && fleet.Index != 60 && _host.Scenario is { } vm)
        {
            int eye = Navigators.Mates(vm.Memory).Select(t => Navigators.Get(vm.Memory, t.Person, Navigators.Stats + 2)).DefaultIfEmpty(0).Max();
            if (eye > _host.Roll(90))
                Say(0x2FE, $"제독님, 저 함대는 {nation} {kinds[Math.Min(k, kinds.Length - 1)]}(를) 장비하고 있습니다만, 아무래도 해적 같습니다.",
                    nation, kinds[Math.Min(k, kinds.Length - 1)]);
        }
    }

    /// <summary>
    /// 「교섭」(<c>0x00441110</c> → <c>0x00441190</c>) — 둘레 두 칩 안의 함대를 고르면(없으면 0xE1) 제독이 하는 일을 말한다(움직임 갈래):
    /// 0 0x98 · 1 0x99 · 2 0x9A(항구) · 3 0x9B · 4 0x9C · 5 ~ 7 0x9D(쫓는 이의 나라 · 이름) · 8 0x9E · 9 0x9F · 10 0xA0.
    /// 주인공을 노리던 함대(5 ~ 7, 과녁이 주인공)는 11 — 「그게 무엇인지 가르쳐 줄까？」(0xA1) 뒤 덮친다. 칙명 희귀품을 아는 이면 지도를 판다(0x00424FD0).
    /// </summary>
    private void Negotiate()
    {
        var near = _host.FleetsInReach();
        if (near.Count == 0) { Say(0xE1, "교섭할 수 있는 함대는 없습니다."); return; }
        int pick = BuildingMenu.Ask(this, "교섭", [.. near.Select(n => $"{_host.PersonName(n.Admiral)} 함대")]);
        if (pick < 0) return;
        var fleet = near[pick];
        if (_host.Common is { Chapter: not 0 } job && job.GetVar(6) == 11 && (job.GetVar(17) & 0xFF) == fleet.Admiral)
        {
            SellMap(job.GetVar(18) & 0xFF);
            return;
        }
        string PortName(int p) => _host.Ports is { } pts && p >= 0 && p < pts.Ports.Count ? pts.Ports[p].Name : $"#{p}";
        int mode = fleet.Mode;
        if (mode is >= 5 and <= 7 && fleet.Goal == _host.HeroPersonNumber) mode = 11;
        switch (mode)
        {
            case 0xFF:
            {
                // 할 일이 없는 함대(+0x1B = 0xFF)는 이름만 댄다 — 「나는 %s의 %s(다).」(0x00441214, 나라는 사람 +0x31 & 0xF).
                int nation = _host.Scenario is { } vm0 ? Navigators.Get(vm0.Memory, fleet.Admiral, Navigators.Kind) & 0x0F : 6;
                string nn = NationNames7[Math.Min(6, nation)], who = _host.PersonName(fleet.Admiral);
                Say(0xD8, $"나는 {nn}의 {who}(다).", nn, who);
                break;
            }
            case 0: Say(0x98, "우리들은 본국으로 돌아가는 길이다."); break;
            case 1: Say(0x99, $"우리들은 {PortName(fleet.Goal)}항에 투자하러 가는 중이다.", PortName(fleet.Goal)); break;
            case 2: Say(0x9A, $"우리들은 {PortName(fleet.Goal)}항과 교역할 예정이다.", PortName(fleet.Goal)); break;
            case 3: Say(0x9B, "후훗！ 어리석은 것."); break;
            case 4: Say(0x9C, "바보 녀석, 내가 누군지 모르는 모양이군."); break;
            case 5 or 6 or 7:
            {
                int nation = _host.Scenario is { } vm ? Navigators.Get(vm.Memory, fleet.Goal, Navigators.Kind) & 0x0F : 6;
                Say(0x9D, $"{NationNames7[Math.Min(6, nation)]}의 {_host.PersonName(fleet.Goal)}(를) 찾고 있다.",
                    NationNames7[Math.Min(6, nation)], _host.PersonName(fleet.Goal));
                break;
            }
            case 8: Say(0x9E, "상선대를 호위하고 있다. 최근 이 부근도 시끄러우니까."); break;
            case 9: Say(0x9F, "해적으로부터 이 바다를 지키고 있다."); break;
            case 10: Say(0xA0, "네게 용건이 있지."); break;
            default:
                Say(0xA1, "그게 무엇인지 가르쳐 줄까？");
                Defend(fleet);   // 0x0044135E — 그 함대가 덮친다
                break;
        }
    }

    /// <summary>「급여설정」 — 바다 명령 목록 28 의 여섯째(<c>0x00441B10</c>). 동료를 골라 급여를 정한다(<see cref="Navigators.SetWage"/>).</summary>
    private void SetWages()
    {
        if (_host.Scenario is not { } vm) return;
        var mates = Navigators.Mates(vm.Memory).ToList();
        if (mates.Count == 0) { Say(0xE5, "동료가 없습니다."); return; }
        int pick = BuildingMenu.Ask(this, "급여설정", mates.Select(t => $"{_host.PersonName(t.Person)}  급여 {t.Wage}").ToList());
        if (pick < 0) return;
        var (slot, person, wage) = mates[pick];
        string name = _host.PersonName(person);
        if (Navigators.Get(vm.Memory, person, Navigators.Liking) >= 80) { Say(0xDC, "제독님, 그런 걱정은 안 해도 됩니다."); return; }
        while (true)
        {
            // 말을 거드는 동료(0x004367D0) 자신이면 「내 급료는 얼마로 해주시는 겁니까？」(0xDD), 아니면 0xDE.
            string ask = person == Navigators.Adviser(vm.Memory)
                ? MsgOr(0xDD, "내 급료는 얼마로 해주시는 겁니까？")
                : MsgOr(0xDE, $"{name}의 급료를 얼마로 하겠습니까？", name);
            int amount = AmountDialog.Ask(this, ask + $" (지금 {wage})", Navigators.MaxWage);
            if (amount < 0) return;
            if (Navigators.SetWage(vm.Memory, slot, person, amount) == Navigators.WageAnswer.Thanks) { Say(0xDF, "감사합니다."); return; }
            Say(0xE0, "겨우 그 정도라니, 말도 안돼.");
        }
    }

    /// <summary>
    /// 「적하일람」(<c>0x004434C0</c>) — 실은 교역품 하나를 고르면 어디서 비싸게 팔리는지 귀띔한다. 주인공이 회계(사람 +0x30 의 2)를
    /// 지녔으면 0x350, 아니면 경리주임(처지 4)이 회계를 지녔으면 0x351, 없으면 0x352 「잘 모르겠는데요」.
    /// </summary>
    private void CargoAdvice()
    {
        if (_host.Economy is not { } eco || _host.Ports is not { } ports) return;
        var goods = _host.Player.CargoTotals().Keys.OrderBy(g => g).ToList();
        if (goods.Count == 0) { Note(" · 실은 교역품이 없습니다"); return; }
        int pick = BuildingMenu.Ask(this, "적하일람", goods.Select(g => $"{eco.GoodsNames[g]} {_host.Player.CargoTotals()[g]}").ToList());
        if (pick < 0) return;
        int good = goods[pick];
        string name = eco.GoodsNames[good];
        int best = eco.BestSellPort(good, ports.Ports.Count);
        string port = best >= 0 ? ports.Ports[best].Name : "";
        const int Accounting = 2;
        if ((HeroField(SkillField) & Accounting) != 0 && best >= 0) { Say(0x350, $"{name}(는) 분명히 {port}항에서 비싸게 팔렸었지.", name, port); return; }
        if (_host.Scenario is { } vm && Navigators.InPost(vm.Memory, 1) is >= 0 and var clerk)
        {
            if ((Navigators.Get(vm.Memory, clerk, SkillField) & Accounting) != 0 && best >= 0)
                Say(0x351, $"내 기억으론, {name}(는) {port}항에서 비싸게 팔렸던 것 같습니다.", name, port);
            else Say(0x352, $"음… {name}(는) 어디에서 팔면 좋지？ 나는 회계 능력이 없어서 잘 모르겠는데요.", name);
            return;
        }
        Note(" · 회계를 아는 이가 없습니다 — 경리주임을 두세요");
    }

    /// <summary>
    /// 「식량할당」 — 물 · 식량 비율 0 ~ 100(<c>0x00441800</c>, 하나씩 올리고 내린다). 배마다 하루에
    /// 선원 x 비율 / 100 + 1 을 먹고, 비율이 낮으면 사기가 떨어진다(docs/22).
    /// </summary>
    private void SetRations()
    {
        var p = _host.Player;
        int water = AmountDialog.Ask(this, $"물 비율 — 지금 {p.WaterRate}", PlayerState.FullRation);
        if (water >= 0) p.WaterRate = water;
        int food = AmountDialog.Ask(this, $"식량 비율 — 지금 {p.FoodRate}", PlayerState.FullRation);
        if (food >= 0) p.FoodRate = food;
        Note($" · 물 {p.WaterRate} · 식량 {p.FoodRate}");
    }

    /// <summary>세이브 칸을 골라 지금 게임을 담는다(<see cref="SaveGame"/>).</summary>
    private void SaveToSlot()
    {
        // 「데이터를 선택해 주세요.」(0x3B7) — 고른 칸에 「저장하겠습니다. 좋습니까？」(0x3BA, 0x0041624A)
        int slot = BuildingMenu.Ask(this, MsgOr(0x3B7, "데이터를 선택해 주세요."), SaveGame.SlotLines());
        if (slot < 0) return;
        if (MessageBox.Show(this, MsgOr(0x3BA, "저장하겠습니다. 좋습니까？"), "저장", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            _host.Capture(_homePort).Write(slot);
            Note($" · {slot + 1}번 칸에 저장했습니다");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Note($" · 저장하지 못했습니다 — {e.Message}");
        }
    }

    /// <summary>세이브를 들여 그 항구나 바다에서 잇는다.</summary>
    public void LoadGame(SaveGame save)
    {
        _homePort = save.HomePort;
        if (save.Original is { } original)
        {
            int home = _host.ImportOriginal(original);
            if (home < 0) { Note($" · 원본 세이브 「{original.Label}」을 들이지 못했습니다"); return; }
            _homePort = home;
            save.Port = original.Port;
        }
        else _host.Restore(save);
        if (save.Port is >= 0 and < SeaMapHost.SmallPortFirst) _portPick.SelectedIndex = save.Port;
        else   // 바다 또는 작은 항구(세계지도를 그대로 본다)
        {
            _portPick.SelectedIndex = -1;
            _host.CellsPerPixel = 1.0 / 6;
        }
        _host.Invalidate();
        Retitle();
    }

    /// <summary>게임이 끝나 제목으로 돌아간다.</summary>
    public event Action? Ended;

    private bool _over;

    /// <summary>
    /// 게임 끝(<c>0x004042C0</c>) — 끝 깃발 0x10 → 말 0x3F8(서른 해 은퇴), 0x18 → 0x3F6(폭풍), 0x19 → 0x1BA 뒤 0x3F7(선원 없음), 0x1A → 0x3F5(해전).
    /// 묘비 글은 「１５%w년%w월%w일」 과 주인공 이름 · 성 둘씩이다. 다 보이면 제목으로 돌아간다.
    /// </summary>
    /// <summary>
    /// 엔딩(<c>0x004042C0</c>, 깃발 0 ~ 0xF) — 끝 그림(END.DAT NPK)은 아직 못 그린다. 「엔딩 이후의 데이터를 저장하겠습니다.」(0x2EA)로 저장 칸을 고르게 하고
    /// 끝 깃발 0x88 처럼 제목으로 돌아간다. 저장한 칸을 불러오면 엔딩 뒤로 이어 할 수 있다.
    /// </summary>
    private void OnStoryEnded(int code)
    {
        if (_over) return;
        _host.StoryOver = true;   // 0x00404388 — 끝 갈래의 2 를 지운다
        var (name, family) = _host.HeroName;
        // 주인공마다 엔딩 그림(END.DAT, 0x00405B50) — 항해일지를 쓰거나 읽는 모습이다.
        var ending = _host.Graphs?.Ending(_host.HeroPersonNumber);
        ScenarioDialog.Play(this, _host.Faces, [new ScenarioVm.Line("", $"{name} {family}의 이야기는 여기서 막을 내린다.")], ending);
        if (MessageBox.Show(this, MsgOr(0x2EA, "엔딩 이후의 데이터를 저장하겠습니다."), "엔딩", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            SaveToSlot();
        _over = true;
        _host.Sailing = false;
        Ended?.Invoke();
        Close();
    }

    private void OnGameOver(int reason)
    {
        if (_over) return;
        _over = true;
        _host.Sailing = false;
        if (reason == SeaMapHost.EndNoCrew) Say(0x1BA, "선원이 한 사람도 안 남았습니다. 더이상 항해를 계속할 수 없습니다.");
        int id = reason switch
        {
            SeaMapHost.EndRetire => 0x3F8, SeaMapHost.EndStorm => 0x3F6, SeaMapHost.EndNoCrew => 0x3F7, _ => 0x3F5,
        };
        var d = _host.Fleet?.Date ?? GameDate.NewGame;
        var (name, family) = _host.HeroName;
        string yy = $"{(d.Year + 1) % 100:00}";
        Say(id, $"{name} {family}의 항해는 여기서 끝났다.", yy, d.Month + 1, d.Day + 1, name, family, name, family);
        Ended?.Invoke();
        Close();
    }

    /// <summary>
    /// 새로 알아본 항구 · 부락(<c>0x00447E20</c>) — 「제독, ○○항을 발견하였다」 「제독, 부락을 발견하였다」 뒤 모험가 명성(0x1DC).
    /// 항구는 이제 고르기 목록에 든다.
    /// </summary>
    private void OnSighted(List<Sighting.Sight> seen)
    {
        var lines = new List<ScenarioVm.Line>();
        foreach (var s in seen)
        {
            // 「제독, 」(0x494) + 「%s항을 」(0x495) · 「부락을 」(0x496) + 「발견했습니다.」(0x497)
            string what = s.Port && _host.Ports is { } pt && s.Index < pt.Ports.Count
                ? MsgOr(0x495, $"{pt.Ports[s.Index].Name}항을 ", pt.Ports[s.Index].Name) : MsgOr(0x496, "부락을 ");
            string fame = s.Fame > 0 ? "\n" + MsgOr(0x1DC, $"모험가의 명성이 {s.Fame} 높아졌습니다.", s.Fame) : "";
            lines.Add(new ScenarioVm.Line("", MsgOr(0x494, "제독, ") + what + MsgOr(0x497, "발견했습니다.") + fame));
        }
        ScenarioDialog.Play(this, _host.Faces, lines);
    }

    /// <summary>
    /// 바다 이벤트 — 함께 쓰는 시나리오와 주인공 것에 「자리 0xA0 · 건물 = 바다에 나온 날 수」 로 묻는다(<c>0x00418D45</c>).
    /// 출항한 날(0)에도 한 번 본다.
    /// </summary>
    private void OnSeaDay(int day)
    {
        if (_host.CurrentPort >= 0 || _host.Fleet == null) return;
        var (name, family) = _host.HeroName;
        ScenarioVm.Chooser ask = (before, options) => ScenarioDialog.Ask(this, _host.Faces, before, options);
        foreach (var vm in new[] { _host.Common, _host.Scenario })
        {
            if (vm == null) continue;
            vm.Choose = ask;
            _host.WriteShipSide();
            if (vm.Enter(SeaMapHost.SeaEventPort, day, _host.Player.Gold, name, family) is not { Lines.Count: > 0 } run) continue;
            ScenarioDialog.Play(this, _host.Faces, run.Pending);
            _host.Apply(run);
            _host.WriteShipSide();
            _seaNote = $" · 바다 이벤트({day}일째)";
            UpdateStatus();
        }
    }

    /// <summary>항구에서 나와 앞바다에 배를 띄운다.</summary>
    /// <summary>
    /// 개발 — 새 게임에서 고향을 떠나 바다에 나온 남 함대 곁에 서서 「공격」 판을 차리고, 제 기함까지 남 배 생각으로 끝까지 돌린 뒤
    /// 뒤끝을 셈해 <paramref name="outPath"/> 에 적는다(<c>Uw2Play.exe --battle-smoke 파일</c>). 판 그림은 <paramref name="shotPath"/> 에 뜬다.
    /// </summary>
    public void DevAutopilotSmoke(string outPath)
    {
        var log = new System.Text.StringBuilder();
        _devQuiet = true;                 // 날씨 · 병 말 창이 돌리기를 막지 않게
        try
        {
            foreach (var s in _host.Player.Ships) { s.Crew = s.MaxCrew; s.Supplies[0] = s.Supplies[1] = 200; }
            _host.SyncLoad();
            PutToSea(check: false);
            foreach (string name in (string[])["세빌리아", "런던", "제노바", "이스탄불", "암스테르담"])
            {
                int port = _host.Ports?.ByName(name)?.Index ?? -1;
                var (ticks, track) = _host.DevAutopilotRun(port, Fleet.TicksPerDay * 120);
                log.AppendLine($"{name}({port}) — {(ticks >= 0 ? $"{ticks} 틱({ticks / Fleet.TicksPerDay} 일)" : "못 닿음")} · 자리 {string.Join(" ", track.Take(40))}");
                if (ticks < 0) break;
            }
        }
        catch (Exception e) { log.AppendLine(e.ToString()); }
        File.WriteAllText(outPath, log.ToString());
    }

    public void DevImportSmoke(string savePath, string outPath)
    {
        var log = new System.Text.StringBuilder();
        try
        {
            var file = OriginalSave.Load(savePath);
            foreach (var slot in file.Slots)
            {
                if (slot == null) continue;
                LoadGame(new SaveGame { Original = slot, Port = slot.Port });
                var f = _host.Fleet!;
                var p = _host.Player;
                log.AppendLine($"[{slot.Number + 1}] {slot.Label} · {_host.HeroName.Name} · {f.Date} {Fleet.Clock(f.TimeOfDay)} · 항구 {_host.CurrentPort} · 자리 {f.X:0},{f.Y:0} · 돈 {p.Gold} · 은행 {p.Balance} · 고향 {_homePort}{(_host.StoryOver ? " · 엔딩 뒤" : "")}");
                foreach (var s in p.Ships)
                    log.AppendLine($"    {s.Name}({s.Type}) 선원 {s.Crew}/{s.MaxCrew} 내구 {s.Hull}/{s.MaxHull} 대포 {s.Mounted}/{s.Guns}({s.GunKind}) 선장 {(s.Captain < 0 ? "-" : _host.PersonName(s.Captain))} 사기 {s.Morale} 보급 {string.Join("/", s.Supplies)} 짐 {string.Join(",", s.Cargo.Where(c => c.Good >= 0).Select(c => $"{c.Good}x{c.Amount}"))}");
                foreach (var (port, s) in p.Docked) log.AppendLine($"    도크 {port}: {s.Name}");
                foreach (var e in _host.Log.Entries.TakeLast(3)) log.AppendLine($"    일지 {1501 + e.Year}.{e.Month + 1}.{e.Day + 1} 갈래 {e.Type} ({e.A}, {e.B}, {e.C}, {e.D})");
                log.AppendLine($"    시나리오 장 {_host.Scenario?.Chapter}.{_host.Scenario?.Section} · SNR0 {_host.Common?.Chapter}.{_host.Common?.Section} · 남 함대 {_host.Others.Count} · 동료 {(_host.Scenario is { } vm ? Navigators.Mates(vm.Memory).Count() : 0)}");
            }
        }
        catch (Exception e) { log.AppendLine(e.ToString()); }
        File.WriteAllText(outPath, log.ToString());
    }

    public void DevBattleSmoke(string outPath, string? shotPath)
    {
        var log = new System.Text.StringBuilder();
        try
        {
            PutToSea(check: false);
            var f = _host.Fleet ?? throw new InvalidOperationException("함대가 없습니다");
            var enemy = _host.Others.Where(o => !o.Docked && o.Slots.Count > 0)
                .OrderBy(o => Math.Abs(o.X - f.X) + Math.Abs(o.Y - f.Y)).First();   // 고향에서 가장 가까운 — 뭍이 보이는 판
            log.AppendLine($"적 함대 {enemy.Index} · 제독 {_host.PersonName(enemy.Admiral)} · 배 {enemy.Slots.Count} · 자리 {enemy.X:0},{enemy.Y:0}");
            log.AppendLine($"자리 옮김 {f.PlaceNearSea(enemy.X, enemy.Y, 6)} → {f.X:0},{f.Y:0}");
            f.UnderSail = false;
            var battle = _host.OpenBattle(enemy);
            log.AppendLine($"판: 바람 {battle.WindDir}/{battle.WindPower} · 시각 {battle.Time} · 원군 {_host.BattleAllies.Count} · 달아난 동료 {_host.BattleDeserters.Count}");
            foreach (var s in battle.All) log.AppendLine($"  편 {s.Side} {s.Name} 선원 {s.Crew} 내구 {s.Hull}/{s.MaxHull} 대포 {s.Guns}({s.GunKind}) 탄약 {s.Shot} 자리 {s.X},{s.Y} 뭍 {battle.IsLand(s.X, s.Y)}");
            if (shotPath != null) BattleWindow.Shot(this, battle, _host.Messages, shotPath);
            int turns = 0;
            while (battle.NextShip() is { } ship && turns++ < 20000)
                foreach (var h in battle.AiTurn(ship)) { }
            log.AppendLine($"끝 {battle.Result} · 시각 {battle.Time} · 차례 {turns}");
            int gold = _host.Player.Gold;
            var spoils = _host.CloseBattle(battle, enemy);
            _host.StowBooty();
            log.AppendLine($"명성 +{spoils.Fame} · 금화 +{spoils.Gold} ({gold} → {_host.Player.Gold}) · 아이템 {spoils.Item ?? "-"} · 나포 후보 {spoils.Prizes.Count}");
            log.AppendLine($"거둔 것 {_host.Booty}");
            log.AppendLine($"남은 남 함대 {_host.Others.Count} · 그 함대 남음 {_host.Others.Contains(enemy)}");
            foreach (var s in _host.Player.Ships) log.AppendLine($"  제 배 {s.Name} 선원 {s.Crew} 내구 {s.Hull}/{s.MaxHull} 탄약 {s.Supplies[3]}");
        }
        catch (Exception ex) { log.AppendLine("오류: " + ex); }
        System.IO.File.WriteAllText(outPath, log.ToString());
    }

    private void PutToSea(bool check = true)
    {
        // 출항 셈(0x004229A0) — 선원 없는 배가 있으면 못 뜬다(0x39). 버틸 날이 0 이면 0x3B, 10 아래 0x3C, 180 넘으면 0x3A, 그 사이는 「약 %d일」(0x3D).
        if (check && _host.CurrentPort >= 0)
        {
            var p = _host.Player;
            if (p.Ships.Any(s => s.Crew == 0)) { Say(0x39, "이것으론 꼼짝도 못합니다."); return; }
            int days = p.SupplyDays;
            if (days == 0) { Say(0x3B, "이걸론 굶어 죽을 겁니다."); return; }
            string ask = days < 10 ? MsgOr(0x3C, "이걸론 그다지 오래 버티지 못합니다만, 출항하겠습니까？")
                       : days > 180 ? MsgOr(0x3A, "６개월 이상 항해할 수 있습니다. 출항하겠습니까？")
                       : MsgOr(0x3D, $"약 {days}일 항해할 수 있습니다. 출항하겠습니까？", days);
            if (MessageBox.Show(this, ask.Replace("\\n", "\n"), "출항",MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        }
        int from = _host.CurrentPort >= 0 ? _host.CurrentPort : _homePort;
        _host.ShowWorld();
        _portPick.SelectedIndex = -1;
        if (!_host.SailFrom(from)) _host.FitToWindow();
        else Dispatcher.BeginInvoke(() => OnSeaDay(0));
        _host.CellsPerPixel = 1.0 / 6;               // 칩 하나에 여섯 점 — 배가 잘 보인다
        _host.Invalidate();
        Retitle();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        double step = 20 * _host.CellsPerPixel;

        // 개발용 — 도시에서 Ctrl+F1 ~ F12 는 걷지 않고 건물 0 ~ 11 에 든다.
        if (_host.CurrentPort >= 0 && Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.F1 and <= Key.F12)
        {
            EnterBuilding(e.Key - Key.F1);
            e.Handled = true;
            return;
        }

        // 도시에서는 방향키가 사람을 걷게 한다 — 누르고 있는 동안 걷도록 호스트가 그때그때 본다.
        if (_host.CurrentPort >= 0 && _host.Walker != null &&
            e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            _host.Invalidate();
            e.Handled = true;
            return;
        }

        // 바다에 나와 있으면 방향키가 뱃머리를 잡는다.
        bool steering = _host.Sailing && _host.CurrentPort < 0 && _host.Fleet != null;
        if (steering)
        {
            switch (e.Key)
            {
                case Key.Left: _host.Steer(-1); e.Handled = true; return;
                case Key.Right: _host.Steer(+1); e.Handled = true; return;
                case Key.Up: _host.SteerTo(0); e.Handled = true; return;      // 북
                case Key.Down: _host.SteerTo(4); e.Handled = true; return;    // 남
                case Key.Space: _host.ToggleSail(); e.Handled = true; return;
            }
        }

        switch (e.Key)
        {
            case Key.Left: _host.Center = (_host.Center.X - step, _host.Center.Y); break;
            case Key.Right: _host.Center = (_host.Center.X + step, _host.Center.Y); break;
            case Key.Up: _host.Center = (_host.Center.X, _host.Center.Y - step); break;
            case Key.Down: _host.Center = (_host.Center.X, _host.Center.Y + step); break;
            case Key.G: _host.Renderer.ShowGrid = !_host.Renderer.ShowGrid; break;
            case Key.A: _arrows.IsChecked = !_host.ShowArrows; break;   // 바람·해류 화살표
            case Key.C: _host.Renderer.UseChips = !_host.Renderer.UseChips; break;
            case Key.F: _host.FitToWindow(); break;
            case Key.S: PutToSea(); return;
            case Key.W: _host.ShowWorld(); _portPick.SelectedIndex = -1; Retitle(); break;
            case Key.L:                                      // 리스본
                if (_host.ShowPort(PortTable.Lisbon)) { _portPick.SelectedIndex = PortTable.Lisbon; Retitle(); }
                break;
            default: return;
        }
        _host.Invalidate();
        e.Handled = true;
    }

    /// <summary>함대 한눈에 — 배 수 · 선원 · 물 · 식량.</summary>
    private string FleetText()
    {
        var p = _host.Player;
        int crew = p.Ships.Sum(s => s.Crew), room = p.Ships.Sum(s => s.MaxCrew);
        int water = p.Ships.Sum(s => s.Supplies[PlayerState.Water]), food = p.Ships.Sum(s => s.Supplies[PlayerState.Food]);
        int morale = p.Ships.Count > 0 ? p.Ships.Min(s => s.Morale) : 0;
        return $"배 {p.Ships.Count}척 · 선원 {crew}/{room} · 사기 {morale} · 물 {water} · 식량 {food}" + JobText();
    }

    /// <summary>맡은 조합 일 — 함께 쓰는 시나리오의 장(1 수송 · 2 구입 · 3 편지 · 4 해적 · 5 빚)과 변수 16 · 17 항구.</summary>
    private string JobText()
    {
        if (_host.Common is not { Chapter: > 0 and <= 5 } job) return "";
        string[] kinds = ["수송", "구입", "편지", "해적 퇴치", "빚 수금"];
        string Port(int i) => _host.Ports is { } pt && i < pt.Ports.Count ? pt.Ports[i].Name : $"#{i}";
        return $" · 일: {kinds[job.Chapter - 1]}({Port(job.GetVar(16))} → {Port(job.GetVar(17))})";
    }

    private string BalanceText() =>
        _host.Player.Balance switch { > 0 and var b => $" · 예금 {b}", < 0 and var b => $" · 빚 {-b}", _ => "" };

    private void UpdateStatus()
    {
        if (_host.Map == null) return;

        if (_host.CurrentPort >= 0)
        {
            var p = _host.Ports != null && _host.CurrentPort < _host.Ports.Ports.Count
                ? _host.Ports.Ports[_host.CurrentPort] : default;
            var wk = _host.Walker;
            string who = wk != null
                ? $" · 사람 ({wk.X:F1}, {wk.Y:F1}) {wk.Face}" + (wk.Blocked ? " · 막힘" : "")
                : " · 걸을 데를 못 찾았습니다";
            _status.Text = $"{_host.SceneName} ({_host.CurrentPort}번) · {PortMap.Size}x{PortMap.Size} 칸"
                         + who + $" · 돈 {_host.Player.Gold}{BalanceText()} · 빈 자리 {_host.Player.Free} · {FleetText()}" + _doorNote + $" · {_host.Source}";
            return;
        }
        // 커서 자리는 칩 눈금이다. 칸은 그 절반이다.
        int chipX = _host.HoverCell.X, chipY = _host.HoverCell.Y;
        int cx = chipX / ChipMap.PerCell, cy = chipY / ChipMap.PerCell;
        bool inside = cx >= 0 && cx < WorldMap.Width && cy >= 0 && cy < WorldMap.Height;
        string cell = inside ? $"칸 ({cx}, {cy}) 값 {_host.Map[cx, cy]}" : "칸 —";

        string wind = "";
        if (_host.Wind != null && inside)
        {
            // 표는 칩 눈금으로 짚는다. 커서 자리에는 함대 날짜가 없어 게임 시작 달(4 = May)로 본다.
            var (dir, speed) = _host.Wind.Wind(4, chipX, chipY);
            var (cdir, cspeed) = _host.Wind.Current(chipX, chipY);
            wind = $" · 바람 {dir}/{speed} 해류 {cdir}/{cspeed}";
        }

        var f = _host.Fleet;
        string sail = "";
        if (f != null && _host.CurrentPort < 0)
        {
            sail = $" | {f.Date} {Fleet.Clock(f.TimeOfDay)} · {f.DaysAtSea}일째 · 돈 {_host.Player.Gold} · 「{_host.ShipName}」 {FleetText()} ({f.X:F0},{f.Y:F0}) 뱃머리 {Fleet.DirName(f.Heading)}"
                 + $" · 바람 {Fleet.DirName(f.LastWind.Dir)} {f.LastWind.Speed}"
                 + $" · 해류 {Fleet.DirName(f.LastCurrent.Dir)} {f.LastCurrent.Speed}"
                 + $" · {Sailing.Knots(f.LastSpeed)}노트({f.LastSpeed})"
                 + (f.UnderSail ? "" : " · 돛 접음") + (f.Blocked ? " · 뭍" : "")
                 + (f.Weather.Label.Length > 0 ? $" · {f.Weather.Label}" : "") + _seaNote;
        }

        double lon = cx / (double)WorldMap.Width * 360 - 180;
        double lat = 90 - cy / (double)WorldMap.Height * 180;
        _status.Text = $"{cell}{wind} · 경도 {lon:F1} 위도 {lat:F1} · 칩당 {1 / _host.CellsPerPixel:F1}점"
                     + sail + $" · {_host.Source}";
    }
}
