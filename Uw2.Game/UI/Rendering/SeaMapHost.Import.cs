using System.Text;
using Uw2.Game.Engine;
using Uw2.Game.Engine.Sea;
using Uw2.Support.Local.Formats;

namespace Uw2.Game.UI.Rendering;

/// <summary>원본 세이브(<see cref="OriginalSave"/>)를 우리 세이브로 옮긴다.</summary>
public sealed partial class SeaMapHost
{
    private const int FleetField = 0x2C, NationField = 0x31;

    /// <summary>
    /// 원본 세이브 한 칸을 들인다 — 주인공 · 날짜 · 머문 항구, 주인공 함대(자리 · 배 · 짐 · 선장 · 사기), 도크의 배, 돈 · 은행,
    /// 시나리오 둘(주인공 · SNR0)의 장 · 절 · 깃발 · 변수 · 가리키개, 시나리오 기억 표(사람 · 나라 · 함대 · 항구 형편 · 시장 · 아이템 · 발견물 …),
    /// 그리고 남 함대를 그 칸의 함대 표로 다시 띄운다. 들인 뒤 고향 항구(그 나라 서울)를 돌려준다. 못 들이면 -1.
    /// </summary>
    public int ImportOriginal(OriginalSave.Slot slot)
    {
        if (StartTables is not { } start || Fleet == null) return -1;
        var b = slot.Body;
        int person = slot.HeroPerson;
        int hero = Array.IndexOf(HeroPerson, person);
        if (hero < 0) return -1;
        SetHero(hero);
        if (Scenario is not { } vm) return -1;

        int pAt = StartData.PersonOffset + person * StartData.PersonSize;
        int fleetNo = b[pAt + FleetField];
        int fAt = StartData.FleetOffset + fleetNo * StartData.FleetSize;
        int nation = b[pAt + NationField] & 7;
        int home = b[StartData.NationOffset + nation * StartData.NationSize + StartData.CapitalField];

        var save = new SaveGame
        {
            Hero = hero, Port = slot.Port, HomePort = home, SavedAt = DateTime.Now,
            Fleet = new SaveGame.FleetState
            {
                CellX = slot.Short(fAt), CellY = slot.Short(fAt + 2), SubX = Engine.Sea.Sailing.CellUnits / 2, SubY = Engine.Sea.Sailing.CellUnits / 2,
                Heading = Fleet.Heading, Year = slot.Year, Month = slot.Month, Day = slot.Day, TimeOfDay = slot.Time,
                DaysAtSea = b[StartData.MiscOffset],
            },
            Player = new SaveGame.PlayerData
            {
                Gold = Math.Clamp(slot.Gold, 0, PlayerState.MaxGold), Balance = slot.Balance,
                WaterRate = b[StartData.MiscOffset + 1], FoodRate = b[StartData.MiscOffset + 2],
            },
            PortStates = b.AsSpan(OriginalSave.PortStateOffset, OriginalSave.PortStateLength).ToArray(),
        };
        for (int i = 0; i < StartData.SlotCount; i++)
            if (OriginalShip(slot, fAt + 0x2B + i * StartData.SlotSize, i, person) is { } ship) save.Player.Ships.Add(ship);
        for (int i = StartData.SlotCount; i < PlayerState.DockTotal + StartData.SlotCount; i++)
        {
            int port = b[OriginalSave.ShipSideOffset + i * OriginalSave.ShipSideSize + 0x1B];
            if ((port & 0x80) == 0) continue;
            if (OriginalShip(slot, OriginalSave.DockSlotOffset + (i - StartData.SlotCount) * StartData.SlotSize, i, person) is { } ship)
            {
                ship.Port = port & 0x7F;
                ship.Captain = -1;
                save.Player.Docked.Add(ship);
            }
        }
        if (save.Player.Ships.Count == 0) return -1;

        var memory = vm.Memory.Export();
        foreach (var (table, at, length, _) in OriginalSave.Tables)
            b.AsSpan(at, Math.Min(length, memory[table].Length)).CopyTo(memory[table]);
        save.Scenario = VmState(vm.Export(), memory, slot, OriginalSave.HeroChapter, OriginalSave.HeroSection, OriginalSave.HeroFlags,
                                OriginalSave.HeroVars, OriginalSave.HeroPointers);
        if (Common != null)
            save.Common = VmState(Common.Export(), memory, slot, OriginalSave.CommonChapter, OriginalSave.CommonSection, OriginalSave.CommonFlags,
                                  OriginalSave.CommonVars, OriginalSave.CommonPointers);
        save.Summary = $"{HeroName.Name} {HeroName.Family} · 원본 {slot.Label}";

        Restore(save);
        var fleets = StartData.ParseFleets(b.AsSpan(StartData.FleetOffset, StartData.FleetSize * StartData.FleetCount).ToArray());
        SpawnOthers(Fleet, fleets, fleetNo);
        return home;
    }

