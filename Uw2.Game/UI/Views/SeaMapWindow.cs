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
        int lots = AmountDialog.Ask(this, $"{Economy.SupplyNames[pick]} 몇 단(열 통)", most);
        if (lots <= 0) return;
        int bought = p.BuySupply(pick, lots, price);
        _host.SyncLoad();
        Note($" · {Economy.SupplyNames[pick]} {bought * PlayerState.Lot}통을 실었습니다");
    }

    /// <summary>조선소 「함선 건조」 — 지을 수 있는 선종과 값(선종 +10)을 늘어놓는다. 짓기는 아직이다.</summary>
    private void ShowShipyard()
    {
        if (_host.Economy is not { } eco || _host.StartTables is not { } start) return;
        var types = eco.Buildable(_host.CurrentPort, start.ShipTypes);
        if (types.Count == 0) { _doorNote = " · 이 조선소는 배를 지을 수 없습니다"; UpdateStatus(); return; }
        // 새 배를 맡을 동료가 있어야 한다(0x00429180, 말 0xA6).
        int mate = _host.FreeMate;
        if (_host.HasMates && mate < 0) { Note(M(0xA6, "새 배를 맡을 항해사가 없습니다 — 주점이나 여관에서 동료를 구하세요")); return; }
        string NameOf(int t) => t < start.Templates.Count ? start.Templates[t].Name ?? t.ToString() : t.ToString();
        var p = _host.Player;
        int pick = BuildingMenu.Ask(this, $"함선 건조 — 돈 {p.Gold} · 배 {p.Ships.Count}/{SeaMapHost.MaxShips}",
            types.Select(t => $"{NameOf(t),-10} {start.ShipTypes[t].Price,6}").ToList());
        if (pick < 0) return;
        int type = types[pick], basePrice = start.ShipTypes[type].Price;
        if (p.Ships.Count >= SeaMapHost.MaxShips) { Note(" · 함대가 다 찼습니다"); return; }
        int industry = _host.CurrentPort >= 0 && _host.CurrentPort < eco.States.Count ? eco.States[_host.CurrentPort].Industry : 0;
        int grades = Shipyard.GradeChoices(type, industry);
        int grade = BuildingMenu.Ask(this, $"{NameOf(type)} — 등급",
            Enumerable.Range(0, grades).Select(g => $"등급 {g}  {Shipyard.BuildPrice(basePrice, g),6}").ToList());
        if (grade < 0) return;
        int price = Shipyard.BuildPrice(basePrice, grade);
        if (p.Gold < price) { Note($" · 돈이 모자랍니다({NameOf(type)} {price})"); return; }
        if (!_host.AddShip(type, grade, mate)) return;
        p.SetGold(p.Gold - price);
        Note($" · {NameOf(type)}{(KoreanBatchim(NameOf(type)) ? "을" : "를")} {price}에 지었습니다{(mate >= 0 ? $" · 선장 {_host.PersonName(mate)}" : "")}");
    }

    /// <summary>조선소 「중고선 구입」 — 항구에 들 때 마련한 다섯 칸(<see cref="Shipyard.EnterPort"/>), 값은 선종 값.</summary>
    private void BuyUsedShip()
    {
        if (_host.StartTables is not { } start || _host.Scenario is not { } vm) return;
        var p = _host.Player;
        var used = Shipyard.Used(vm.Memory, start.ShipTypes.Count);
        if (used.Count == 0) { Note(" · 지금은 중고선이 없습니다"); return; }
        if (p.Ships.Count >= SeaMapHost.MaxShips) { Note(" · 함대가 다 찼습니다"); return; }
        int mate = _host.FreeMate;
        if (mate < 0) { Note(M(0xA6, "새 배를 맡을 항해사가 없습니다 — 주점이나 여관에서 동료를 구하세요")); return; }
        string NameOf(int t) => t < start.Templates.Count ? start.Templates[t].Name ?? t.ToString() : t.ToString();
        int pick = BuildingMenu.Ask(this, $"중고선 구입 — 돈 {p.Gold}",
            used.Select(u => $"{NameOf(u.Type),-10} {start.ShipTypes[u.Type].Price,6}").ToList());
        if (pick < 0) return;
        var (slot, type) = used[pick];
        int price = start.ShipTypes[type].Price;
        if (p.Gold < price) { Note($" · 돈이 모자랍니다({NameOf(type)} {price})"); return; }
        if (!_host.AddShip(type, captain: mate)) return;
        p.SetGold(p.Gold - price);
        Shipyard.TakeUsed(vm.Memory, slot);
        Note($" · 중고 {NameOf(type)}{(KoreanBatchim(NameOf(type)) ? "을" : "를")} {price}에 샀습니다 · 선장 {_host.PersonName(mate)}");
    }

    /// <summary>조선소 「수리」 — 배를 골라 고친다(<see cref="Shipyard.RepairCost"/>).</summary>
    private void RepairShips()
    {
        if (_host.StartTables is not { } start) return;
        var p = _host.Player;
        var costs = p.Ships.Select(s => Shipyard.RepairCost(start.ShipTypes[s.Type], s)).ToList();
        if (costs.All(c => c == 0)) { Note(M(0xCC, "고칠 곳이 없습니다")); return; }
        int pick = BuildingMenu.Ask(this, $"수리 — 돈 {p.Gold}",
            p.Ships.Select((s, i) => $"{s.Name,-10} 내구 {s.Hull,3}/{s.MaxHull,-3} {(costs[i] > 0 ? costs[i].ToString() : "-"),6}").ToList());
        if (pick < 0) return;
        if (costs[pick] == 0) { Note(M(0xCC, "고칠 곳이 없습니다")); return; }
        if (p.Gold < costs[pick]) { Note(M(0xCF, $"돈이 모자랍니다(수리 {costs[pick]})")); return; }
        p.SetGold(p.Gold - costs[pick]);
        Shipyard.Repair(start.ShipTypes[p.Ships[pick].Type], p.Ships[pick]);
        Note($" · {p.Ships[pick].Name} 수리 {costs[pick]}");
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
        if (b == NpcBuilding) { AtPatron(); return; }
        var items = _host.Menus?.ForBuilding(b) ?? [];
        if (items.Count == 0) return;
        if (b == ChurchBuilding) _worshipped = false;
        int pick = BuildingMenu.Ask(this, PortDoors.Names[b], items);
        if (pick < 0) return;
        if (b == HarborBuilding && pick == 0) { PutToSea(); return; }
        if (b == TradeBuilding && pick == 0) { ShowGoods(); return; }
        if (b == TradeBuilding && pick == 1) { SellGoods(); return; }
        if (b == TradeBuilding && pick == 2) { Invest(industry: false); return; }
        if (b == ShipyardBuilding && pick == 5) { Invest(industry: true); return; }
        if (b == ShipyardBuilding && pick == 0) { ShowShipyard(); return; }
        if (b == ShipyardBuilding && pick == 1) { BuyUsedShip(); return; }
        if (b == ShipyardBuilding && pick == 2) { RepairShips(); return; }
        if (b == ShipyardBuilding && pick == 3) { SellShip(); return; }
        if (b == PubBuilding && pick == 0) { HireCrew(); return; }
        if (b == PubBuilding && pick == 3) { TalkToNavigator(pub: true); return; }
        if (b == PubBuilding && pick == 4) { TalkToBarmaid(); return; }
        if (b == InnBuilding && pick == 0) { TalkToNavigator(pub: false); return; }
        if (b == InnBuilding && pick == 1) { ShowSupport(); return; }
        if (b == InnBuilding && pick == 2 && _host.Fleet is { } f)
        {
            f.SleepUntilMorning();
            Note($" · 하룻밤 묵었습니다 — {f.Date} {Fleet.Clock(f.TimeOfDay)}");
            return;
        }
        if (b == BankBuilding) { UseBank(pick); return; }
        if (b == ChurchBuilding) { AtChurch(pick); return; }
        if (b == PalaceBuilding) { AtPalace(pick); return; }
        if (b == GuildBuilding && pick == 0) { OfferJobs(); return; }
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
    /// 한 목록으로 늘어놓는다. 업무정보(<c>0x00426410</c>)는 아직이다.
    /// </summary>
    private void TalkToBarmaid()
    {
        if (_host.Scenario is not { } vm) return;
        var m = vm.Memory;
        int maid = Barmaids.InPort(m, _host.CurrentPort);
        if (maid < 0) { Note(" · 여급이 없습니다"); return; }
        string name = Barmaids.Name(m, maid);
        Say(0x18E, "무엇을 듣고 싶은가요？");
        string[] items = ["업무정보", "그녀의취향", "그녀의연령", "자  랑", "선  물"];
        while (true)
        {
            int pick = BuildingMenu.Ask(this, $"{name} — 호감 {Barmaids.Get(m, maid, Barmaids.Liking)}", items);
            if (pick < 0) return;
            switch (pick)
            {
                case 0: Say(0x18F, "죄송해요, 모르겠어요."); break;
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
        if (who < 0) { Note(" · 아무도 없습니다"); return; }
        string name = Patrons.Name(m, who);
        int kind = Patrons.KindOf(m, who);
        if (kind is not (Patrons.Noble or Patrons.Scholar)) { Note($" · {name}"); return; }
        var (hero, family) = _host.HeroName;
        if (kind == Patrons.Scholar) Say(0x1ED, "내게 뭔가 용건이 있습니까？");
        else if (Patrons.IsContracted(m, who)) Say(0x1DE, $"오！ {family}님, 어서 들어오세요.", family);
        else Say(0x1DD, "우리 가문에 무슨 용건이 있으십니까？");
        string[] items = kind == Patrons.Noble ? ["계  약", "발견물 보고", "모험소문"] : ["계  약", "기능 습득", "지리 보고"];
        while (true)
        {
            bool bound = Patrons.IsContracted(m, who);
            var lines = items.Select((t, i) => (i == 0) == bound && !(kind == Patrons.Scholar && i == 1) ? t + " (안 됨)" : t).ToList();
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
                else ReportSurvey(who);
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

    /// <summary>「지리 보고」(<c>0x00422150</c>) — 보고 뒤 칠한 칸 x 20 x (5 − 격)을 받고 명성이 칸 x 5 오른다.</summary>
    private void ReportSurvey(int who)
    {
        if (_host.Scenario is not { } vm) return;
        var sv = _host.Survey;
        if (sv.SinceReport == 0) { Say(0x1FA, "아니, 조사가 전혀 진척되어 있지 않군요. 더 분발해 주세요."); return; }
        if (sv.Total >= Survey.Complete) { Say(0x1FB, "와아！！ 드디어 세계지도를 완성하셨군요！"); sv.Finish(); }
        int rank = vm.Memory.Read(ScenarioMemory.At(Patrons.Table, who, Patrons.Flags), false);
        int reward = Survey.Reward(rank, sv.SinceReport);
        var fp = ScenarioMemory.At(ScenarioMemory.HeroTable, _host.Hero, Patrons.FameField);
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
        int reward = Patrons.Report(m, who, disc, _host.Hero, out int fame, out int message);
        _host.Player.SetGold(_host.Player.Gold + reward);
        _host.WriteShipSide();
        Say(message, $"{name}？ 그럼, 금화 {reward}닢을 받아 주시요.", name, reward);
        Note(M(0x1DC, $"모험가의 명성이 {fame} 높아졌습니다.", fame));
    }

    /// <summary>게임 말 하나를 대사 창에 띄운다 — 말이 없으면 <paramref name="fallback"/>.</summary>
    private void Say(int id, string fallback, params object[] args)
    {
        string text = _host.Messages is { } m && m[id].Length > 0 ? m.Format(id, args) : fallback;
        ScenarioDialog.Play(this, _host.Faces, [new ScenarioVm.Line("", text)]);
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
        // 동료운(0x00428340) — 0x318 뒤 동료를 고르면 그 운(+0x23) / 25 로 말 0x4E 부터.
        Say(0x318, "누구의 운세를 볼까요？");
        var mates = Navigators.Mates(vm.Memory).Select(t => t.Person).ToList();
        if (mates.Count == 0) { Note(" · 동료가 없습니다"); return; }
        int who = BuildingMenu.Ask(this, "동료운", mates.Select(_host.PersonName).ToList());
        if (who < 0) return;
        Say(0x4E + Navigators.Get(vm.Memory, mates[who], Church.Luck) / 25, "평범한 운세인 것 같군요.");
    }

    /// <summary>
    /// 조합 「업무 알선」(<c>0x0041E900</c>) — 일은 함께 쓰는 시나리오(SNR0)가 장 하나로 돌린다.
    /// <code>
    ///   장 0(일 없음)  변수 0 ~ 2 가 고를 일 셋의 갈래, 3 ~ 5 가 그 짝. 고르면 변수 8 = 짝, 절 = 고른 차례 + 1 로
    ///                  조합(건물 6)의 이벤트를 돌리고(0x0041EA50), 끝나면 장 = 변수 6(맡은 일), 절 = 0
    ///                  깃발 16 · 17 이 다 서 있으면 나라 탓에 일을 주지 않는다
    ///   장 ≠ 0         변수 6 이 6 아래면 맡은 일이 있다 — 변수 16 이 이 항구면 말 0x3A3, 아니면 「○○ 의 일이 남았다」(0x152)
    /// </code>
    /// 변수 6 이 6 이상인 일(보물 · 소문 갈래, 0x0041EA90)은 아직이다.
    /// </summary>
    private void OfferJobs()
    {
        if (_host.Common is not { } job) { Note(" · 지금은 맡길 일이 없습니다"); return; }
        int port = _host.CurrentPort;
        if (job.Chapter != 0)
        {
            int kind = job.GetVar(6), to = job.GetVar(16);
            string where = _host.Ports is { } pt && to < pt.Ports.Count ? pt.Ports[to].Name : $"#{to}";
            Note(kind >= 6 ? " · 그 일은 아직 없습니다"
                 : to == port ? " · 맡은 일을 먼저 마치세요" : $" · {where}의 일이 남아 있습니다");
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

    /// <summary>왕궁 — 망명은 다른 나라에서, 자금 · 선박원조는 제 나라에서(<see cref="Palace"/>). 알현 · 밀회는 시나리오 몫이다.</summary>
    private void AtPalace(int pick)
    {
        if (_host.Scenario is not { } vm || _host.Ports is not { } ports || _host.CurrentPort < 0
            || _host.CurrentPort >= ports.Ports.Count) return;
        int nation = Palace.NationOf(ports.Ports[_host.CurrentPort]);
        bool ours = nation == (_host.HeroNation & 7);
        if (pick == 1)
        {
            // 망명 — 다른 나라 왕궁에서만 뜻이 있다.
            if (ours) { Note(" · 이미 이 나라를 섬기고 있습니다"); return; }
            string name = nation < NationNames.Length ? NationNames[nation] : "이 나라";
            if (MessageBox.Show(this, $"{name}에 망명하시겠습니까?", "망명", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            Palace.Defect(vm.Memory, _host.HeroPersonNumber, nation);
            _homePort = _host.CurrentPort;
            Note($" · {name}의 사람이 되었습니다");
            return;
        }
        if (pick == 0) { Audience(ours); return; }
        if (!ours) { Note(" · 이 나라의 왕궁은 당신을 맞지 않습니다"); return; }
        if (pick == 3)
        {
            int type = Palace.ShipAid(vm.Memory, nation);
            if (type < 0 || _host.StartTables is not { } st || type >= st.ShipTypes.Count) { Note(M(0x1CD, "지금 내어 줄 배는 없다고 합니다")); return; }
            if (_host.Player.Ships.Count >= SeaMapHost.MaxShips) { Note(" · 함대가 다 찼습니다"); return; }
            int mate = _host.FreeMate;
            if (mate < 0) { Note(" · 배를 맡을 항해사가 없습니다"); return; }
            if (!_host.AddShip(type, captain: mate)) return;
            Palace.TookShip(vm.Memory, nation);
            string ship = type < st.Templates.Count ? st.Templates[type].Name ?? "" : "";
            Note($" · {Josa(ship, "을", "를")} 받았습니다 · 선장 {_host.PersonName(mate)}");
            return;
        }
        if (pick != 2) { Note(" · 그것은 아직 없습니다"); return; }
        int gold = _host.Player.Gold;
        int got = Palace.TakeFunds(vm.Memory, nation, ref gold);
        _host.Player.SetGold(gold);
        Note(got switch
        {
            0 => M(0x1CD, "지금 내어 줄 자금은 없다고 합니다"),
            < 0 => " · 더는 가질 수 없습니다",
            _ => $" · 자금 {got}을 받았습니다",
        });
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
        string[] items = ["세력상황", ours ? "사략허가서" : "사략허가서 (안 됨)", ours ? "면세증" : "면세증 (안 됨)"];
        while (true)
        {
            int pick = BuildingMenu.Ask(this, "알현", items);
            if (pick < 0) return;
            if (pick == 0) ShowInfluence();
            else if (ours) Note($" · 「{items[pick]}」은 아직 없습니다");
        }
    }

    /// <summary>「세력상황」 — 나라마다 몇 항구가 그 나라 것인지(항구 표 +0x15).</summary>
    private void ShowInfluence()
    {
        if (_host.Ports is not { } ports) return;
        var sb = new System.Text.StringBuilder();
        foreach (var g in ports.Ports.GroupBy(p => Palace.NationOf(p)).OrderBy(g => g.Key))
            sb.AppendLine($"{(g.Key < NationNames.Length ? NationNames[g.Key] : "그 밖")}  {g.Count()}항");
        MessageBox.Show(this, sb.ToString(), "세력상황");
    }

    /// <summary>ITEM 「구입」 — 이 항구 가게의 셋(밤 6 ~ 8 시각이면 넷, <see cref="ItemShop"/>).</summary>
    private void BuyItem()
    {
        if (_host.Scenario is not { } vm || _host.Economy is not { } eco || _host.CurrentPort < 0) return;
        var p = _host.Player;
        var stock = ItemShop.ForSale(eco.StateBytes, _host.CurrentPort, _host.Fleet?.TimeOfDay ?? 0);
        if (stock.Count == 0) { Note(" · 파는 것이 없습니다"); return; }
        var items = stock.Select(i => ItemShop.Get(vm.Memory, i)).ToList();
        int pick = BuildingMenu.Ask(this, $"구입 — 돈 {p.Gold}", items.Select(it => $"{it.Name,-10} {it.Price,7}").ToList());
        if (pick < 0) return;
        int gold = p.Gold;
        var result = ItemShop.Buy(vm.Memory, items[pick].Index, ref gold);
        p.SetGold(gold);
        Note(result switch
        {
            ItemShop.Result.Bought => $" · {Josa(items[pick].Name, "을", "를")} {items[pick].Price}에 샀습니다",
            ItemShop.Result.Owned => M(0x14C, "이미 가지고 있습니다"),
            ItemShop.Result.Poor => M(0xEF, "돈이 모자랍니다"),
            _ => " · 더 가질 수 없습니다",
        });
    }

    /// <summary>ITEM 「매각」 — 값의 반에서 부르고, 마다하면 운에 따라 매력만큼 올려 부른다.</summary>
    private void SellItem()
    {
        if (_host.Scenario is not { } vm) return;
        var owned = ItemShop.Owned(vm.Memory).Select(i => ItemShop.Get(vm.Memory, i)).ToList();
        if (owned.Count == 0) { Note(M(0xF3, "팔 것이 없습니다")); return; }
        int pick = BuildingMenu.Ask(this, "매각", owned.Select(it => it.Name).ToList());
        if (pick < 0) return;
        var item = owned[pick];
        if ((item.Kind & 0x10) != 0) { Note(M(0x14D, "그것은 팔 수 없습니다")); return; }
        if (item.Power == 0) { Note(M(0x3A1, "그런 것은 받지 않습니다")); return; }
        int hero = _host.HeroPersonNumber, offer = ItemShop.Offer(item);
        while (true)
        {
            if (MessageBox.Show(this, $"{Josa(item.Name, "이라면", "라면")} {offer}에 사지요.", "매각", MessageBoxButton.YesNo)
                == MessageBoxResult.Yes)
            {
                int gold = _host.Player.Gold;
                ItemShop.Sell(vm.Memory, item.Index, offer, ref gold);
                _host.Player.SetGold(gold);
                Note($" · {Josa(item.Name, "을", "를")} {offer}에 팔았습니다");
                return;
            }
            offer = ItemShop.Haggle(offer, Navigators.Get(vm.Memory, hero, Church.Luck),
                                    Navigators.Get(vm.Memory, hero, Navigators.Stats + 6), _host.Roll);
            if (offer < 0) { Note(" · 흥정이 깨졌습니다"); return; }
        }
    }

    /// <summary>교회 「예배 · 기부」 — 주인공의 운(사람 +0x23)을 올린다(<see cref="Church"/>).</summary>
    private void AtChurch(int pick)
    {
        if (_host.Scenario is not { } vm) return;
        int hero = _host.HeroPersonNumber;
        var p = _host.Player;
        if (pick == 0)
        {
            int gain = _worshipped ? 0 : Church.Worship(vm.Memory, hero, _host.Roll);
            _worshipped = true;
            Note(gain > 0 ? " · 예배를 드렸습니다 · 마음이 맑아졌습니다" : " · 예배를 드렸습니다");
            return;
        }
        int n = AmountDialog.Ask(this, $"기부 — 돈 {p.Gold}", p.Gold);
        if (n <= 0) return;
        int luck = Church.Donate(vm.Memory, hero, p.Gold, n, _host.Roll);
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
        if (most <= 0)
        {
            Note(pick switch
            {
                0 when p.Balance < 0 => M(0x66, "빚이 있으면 예금할 수 없습니다"),
                0 when p.Gold <= Bank.DepositFloorGold => M(0x64, "1000 넘게 가져와야 예금할 수 있습니다"),
                0 => M(0x65, "더는 맡길 수 없습니다"),
                1 => M(0x68, "맡긴 돈이 없습니다"),
                2 when p.Balance > 0 => M(0x6F, "예금이 있으면 빌릴 수 없습니다"),
                2 => M(0x71, "더는 빌려 드릴 수 없습니다"),
                _ => M(0x76, "갚을 빚이 없습니다"),
            });
            return;
        }
        string balance = p.Balance >= 0 ? $"예금 {p.Balance}" : $"빚 {-p.Balance}";
        int n = AmountDialog.Ask(this, $"{names[pick]} — 돈 {p.Gold} · {balance}", most);
        if (n <= 0) return;
        p.Transfer(pick is 0 or 3 ? n : -n);
        Note(pick switch
        {
            0 => M(0x6A, $"예금 {n}", n),
            1 => M(0x6E, $"출금 {n}", n),
            2 => M(0x74, $"대출 {n}", n) + M(0x75, ""),
            _ => M(0x78, $"상환 {n}"),
        });
    }

    /// <summary>교역소 「투자」 · 조선소 「공업투자」 — 이달 투자에 더하고 주인공 나라의 몫을 올린다(<see cref="Economy.Invest"/>).</summary>
    /// <remarks>원본은 나라 서울에서는 투자를 받지 않는다(0x00431A70, 나라 표 +0x0A). 원본 셈은 앞 여섯 나라만 본다.</remarks>
    private void Invest(bool industry)
    {
        if (_host.Economy is not { } eco) return;
        int port = _host.CurrentPort;
        if (port < 0 || port >= eco.States.Count) return;
        var p = _host.Player;
        if (_host.StartTables is { } st && st.Capitals.Take(6).Contains(port)) { Note(" · 서울에서는 투자를 받지 않습니다"); return; }
        int most = eco.MaxInvest(port, industry, p.Gold);
        if (most <= 0) { Note(" · 이달에는 더 받지 않습니다"); return; }
        string what = industry ? "공업투자" : "투자";
        int n = AmountDialog.Ask(this, $"{what} — 돈 {p.Gold} · 경제 {eco.States[port].Economy} · 공업 {eco.States[port].Industry}", most);
        if (n <= 0) return;
        int nation = _host.HeroNation;
        int gain = eco.Invest(port, industry, n, nation);
        p.SetGold(p.Gold - n);
        string share = nation < Economy.NoNation ? $" · 우리 몫 {eco.States[port].Shares[nation]}%(+{gain})" : "";
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
        var items = _host.Menus?.Lists is { Count: > 12 } lists ? lists[12].ToList() : ["한턱내기", "항해자정보", "고용", "결투"];
        if (!pub) items = items.Skip(1).Take(2).ToList();
        while (true)
        {
            int pick = BuildingMenu.Ask(this, $"{name} — 호감 {Navigators.Get(m, nav, Navigators.Liking)}", items);
            if (pick < 0) return;
            if (!pub) pick++;                                   // 여관은 한턱내기가 없다
            if (pick == 0)
            {
                int cls = _host.Economy is { } eco && port * Economy.StateSize + 0x24 < eco.StateBytes.Length
                    ? eco.StateBytes[port * Economy.StateSize + 0x24] : 0;
                int price = cls < Navigators.TreatPrice.Length ? Navigators.TreatPrice[cls] : 1;
                if (_host.Player.Gold < price) { Note(" · 돈이 모자랍니다"); return; }
                _host.Player.SetGold(_host.Player.Gold - price);
                int gain = Navigators.Treat(m, nav, hero);
                Note($" · {name}에게 한턱냈습니다({price}) · 호감 +{gain}");
            }
            else if (pick == 1)
            {
                var stats = Enumerable.Range(0, 7).Select(k => Navigators.Get(m, nav, Navigators.Stats + k));
                MessageBox.Show(this,
                    $"{name}\n능력치 {string.Join(" · ", stats)}\n항해 레벨 {Navigators.Get(m, nav, Navigators.NavLevel)} · " +
                    $"전투 레벨 {Navigators.Get(m, nav, Navigators.BattleLevel)}\n호감 {Navigators.Get(m, nav, Navigators.Liking)}",
                    "항해자정보");
            }
            else if (pick == 2)
            {
                var answer = Navigators.Ask(m, nav, hero, _host.HeroRank, _host.Roll, out int wage, needLiking: pub);
                switch (answer)
                {
                    case Navigators.Answer.AlreadyBound: Note(M(0x31, $"{Josa(name, "은", "는")} 이미 매인 몸입니다")); return;
                    case Navigators.Answer.NoRoom: Note(M(0x8C, "동료 칸이 다 찼습니다")); return;
                    case Navigators.Answer.NotFriendly: Note(M(0x8D, $"{Josa(name, "은", "는")} 아직 마음을 열지 않았습니다")); continue;
                    case Navigators.Answer.Refused: Note(M(0x91, $"{Josa(name, "이", "가")} 거절했습니다")); return;
                }
                if (MessageBox.Show(this, $"{name}: " + M(0x8E, $"한 달에 {wage}씩 주신다면 따르겠습니다.", wage)[3..], "고용", MessageBoxButton.YesNo)
                    != MessageBoxResult.Yes) { Note(M(0x8F, "고용하지 않았습니다")); return; }
                if (Navigators.Join(m, nav, hero, wage)) Note($" · {Josa(name, "이", "가")} 동료가 되었습니다(급여 {wage})" + M(0x90, ""));
                return;
            }
            else { Note(" · 결투는 아직 없습니다"); return; }
        }
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
        if (p.Vacancy == 0) { Note(M(0x1D, "더 태울 자리가 없습니다")); return; }
        if (p.Gold < Pub.MinGold) { Note(M(0x1E, "돈이 모자랍니다")); return; }
        int port = _host.CurrentPort;
        if (_host.Economy is not { } eco || port < 0 || port >= eco.States.Count || _host.HeroRecord is not { } hero) return;
        int economy = eco.States[port].Economy, price = Pub.Price(economy);
        int available = Pub.Available(economy, hero.Stats.Length > 6 ? hero.Stats[6] : 0, _host.HeroRank);
        if (available == 0) { Note(M(0x7B, "지금은 일하려는 사람이 없습니다")); return; }
        int most = Pub.MostHirable(available, p.Gold, price, p.Vacancy);
        int n = AmountDialog.Ask(this, $"선원 고용 — {available}명이 일하겠답니다 · 한 사람 {price} · 빈 자리 {p.Vacancy}", most);
        if (n <= 0) return;
        int hired = p.Hire(n, price);
        _host.SyncLoad();
        Note(M(0x79, $"선원 {hired}명을 {hired * price}에 고용했습니다", hired) + $" · 금화 {hired * price}");
    }

    /// <summary>교역소 「구입」 — 고른 교역품을 수만큼 산다(<see cref="Economy.ForSale"/> 값, <see cref="PlayerState.Buy"/>).</summary>
    private void ShowGoods()
    {
        if (_host.Economy is not { } eco) return;
        var goods = eco.ForSale(_host.CurrentPort);
        if (goods.Count == 0) { Note(" · 이 항구 교역소에는 파는 것이 없습니다"); return; }
        var p = _host.Player;
        int pick = BuildingMenu.Ask(this, $"구입 — 돈 {p.Gold} · 빈 자리 {p.Free}",
                                    goods.Select(g => $"{eco.GoodsNames[g.Good],-6}  {g.Price,5}").ToList());
        if (pick < 0) return;
        var (good, price) = goods[pick];
        int most = Math.Min(p.Free, price > 0 ? p.Gold / price : 0);
        int amount = AmountDialog.Ask(this, $"{eco.GoodsNames[good]} 몇 통", most);
        if (amount <= 0) return;
        int bought = p.Buy(good, amount, price);
        _host.SyncLoad();
        Note($" · {eco.GoodsNames[good]} {bought}통을 {bought * price}에 샀습니다");
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
        bar.Children.Add(Button("식량할당", (_, _) => SetRations()));
        bar.Children.Add(Button("함대", (_, _) => ShowFleetInfo()));
        bar.Children.Add(Button("상륙", (_, _) => Land()));

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
        _host.DiseaseCaught += name =>
        {
            _seaNote = $" · {name}이 돌기 시작했습니다";
            UpdateStatus();
        };
        _host.FleetReady += f => f.Weather.Said += line =>
        {
            _seaNote = $" · 「{line}」";
            UpdateStatus();
        };
        _host.BuildingEntered += (b, open) =>
        {
            _doorNote = open
                ? $" · 「{PortDoors.Names[b]}」에 들어갔습니다"
                : $" · 「{PortDoors.Names[b]}」 — {PortDoors.LockedText}";
            UpdateStatus();
            // 열려 있으면 그 건물의 MENU.DAT 목록을 띄운다 — 그림을 그리는 틈 안이라 한 박자 뒤에 연다.
            if (open) Dispatcher.BeginInvoke(() => EnterBuilding(b));
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
    /// 「함대」 — 목록 26(자함대정보 · 함선정보 · 인물정보 · 주인공정보 · 적하정보 …) 가운데 넷을 글로 보인다.
    /// 원본은 그림 창(목록 47 의 머리 글)이다.
    /// </summary>
    private void ShowFleetInfo()
    {
        string[] items = ["자함대정보", "인물정보", "주인공정보", "적하정보", "발견물정보"];
        int pick = BuildingMenu.Ask(this, "함대", items);
        if (pick < 0) return;
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
                }
                if (p.Scurvy || p.Fever) sb.AppendLine((p.Scurvy ? "괴혈병 " : "") + (p.Fever ? "열병" : ""));
                break;
            case 1:
                if (vm == null) break;
                foreach (var (_, person, wage) in Navigators.Mates(vm.Memory))
                {
                    var stats = Enumerable.Range(0, 7).Select(k => Get(person, Navigators.Stats + k));
                    string role = Get(person, Navigators.Status) == Navigators.Captain ? "선장" : "쉬는 중";
                    sb.AppendLine($"{_host.PersonName(person)} — {role} · 급여 {wage} · 호감 {Get(person, Navigators.Liking)}");
                    sb.AppendLine($"    능력치 {string.Join(" · ", stats)} · 항해 {Get(person, Navigators.NavLevel)} · 전투 {Get(person, Navigators.BattleLevel)}");
                }
                if (sb.Length == 0) sb.AppendLine("동료가 없습니다.");
                break;
            case 2:
            {
                int hero = _host.HeroPersonNumber;
                var stats = Enumerable.Range(0, 7).Select(k => Get(hero, Navigators.Stats + k));
                string nation = _host.HeroNation < NationNames.Length ? NationNames[_host.HeroNation] : "없음";
                int fame = vm != null ? vm.Memory.Read(ScenarioMemory.At(ScenarioMemory.HeroTable, _host.Hero, Patrons.FameField), true) : 0;
                sb.AppendLine($"{_host.PersonName(hero)} — {nation} · 작위 {_host.HeroRank} · 모험가 명성 {fame}");
                sb.AppendLine($"능력치 {string.Join(" · ", stats)}");
                sb.AppendLine($"항해 레벨 {Get(hero, Navigators.NavLevel)} · 전투 레벨 {Get(hero, Navigators.BattleLevel)} · 운 {Get(hero, Church.Luck)}");
                sb.AppendLine($"돈 {p.Gold}{BalanceText()}");
                if (vm != null)
                {
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
    /// 「상륙」 — 바다 명령 목록 29(<c>0x0041AD60</c>). 함대 둘레에 뭍이 있어야 내린다(아니면 말 0x1B9).
    /// 곁에 이번 게임의 발견물이 있으면 「부락에 상륙하겠습니다」 뒤 목록 35, 없으면 목록 36 이다(<see cref="Discoveries"/>).
    /// </summary>
    private void Land()
    {
        if (_host.Fleet is not { } f || _host.CurrentPort >= 0 || _host.Scenario is not { } vm) return;
        int fx = (int)Math.Floor(f.X), fy = (int)Math.Floor(f.Y);
        bool shore = false;
        for (int dy = -2; dy <= 3 && !shore; dy++)
            for (int dx = -2; dx <= 3 && !shore; dx++)
                if (!f.IsSea(fx + dx, fy + dy)) shore = true;
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

    /// <summary>탐색 덤(<c>0x0049A164</c>) — 부락에 내릴 때마다 0 에서 시작하고 「환대」 가 5 씩 올린다.</summary>
    private int _searchBonus;

    /// <summary>부락 — 목록 35(보물탐색 · 환대 · 식량약탈 · 출항 · 대기, <c>0x0041BA90</c>).</summary>
    private void AtVillage(int index)
    {
        _searchBonus = 0;
        string[] items = ["보물탐색", "환  대", "식량약탈", "출  항"];
        while (true)
        {
            int pick = BuildingMenu.Ask(this, "부락", items);
            switch (pick)
            {
                case 0: SearchVillage(index); break;
                case 1: Feast(); break;
                case 2: Loot(); break;
                default: Note(" · 배로 돌아왔습니다"); return;
            }
        }
    }

    /// <summary>뭍 — 목록 36(출항 · 응급수리 · 탐색 · 대기, <c>0x0041B090</c>).</summary>
    private void OnShore()
    {
        string[] items = ["출  항", "응급수리", "탐  색"];
        while (true)
        {
            int pick = BuildingMenu.Ask(this, "상륙", items);
            if (pick == 1) PatchShip();
            else if (pick == 2) SearchWater();
            else { Note(" · 배로 돌아왔습니다"); return; }
        }
    }

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

    /// <summary>
    /// 뭍 「탐색」 — 물을 찾는다(<c>0x0041B8B0</c>). rand(100) &lt; 운이면 찾고(0x1AA) 배마다 빈 자리만큼 물을 채운다.
    /// 원본은 사막 칩(0x59)이면 찾을 수 없다고 먼저 묻는데(0x1A8), 그건 아직 안 가린다.
    /// </summary>
    private void SearchWater()
    {
        if (Random.Shared.Next(100) >= HeroField(Church.Luck)) { Say(0x1A9, "물은 발견되지 않았습니다."); return; }
        int filled = 0;
        foreach (var s in _host.Player.Ships)
        {
            int k = s.Free;
            s.Supplies[PlayerState.Water] += k;
            filled += k;
        }
        _host.SyncLoad();
        Say(0x1AA, "깨끗한 물을 발견했습니다.");
        if (filled == 0) Say(0x1A6, "더이상 실을 수 없습니다.");
        else Note($" · 물 {filled}통을 실었습니다");
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
        int slot = BuildingMenu.Ask(this, "저장 — 칸을 고르세요", SaveGame.SlotLines());
        if (slot < 0) return;
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
        _host.Restore(save);
        if (save.Port >= 0) _portPick.SelectedIndex = save.Port;
        else
        {
            _portPick.SelectedIndex = -1;
            _host.CellsPerPixel = 1.0 / 6;
        }
        _host.Invalidate();
        Retitle();
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
    private void PutToSea()
    {
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
