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
        Transform _set;
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
            var target = Vector3.Lerp(new Vector3(0.1f, 0.55f, 0.35f), _partyCentre + new Vector3(0.9f, 0.5f, 0f), k);
            var pitch = Mathf.Lerp(25f, 12f, k) * Mathf.Deg2Rad;
            var dist = Mathf.Lerp(7.7f, 4.6f, k);
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
                _set = OfficeStage.Build(_root, Layer, sim.Stage);
                _setMood = mood;
                _cam.backgroundColor = mood == 2 ? new Color(0.16f, 0.18f, 0.32f) : new Color(0.86f, 0.93f, 1f);
            }
            _entering = sim.Elapsed < 0.5f;
            foreach (var h in sim.Heroes) Ensure(h);
            foreach (var m in sim.Monsters) Ensure(m);
            _entering = false;
        }

        bool _entering;

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
        }

        // ------------------------------------------------------------------ mapping --

        /// <summary>Sim lane x (the web build's 832-unit field) to world x.</summary>
        public static float WX(float simX) => (simX - 400f) / 64f * 0.95f;

        static readonly float[] LaneZ = { 0.15f, 0.95f, -0.7f, 1.55f, -1.25f };

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
            if (c.side == Side.Hero)
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
                        Spark(s, s.Accent, 1.1f);
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
            PlaceCamera(shake * (1f - _closeUp));

            foreach (var c in _sim.Heroes) Ensure(c);
            foreach (var c in _sim.Monsters) Ensure(c);

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
                a.Update(dt, _time, WX(drawX(c)), _cam.transform, _mpb, _closeUp);
            }

            SyncShots();
            UpdateSparks(dt);
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
            public Combatant C;
            public ChibiRig Rig;
            public float Scale = 1f, X, Z;
            public Color Accent;
            public float Attack, Hit, Skill, Dying, Cheer, Knock;
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
