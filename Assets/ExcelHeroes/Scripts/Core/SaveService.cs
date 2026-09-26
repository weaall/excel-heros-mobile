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

        /// <summary>
        /// 세이브 내보내기 — the save as one line of text, ported from the web's `exportSave`.
        ///
        /// With cloud sync blocked on a mobile OAuth decision this is the ONLY way a save moves
        /// between devices, which is why it is worth having before the cloud is.
        ///
        /// Base64 of the JSON, same as the web, so a string exported there can be read here and
        /// the other way round. It is encoding and not encryption: anyone holding the string can
        /// read and edit it. That is the web build's choice too — it is the player's own save, and
        /// a player who wants to edit their single-player numbers can already edit the file.
        /// </summary>
        public static string Export(PlayerState state)
        {
            try
            {
                var json = JsonUtility.ToJson(state);
                return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] export failed: {e.Message}");
                return "";
            }
        }

        public readonly struct ImportResult
        {
            public readonly PlayerState State;
            public readonly string Error;
            public ImportResult(PlayerState state, string error) { State = state; Error = error; }
            public bool Ok => State != null;
        }

        /// <summary>
        /// Reads a string from Export back into a save — WITHOUT applying it. The caller decides
        /// whether to, because applying one throws away whatever is on this device and there is no
        /// undo; the roster, the stage and every card go at once.
        ///
        /// Everything about the input is treated as untrusted: it may be truncated, from another
        /// build, or simply not a save at all. A wrong string has to fail with a sentence the
        /// player can act on, never with a half-loaded state.
        /// </summary>
        public static ImportResult Import(string encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded)) return new ImportResult(null, "붙여넣은 코드가 없습니다");

            string json;
            try
            {
                json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded.Trim()));
            }
            catch
            {
                return new ImportResult(null, "코드 형식이 올바르지 않습니다");
            }

            PlayerState state;
            try { state = JsonUtility.FromJson<PlayerState>(json); }
            catch { return new ImportResult(null, "세이브를 읽을 수 없습니다"); }

            if (state == null) return new ImportResult(null, "세이브를 읽을 수 없습니다");

            // JsonUtility fills a struct-shaped object from almost anything, so "it parsed" proves
            // very little — and checking these for NULL proves even less, because the fields carry
            // initialisers and come back as empty lists rather than null no matter what was fed in.
            // `{"hello":1}` sailed through that check until a test tried it.
            //
            // The discriminator is the party: every save this game has ever written has its slots
            // pre-filled to partySize, empty strings included, from the moment it is created.
            // A zero-length party is not a save of this game in any version of it.
            if (state.party == null || state.owned == null || state.party.Count == 0)
                return new ImportResult(null, "이 게임의 세이브가 아닙니다");

            return new ImportResult(state, "");
        }

        /// <summary>Applies an imported save and writes it. Everything on this device is gone.</summary>
        public static void Apply(PlayerState state)
        {
            var size = Data.GameData.Balance?.partySize ?? 5;
            while (state.party.Count < size) state.party.Add("");
            while (state.party.Count > size) state.party.RemoveAt(state.party.Count - 1);
            if (!state.Owns(Data.GameData.MainId))
            {
                state.owned.Add(new OwnedHero(Data.GameData.MainId));
                var slot = state.party.IndexOf("");
                if (slot >= 0) state.party[slot] = Data.GameData.MainId;
            }
            if (string.IsNullOrEmpty(state.mainJob)) state.mainJob = "intern";

            Game.ReplaceState(state);   // in-memory swap; the write below is what makes it real
            Save(state);
        }

        public static void Delete()
        {
            try { if (File.Exists(Path)) File.Delete(Path); }
            catch (Exception e) { Debug.LogError($"[Save] delete failed: {e.Message}"); }
        }
    }
}
