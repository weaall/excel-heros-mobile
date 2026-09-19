using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 스킨 — alternate looks, ported from the web build's `skinsOf` / `unlockSkin` / `equipSkin`
    /// in `GameManager.js` and the table in `src/data/skins.js`.
    ///
    /// Two per hero, and the two unlock in completely different ways on purpose: 퇴근 사복 is the
    /// reward at the top of 호감도, so it cannot be bought at any price, and 회사 정장 is bought
    /// with gems, so it cannot be earned by playing. One is the end of a relationship track and the
    /// other is a shop item; collapsing them into one currency would lose that.
    ///
    /// A skin is not a recolour. The web's paper doll changes CLOTHES for it — 퇴근 사복 is a hoodie
    /// or a cardigan, not the same suit in another palette — so both the battle sprite and the card
    /// illustration are separate baked files (`&lt;hero&gt;__casual`, `&lt;hero&gt;__formal`) rather than
    /// anything tinted at runtime.
    /// </summary>
    public static class SkinService
    {
        /// <summary>
        /// The skins defined for a hero, keyed on the DEFINITION id.
        ///
        /// For the 55 recruited heroes the save id and the definition id are the same, so this is
        /// a lookup. The main hero is the exception: their definition comes from the job they hold
        /// and changes on every promotion, so their skins are filed under 인턴 / 사원 / 영업부장 and
        /// not under "main". The promotion track is not ported yet — the save has no job field —
        /// so `GameData.Hero("main")` finds nothing and they get an empty list, which is the
        /// truthful answer until the track lands. The 22 job skin strips are already baked.
        /// </summary>
        public static List<SkinDef> For(string heroId)
        {
            var defId = GameData.Hero(heroId)?.id ?? heroId;
            return GameData.SkinDefs.Where(s => s.heroDefId == defId).ToList();
        }

        public static SkinState State(PlayerState p, string heroId)
        {
            var st = p.skins.FirstOrDefault(s => s.heroId == heroId);
            if (st == null) { st = new SkinState { heroId = heroId }; p.skins.Add(st); }
            return st;
        }

        public static bool Owns(PlayerState p, string heroId, string skinId) =>
            State(p, heroId).owned.Contains(skinId);

        /// <summary>The equipped skin's id, or "" for the hero's own look.</summary>
        public static string Active(PlayerState p, string heroId)
        {
            var active = State(p, heroId).active ?? "";
            // A skin that is equipped but not owned cannot happen through the UI; it can happen
            // through an edited save or a rolled-back build, and it would dress a hero in something
            // they never unlocked. Treat it as unequipped rather than trusting it.
            return Owns(p, heroId, active) ? active : "";
        }

        /// <summary>Why this skin is not available yet — empty when it is.</summary>
        public static string Blocked(PlayerState p, string heroId, SkinDef sk)
        {
            if (sk == null) return "없는 스킨입니다";
            if (Owns(p, heroId, sk.id)) return "";

            var owned = p.Find(heroId);
            if (owned == null) return "보유하지 않은 사원입니다";

            if (sk.unlockAffection > 0)
                return owned.affection >= sk.unlockAffection ? "" : $"호감도 Lv {sk.unlockAffection} 필요";

            if (sk.unlockGems > 0)
                return p.gems >= sk.unlockGems ? "" : $"보석 {sk.unlockGems} 필요";

            return "";
        }

        public static bool CanUnlock(PlayerState p, string heroId, SkinDef sk) =>
            sk != null && !Owns(p, heroId, sk.id) && Blocked(p, heroId, sk).Length == 0;

        /// <summary>
        /// Unlock a skin. An affection skin is free once the level is reached — the levelling was
        /// the price — and a gem skin charges. Returns false and takes nothing if it cannot.
        /// </summary>
        public static bool Unlock(PlayerState p, string heroId, string skinId)
        {
            var sk = For(heroId).FirstOrDefault(s => s.id == skinId);
            if (!CanUnlock(p, heroId, sk)) return false;

            if (sk.unlockGems > 0) p.gems -= sk.unlockGems;
            State(p, heroId).owned.Add(sk.id);
            return true;
        }

        /// <summary>Equip an owned skin, or pass null/"" to go back to the hero's own look.</summary>
        public static bool Equip(PlayerState p, string heroId, string skinId)
        {
            var st = State(p, heroId);
            if (!string.IsNullOrEmpty(skinId) && !st.owned.Contains(skinId)) return false;
            st.active = string.IsNullOrEmpty(skinId) ? null : skinId;
            return true;
        }

        /// <summary>The art key for whatever this hero is wearing: "acct_lead__casual", or the
        /// plain id when nothing is equipped. Both the card and the sprite are baked under it.</summary>
        public static string ArtKey(PlayerState p, string heroId)
        {
            var defId = GameData.Hero(heroId)?.id ?? heroId;
            var active = Active(p, heroId);
            return string.IsNullOrEmpty(active) ? defId : $"{defId}__{active}";
        }
    }
}
