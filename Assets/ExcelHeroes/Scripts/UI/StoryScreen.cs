using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 사내 메신저 — the story, told as chat rooms, laid out the way the reference's MomoTalk is:
    /// one window with a pink header, the rooms down the left (a face, the title, the last line,
    /// an unread badge) and the open conversation on the right in bubbles — others dark slate with
    /// a name and a round avatar, the player's own blue on the right, notices as a centred pill.
    ///
    /// Episodes unlock as the player pushes deeper; opening one for the first time pays gems.
    /// Nothing is opened (or paid) until the player taps a room.
    /// </summary>
    public class StoryScreen : IScreen
    {
        public string Cell => "A6";
        public string Formula => "=TEXTJOIN(CHAR(10),TRUE,수신_메일!B2:B40)";

        readonly AppRoot _app;
        VisualElement _root;
        string _open;

        public StoryScreen(AppRoot app) { _app = app; }

        public VisualElement Build()
        {
            _root = UiKit.Div("screen-body mt-screen");
            Refresh();
            return _root;
        }

        public void Refresh()
        {
            if (_root == null) return;
            _root.Clear();
            var p = Game.Player;
            // Back where the player left off: the last room already read (opening it pays nothing).
            _open ??= GameData.Episodes.OrderBy(e => e.phase).LastOrDefault(e => p.readEpisodes.Contains(e.id))?.id;

            var win = UiKit.Div("mt", _root);
            ModalFrame.Painted(win, (ctx, r) =>
            {
                var body = UiPaint.RoundRect(r, 18f, 6);
                UiPaint.Shadow(ctx, body, new Vector2(0f, 8f), UiPaint.C(20, 40, 80, 0.22f), 18f);
                UiPaint.Fill(ctx, body, UiPaint.C(255, 255, 255));
                var head = UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + 92f), 18f, 6);
                UiPaint.Fill(ctx, head, UiPaint.Vertical(UiPaint.C(255, 160, 176), UiPaint.C(250, 128, 150), r.yMin, r.yMin + 92f));
                UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMin + 70f, r.xMax, r.yMin + 92f), 0f, 1),
                             UiPaint.C(250, 128, 150));
            });

            var head = UiKit.Div("mt__head", win);
            ModalFrame.Painted(UiKit.Div("mt__logo", head), (ctx, r) =>
            {
                // a speech bubble with three dots
                var c = r.center;
                UiPaint.Fill(ctx, UiPaint.Ellipse(c + new Vector2(0f, -3f), r.width * 0.46f, r.height * 0.38f), UiPaint.C(255, 255, 255));
                UiPaint.Fill(ctx, new System.Collections.Generic.List<Vector2>
                    { c + new Vector2(-r.width * 0.2f, r.height * 0.2f), c + new Vector2(-r.width * 0.32f, r.height * 0.46f), c + new Vector2(-r.width * 0.02f, r.height * 0.28f) },
                    UiPaint.C(255, 255, 255));
                for (var i = -1; i <= 1; i++)
                    UiPaint.Fill(ctx, UiPaint.Ellipse(c + new Vector2(i * r.width * 0.2f, -3f), 3.5f, 3.5f), UiPaint.C(250, 128, 150));
            });
            UiKit.Text("사내 메신저", "mt__title", head);
            UiKit.Div("spacer", head);
            var read = GameData.Episodes.Count(e => p.readEpisodes.Contains(e.id));
            UiKit.Text($"읽음 {read} / {GameData.Episodes.Count} · 처음 읽으면 보석 {GameData.Balance.storyGems}", "mt__count", head);

            var cols = UiKit.Div("mt__cols", win);
            var list = UiKit.Scroll("mt__list", cols);
            var chat = UiKit.Scroll("mt__chat", cols);

            foreach (var ep in GameData.Episodes.OrderBy(e => e.phase))
            {
                var unlocked = p.stage >= ep.phase;
                var wasRead = p.readEpisodes.Contains(ep.id);
                var row = UiKit.Div("mt-room", list);
                row.EnableInClassList("mt-room--locked", !unlocked);
                row.EnableInClassList("mt-room--on", ep.id == _open);

                var face = UiKit.Div("mt-room__face", row);
                var who = ep.lines.Select(l => l.who).FirstOrDefault(w => w != GameData.MainId && GameData.Hero(w) != null);
                if (unlocked && who != null) UiKit.SetArt(face, GameData.CardArt(who));

                var text = UiKit.Div("mt-room__text", row);
                UiKit.Text(ep.title, "mt-room__title", text);
                var last = ep.lines.LastOrDefault(l => l.who != "sys")?.text ?? ep.room;
                UiKit.Text(unlocked ? last : $"Phase {ep.phase} 도달 시 해금", "mt-room__last", text);

                if (unlocked && !wasRead)
                {
                    var badge = UiKit.Div("mt-room__badge", row);
                    ModalFrame.Painted(badge, (ctx, r) => UiPaint.Fill(ctx, UiPaint.Ellipse(r.center, r.width * 0.5f, r.height * 0.5f), UiPaint.C(250, 96, 120)));
                    UiKit.Text(Mathf.Min(99, ep.lines.Count).ToString(), "mt-room__badge-text", badge);
                }

                if (unlocked)
                {
                    var id = ep.id;
                    row.RegisterCallback<ClickEvent>(_ => Open(GameData.Episodes.First(e => e.id == id)));
                }
            }

            var open = GameData.Episodes.FirstOrDefault(e => e.id == _open);
            if (open == null)
            {
                var empty = UiKit.Div("mt__empty", chat);
                UiKit.Text("대화방을 선택하세요", "mt__empty-text", empty);
                return;
            }
            BuildChat(chat, open);
        }

        void Open(EpisodeDef ep)
        {
            var p = Game.Player;
            if (!p.readEpisodes.Contains(ep.id))
            {
                p.readEpisodes.Add(ep.id);
                p.gems += GameData.Balance.storyGems;
                AudioService.Play("bond");
                _app.SetStatus($"{ep.title} · 보석 +{GameData.Balance.storyGems}");
                Game.Touch();
            }
            else AudioService.Play("nav", 0.5f);
            _open = ep.id;
            Refresh();
        }

        static void BuildChat(ScrollView chat, EpisodeDef ep)
        {
            var header = UiKit.Div("mt-chat__head", chat);
            UiKit.Text(ep.title, "mt-chat__title", header);
            UiKit.Text(ep.room, "mt-chat__room", header);

            var previous = "";
            foreach (var line in ep.lines)
            {
                var isMine = line.who == GameData.MainId;
                if (line.who == "sys")
                {
                    var notice = UiKit.Div("mt-line mt-line--sys", chat);
                    UiKit.Text(line.text, "mt-sys", notice);
                    previous = "";
                    continue;
                }

                var row = UiKit.Div("mt-line" + (isMine ? " mt-line--mine" : ""), chat);
                var speaker = GameData.Hero(line.who);
                var repeat = line.who == previous;
                if (!isMine)
                {
                    var avatar = UiKit.Div("mt-avatar", row);
                    if (!repeat && speaker != null) UiKit.SetArt(avatar, GameData.CardArt(line.who));
                    else avatar.AddToClassList("mt-avatar--blank");
                }
                var column = UiKit.Div("mt-column", row);
                if (!isMine && !repeat) UiKit.Text(speaker?.name ?? line.who, "mt-who", column);
                var bubble = UiKit.Div("mt-bubble" + (isMine ? " mt-bubble--mine" : ""), column);
                UiKit.Text(line.text, "mt-bubble__text", bubble);
                previous = line.who;
            }
        }
    }
}
