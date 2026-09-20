using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 사내 메신저 — the story, told as an exported chat log, which is the joke the whole game runs
    /// on: even the plot is a spreadsheet export. Episodes unlock as the player pushes deeper, and
    /// reading one for the first time pays gems.
    ///
    /// Every speaker wears their card art. Without it the log is a wall of names, and a game that
    /// generated 55 portraits was showing none of them where the writing actually lives — the cast
    /// is the reason these lines land, so the cast has to be on screen saying them.
    /// </summary>
    public class StoryScreen : IScreen
    {
        public string Cell => "A6";
        public string Formula => "=TEXTJOIN(CHAR(10),TRUE,수신_메일!B2:B40)";

        readonly AppRoot _app;
        VisualElement _root;

        public StoryScreen(AppRoot app) { _app = app; }

        public VisualElement Build()
        {
            _root = UiKit.Div("screen-body");
            Refresh();
            return _root;
        }

        public void Refresh()
        {
            if (_root == null) return;
            _root.Clear();
            var p = Game.Player;

            // One bar, not a bar and a line under it.
            //
            // `section-title` is a 66px navy bar now, and the muted line under it was another 40.
            // The band does not have 106px to spend on a caption: 820 less the pager leaves 750,
            // and two rows of the spec's 340 cards come to 712 before the caption is counted.
            // The count reads perfectly well inside the bar.
            var head = UiKit.Div("ep-head", _root);
            var read = GameData.Episodes.Count(e => p.readEpisodes.Contains(e.id));
            UiKit.Text($"에피소드 · {read} / {GameData.Episodes.Count} 읽음 · 처음 읽으면 보석 {GameData.Balance.storyGems}",
                "section-title", head);

            var pages = new Pages<EpisodeDef>(_root, "ep-grid", 6);
            pages.Fill(GameData.Episodes.OrderBy(e => e.phase).ToList(), (ep, list) =>
            {
                var unlocked = p.stage >= ep.phase;
                var wasRead = p.readEpisodes.Contains(ep.id);

                var row = UiKit.Div("panel ep", list);
                row.EnableInClassList("ep--locked", !unlocked);

                // The faces in this episode, so the list is browsable by "who is in it".
                var cast = UiKit.Div("ep__cast", row);
                foreach (var id in ep.lines.Select(l => l.who)
                             .Where(w => GameData.Hero(w) != null).Distinct().Take(6))
                {
                    var face = UiKit.Div("ep__face", cast);
                    if (unlocked) UiKit.SetArt(face, GameData.CardArt(id));
                }

                UiKit.Text(ep.title, "section-title", row);
                UiKit.Text(unlocked ? ep.room : $"Phase {ep.phase} 도달 시 해금", "muted", row);

                if (!unlocked) return;
                UiKit.Btn(wasRead ? "다시 읽기" : $"읽기 (보석 +{GameData.Balance.storyGems})", "btn",
                    () => Open(ep), row);
            });
        }

        void Open(EpisodeDef ep)
        {
            var p = Game.Player;
            if (!p.readEpisodes.Contains(ep.id))
            {
                p.readEpisodes.Add(ep.id);
                p.gems += GameData.Balance.storyGems;
                AudioService.Play("bond");
                Game.Touch();
            }
            else AudioService.Play("nav", 0.5f);

            var overlay = _app.Overlay;
            overlay.Clear();
            overlay.RemoveFromClassList("hidden");

            var view = UiKit.Div("detail chat");
            var body = UiKit.Scroll("detail__body", view);

            var header = UiKit.Div("chat__header", body);
            UiKit.Text(ep.title, "chat__title", header);
            UiKit.Text(ep.room, "chat-room", header);

            var previous = "";
            foreach (var line in ep.lines)
            {
                var isMine = line.who == GameData.MainId;
                var isSys = line.who == "sys";

                if (isSys)
                {
                    // System notices are the room talking, not a person — centred, no avatar.
                    var notice = UiKit.Div("chat-line chat-line--sys", body);
                    UiKit.Text(line.text, "chat-bubble", notice);
                    previous = "";
                    continue;
                }

                var row = UiKit.Div("chat-line " + (isMine ? "chat-line--mine" : ""), body);
                var speaker = GameData.Hero(line.who);
                // Consecutive lines from one person share an avatar, the way a real chat log reads.
                var repeat = line.who == previous;

                if (!isMine)
                {
                    var avatar = UiKit.Div("chat-avatar", row);
                    if (!repeat && speaker != null) UiKit.SetArt(avatar, GameData.CardArt(line.who));
                    else avatar.AddToClassList("chat-avatar--blank");
                }

                var column = UiKit.Div("chat-column", row);
                if (!isMine && !repeat)
                {
                    var name = UiKit.Text(speaker?.name ?? line.who, "chat-who", column);
                    var grade = GameData.Grade(speaker?.grade);
                    if (grade != null) name.style.color = grade.Color;
                }
                UiKit.Text(line.text, "chat-bubble", column);
                previous = line.who;
            }

            UiKit.Btn(Icons.Close, "detail__close icon", () =>
            {
                overlay.Clear();
                overlay.AddToClassList("hidden");
                Refresh();
            }, view);

            overlay.Add(view);
        }
    }
}
