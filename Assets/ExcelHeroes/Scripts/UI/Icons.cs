namespace ExcelHeroes.UI
{
    /// <summary>
    /// The icon set, as characters.
    ///
    /// Everything with a glyph in this UI used to be either a PNG I drew by hand or whatever symbol
    /// Unity's default font happened to have — which is why several of them rendered as □ and the
    /// rest looked drawn by someone in a hurry. This is Material Symbols, Google's own set, under
    /// Apache 2.0: the same icons Excel for Android is drawn with, which makes it the right set for
    /// a game pretending to be Excel for Android rather than merely a better-looking one.
    ///
    /// They are text, not images. A variable font ships one file for four thousand icons, scales to
    /// any size without a second asset, and takes its colour from `color` like any other label — so
    /// an icon that has to be white on the app bar and grey in the tab strip is one glyph, not two
    /// PNGs. The codepoints come from the set's own .codepoints file, kept beside the font.
    /// </summary>
    public static class Icons
    {
        // --- chrome -------------------------------------------------------------------------
        public const string Back = "";        // arrow_back
        public const string Search = "";      // search
        public const string Undo = "";        // undo
        public const string Overflow = "";    // more_vert
        public const string Sheets = "";      // table_chart
        public const string Add = "";         // add
        public const string Close = "";       // close
        public const string Expand = "";      // expand_more
        public const string Check = "";       // check

        // 위장 — an eye, open and closed. The disguise is literally "do not look at this".
        public const string BossKey = "";     // visibility
        public const string BossKeyOn = "";   // visibility_off

        // --- sheets -------------------------------------------------------------------------
        public const string Battle = "";      // swords
        public const string Roster = "";      // group
        public const string Gacha = "";       // download
        public const string Tasks = "";       // checklist
        public const string Story = "";       // chat
        public const string Album = "";       // photo_library
        public const string Codex = "";       // bug_report
        public const string Chart = "";       // bar_chart

        // --- the game -----------------------------------------------------------------------
        public const string Gem = "";         // healing, stands in for the 보석 currency
        public const string Gold = "";        // paid
        public const string Star = "";        // star
        public const string Bolt = "";        // bolt, the skill charge
        public const string Shield = "";      // shield, the tank
        public const string Refresh = "";     // refresh
        public const string Upgrade = "";     // upgrade

        /// <summary>The lobby, which is where the reference puts its own home button.</summary>
        public const string Home = "";        // home
        public const string Settings = "";    // settings
    }
}
