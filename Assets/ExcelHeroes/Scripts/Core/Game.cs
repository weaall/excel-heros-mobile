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

        /// <summary>
        /// Swaps the live save IN MEMORY, without writing anything.
        ///
        /// Two callers, both of which genuinely replace the whole state rather than change part of
        /// it: the screenshot driver, so a capture run photographs a known state instead of
        /// whatever the last run left behind, and 세이브 불러오기, which then writes the result
        /// itself. Nothing here touches the disk, so a caller that wants the swap to persist has
        /// to say so.
        /// </summary>
        public static void ReplaceState(PlayerState state) => Player = state;
        public static event Action Changed;

        static float _saveDue;

        /// <summary>
        /// What the party earned while the game was shut, worked out once at boot and held for
        /// whoever shows it. It has to be taken here: the timestamp it reads is overwritten the
        /// first time anything saves, and the first save happens well before a screen exists.
        /// </summary>
        public static IdleService.Report Idle { get; private set; }

        public static void Boot()
        {
            GameData.Load();
            if (Player == null)
            {
                Player = SaveService.Load();
                Idle = IdleService.SinceLastSeen(Player);
            }
        }

        /// <summary>Takes the boot report, so it is paid once and never offered twice.</summary>
        public static IdleService.Report TakeIdle()
        {
            var r = Idle;
            Idle = default;
            return r;
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