    /// <summary>
    /// 배 하나 — 함대 칸 9 바이트(<paramref name="at"/>: +0 선원 · +2 내구 · +3 최대 · +4 역풍 · +5 돛 · +6 단 대포 · +7 배 기록 · +8 깃발),
    /// 배 기록 22 바이트(이름 · +0x0F 선종 · +0x10 등급 · +0x11 대포 · +0x12 선원 자리 · +0x14 짐 자리),
    /// 칸 곁 30 바이트(<paramref name="side"/> 번: 보급 넷 · +8 파수 · +9 운항 · 짐 양 · 짐 번호 · +0x1B 선장 · +0x1C 사기 · +0x1D 선수상).
    /// </summary>
    private static SaveGame.ShipData? OriginalShip(OriginalSave.Slot slot, int at, int side, int hero)
    {
        var b = slot.Body;
        if ((b[at + 8] & 0x30) != 0x10) return null;
        int rAt = StartData.ShipRecordOffset + b[at + 7] * StartData.ShipRecordSize;
        if (rAt + StartData.ShipRecordSize > b.Length || b[rAt + 0x0F] >= StartData.ShipTypeCount) return null;
        int sAt = OriginalSave.ShipSideOffset + side * OriginalSave.ShipSideSize;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string name = Encoding.GetEncoding(949).GetString(b, rAt, 15).Split('\0')[0];
        int captain = b[sAt + 0x1B];
        return new SaveGame.ShipData
        {
            Type = b[rAt + 0x0F], Name = name, Grade = b[rAt + 0x10] & 7, Guns = b[rAt + 0x11],
            MaxCrew = slot.Word(rAt + 0x12), Room = slot.Word(rAt + 0x14),
            Crew = slot.Word(at), Hull = b[at + 2], MaxHull = b[at + 3], Upwind = b[at + 4], Sail = b[at + 5],
            Mounted = b[at + 6], GunKind = b[at + 8] & 7,
            Supplies = [.. Enumerable.Range(0, PlayerState.SupplyKinds).Select(k => slot.Word(sAt + k * 2))],
            WatchShare = b[sAt + 8], SailShare = b[sAt + 9],
            CargoAmounts = [.. Enumerable.Range(0, PlayerState.CargoSlots).Select(k => slot.Word(sAt + 0x0C + k * 2))],
            CargoGoods = [.. Enumerable.Range(0, PlayerState.CargoSlots).Select(k => b[sAt + 0x16 + k] == 0xFF || slot.Word(sAt + 0x0C + k * 2) == 0 ? -1 : b[sAt + 0x16 + k])],
            Captain = captain == 0xFF || captain == hero || side == 0 ? -1 : captain,
            Morale = b[sAt + 0x1C], Figurehead = b[sAt + 0x1D],
        };
    }

    /// <summary>시나리오 기계 하나 — 장 · 절 · 깃발 64(비트 아래부터, <c>0x0043DCEE</c>) · 낱말 변수 64 · 가리키개 64(원본 자리 → 표 자리).</summary>
    private static ScenarioVm.State VmState(ScenarioVm.State fresh, byte[][] memory, OriginalSave.Slot slot,
                                            int chapter, int section, int flags, int vars, int pointers)
    {
        var b = slot.Body;
        var flagBits = fresh.Flags.ToArray();
        for (int i = 0; i < 64 && i < flagBits.Length; i++) flagBits[i] = (b[flags + (i >> 3)] & (1 << (i & 7))) != 0;
        var words = fresh.Vars.ToArray();
        for (int i = 0; i < 64 && i < words.Length; i++) words[i] = (ushort)slot.Word(vars + i * 2);
        var ptrs = fresh.Pointers.ToArray();
        for (int i = 0; i < 64 && 2 * i + 1 < ptrs.Length; i++)
            if (OriginalSave.Pointer(BitConverter.ToInt32(b, pointers + i * 4)) is var (table, offset))
            {
                ptrs[2 * i] = table;
                ptrs[2 * i + 1] = offset;
            }
        return new ScenarioVm.State
        {
            Chapter = b[chapter], Section = b[section], Flags = flagBits, Vars = words, Pointers = ptrs,
            Memory = [.. memory.Select(t => (byte[])t.Clone())],
        };
    }
}
