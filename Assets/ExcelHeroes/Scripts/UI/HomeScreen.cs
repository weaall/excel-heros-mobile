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
            // the sheet is painted into the standing art (tools/bake_sheet.py); no overlay
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
            LobbyIcon(icons, "notice", Icons.Chart, "공지", "home__glyph--blue", AppRoot.Sheet.Chart);
            LobbyIcon(icons, "messenger", Icons.Story, "메신저", "home__glyph--pink", AppRoot.Sheet.Story);
            LobbyIcon(icons, "tasks", Icons.Tasks, "업무", "home__glyph--blue", AppRoot.Sheet.Quests);
            LobbyIcon(icons, "shop", Icons.Gacha, "상점", "home__glyph--cyan", AppRoot.Sheet.Gacha);

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
            var campaign = UiKit.Div("home__campaign", _root);
            var folder = UiKit.Div("home__folder", campaign);
            ModalFrame.Painted(folder, DrawFolder);
            UiKit.Text(Icons.Battle, "icon home__folder-glyph", folder).pickingMode = PickingMode.Ignore;
            var plate = UiKit.Div("home__campaign-plate", campaign);
            ModalFrame.Painted(plate, (ctx, r) => SkewPlate.DrawPlate(ctx, r, SkewPlate.Kind.Light, accents: false));
            UiKit.Text("업무", "home__campaign-label", plate).pickingMode = PickingMode.Ignore;
            var tag = UiKit.Div("home__campaign-tag", campaign);
            ModalFrame.Painted(tag, (ctx, r) =>
            {
                var poly = UiPaint.SkewRect(r, SkewPlate.SlantFor(r.height), 4f);
                UiPaint.Shadow(ctx, poly, new Vector2(0f, 2f), UiPaint.C(80, 10, 40, 0.3f), 4f);
                UiPaint.Fill(ctx, poly, Color.white);
                UiPaint.Fill(ctx, UiPaint.Offset(poly, -2f), UiPaint.Vertical(UiPaint.C(255, 92, 150), UiPaint.C(232, 40, 110), r.yMin, r.yMax));
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

        void LobbyIcon(VisualElement parent, string art, string glyph, string label, string tint, AppRoot.Sheet target)
        {
            var btn = UiKit.Div("home__icon", parent);
            var sprite = GameData.Icon(art);
            if (sprite != null) UiKit.SetArt(UiKit.Div("home__art-icon", btn), sprite);
            else UiKit.Text(glyph, "icon home__glyph " + tint, btn);
            Juice.Press(btn);
            UiKit.Text(label, "home__icon-label", btn);
            btn.RegisterCallback<ClickEvent>(_ =>
            {
                AudioService.Play("nav", 0.5f);
                _app.Show(target);
            });
        }

        /// <summary>
        /// The reference's lobby bubble: small, white, rounded, a hairline blue-grey border and a
        /// short tail pointing left at the speaker's head. No name plate, no gauge — just the line.
        /// </summary>
        static void DrawBubble(MeshGenerationContext ctx, Rect r)
        {
            var body = UiPaint.RoundRect(r, Mathf.Min(26f, r.height * 0.4f), 6);
            UiPaint.Shadow(ctx, body, new Vector2(0f, 4f), UiPaint.C(20, 40, 80, 0.16f), 10f);
            var cy = r.yMin + Mathf.Min(r.height * 0.5f, 52f);
            var tail = new List<Vector2>
            {
                new Vector2(r.xMin + 2f, cy - 16f), new Vector2(r.xMin - 26f, cy + 10f), new Vector2(r.xMin + 2f, cy + 12f),
            };
            var edge = UiPaint.C(176, 200, 226);
            UiPaint.Fill(ctx, UiPaint.Offset(body, 2f), edge);
            UiPaint.Fill(ctx, UiPaint.Offset(tail, 2f), edge);
            UiPaint.Fill(ctx, body, UiPaint.C(255, 255, 255, 0.98f));
            UiPaint.Fill(ctx, tail, UiPaint.C(255, 255, 255, 0.98f));
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

        string GetLeadHeroId()
        {
            var p = Game.Player;
            if (!string.IsNullOrEmpty(p.leadHeroId) && p.owned.Any(x => x.id == p.leadHeroId))
                return p.leadHeroId;
            var party = p.PartyMembers().FirstOrDefault();
            if (party != null) return party.id;
            var first = p.owned.FirstOrDefault();
            return first != null ? first.id : "main";
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