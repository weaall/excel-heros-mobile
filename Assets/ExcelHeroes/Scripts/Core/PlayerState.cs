using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>One owned card. Duplicates stack as `copies`, which is what buys the next ★.</summary>
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
