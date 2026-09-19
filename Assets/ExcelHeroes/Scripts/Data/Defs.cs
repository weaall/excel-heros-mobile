using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.Data
{
    // Plain serializable mirrors of the JSON that tools/export-data.mjs writes out of the web build.
    // Unity's JsonUtility cannot deserialise a top-level array, which is why every list arrives
    // wrapped in an object with an `items` field.

    [Serializable]
    public class Wrapper<T>
    {
        public List<T> items = new();
    }

    [Serializable]
    public class GradeDef
    {
        public string id;       // D C B A S
        public string name;
        public string label;    // 일반 / 희귀 / 고급 / 영웅 / 전설
        public float rate;      // gacha probability, 0..1
        public int[] promote;   // cards needed for ★2..★5
        public string color;    // hex, used for frames and rarity text
        public string bg;       // hex, card background tint
        public int baseAtk;
        public int baseHp;

        public Color Color => Palette.Hex(color, UnityEngine.Color.white);
        public Color Bg => Palette.Hex(bg, new Color(1f, 1f, 1f, 0.06f));
    }

    [Serializable]
    public class RoleDef
    {
        public string id;       // tank melee ranged healer
        public string name;
        public float atk;       // multiplier on the grade's base attack
        public float hp;        // multiplier on the grade's base health
        public float interval;  // seconds between basic attacks
        public float range;     // in battle cells
    }

    [Serializable]
    public class SkillDef
    {
        public string id;
        public string name;
        public string desc;     // {p} is replaced with the hero's skill power
        public float cooldown;
        public float duration;
    }

    [Serializable]
    public class TraitDef
    {
        public string id;
        public string name;
        public string desc;
        public float value;
    }

    [Serializable]
    public class HeroDef
    {
        public string id;
        public string name;
        public string grade;
        public string role;
        public string trait;
        public string skillType;
        public float skillPower;
        public string skillName;
        public string division;
        public string nick;     // 만년 인턴, 카운터의 여왕 …
        public string dept;
        public string gender;   // M / F
        public string bio;
        public string line;     // spoken on the gacha reveal
        public string ult;      // spoken when the skill fires
        public string colorHair;
        public string colorBody;
        public string colorPants;
        public string colorAccent;
    }

    [Serializable]
    public class MainJobDef
    {
        public string id;
        public int tier;
        public string grade;
        public string name;
        public string title;
        public string track;
        public string role;
        public string trait;
        public string skillType;
        public float skillPower;
        public string skillName;
        public string[] next;
    }

    [Serializable]
    public class MainJobFile
    {
        public string mainId;
        public List<MainJobDef> items = new();
    }

    [Serializable]
    public class DivisionDef
    {
        public string id;
        public string name;
        public string color;

        public Color Color => Palette.Hex(color, UnityEngine.Color.gray);
    }

    [Serializable]
    public class PerkDef
    {
        public string id;       // division id
        public string key;      // gold regen revive boss cooldown crit skill
        public float value;
        public string desc;
    }

    [Serializable]
    public class DivisionFile
    {
        public List<DivisionDef> items = new();
        public List<PerkDef> perks = new();
        public float pairAtk, pairHp, trioAtk, trioHp, balancedHp;
    }

    [Serializable]
    public class StoryLine
    {
        public string who;      // hero id, "main", or "sys"
        public string text;
    }

    [Serializable]
    public class EpisodeDef
    {
        public string id;
        public int phase;
        public string title;
        public string room;     // the chat room the log came from
        public List<StoryLine> lines = new();
    }

    /// <summary>
    /// 오늘의 픽업. A featured S and A rotate through the whole roster, three days each, the same
    /// for everyone. That turns "I want her" from praying at a 0.5% rate into "come back on her
    /// banner" — and 모집 포인트 make even a failed chase converge on the card eventually.
    /// </summary>
    [Serializable]
    public class PickupOrder
    {
        public string grade;
        public List<string> ids = new();   // the fixed rotation the web build walks, one step per banner
    }

    [Serializable]
    public class PickupFile
    {
        public float rate;      // chance a pull of that grade lands on the featured card
        public int days;        // banner length
        public int sparkS, sparkA;
        public List<PickupOrder> orders = new();
    }

    /// <summary>One daily task. `goldKills` is priced in kills-worth-of-gold at the player's stage,
    /// so a reward stays meaningful however deep they are.</summary>
    [Serializable]
    public class QuestDef
    {
        public string id;
        public string name;
        public string desc;
        public int target;
        public int gems;
        public int goldKills;
    }

    [Serializable]
    public class QuestFile
    {
        public int perDay;
        public int loginGems, loginGoldKills;
        public int streakGemsPerDay, streakMaxDays;
        public int allClearGems;
        public List<QuestDef> items = new();
    }

    /// <summary>호감도 unlocks: a second bio line at Lv3 and a private message at Lv5.</summary>
    [Serializable]
    public class AffectionText
    {
        public string id;
        public string secret;
        public string line2;
    }

    /// <summary>A regular wave enemy — a spreadsheet error given a shape and a face.</summary>
    [Serializable]
    public class MonsterTypeDef
    {
        public string id;
        public string name;
        public string shape;
    }

    /// <summary>
    /// A boss's scripted move. `every` counts that boss's own basic attacks: every 2nd attack from
    /// 인사팀 채용 공고 calls in reinforcements, and so on. This is what makes each phase's boss feel
    /// like a puzzle rather than a bigger health bar.
    /// </summary>
    [Serializable]
    public class BossSpecialDef
    {
        public int every;
        public string kind;   // volley sweep stomp throw slow summon heal shield
        public string name;   // shouted on screen when it fires
        public string desc;
    }

    [Serializable]
    public class BossDef
    {
        public string id;
        public string name;
        public string desc;
        public float hp;        // multipliers on the stage's baseline monster stats
        public float atk;
        public float interval;
        public List<BossSpecialDef> specials = new();
    }

    /// <summary>
    /// One page of the opening: a backdrop and a few lines of narration. Shown once on a new save
    /// and re-readable from the 사내_메신저 sheet afterwards.
    /// </summary>
    [Serializable]
    public class PrologueScene
    {
        public string id;
        public string title;
        public List<string> lines = new();
    }

    [Serializable]
    public class PrologueFile { public List<PrologueScene> items = new(); }

    [Serializable]
    public class MonsterFile
    {
        public List<MonsterTypeDef> items = new();
        public List<BossDef> bosses = new();
    }

    [Serializable]
    public class BalanceDef
    {
        public float upgradeCostBase, upgradeCostGrowth;
        public float monsterHpBase, monsterHpGrowth;
        public float goldBase, goldGrowth;
        public int gachaSingleCost, gachaTenCost;
        public int pityA, pityS;
        public int duplicateShardsMin, duplicateShardsMax, unlockShards;
        public int startingGold, startingGems;
        public int partySize, maxStar, skillUnlockStar, skillBoostStar;
        public int storyGems;
        public float heroAtkGrowth, heroHpGrowth;
        public float[] starMult;
        public int[] levelCapByStar;
        public float enhancePerLevel;
        public float traitPerStar;
        public int monsterAtkRampFull, monsterAtkRampByStage;
        public int affectionMax;
        public float affectionXpBase, affectionXpGrowth;
        public int affectionXpPerKill, affectionXpPerBoss, affectionGiftXp, affectionGiftGoldKills;
        public float affectionBonusPerLevel;
        public int affectionUnlockSecret, affectionUnlockLine;
        public List<TeamUpgradeDef> teamUpgrades = new();
        public float gemDropBase;
        public float comboPerHit, comboMax, comboDecay;
        public float tankChance, tankChancePerStar, tankChanceMax;
        public float tankReduce, tankReducePerStar, tankReduceMax, tankSaveCd;
        public long offlineCapSec;
        public float offlineEfficiency;
        public long offlineMinSec;
        public int adPerDay;
        public int prestigeMinCleared;
        public float prestigeBonusPerShare;
        public float equipDropChance, equipBossDropChance, equipPctPerLevel, equipUpgradeGrowth;
        public int equipBossRolls, equipBossFirstRolls, equipMaxLevel, equipUpgradeGoldKills;
        public int equipInventoryMax, equipSetAny;
        public string equipBossFirstMinGrade;
        public List<string> equipDismantleGrades = new();
        public List<int> equipDismantleKills = new();
        public List<string> equipSetSameGrades = new();
        public List<int> equipSetSameValues = new();
        public float overtimeDuration, overtimeElite, overtimeHpMult, overtimeGemsPerPhase;
        public int overtimeStageOffset, overtimeCount, overtimeGemsPerKill, overtimeGemsPerElite;
        public int overtimeMaxGems, overtimeCardsPerPhase;
        public float dispatchHours;
        public int dispatchSlots, dispatchMaxPerDay, dispatchGemsBase, dispatchCardsPerPhase, dispatchAffectionXp;
        public List<string> dispatchGemGrades = new();
        public List<int> dispatchGemValues = new();
        public int bossEvery;
        public List<AdOfferDef> adOffers = new();
    }

    /// <summary>비품 슬롯 — one per stat, so a full set reads as "this one is built for attack".</summary>
    [Serializable]
    public class EquipSlotDef
    {
        public string id;       // keyboard chair monitor badge
        public string name;
        public string stat;     // atk hp skill speed
        public string label;
        public string desc;
    }

    /// <summary>The item's name for a slot at a grade — the same object, one rung up the ladder.</summary>
    [Serializable]
    public class EquipNameDef
    {
        public string slot;
        public string grade;
        public string name;
    }

    [Serializable]
    public class EquipBaseDef
    {
        public string grade;
        public float pct;
    }

    [Serializable]
    public class EquipmentFile
    {
        public List<EquipSlotDef> slots = new();
        public List<EquipNameDef> names = new();
        public List<EquipBaseDef> basePct = new();
    }

    /// <summary>
    /// One rewarded-ad offer. Every one of them pays a FLAT amount — never a multiplier on what
    /// the run earned while away, because a reward that scales with time spent not playing makes
    /// leaving the game closed the better play.
    /// </summary>
    [Serializable]
    public class AdOfferDef
    {
        public string id;       // gold gems cards dispatch overtime
        public string name;
        public string desc;
        public int perDay;
        public float hours;     // gold: how many hours of the current rate it pays
        public int amount;      // gems / cards: the flat number
    }

    /// <summary>
    /// One office upgrade — the gold sink that runs alongside the auto-battle. Straight out of the
    /// web build's BALANCE.TEAM_UPGRADES: a flat per-level bonus, a geometric price, and a cap.
    /// </summary>
    [Serializable]
    public class TeamUpgradeDef
    {
        public string id;       // coffee payroll chairs sales
        public string name;
        public string desc;
        public float per;       // bonus added per level
        public int baseCost;    // renamed on load: `base` is a C# keyword
        public float growth;
        public int max;
        public string unit;     // "pct" formats the bonus as a percentage point
    }

    /// <summary>
    /// 업적 — cumulative and tiered, never reset. `stat` names which running total it reads; the
    /// Unity side resolves that in AchievementService because several of them are not plain
    /// counters (owned cards, bestiary entries, equipment held).
    /// </summary>
    [Serializable]
    public class AchievementDef
    {
        public string id;
        public string name;
        public string desc;
        public string stat;
        public long[] tiers;
        public int[] gems;
        public string unit;     // "time" formats the value as hours rather than as a count
    }

    /// <summary>
    /// 마일스톤 — a one-time reward for passing a mark. Flattened out of the web build's procedural
    /// list rather than re-derived here, because two generators drift the first time either changes.
    /// </summary>
    [Serializable]
    public class MilestoneDef
    {
        public string id;
        public string kind;     // stage | level | party
        public int target;
        public string name;
        public string desc;
        public int gems;
        public int cards;
    }

    /// <summary>Hex string to Color, tolerant of missing or malformed values in the data files.</summary>
    public static class Palette
    {
        public static Color Hex(string hex, Color fallback)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return fallback;
        }
    }
}
