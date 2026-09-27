using System.Collections.Generic;
using System.Linq;
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
    public class BattleWorld
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
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            PlaceCamera(0f);

            // light from the upper left, a little in front — the reference's key light
            Shader.SetGlobalVector("_EhLightDir", new Vector4(-0.45f, 0.85f, -0.5f, 0f));
            _mpb = new MaterialPropertyBlock();
        }

        // 0 = the fight's wide shot, 1 = the victory close-up on the party (the reference's
        // "Battle Complete": the camera comes down in front of the squad as they cheer).
        float _closeUp, _closeUpTarget;
        Vector3 _partyCentre;

        /// <summary>Victory: bring the camera down onto the party, who turn to it and cheer.</summary>
        public void Celebrate(bool on) => _closeUpTarget = on ? 1f : 0f;

        void PlaceCamera(float shake)
        {
            var k = Mathf.SmoothStep(0f, 1f, _closeUp);
            // The reference's battle camera (temp_images/712980) is HIGH: about 32° above the
            // street, looking down onto the road, the sidewalk and the storefronts — no sky. The
            // close-up for the win drops lower and nearer to the squad's centre.
            var target = Vector3.Lerp(new Vector3(0f, 0.4f, 0.9f), _partyCentre + new Vector3(0.2f, 0.55f, 0f), k);
            var pitch = Mathf.Lerp(32f, 14f, k) * Mathf.Deg2Rad;
            var dist = Mathf.Lerp(9.2f, 5.8f, k);
            var pos = target + new Vector3(0f, Mathf.Sin(pitch), -Mathf.Cos(pitch)) * dist;
            if (shake > 0f) pos += new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * shake * 0.04f;
            _cam.transform.localPosition = pos;
            _cam.transform.localRotation = Quaternion.LookRotation(target - pos, Vector3.up);
        }

        // ------------------------------------------------------------------ lifecycle --

        /// <summary>Attach to a (new or resumed) run: rebuild the set for its mood and the cast.</summary>
        public void Begin(BattleSim sim)
        {
            _sim = sim;
            _closeUp = _closeUpTarget = 0f;
            foreach (var a in _actors.Values) Object.Destroy(a.Rig.Root.gameObject);
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
                _setMood = mood;
                _cam.backgroundColor = mood == 2 ? new Color(0.16f, 0.18f, 0.32f) : new Color(0.86f, 0.93f, 1f);
            }
            _entering = sim.Elapsed < 0.5f;
            foreach (var h in sim.Heroes) Ensure(h);
            foreach (var m in sim.Monsters) Ensure(m);
            _entering = false;
        }

        bool _entering;

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

        public void SetVisible(bool on) => _cam.enabled = on && _rt != null;

        /// <summary>Keep the render target the size of the field on screen (in real pixels).</summary>
        public void Resize(int w, int h)
        {
            w = Mathf.Clamp(w, 64, 4096);
            h = Mathf.Clamp(h, 64, 4096);
            if (_rt != null && _rt.width == w && _rt.height == h) return;
            if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Object.Destroy(_rt); }
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "BattleRT", antiAliasing = 1, filterMode = FilterMode.Bilinear };
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
            return c.side == Side.Hero ? LaneZ[i % LaneZ.Length] : LaneZ[(i + 1) % LaneZ.Length] * 0.9f;
        }

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

        Actor Ensure(Combatant c)
        {
            if (_actors.TryGetValue(c, out var a)) return a;
            a = new Actor { C = c };
            // 3D SD first (made from the 2D SD, World/SdModel), then the 2D SD sprite, then the built doll
            // the common SD base (World/SdBase) first: one body, one skeleton, one set of motions
            if (c.side == Side.Hero && (SdRef.Build(c.heroId, _root, Layer) ?? SdBase.Build(c.heroId, _root, Layer) ?? SdSprite.Build(c.heroId, _root, Layer)) is { } sd)
            {
                a.Rig = sd;
                a.Rig.Root.name = c.name;
                var def = GameData.Hero(c.heroId);
                var owned = Game.Player?.Find(c.heroId);
                var spec = BackSheet.For(def, owned);
                // a 2D SD already has its sheet painted in; the 3D one carries it on the back
                if (!a.Rig.Sprite) ChibiBuilder.AddSheet(a.Rig, SheetTexture.For(spec, c.heroId), spec.Left ? 1 : -1, Layer);
                if (a.Rig.Model3D) a.Rig.Sheet.localScale = Vector3.one * 0.9f;
                if (a.Rig.RefModel) SdRef.WearSheet(a.Rig, 0.9f);
                a.Scale = c.role == "tank" ? 1.06f : 1f;
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
            else if ((SdModel.BuildMonster(c.boss != null ? c.boss.id : c.typeId, _root, Layer) ?? SdSprite.BuildMonster(c.boss != null ? c.boss.id : c.typeId, _root, Layer)) is { } sdm)
            {
                a.Rig = sdm;
                a.Rig.Root.name = c.name;
                a.Scale = c.boss != null ? 1.9f : c.elite ? 1.25f : 1f;
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
                        t.Knock = e.target.side == Side.Hero ? -0.12f : 0.12f;
                        Spark(t, e.crit ? new Color(1f, 0.85f, 0.3f) : Color.white, e.crit ? 0.7f : 0.45f);
                        HitRing(t, e.crit);
                    }
                    if (e.actor != null && _actors.TryGetValue(e.actor, out var a) && a.Attack <= 0f) a.Attack = 0.3f;
                    break;
                case EventKind.Heal:
                    if (e.target != null && _actors.TryGetValue(e.target, out var hl))
                        Spark(hl, new Color(0.45f, 1f, 0.6f), 0.6f, rise: true);
                    break;
                case EventKind.Skill:
                    if (e.actor != null && _actors.TryGetValue(e.actor, out var s))
                    {
                        s.Skill = 0.75f;
                        SkillBurst(s);
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
            var living = _actors.Values.Where(a => a.C.side == Side.Hero && a.C.Alive).ToList();
            if (living.Count > 0 && _closeUpTarget <= 0f)
                _partyCentre = new Vector3(living.Average(a => a.X), 0f, living.Average(a => a.Z));
            PlaceCamera((shake + _localShake * 20f) * (1f - _closeUp));

            foreach (var c in _sim.Heroes) Ensure(c);
            foreach (var c in _sim.Monsters) Ensure(c);

            // where the fight is, for the heads: the nearest living enemy / hero along the lane
            var nearestEnemyX = _sim.Monsters.Where(m => m.Alive).Select(m => WX(m.x)).DefaultIfEmpty(WX(800f)).Min();
            var nearestHeroX = _sim.Heroes.Where(h => h.Alive).Select(h => WX(h.x)).DefaultIfEmpty(WX(0f)).Max();
            foreach (var (c, a) in _actors.ToList())
            {
                var gone = c.side == Side.Monster && !_sim.Monsters.Contains(c);
                if (gone && (a.Dying <= 0f || a.Dying > 1.1f))
                {
                    Object.Destroy(a.Rig.Root.gameObject);
                    _actors.Remove(c);
                    continue;
                }
                // The victory shot is the party's: whatever is left of the wave steps out of it.
                if (c.side == Side.Monster) a.Rig.Root.gameObject.SetActive(_closeUp < 0.05f);
                a._enemyX = nearestEnemyX; a._heroX = nearestHeroX;
                a.Update(dt, _time, WX(drawX(c)), _cam.transform, _mpb, _closeUp);
            }

            SyncShots();
            UpdateSparks(dt);
            UpdateFx(dt);
            _localShake = Mathf.MoveTowards(_localShake, 0f, dt * 1.5f);
        }

        void SyncShots()
        {
            foreach (var shot in _sim.Shots)
            {
                if (!_shots.TryGetValue(shot, out var t))
                {
                    t = MakeShot(shot);
                    _shots[shot] = t;
                    if (shot.From != null && _actors.TryGetValue(shot.From, out var fa)) fa.Attack = 0.32f;
                }
                if (shot.From == null || shot.To == null) continue;
                _actors.TryGetValue(shot.From, out var from);
                _actors.TryGetValue(shot.To, out var to);
                if (from == null || to == null) continue;

                var k = shot.Progress;
                var start = from.Rig.Sheet != null && shot.From.side == Side.Hero
                    ? from.Rig.Sheet.position
                    : from.Rig.Root.position + Vector3.up * from.Rig.Height * from.Scale * 0.55f;
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
            foreach (var pair in _shots.ToList())
            {
                if (_sim.Shots.Contains(pair.Key)) continue;
                Object.Destroy(pair.Value.gameObject);
                _shots.Remove(pair.Key);
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

            var floor = MeshKit.Part("ring", _root, FloorQuad, ring, Layer).transform;
            floor.position = a.Rig.Root.position + Vector3.up * 0.02f;
            _fx.Add(new Fx { T = floor, Life = 0.6f, Max = 0.6f, Grow0 = 0.4f, Grow1 = 3.2f, Flat = true });

            var up = MeshKit.Part("ring", _root, Quad, ring, Layer).transform;
            up.position = centre + new Vector3(0f, 0f, 0.3f);
            _fx.Add(new Fx { T = up, Life = 0.5f, Max = 0.5f, Grow0 = 0.5f, Grow1 = 2.6f, Face = true });

            Spark(a, Color.white, 2.2f);
            for (var i = 0; i < 14; i++)
            {
                var shard = MeshKit.Part("shard", _root, Cell, MeshKit.Toon, Layer).transform;
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
        void HitRing(Actor at, bool crit)
        {
            if (at.LastRing >= 0f && _time - at.LastRing < 0.12f) return;
            at.LastRing = _time;
            var c = crit ? new Color(1f, 0.86f, 0.4f, 0.95f) : new Color(0.78f, 0.96f, 1f, 0.9f);
            if (!RingMats.TryGetValue(c, out var ring) || ring == null) RingMats[c] = ring = MeshKit.NewGlass(RingTex, c);
            var s = at.Scale * (crit ? 1.3f : 1f);
            var floor = MeshKit.Part("hitring", _root, FloorQuad, ring, Layer).transform;
            floor.position = at.Rig.Root.position + Vector3.up * 0.025f;
            _fx.Add(new Fx { T = floor, Life = 0.3f, Max = 0.3f, Grow0 = 0.25f * s, Grow1 = 1.05f * s, Flat = true });
            var up = MeshKit.Part("hitring", _root, Quad, ring, Layer).transform;
            up.position = at.Rig.Root.position + Vector3.up * at.Rig.Height * at.Scale * 0.55f + new Vector3(0f, 0f, -0.32f);
            _fx.Add(new Fx { T = up, Life = 0.22f, Max = 0.22f, Grow0 = 0.15f * s, Grow1 = 0.75f * s, Face = true });
        }

        float _localShake;
        void AddShakeLocal(float s) => _localShake = Mathf.Max(_localShake, s);

        struct Fx
        {
            public Transform T;
            public float Life, Max, Grow0, Grow1;
            public Vector3 Vel;
            public bool Gravity, Flat, Face, Spin;
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
                f.Life -= dt;
                if (f.Life <= 0f || f.T == null)
                {
                    if (f.T != null) Object.Destroy(f.T.gameObject);
                    _fx.RemoveAt(i);
                    continue;
                }
                var k = 1f - f.Life / f.Max;
                if (f.Gravity) f.Vel += Vector3.down * 9f * dt;
                f.T.position += f.Vel * dt;
                var sc = Mathf.Lerp(f.Grow0, f.Grow1, 1f - (1f - k) * (1f - k));
                f.T.localScale = Vector3.one * sc;
                if (f.Face) f.T.rotation = Quaternion.LookRotation(f.T.position - _cam.transform.position);
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
                    if (Dying > 0f || !C.Alive) _pose = SdPose.Dead(Dying > 0f ? Mathf.Clamp01(Dying / 0.45f) : 1f);
                    else if (cheering) _pose = SdPose.Victory(SdPose.WinOf(C.heroId), _winT);
                    else if (Skill > 0f) _pose = SdPose.Skill(SdPose.AttackOf(C.heroId, C.role), 1f - Skill / 0.75f);
                    else if (Hit > 0f) _pose = SdPose.Hit(Hit / 0.16f);
                    else if (Attack > 0f) _pose = SdPose.Attack(SdPose.AttackOf(C.heroId, C.role), 1f - Attack / 0.32f);
                    else if (walking) _pose = SdPose.Walk(_walk * 0.9f);
                    else _pose = SdPose.Ready(SdPose.AttackOf(C.heroId, C.role), time, Z * 2f);   // in a fight: the combat stance, not the lobby idle
                    // the head looks at the fight: heroes toward the enemy line, enemies toward the squad
                    if (!cheering && (Attack <= 0f) && C.Alive)
                    {
                        var look = hero ? Mathf.Clamp((_enemyX - X) * 6f, -14f, 14f) : Mathf.Clamp((_heroX - X) * -6f, -14f, 14f);
                        _pose.HeadYaw += look;
                    }
                    // a damped spring between states instead of a fade: a body overshoots a little
                    // and settles; stiffer into an attack or a hit
                    var omega = Attack > 0f || Hit > 0f ? 42f : 26f;
                    if (!_shownInit) { _shown = _pose; _vel = new float[Pose.Count]; _shownInit = true; }
                    else Pose.Spring(ref _shown, _vel, _pose, dt, omega, 0.78f);
                    y = _shown.Y; spin = _shown.Yaw;
                }
                // The reference's squads FACE the enemy (right), seen from behind-and-above; they only
                // turn to the camera for the win close-up. Root rotation 180 = facing the camera,
                // 90 = facing +x (the enemies): heroes at 105, enemies mirrored at 255.
                var yaw = hero ? Mathf.Lerp(-75f, -10f, closeUp) : 75f;
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
                var facing = Rig.RefModel ? root.localRotation * Vector3.forward * _shown.Step : Vector3.zero;   // the pose's step along the facing
                root.localPosition = new Vector3(X + lunge, y, Z) + facing;
                // bones: offsets onto the rest pose (the sample rig's rest rotations are not identity)
                if (Rig.RefModel)
                {
                    SdPose.Apply(Rig, _shown);
                    SdExpr.Tick(Rig, C.heroId, _shown.Expr, time);
                }
                else
                {
                    if (Rig.Body != null) Rig.Body.localRotation = Quaternion.Euler(lean, twist, 0f);
                    if (Rig.Head != null) Rig.Head.localRotation = Quaternion.Euler(br * 2f, 0f, Hit > 0f ? 6f : 0f);
                    if (Rig.ArmL != null) Rig.ArmL.localRotation = Quaternion.Euler(fwdL, 0f, armL);
                    if (Rig.ArmR != null) Rig.ArmR.localRotation = Quaternion.Euler(fwdR, 0f, armR);
                    if (Rig.LegL != null) Rig.LegL.localRotation = Quaternion.Euler(legSwing, 0f, 0f);
                    if (Rig.LegR != null) Rig.LegR.localRotation = Quaternion.Euler(-legSwing, 0f, 0f);
                }
                var flash = Hit > 0.08f ? 0.8f : 0f;
                foreach (var r in Rig.Renderers)
                {
                    if (r == null || r == Rig.SheetRenderer) continue;
                    r.GetPropertyBlock(mpb); mpb.SetFloat("_Flash", flash); r.SetPropertyBlock(mpb);
                }
                if (Rig.Sheet != null && !Rig.SheetWorn)
                {
                    Rig.Sheet.localPosition = new Vector3(Rig.SheetSide * 0.12f, Rig.Height * 0.62f + Mathf.Sin(time * 1.7f) * 0.015f, -0.14f);
                    Rig.Sheet.localRotation = Quaternion.Euler(0f, 180f, Rig.SheetSide * 12f);
                }
            }

            /// <summary>Swaps the eye/mouth sheet on the face renderer's eye submesh (SdRef only).</summary>
            void SetExpression(string expr)
            {
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
            public float Scale = 1f, X, Z;
            public Color Accent;
            public float Attack, Hit, Skill, Dying, Cheer, Knock;
            public float LastRing = -1f;     // world time of the last hit ring on this actor (HitRing throttle)
            public float Enter;             // 1 → 0: running in from the left at the start of a run
            float _walk, _lastX;

            public void Update(float dt, float time, float targetX, Transform cam, MaterialPropertyBlock mpb, float closeUp)
            {
                var hero = C.side == Side.Hero;
                var drawn = targetX - Mathf.SmoothStep(0f, 1f, Enter) * 4.5f;
                var moved = Mathf.Abs(drawn - _lastX);
                _lastX = drawn;
                // the reference's squads run onto the field at the start of every battle
                Enter = Mathf.MoveTowards(Enter, 0f, dt * 1.25f);
                X = targetX + Knock - Mathf.SmoothStep(0f, 1f, Enter) * 4.5f;
                Knock = Mathf.MoveTowards(Knock, 0f, dt * 1.2f);

                var speed = moved / Mathf.Max(0.0001f, dt);
                var walking = speed > 0.4f && C.Alive;
                _walk += dt * (walking ? 11f : 0f);

                Attack = Mathf.Max(0f, Attack - dt);
                Hit = Mathf.Max(0f, Hit - dt);
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
                foreach (var r in Rig.Renderers)
                {
                    if (r == null || r == Rig.SheetRenderer) continue;
                    if (r.sharedMaterial == null || r.sharedMaterial.shader != MeshKit.ToonShader) continue;
                    r.GetPropertyBlock(mpb);
                    mpb.SetFloat("_Flash", flash);
                    r.SetPropertyBlock(mpb);
                }
            }
        }
    }
}
