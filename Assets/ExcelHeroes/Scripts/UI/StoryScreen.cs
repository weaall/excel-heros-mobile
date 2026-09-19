using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    /// <summary>
    /// 사내 메신저 — the story, told as an exported chat log, which is the joke the whole game runs
    /// on: even the plot is a spreadsheet export. Episodes unlock as the player pushes deeper, and
    /// reading one for the first time pays gems.
    /// </summary>
    public class StoryScreen : IScreen
    {
        public string Title => "사내 메신저";

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

            var head = UiKit.Div("panel", _root);
            UiKit.Text("에피소드", "section-title", head);
            UiKit.Text($"Phase {p.stage} 까지 진행 · 처음 읽으면 보석 {GameData.Balance.storyGems}개", "muted", head);

            var list = UiKit.Scroll(null, _root);
            foreach (var ep in GameData.Episodes.OrderBy(e => e.phase))
            {
                var unlocked = p.stage >= ep.phase;
                var read = p.readEpisodes.Contains(ep.id);
                var row = UiKit.Div("panel", list);

                UiKit.Text(ep.title, "section-title", row);
                UiKit.Text(unlocked ? ep.room : $"Phase {ep.phase} 도달 시 해금", "muted", row);

                if (!unlocked) continue;
                UiKit.Btn(read ? "다시 읽기" : $"읽기 (보석 +{GameData.Balance.storyGems})", "btn",
                    () => Open(ep), row);
            }
        }

        void Open(EpisodeDef ep)
        {
            var p = Game.Player;
            if (!p.readEpisodes.Contains(ep.id))
            {
                p.readEpisodes.Add(ep.id);
                p.gems += GameData.Balance.storyGems;
                Game.Touch();
            }

            var overlay = _app.Overlay;
            overlay.Clear();
            overlay.RemoveFromClassList("hidden");

            var view = UiKit.Div("detail");
            var body = UiKit.Scroll("detail__body", view);
            UiKit.Text(ep.title, "detail__name", body);
            UiKit.Text(ep.room, "chat-room", body);

            foreach (var line in ep.lines)
            {
                var isMine = line.who == GameData.MainId;
                var isSys = line.who == "sys";
                var row = UiKit.Div("chat-line " + (isMine ? "chat-line--mine" : isSys ? "chat-line--sys" : ""), body);

                var column = UiKit.Div(null, row);
                if (!isMine && !isSys)
                {
                    var speaker = GameData.Hero(line.who);
                    UiKit.Text(speaker?.name ?? line.who, "chat-who", column);
                }
                UiKit.Text(line.text, "chat-bubble", column);
            }

            UiKit.Btn("✕", "detail__close", () =>
            {
                overlay.Clear();
                overlay.AddToClassList("hidden");
                Refresh();
            }, view);

            overlay.Add(view);
        }
    }
}
