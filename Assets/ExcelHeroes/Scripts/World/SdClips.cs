using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ExcelHeroes.World
{
    /// <summary>
    /// Keyframed motion for the SD figures: Quaternius' Universal Animation Library (CC0,
    /// Resources/Anim/UAL1_Standard.fbx) retargeted through the Humanoid avatar (SdHumanoid).
    /// Replaces the hand-written joint angles of SdPose for anything that has a clip; SdPose stays
    /// for faces, the lobby and anything without one.
    ///
    /// One manual PlayableGraph per figure with a two-input mixer: Play() cross-fades to a clip,
    /// Tick() advances and evaluates. After evaluation the pelvis is put back at its rest place
    /// (the model's 114.8x import scale turns a clip's body position into metres of drift) and
    /// turned round (the samples face the model's −Z, Mecanim drives +Z), and the feet are planted
    /// the way SdPose.Apply plants them (rig.FootDrop).
    /// </summary>
    public class SdClips
    {
        static Dictionary<string, AnimationClip> _lib;

        public static AnimationClip Clip(string name)
        {
            if (_lib == null)
            {
                _lib = new Dictionary<string, AnimationClip>();
                foreach (var c in Resources.LoadAll<AnimationClip>("Anim/UAL1_Standard").Concat(Resources.LoadAll<AnimationClip>("Anim/UAL2_Standard")))
                {
                    var n = c.name; var bar = n.LastIndexOf('|');
                    _lib[bar >= 0 ? n.Substring(bar + 1) : n] = c;
                }
            }
            return name != null && _lib.TryGetValue(name, out var clip) ? clip : null;
        }

        public static bool Available => Clip("Idle_Loop") != null;

        readonly ChibiRig _rig;
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        readonly AnimationClipPlayable[] _slot = new AnimationClipPlayable[2];
        readonly string[] _name = new string[2];
        readonly float[] _t = new float[2], _speed = new float[2];
        int _cur;
        float _fade, _fadeLen;
        Vector3 _pelvisRest;

        public string Current => _name[_cur];
        public float Time01 { get { var c = _slot[_cur]; if (!c.IsValid()) return 0f; var len = c.GetAnimationClip().length; return len <= 0f ? 1f : _t[_cur] / len; } }
        public bool Done { get { var c = _slot[_cur]; return !c.IsValid() || (!c.GetAnimationClip().isLooping && _t[_cur] >= c.GetAnimationClip().length); } }

        public SdClips(ChibiRig rig, string id)
        {
            _rig = rig;
            if (!rig.Model.gameObject.TryGetComponent<Animator>(out var anim)) anim = rig.Model.gameObject.AddComponent<Animator>();
            anim.avatar = SdHumanoid.Build(rig.Model, id);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _pelvisRest = rig.Pelvis.localPosition;
            _graph = PlayableGraph.Create("sdclips:" + id);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "out", anim);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            output.SetSourcePlayable(_mixer);
        }

        /// <summary>Cross-fade to `clip` from time `start` (seconds) at `speed`. Same clip again: kept unless restart.</summary>
        public void Play(string clip, float fade = 0.12f, float start = 0f, float speed = 1f, bool restart = false)
        {
            if (_name[_cur] == clip && !restart) { _speed[_cur] = speed; return; }
            var c = Clip(clip);
            if (c == null) return;
            var next = 1 - _cur;
            if (_slot[next].IsValid()) { _graph.Disconnect(_mixer, next); _slot[next].Destroy(); }
            _slot[next] = AnimationClipPlayable.Create(_graph, c);
            _slot[next].SetApplyFootIK(false);
            _graph.Connect(_slot[next], 0, _mixer, next);
            _name[next] = clip; _t[next] = Mathf.Max(0f, start); _speed[next] = speed;
            _cur = next;
            _fadeLen = _name[1 - _cur] == null ? 0f : fade; _fade = 0f;
        }

        public void Tick(float dt)
        {
            if (!_graph.IsValid() || !_slot[_cur].IsValid()) return;
            for (var i = 0; i < 2; i++)
            {
                if (!_slot[i].IsValid()) continue;
                _t[i] += dt * _speed[i];
                var clip = _slot[i].GetAnimationClip();
                var t = clip.isLooping ? Mathf.Repeat(_t[i], clip.length) : Mathf.Min(_t[i], clip.length);
                _slot[i].SetTime(t);
            }
            _fade = _fadeLen <= 0f ? 1f : Mathf.Min(1f, _fade + dt / _fadeLen);
            var w = Mathf.SmoothStep(0f, 1f, _fade);
            _mixer.SetInputWeight(_cur, w);
            _mixer.SetInputWeight(1 - _cur, _slot[1 - _cur].IsValid() ? 1f - w : 0f);
            _graph.Evaluate(0f);

            var rig = _rig;
            rig.Pelvis.localPosition = _pelvisRest;
            rig.Pelvis.rotation = Quaternion.AngleAxis(180f, rig.Root.up) * rig.Pelvis.rotation;
            if (rig.FootL != null && rig.FootR != null && rig.RestFootY != float.MinValue && !float.IsNaN(rig.RestFootY))
            {
                var low = Mathf.Min(rig.Root.InverseTransformPoint(rig.FootL.position).y, rig.Root.InverseTransformPoint(rig.FootR.position).y);
                rig.FootDrop = rig.RestFootY - low;
            }
        }

        public void Dispose() { if (_graph.IsValid()) _graph.Destroy(); }
    }
}
