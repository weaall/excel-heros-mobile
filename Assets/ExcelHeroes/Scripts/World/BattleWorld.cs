using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using ExcelHeroes.Ability;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using ExcelHeroes.UI;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The 3D battlefield: an office set, the party and the wave as SD figures, drawn by its own
    /// camera into a RenderTexture that BattleScreen puts behind its HUD.
    ///
    /// It mirrors BattleSim and never drives it — positions come from the sim's lane coordinate
    /// (BattleScreen passes the drawn x, lunges included), and the animation is keyed off the
    /// sim's shots and events: a new shot is a throw, a Damage event a flinch, Death a fall,
    /// Skill a jump with the back sheet flaring. The HUD asks it where a fighter's head is on the
    /// screen, so health bars and damage numbers stay pinned to the figures.
    /// </summary>
    public class BattleWorld : IAbilityPresenter
    {
        public const int Layer = 30;
        static BattleWorld _instance;
        public static BattleWorld Instance => _instance ??= new BattleWorld();

        readonly Transform _root;
        readonly Camera _cam;
        Transform _set, _street;
        int _setMood = -1;
        RenderTexture _rt;
        readonly Transform _templates;
        readonly Dictionary<string, (GameObject go, float h)> _templateCache = new();
        readonly Dictionary<Combatant, Actor> _actors = new();
        readonly Dictionary<Shot, Transform> _shots = new();
        readonly List<Combatant> _actorKeys = new();
        readonly List<Shot> _shotKeys = new();
        readonly List<(Transform t, float life, float max, Vector3 vel, float grow)> _sparks = new();
        readonly Stack<Transform> _sparkPool = new();
        BattleSim _sim;
        float _time;
        MaterialPropertyBlock _mpb;

        public RenderTexture Texture => _rt;
        public Camera Camera => _cam;

        BattleWorld()
        {
            var go = new GameObject("BattleWorld");
            Object.DontDestroyOnLoad(go);
            _root = go.transform;
            _root.position = new Vector3(0f, -300f, 0f);

            _templates = new GameObject("templates").transform;
            _templates.SetParent(_root, false);
            _templates.gameObject.SetActive(false);

            var camGo = new GameObject("BattleCamera") { layer = Layer };
            camGo.transform.SetParent(_root, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.cullingMask = 1 << Layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.86f, 0.93f, 1f);
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 60f;
            _cam.depth = -10;
            _cam.enabled = false;
            var data = _cam.GetUniversalAdditionalCameraData();
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            PostFx.Use(_cam, Layer);   // bloom, tone curve, saturation, vignette (PostFx)
            data.antialiasingQuality = AntialiasingQuality.High;
            PlaceCamera(0f);
            AbilityHost.Presenter = this;

            // light from the upper left, a little in front — the reference's key light
            Shader.SetGlobalVector("_EhLightDir", new Vector4(-0.45f, 0.85f, -0.5f, 0f));
            ToonLook.Apply();   // crisp outlines at any distance (Toon.shader)
            _mpb = new MaterialPropertyBlock();
        }

        // 0 = the fight's wide shot, 1 = the victory close-up on the party (the reference's
        // "Battle Complete": the camera comes down in front of the squad as they cheer).
        float _closeUp, _closeUpTarget;
        Vector3 _partyCentre;

        /// <summary>Victory: bring the camera down onto the party, who turn to it and cheer.</summary>
        public void Celebrate(bool on) => _closeUpTarget = on ? 1f : 0f;

        static float EnvF(string k, float d) => float.TryParse(System.Environment.GetEnvironmentVariable(k), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
        /// <summary>The win's close-up: the camera's distance and aim height, the members' spacing and the depth of the back row.</summary>
        public static float WinDist = EnvF("EH_WINDIST", 5.3f), WinAimY = EnvF("EH_WINAIM", 0.76f), WinSpacing = EnvF("EH_WINSPACE", 1.02f), WinStagger = EnvF("EH_WINSTAGGER", 0.5f);
        /// <summary>The squad's turn toward the enemies (the user: "they keep looking ahead, not at the enemy"): -32 had them
        /// 32° off the lens; -78 is ~40° off the line to the enemies — at the fight, the face still in view.</summary>
        public static float HeroFace = EnvF("EH_HEROFACE", -68f);
        public static float HeroScale = EnvF("EH_HEROSCALE", 1.08f);   // 1.08: A/B vs BA 33/48 over two runs against 1.18 (1.3: 9/24)
        public static float QuarterPitch = EnvF("EH_PITCH", 26f), QuarterYaw = EnvF("EH_YAW", 28f), QuarterDist = EnvF("EH_DIST", 9f);   // 9 m: the squad larger in frame (part cross-check)   // 33°: lower than the old 40, so a fighting squad shows faces, not crowns (the BA cross-check)
        float _camFocusX = -0.2f, _camZoom = 1f, _lastEnemyX = float.NaN, _lastHeroX = float.NaN, _punch;

        void PlaceCamera(float shake)
        {
            var k = Mathf.SmoothStep(0f, 1f, _closeUp);
            // The reference's battle camera (temp_images/712980) is HIGH: about 32° above the
            // street, looking down onto the road, the sidewalk and the storefronts — no sky. The
            // close-up for the win drops lower and nearer to the squad's centre.
            // Quarter view (after the reference RPG's QuarterView camera, offset (0, 4, -5) = 39 deg
            // down): high AND turned, from the front-left, so the lane runs diagonally up the
            // picture as on target_3 — the squad near and low on the left, the errors further up
            // on the right. The close-up swings back square onto the party.
            // the fight, not a fixed point: between the squad and the nearest errors (tools/out/action_critique.md
            // #5 — the enemies stood small at the top-right edge), a little further out when they are far apart
            var target = Vector3.Lerp(new Vector3(_camFocusX, 0.4f, 0.1f), _partyCentre + new Vector3(0.2f, WinAimY, WinStagger * 0.5f), k);
            var pitch = Mathf.Lerp(QuarterPitch, 14f, k) * Mathf.Deg2Rad;
            var yaw = Mathf.Lerp(QuarterYaw, 0f, k);
            var dist = Mathf.Lerp(QuarterDist * _camZoom * (1f - 0.035f * _punch), WinDist, k);
            var pos = target + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, Mathf.Sin(pitch), -Mathf.Cos(pitch)) * dist;
            if (shake > 0f) pos += new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * shake * 0.04f;
            _cam.transform.localPosition = pos;
            _cam.transform.localRotation = Quaternion.LookRotation(target - pos, Vector3.up);
        }

        // ------------------------------------------------------------------ lifecycle --

        /// <summary>Attach to a (new or resumed) run: rebuild the set for its mood and the cast.</summary>
        public void Begin(BattleSim sim)
        {
            _sim = sim;
            ResetAbilities();
            _closeUp = _closeUpTarget = 0f;
            // the floor sheet is re-parented out of the rig (SdRef.FloorSheet): destroy it with the actor,
            // or every new run left the last run's sheets on the street — two sheets per member (the user)
            foreach (var a in _actors.Values) { a.DisposeClips(); if (a.Rig.Sheet != null) Object.Destroy(a.Rig.Sheet.gameObject); Object.Destroy(a.Rig.Root.gameObject); };
            _actors.Clear();
            foreach (var s in _shots.Values) Object.Destroy(s.gameObject);
            _shots.Clear();

            var mood = sim.Stage >= 40 ? 2 : sim.Stage >= 20 ? 1 : 0;
            if (_set == null || mood != _setMood)
            {
                if (_set != null) Object.Destroy(_set.gameObject);
                if (_street != null) Object.Destroy(_street.gameObject);
                // The painted stage (tools/gen_bg_gemini.py) when there is one: a backdrop fixed to
                // the camera for the sky and the far city, with the 3D street set (StreetSet) in
                // front of it. The procedural office is the fallback.
                var bg = GameData.BattleBackdrop(mood == 2 ? "night" : mood == 1 ? "evening" : "day");
                _set = bg != null ? Backdrop(bg) : OfficeStage.Build(_root, Layer, sim.Stage);
                _street = bg != null ? StreetSet.Build(_root, Layer, mood) : null;
                StreetSet.Paint(_street, mood);   // the painted street projected onto the set (Plate.shader)
                _props = _street != null ? StreetSet.Props(_street, Layer, mood) : null;
                _setMood = mood;
                _cam.backgroundColor = mood == 2 ? new Color(0.16f, 0.18f, 0.32f) : new Color(0.86f, 0.93f, 1f);
            }
            _entering = sim.Elapsed < 0.5f;
            foreach (var h in sim.Heroes) Ensure(h);
            foreach (var m in sim.Monsters) Ensure(m);
            _entering = false;
        }

        bool _entering;
        /// <summary>The member's sheet floats over her head as a halo in the fight, not flat on the street under her.</summary>
        public static bool HaloInFight = System.Environment.GetEnvironmentVariable("EH_FLOORSHEET") != "1";
        Transform _props;

        const float BackdropDistance = 40f;

        /// <summary>
        /// The painted stage on a quad parented to the camera, sized to fill the frustum at 40 m
        /// and cropped (not stretched) to the frame's aspect, keeping the floor in view.
        /// </summary>
        Transform Backdrop(Sprite bg)
        {
            var go = new GameObject("backdrop") { layer = Layer };
            go.transform.SetParent(_cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, BackdropDistance);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MeshKit.NewGlass(bg.texture);
            go.AddComponent<MeshFilter>();
            _backdropSprite = bg;
            FitBackdrop(go.transform);
            return go.transform;
        }

        Sprite _backdropSprite;
        float _backdropAspect;

        void FitBackdrop(Transform t)
        {
            if (t == null || _backdropSprite == null) return;
            var h = 2f * BackdropDistance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var w = h * _cam.aspect;
            var tex = _backdropSprite.texture;
            var r = _backdropSprite.rect;
            var imgAspect = r.width / r.height;
            // cover: crop the image's height when the frame is wider than the picture
            var vSpan = Mathf.Clamp01(imgAspect / _cam.aspect);
            var v0 = Mathf.Clamp01((1f - vSpan) * 0.35f);          // street set: keep most of the road, trim a little sky
            var uv0 = new Vector2(r.xMin / tex.width, (r.yMin + r.height * v0) / tex.height);
            var uv1 = new Vector2(r.xMax / tex.width, (r.yMin + r.height * (v0 + vSpan)) / tex.height);
            var b = new MeshKit.Builder();
            b.Quad(Vector3.zero, new Vector3(w * 0.5f * 1.04f, 0f, 0f), new Vector3(0f, h * 0.5f * 1.04f, 0f), Color.white, uv0, uv1);
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = b.Bake("backdrop");
            _backdropAspect = _cam.aspect;
        }

        static readonly float SuperSample = EnvF("EH_SS", 1f);   // BattleScreen.Layout3D already asks for 1.25x (A/B hook)

        public void SetVisible(bool on) => _cam.enabled = on && _rt != null;

        /// <summary>Keep the render target the size of the field on screen (in real pixels).</summary>
        public void Resize(int w, int h)
        {
            // rendered a quarter over the screen's pixels with 4x MSAA, and shown downscaled: crisp outlines and
            // no stair-stepping on the models (the BA cross-check: "muddy, pixelated")
            w = Mathf.Clamp(Mathf.RoundToInt(w * SuperSample), 64, 4096);
            h = Mathf.Clamp(Mathf.RoundToInt(h * SuperSample), 64, 4096);
            if (_rt != null && _rt.width == w && _rt.height == h) return;
            if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Object.Destroy(_rt); }
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "BattleRT", antiAliasing = 4, filterMode = FilterMode.Bilinear };
            _rt.Create();
            _cam.targetTexture = _rt;
            _cam.aspect = w / (float)h;
            if (_set != null && _set.name == "backdrop") FitBackdrop(_set);
        }

        // ------------------------------------------------------------------ mapping --

        /// <summary>Sim lane x (the web build's 832-unit field) to world x.</summary>
        public static float WX(float simX) => (simX - 400f) / 64f * 0.95f;

        // a shallower stagger: the camera is low now and a deep row reads as floating
        static readonly float[] LaneZ = { 0.1f, 0.6f, -0.45f, 1.0f, -0.8f };

        float ZOf(Combatant c)
        {
            var list = c.side == Side.Hero ? _sim.Heroes : _sim.Monsters;
            var i = Mathf.Max(0, list.IndexOf(c));
            return c.side == Side.Hero ? LaneZ[i % LaneZ.Length] : MonLaneZ[i % MonLaneZ.Length];
        }

        // the errors stand further back than the squad: the EX cards cover the lower right of the
        // screen, and the near lanes put the enemies behind them (target_3 has them mid-field)
        static readonly float[] MonLaneZ = { 0.45f, 1.05f, 0.0f, 1.4f, 0.75f };

        /// <summary>Where a world point lands on the field, 0..1 with y DOWN (UI space).</summary>
        public Vector2 Project(Vector3 world)
        {
            var v = _cam.WorldToViewportPoint(world);
            return new Vector2(v.x, 1f - v.y);
        }

        /// <summary>The top of a fighter's head on the field (0..1, y down), for its HP bar.</summary>
        public bool Head(Combatant c, out Vector2 p)
        {
            p = default;
            if (!_actors.TryGetValue(c, out var a)) return false;
            p = Project(a.Rig.Root.position + Vector3.up * (a.Rig.Height * a.Scale + 0.08f));
            return true;
        }

        /// <summary>Sim-space point (x along the lane, y the web field's height) to the field.</summary>
        public Vector2 ProjectSim(float x, float y, float z = 0f)
        {
            var h = (BattleSim.GroundY - y) / 64f * 0.95f;
            return Project(_root.TransformPoint(new Vector3(WX(x), Mathf.Max(0f, h), z)));
        }

        // ------------------------------------------------------------------ cast --

        /// <summary>A/B hook: the street's own SD proportion (SD_BATTLEHEAD / SD_BATTLELEG; default = the menus').</summary>
        static readonly float BattleHead = EnvF("SD_BATTLEHEAD", SdSample.HeadScale), BattleLeg = EnvF("SD_BATTLELEG", SdSample.LegScale);

        ChibiRig BuildSd(string heroId)
        {
            var (h0, l0) = (SdSample.HeadScale, SdSample.LegScale);
            SdSample.HeadScale = BattleHead; SdSample.LegScale = BattleLeg;
            try { return SdRef.Build(heroId, _root, Layer) ?? SdBase.Build(heroId, _root, Layer) ?? SdSprite.Build(heroId, _root, Layer); }
            finally { SdSample.HeadScale = h0; SdSample.LegScale = l0; }
        }

        Actor Ensure(Combatant c)
        {
            if (_actors.TryGetValue(c, out var a)) return a;
            a = new Actor { C = c };
            // 3D SD first (made from the 2D SD, World/SdModel), then the 2D SD sprite, then the built doll
            // the common SD base (World/SdBase) first: one body, one skeleton, one set of motions
            if (c.side == Side.Hero && BuildSd(c.heroId) is { } sd)
            {
                a.Rig = sd;
                a.Rig.Root.name = c.name;
                var def = GameData.Hero(c.heroId);
                var owned = Game.Player?.Find(c.heroId);
                var spec = BackSheet.For(def, owned);
                // one sheet per member, the same one everywhere: the 2D SD art no longer has an old
                // sheet painted in (tools/sd2d_hf.py), so the sprite carries the game's own too
                ChibiBuilder.AddSheet(a.Rig, SheetTexture.For(spec, c.heroId), spec.Left ? 1 : -1, Layer);
                if (a.Rig.Model3D) a.Rig.Sheet.localScale = Vector3.one * 0.9f;
                if (a.Rig.RefModel) SdRef.FloorSheet(a.Rig, 1.8f);
                if (a.Rig.RefModel) CastShadow(a.Rig);
                // the squad a size up (the BA cross-check: the members read small against the street)
                a.Scale = (c.role == "tank" ? 1.06f : 1f) * (a.Rig.RefModel ? HeroScale : 1f);
                a.Accent = spec.Accent;
            }
            else if (c.side == Side.Hero)
            {
                var male = c.heroId == GameData.MainId;
                var key = "h:" + c.heroId;
                var tpl = Template(key, () =>
                {
                    var doll = DollData.For(c.heroId);
                    return (ChibiBuilder.Build(doll, _templates, Layer, male).gameObject, 0.95f);
                });
                var go = Object.Instantiate(tpl.go, _root);
                go.name = c.name;
                a.Rig = ChibiRig.Bind(go.transform, tpl.h);
                var def = GameData.Hero(c.heroId);
                var owned = Game.Player?.Find(c.heroId);
                var spec = BackSheet.For(def, owned);
                ChibiBuilder.AddSheet(a.Rig, SheetTexture.For(spec, c.heroId), DollData.For(c.heroId).sheetSide == "left" ? 1 : -1, Layer);
                a.Scale = c.role == "tank" ? 1.08f : 1f;
                a.Accent = spec.Accent;
            }
            // the drawn mascot first (enemies v2, tools/gen_monsters_v2.py): one clean BA hand reads better
            // than the TripoSR meshes made from the first set, which stay as the fallback
            // 3D when the textured mesh exists (TRELLIS + the drawing, tools/mon3d_pack.py) — the squad is
            // 3D and the errors stand in the same street
            // enemies v3: the office object on the heroes' skeleton (World/SdEnemy) — same joints, same clips
            else if (SdEnemy.Build(c.boss != null ? c.boss.id : c.typeId, _root, Layer) is { } sde)
            {
                a.Rig = sde;
                a.Rig.Root.name = c.name;
                a.Scale = c.boss != null ? 1.9f : c.elite ? 1.2f : 1.05f;
                a.Accent = new Color(1f, 0.35f, 0.35f);
            }
            else if ((SdModel.BuildMonster(c.boss != null ? c.boss.id : c.typeId, _root, Layer, texturedOnly: true)
                      ?? SdSprite.BuildMonster(c.boss != null ? c.boss.id : c.typeId, _root, Layer)
                      ?? SdModel.BuildMonster(c.boss != null ? c.boss.id : c.typeId, _root, Layer)) is { } sdm)
            {
                a.Rig = sdm;
                a.Rig.Root.name = c.name;
                a.Scale = c.boss != null ? 1.9f : c.elite ? 1.25f : 1f;
                // a round 3D mascot of height 1 reads half the size of the drawn one beside a 1.3 m hero
                if (sdm.Mascot) a.Scale *= 1.4f;
                a.Accent = new Color(1f, 0.35f, 0.35f);
            }
            else
            {
                var boss = c.boss != null;
                var shape = GameData.MonsterTypes?.FirstOrDefault(t => t.id == c.typeId)?.shape ?? "blob";
                var key = $"m:{c.typeId}:{shape}:{boss}";
                var tpl = Template(key, () =>
                {
                    var t = MonsterBuilder.Build(c.typeId ?? "x", shape, boss, _templates, Layer, out var h);
                    return (t.gameObject, h);
                });
                var go = Object.Instantiate(tpl.go, _root);
                go.name = c.name;
                a.Rig = ChibiRig.Bind(go.transform, tpl.h);
                a.Scale = boss ? 2.3f : c.elite ? 1.35f : 1.1f;
                a.Accent = new Color(1f, 0.35f, 0.35f);
            }
            a.Rig.Root.gameObject.SetActive(true);
            a.Rig.Root.localScale = Vector3.one * a.Scale;
            a.Z = ZOf(c);
            a.X = WX(c.x);
            if (c.side == Side.Hero && _entering) a.Enter = 1f;
            _actors[c] = a;
            return a;
        }

        (GameObject go, float h) Template(string key, System.Func<(GameObject, float)> make)
        {
            if (_templateCache.TryGetValue(key, out var t) && t.go != null) return t;
            t = make();
            _templateCache[key] = t;
            return t;
        }

        // ------------------------------------------------------------------ events --

        /// <summary>The attacker's swing, started as the sim resolves the hit; the hit itself is drawn on contact (BattleScreen.ImpactLag).</summary>
        public void Swing(Combatant actor)
        {
            if (actor != null && _actors.TryGetValue(actor, out var a) && a.Attack <= 0f) a.Attack = 0.32f;
        }

        public void OnEvent(BattleEvent e)
        {
            if (_sim == null) return;
            switch (e.kind)
            {
                case EventKind.Spawn:
                    if (e.actor != null) Ensure(e.actor);
                    break;
                case EventKind.Damage:
                    if (e.target != null && _actors.TryGetValue(e.target, out var t))
                    {
                        t.Hit = 0.16f;
                        t.Freeze = Mathf.Max(t.Freeze, e.crit ? 0.11f : 0.065f); t.FreezeShake = e.crit ? 0.045f : 0.025f;
                        if (e.actor != null && _actors.TryGetValue(e.actor, out var att) && (e.actor.side == Side.Monster || e.actor.role is "melee" or "tank"))
                            att.Freeze = Mathf.Max(att.Freeze, e.crit ? 0.08f : 0.05f);
                        AddShakeLocal(e.crit ? 0.18f : 0.05f);
                        ImpactShards(t, e.crit);
                        // shoved back along the line: an enemy further than a hero, a crit twice as far
                        t.Knock = e.target.side == Side.Hero ? (e.crit ? -0.16f : -0.08f) : (e.crit ? 0.34f : 0.2f);
                        Spark(t, e.crit ? new Color(1f, 0.85f, 0.3f) : Color.white, e.crit ? 0.7f : 0.45f);
                        HitRing(t, e.crit);
                        if (_cast.A != null && e.actor != null && _actors.TryGetValue(e.actor, out var ca) && ca == _cast.A) SkillHit(_cast.Type, ca, t);
                    }
                    break;
                case EventKind.Heal:
                    if (e.target != null && _actors.TryGetValue(e.target, out var hl))
                    {
                        Spark(hl, new Color(0.45f, 1f, 0.6f), 0.6f, rise: true);
                        if (_cast.A != null) SkillHeal(_cast.Type, hl);
                    }
                    break;
                case EventKind.Warn:
                    if (e.actor != null && _actors.TryGetValue(e.actor, out var wb)) { Telegraph(wb, e.text); BossStrikes(wb, e.text); }
                    break;
                case EventKind.Skill:
                    if (e.actor != null && _actors.TryGetValue(e.actor, out var s))
                    {
                        if (s.C.boss != null) Resolve(s, e.text);
                        s.Skill = 0.75f;
                        var st = _cast.A == s ? _cast.Type : null;
                        if (st == null || st == "ult") SkillBurst(s);
                        if (st != null) SkillCast(s, st);
                    }
                    break;
                case EventKind.Death:
                    if (e.target != null && _actors.TryGetValue(e.target, out var d)) d.Dying = 0.0001f;
                    break;
                case EventKind.Victory:
                    foreach (var h in _actors.Values.Where(x => x.C.side == Side.Hero && x.C.Alive)) h.Cheer = 1.4f;
                    break;
            }
        }

        // ------------------------------------------------------------------ frame --

        /// <summary>Called every frame the battle screen is up. `drawX` is the sim x as drawn (lunges in).</summary>
        public void Sync(float dt, System.Func<Combatant, float> drawX, float shake)
        {
            if (_sim == null) return;
            _time += dt;
            _closeUp = Mathf.MoveTowards(_closeUp, _closeUpTarget, dt * 1.4f);
            Shader.SetGlobalFloat("_EhGroundY", _root.position.y + 0.036f);   // the street's plane for the cast shadows
            if (_props != null) _props.gameObject.SetActive(_closeUp < 0.05f);   // the win's close-up: no cover in front of the squad
            // (loops, not LINQ: this runs every frame)
            float cx = 0f, cz = 0f; var alive = 0;
            foreach (var a in _actors.Values) if (a.C.side == Side.Hero && a.C.Alive) { cx += a.X; cz += a.Z; alive++; }
            if (alive > 0 && _closeUpTarget <= 0f) _partyCentre = new Vector3(cx / alive, 0f, cz / alive);
            // the win: the squad lined up in one row facing the lens, as the reference's result lines its
            // members up (the BA cross-check: ours stood scattered down the diagonal, one huge in front)
            var slot = 0;
            foreach (var a in _actors.Values)
                if (a.C.side == Side.Hero && a.C.Alive)
                {
                    // closer together and staggered in depth, every other member half a step back (the reference's
                    // result: the squad large in frame at a few depths, not a flat police line-up)
                    a.LineX = _partyCentre.x + (slot - (alive - 1) * 0.5f) * WinSpacing;
                    a.LineZ = _partyCentre.z + (slot % 2 == 1 ? WinStagger : 0f);
                    slot++;
                }
            if (!float.IsNaN(_lastEnemyX) && !float.IsNaN(_lastHeroX))
            {
                var squadX = alive > 0 ? cx / alive : _lastHeroX;
                var want = Mathf.Clamp(Mathf.Lerp(squadX, _lastEnemyX, 0.45f) - 0.4f, -1.5f, 2.2f);
                _camFocusX = Mathf.Lerp(_camFocusX, want, 1f - Mathf.Exp(-dt * 2.2f));
                var spread = Mathf.Abs(_lastEnemyX - squadX);
                _camZoom = Mathf.Lerp(_camZoom, Mathf.Clamp(0.92f + spread * 0.03f, 0.92f, 1.12f), 1f - Mathf.Exp(-dt * 1.5f));
            }
            PlaceCamera((shake + _localShake * 20f) * (1f - _closeUp));

            foreach (var c in _sim.Heroes) Ensure(c);
            foreach (var c in _sim.Monsters) Ensure(c);

            // where the fight is, for the heads: the nearest living enemy / hero along the lane
            float nearestEnemyX = float.MaxValue, nearestHeroX = float.MinValue;
            foreach (var m in _sim.Monsters) if (m.Alive) nearestEnemyX = Mathf.Min(nearestEnemyX, WX(m.x));
            foreach (var h in _sim.Heroes) if (h.Alive) nearestHeroX = Mathf.Max(nearestHeroX, WX(h.x));
            if (nearestEnemyX == float.MaxValue) nearestEnemyX = WX(800f);
            if (nearestHeroX == float.MinValue) nearestHeroX = WX(0f);
            _lastEnemyX = nearestEnemyX; _lastHeroX = nearestHeroX;
            _actorKeys.Clear(); _actorKeys.AddRange(_actors.Keys);
            foreach (var c in _actorKeys)
            {
                var a = _actors[c];
                var gone = c.side == Side.Monster && !_sim.Monsters.Contains(c);
                if (gone && (a.Dying <= 0f || a.Dying > 1.1f))
                {
                    { a.DisposeClips(); if (a.Rig.Sheet != null) Object.Destroy(a.Rig.Sheet.gameObject); Object.Destroy(a.Rig.Root.gameObject); };
                    _actors.Remove(c);
                    continue;
                }
                // The victory shot is the party's: whatever is left of the wave steps out of it.
                if (c.side == Side.Monster) a.Rig.Root.gameObject.SetActive(_closeUp < 0.05f);
                a._enemyX = nearestEnemyX; a._heroX = nearestHeroX;
                a.Update(dt, _time, WX(drawX(c)), _cam.transform, _mpb, _closeUp);
                if (a.Rig.RefModel && c.side == Side.Monster && a.Dying > 0.7f && !a.Popped) { a.Popped = true; DeathPop(a); }
                // footfall dust on the run: a puff kicked back at every contact (the part cross-check: the frames
                // showed no motion — nothing to say the squad was moving)
                if (a.Running && _closeUp < 0.05f)
                {
                    if (!float.IsNaN(a.LastStride) && Mathf.Floor(a.Stride / Mathf.PI) != Mathf.Floor(a.LastStride / Mathf.PI))
                    {
                        var dir = c.side == Side.Hero ? 1f : -1f;
                        var foot = a.Rig.Root.position + new Vector3(-dir * 0.12f, 0.06f, Random.Range(-0.06f, 0.06f));
                        FxCard("blob", foot, 0.34f, 0.2f, 0.5f, new Vector3(-dir * 0.5f, 0.3f, 0f), new Color(0.92f, 0.93f, 0.97f, 0.55f), 0f, 0.5f, 1.6f);
                    }
                    a.LastStride = a.Stride;
                }
                else a.LastStride = float.NaN;
            }

            SyncShots();
            UpdateAbilityShots(dt);
            UpdateAbilityWarns(dt);
            UpdateTelegraphs(dt);
            UpdateSparks(dt);
            UpdateFx(dt);
            _localShake = Mathf.MoveTowards(_localShake, 0f, dt * 1.5f);
            _punch = Mathf.MoveTowards(_punch, 0f, dt * 7f);   // a crit's zoom-in, gone in 0.14 s
        }

        void SyncShots()
        {
            foreach (var shot in _sim.Shots)
            {
                if (!_shots.TryGetValue(shot, out var t))
                {
                    t = MakeShot(shot);
                    _shots[shot] = t;
                    if (shot.From != null && _actors.TryGetValue(shot.From, out var fa)) { fa.Attack = 0.32f; MuzzleFlash(fa, shot.Hostile); }
                }
                t.gameObject.SetActive(_closeUp < 0.05f);   // the win's close-up: no shots or trace arrows left hanging in it
                if (shot.From == null || shot.To == null) continue;
                _actors.TryGetValue(shot.From, out var from);
                _actors.TryGetValue(shot.To, out var to);
                if (from == null || to == null) continue;
                if (_volleys.TryGetValue(shot, out var vol)) { DrawVolley(shot, vol, from, to); continue; }

                var k = shot.Progress;
                var start = Muzzle(from);
                var end = to.Rig.Root.position + Vector3.up * to.Rig.Height * to.Scale * 0.5f;
                Vector3 p;
                switch (shot.Kind)
                {
                    case "slash":
                        p = end + new Vector3(-0.15f, 0.1f, -0.2f);
                        t.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.1f, k);
                        break;
                    case "drop":
                        p = end + Vector3.up * Mathf.Lerp(2.2f, 0f, k * k);
                        break;
                    case "heal":
                        p = Vector3.Lerp(start, end, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.4f;
                        break;
                    default:
                        p = Vector3.Lerp(start, end, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.55f;
                        break;
                }
                t.position = p;
                t.rotation = Quaternion.Euler(_time * 540f, _time * 300f, 0f);
                var glow = t.Find("glow");
                if (glow != null) glow.rotation = Quaternion.LookRotation(glow.position - _cam.transform.position);
            }
            _shotKeys.Clear(); _shotKeys.AddRange(_shots.Keys);
            foreach (var key in _shotKeys)
            {
                if (_sim.Shots.Contains(key)) continue;
                Object.Destroy(_shots[key].gameObject);
                _shots.Remove(key);
                _volleys.Remove(key);
            }
        }

        // ------------------------------------------------------------------ fire profiles --
        // The sim fires one Shot per attack and lands it on arrival. How that one blow LOOKS depends
        // on the hero's division, and the squad attacks with the spreadsheet itself: IT fills a
        // column down (five cells in a stream), marketing pastes a range (a fan of cells), finance
        // charges =SUM( and throws the total, the executives trace precedents (Excel's blue arrow),
        // admin / ops / people type three values in. Healers lob coffee. Melee either cuts the
        // target out (marching ants, then the cut) or strikes across it. The last round arrives
        // when the Shot does, so the number and the flinch still land on it. (Rhythms after the
        // reference RPG's ability compositions: Repeat 5 x 0.1 s, Spread 5 x 20 deg, charge then orb.)

        enum Fire { Single, Type3, Fill5, Paste5, Sum, Trace, Lob, SlashH, SlashD, SlashX, Cut, Ability }

        class Volley { public Fire F; public Transform[] Parts; public bool[] Landed; public float Seed; }
        readonly Dictionary<Shot, Volley> _volleys = new();

        /// <summary>The body's attack for the hero's Excel attack (SdPose.Attack kinds 3–12); the old role pose otherwise.</summary>
        static int AttackPose(Combatant c)
        {
            if (c == null || c.side != Side.Hero) return 0;
            var f = FireOf(c, c.role is "ranged" or "healer" ? "shot" : "slash");
            return f switch
            {
                Fire.Type3 => 3, Fire.Fill5 => 4, Fire.Paste5 => 5, Fire.Sum => 6, Fire.Trace => 7, Fire.Lob => 8,
                Fire.SlashH => 9, Fire.SlashD => 10, Fire.SlashX => 11, Fire.Cut => 12,
                _ => SdPose.AttackOf(c.heroId, c.role),
            };
        }

        static Fire FireOf(Combatant c, string kind)
        {
            if (c == null || c.side != Side.Hero) return Fire.Single;
            var div = GameData.Hero(c.heroId)?.division;
            // each member their own move (the user: "no attack motion of their own") — the division's move as the
            // starting point, stepped through the pool by the member's hash, so a squad from one division no longer
            // swings as clones
            var h = SdPose.Hash(c.heroId);
            if (kind == "slash")
            {
                var melee = new[] { Fire.Cut, Fire.SlashX, Fire.SlashD, Fire.SlashH };
                var d0 = div switch { "finance" or "admin" or "exec" => 0, "tech" => 1, "ops" => 2, _ => 3 };
                return melee[(d0 + h % 3) % melee.Length];
            }
            if (c.role == "healer") return Fire.Lob;
            if (kind != "shot") return Fire.Single;
            var ranged = new[] { Fire.Fill5, Fire.Paste5, Fire.Sum, Fire.Trace, Fire.Type3 };
            var r0 = div switch { "tech" => 0, "market" => 1, "finance" => 2, "exec" => 3, _ => 4 };
            return ranged[(r0 + h % 4) % ranged.Length];
        }

        static readonly Dictionary<string, Material> FxMats = new();
        static Material FxMat(string name)
        {
            if (FxMats.TryGetValue(name, out var m) && m != null) return m;
            var tex = Resources.Load<Texture2D>("Art/Fx/" + name);
            return FxMats[name] = MeshKit.NewGlass(tex != null ? tex : MeshKit.Blob, Color.white);
        }

        static readonly float ShotCardScale = EnvF("EH_SHOTCARD", 1f);

        Transform Card(Transform parent, string tex, float w, float h)
        {
            var q = MeshKit.Part("card", parent, Quad, FxMat(tex), Layer).transform;
            if (tex.StartsWith("cell_") || tex is "result" or "formula") { w *= ShotCardScale; h *= ShotCardScale; }   // A/B hook (EH_SHOTCARD)
            q.localScale = new Vector3(w, h, 1f);
            q.gameObject.SetActive(false);
            return q;
        }

        Transform Bullet(Transform parent, Color c, float size)
        {
            var b = new GameObject("b") { layer = Layer }.transform;
            b.SetParent(parent, false);
            var part = MeshKit.Part("cell", b, Cell, MeshKit.Toon, Layer);
            var mpb = new MaterialPropertyBlock(); mpb.SetColor("_Color", MeshKit.Lin(c));
            part.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
            part.transform.localScale = Vector3.one * size;
            var glow = MeshKit.Part("glow", b, Quad, GlowMat(c), Layer).transform;
            glow.localScale = Vector3.one * 0.55f * size;
            b.gameObject.SetActive(false);
            return b;
        }

        Transform Streak(Transform parent, Color c)
        {
            var q = MeshKit.Part("streak", parent, Quad, GlowMat(c), Layer).transform;
            q.gameObject.SetActive(false);
            return q;
        }

        Transform Strip(Transform parent, string tex)
        {
            var q = MeshKit.Part("strip", parent, Quad, FxMat(tex), Layer).transform;
            q.gameObject.SetActive(false);
            return q;
        }

        /// <summary>Lays a camera-facing quad along a to b, `width` thick.</summary>
        void Lay(Transform q, Vector3 a, Vector3 b, float width)
        {
            var cam = _cam.transform; var d = b - a;
            var ang = Mathf.Atan2(Vector3.Dot(d, cam.up), Vector3.Dot(d, cam.right)) * Mathf.Rad2Deg;
            q.position = (a + b) * 0.5f;
            q.rotation = Quaternion.LookRotation(q.position - cam.position, cam.up) * Quaternion.Euler(0f, 0f, ang);
            q.localScale = new Vector3(Mathf.Max(0.01f, d.magnitude), width, 1f);
        }

        /// <summary>A camera-facing card at p, turned `roll` degrees in the picture plane.</summary>
        void Billboard(Transform q, Vector3 p, float roll)
        {
            q.position = p;
            q.rotation = Quaternion.LookRotation(p - _cam.transform.position, _cam.transform.up) * Quaternion.Euler(0f, 0f, roll);
        }

        Volley MakeVolley(Shot shot, Transform root, Color accent)
        {
            var f = FireOf(shot.From, shot.Kind);
            if (f == Fire.Single) return null;
            if (FireAbility(shot, f, accent))
            {
                // the ported ability system draws this one (AttackBook); the Shot itself shows nothing
                var av = new Volley { F = Fire.Ability, Parts = new Transform[0], Landed = new bool[0] };
                _volleys[shot] = av;
                return av;
            }
            var v = new Volley { F = f, Seed = Random.value * 100f };
            const float cw = 0.46f, ch = 0.23f;   // readable at the battle camera's distance
            Transform C(int i) => Card(root, "cell_" + (i % 3), cw, ch);
            switch (f)
            {
                case Fire.Type3: v.Parts = new[] { C(0), C(1), C(2) }; break;
                case Fire.Fill5: v.Parts = new Transform[5]; for (var i = 0; i < 5; i++) v.Parts[i] = C(i); break;
                case Fire.Paste5: v.Parts = new Transform[5]; for (var i = 0; i < 5; i++) v.Parts[i] = C(i + 1); break;
                case Fire.Sum: v.Parts = new[] { Card(root, "formula", 0.7f, 0.24f), Card(root, "result", 0.56f, 0.28f) }; break;
                case Fire.Trace: v.Parts = new[] { Strip(root, "solid"), Card(root, "trace_dot", 0.18f, 0.18f), Card(root, "trace_head", 0.28f, 0.28f) }; break;
                case Fire.Lob: v.Parts = new[] { Bullet(root, new Color(0.62f, 0.42f, 0.26f), 0.9f) }; break;
                case Fire.Cut: v.Parts = new[] { Strip(root, "ants"), Strip(root, "ants"), Strip(root, "ants"), Strip(root, "ants"), Streak(root, Color.white) }; break;
                case Fire.SlashX: v.Parts = new[] { Streak(root, accent), Streak(root, accent), Streak(root, Color.white) }; break;
                default: v.Parts = new[] { Streak(root, accent), Streak(root, Color.white) }; break;
            }
            v.Landed = new bool[v.Parts.Length];
            _volleys[shot] = v;
            return v;
        }

        static readonly float[] AnglesH = { 8f, 8f }, AnglesD = { -38f, -38f }, AnglesX = { -40f, 40f, 40f };

        void DrawVolley(Shot shot, Volley v, Actor from, Actor to)
        {
            if (v.F == Fire.Ability) return;
            var k = shot.Progress;
            var start = Muzzle(from);
            var end = to.Rig.Root.position + Vector3.up * to.Rig.Height * to.Scale * 0.5f;
            var cam = _cam.transform;
            var side = Vector3.Cross((end - start).normalized, cam.forward).normalized;
            void Fly(int i, float ki, float arc, float spin)
            {
                var q = v.Parts[i];
                var live = ki > 0f && ki < 1f;
                q.gameObject.SetActive(live);
                // the rounds before the last spark on arrival; the last one is the Shot landing (HitRing)
                if (ki >= 1f && !v.Landed[i]) { v.Landed[i] = true; if (i < v.Parts.Length - 1) Spark(to, Color.white, 0.35f); }
                if (!live) return;
                var p = Vector3.Lerp(start, end, ki) + Vector3.up * Mathf.Sin(ki * Mathf.PI) * arc;
                if (q.Find("glow") != null)
                {
                    q.position = p; q.rotation = Quaternion.Euler(_time * 720f, _time * 360f, 0f);
                    var g = q.Find("glow"); g.rotation = Quaternion.LookRotation(g.position - cam.position);
                }
                else Billboard(q, p, Mathf.Sin(_time * 9f + i * 1.7f + v.Seed) * spin);
            }
            switch (v.F)
            {
                case Fire.Type3:
                case Fire.Fill5:
                {
                    var n = v.Parts.Length; var gap = v.F == Fire.Type3 ? 0.2f : 0.12f; var g = gap * (n - 1);
                    for (var i = 0; i < n; i++) Fly(i, k * (1f + g) - i * gap, v.F == Fire.Fill5 ? 0.05f : 0.14f, 18f);
                    break;
                }
                case Fire.Paste5:
                    for (var i = 0; i < 5; i++)
                    {
                        Fly(i, k, 0.08f, 10f);
                        if (k < 1f) v.Parts[i].position += side * (i - 2) * 0.17f * Mathf.Sin(k * Mathf.PI * 0.9f) + Vector3.up * ((i % 2) - 0.5f) * 0.12f * k;
                    }
                    break;
                case Fire.Sum:
                {
                    // =SUM( gathers at the tablet, then the total flies
                    var fm = v.Parts[0]; var rs = v.Parts[1];
                    fm.gameObject.SetActive(k < 0.45f); rs.gameObject.SetActive(k >= 0.45f && k < 1f);
                    if (k < 0.45f)
                    {
                        var c = k / 0.45f;
                        Billboard(fm, start + Vector3.up * (0.12f + c * 0.08f), Mathf.Sin(_time * 30f) * 3f * c);
                        fm.localScale = new Vector3(0.7f, 0.24f, 1f) * Mathf.Lerp(0.5f, 1.25f, c * c);
                    }
                    else
                    {
                        var c = (k - 0.45f) / 0.55f;
                        Billboard(rs, Vector3.Lerp(start, end, c * c) + Vector3.up * Mathf.Sin(c * Mathf.PI) * 0.2f, -c * 25f);
                        rs.localScale = new Vector3(0.56f, 0.28f, 1f) * (1.2f + Mathf.Sin(c * Mathf.PI) * 0.3f);
                    }
                    break;
                }
                case Fire.Trace:
                {
                    // Excel's trace-precedents arrow: a dot on the source, the blue line drawn out to the
                    // target, the arrowhead riding its tip; it holds a beat, then thins away
                    var grow = Mathf.Clamp01(k / 0.55f); var tip = Vector3.Lerp(start, end, grow);
                    var fade = k < 0.55f ? 1f : 1f - (k - 0.55f) / 0.45f;
                    var line = v.Parts[0]; line.gameObject.SetActive(k < 1f); Lay(line, start, tip, 0.045f * (0.4f + fade * 0.6f));
                    var dot = v.Parts[1]; dot.gameObject.SetActive(k < 1f); Billboard(dot, start, 0f);
                    var d = tip - start; var ang = Mathf.Atan2(Vector3.Dot(d, cam.up), Vector3.Dot(d, cam.right)) * Mathf.Rad2Deg;
                    var head = v.Parts[2]; head.gameObject.SetActive(k < 1f); Billboard(head, tip, ang);
                    break;
                }
                case Fire.Lob: Fly(0, k, 1.1f, 0f); break;
                case Fire.Cut:
                {
                    // the target selected with marching ants, then cut through
                    var ctr = end + new Vector3(0f, 0.02f, -0.3f);
                    var hw = 0.42f * to.Scale; var hh = 0.52f * to.Scale;
                    var r = cam.right; var u = cam.up;
                    var c0 = ctr - r * hw - u * hh; var c1 = ctr + r * hw - u * hh; var c2 = ctr + r * hw + u * hh; var c3 = ctr - r * hw + u * hh;
                    var corners = new[] { c0, c1, c2, c3 };
                    var sel = k < 0.72f;
                    var mpb = new MaterialPropertyBlock();
                    for (var j = 0; j < 4; j++)
                    {
                        var q = v.Parts[j]; q.gameObject.SetActive(sel);
                        if (!sel) continue;
                        var a0 = corners[j]; var a1 = corners[(j + 1) % 4];
                        Lay(q, a0, a1, 0.03f);
                        var mr = q.GetComponent<MeshRenderer>(); mr.GetPropertyBlock(mpb);
                        mpb.SetVector("_MainTex_ST", new Vector4((a1 - a0).magnitude / 0.08f, 1f, -_time * 3f, 0f));
                        mr.SetPropertyBlock(mpb);
                    }
                    var cut = v.Parts[4]; var on = !sel && k < 1f; cut.gameObject.SetActive(on);
                    if (on) { var cc = (k - 0.72f) / 0.28f; Lay(cut, c3 + (c1 - c3) * 0f, c3 + (c1 - c3) * Mathf.SmoothStep(0f, 1f, cc * 1.4f), 0.06f * (1.3f - cc)); }
                    break;
                }
                default:
                {
                    // blades: a streak drawn across the target as the cut goes through it
                    var c = Mathf.SmoothStep(0f, 1f, k); var len = 0.9f * to.Scale; var ctr = end + new Vector3(0f, 0.05f, -0.25f);
                    var angles = v.F == Fire.SlashH ? AnglesH : v.F == Fire.SlashD ? AnglesD : AnglesX;
                    for (var j = 0; j < v.Parts.Length; j++)
                    {
                        var ang = angles[j] * Mathf.Deg2Rad; var dir = cam.right * Mathf.Cos(ang) + cam.up * Mathf.Sin(ang);
                        var delay = v.F == Fire.SlashX && j > 0 ? 0.35f : 0f;
                        var cj = Mathf.Clamp01((c - delay) / (1f - delay));
                        var show = cj > 0f && k < 1f;
                        v.Parts[j].gameObject.SetActive(show);
                        if (!show) continue;
                        var a = ctr - dir * len * 0.5f; var b = a + dir * len * cj;
                        var core = j == v.Parts.Length - 1;
                        Lay(v.Parts[j], a, b, (core ? 0.05f : 0.16f) * (1.2f - cj * 0.6f));
                    }
                    break;
                }
            }
        }

        static Mesh _cellMesh, _quadMesh;
        static readonly Dictionary<Color, Material> GlowMats = new();

        static Mesh Cell
        {
            get
            {
                if (_cellMesh != null) return _cellMesh;
                var b = new MeshKit.Builder();
                b.Box(Vector3.zero, new Vector3(0.14f, 0.1f, 0.03f), Color.white);
                return _cellMesh = b.Bake("cell");
            }
        }

        static Mesh Quad
        {
            get
            {
                if (_quadMesh != null) return _quadMesh;
                var b = new MeshKit.Builder();
                b.Quad(Vector3.zero, new Vector3(0.5f, 0f, 0f), new Vector3(0f, 0.5f, 0f), Color.white);
                return _quadMesh = b.Bake("quad");
            }
        }

        static Material GlowMat(Color c)
        {
            c.a = 0.85f;
            if (GlowMats.TryGetValue(c, out var m) && m != null) return m;
            return GlowMats[c] = MeshKit.NewGlass(MeshKit.Blob, c);
        }

        Transform MakeShot(Shot shot)
        {
            var from = shot.From != null && _actors.TryGetValue(shot.From, out var a) ? a : null;
            var accent = shot.Hostile ? new Color(1f, 0.3f, 0.3f)
                : shot.Kind == "heal" ? new Color(0.4f, 1f, 0.55f)
                : from?.Accent ?? new Color(0.3f, 0.8f, 1f);
            var root = new GameObject("shot:" + shot.Kind) { layer = Layer }.transform;
            root.SetParent(_root, false);
            if (MakeVolley(shot, root, accent) != null) return root;
            if (shot.Kind != "slash" && shot.Kind != "heal")
            {
                // the projectile is a cell torn off the sheet
                var cell = MeshKit.Part("cell", root, Cell, MeshKit.Toon, Layer);
                var mr = cell.GetComponent<MeshRenderer>();
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_Color", MeshKit.Lin(shot.Kind == "bar" ? new Color(0.3f, 0.85f, 0.45f) : accent));
                mr.SetPropertyBlock(mpb);
                if (shot.Kind == "bar") cell.transform.localScale = new Vector3(0.6f, 2.4f, 1f);
            }
            var glow = MeshKit.Part("glow", root, Quad, GlowMat(accent), Layer).transform;
            glow.localScale = Vector3.one * (shot.Kind == "slash" ? 1.1f : 0.55f);
            return root;
        }

        /// <summary>A blow landing: a white-core burst and a handful of spreadsheet cells flying off the target.</summary>
        void ImpactShards(Actor t, bool crit)
        {
            var at = Mid(t);
            // the burst: a cross of light the size of the target (a crit bigger and gold), 0.1 s
            var size = t.Rig.Height * t.Scale * (crit ? 1.1f : 0.8f);
            var core = FxPart("impact", Quad, GlowMat(crit ? new Color(1f, 0.85f, 0.3f) : new Color(0.3f, 0.95f, 1f)), Layer).transform;
            core.position = at;
            _fx.Add(new Fx { T = core, Life = 0.1f, Max = 0.1f, Grow0 = size * 0.5f, Grow1 = size * 0.9f, Face = true });
            foreach (var roll in new[] { 0f, 90f })
            {
                var q = FxPart("impact", Quad, GlowMat(Color.white), Layer).transform;
                q.position = at;
                _fx.Add(new Fx { T = q, Life = 0.09f, Max = 0.09f, Aspect = new Vector2(1f, 0.1f), Grow0 = size, Grow1 = size * 1.4f, Face = true, Roll = roll + (crit ? 45f : 15f) });
            }
            if (crit) _punch = 1f;
            var n = crit ? 8 : 5;
            var col = crit ? new Color(1f, 0.85f, 0.3f) : new Color(0.55f, 0.95f, 0.7f);
            for (var i = 0; i < n; i++)
            {
                var shard = FxPart("shard", Cell, MeshKit.Toon, Layer).transform;
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_Color", MeshKit.Lin(i % 2 == 0 ? Color.white : col));
                shard.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
                shard.position = at;
                var ang = (i / (float)n + Random.value * 0.15f) * Mathf.PI * 2f;
                var away = t.C.side == Side.Monster ? 1f : -1f;
                var vel = new Vector3(Mathf.Cos(ang) * 1.6f + away * 1.2f, 1.4f + Mathf.Abs(Mathf.Sin(ang)) * 1.6f, Mathf.Sin(ang) * 0.9f);
                _fx.Add(new Fx { T = shard, Life = 0.28f, Max = 0.28f, Vel = vel, Gravity = true, Grow0 = crit ? 0.55f : 0.4f, Grow1 = 0.1f, Spin = true });
            }
        }

        /// <summary>The flash at the muzzle as a shot leaves: a bright cross over a soft glow, 0.06 s.</summary>
        void MuzzleFlash(Actor a, bool hostile)
        {
            var p = Muzzle(a);
            var c = hostile ? new Color(1f, 0.4f, 0.35f) : new Color(0.3f, 1f, 0.6f);
            var glow = FxPart("muzzle", Quad, GlowMat(c), Layer).transform;
            glow.position = p;
            _fx.Add(new Fx { T = glow, Life = 0.08f, Max = 0.08f, Grow0 = 0.5f, Grow1 = 0.15f, Face = true });
            foreach (var roll in new[] { 0f, 90f })
            {
                var q = FxPart("muzzle", Quad, GlowMat(Color.white), Layer).transform;
                q.position = p;
                _fx.Add(new Fx { T = q, Life = 0.06f, Max = 0.06f, Aspect = new Vector2(0.55f, 0.08f), Grow0 = 1f, Grow1 = 0.3f, Face = true, Roll = roll + 45f });
            }
        }

        /// <summary>An enemy's end: a flash, a ring on the street and its body's cells flying off in its colours.</summary>
        void DeathPop(Actor a)
        {
            var centre = a.Rig.Root.position + Vector3.up * a.Rig.Height * 0.4f;
            Spark(a, Color.white, a.C.boss != null ? 3.2f : 1.9f);
            var ringCol = new Color(1f, 0.55f, 0.4f, 0.95f);
            if (!RingMats.TryGetValue(ringCol, out var ring) || ring == null) RingMats[ringCol] = ring = MeshKit.NewGlass(RingTex, ringCol);
            var floor = FxPart("ring", FloorQuad, ring, Layer).transform;
            floor.position = new Vector3(centre.x, a.Rig.Root.parent != null ? a.Rig.Root.parent.position.y + 0.02f : 0.02f, centre.z);
            _fx.Add(new Fx { T = floor, Life = 0.5f, Max = 0.5f, Grow0 = 0.3f, Grow1 = 2.4f, Flat = true });
            var cols = new[] { new Color(0.95f, 0.95f, 0.98f), a.Accent, new Color(1f, 0.85f, 0.4f) };
            var n = a.C.boss != null ? 22 : 12;
            for (var i = 0; i < n; i++)
            {
                var shard = FxPart("shard", Cell, MeshKit.Toon, Layer).transform;
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_Color", MeshKit.Lin(cols[i % cols.Length]));
                shard.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
                shard.position = centre;
                var ang = i / (float)n * Mathf.PI * 2f;
                var vel = new Vector3(Mathf.Cos(ang) * 2.4f, 2.2f + (i % 4) * 0.6f, Mathf.Sin(ang) * 1.4f);
                _fx.Add(new Fx { T = shard, Life = 0.7f, Max = 0.7f, Vel = vel, Gravity = true, Grow0 = 1.1f, Grow1 = 0.3f, Spin = true });
            }
            AddShakeLocal(a.C.boss != null ? 0.6f : 0.16f);
        }

        void Spark(Actor at, Color c, float size, bool rise = false)
        {
            var t = _sparkPool.Count > 0 ? _sparkPool.Pop() : MeshKit.Part("spark", _root, Quad, GlowMat(Color.white), Layer).transform;
            t.gameObject.SetActive(true);
            t.GetComponent<MeshRenderer>().sharedMaterial = GlowMat(c);
            t.position = at.Rig.Root.position + Vector3.up * at.Rig.Height * at.Scale * 0.55f + new Vector3(0f, 0f, -0.3f);
            t.localScale = Vector3.one * size * 0.3f;
            _sparks.Add((t, 0.28f, 0.28f, rise ? Vector3.up * 1.2f : Vector3.zero, size));
        }

        static Texture2D _ringTex;
        static Texture2D RingTex
        {
            get
            {
                if (_ringTex != null) return _ringTex;
                const int n = 128;
                _ringTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "ring" };
                var px = new Color32[n * n];
                for (var y = 0; y < n; y++)
                    for (var x = 0; x < n; x++)
                    {
                        var d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                        var a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) / 0.12f);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
                    }
                _ringTex.SetPixels32(px);
                _ringTex.Apply(true);
                return _ringTex;
            }
        }

        static readonly Dictionary<Color, Material> RingMats = new();

        /// <summary>
        /// EX skill: a ring bursting out on the floor, a second one standing up behind the figure,
        /// a flash, and the sheet's cells flying off in the character's colour.
        /// </summary>
        void SkillBurst(Actor a)
        {
            var c = a.Accent; c.a = 0.95f;
            if (!RingMats.TryGetValue(c, out var ring) || ring == null) RingMats[c] = ring = MeshKit.NewGlass(RingTex, c);
            var centre = a.Rig.Root.position + Vector3.up * a.Rig.Height * a.Scale * 0.5f;

            var floor = FxPart("ring", FloorQuad, ring, Layer).transform;
            floor.position = a.Rig.Root.position + Vector3.up * 0.02f;
            _fx.Add(new Fx { T = floor, Life = 0.6f, Max = 0.6f, Grow0 = 0.4f, Grow1 = 3.2f, Flat = true });

            var up = FxPart("ring", Quad, ring, Layer).transform;
            up.position = centre + new Vector3(0f, 0f, 0.3f);
            _fx.Add(new Fx { T = up, Life = 0.5f, Max = 0.5f, Grow0 = 0.5f, Grow1 = 2.6f, Face = true });

            Spark(a, Color.white, 2.2f);
            for (var i = 0; i < 14; i++)
            {
                var shard = FxPart("shard", Cell, MeshKit.Toon, Layer).transform;
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_Color", MeshKit.Lin(Color.Lerp(a.Accent, Color.white, (i % 3) * 0.25f)));
                shard.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
                shard.position = (a.Rig.Sheet != null ? a.Rig.Sheet.position : centre);
                var ang = i / 14f * Mathf.PI * 2f;
                var vel = new Vector3(Mathf.Cos(ang) * 2.2f, 2.4f + (i % 4) * 0.5f, Mathf.Sin(ang) * 1.2f);
                _fx.Add(new Fx { T = shard, Life = 0.8f, Max = 0.8f, Vel = vel, Gravity = true, Grow0 = 0.9f, Grow1 = 0.4f, Spin = true });
            }
            AddShakeLocal(0.35f);
        }

        /// <summary>
        /// A hit landing (the boss-fight mock-ups, Blue Archive style): a thin white-cyan ring
        /// spreading on the floor under the target and a smaller one standing at the hit, facing
        /// the camera. Gold on a crit. At most one pair per target every 0.12 s, so a flurry reads
        /// as rhythm rather than a pile of rings.
        /// </summary>
        // ------------------------------------------------------------------ skills --
        // Every EX skill looked the same (one generic burst). Each now has its own, in the sheet's
        // own vocabulary: a beam for strike, a crosshair and #DIV/0! for execute, a row selected
        // across the whole enemy line for sweep, Ctrl+A over everything for ult, trace arrows
        // hopping target to target for chain, red cells flying home for drain, red conditional
        // formatting on the burning, + cells for heals, arrows up for buff and haste, a green cell
        // border round each member for barrier, a red ! for taunt, a gold pillar for revive.
        // The sim queues a skill's damage BEFORE its Skill event, so the screen marks the caster
        // first (MarkSkill) and the batch's hits and heals pick the skill up.

        (Actor A, string Type, Actor Prev, int N) _cast;

        public void MarkSkill(Combatant actor, string text)
        {
            if (actor == null || actor.side != Side.Hero || !_actors.TryGetValue(actor, out var a)) return;
            var def = GameData.Hero(actor.heroId);
            if (def == null || def.skillName != text) return;   // 엄호 (the tank covering) is a Skill event too, not an EX
            _cast = (a, def.skillType, a, 0);
        }

        public void EndSkillBatch() => _cast = default;

        static readonly Color Green = new(0.2f, 0.72f, 0.42f), Red = new(1f, 0.33f, 0.3f), Gold = new(1f, 0.82f, 0.3f), Cyan = new(0.35f, 0.85f, 1f), Blue = new(0.2f, 0.5f, 0.95f);

        Vector3 Mid(Actor a) => a.Rig.Root.position + Vector3.up * a.Rig.Height * a.Scale * 0.55f + new Vector3(0f, 0f, -0.3f);

        // Effect quads are pooled per (mesh, material): a hit makes a floor ring, a face ring and
        // six to nine streaks, and creating then destroying that many GameObjects every hit was
        // behind the fight's worst frames (PerfProbe, 25–30 ms spikes).
        readonly Dictionary<(Mesh, Material), Stack<Transform>> _fxPool = new();
        readonly Dictionary<Transform, (Mesh, Material)> _fxKey = new();

        Transform FxPart(string name, Mesh mesh, Material mat, int layer)
        {
            var key = (mesh, mat);
            Transform t = null;
            if (_fxPool.TryGetValue(key, out var st)) while (st.Count > 0 && t == null) t = st.Pop();
            if (t == null) { t = MeshKit.Part(name, _root, mesh, mat, layer).transform; _fxKey[t] = key; }
            t.gameObject.SetActive(true);
            t.localScale = Vector3.one; t.localRotation = Quaternion.identity;
            return t;
        }

        void FxRelease(Transform t)
        {
            if (!_fxKey.TryGetValue(t, out var key)) { Object.Destroy(t.gameObject); return; }
            t.gameObject.SetActive(false);
            var mr = t.GetComponent<MeshRenderer>(); if (mr != null) mr.SetPropertyBlock(null);
            if (!_fxPool.TryGetValue(key, out var st)) _fxPool[key] = st = new Stack<Transform>();
            st.Push(t);
        }

        static readonly MaterialPropertyBlock _fxBlock = new();   // reused: a new block per effect per frame was garbage

        void TintFx(Transform t, Color c, float alpha)
        {
            var mr = t.GetComponent<MeshRenderer>(); if (mr == null) return;
            var mb = _fxBlock; mr.GetPropertyBlock(mb);
            var l = MeshKit.Lin(c); l.a = c.a * Mathf.Clamp01(alpha);
            mb.SetColor("_Color", l); mr.SetPropertyBlock(mb);
        }

        void FxCard(string tex, Vector3 p, float w, float h, float life, Vector3 vel, Color tint, float delay = 0f, float g0 = 0.6f, float g1 = 1f, float roll = 0f)
        {
            var q = FxPart("fxcard", Quad, FxMat(tex), Layer).transform;
            q.position = p; q.gameObject.SetActive(delay <= 0f);
            TintFx(q, tint, 1f);
            _fx.Add(new Fx { T = q, Life = life, Max = life, Vel = vel, Aspect = new Vector2(w, h), Grow0 = g0, Grow1 = g1, Fade = true, Tint = tint, Delay = delay, Roll = roll });
        }

        void FxLine(string tex, Vector3 a, Vector3 b, float w, float life, Color tint, float delay = 0f)
        {
            var q = FxPart("fxline", Quad, tex == null ? GlowMat(tint) : FxMat(tex), Layer).transform;
            q.gameObject.SetActive(delay <= 0f);
            Lay(q, a, b, w);
            if (tex != null) TintFx(q, tint, 1f);
            _fx.Add(new Fx { T = q, Life = life, Max = life, Line = true, A = a, B = b, W = w, Fade = tex != null, Tint = tint, Delay = delay });
        }

        void FxRect(Vector3 c, float hw, float hh, float w, float life, Color tint, float delay = 0f)
        {
            var r = _cam.transform.right; var u = _cam.transform.up;
            var p = new[] { c - r * hw - u * hh, c + r * hw - u * hh, c + r * hw + u * hh, c - r * hw + u * hh };
            for (var i = 0; i < 4; i++) FxLine("white", p[i], p[(i + 1) % 4], w, life, tint, delay);
        }

        void FloorRing(Vector3 at, Color c, float life, float g0, float g1)
        {
            if (!RingMats.TryGetValue(c, out var ring) || ring == null) RingMats[c] = ring = MeshKit.NewGlass(RingTex, c);
            var floor = FxPart("ring", FloorQuad, ring, Layer).transform;
            floor.position = at + Vector3.up * 0.03f;
            _fx.Add(new Fx { T = floor, Life = life, Max = life, Grow0 = g0, Grow1 = g1, Flat = true });
        }

        // the skill's own hits may just have killed them: those falling still count as its targets
        IEnumerable<Actor> Living(Side side) => _actors.Values.Where(x => x.C.side == side && (x.C.Alive || x.Dying > 0f));

        void SkillCast(Actor a, string type)
        {
            var up = Vector3.up;
            switch (type)
            {
                case "sweep":
                {
                    var foes = Living(Side.Monster).ToList(); if (foes.Count == 0) break;
                    var y = foes.Average(f => Mid(f).y); var z = foes.Average(f => Mid(f).z);
                    var x0 = foes.Min(f => Mid(f).x) - 0.5f; var x1 = foes.Max(f => Mid(f).x) + 0.5f;
                    if (x1 - x0 < 3.2f) { var cx = (x0 + x1) * 0.5f; x0 = cx - 1.6f; x1 = cx + 1.6f; }   // one big target still reads as a row
                    // the whole row selected: a green band across the line, a white core through it
                    FxLine("white", new Vector3(x0, y, z), new Vector3(x1, y, z), 0.42f, 0.4f, new Color(Green.r, Green.g, Green.b, 0.4f));
                    FxLine(null, new Vector3(x0, y, z), new Vector3(x1, y, z), 0.1f, 0.3f, Color.white);
                    AddShakeLocal(0.3f);
                    break;
                }
                case "ult":
                {
                    var foes = Living(Side.Monster).ToList(); if (foes.Count == 0) break;
                    var c = foes.Aggregate(Vector3.zero, (s, f) => s + Mid(f)) / foes.Count;
                    var hw = (foes.Max(f => Mid(f).x) - foes.Min(f => Mid(f).x)) * 0.5f + 0.7f;
                    // Ctrl+A: the selection over everything, a pale blue fill inside a blue border
                    FxCard("white", c, hw * 2f, 1.6f, 0.55f, Vector3.zero, new Color(0.55f, 0.75f, 1f, 0.32f), g0: 1f, g1: 1f);
                    FxRect(c, hw, 0.8f, 0.05f, 0.55f, Blue);
                    AddShakeLocal(0.55f);
                    break;
                }
                case "burn":
                    foreach (var f in Living(Side.Monster))
                    {
                        var m = Mid(f);
                        // red conditional formatting on every burning target, flickering out
                        FxCard("white", m, 0.9f * f.Scale, 1.1f * f.Scale, 1.4f, Vector3.zero, new Color(1f, 0.35f, 0.25f, 0.34f), g0: 0.9f, g1: 1.05f);
                        for (var i = 0; i < 4; i++) Spark(f, new Color(1f, 0.55f, 0.2f), 0.5f, rise: true);
                    }
                    break;
                case "buff":
                case "haste":
                {
                    var c = type == "buff" ? Gold : Cyan;
                    foreach (var h in Living(Side.Hero))
                        for (var i = 0; i < 3; i++)
                            FxCard("arrow_up", Mid(h) + new Vector3((i - 1) * 0.28f, -0.3f, 0f), 0.2f, 0.2f, 0.7f, up * 1.6f, c, delay: i * 0.08f);
                    break;
                }
                case "barrier":
                    foreach (var h in Living(Side.Hero))
                    {
                        // a thick green cell border round each member, left up a beat
                        FxRect(Mid(h), 0.34f * h.Scale, 0.55f * h.Scale, 0.06f, 1.3f, Green);
                        FxCard("white", Mid(h), 0.68f * h.Scale, 1.1f * h.Scale, 1.3f, Vector3.zero, new Color(0.6f, 1f, 0.75f, 0.16f), g0: 1f, g1: 1f);
                    }
                    break;
                case "taunt":
                    FxCard("bang", Mid(a) + up * 0.7f, 0.42f, 0.42f, 1f, up * 0.5f, Color.white, g0: 0.4f, g1: 1.1f);
                    FloorRing(a.Rig.Root.position, new Color(1f, 0.35f, 0.3f, 0.9f), 0.7f, 0.5f, 4f);
                    AddShakeLocal(0.3f);
                    break;
                case "cleanse":
                    foreach (var h in Living(Side.Hero)) { FloorRing(h.Rig.Root.position, new Color(1f, 1f, 1f, 0.9f), 0.5f, 0.3f, 1.8f); Spark(h, Color.white, 1.2f, rise: true); }
                    break;
                case "heal":
                case "drain":
                case "revive":
                    FloorRing(a.Rig.Root.position, new Color(0.45f, 1f, 0.6f, 0.9f), 0.6f, 0.4f, 3f);
                    break;
                case "strike":
                case "execute":
                case "chain":
                    FloorRing(a.Rig.Root.position, new Color(a.Accent.r, a.Accent.g, a.Accent.b, 0.9f), 0.45f, 0.4f, 2.2f);
                    break;
            }
        }

        void SkillHit(string type, Actor a, Actor t)
        {
            var from = Mid(a); var to = Mid(t);
            switch (type)
            {
                case "strike":
                    FxLine(null, from, to, 0.34f, 0.28f, a.Accent);
                    FxLine(null, from, to, 0.1f, 0.22f, Color.white);
                    AddShakeLocal(0.35f);
                    break;
                case "execute":
                {
                    var r = _cam.transform.right; var u = _cam.transform.up;
                    FxLine("white", to - r * 0.55f, to + r * 0.55f, 0.03f, 0.45f, Red);
                    FxLine("white", to - u * 0.55f, to + u * 0.55f, 0.03f, 0.45f, Red);
                    FxLine(null, from, to, 0.06f, 0.2f, Color.white);
                    FxCard("err_div0", to + Vector3.up * 0.55f, 0.6f, 0.3f, 1f, Vector3.up * 0.45f, Color.white, g0: 0.5f, g1: 1.1f, roll: -6f);
                    break;
                }
                case "chain":
                {
                    // hop by hop, each a trace arrow from the last target
                    var p = Mid(_cast.Prev); var d = _cast.N * 0.09f;
                    FxLine("solid", p, to, 0.05f, 0.45f, Color.white, d);
                    var dd = to - p; var ang = Mathf.Atan2(Vector3.Dot(dd, _cam.transform.up), Vector3.Dot(dd, _cam.transform.right)) * Mathf.Rad2Deg;
                    FxCard("trace_head", to, 0.3f, 0.3f, 0.45f, Vector3.zero, Color.white, d, 1f, 1f, ang);
                    FxCard("trace_dot", p, 0.2f, 0.2f, 0.45f, Vector3.zero, Color.white, d, 1f, 1f);
                    _cast.Prev = t;
                    break;
                }
                case "drain":
                    FxCard("cell_1", to, 0.4f, 0.2f, 0.5f, (from - to) / 0.5f, Red, _cast.N * 0.05f, 0.8f, 0.6f);
                    break;
                case "ult":
                    FloorRing(t.Rig.Root.position, new Color(0.7f, 0.85f, 1f, 0.9f), 0.4f, 0.3f, 2f);
                    break;
            }
            _cast.N++;
        }

        void SkillHeal(string type, Actor h)
        {
            if (type == "revive")
            {
                var b = h.Rig.Root.position;
                FxLine(null, b, b + Vector3.up * 3f, 0.7f, 0.9f, Gold);
                FxLine(null, b, b + Vector3.up * 3f, 0.2f, 0.8f, Color.white);
                return;
            }
            if (type is "heal" or "cleanse" or "drain")
                FxCard("plus_cell", Mid(h) + Vector3.up * 0.3f, 0.3f, 0.24f, 0.9f, Vector3.up * 0.7f, Color.white, _cast.N++ * 0.05f, 0.5f, 1f);
        }


        // ------------------------------------------------------------------ ported ability system --
        // Attacks and boss patterns are compositions of the effects ported from OperationKivotos
        // (Assets/ExcelHeroes/Scripts/Ability, see THIRD_PARTY_NOTICES.md): Repeat, Spread, Delay,
        // SpawnVFX, SpawnProjectiles, AreaStrike, ScatterPattern, RadialBurstPattern. The sim still
        // lands every hit; each composition is timed so its last round / last blast falls on it.
        // BattleWorld is the presenter: Excel cells for bullets, #REF! discs for warnings.

        Vector3 Muzzle(Actor a) => a.Rig.HandR != null && a.C.side == Side.Hero
            ? a.Rig.HandR.position + Vector3.up * 0.05f
            : a.Rig.Root.position + Vector3.up * a.Rig.Height * a.Scale * 0.55f;

        // the basic-attack compositions (the Shot flies 0.28 s for a ranged hero)
        static (AbilityData ab, float flight, string payload)? _type3, _fill5, _paste5, _sum, _lob;

        static (AbilityData, float, string) Book(Fire f) => f switch
        {
            Fire.Type3 => _type3 ??= (AbilityData.Make("셀 입력", 0f, RepeatEffect.Make(3, 0.05f, SpawnProjectiles.Make())), 0.18f, "cell"),
            Fire.Fill5 => _fill5 ??= (AbilityData.Make("자동 채우기", 0f, RepeatEffect.Make(5, 0.03f, SpawnProjectiles.Make())), 0.16f, "cell"),
            Fire.Paste5 => _paste5 ??= (AbilityData.Make("범위 붙여넣기", 0f, SpreadProjectiles.Make(5, 26f)), 0.28f, "cell"),
            Fire.Sum => _sum ??= (AbilityData.Make("=SUM", 0f, SpawnVFX.Make("charge", Anchor.At(Anchor.Source.Muzzle)), DelayEffect.Make(0.12f), SpawnProjectiles.Make()), 0.16f, "result"),
            Fire.Lob => _lob ??= (AbilityData.Make("커피", 0f, SpawnProjectiles.Make()), 0.28f, "coffee"),
            _ => (null, 0f, null),
        };

        readonly AbilityRunner _runner = new AbilityRunner();
        System.Threading.CancellationTokenSource _abCts = new();

        bool FireAbility(Shot shot, Fire f, Color accent)
        {
            var (ab, flight, payload) = Book(f);
            if (ab == null || !_actors.TryGetValue(shot.From, out var from) || !_actors.TryGetValue(shot.To, out var to)) return false;
            var ctx = new AbilityContext
            {
                CasterGO = from.Rig.Root.gameObject, Object = from.Rig.HandR != null ? from.Rig.HandR : from.Rig.Root,
                Target = to.Rig.Root.gameObject, TargetPoint = to.Rig.Root.position,
                Accent = accent, Flight = flight, Payload = payload,
            };
            _runner.Fire(ab, ctx, _abCts.Token).Forget();
            return true;
        }

        class AbShot { public Transform T; public Vector3 A; public Actor To; public Vector3 B; public float Time, Flight, Lateral, Seed; public string Payload; }
        readonly List<AbShot> _abShots = new();

        Actor ActorOf(GameObject go) => go == null ? null : _actors.Values.FirstOrDefault(a => a.Rig.Root.gameObject == go);

        public void Projectile(AbilityContext ctx, Vector3 from, Vector3 to, float flight, float lateral)
        {
            ShotCount++;
            var target = ActorOf(ctx.Target);
            var caster = ActorOf(ctx.CasterGO);
            if (caster != null) from = Muzzle(caster);
            Transform t = ctx.Payload switch
            {
                "coffee" => Bullet(_root, new Color(0.62f, 0.42f, 0.26f), 0.9f),
                "result" => Card(_root, "result", 0.56f, 0.28f),
                _ => Card(_root, "cell_" + Random.Range(0, 3), 0.46f, 0.23f),
            };
            t.gameObject.SetActive(true);
            _abShots.Add(new AbShot { T = t, A = from, To = target, B = to, Flight = Mathf.Max(0.05f, flight), Lateral = lateral, Payload = ctx.Payload, Seed = Random.value * 10f });
        }

        void UpdateAbilityShots(float dt)
        {
            var cam = _cam.transform;
            for (var i = _abShots.Count - 1; i >= 0; i--)
            {
                var s = _abShots[i];
                s.Time += dt;
                var k = s.Time / s.Flight;
                if (s.T == null || k >= 1f)
                {
                    if (s.To != null && s.T != null) Spark(s.To, Color.white, 0.3f);
                    if (s.T != null) Object.Destroy(s.T.gameObject);
                    _abShots.RemoveAt(i);
                    continue;
                }
                var end = s.To != null ? s.To.Rig.Root.position + Vector3.up * s.To.Rig.Height * s.To.Scale * 0.5f : s.B;
                var side = Vector3.Cross((end - s.A).normalized, cam.forward).normalized;
                var arc = s.Payload == "coffee" ? 1.1f : s.Payload == "result" ? 0.22f : 0.1f;
                var p = Vector3.Lerp(s.A, end, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * arc
                      + side * s.Lateral * (end - s.A).magnitude * Mathf.Sin(k * Mathf.PI * 0.9f);
                if (s.T.Find("glow") != null)
                {
                    s.T.position = p; s.T.rotation = Quaternion.Euler(_time * 720f, _time * 360f, 0f);
                    var g = s.T.Find("glow"); g.rotation = Quaternion.LookRotation(g.position - cam.position);
                }
                else Billboard(s.T, p, Mathf.Sin(_time * 9f + s.Seed) * 14f);
                _abShots[i] = s;
            }
        }

        class AbWarn { public Transform Disc, Ring; public float Time, Seconds, Radius; public bool Danger; }
        readonly List<AbWarn> _abWarns = new();

        public static int WarnCount, ShotCount;
        public void Warn(Vector3 at, float radius, float seconds, bool danger)
        {
            WarnCount++;
            at.y = _root.position.y + 0.06f;   // (the stage's floor, not world 0) over the telegraph's red floor, in amber, so the incoming strikes read against it
            var disc = MeshKit.Part("abwarn", _root, FloorQuad, DiscMat, Layer).transform;
            var ring = MeshKit.Part("abwarn", _root, FloorQuad, RingMat(danger ? new Color(1f, 0.78f, 0.25f, 1f) : new Color(0.8f, 0.6f, 1f, 1f)), Layer).transform;
            disc.position = ring.position = at;
            _abWarns.Add(new AbWarn { Disc = disc, Ring = ring, Seconds = Mathf.Max(0.05f, seconds), Radius = radius, Danger = danger });
        }

        void UpdateAbilityWarns(float dt)
        {
            for (var i = _abWarns.Count - 1; i >= 0; i--)
            {
                var w = _abWarns[i];
                w.Time += dt;
                var k = w.Time / w.Seconds;
                if (k >= 1f || w.Disc == null)
                {
                    if (w.Disc != null) Object.Destroy(w.Disc.gameObject);
                    if (w.Ring != null) Object.Destroy(w.Ring.gameObject);
                    _abWarns.RemoveAt(i);
                    continue;
                }
                // the disc fills in toward the ring as the strike comes (the reference's warning decal)
                w.Ring.localScale = Vector3.one * w.Radius * 2f;
                w.Disc.localScale = Vector3.one * w.Radius * 2f * Mathf.Lerp(0.15f, 1f, k);
                TintFx(w.Disc, w.Danger ? new Color(1f, 0.62f, 0.2f, 0.35f + k * 0.4f) : new Color(0.62f, 0.4f, 1f, 0.25f + k * 0.3f), 1f);
            }
        }

        public void Blast(Vector3 at, float radius, Color color)
        {
            at.y = _root.position.y;
            FloorRing(at, color, 0.3f, radius * 0.6f, radius * 2.4f);
            // a round flash (no Fx texture called "blob": FxMat falls back to the soft round Blob)
            FxCard("blob", at + Vector3.up * 0.3f, radius * 1.6f, radius * 1.1f, 0.2f, Vector3.up * 0.6f, new Color(1f, 0.8f, 0.55f, 0.75f), g0: 0.5f, g1: 1.5f);
            AddShakeLocal(0.12f);
        }

        public void Vfx(string name, Vector3 at, AbilityContext ctx)
        {
            var caster = ActorOf(ctx.CasterGO);
            if (caster != null) at = Muzzle(caster);
            switch (name)
            {
                case "charge": FxCard("formula", at + Vector3.up * 0.15f, 0.7f, 0.24f, 0.16f, Vector3.up * 0.3f, Color.white, g0: 0.5f, g1: 1.25f); break;
                default: FxCard("white", at, 0.14f, 0.14f, 0.08f, Vector3.zero, new Color(1f, 1f, 1f, 0.8f)); break;
            }
        }

        /// <summary>
        /// The boss special as a strike pattern, timed so its last blast lands on the sim's hit: the
        /// hit comes on the boss's next swing (its attack timer) plus the swing's own 0.5 s drop.
        /// </summary>
        void BossStrikes(Actor boss, string name, string kindOverride = null, float hitOverride = -1f)
        {
            var kind = kindOverride ?? boss.C.boss?.specials?.FirstOrDefault(x => x.name == name)?.kind;
            if (kind == null) return;
            var speed = Mathf.Max(0.2f, 1f + boss.C.hasteAmount - boss.C.slowAmount);
            var hitIn = hitOverride > 0f ? hitOverride : boss.C.attackTimer / speed + 0.5f;
            var squad = Living(Side.Hero).Where(h => h.C.Alive).OrderByDescending(h => h.C.x).ToList();
            if (squad.Count == 0) return;
            void Run(AbilityData ab, Actor on, Vector3 point)
            {
                var ctx = new AbilityContext { CasterGO = boss.Rig.Root.gameObject, Object = boss.Rig.Root, Target = on?.Rig.Root.gameObject, TargetPoint = point };
                _runner.Fire(ab, ctx, _abCts.Token).Forget();
                Object.Destroy(ab, hitIn + 3f);
            }
            switch (kind)
            {
                case "volley":
                    foreach (var h in squad)
                        Run(AbilityData.Make("volley", 0f, DelayEffect.Make(Mathf.Max(0f, hitIn - 0.8f)), AreaStrike.Make(0.8f, 0.7f * h.Scale)), h, h.Rig.Root.position);
                    break;
                case "throw":
                {
                    var back = squad[squad.Count - 1];
                    Run(AbilityData.Make("throw", 0f, DelayEffect.Make(Mathf.Max(0f, hitIn - 0.9f)), AreaStrike.Make(0.9f, 1f)), back, back.Rig.Root.position);
                    break;
                }
                case "sweep":
                    foreach (var h in squad.Take(2))
                        Run(AbilityData.Make("sweep", 0f, DelayEffect.Make(Mathf.Max(0f, hitIn - 0.85f)),
                            ScatterPattern.Make(Anchor.At(Anchor.Source.TargetPoint), 0f, 0.9f, 4, 0.06f, ForkEffect.Make(AreaStrike.Make(0.6f, 0.45f)))), h, h.Rig.Root.position);
                    break;
                case "stomp":
                {
                    // converging from outside in over the whole squad (the reference's RadialBurst)
                    var c = squad.Aggregate(Vector3.zero, (acc, h) => acc + h.Rig.Root.position) / squad.Count;
                    const int waves = 4; const float gap = 0.12f, strike = 0.4f;
                    var span = waves * gap + strike;
                    Run(AbilityData.Make("stomp", 0f, DelayEffect.Make(Mathf.Max(0f, hitIn - span)),
                        RadialBurstPattern.Make(Anchor.At(Anchor.Source.TargetPoint), 6, waves, 3.4f, 0.6f, 30f, gap, ForkEffect.Make(AreaStrike.Make(strike, 0.55f)))), null, c);
                    break;
                }
            }
        }

        public void ResetAbilities()
        {
            _abCts.Cancel(); _abCts.Dispose(); _abCts = new System.Threading.CancellationTokenSource();
            foreach (var s2 in _abShots) if (s2.T != null) Object.Destroy(s2.T.gameObject);
            _abShots.Clear();
            foreach (var w in _abWarns) { if (w.Disc != null) Object.Destroy(w.Disc.gameObject); if (w.Ring != null) Object.Destroy(w.Ring.gameObject); }
            _abWarns.Clear();
        }

        // ------------------------------------------------------------------ boss telegraphs --
        // A boss announces its next special one swing ahead (EventKind.Warn); until it lands the
        // floor says where: red conditional-format discs under whoever it will hit, #REF! over
        // their heads, pulsing faster as it comes. The shapes follow what the move does in the sim
        // (FireBossMove): volley = every member, sweep = the front two, stomp = one ring over the
        // whole squad, throw = the backmost. A move that hits nobody (slow, shield, heal, summon)
        // marks the boss itself. When the Skill event fires the marks flash and go.
        // (After the reference RPG's AreaStrike: warning decal, a delay, then the hit.)

        class Tele { public Actor Boss; public string Name; public bool Danger; public List<(Transform t, Actor on, float size)> Marks = new(); public float T; }
        readonly List<Tele> _teles = new();
        public int TelegraphCount => _teles.Count;

        static Material _discMat;
        static Material DiscMat => _discMat != null ? _discMat : _discMat = MeshKit.NewGlass(MeshKit.Blob, Color.white);

        /// <summary>Capture pass only: the floor marks of `kind` for the boss on the field, replacing any shown.</summary>
        public string DebugFacing()
        {
            var sb = new System.Text.StringBuilder($"cam {_cam.transform.position} fwd {_cam.transform.forward} | ");
            foreach (var a in _actors.Values) if (a.C.side == Side.Hero) sb.Append($"{a.C.heroId} yaw {a.Rig.Root.eulerAngles.y:0} fwd {a.Rig.Root.forward} model {(a.Rig.Model != null ? a.Rig.Model.forward.ToString() : "-")}; ");
            return sb.ToString();
        }

        static Material _castMat;

        /// <summary>
        /// The figure's cast shadow on the street (PlanarShadow.shader): each of its skinned meshes drawn once
        /// more on the same bones, flattened along the key light onto the ground. The shadow blob stays for the
        /// contact under the feet.
        /// </summary>
        static void CastShadow(ChibiRig rig)
        {
            var sh = Shader.Find("ExcelHeroes/PlanarShadow");
            if (sh == null || rig?.Root == null) return;
            _castMat ??= new Material(sh) { name = "cast" };
            foreach (var smr in rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.enabled || smr.sharedMesh == null || smr.name.Contains("Outline") || smr.name.StartsWith("face") || smr.name.Contains("Eyebrow")) continue;
                var go = new GameObject("cast:" + smr.name) { layer = smr.gameObject.layer };
                go.transform.SetParent(smr.transform.parent, false);
                go.transform.localPosition = smr.transform.localPosition; go.transform.localRotation = smr.transform.localRotation; go.transform.localScale = smr.transform.localScale;
                var c = go.AddComponent<SkinnedMeshRenderer>();
                c.sharedMesh = smr.sharedMesh; c.bones = smr.bones; c.rootBone = smr.rootBone;
                c.sharedMaterials = Enumerable.Repeat(_castMat, smr.sharedMesh.subMeshCount).ToArray();
                c.updateWhenOffscreen = true;
                c.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; c.receiveShadows = false;
            }
        }

        public bool DebugTelegraph(string kind)
        {
            foreach (var t in _teles) Drop(t);
            _teles.Clear();
            var boss = _actors.Values.FirstOrDefault(a => a.C.boss != null && a.C.Alive);
            if (boss == null || kind == "none") return boss != null;
            Telegraph(boss, "debug:" + kind, kind);
            BossStrikes(boss, "debug:" + kind, kind, 0.95f);   // lands just after the capture's 0.35 s look
            return true;
        }

        void Telegraph(Actor boss, string name, string kindOverride = null)
        {
            var kind = kindOverride ?? boss.C.boss?.specials?.FirstOrDefault(x => x.name == name)?.kind;
            if (kind == null || _teles.Any(t => t.Boss == boss && t.Name == name)) return;
            var tele = new Tele { Boss = boss, Name = name };
            var squad = Living(Side.Hero).Where(h => h.C.Alive).OrderByDescending(h => h.C.x).ToList();
            List<Actor> on = kind switch
            {
                "volley" => squad,
                "sweep" => squad.Take(2).ToList(),
                "throw" => squad.Skip(Mathf.Max(0, squad.Count - 1)).ToList(),
                _ => null,
            };
            void Mark(Actor a, float size, bool danger)
            {
                tele.Danger |= danger;
                var disc = MeshKit.Part("tele", _root, FloorQuad, DiscMat, Layer).transform;
                var ring = MeshKit.Part("tele", _root, FloorQuad, RingMat(danger ? new Color(1f, 0.3f, 0.28f, 0.95f) : new Color(0.7f, 0.45f, 1f, 0.95f)), Layer).transform;
                tele.Marks.Add((disc, a, size)); tele.Marks.Add((ring, a, size * 1.05f));
                if (danger)
                {
                    var card = MeshKit.Part("tele", _root, Quad, FxMat("err_ref"), Layer).transform;
                    tele.Marks.Add((card, a, -1f));
                }
            }
            if (kind == "stomp" && squad.Count > 0)
            {
                // one ring over the whole squad, centred between its ends
                var span = squad.Max(h => h.X) - squad.Min(h => h.X);
                Mark(squad[squad.Count / 2], Mathf.Max(3f, span * 1.3f + 1.5f), true);
            }
            else if (on != null) foreach (var a in on) Mark(a, 1.4f * a.Scale, true);
            else Mark(boss, 2.4f * boss.Scale, false);
            _teles.Add(tele);
        }

        Material RingMat(Color c)
        {
            if (!RingMats.TryGetValue(c, out var ring) || ring == null) RingMats[c] = ring = MeshKit.NewGlass(RingTex, c);
            return ring;
        }

        void UpdateTelegraphs(float dt)
        {
            var cam = _cam.transform;
            for (var i = _teles.Count - 1; i >= 0; i--)
            {
                var t = _teles[i];
                t.T += dt;
                if (t.Boss == null || !t.Boss.C.Alive || t.T > 12f) { Drop(t); _teles.RemoveAt(i); continue; }
                var pulse = 0.5f + 0.5f * Mathf.Sin(t.T * Mathf.Lerp(7f, 20f, Mathf.Clamp01(t.T / 3f)));
                foreach (var (tr, on, size) in t.Marks)
                {
                    if (tr == null || on == null) continue;
                    var b = on.Rig.Root.position;
                    if (size < 0f)
                    {
                        // the #REF! card over the head, bobbing
                        tr.position = b + Vector3.up * (on.Rig.Height * on.Scale + 0.45f + pulse * 0.06f);
                        tr.rotation = Quaternion.LookRotation(tr.position - cam.position, cam.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(t.T * 9f) * 4f);
                        tr.localScale = new Vector3(0.55f, 0.28f, 1f);
                        continue;
                    }
                    tr.position = b + Vector3.up * 0.035f;
                    tr.localScale = Vector3.one * size * (1f + pulse * 0.05f);
                    if (tr.GetComponent<MeshRenderer>().sharedMaterial == DiscMat)
                        TintFx(tr, t.Danger ? new Color(1f, 0.25f, 0.22f, 0.22f + pulse * 0.2f) : new Color(0.62f, 0.4f, 1f, 0.18f + pulse * 0.16f), 1f);
                }
            }
        }

        void Resolve(Actor boss, string name)
        {
            var t = _teles.FirstOrDefault(x => x.Boss == boss && x.Name == name);
            if (t == null) return;
            foreach (var (tr, on, size) in t.Marks)
                if (tr != null && on != null && size > 0f && tr.GetComponent<MeshRenderer>().sharedMaterial == DiscMat)
                    FloorRing(on.Rig.Root.position, new Color(1f, 0.45f, 0.35f, 0.95f), 0.35f, size * 0.6f, size * 1.4f);
            AddShakeLocal(0.45f);
            Drop(t); _teles.Remove(t);
        }

        void Drop(Tele t) { foreach (var (tr, _, _) in t.Marks) if (tr != null) Object.Destroy(tr.gameObject); t.Marks.Clear(); }

        void HitRing(Actor at, bool crit)
        {
            if (at.LastRing >= 0f && _time - at.LastRing < 0.12f) return;
            at.LastRing = _time;
            var c = crit ? new Color(1f, 0.86f, 0.4f, 0.95f) : new Color(0.78f, 0.96f, 1f, 0.9f);
            if (!RingMats.TryGetValue(c, out var ring) || ring == null) RingMats[c] = ring = MeshKit.NewGlass(RingTex, c);
            var s = at.Scale * (crit ? 1.3f : 1f);
            var floor = FxPart("hitring", FloorQuad, ring, Layer).transform;
            floor.position = at.Rig.Root.position + Vector3.up * 0.025f;
            _fx.Add(new Fx { T = floor, Life = 0.48f, Max = 0.48f, Grow0 = 0.3f * s, Grow1 = 1.8f * s, Flat = true });
            var up = FxPart("hitring", Quad, ring, Layer).transform;
            var hitAt = at.Rig.Root.position + Vector3.up * at.Rig.Height * at.Scale * 0.55f + new Vector3(0f, 0f, -0.32f);
            up.position = hitAt;
            _fx.Add(new Fx { T = up, Life = 0.36f, Max = 0.36f, Grow0 = 0.15f * s, Grow1 = 1.25f * s, Face = true });   // lives long enough to be seen (a frame of the fight caught it half the time)
            // the burst the reference mock throws off every hit: thin light streaks flying out
            // radially in the picture plane, yellow-white, gone in a fifth of a second
            var streakCol = crit ? new Color(1f, 0.9f, 0.45f) : new Color(1f, 0.97f, 0.75f);
            var n = crit ? 9 : 6;
            var camRight = _cam.transform.right; var camUp = _cam.transform.up;
            for (var i = 0; i < n; i++)
            {
                var ang = (i + Random.value * 0.6f) / n * 360f;
                var dir = Quaternion.AngleAxis(ang, -_cam.transform.forward) * camUp;
                var st = FxPart("streak", Quad, GlowMat(streakCol), Layer).transform;
                st.position = hitAt + dir * 0.08f * s;
                var speed = (crit ? 5.5f : 4f) * s * (0.7f + Random.value * 0.6f);
                _fx.Add(new Fx { T = st, Life = 0.28f, Max = 0.28f, Grow0 = 1.0f * s, Grow1 = 0.5f * s, Face = true, Roll = ang, Aspect = new Vector2(0.09f, 0.55f), Vel = dir * speed });
            }
        }

        float _localShake;
        void AddShakeLocal(float s) => _localShake = Mathf.Max(_localShake, s);

        struct Fx
        {
            public Transform T;
            public float Life, Max, Grow0, Grow1;
            public Vector3 Vel;
            public bool Gravity, Flat, Face, Spin;
            public float Roll; public Vector2 Aspect;   // a camera-facing streak: turned in the picture plane, stretched along its length
            public bool Line; public Vector3 A, B; public float W;   // laid along A to B each frame, thinning out
            public float Delay;                                    // hidden until this has run out (a chain's later hops)
            public bool Fade; public Color Tint;                   // a card: fades its alpha instead of glowing white
        }

        readonly List<Fx> _fx = new();

        static Mesh _floorQuad;
        static Mesh FloorQuad
        {
            get
            {
                if (_floorQuad != null) return _floorQuad;
                var b = new MeshKit.Builder();
                b.Quad(Vector3.zero, new Vector3(0.5f, 0f, 0f), new Vector3(0f, 0f, 0.5f), Color.white);
                return _floorQuad = b.Bake("floorquad");
            }
        }

        void UpdateFx(float dt)
        {
            for (var i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                if (f.Delay > 0f)
                {
                    f.Delay -= dt;
                    if (f.T != null) f.T.gameObject.SetActive(f.Delay <= 0f);
                    _fx[i] = f;
                    continue;
                }
                f.Life -= dt;
                if (f.Life <= 0f || f.T == null)
                {
                    if (f.T != null) FxRelease(f.T);
                    _fx.RemoveAt(i);
                    continue;
                }
                var k = 1f - f.Life / f.Max;
                if (f.Line)
                {
                    Lay(f.T, f.A, f.B, f.W * (1f - k * 0.75f));
                    if (f.Fade) TintFx(f.T, f.Tint, 1f - k * k);
                    _fx[i] = f;
                    continue;
                }
                if (f.Fade)
                {
                    f.T.position += f.Vel * dt;
                    var sc2 = Mathf.Lerp(f.Grow0, f.Grow1, 1f - (1f - k) * (1f - k));
                    f.T.localScale = new Vector3(sc2 * f.Aspect.x, sc2 * f.Aspect.y, 1f);
                    f.T.rotation = Quaternion.LookRotation(f.T.position - _cam.transform.position, _cam.transform.up) * Quaternion.Euler(0f, 0f, f.Roll);
                    TintFx(f.T, f.Tint, k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f);
                    _fx[i] = f;
                    continue;
                }
                if (f.Gravity) f.Vel += Vector3.down * 9f * dt;
                f.T.position += f.Vel * dt;
                var sc = Mathf.Lerp(f.Grow0, f.Grow1, 1f - (1f - k) * (1f - k));
                f.T.localScale = f.Aspect == Vector2.zero ? Vector3.one * sc : new Vector3(sc * f.Aspect.x, sc * f.Aspect.y, 1f);
                if (f.Face) f.T.rotation = Quaternion.LookRotation(f.T.position - _cam.transform.position) * Quaternion.Euler(0f, 0f, f.Roll);
                if (f.Aspect != Vector2.zero)
                {
                    // streaks thin out and fade as they fly
                    var mr = f.T.GetComponent<MeshRenderer>();
                    if (mr != null) { var mb = _fxBlock; mr.GetPropertyBlock(mb); mb.SetFloat("_Glow", 1f - k); mr.SetPropertyBlock(mb); }
                }
                if (f.Spin) f.T.rotation = Quaternion.Euler(k * 720f, k * 540f, 0f);
                _fx[i] = f;
            }
        }

        void UpdateSparks(float dt)
        {
            for (var i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s.life -= dt;
                if (s.life <= 0f)
                {
                    s.t.gameObject.SetActive(false);
                    _sparkPool.Push(s.t);
                    _sparks.RemoveAt(i);
                    continue;
                }
                var k = 1f - s.life / s.max;
                s.t.position += s.vel * dt;
                s.t.localScale = Vector3.one * s.grow * Mathf.Lerp(0.35f, 1.2f, k) * (1f - k * 0.3f);
                s.t.rotation = Quaternion.LookRotation(s.t.position - _cam.transform.position);
                _sparks[i] = s;
            }
        }

        // ------------------------------------------------------------------ actor --

        class Actor
        {
            /// <summary>
            /// SD sprite motion. Everything is squash, stretch, hop and lean in the picture plane,
            /// timed the way SD figures move: a small crouch before every action (anticipation),
            /// the action overshooting, then settling.
            /// </summary>
            /// <summary>
            /// The 3D SD: turned three-quarters to camera like the reference's squads, posed on its
            /// bones — idle sway, a trot, an arm-thrown attack with anticipation, a flinch, a spin
            /// for EX, a hop and wave for the win.
            /// </summary>
            Pose _pose, _shown; bool _shownInit; float _winT; float[] _vel;
            // the pose's own clocks: the sim's Attack (0.32 s) and Hit (0.16 s) are too short for a body to
            // read — the wind-up, the held extension and the settle need about half a second (BA's
            // blows snap out in two or three frames and then HOLD) — so the SD's pose runs on its own
            // longer timeline, started when the sim's timer restarts
            float _atkT = 9f, _hitT = 9f, _sklT = 9f, _pAtk, _pHit, _pSkl;
            public float _enemyX, _heroX;         // world x of the nearest living enemy / hero, for the look

            void Update3D(float dt, float time, bool walking, MaterialPropertyBlock mpb, float closeUp)
            {
                var hero = C.side == Side.Hero;
                var root = Rig.Root;
                var y = 0f; var lean = 0f; var twist = 0f;
                var br = Mathf.Sin(time * 2.6f + Z * 2f);
                var armL = 8f + br * 3f; var armR = -8f - br * 3f; var fwdR = 0f; var fwdL = 0f;
                var legSwing = 0f;
                if (walking) { var ph = _walk * 0.9f; y += Mathf.Abs(Mathf.Sin(ph)) * 0.06f; legSwing = Mathf.Sin(ph) * 30f; fwdL = -legSwing * 0.8f; fwdR = legSwing * 0.8f; lean -= 6f; }
                if (Attack > 0f)
                {
                    var a = 1f - Attack / 0.32f;
                    if (a < 0.3f) { var k = a / 0.3f; fwdR = -40f * k; lean += 5f * k; twist = -12f * k; }
                    else if (a < 0.6f) { var k = (a - 0.3f) / 0.3f; fwdR = Mathf.Lerp(-40f, 95f, k); lean -= 10f * k; twist = Mathf.Lerp(-12f, 16f, k); }
                    else { var k = (a - 0.6f) / 0.4f; fwdR = Mathf.Lerp(95f, 0f, k); lean -= 10f * (1f - k); twist = 16f * (1f - k); }
                }
                if (Hit > 0f) { var k = Hit / 0.16f; lean += 14f * k; armL += 20f * k; armR -= 20f * k; }
                var spin = 0f;
                if (Skill > 0f) { var k = 1f - Skill / 0.75f; y += Mathf.Sin(k * Mathf.PI) * 0.45f; spin = k * 360f; fwdL = fwdR = -150f * Mathf.Sin(k * Mathf.PI); }
                if ((Cheer > 0f || closeUp > 0.5f) && C.Alive) { var h = Mathf.Abs(Mathf.Sin(time * 7f)); y += h * 0.12f; fwdR = -160f; armR = -20f + Mathf.Sin(time * 12f) * 15f; }
                if (Dying > 0f || !C.Alive)
                {
                    if (Dying > 0f) Dying += dt;
                    var k = Dying > 0f ? Mathf.Clamp01(Dying / 0.45f) : 1f;
                    lean = 80f * Mathf.SmoothStep(0f, 1f, k);
                }
                if (Rig.RefModel)
                {
                    // the sample rig: a real pose per state from the library — the character's own
                    // idle and victory, an attack by role — instead of offsets on the A-pose
                    var cheering = (Cheer > 0f || closeUp > 0.5f) && C.Alive;
                    _winT = cheering ? _winT + dt : 0f;
                    var live = Freeze > 0f ? dt * 0.05f : dt;
                    if (Attack > _pAtk + 1e-4f) _atkT = 0f; else _atkT += live;
                    if (Hit > _pHit + 1e-4f) _hitT = 0f; else _hitT += live;
                    if (Skill > _pSkl + 1e-4f) _sklT = 0f; else _sklT += dt;
                    _pAtk = Attack; _pHit = Hit; _pSkl = Skill;
                    var pers = SdPersona.For(Pid);
                    var atkOn = _atkT < SdPose.AttackLen; var hitOn = _hitT < SdPose.HitLen; var sklOn = _sklT < SdPose.SkillLen;
                    if (Dying > 0f || !C.Alive) _pose = SdPose.Dead(Dying > 0f ? Mathf.Clamp01(Dying / SdPose.DeadLen) : 1f);
                    else if (cheering) _pose = SdPose.Victory(SdPose.WinOf(Pid), _winT);
                    else if (sklOn) _pose = SdPose.SkillAt(SdPose.AttackOf(Pid, C.role), _sklT / SdPose.SkillLen);
                    else if (hitOn) _pose = SdPose.Hit(1f - _hitT / SdPose.HitLen);
                    else if (atkOn) _pose = SdPose.AttackAt(AttackPose(C), _atkT / SdPose.AttackLen);
                    else if (walking) _pose = _speed > RunSpeed * Rig.Root.localScale.x ? SdPose.Run(_walk) : SdPose.Walk(_walk);
                    else
                    {
                        // in a fight: the combat stance, not the lobby idle; every other ranged member kneels to shoot
                        var rk = SdPose.AttackOf(Pid, C.role);
                        _pose = rk == 1 && SdPose.Hash(Pid) % 2 == 0 ? SdPose.ReadyKneel(time * pers.Tempo, Z * 2f) : SdPose.Ready(rk, time * pers.Tempo, Z * 2f);
                    }
                    // the character's own bearing over it (SdPersona): full in the guard, less on the run or celebrating, none mid-blow
                    pers.Shape(ref _pose, Dying > 0f || !C.Alive || sklOn || hitOn || atkOn ? 0f : cheering ? 0.4f : walking ? 0.45f : 0.7f);
                    // the chin up to the camera above (the BA cross-check: seen from 33° up, a forward lean and a
                    // level chin put the big fringe over the face and the squad read as the backs of heads)
                    // the tablet attacks drop the eyes 12° onto the screen, which from up here is the crown of the head:
                    // the chin is kept at least a little up in the fight, the waist hardly folding
                    if (hero && !cheering && C.Alive && Dying <= 0f)
                    {
                        _pose.HeadPitch = Mathf.Min(_pose.HeadPitch - 9f, -4f);
                        _pose.Lean *= 0.7f; _pose.SpineBend = Mathf.Min(_pose.SpineBend, 2f);
                    }
                    // the head looks at the fight: heroes toward the enemy line, enemies toward the squad
                    if (!cheering && !atkOn && C.Alive)
                    {
                        var look = hero ? Mathf.Clamp((_enemyX - X) * 6f, -14f, 14f) : Mathf.Clamp((_heroX - X) * -6f, -14f, 14f);
                        _pose.HeadYaw += look;
                    }
                    // a damped spring between states instead of a fade: a body overshoots a little
                    // and settles; stiffer into an attack or a hit
                    // stiff enough to follow a three-frame snap (at 42 the spring smeared the release into a swing)
                    var omega = atkOn || sklOn ? 64f : hitOn ? 58f : Dying > 0f ? 40f : 26f;
                    if (!_shownInit) { _shown = _pose; _vel = new float[Pose.Count]; _shownInit = true; }
                    // hitstop: the body all but stops for the few frames of the blow (as the clips did), then springs on
                    else Pose.Spring(ref _shown, _vel, _pose, Freeze > 0f ? dt * 0.05f : dt, omega, 0.78f);
                    y = _shown.Y; spin = _shown.Yaw;
                }
                // The reference's squads FACE the enemy (right), seen from behind-and-above; they only
                // turn to the camera for the win close-up. Root rotation 180 = facing the camera,
                // 90 = facing +x (the enemies): heroes at 105, enemies mirrored at 255.
                // turned with the quarter-view camera, so each keeps the same angle to the lens
                // a mascot is a drawing given depth: its face is the drawing's front, so it turns only a
                // little toward the squad (28° off the lens) — side-on it showed its thin plush seam
                var yaw = (hero ? Mathf.Lerp(Rig.RefModel ? HeroFace : -75f, -10f, closeUp) : Rig.Mascot ? 28f : Rig.RefModel ? 38f : 75f) + QuarterYaw * (1f - closeUp);   // the SD squad turned a three-quarter towards the lens as the reference's are (faces, not the backs of heads: the BA cross-check)
                // (an SdEnemy wears the heroes' skeleton and its clips' half turn: mirrored from the heroes,
                // toward the squad and a little toward the lens, so its face shows)
                // a limbless mascot (3D monster) attacks by lunging: a hop toward the squad
                var lunge = 0f;
                if (Rig.ArmR == null && Attack > 0f)
                {
                    var a = Mathf.Sin((1f - Attack / 0.32f) * Mathf.PI);
                    lunge = (hero ? 1f : -1f) * a * 0.35f; y += a * 0.18f; lean += a * 12f;
                }
                if (Rig.ArmR == null && Hit > 0f) { var k = Hit / 0.16f; lunge = (hero ? -1f : 1f) * k * 0.12f; }
                // the mesh faces +z; the camera looks along +z, so 180 turns it to camera, yaw toward the fight
                root.localRotation = Quaternion.Euler(0f, 180f + yaw + spin, 0f);
                // a fall tips the whole figure about its feet (SdPose.Dead)
                if (Rig.RefModel && hero && Mathf.Abs(_shown.Pitch) > 0.01f) root.localRotation *= Quaternion.Euler(_shown.Pitch, 0f, 0f);   // an enemy is knocked up and spun instead
                var facing = Rig.RefModel ? root.localRotation * Vector3.forward * _shown.Step : Vector3.zero;   // the pose's step along the facing
                // bones first (offsets onto the rest pose), then the root: planting the feet needs the pose
                if (Rig.RefModel)
                {
                    // keyframed clips (SdClips) when the library is there; the hand-written poses otherwise
                    if (SdClips.Available && SdClips.InGame && ClipMotion(dt, walking, closeUp)) { }
                    else SdPose.Apply(Rig, _shown);
                    if (C.heroId != null) SdExpr.Tick(Rig, C.heroId, _shown.Expr, time);
                    y += Rig.FootDrop * root.localScale.y;
                }
                else
                {
                    if (Rig.Mascot) Squash(time, walking, ref y);
                    if (Rig.Body != null) Rig.Body.localRotation = Quaternion.Euler(lean, twist, 0f);
                    if (Rig.Head != null) Rig.Head.localRotation = Quaternion.Euler(br * 2f, 0f, Hit > 0f ? 6f : 0f);
                    if (Rig.ArmL != null) Rig.ArmL.localRotation = Quaternion.Euler(fwdL, 0f, armL);
                    if (Rig.ArmR != null) Rig.ArmR.localRotation = Quaternion.Euler(fwdR, 0f, armR);
                    if (Rig.LegL != null) Rig.LegL.localRotation = Quaternion.Euler(legSwing, 0f, 0f);
                    if (Rig.LegR != null) Rig.LegR.localRotation = Quaternion.Euler(-legSwing, 0f, 0f);
                }
                // a blow's body: a shooter pulls back before the shot and kicks back after it; a melee
                // striker leans in on the swing
                if (Rig.RefModel && Attack > 0f && C.Alive)
                {
                    var a = 1f - Attack / 0.32f;
                    var dir = hero ? -1f : 1f;   // away from the enemy line
                    var ranged = hero && C.role is "ranged" or "healer";
                    if (ranged)
                        lunge += dir * (a < 0.25f ? 0.03f * (a / 0.25f) : a < 0.35f ? Mathf.Lerp(0.03f, 0.12f, (a - 0.25f) / 0.1f) : 0.12f * (1f - Mathf.SmoothStep(0f, 1f, (a - 0.35f) / 0.5f))) * Rig.Height;
                    else
                        lunge -= dir * Mathf.Sin(Mathf.Clamp01((a - 0.15f) / 0.6f) * Mathf.PI) * 0.1f * Rig.Height;
                }
                // the target shaking in place while the hit holds
                if (Freeze > 0f && Hit > 0f) lunge += (Mathf.PerlinNoise(time * 60f, Z * 7f) - 0.5f) * 2f * FreezeShake;
                // an enemy on the heroes' skeleton goes down the way BA's do: knocked up and back, spinning,
                // then bursting into cells (BattleWorld.DeathPop, at 0.72 s) as it shrinks away
                if (Rig.RefModel && !hero && Dying > 0f)
                {
                    if (_deathScale == Vector3.zero) _deathScale = Vector3.one * Scale;
                    var k = Mathf.Clamp01(Dying / 0.72f);
                    y += Mathf.Sin(k * Mathf.PI) * 0.8f * Mathf.Sqrt(_deathScale.y);
                    lunge += Mathf.SmoothStep(0f, 1f, k) * 1.4f;
                    root.localRotation *= Quaternion.Euler(-35f * k, k * 420f, 0f);
                    var shrink = Mathf.Clamp01((Dying - 0.62f) / 0.2f);
                    root.localScale = _deathScale * (1f - shrink * 0.95f);
                }
                if (Rig.RefModel && Rig.HandR != null) SwingTrail(hero);
                // the flinch: squashed on the hit's first frames, springing back past round (not while dying)
                if (Rig.RefModel && Dying <= 0f)
                {
                    _baseScale = Vector3.one * Scale;
                    var hk = Hit > 0f ? 1f - Hit / 0.16f : 1f;
                    var sq = Hit > 0f ? (hk < 0.3f ? -0.12f * Mathf.Sin(hk / 0.3f * Mathf.PI * 0.5f) : -0.12f * Mathf.Cos((hk - 0.3f) / 0.7f * Mathf.PI * 1.5f) * (1f - hk)) : 0f;
                    root.localScale = new Vector3(_baseScale.x * (1f - sq * 0.6f), _baseScale.y * (1f + sq), _baseScale.z * (1f - sq * 0.6f));
                }
                var px = X; var pz = Z;
                if (hero && closeUp > 0f && C.Alive) { var kk = Mathf.SmoothStep(0f, 1f, closeUp); px = Mathf.Lerp(X, LineX, kk); pz = Mathf.Lerp(Z, LineZ, kk); }
                root.localPosition = new Vector3(px + lunge, y, pz) + facing;
                // lying down, nothing under the street: lift by what the big head would sink (SdPose.GroundLift)
                if (Rig.RefModel && hero && (Dying > 0f || !C.Alive))
                {
                    var g = root.parent != null ? root.parent.position.y : 0f;
                    var lift = SdPose.GroundLift(Rig, g);
                    if (lift > 0f) root.position += Vector3.up * lift;
                }
                if (Rig.SheetFloor && Rig.Sheet != null)
                {
                    // flat on the street under the member, square to the camera's turn, gone when down
                    Rig.Sheet.gameObject.SetActive(C.Alive && Dying <= 0f);
                    // in the fight too (the BA part check, battle_chars: "intrusive UI clipping" — the neon sheet on the
                    // street under every member read as clutter; the reference's squads carry only halos)
                    if (closeUp > 0.5f || HaloInFight)
                    {
                        // the win's close-up: the member's sheet rises to float over her head, small and tipped
                        // back like a halo, bobbing — the reference's squads are read by their halos (the BA
                        // cross-check: our silhouettes had nothing over the head); still one sheet per member
                        var head = new Vector3(px, y + Rig.Height * root.localScale.y * (1.02f + Mathf.Max(0f, BattleHead / SdSample.HeadScale - 1f) * 0.6f) + Mathf.Sin(time * 2.2f + Z) * 0.03f, pz);   // over a bigger head too
                        Rig.Sheet.localPosition = head;
                        Rig.Sheet.localRotation = Quaternion.Euler(62f, 0f, Rig.SheetSide * 8f);
                        Rig.Sheet.localScale = Vector3.one * (closeUp > 0.5f ? 0.62f : 0.72f);
                    }
                    else
                    {
                        Rig.Sheet.localPosition = new Vector3(X + lunge, 0.02f, Z) + facing;
                        Rig.Sheet.localRotation = Quaternion.Euler(90f, QuarterYaw * (1f - closeUp), 0f);
                        Rig.Sheet.localScale = Vector3.one * 1.8f;
                    }
                }
                var flash = Hit > 0.11f ? 1f : Hit > 0.08f ? 0.45f : 0f;
                SetFlash(flash, mpb);
                if (Rig.Sheet != null && !Rig.SheetWorn)
                {
                    Rig.Sheet.localPosition = new Vector3(Rig.SheetSide * 0.12f, Rig.Height * 0.62f + Mathf.Sin(time * 1.7f) * 0.015f, -0.14f);
                    Rig.Sheet.localRotation = Quaternion.Euler(0f, 180f, Rig.SheetSide * 12f);
                }
            }

            /// <summary>
            /// A streak of light behind the striking hand while a blow is in the air — in the hero's
            /// accent, red on an enemy. Melee and tank strikes, and every enemy blow; not the shooters.
            /// </summary>
            void SwingTrail(bool hero)
            {
                var melee = !hero || C.role is "melee" or "tank";
                if (!melee) return;
                if (_trail == null)
                {
                    var go = new GameObject("trail") { layer = Rig.Root.gameObject.layer };
                    go.transform.SetParent(Rig.HandR, false);
                    _trail = go.AddComponent<TrailRenderer>();
                    _trail.time = 0.14f; _trail.minVertexDistance = 0.02f;
                    _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.16f * Rig.Height), new Keyframe(1f, 0f));
                    var c = hero ? Accent : new Color(1f, 0.35f, 0.3f);
                    var g = new Gradient();
                    g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 0.35f), new GradientColorKey(c, 1f) },
                              new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
                    _trail.colorGradient = g;
                    _trail.sharedMaterial = TrailMat;
                    _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _trail.receiveShadows = false;
                    _trail.emitting = false;
                }
                // the middle of the swing, where the hand is fastest
                var a = Attack > 0f ? 1f - Attack / 0.32f : -1f;
                _trail.emitting = (a > 0.18f && a < 0.8f) || (Skill > 0f && Skill < 0.6f);
            }

            static Material _trailMat;
            static Material TrailMat => _trailMat != null ? _trailMat : (_trailMat = MeshKit.NewGlass(MeshKit.Blob, Color.white));

            /// <summary>
            /// A limbless 3D mascot moves the way the 2D one did: it breathes, crouches before a lunge
            /// and stretches into it, flattens when hit, squashes on each hop's landing.
            /// </summary>
            void Squash(float time, bool walking, ref float y)
            {
                var sx = 1f; var sy = 1f;
                var br = Mathf.Sin(time * 3.1f + Z * 2f);
                sx *= 1f - br * 0.015f; sy *= 1f + br * 0.022f;
                if (walking)
                {
                    var hop = Mathf.Abs(Mathf.Sin(_walk * 0.9f));
                    y += hop * 0.08f;
                    if (hop < 0.25f) { sx *= 1.07f; sy *= 0.93f; }
                }
                if (Attack > 0f)
                {
                    var a = 1f - Attack / 0.32f;
                    if (a < 0.3f) { var k = a / 0.3f; sx *= 1f + 0.12f * k; sy *= 1f - 0.14f * k; }
                    else if (a < 0.55f) { var k = (a - 0.3f) / 0.25f; sx *= 1.12f - 0.2f * k; sy *= 0.86f + 0.24f * k; }
                    else { var k = (a - 0.55f) / 0.45f; sx *= 0.92f + 0.08f * k; sy *= 1.1f - 0.1f * k; }
                }
                if (Hit > 0f) { var k = Hit / 0.16f; sx *= 1f + 0.12f * k; sy *= 1f - 0.12f * k; }
                if (Rig.Body != null) Rig.Body.localScale = new Vector3(sx, sy, sx);
            }

            // The hit flash as a renderer property block only while it shows. A block on a renderer
            // takes it out of the SRP Batcher, and writing _Flash = 0 every frame did that to all 35
            // body renderers of a fight: SetPass calls ran ~130 (PerfProbe). At 0 the block goes.
            bool _flashOn;
            void SetFlash(float flash, MaterialPropertyBlock mpb, bool toonOnly = false)
            {
                var on = flash > 0.001f;
                if (!on && !_flashOn) return;
                _flashOn = on;
                foreach (var r in Rig.Renderers)
                {
                    if (r == null || r == Rig.SheetRenderer) continue;
                    if (toonOnly && (r.sharedMaterial == null || r.sharedMaterial.shader != MeshKit.ToonShader)) continue;
                    if (!on) { r.SetPropertyBlock(null); continue; }
                    mpb.Clear(); mpb.SetFloat("_Flash", flash); r.SetPropertyBlock(mpb);
                }
            }

            // ---- keyframed motion ------------------------------------------------------------
            SdClips _clips;
            float _lastAttack, _lastHit, _lastSkill;
            public void DisposeClips() { _clips?.Dispose(); _clips = null; }

            /// <summary>
            /// A hero's own way of moving, picked once from their role and a hash of their id, so 55
            /// people in the same street do not move as one: a stance, a set of attacks they cycle
            /// through, an EX, a victory, their tempo and a posture laid over every clip.
            /// </summary>
            class Profile
            {
                public string Stance, Idle, Ex, Win, Walk;
                public (string clip, float contact)[] Attacks;
                public float Speed, Lean, Tilt, Chest;
            }

            static readonly Dictionary<string, Profile> Profiles = new();

            static Profile ProfileOf(Combatant c)
            {
                var key = c.heroId ?? c.typeId ?? "x";
                if (Profiles.TryGetValue(key, out var p)) return p;
                var h = (uint)SdPose.Hash(key);
                T Pick<T>(int salt, params T[] xs) => xs[(int)((h / (uint)(salt * 7 + 1)) % (uint)xs.Length)];
                var kind = AttackPose(c);
                p = new Profile { Speed = 0.9f + (h % 23) / 100f, Lean = ((int)(h / 7 % 9) - 4) * 0.9f, Tilt = ((int)(h / 11 % 9) - 4) * 1.4f, Chest = ((int)(h / 13 % 7) - 3) * 1.2f, Walk = Pick(9, "Jog_Fwd_Loop", "Jog_Fwd_Loop", "Walk_Formal_Loop") };
                if (c.heroId == null)
                {
                    // an enemy (SdEnemy on the heroes' skeleton): a brawler — a guard, punches and a
                    // hook, a heavier walk, the boss a shade slower and heavier
                    var boss = c.boss != null;
                    p.Stance = Pick(1, "Idle_Shield_Loop", "Sword_Idle", "Idle_Loop");
                    p.Attacks = Pick(2, new[] { ("Punch_Jab", 0.3f), ("Punch_Cross", 0.35f) }, new[] { ("Melee_Hook", 0.45f), ("Punch_Cross", 0.35f) }, new[] { ("Sword_Regular_A", 0.4f), ("Punch_Jab", 0.3f) });
                    p.Ex = boss ? "Sword_Heavy_Combo" : "Melee_Hook";
                    p.Walk = boss ? "Walk_Formal_Loop" : Pick(3, "Jog_Fwd_Loop", "Walk_Formal_Loop");
                    p.Idle = p.Stance; p.Win = Pick(4, "Dance_Loop", "Yes");
                    if (boss) p.Speed *= 0.85f;
                    return Profiles[key] = p;
                }
                switch (c.role)
                {
                    case "tank":
                        p.Stance = Pick(1, "Idle_Shield_Loop", "Sword_Idle");
                        p.Attacks = new[] { ("Shield_OneShot", 0.35f), ("Melee_Hook", 0.45f), ("Punch_Cross", 0.35f) };
                        p.Ex = "Shield_Dash"; break;
                    case "melee":
                        p.Stance = "Sword_Idle";
                        p.Attacks = Pick(2,
                            new[] { ("Sword_Regular_A", 0.4f), ("Sword_Regular_B", 0.4f), ("Sword_Regular_C", 0.45f) },
                            new[] { ("Punch_Jab", 0.3f), ("Punch_Cross", 0.35f), ("Melee_Hook", 0.45f) },
                            new[] { ("Sword_Attack", 0.42f), ("Sword_Regular_B", 0.4f) });
                        p.Ex = Pick(3, "Sword_Heavy_Combo", "Sword_Dash"); break;
                    case "healer":
                        p.Stance = "Spell_Simple_Idle_Loop";
                        p.Attacks = kind == 8 ? new[] { ("OverhandThrow", 0.45f) } : new[] { ("Spell_Simple_Shoot", 0.3f), ("OverhandThrow", 0.45f) };
                        p.Ex = "Spell_Simple_Shoot"; break;
                    default:   // ranged
                        p.Stance = kind is 3 or 4 or 7 ? "Pistol_Idle_Loop" : "Spell_Simple_Idle_Loop";
                        p.Attacks = kind is 3 or 4 or 7 ? new[] { ("Pistol_Shoot", 0.12f) }
                                  : kind == 5 ? new[] { ("OverhandThrow", 0.45f), ("Spell_Simple_Shoot", 0.3f) }
                                  : new[] { ("Spell_Simple_Shoot", 0.3f) };
                        p.Ex = Pick(4, "Spell_Simple_Shoot", "OverhandThrow"); break;
                }
                p.Idle = Pick(5, "Idle_Loop", "Idle_FoldArms_Loop", "Idle_Talking_Loop", "Idle_TalkingPhone_Loop");
                p.Win = Pick(6, "Dance_Loop", "Yes", "Idle_FoldArms_Loop", "Dance_Loop");
                return Profiles[key] = p;
            }

            int _attackN;
            static readonly HashSet<string> OneShots = new() { "Hit_Chest", "Hit_Head", "Hit_Knockback" };

            /// <summary>The fight's state as a clip: dead, cheering, EX, hit, attack, walk, stance.</summary>
            bool ClipMotion(float dt, bool walking, float closeUp)
            {
                if (_clips == null) _clips = new SdClips(Rig, C.heroId ?? C.typeId ?? "sd");
                var p = ProfileOf(C);
                var cheering = (Cheer > 0f || closeUp > 0.5f) && C.Alive;
                var acting = _clips.Current != null && !_clips.Done && _clips.Current != p.Stance && _clips.Current != p.Walk && _clips.Current != p.Win;
                if (Dying > 0f || !C.Alive) _clips.Play("Death01", 0.1f, 0f, 1.4f);
                else if (cheering) _clips.Play(p.Win, 0.25f, 0f, p.Speed);
                else if (Skill > _lastSkill + 1e-4f) _clips.Play(p.Ex, 0.06f, 0f, 1.15f * p.Speed, restart: true);
                // a boss does not flinch; an enemy only staggers (the knockback fall, replayed on every hit
                // below 30 % hp, kept a boss upside down on the floor)
                else if (Hit > _lastHit + 1e-4f && !acting && C.boss == null)
                    _clips.Play(C.heroId != null && C.hp < C.maxHp * 0.3f ? "Hit_Knockback" : Random.value < 0.5f ? "Hit_Chest" : "Hit_Head", 0.05f, 0f, 1.1f, restart: true);
                else if (Attack > _lastAttack + 1e-4f)
                {
                    // the next of the hero's attacks, its contact frame on the Shot's arrival
                    var (clip, contact) = p.Attacks[_attackN++ % p.Attacks.Length];
                    var speed = 1.35f * p.Speed;
                    var len = SdClips.Clip(clip)?.length ?? 1f;
                    var flight = C.role is "ranged" or "healer" ? 0.28f : 0.16f;
                    _clips.Play(clip, 0.05f, Mathf.Max(0f, contact * len - flight * speed), speed, restart: true);
                }
                else if (_clips.Current is null || _clips.Done || (_clips.Current == p.Win && !cheering)
                         || (walking != (_clips.Current == p.Walk)) && !acting)
                    _clips.Play(walking ? p.Walk : p.Stance, 0.18f, Random.value * 0.5f, p.Speed);
                _lastAttack = Attack; _lastHit = Hit; _lastSkill = Skill;
                _clips.Tick(Freeze > 0f ? dt * 0.05f : dt);
                // the hero's posture over whatever the clip does: a lean, a head tilt, the chest up or down
                if (Rig.Spine != null) Rig.Spine.localRotation *= Quaternion.Euler(0f, 0f, p.Chest);
                if (Rig.Head != null && Rig.Head != Rig.Body) Rig.Head.localRotation *= Quaternion.Euler(0f, p.Tilt, 0f);
                if (Rig.Pelvis != null) Rig.Pelvis.localRotation *= Quaternion.Euler(0f, 0f, p.Lean);
                return true;
            }

            /// <summary>The id the pose library keys on: the hero's, or the enemy's type (SdEnemy wears the heroes' skeleton).</summary>
            string Pid => C.heroId ?? "enemy_" + (C.typeId ?? "x");

            /// <summary>Swaps the eye/mouth sheet on the face renderer's eye submesh (SdRef only).</summary>
            void SetExpression(string expr)
            {
                if (Rig.Blink != null && Rig.Expression != expr) { Rig.Expression = expr; Rig.Blink.Express(expr); return; }
                if (Rig.EyeSub < 0 || Rig.FaceRenderer == null || Rig.Expression == expr) return;
                Rig.Expression = expr;
                var look = SdRefLook.For(C.heroId);
                var b = new MaterialPropertyBlock();
                Rig.FaceRenderer.GetPropertyBlock(b, Rig.EyeSub);
                b.SetTexture("_MainTex", look.EyeSheet(expr));
                Rig.FaceRenderer.SetPropertyBlock(b, Rig.EyeSub);
            }

            void UpdateSprite(float dt, float time, bool walking, Transform cam, MaterialPropertyBlock mpb, float closeUp)
            {
                var root = Rig.Root;
                var body = Rig.Body;
                // face the camera, upright
                var fwd = cam.forward; fwd.y = 0f;
                root.rotation = Quaternion.LookRotation(fwd.sqrMagnitude > 0.001f ? fwd : Vector3.forward, Vector3.up);

                var y = 0f; var lean = 0f; var sx = 1f; var sy = 1f; var dx = 0f;
                var br = Mathf.Sin(time * 3.1f + Z * 2f);
                sx *= 1f - br * 0.012f; sy *= 1f + br * 0.018f;             // breathing

                if (walking)
                {
                    var hop = Mathf.Abs(Mathf.Sin(_walk * 0.9f));
                    y += hop * 0.1f;
                    lean -= 4f;
                    if (hop < 0.2f) { sx *= 1.06f; sy *= 0.94f; }            // landing squash
                }
                if (Attack > 0f)
                {
                    var a = 1f - Attack / 0.32f;                              // 0 → 1
                    if (a < 0.3f) { var k = a / 0.3f; sx *= 1f + 0.08f * k; sy *= 1f - 0.1f * k; lean += 6f * k; }
                    else if (a < 0.55f) { var k = (a - 0.3f) / 0.25f; sx *= 1.08f - 0.14f * k; sy *= 0.9f + 0.18f * k; lean -= 12f * k; dx += 0.14f * k; }
                    else { var k = (a - 0.55f) / 0.45f; sx *= 0.94f + 0.06f * k; sy *= 1.08f - 0.08f * k; lean -= 12f * (1f - k); dx += 0.14f * (1f - k); }
                }
                if (Hit > 0f)
                {
                    var k = Hit / 0.16f;
                    lean += 12f * k;
                    dx += Mathf.Sin(time * 90f) * 0.03f * k;
                    sx *= 1f + 0.05f * k; sy *= 1f - 0.05f * k;
                }
                var spin = 1f;
                if (Skill > 0f)
                {
                    var k = 1f - Skill / 0.75f;
                    y += Mathf.Sin(k * Mathf.PI) * 0.5f;
                    spin = Mathf.Cos(k * Mathf.PI * 2f);                     // a full turn, as a flip
                    var pop = 1f + Mathf.Sin(k * Mathf.PI) * 0.12f;
                    sx *= pop; sy *= pop;
                }
                if (Cheer > 0f || closeUp > 0.5f && C.Alive)
                {
                    var h = Mathf.Abs(Mathf.Sin(time * 7f));
                    y += h * 0.16f;
                    if (h < 0.25f) { sx *= 1.07f; sy *= 0.93f; }
                }
                var alpha = 1f;
                if (Dying > 0f || !C.Alive)
                {
                    if (Dying > 0f) Dying += dt;
                    var k = Dying > 0f ? Mathf.Clamp01(Dying / 0.45f) : 1f;
                    lean = 80f * Mathf.SmoothStep(0f, 1f, k);
                    alpha = Dying > 0f ? 1f - Mathf.Clamp01((Dying - 0.5f) / 0.5f) : 0f;
                }

                // the errors face left: their lunge and lean mirror the party's
                var dir = C.side == Side.Hero ? 1f : -1f;
                dx *= dir; lean *= dir;
                root.position = root.parent.TransformPoint(new Vector3(X, y, Z));
                body.localPosition = new Vector3(dx, 0f, 0f);
                body.localRotation = Quaternion.Euler(0f, 0f, lean);
                body.localScale = new Vector3(sx * spin, sy, 1f);

                if (Rig.SpriteRenderer != null)
                {
                    Rig.SpriteRenderer.GetPropertyBlock(mpb);
                    mpb.SetFloat("_Glow", Hit > 0.06f ? 0.9f : Skill > 0.6f ? 0.5f : 0f);
                    mpb.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
                    Rig.SpriteRenderer.SetPropertyBlock(mpb);
                }
                if (Rig.Sheet != null)
                {
                    var bob = Mathf.Sin(time * 1.7f + Z) * 0.02f;
                    var spot = ChibiBuilder.SpriteSheetSpot(Rig, Rig.SheetSide);
                    Rig.Sheet.localPosition = spot + new Vector3(dx * 0.5f, y + bob + (Attack > 0f ? 0.04f : 0f), 0f);
                    Rig.Sheet.localRotation = Quaternion.Euler(0f, 0f, 18f + lean * 0.3f);
                    var flare = Mathf.Max(Attack > 0f ? 0.4f : 0f, Skill > 0f ? 1f : 0f);
                    Rig.Sheet.localScale = Vector3.one * 1.15f * (1f + flare * 0.15f);
                    if (Rig.SheetRenderer != null)
                    {
                        Rig.SheetRenderer.GetPropertyBlock(mpb);
                        mpb.SetFloat("_Glow", flare);
                        mpb.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
                        Rig.SheetRenderer.SetPropertyBlock(mpb);
                    }
                }
            }

            public Combatant C;
            public ChibiRig Rig;
            public float Scale = 1f, X, Z, LineX, LineZ;
            public Color Accent;
            public float Attack, Hit, Skill, Dying, Cheer, Knock;
            // hitstop: the clip all but stops for a few frames when a blow lands (the attacker's too, for a
            // melee blow), the target shaking in place — the weight a hit has in BA's fights
            public float Freeze, FreezeShake;
            public bool Popped;
            Vector3 _deathScale, _baseScale;
            TrailRenderer _trail;
            public float LastRing = -1f;     // world time of the last hit ring on this actor (HitRing throttle)
            public float Enter;             // 1 → 0: running in from the left at the start of a run
            float _walk, _lastX, _speed;
            /// <summary>Running (above RunSpeed) and the stride's phase, for the footfall dust (BattleWorld.Sync).</summary>
            public bool Running => Rig.RefModel && _speed > RunSpeed * Rig.Root.localScale.x && C.Alive;
            public float Stride => _walk;
            public float LastStride = float.NaN;
            const float RunSpeed = 1.6f;   // world units a second at root scale 1: faster than this, the SD runs

            public void Update(float dt, float time, float targetX, Transform cam, MaterialPropertyBlock mpb, float closeUp)
            {
                var hero = C.side == Side.Hero;
                var drawn = targetX - Mathf.SmoothStep(0f, 1f, Enter) * 4.5f;
                var moved = Mathf.Abs(drawn - _lastX);
                _lastX = drawn;
                // the reference's squads run onto the field at the start of every battle
                Enter = Mathf.MoveTowards(Enter, 0f, dt * 1.25f);
                X = targetX + Knock - Mathf.SmoothStep(0f, 1f, Enter) * 4.5f;
                Knock = Mathf.MoveTowards(Knock, 0f, dt * (0.4f + Mathf.Abs(Knock) * 6f));   // fast out of the shove, a soft settle

                var speed = moved / Mathf.Max(0.0001f, dt);
                var walking = speed > 0.4f && C.Alive;
                // the walk turns with DISTANCE, 2π per stride cycle at this figure's size, so the
                // planted foot stays put however fast the body moves (a fixed 11 rad/s skated at every
                // speed but one). The sprite and doll rigs keep the old rate.
                // above a jog's speed the SD runs (SdPose.Run, its longer stride); the eased speed keeps it from flickering between the two
                _speed = Mathf.Lerp(_speed, walking ? speed : 0f, 1f - Mathf.Exp(-dt * 8f));
                var cycle = _speed > RunSpeed * Rig.Root.localScale.x ? SdPose.RunCycle : SdPose.WalkCycle;
                _walk += walking ? (Rig.RefModel ? moved / Mathf.Max(0.05f, cycle * Rig.Root.localScale.x) * Mathf.PI * 2f : dt * 11f) : 0f;

                // the blow's own clock stops in the hitstop too, so the pose holds on the contact frame
                var live = Freeze > 0f ? dt * 0.05f : dt;
                Attack = Mathf.Max(0f, Attack - live);
                Freeze = Mathf.Max(0f, Freeze - dt);
                Hit = Mathf.Max(0f, Hit - live);
                Skill = Mathf.Max(0f, Skill - dt);
                Cheer = Mathf.Max(0f, Cheer - dt);

                if (Rig.Sprite) { UpdateSprite(dt, time, walking, cam, mpb, closeUp); return; }
                if (Rig.Model3D) { Update3D(dt, time, walking, mpb, closeUp); return; }

                // Facing: the party looks right, the errors left, both turned a little to camera.
                var yaw = hero ? Mathf.Lerp(48f, 82f, closeUp) : 132f;
                if (hero && closeUp > 0.5f && C.Alive && Cheer <= 0f) Cheer = 1.4f;
                var root = Rig.Root;
                var y = 0f;
                if (Skill > 0f) y += Mathf.Sin((1f - Skill / 0.75f) * Mathf.PI) * 0.35f;
                if (Cheer > 0f) y += Mathf.Abs(Mathf.Sin(Cheer * 9f)) * 0.18f;
                if (walking) y += Mathf.Abs(Mathf.Sin(_walk)) * 0.03f;
                root.localPosition = new Vector3(X, y, Z);
                root.localRotation = Quaternion.Euler(0f, yaw + (Skill > 0f ? (1f - Skill / 0.75f) * 360f : 0f), 0f);

                var body = Rig.Body;
                var breathe = Mathf.Sin(time * 2.4f + Z * 3f);
                var lean = 0f;
                if (Hit > 0f) lean = 10f * (Hit / 0.16f);
                if (Attack > 0f) lean = -8f * Mathf.Sin(Attack / 0.32f * Mathf.PI);

                if (Dying > 0f)
                {
                    Dying += dt;
                    var k = Mathf.Clamp01(Dying / 0.4f);
                    lean = 85f * Mathf.SmoothStep(0f, 1f, k);
                    if (!hero)
                    {
                        var shrink = Mathf.Clamp01((Dying - 0.45f) / 0.5f);
                        root.localScale = Vector3.one * Scale * (1f - shrink);
                    }
                }
                else if (!C.Alive) lean = 85f;

                // positive lean tips backward (away from where it faces)
                body.localRotation = Quaternion.Euler(0f, 0f, lean);
                body.localPosition = new Vector3(0f, breathe * 0.004f, 0f);
                if (Rig.Head != null && Rig.Head != body)
                    Rig.Head.localRotation = Quaternion.Euler(breathe * 2f, 0f, Hit > 0f ? 6f : 0f);

                var swing = walking ? Mathf.Sin(_walk) * 28f : 0f;
                if (Rig.LegL != null) Rig.LegL.localRotation = Quaternion.Euler(0f, 0f, swing);
                if (Rig.LegR != null) Rig.LegR.localRotation = Quaternion.Euler(0f, 0f, -swing);
                var armIdle = breathe * 3f;
                if (Rig.ArmL != null) Rig.ArmL.localRotation = Quaternion.Euler(-8f, 0f, -swing * 0.8f + armIdle);
                if (Rig.ArmR != null)
                {
                    // the right arm throws: up and forward during an attack
                    var throwA = Attack > 0f ? Mathf.Sin(Attack / 0.32f * Mathf.PI) * 110f : 0f;
                    if (Cheer > 0f) throwA = 150f;
                    Rig.ArmR.localRotation = Quaternion.Euler(8f, 0f, swing * 0.8f - armIdle + throwA);
                }

                // The sheet stays behind the shoulder and turns to face the camera, as a halo would.
                if (Rig.Sheet != null)
                {
                    var bob = Mathf.Sin(time * 1.7f + Z) * 0.012f;
                    Rig.Sheet.localPosition = new Vector3(-0.17f, 0.66f + bob + (Attack > 0f ? 0.03f : 0f), Rig.SheetSide * 0.13f);
                    Rig.Sheet.rotation = Quaternion.LookRotation(Rig.Sheet.position - cam.position, Vector3.up)
                                         * Quaternion.Euler(0f, 0f, Rig.SheetSide * 10f);
                    var flare = Mathf.Max(Attack > 0f ? 0.35f : 0f, Skill > 0f ? 1f : 0f);
                    Rig.Sheet.localScale = Vector3.one * (1f + flare * 0.12f);
                    if (Rig.SheetRenderer != null)
                    {
                        Rig.SheetRenderer.GetPropertyBlock(mpb);
                        mpb.SetFloat("_Glow", flare);
                        Rig.SheetRenderer.SetPropertyBlock(mpb);
                        Rig.SheetRenderer.enabled = C.Alive || Dying < 0.3f;
                    }
                }

                // hit flash on the toon parts
                var flash = Hit > 0.08f ? 0.85f : 0f;
                SetFlash(flash, mpb, toonOnly: true);
            }
        }
    }
}
