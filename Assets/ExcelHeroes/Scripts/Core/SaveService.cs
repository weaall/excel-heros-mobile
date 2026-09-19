using System;
using System.IO;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// Local save only, by design for this slice: one JSON file under persistentDataPath, written
    /// atomically through a temp file so a kill mid-write cannot leave a truncated save behind.
    /// Cloud sync is a later milestone — nothing here assumes a server.
    /// </summary>
    public static class SaveService
    {
        const string FileName = "excel-heroes-save.json";
        static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        public static PlayerState Load()
        {
            try
            {
                if (!File.Exists(Path)) return PlayerState.New();
                var json = File.ReadAllText(Path);
                var state = JsonUtility.FromJson<PlayerState>(json);
                if (state == null) return PlayerState.New();

                // A save written before the party size changed would otherwise field the wrong count.
                var size = Data.GameData.Balance?.partySize ?? 5;
                while (state.party.Count < size) state.party.Add("");
                while (state.party.Count > size) state.party.RemoveAt(state.party.Count - 1);

                // Saves written before 승진 landed have no main hero at all. Give them one rather
                // than leaving the player without the character the game is about; an empty first
                // slot has no way to be filled, because 김인턴 is never in the recruit pool.
                if (!state.Owns(Data.GameData.MainId))
                {
                    state.owned.Add(new OwnedHero(Data.GameData.MainId));
                    var slot = state.party.IndexOf("");
                    if (slot >= 0) state.party[slot] = Data.GameData.MainId;
                }
                if (string.IsNullOrEmpty(state.mainJob)) state.mainJob = "intern";

                return state;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] load failed, starting fresh: {e.Message}");
                return PlayerState.New();
            }
        }

        public static void Save(PlayerState state)
        {
            try
            {
                state.lastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var tmp = Path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(state));
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(tmp, Path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] write failed: {e.Message}");
            }
        }

        public static void Delete()
        {
            try { if (File.Exists(Path)) File.Delete(Path); }
            catch (Exception e) { Debug.LogError($"[Save] delete failed: {e.Message}"); }
        }
    }
}
