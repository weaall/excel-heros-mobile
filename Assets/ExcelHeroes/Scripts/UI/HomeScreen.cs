using System;
using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.UI
{
    public class HomeScreen : IScreen
    {
        public string Cell => "A1";
        public string Formula => "=TODAY()";

        readonly AppRoot _app;
        VisualElement _root;
        VisualElement _charContainer;
        VisualElement _bubble;
        Label _whoLabel;
        Label _speechLabel;
        Label _bondLevel;
        VisualElement _bondFill;

        string _heroId;
        int _dialogueIndex = 0;
        List<string> _dialogues = new();

        public HomeScreen(AppRoot app) { _app = app; }

        public VisualElement Build()
        {
            _dots.Clear();
            _root = UiKit.Div("home");

            // No picture behind the lobby yet: home_bg was an orange sunset, and every reference
            // lobby is a bright office in daylight. The shell's painted sky shows through until
            // the generated backgrounds land (docs/HANDOFF.md, "backgrounds").

            _heroId = GetLeadHeroId();
            var def = GameData.Hero(_heroId);
            var p = Game.Player;
            var owned = p.owned.FirstOrDefault(x => x.id == _heroId) ?? new OwnedHero(_heroId);

            // ---- Left: Character Visual Element -------------------------------------------
            // The hero's sheet, behind the portrait and leaning out past one shoulder.
            // the halo sheet, drawn by the game behind the figure (v2 standing art is clean)
            if (def != null) BackSheet.Add(_root, def, owned, "backsheet home__sheet");
            Ambience(_root);
            _charContainer = UiKit.Div("home__char-container", _root);
            if (def != null)
            {
                // The transparent standing art when it exists: the character stands in the room
                // at full height, as on the reference lobby, with the sheet behind her. Until it
                // exists, the boxed card.
                var standing = GameData.StandingArt(_heroId);
                if (standing != null)
                {
                    _charContainer.AddToClassList("home__char-container--standing");
                    _charContainer.AddToClassList("clips");   // the figure runs off the bottom on purpose (the reference's lobby crop)
                    UiKit.SetArt(_charContainer, standing);
                }
                else UiKit.SetArt(_charContainer, GameData.WornCardArt(_heroId));
            }

            // Apply Blue Archive slow breathing animation
            ArtMotion.Breathe(_charContainer);

            // Click character to trigger dialogue change
            _charContainer.RegisterCallback<ClickEvent>(evt => OnCharacterClicked());

            // ---- Top-left icon grid, as the reference's lobby: 공지 / 모모톡 / 미션 / 구매 ---
            // Coloured glyphs with a label under them, two across, on nothing — no discs. The
            // round white buttons this had read as a toolbar; the reference's read as things
            // lying on the desk.
            var icons = UiKit.Div("home__icons", _root);
            // 공지 and 우편 are panels over the lobby (InboxPanels); 상점 its own screen (ShopScreen).
            // Each carries a red dot while something waits — an unread notice, unclaimed mail, today's free goods.
            MailService.Seed(Game.Player);
            LobbyIcon(icons, "notice", Icons.Chart, "공지", "home__glyph--blue", () => InboxPanels.OpenNotice(_app, RefreshDots), () => NoticeService.Unread(Game.Player) > 0);
            LobbyIcon(icons, "mail", Icons.Chart, "우편", "home__glyph--blue", () => InboxPanels.OpenMail(_app, RefreshDots), () => MailService.Unclaimed(Game.Player) > 0);
            LobbyIcon(icons, "shop", Icons.Gacha, "상점", "home__glyph--cyan", () => _app.Show(AppRoot.Sheet.Shop), () => ShopService.HasFree(Game.Player));
            // IA (ui_score 05-Home): 메신저 lives on the bottom bar only; 앨범 and 통계 — look-up
            // screens, not the daily loop — moved here so the bar keeps six entries plus 모집
            LobbyIcon(icons, "album", Icons.Story, "앨범", "home__glyph--pink", () => _app.Show(AppRoot.Sheet.Album));
            LobbyIcon(icons, "chart", Icons.Chart, "통계", "home__glyph--blue", () => _app.Show(AppRoot.Sheet.Chart));

            // ---- Bottom-left: the event banner (the reference's lobby carries the current event there) --------------
            var evArt = Resources.Load<Sprite>("Art/Notice/n_event");
            if (evArt != null)
            {
                var ev = UiKit.Div("home__event", _root);
                ModalFrame.Painted(ev, (ctx, r) =>
                {
                    var q = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height) * 0.35f, 6f);
                    UiPaint.Shadow(ctx, q, new Vector2(0f, 6f), UiPaint.C(10, 20, 50, 0.3f), 12f);
                    UiPaint.Fill(ctx, q, Color.white);
                    var inner = UiPaint.Offset(q, -4f);
                    UiPaint.Image(ctx, inner, evArt, r, 0.35f);
                    UiPaint.Fill(ctx, inner, UiPaint.Horizontal(UiPaint.C(255, 70, 140, 0.85f), UiPaint.C(255, 70, 140, 0f), r.xMin, r.xMin + r.width * 0.55f), 0f);
                });
                UiKit.Text("EVENT!", "home__event-kicker", ev).pickingMode = PickingMode.Ignore;
                UiKit.Text("야근 수당 두 배", "home__event-title", ev).pickingMode = PickingMode.Ignore;
                UiKit.Text("9/30 ~ 10/13", "home__event-date", ev).pickingMode = PickingMode.Ignore;
                ev.RegisterCallback<ClickEvent>(_ => { AudioService.Play("tap", 0.5f); InboxPanels.OpenNotice(_app, RefreshDots); });
                Juice.Press(ev);
            }

            // ---- Right-Floating Speech Bubble ---------------------------------------------
            _bubble = UiKit.Div("home__bubble", _root);
            ModalFrame.Painted(_bubble, DrawBubble);
            _whoLabel = UiKit.Text(def?.nick ?? "김인턴", "home__who", _bubble);
            _speechLabel = UiKit.Text("", "home__speech", _bubble);

            // Bond (Affection)
            var bond = UiKit.Div("home__bond", _bubble);
            _bondLevel = UiKit.Text($"호감도 Lv.{owned.affection}", "home__bond-level", bond);
            var track = UiKit.Div("home__bond-track", bond);
            _bondFill = UiKit.Div("home__bond-fill", track);

            // Calculate bond bar fill percentage (affection cap is 20 for S, etc. let's scale max 20)
            float maxAffection = 20f;
            float fillPercent = Mathf.Clamp01(owned.affection / maxAffection) * 100f;
            _bondFill.style.width = Length.Percent(fillPercent);

            // Dialogue lists
            BuildDialogues(def, owned);
            ShowNextDialogue(playTap: false);

            // ---- Bottom-right: the 업무 folder -------------------------------------------
            // The reference's lobby enters the game through a cyan folder with a white plate
            // under it and a pink "in progress" tag — not a button. It is the one object on the
            // screen that is obviously the way in.
            // the lobby's way in: one cyan slanted panel (ui_critique 05-Home #1) — an English sub-label,
            // 업무 large, the phase and a gauge through it, the battle mark on the right, the pink tag on top
            var campaign = UiKit.Div("home__campaign", _root);
            var stage = Mathf.Max(1, Game.Player?.stage ?? 1);
            var prog = ((stage - 1) % 10 + 1) / 10f;
            ModalFrame.Painted(campaign, (ctx, r) =>
            {
                var body = Rect.MinMaxRect(r.xMin, r.yMin + 26f, r.xMax, r.yMax);
                var slant = SkewPlate.SlantFor(body.height) * 0.9f;
                var lip = UiPaint.SkewRect(Rect.MinMaxRect(body.xMin, body.yMin + 6f, body.xMax, body.yMax), slant, 6f);
                UiPaint.Shadow(ctx, lip, new Vector2(0f, 8f), UiPaint.C(0, 60, 130, 0.3f), 16f);
                UiPaint.Fill(ctx, lip, UiPaint.C(0, 150, 220));
                var face = UiPaint.SkewRect(Rect.MinMaxRect(body.xMin, body.yMin, body.xMax, body.yMax - 6f), slant, 6f);
                // light glass into cyan (the r5 lobby redesign): the saturated slab read as too loud
                UiPaint.Fill(ctx, face, UiPaint.Horizontal(UiPaint.C(236, 246, 253, 0.96f), UiPaint.C(70, 206, 250, 0.96f), body.xMin + body.width * 0.25f, body.xMax));
                UiPaint.Fill(ctx, UiPaint.Clip(new List<Vector2> { new(body.xMin - 20f, body.yMin), new(body.xMax + 20f, body.yMin), new(body.xMax + 20f, body.yMin + body.height * 0.34f), new(body.xMin - 20f, body.yMin + body.height * 0.4f) }, face), UiPaint.C(255, 255, 255, 0.2f), 0f);
                UiPaint.Stroke(ctx, face, UiPaint.C(179, 229, 252), 2f);
                // the gauge
                var gx0 = body.xMin + slant + 30f; var gx1 = body.xMax - 150f; var gy = body.yMax - 34f;
                UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(gx0, gy, gx1, gy + 16f), 3f), UiPaint.C(20, 60, 110, 0.18f));
                UiPaint.Fill(ctx, UiPaint.RoundRect(Rect.MinMaxRect(gx0, gy, gx0 + (gx1 - gx0) * prog, gy + 16f), 3f), UiPaint.C(255, 232, 20));
            });
            var words = UiKit.Div("home__campaign-words", campaign); words.pickingMode = PickingMode.Ignore;
            UiKit.Text("TASK OPERATION", "home__campaign-en", words).pickingMode = PickingMode.Ignore;
            UiKit.Text("업무", "home__campaign-label", words).pickingMode = PickingMode.Ignore;
            UiKit.Text($"PHASE {stage}", "home__campaign-sub", words).pickingMode = PickingMode.Ignore;
            var mark = UiKit.Div("home__campaign-mark", campaign); mark.pickingMode = PickingMode.Ignore;
            var battleArt = GameData.Icon("battle"); if (battleArt != null) UiKit.SetArt(mark, battleArt);
            var tag = UiKit.Div("home__campaign-tag", campaign);
            ModalFrame.Painted(tag, (ctx, r) =>
            {
                var poly = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height), 4f);
                UiPaint.Fill(ctx, poly, UiPaint.C(20, 33, 54));
            });
            UiKit.Text("캠페인 진행중", "home__campaign-tag-text", tag).pickingMode = PickingMode.Ignore;
            Juice.Press(campaign);
            campaign.RegisterCallback<ClickEvent>(_ =>
            {
                AudioService.Play("nav", 0.5f);
                _app.Show(AppRoot.Sheet.Battle);
            });

            return _root;
        }

        public void Refresh()
        {
            // Update values if changed
            _heroId = GetLeadHeroId();
            var def = GameData.Hero(_heroId);
            var p = Game.Player;
            var owned = p.owned.FirstOrDefault(x => x.id == _heroId) ?? new OwnedHero(_heroId);

            if (_whoLabel != null) _whoLabel.text = def?.nick ?? "김인턴";
            if (_bondLevel != null) _bondLevel.text = $"호감도 Lv.{owned.affection}";
            if (_bondFill != null)
            {
                float maxAffection = 20f;
                float fillPercent = Mathf.Clamp01(owned.affection / maxAffection) * 100f;
                _bondFill.style.width = Length.Percent(fillPercent);
            }

            BuildDialogues(def, owned);
            ShowNextDialogue(playTap: false);
        }

        readonly List<(VisualElement dot, System.Func<bool> on)> _dots = new();

        void RefreshDots() { foreach (var (dot, on) in _dots) dot.EnableInClassList("hidden", !on()); }

        void LobbyIcon(VisualElement parent, string art, string glyph, string label, string tint, System.Action open, System.Func<bool> badge = null)
        {
            // the reference's lobby shortcuts: the illustrated icon standing free, its word under it — no tile behind
            var btn = UiKit.Div("home__icon home__icon--free", parent);
            var sprite = GameData.Icon(art);
            if (sprite != null) UiKit.SetArt(UiKit.Div("home__art-icon", btn), sprite);
            else UiKit.Text(glyph, "icon home__glyph " + tint, btn);
            Juice.Press(btn);
            UiKit.Text(label, "home__icon-label", btn);
            if (badge != null)
            {
                var dot = UiKit.Div("home__dot", btn); dot.pickingMode = PickingMode.Ignore;
                _dots.Add((dot, badge)); dot.EnableInClassList("hidden", !badge());
            }
            btn.RegisterCallback<ClickEvent>(_ =>
            {
                AudioService.Play("nav", 0.5f);
                open();
            });
        }

        /// <summary>
        /// The reference's lobby bubble: small, white, rounded, a hairline blue-grey border and a
        /// short tail pointing left at the speaker's head. No name plate, no gauge — just the line.
        /// </summary>
        static void DrawBubble(MeshGenerationContext ctx, Rect r)
        {
            // the tail is drawn INSIDE the element (paint outside its rect is clipped — the gate caught
            // a bubble whose tail had vanished and pointed at nothing): the body starts 34px in and
            // the tail reaches back down-left to the speaker's face
            var bodyRect = Rect.MinMaxRect(r.xMin + 44f, r.yMin, r.xMax, r.yMax);
            var body = UiPaint.RoundRect(bodyRect, Mathf.Min(26f, r.height * 0.4f), 6);
            UiPaint.Shadow(ctx, body, new Vector2(0f, 4f), UiPaint.C(20, 40, 80, 0.16f), 10f);
            var cy = r.yMin + Mathf.Min(r.height * 0.62f, 70f);
            var tail = new List<Vector2>
            {
                new Vector2(bodyRect.xMin + 6f, cy - 26f), new Vector2(r.xMin + 2f, cy + 30f), new Vector2(bodyRect.xMin + 6f, cy + 4f),
            };
            var edge = UiPaint.C(176, 200, 226);
            // navy with white type (the r5 redesign): it reads against the light room, the tail is plain
            UiPaint.Fill(ctx, body, UiPaint.C(26, 40, 66, 0.95f));
            UiPaint.Fill(ctx, tail, UiPaint.C(26, 40, 66, 0.95f));
        }

        /// <summary>The reference's campaign folder: a cyan folder with a tab, lit from above.</summary>
        static void DrawFolder(MeshGenerationContext ctx, Rect r)
        {
            var tab = UiPaint.RoundRect(Rect.MinMaxRect(r.xMin + 10f, r.yMin, r.xMin + r.width * 0.42f, r.yMin + 40f), 10f, 4);
            var body = UiPaint.RoundRect(Rect.MinMaxRect(r.xMin, r.yMin + 24f, r.xMax, r.yMax), 16f, 6);
            UiPaint.Ring(ctx, body, UiPaint.C(90, 225, 255, 0.45f), UiPaint.C(90, 225, 255, 0f), 14f);
            UiPaint.Shadow(ctx, body, new Vector2(0f, 6f), UiPaint.C(10, 60, 120, 0.3f), 12f);
            UiPaint.Fill(ctx, tab, UiPaint.C(40, 160, 220));
            UiPaint.Fill(ctx, body, Color.white);
            UiPaint.Fill(ctx, UiPaint.Offset(body, -3f), UiPaint.Vertical(UiPaint.C(110, 226, 255), UiPaint.C(28, 170, 236), r.yMin, r.yMax));
            // Sheen across the top third.
            var sheen = UiPaint.Clip(new List<Vector2>
            {
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
                new Vector2(r.xMax, r.yMin + r.height * 0.42f), new Vector2(r.xMin, r.yMin + r.height * 0.42f),
            }, UiPaint.Offset(body, -3f));
            UiPaint.Fill(ctx, sheen, UiPaint.C(255, 255, 255, 0.22f), 0f);
        }

        string GetLeadHeroId() => LeadHeroId();

        public static string LeadHeroId()
        {
            var p = Game.Player;
            if (!string.IsNullOrEmpty(p.leadHeroId) && p.owned.Any(x => x.id == p.leadHeroId))
                return p.leadHeroId;
            var party = p.PartyMembers().FirstOrDefault();
            if (party != null) return party.id;
            var first = p.owned.FirstOrDefault();
            return first != null ? first.id : "main";
        }

        /// <summary>
        /// The room's light, alive (the BA cross-check: "the lobby background lacks depth and
        /// lighting, and nothing moves"): soft shafts of window light from the upper left that
        /// breathe in strength and drift a little, and dust motes floating up through them. Painted
        /// over the room, under the character, so she stands in the light.
        /// </summary>
        static void Ambience(VisualElement host)
        {
            var fx = UiKit.Div("home__ambience", host); fx.pickingMode = PickingMode.Ignore;
            var t0 = Time.realtimeSinceStartup;
            var rng = new System.Random(11);
            var motes = Enumerable.Range(0, 26).Select(_ => (x: (float)rng.NextDouble(), y: (float)rng.NextDouble(), s: 2.5f + (float)rng.NextDouble() * 5f, v: 0.012f + (float)rng.NextDouble() * 0.02f, ph: (float)rng.NextDouble() * 6f)).ToArray();
            ModalFrame.Painted(fx, (ctx, r) =>
            {
                var t = Time.realtimeSinceStartup - t0;
                for (var i = 0; i < 4; i++)
                {
                    var k = 0.55f + 0.45f * Mathf.Sin(t * 0.35f + i * 1.7f);
                    var x = r.xMin + r.width * (0.02f + i * 0.13f) + Mathf.Sin(t * 0.12f + i) * 20f;
                    var w = r.width * (0.05f + 0.025f * (i % 2));
                    var shaft = new System.Collections.Generic.List<Vector2> { new(x, r.yMin), new(x + w, r.yMin), new(x + w + r.height * 0.55f, r.yMax), new(x + r.height * 0.55f, r.yMax) };
                    UiPaint.Fill(ctx, shaft, UiPaint.Vertical(UiPaint.C(255, 250, 235, 0.13f * k), UiPaint.C(255, 250, 235, 0f), r.yMin, r.yMax), 18f);
                }
                foreach (var m in motes)
                {
                    var yy = Mathf.Repeat(m.y - t * m.v, 1f);
                    var xx = m.x + Mathf.Sin(t * 0.5f + m.ph) * 0.01f;
                    var a = Mathf.Sin(yy * Mathf.PI) * (0.35f + 0.25f * Mathf.Sin(t * 1.3f + m.ph));
                    var c = new Vector2(r.xMin + r.width * xx, r.yMin + r.height * yy);
                    UiPaint.Fill(ctx, UiPaint.Ellipse(c, m.s, m.s, 10), UiPaint.C(255, 252, 240, Mathf.Clamp01(a)), m.s);
                }
            });
            fx.schedule.Execute(() => fx.MarkDirtyRepaint()).Every(50);
        }

        void BuildDialogues(HeroDef def, OwnedHero owned)
        {
            _dialogues.Clear();

            // 1. Default greeting
            if (def != null && !string.IsNullOrEmpty(def.line))
            {
                _dialogues.Add(def.line);
            }

            // Fetch affection dialogues
            var aff = GameData.Affection(_heroId);
            if (aff != null)
            {
                // 2. Secret line (affection >= 2)
                if (owned.affection >= 2 && !string.IsNullOrEmpty(aff.line2))
                {
                    _dialogues.Add(aff.line2);
                }

                // 3. Secret text / background info (affection >= 3)
                if (owned.affection >= 3 && !string.IsNullOrEmpty(aff.secret))
                {
                    _dialogues.Add($"[비밀 문서] {aff.secret}");
                }
            }

            // Fallback in case no lines are found
            if (_dialogues.Count == 0)
            {
                _dialogues.Add("오늘도 열심히 일해볼까요!");
            }
        }

        void OnCharacterClicked()
        {
            ShowNextDialogue(playTap: true);

            // Apply a nice little jump/pop animation on tap to give physical feedback!
            if (_charContainer != null)
            {
                _charContainer.style.scale = new StyleScale(new Scale(new Vector3(1.02f, 1.02f, 1f)));
                _charContainer.schedule.Execute(() =>
                {
                    _charContainer.style.scale = new StyleScale(new Scale(Vector3.one));
                }).ExecuteLater(100);
            }
        }

        void ShowNextDialogue(bool playTap)
        {
            if (_dialogues.Count == 0) return;

            _dialogueIndex = (_dialogueIndex + 1) % _dialogues.Count;
            if (_speechLabel != null)
            {
                _speechLabel.text = _dialogues[_dialogueIndex];
                // pop in: a new line appears the way the reference's does — fade and rise
                if (_bubble != null)
                {
                    _bubble.RemoveFromClassList("home__bubble--in");
                    _bubble.schedule.Execute(() => _bubble.AddToClassList("home__bubble--in")).StartingIn(20);
                }
            }

            if (playTap)
            {
                AudioService.Play("tap", 0.5f);
            }
        }
    }
}