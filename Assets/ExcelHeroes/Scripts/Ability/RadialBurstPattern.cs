// Ported from OperationKivotos-Code by vfly1189 — https://github.com/vfly1189/OperationKivotos-Code
// Used with the author's permission (given to the project owner by phone, 2026-09-28; see
// THIRD_PARTY_NOTICES.md). Adapted for Excel Heroes: namespace ExcelHeroes.Ability, code-built
// effect assets (the static Make factories), and the prefab / physics payloads replaced by an
// IAbilityPresenter — the battle sim still resolves every hit; abilities choreograph how it looks.
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ExcelHeroes.Ability
{
    // Points in several directions, the radius interpolated wave by wave (outside in, or in out).
    // Where (the centre) is the Anchor; what is the children, run at each point.
    [CreateAssetMenu(menuName = "ExcelHeroes/Effect/RadialBurstPattern")]
    public class RadialBurstPattern : EffectData, IEffect
    {
        [SerializeField] private Anchor _center = new();
        [SerializeField] private int _directions = 4;
        [SerializeField] private int _waves = 5;
        [SerializeField] private float _startRadius = 8f;
        [SerializeField] private float _endRadius = 0f;
        [SerializeField] private float _angleStep = 0f;
        [SerializeField] private float _interval = 0.25f;
        [SerializeField] private List<EffectData> _children = new();

        public static RadialBurstPattern Make(Anchor center, int directions, int waves, float startRadius, float endRadius,
                                              float angleStep, float interval, params EffectData[] children)
        {
            var e = CreateInstance<RadialBurstPattern>();
            e._center = center; e._directions = directions; e._waves = waves; e._startRadius = startRadius;
            e._endRadius = endRadius; e._angleStep = angleStep; e._interval = interval; e._children.AddRange(children);
            return e;
        }

        public override IEffect CreateRuntime() => this;

        public async UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token)
        {
            var payload = new List<IEffect>(_children.Count);
            foreach (var c in _children) if (c) payload.Add(c.CreateRuntime());
            _center.Resolve(ctx, out Vector3 center, out _);
            for (int w = 0; w < _waves; w++)
            {
                float t = _waves <= 1 ? 0f : (float)w / (_waves - 1);
                float radius = Mathf.Lerp(_startRadius, _endRadius, t);
                for (int d = 0; d < _directions; d++)
                {
                    if (token.IsCancellationRequested) return;
                    float ang = _angleStep * w + 360f / _directions * d;
                    Vector3 point = center + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * radius;
                    var sub = ctx.CloneAt(point);
                    foreach (var e in payload) await e.ExecuteAsync(sub, token);
                }
                if (_interval > 0f && w < _waves - 1)
                {
                    bool canceled = await UniTask.Delay(TimeSpan.FromSeconds(_interval), cancellationToken: token).SuppressCancellationThrow();
                    if (canceled) return;
                }
            }
        }
    }
}
