using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>One owned card. Duplicates stack as `copies`, which is what buys the next ★.</summary>
    /// <summary>One piece of 비품 in the bag. Level is bought with gold; grade never changes.</summary>
    [Serializable]
    public class EquipItem
    {
        public int id;
        public string slot;
        public string grade;
        public int lv;
    }

    [Serializable]
    public class OwnedHero
    {
        public string id;
        public int star = 1;
        public int level = 1;
        public int copies;       // spare copies held towards the next promotion
        public int affection;    // 호감도 level, drives the small per-hero stat bonus
        public int affectionXp;  // progress towards the next level
        public bool isNew = true;

        public OwnedHero() { }
        public OwnedHero(string heroId) { id = heroId; }
    }

    /// <summary>Progress on one of today's tasks.</summary>
    [Serializable]
    public class QuestProgress
    {
        public string id;
        public int count;
        public bool claimed;
    }

    /// <summary>
    /// Everything the player owns. Serialised straight to JSON by SaveService — keep it to plain
    /// fields and lists so JsonUtility can round-trip it.
    /// </summary>
    [Serializable]
    public class PlayerState
    {
        public int version = 1;
        public int gems;
        public int gold;
        public long lastSeenUnix;

        public List<OwnedHero> owned = new();
        public List<string> party = new();      // hero ids, empty slots are ""
        public int pullsSinceA;                 // pity counters, see GachaService
        public int pullsSinceS;
        public int totalPulls;
        public int sparkPoints;                 // 모집 포인트 — 1 per pull, never expires
        public int stage = 1;                   // highest stage reached
        public string leadHeroId = "";          // the card that greets the player on the home screen
        public List<string> readEpisodes = new();

        // 일일 업무 — rolls over at local midnight, see QuestService.
        public string dailyDate = "";
        public List<QuestProgress> quests = new();
        public string checkInDate = "";
        public int streak;
        public bool allClearClaimed;

        /// <summary>
        /// 비품 — the bag, and what each hero is wearing.
        ///
        /// Items carry their own id because a hero refers to one by id: two identical keyboards
        /// are two objects, and upgrading the one on someone's desk must not upgrade the spare.
        /// The worn map is parallel lists of "heroId/slot" against item id, because JsonUtility
        /// cannot serialise a dictionary and cannot nest one either.
        /// </summary>
        public List<EquipItem> items = new();
        public int nextItemId = 1;
        public List<string> wornKeys = new();
        public List<int> wornItemIds = new();

        static string WornKey(string heroId, string slot) => heroId + "/" + slot;

        public int WornId(string heroId, string slot)
        {
            var i = wornKeys.IndexOf(WornKey(heroId, slot));
            return i >= 0 && i < wornItemIds.Count ? wornItemIds[i] : 0;
        }

        public void SetWorn(string heroId, string slot, int itemId)
        {
            var key = WornKey(heroId, slot);
            var i = wornKeys.IndexOf(key);
            if (itemId <= 0)
            {
                if (i < 0) return;
                wornKeys.RemoveAt(i);
                if (i < wornItemIds.Count) wornItemIds.RemoveAt(i);
                return;
            }
            if (i < 0) { wornKeys.Add(key); wornItemIds.Add(itemId); return; }
            while (wornItemIds.Count <= i) wornItemIds.Add(0);
            wornItemIds[i] = itemId;
        }

        /// <summary>Who is wearing an item, or "" when it is in the bag.</summary>
        public string WornBy(int itemId)
        {
            var i = wornItemIds.IndexOf(itemId);
            if (i < 0 || i >= wornKeys.Count) return "";
            var key = wornKeys[i];
            var slash = key.LastIndexOf('/');
            return slash > 0 ? key.Substring(0, slash) : "";
        }

        public EquipItem Item(int id) => items.FirstOrDefault(it => it.id == id);

        /// <summary>야근 모드 — done today, plus any extra runs bought with an ad. Both sit in
        /// the daily block and reset with it; `overtimeBest` is a lifetime record and does not.</summary>
        public bool overtimeDone;
        public int overtimeExtra;
        public int overtimeBest;

        /// <summary>
        /// 출장 — who is away, when they get back, and how many runs have been started today.
        /// `dispatchDate` is what makes the daily cap daily; the rest of the block outlives the
        /// day on purpose, because a run started at 23:50 must still come back at 03:50.
        /// </summary>
        public List<string> dispatchHeroIds = new();
        public long dispatchEndsUnix;
        public string dispatchDate = "";
        public int dispatchCount;
        public int dispatches;      // lifetime, for 업적

        /// <summary>광고 보상 — how many of each offer were taken today, as parallel lists.
        /// Cleared with the rest of the daily block at local midnight.</summary>
        public List<string> adIds = new();
        public List<int> adCounts = new();

        public int AdsUsed(string id)
        {
            var i = adIds.IndexOf(id);
            return i >= 0 && i < adCounts.Count ? adCounts[i] : 0;
        }

        public int AdsUsedTotal()
        {
            var n = 0;
            foreach (var c in adCounts) n += c;
            return n;
        }

        public void NoteAd(string id)
        {
            var i = adIds.IndexOf(id);
            if (i < 0) { adIds.Add(id); adCounts.Add(1); return; }
            while (adCounts.Count <= i) adCounts.Add(0);
            adCounts[i]++;
        }

        // 위장 모드. Persisted because someone who turns it on is at work and will still be at work
        // when they next open the app; coming back un-disguised is the one failure that matters.
        public bool stealth;

        /// <summary>The opening has been watched. It is re-readable from the 사내_메신저 sheet.</summary>
        public bool prologueSeen;

        /// <summary>
        /// 사무실 개선 levels, as parallel lists because JsonUtility cannot serialise a dictionary.
        /// Unknown ids read as 0, so adding an upgrade to the data never invalidates a save.
        /// </summary>
        public List<string> teamIds = new();
        public List<int> teamLevels = new();

        /// <summary>자동 전투 — on by default; the main sheet is an idle battle, not a menu.</summary>
        public bool autoSkill = true;

        // --- 누적 기록 ------------------------------------------------------------------
        //
        // The running totals the achievements read. They are counters rather than a derived view
        // on purpose: "how many monsters have you ever put down" cannot be recomputed from a save
        // that only knows the current stage, and an achievement that silently resets is worse than
        // no achievement.
        public long totalKills;
        public long totalGold;
        public int bossKills;
        public int chestsOpened;
        public int enhances;         // card upgrades bought, of any kind
        public int overtimes;        // 야근 모드 runs
        public long playSeconds;
        public int maxCleared;       // the deepest stage actually cleared, which `stage` is not

        /// <summary>업적 id -> how many tiers have been claimed.</summary>
        public List<string> achievementIds = new();
        public List<int> achievementTiers = new();

        /// <summary>마일스톤 ids already granted.</summary>
        public List<string> milestonesClaimed = new();

        /// <summary>강화 카드 — the currency milestones pay in, spent on card upgrades.</summary>
        public int cards;

        /// <summary>오류 도감 — monster type id -> kills. Elites are counted under "<id>!".</summary>
        public List<string> bestiaryIds = new();
        public List<int> bestiaryKills = new();

        public int AchievementTier(string id)
        {
            var i = achievementIds.IndexOf(id);
            return i >= 0 && i < achievementTiers.Count ? achievementTiers[i] : 0;
        }

        public void SetAchievementTier(string id, int tier)
        {
            var i = achievementIds.IndexOf(id);
            if (i < 0) { achievementIds.Add(id); achievementTiers.Add(tier); return; }
            while (achievementTiers.Count <= i) achievementTiers.Add(0);
            achievementTiers[i] = tier;
        }

        public void NoteKill(string typeId, bool elite)
        {
            if (string.IsNullOrEmpty(typeId)) return;
            Bump(typeId);
            if (elite) Bump(typeId + "!");

            void Bump(string key)
            {
                var i = bestiaryIds.IndexOf(key);
                if (i < 0) { bestiaryIds.Add(key); bestiaryKills.Add(1); return; }
                while (bestiaryKills.Count <= i) bestiaryKills.Add(0);
                bestiaryKills[i]++;
            }
        }

        /// <summary>Monster types seen at least once. Elite entries do not count twice.</summary>
        public int BestiaryCount() => bestiaryIds.Count(k => !k.EndsWith("!"));

        public int TeamLevel(string id)
        {
            var i = teamIds.IndexOf(id);
            return i >= 0 && i < teamLevels.Count ? teamLevels[i] : 0;
        }

        public void SetTeamLevel(string id, int level)
        {
            var i = teamIds.IndexOf(id);
            if (i < 0) { teamIds.Add(id); teamLevels.Add(level); return; }
            while (teamLevels.Count <= i) teamLevels.Add(0);
            teamLevels[i] = level;
        }

        public static PlayerState New()
        {
            var b = GameData.Balance;
            var s = new PlayerState
            {
                gems = b?.startingGems ?? 1000,
                gold = b?.startingGold ?? 0,
                lastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
            for (var i = 0; i < (b?.partySize ?? 5); i++) s.party.Add("");
            return s;
        }

        public OwnedHero Find(string heroId) => owned.FirstOrDefault(o => o.id == heroId);
        public bool Owns(string heroId) => Find(heroId) != null;

        /// <summary>Party members in slot order, skipping empty slots.</summary>
        public IEnumerable<OwnedHero> PartyMembers()
        {
            foreach (var id in party)
            {
                if (string.IsNullOrEmpty(id)) continue;
                var o = Find(id);
                if (o != null) yield return o;
            }
        }

        public int PartyCount() => party.Count(id => !string.IsNullOrEmpty(id));

        /// <summary>Puts a hero in the first free slot. Returns false when the party is full.</summary>
        public bool AddToParty(string heroId)
        {
            if (party.Contains(heroId)) return true;
            var i = party.IndexOf("");
            if (i < 0) return false;
            party[i] = heroId;
            return true;
        }

        public void RemoveFromParty(string heroId)
        {
            var i = party.IndexOf(heroId);
            if (i >= 0) party[i] = "";
        }
    }
}
