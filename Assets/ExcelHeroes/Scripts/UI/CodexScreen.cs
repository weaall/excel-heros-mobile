using System.Linq;
using System.Collections.Generic;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 오류 도감 — the bestiary, as a gallery of cards.
    ///
    /// It used to be a spreadsheet table, because the whole game used to be disguised as one. With
    /// the disguise gone a table of three text columns was the last screen that still looked like
    /// a document, and the reference puts everything collectable in a grid of portraits. So each
    /// entry is a card carrying the thing it is actually about: the sprite you fight.
    ///
    /// An entry unlocks by being reached rather than by being counted. Per-kill tallies need save
    /// state this build does not keep yet, and a grid of zeroes reads worse than no number.
    /// </summary>
    public class CodexScreen : IScreen
    {
        public string Cell => "A2";
        public string Formula => "=COUNTIF(오류_로그!D:D,\"처리됨\")";

        readonly AppRoot _app;
        VisualElement _root;

        /// <summary>One card: the codex mixes minions and bosses, so a card works on what it
        /// shows rather than on either definition type.</summary>
        readonly struct Entry
        {
            public readonly string Name, SpriteId, Kind, Phase;
            public readonly bool Known, Boss;
            public Entry(string name, string spriteId, string kind, string phase, bool known, bool boss)
            { Name = name; SpriteId = spriteId; Kind = kind; Phase = phase; Known = known; Boss = boss; }
        }

        Pages<Entry> _pages;
        Label _count;

        public CodexScreen(AppRoot app) => _app = app;

        /// <summary>
        /// The tileset's shape names are ids, not words: "blob", "cube", "spike". They were being
        /// printed straight into the 분류 column in English on an otherwise Korean screen.
        /// </summary>
        static string KindName(string shape) => shape switch
        {
            "blob" => "점착형",
            "cube" => "각형",
            "spike" => "가시형",
            "diamond" => "결정형",
            "ghost" => "잔상형",
            "bug" => "오류형",
            "sheet" => "문서형",
            "chart" => "도표형",
            "cloud" => "구름형",
            "cursor" => "포인터형",
            "hourglass" => "지연형",
            "lock" => "잠금형",
            "bull" => "돌진형",
            "monkey" => "난동형",
            _ => "기타",
        };

        public VisualElement Build()
        {
            _root = UiKit.Div("codex");

            var head = UiKit.Div("sheet-head", _root);
            _count = UiKit.Text("", "sheet-head__stat", head);
            UiKit.Text("지나온 구간에서 만난 오류가 기록됩니다", "muted", head);

            // Ten cards is what the frame holds in two rows of five without a scroll.
            _pages = new Pages<Entry>(_root, "codex-grid", 10).Empty("아직 만난 오류가 없습니다");
            Refresh();
            return _root;
        }

        public void Refresh()
        {
            if (_pages == null) return;

            var stage = Game.Player.stage;
            var types = GameData.MonsterTypes;
            var entries = new List<Entry>();
            var found = 0;

            for (var i = 0; i < types.Count; i++)
            {
                // Types are listed roughly in the order they show up, so the first few are always
                // known and the rest fill in as the player gets further along.
                var firstSeen = 1 + i * 2;
                var known = stage >= firstSeen;
                if (known) found++;
                entries.Add(new Entry(known ? types[i].name : "— 미발견 —", types[i].id,
                                      KindName(types[i].shape), $"Phase {firstSeen}", known, false));
            }

            // Bosses are listed in phase order and one guards the end of each phase, so a boss's
            // index is the phase you meet it in — there is no phase field on the definition itself.
            for (var i = 0; i < GameData.Bosses.Count; i++)
            {
                var phase = i + 1;
                var known = stage >= phase;
                if (known) found++;
                entries.Add(new Entry(known ? GameData.Bosses[i].name : "— 미발견 —",
                                      GameData.Bosses[i].id, "대용량 수식", $"Phase {phase}", known, true));
            }

            _pages.Fill(entries, (e, body) =>
            {
                var card = UiKit.Div("mcard", body);
                card.EnableInClassList("mcard--locked", !e.Known);
                card.EnableInClassList("mcard--boss", e.Boss && e.Known);

                var art = UiKit.Div("mcard__art", card);
                if (e.Known)
                {
                    // The same 3D model the fight uses, rendered once into a picture.
                    var shape = GameData.MonsterTypes?.FirstOrDefault(t => t.id == e.SpriteId)?.shape ?? "blob";
                    var snap = World.Snapshot3D.Monster(e.SpriteId, shape, e.Boss, () => art.MarkDirtyRepaint());
                    if (snap != null) art.style.backgroundImage = new StyleBackground(Background.FromTexture2D(snap));
                    else
                    {
                        var sprite = GameData.MonsterSprite(e.SpriteId);
                        if (sprite != null) UiKit.SetArt(art, sprite);
                    }
                }
                else UiKit.Text("?", "mcard__unknown", art);

                UiKit.Text(e.Name, "mcard__name", card);
                if (e.Known)
                {
                    // what it is as a small tag, where it lives beside it (ui_score 14-Codex #2)
                    var meta = UiKit.Div("mcard__metarow", card);
                    var kind = UiKit.Text(e.Boss ? "BOSS" : e.Kind, "mcard__kind" + (e.Boss ? " mcard__kind--boss" : ""), meta);
                    UiKit.Text(e.Phase, "mcard__phase", meta);
                }
                else UiKit.Text("UNKNOWN", "mcard__unknown-en", card);
            });

            _count.text = $"<size=55%><color=#00D2FF>DISCOVERED</color></size>   <color=#FFFFFF>{found}</color> <size=70%><color=#7E95B3>/ {types.Count + GameData.Bosses.Count}</color></size>";
        }
    }
}
