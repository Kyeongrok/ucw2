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
    private const int HarborBuilding = 3, TradeBuilding = 0, ShipyardBuilding = 2, PubBuilding = 1, BankBuilding = 8, InnBuilding = 4, ChurchBuilding = 10, ItemBuilding = 9, PalaceBuilding = 5;

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
        if (_host.HasMates && mate < 0) { Note(" · 새 배를 맡을 항해사가 없습니다 — 주점이나 여관에서 동료를 구하세요"); return; }
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
        if (mate < 0) { Note(" · 새 배를 맡을 항해사가 없습니다 — 주점이나 여관에서 동료를 구하세요"); return; }
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
        if (costs.All(c => c == 0)) { Note(" · 고칠 곳이 없습니다"); return; }
        int pick = BuildingMenu.Ask(this, $"수리 — 돈 {p.Gold}",
            p.Ships.Select((s, i) => $"{s.Name,-10} 내구 {s.Hull,3}/{s.MaxHull,-3} {(costs[i] > 0 ? costs[i].ToString() : "-"),6}").ToList());
        if (pick < 0) return;
        if (costs[pick] == 0) { Note(" · 고칠 곳이 없습니다"); return; }
        if (p.Gold < costs[pick]) { Note($" · 돈이 모자랍니다(수리 {costs[pick]})"); return; }
        p.SetGold(p.Gold - costs[pick]);
        Shipyard.Repair(start.ShipTypes[p.Ships[pick].Type], p.Ships[pick]);
        Note($" · {p.Ships[pick].Name} 수리 {costs[pick]}");
    }

    /// <summary>조선소 「매각」 — 배 값의 반(<see cref="Shipyard.SellPrice"/>). 마지막 한 척은 못 판다.</summary>
    private void SellShip()
    {
        if (_host.StartTables is not { } start || _host.Economy is not { } eco) return;
        var p = _host.Player;
        if (p.Ships.Count <= 1) { Note(" · 마지막 배는 팔 수 없습니다"); return; }
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
        if (_host.Scenario is { } vm)
            vm.Choose = (before, options) => ScenarioDialog.Ask(this, _host.Faces, before, options);
        _host.WriteShipSide();
        if (_host.Scenario?.Enter(_host.CurrentPort, b, _host.Player.Gold, name, family) is { Lines.Count: > 0 } run)
        {
            ScenarioDialog.Play(this, _host.Faces, run.Pending);
            _host.Apply(run);
            Note($" · 「{PortDoors.Names[b]}」 이벤트"
                 + string.Concat(run.Joined.Select(p => $" · {Josa(_host.PersonName(p), "이", "가")} 동료가 되었습니다")));
            // F8 이 선 이벤트는 건물 볼일을 대신한다. 아니면, 또 여관이면 늘 목록이 이어서 뜬다(0x00431FF9).
            if (run.Stopped && b != InnBuilding) return;
        }
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
        if (b == InnBuilding && pick == 0) { TalkToNavigator(pub: false); return; }
        if (b == InnBuilding && pick == 1) { ShowSupport(); return; }
        if (b == BankBuilding) { UseBank(pick); return; }
        if (b == ChurchBuilding) { AtChurch(pick); return; }
        if (b == PalaceBuilding) { AtPalace(pick); return; }
        if (b == ItemBuilding && pick == 0) { BuyItem(); return; }
        if (b == ItemBuilding && pick == 1) { SellItem(); return; }
        if (b == HarborBuilding && pick == 1) { ShowSupplies(); return; }
        _doorNote = $" · 「{items[pick]}」은 아직 없습니다";
        UpdateStatus();
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
        if (!ours) { Note(" · 이 나라의 왕궁은 당신을 맞지 않습니다"); return; }
        if (pick == 3)
        {
            int type = Palace.ShipAid(vm.Memory, nation);
            if (type < 0 || _host.StartTables is not { } st || type >= st.ShipTypes.Count) { Note(" · 지금 내어 줄 배는 없다고 합니다"); return; }
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
            0 => " · 지금 내어 줄 자금은 없다고 합니다",
            < 0 => " · 더는 가질 수 없습니다",
            _ => $" · 자금 {got}을 받았습니다",
        });
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
            ItemShop.Result.Owned => " · 이미 가지고 있습니다",
            ItemShop.Result.Poor => " · 돈이 모자랍니다",
            _ => " · 더 가질 수 없습니다",
        });
    }

    /// <summary>ITEM 「매각」 — 값의 반에서 부르고, 마다하면 운에 따라 매력만큼 올려 부른다.</summary>
    private void SellItem()
    {
        if (_host.Scenario is not { } vm) return;
        var owned = ItemShop.Owned(vm.Memory).Select(i => ItemShop.Get(vm.Memory, i)).ToList();
        if (owned.Count == 0) { Note(" · 팔 것이 없습니다"); return; }
        int pick = BuildingMenu.Ask(this, "매각", owned.Select(it => it.Name).ToList());
        if (pick < 0) return;
        var item = owned[pick];
        if ((item.Kind & 0x10) != 0) { Note(" · 그것은 팔 수 없습니다"); return; }
        if (item.Power == 0) { Note(" · 그런 것은 받지 않습니다"); return; }
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
                0 when p.Balance < 0 => " · 빚이 있으면 예금할 수 없습니다",
                0 when p.Gold <= Bank.DepositFloorGold => " · 1000 넘게 가져와야 예금할 수 있습니다",
                0 => " · 더는 맡길 수 없습니다",
                1 => " · 맡긴 돈이 없습니다",
                2 when p.Balance > 0 => " · 예금이 있으면 빌릴 수 없습니다",
                2 => " · 더는 빌려 드릴 수 없습니다",
                _ => " · 갚을 빚이 없습니다",
            });
            return;
        }
        string balance = p.Balance >= 0 ? $"예금 {p.Balance}" : $"빚 {-p.Balance}";
        int n = AmountDialog.Ask(this, $"{names[pick]} — 돈 {p.Gold} · {balance}", most);
        if (n <= 0) return;
        p.Transfer(pick is 0 or 3 ? n : -n);
        Note($" · {names[pick]} {n}");
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
                    case Navigators.Answer.AlreadyBound: Note($" · {Josa(name, "은", "는")} 이미 매인 몸입니다"); return;
                    case Navigators.Answer.NoRoom: Note(" · 동료 칸이 다 찼습니다"); return;
                    case Navigators.Answer.NotFriendly: Note($" · {Josa(name, "은", "는")} 아직 마음을 열지 않았습니다"); continue;
                    case Navigators.Answer.Refused: Note($" · {Josa(name, "이", "가")} 거절했습니다"); return;
                }
                if (MessageBox.Show(this, $"{name}: 한 달에 {wage}씩 주신다면 따르겠습니다.", "고용", MessageBoxButton.YesNo)
                    != MessageBoxResult.Yes) { Note(" · 고용하지 않았습니다"); return; }
                if (Navigators.Join(m, nav, hero, wage)) Note($" · {Josa(name, "이", "가")} 동료가 되었습니다(급여 {wage})");
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
        if (p.Vacancy == 0) { Note(" · 더 태울 자리가 없습니다"); return; }
        if (p.Gold < Pub.MinGold) { Note(" · 돈이 모자랍니다"); return; }
        int port = _host.CurrentPort;
        if (_host.Economy is not { } eco || port < 0 || port >= eco.States.Count || _host.HeroRecord is not { } hero) return;
        int economy = eco.States[port].Economy, price = Pub.Price(economy);
        int available = Pub.Available(economy, hero.Stats.Length > 6 ? hero.Stats[6] : 0, _host.HeroRank);
        if (available == 0) { Note(" · 지금은 일하려는 사람이 없습니다"); return; }
        int most = Pub.MostHirable(available, p.Gold, price, p.Vacancy);
        int n = AmountDialog.Ask(this, $"선원 고용 — {available}명이 일하겠답니다 · 한 사람 {price} · 빈 자리 {p.Vacancy}", most);
        if (n <= 0) return;
        int hired = p.Hire(n, price);
        _host.SyncLoad();
        Note($" · 선원 {hired}명을 {hired * price}에 고용했습니다");
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

    /// <summary>항구에서 나와 앞바다에 배를 띄운다.</summary>
    private void PutToSea()
    {
        int from = _host.CurrentPort >= 0 ? _host.CurrentPort : _homePort;
        _host.ShowWorld();
        _portPick.SelectedIndex = -1;
        if (!_host.SailFrom(from)) _host.FitToWindow();
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
        return $"배 {p.Ships.Count}척 · 선원 {crew}/{room} · 사기 {morale} · 물 {water} · 식량 {food}";
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
