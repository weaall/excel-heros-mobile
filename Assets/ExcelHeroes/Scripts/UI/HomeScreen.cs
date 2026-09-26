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

            // Set beautiful generated home background
            var bg = GameData.UiArt("home_bg");
            if (bg != null) _root.style.backgroundImage = new StyleBackground(bg);

            _heroId = GetLeadHeroId();
            var def = GameData.Hero(_heroId);
            var p = Game.Player;
            var owned = p.owned.FirstOrDefault(x => x.id == _heroId) ?? new OwnedHero(_heroId);

            // ---- Left: Character Visual Element -------------------------------------------
            _charContainer = UiKit.Div("home__char-container", _root);
            if (def != null)
            {
                UiKit.SetArt(_charContainer, GameData.WornCardArt(_heroId));
            }

            // Apply Blue Archive slow breathing animation
            ArtMotion.Breathe(_charContainer);

            // Click character to trigger dialogue change
            _charContainer.RegisterCallback<ClickEvent>(evt => OnCharacterClicked());

            // ---- Left-Floating Circular Buttons (Blue Archive Lobby Icons) ----------------
            // 1. MomoTalk Circular Button (MESSENGER)
            var momo = UiKit.Div("home__circle-btn", _root);
            momo.style.top = 200;
            var momoIcon = UiKit.Text(Icons.Story, "home__circle-icon icon", momo);
            UiKit.Text("모모톡", "home__circle-label", momo);
            momo.RegisterCallback<ClickEvent>(evt =>
            {
                AudioService.Play("nav", 0.5f);
                _app.Show(AppRoot.Sheet.Story);
            });

            // 2. Shop/Gacha Circular Button (RECRUITMENT)
            var shop = UiKit.Div("home__circle-btn", _root);
            shop.style.top = 330;
            var shopIcon = UiKit.Text(Icons.Gacha, "home__circle-icon icon", shop);
            UiKit.Text("상점", "home__circle-label", shop);
            shop.RegisterCallback<ClickEvent>(evt =>
            {
                AudioService.Play("nav", 0.5f);
                _app.Show(AppRoot.Sheet.Gacha);
            });

            // 3. Codex Circular Button (CODEX)
            var codex = UiKit.Div("home__circle-btn", _root);
            codex.style.top = 460;
            var codexIcon = UiKit.Text(Icons.Codex, "home__circle-icon icon", codex);
            UiKit.Text("도감", "home__circle-label", codex);
            codex.RegisterCallback<ClickEvent>(evt =>
            {
                AudioService.Play("nav", 0.5f);
                _app.Show(AppRoot.Sheet.Codex);
            });

            // ---- Right-Floating Speech Bubble ---------------------------------------------
            _bubble = UiKit.Div("home__bubble", _root);
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

            // ---- Bottom-Right: Massive "업무" (Campaign/Battle) Button ---------------------
            var campaignContainer = UiKit.Div("home__campaign-container", _root);
            
            // Pink event banner above the button
            var campaignBadge = UiKit.Div("home__campaign-badge", campaignContainer);
            UiKit.Text("캠페인 진행중", "home__campaign-badge-text", campaignBadge);

            // Plated Button
            var campaignBtn = UiKit.Btn("업무", "btn home__campaign-btn", () =>
            {
                AudioService.Play("nav", 0.5f);
                _app.Show(AppRoot.Sheet.Battle);
            }, campaignContainer);

            // Apply blue primary skew plate
            SkewPlate.Apply(campaignBtn, SkewPlate.Kind.Primary);

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
            }

            if (playTap)
            {
                AudioService.Play("tap", 0.5f);
            }
        }
    }
}