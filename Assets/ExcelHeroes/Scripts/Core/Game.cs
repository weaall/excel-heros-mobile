using System;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// The one place the UI reaches for session state. Holds the loaded database and the player's
    /// save, and raises Changed whenever something the UI shows has moved, so screens can refresh
    /// without polling.
    /// </summary>
    public static class Game
    {
        public static PlayerState Player { get; private set; }
        public static event Action Changed;

        static float _saveDue;

        public static void Boot()
        {
            GameData.Load();
            Player ??= SaveService.Load();
        }

        /// <summary>Call after mutating Player. Marks the save dirty and refreshes the UI.</summary>
        public static void Touch()
        {
            _saveDue = 2f;            // coalesce bursts of changes into one write
            Changed?.Invoke();
        }

        public static void Tick(float dt)
        {
            if (_saveDue <= 0f) return;
            _saveDue -= dt;
            if (_saveDue <= 0f) SaveService.Save(Player);
        }

        public static void FlushSave()
        {
            if (Player == null) return;
            _saveDue = 0f;
            SaveService.Save(Player);
        }

        /// <summary>Wipes the save and starts over. Used by the debug reset on the roster screen.</summary>
        public static void ResetProgress()
        {
            SaveService.Delete();
            Player = PlayerState.New();
            Touch();
        }
    }
}
