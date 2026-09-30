using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 비품 — four office supplies per hero, ported from the web build's equipment block.
    ///
    /// It is the one growth axis that comes from PLAYING stages rather than from spending a
    /// currency: items drop on clears, bosses drop better, and levelling them is where the endless
    /// late-game gold finally goes. The percentages are deliberately smaller than ★ or 각성 give,
    /// so 비품 supplements the axes that already exist instead of replacing them.
    /// </summary>
    public static class EquipService
    {
        public static IReadOnlyList<EquipSlotDef> Slots => GameData.Equipment.slots;

        public static EquipSlotDef Slot(string id) =>
            GameData.Equipment.slots.FirstOrDefault(s => s.id == id);

        public static string Name(string slot, string grade) =>
            GameData.Equipment.names.FirstOrDefault(n => n.slot == slot && n.grade == grade)?.name ?? slot;

        public static float BasePct(string grade) =>
            GameData.Equipment.basePct.FirstOrDefault(b => b.grade == grade)?.pct ?? 0f;

        /// <summary>What an item gives right now: its base, plus a step of that base per level.</summary>
        public static float Pct(EquipItem it) =>
            it == null ? 0f : BasePct(it.grade) * (1f + it.lv * GameData.Balance.equipPctPerLevel);

        public static string Label(EquipItem it) =>
            it == null ? "" : it.lv > 0 ? $"{Name(it.slot, it.grade)} +{it.lv}" : Name(it.slot, it.grade);

        // ------------------------------------------------------------------ drops

        /// <summary>
        /// Rolls a grade for a drop. Later phases shift the table upward, which is why a boss is
        /// worth challenging rather than farming a comfortable stage forever.
        /// </summary>
        public static string RollGrade(int phase)
        {
            var p = Mathf.Max(0, phase);
            var order = GameData.GradeOrder;
            var w = new Dictionary<string, float>
            {
                ["D"] = Mathf.Max(4f, 40f - p * 6f),
                ["C"] = 30f,
                ["B"] = 14f + p * 2f,
                ["A"] = 4f + p * 1.5f,
                ["S"] = 0.4f + p * 0.4f,
            };
            var total = order.Sum(g => w.TryGetValue(g, out var v) ? v : 0f);
            var r = UnityEngine.Random.value * total;
            foreach (var g in order)
            {
                r -= w.TryGetValue(g, out var v) ? v : 0f;
                if (r <= 0f) return g;
            }
            return "D";
        }

        /// <summary>
        /// Rolls a drop for a cleared stage and puts it in the bag. Bosses roll twice and keep the
        /// better one; a boss's FIRST clear rolls four times and floors the grade, so the first
        /// time a wall falls the player gets something they can feel.
        /// </summary>
        public static EquipItem Drop(PlayerState p, int stage, bool boss, bool firstBoss)
        {
            var b = GameData.Balance;
            if (UnityEngine.Random.value >= (boss ? b.equipBossDropChance : b.equipDropChance)) return null;
            if (p.items.Count >= b.equipInventoryMax) return null;

            var phase = Mathf.Max(0, (Mathf.Max(1, stage) - 1) / Mathf.Max(1, b.bossEvery));
            var rolls = firstBoss ? b.equipBossFirstRolls : boss ? b.equipBossRolls : 1;

            string bestGrade = null;
            for (var i = 0; i < rolls; i++)
            {
                var g = RollGrade(phase);
                if (bestGrade == null || BasePct(g) > BasePct(bestGrade)) bestGrade = g;
            }
            if (firstBoss && BasePct(bestGrade) < BasePct(b.equipBossFirstMinGrade))
                bestGrade = b.equipBossFirstMinGrade;

            var slots = GameData.Equipment.slots;
            if (slots.Count == 0) return null;
            var slot = slots[UnityEngine.Random.Range(0, slots.Count)].id;

            var item = new EquipItem { id = p.nextItemId++, slot = slot, grade = bestGrade, lv = 0 };
            p.items.Add(item);
            return item;
        }

        /// <summary>A piece of a set slot and grade (the shop's boxes, mail gifts).</summary>
        public static EquipItem Make(PlayerState p, string slot, string grade)
        {
            var item = new EquipItem { id = p.nextItemId++, slot = slot, grade = grade, lv = 0 };
            p.items.Add(item);
            return item;
        }

        // ------------------------------------------------------------------ wearing

        public static EquipItem Worn(PlayerState p, string heroId, string slot) =>
            p.Item(p.WornId(heroId, slot));

        /// <summary>Items not on anyone's desk, for the picker.</summary>
        public static List<EquipItem> Free(PlayerState p, string slot = null) =>
            p.items.Where(it => (slot == null || it.slot == slot) && string.IsNullOrEmpty(p.WornBy(it.id)))
                   .OrderByDescending(it => Pct(it))
                   .ToList();

        public static void Equip(PlayerState p, string heroId, int itemId)
        {
            var item = p.Item(itemId);
            if (item == null) return;
            // Taking something off someone else's desk rather than duplicating it: an item is one
            // object, and two heroes wearing the same keyboard is the bug that makes the numbers
            // stop adding up.
            var holder = p.WornBy(itemId);
            if (!string.IsNullOrEmpty(holder)) p.SetWorn(holder, item.slot, 0);
            p.SetWorn(heroId, item.slot, itemId);
        }

        public static void Unequip(PlayerState p, string heroId, string slot) =>
            p.SetWorn(heroId, slot, 0);

        // ------------------------------------------------------------------ the numbers

        public readonly struct Bonus
        {
            public readonly float Atk, Hp, Skill, Speed, SetPct;
            public readonly string SetName;
            public Bonus(float atk, float hp, float skill, float speed, float setPct, string setName)
            { Atk = atk; Hp = hp; Skill = skill; Speed = speed; SetPct = setPct; SetName = setName; }
        }

        /// <summary>
        /// What a hero's four slots are worth, as percentages. Filling all four pays on its own and
        /// matching the grades pays more — so a matched D set can be worth more than three
        /// mismatched A pieces, which is what makes the drops keep mattering after the first S.
        /// </summary>
        public static Bonus Stats(PlayerState p, string heroId)
        {
            var b = GameData.Balance;
            float atk = 0, hp = 0, skill = 0, speed = 0;
            var worn = new List<EquipItem>();

            foreach (var slot in GameData.Equipment.slots)
            {
                var it = Worn(p, heroId, slot.id);
                if (it == null) continue;
                worn.Add(it);
                var pct = Pct(it);
                switch (slot.stat)
                {
                    case "atk": atk += pct; break;
                    case "hp": hp += pct; break;
                    case "skill": skill += pct; break;
                    case "speed": speed += pct; break;
                }
            }

            float setPct = 0;
            var setName = "";
            if (worn.Count == GameData.Equipment.slots.Count && worn.Count > 0)
            {
                var same = worn.All(it => it.grade == worn[0].grade);
                if (same)
                {
                    var i = b.equipSetSameGrades.IndexOf(worn[0].grade);
                    setPct = i >= 0 && i < b.equipSetSameValues.Count ? b.equipSetSameValues[i] : b.equipSetAny;
                    setName = $"{worn[0].grade}급 풀세트";
                }
                else
                {
                    setPct = b.equipSetAny;
                    setName = "4부위 착용";
                }
                atk += setPct; hp += setPct; skill += setPct; speed += setPct;
            }

            return new Bonus(atk, hp, skill, speed, setPct, setName);
        }

        // ------------------------------------------------------------------ gold

        /// <summary>Priced in "kills worth of gold" at the player's best stage, like every other
        /// gold sink, so it stays meaningful however deep the run gets.</summary>
        public static int UpgradeCost(PlayerState p, EquipItem it)
        {
            var b = GameData.Balance;
            if (it == null || it.lv >= b.equipMaxLevel) return 0;
            var kills = b.equipUpgradeGoldKills * Mathf.Pow(b.equipUpgradeGrowth, it.lv);
            return Mathf.Max(1, StatMath.Sat((double)StatMath.StageGold(Mathf.Max(1, p.maxCleared)) * kills));
        }

        public static bool Upgrade(PlayerState p, int itemId)
        {
            var it = p.Item(itemId);
            var cost = UpgradeCost(p, it);
            if (it == null || cost <= 0 || p.gold < cost) return false;
            p.gold -= cost;
            it.lv++;
            p.enhances++;
            return true;
        }

        public static int DismantleGold(PlayerState p, EquipItem it)
        {
            var b = GameData.Balance;
            if (it == null) return 0;
            var i = b.equipDismantleGrades.IndexOf(it.grade);
            var kills = i >= 0 && i < b.equipDismantleKills.Count ? b.equipDismantleKills[i] : 2;
            return Mathf.Max(1, StatMath.Sat((double)StatMath.StageGold(Mathf.Max(1, p.maxCleared)) * kills));
        }

        /// <summary>Breaks an item back into gold. Anything worn comes off first.</summary>
        public static int Dismantle(PlayerState p, int itemId)
        {
            var it = p.Item(itemId);
            if (it == null) return 0;
            var gold = DismantleGold(p, it);
            var holder = p.WornBy(itemId);
            if (!string.IsNullOrEmpty(holder)) p.SetWorn(holder, it.slot, 0);
            p.items.Remove(it);
            p.gold += gold;
            p.totalGold += gold;
            return gold;
        }
    }
}
