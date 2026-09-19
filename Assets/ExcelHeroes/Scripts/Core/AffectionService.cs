using System;
using System.Linq;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 호감도. Fighting alongside a card raises it; a 간식 bought with gold raises it faster. Each
    /// level is a small stat bonus, but the real payoff is text: an office secret at Lv3 and a
    /// private message at Lv5, written per hero in the web build.
    ///
    /// This is the axis that makes a D-rank card you have fielded for fifty stages worth more to
    /// you than a fresh S — which is the whole reason a collection game keeps anyone around.
    /// </summary>
    public static class AffectionService
    {
        /// <summary>XP needed to go from `level` to the next one. Grows 1.45x a level.</summary>
        public static int XpForNext(int level)
        {
            var b = GameData.Balance;
            if (level >= b.affectionMax) return 0;
            return Math.Max(1, (int)MathF.Floor(b.affectionXpBase * MathF.Pow(b.affectionXpGrowth, Math.Max(0, level))));
        }

        public static bool AtMax(OwnedHero o) => o.affection >= GameData.Balance.affectionMax;

        /// <summary>Adds XP and rolls any levels it earns. Returns how many levels were gained.</summary>
        public static int AddXp(OwnedHero o, int xp)
        {
            if (xp <= 0 || AtMax(o)) return 0;
            o.affectionXp += xp;

            var gained = 0;
            while (!AtMax(o))
            {
                var need = XpForNext(o.affection);
                if (need <= 0 || o.affectionXp < need) break;
                o.affectionXp -= need;
                o.affection++;
                gained++;
            }
            if (AtMax(o)) o.affectionXp = 0;
            return gained;
        }

        /// <summary>Gold price of one 간식, priced in "kills worth of gold" at the player's stage.</summary>
        public static int GiftCost(PlayerState p) =>
            Math.Max(1, StatMath.StageGold(Math.Max(1, p.stage)) * GameData.Balance.affectionGiftGoldKills);

        /// <summary>Buys a snack for one hero. Returns levels gained, or -1 when it could not be paid for.</summary>
        public static int Gift(PlayerState p, OwnedHero o)
        {
            if (AtMax(o)) return -1;
            var cost = GiftCost(p);
            if (p.gold < cost) return -1;
            p.gold -= cost;
            return AddXp(o, GameData.Balance.affectionGiftXp);
        }

        /// <summary>
        /// Paid out after a battle: everyone who fought gets XP per kill, more for a boss. Called
        /// once on victory rather than per swing, so a long fight is not worth more than a clean one.
        /// </summary>
        public static void AwardBattle(PlayerState p, int kills, bool clearedBoss)
        {
            var b = GameData.Balance;
            var xp = kills * b.affectionXpPerKill + (clearedBoss ? b.affectionXpPerBoss : 0);
            if (xp <= 0) return;
            foreach (var o in p.PartyMembers()) AddXp(o, xp);
        }

        public static bool SecretUnlocked(OwnedHero o) => o.affection >= GameData.Balance.affectionUnlockSecret;
        public static bool LineUnlocked(OwnedHero o) => o.affection >= GameData.Balance.affectionUnlockLine;

        /// <summary>The greeting the home screen's lead card says, scaled to how well you know them.</summary>
        public static string Greeting(OwnedHero o)
        {
            var def = GameData.Hero(o?.id);
            if (def == null) return "";
            var extra = GameData.Affection(def.id);
            if (o != null && LineUnlocked(o) && !string.IsNullOrEmpty(extra?.line2)) return extra.line2;
            return def.line;
        }
    }
}
