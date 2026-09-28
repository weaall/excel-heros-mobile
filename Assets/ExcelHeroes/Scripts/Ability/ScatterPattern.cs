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
    // N points over a disc or ring (area-uniform), the children run at each.
    [CreateAssetMenu(menuName = "ExcelHeroes/Effect/ScatterPattern")]
    public class ScatterPattern : EffectData, IEffect
    {
        [SerializeField] private Anchor _center = new();
        [SerializeField] private float _innerRadius = 0f;
        [SerializeField] private float _outerRadius = 2f;
        [SerializeField] private int _count = 8;
        [SerializeField] private float _interval = 0.05f;
        [SerializeField] private List<EffectData> _children = new();

        public static ScatterPattern Make(Anchor center, float inner, float outer, int count, float interval, params EffectData[] children)
        {
            var e = CreateInstance<ScatterPattern>();
            e._center = center; e._innerRadius = inner; e._outerRadius = outer; e._count = count; e._interval = interval;
            e._children.AddRange(children);
            return e;
        }

        public override IEffect CreateRuntime() => this;

        public async UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token)
        {
            var payload = new List<IEffect>(_children.Count);
            foreach (var c in _children) if (c) payload.Add(c.CreateRuntime());
            _center.Resolve(ctx, out Vector3 center, out _);
            for (int i = 0; i < _count; i++)
            {
                if (token.IsCancellationRequested) return;
                Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
                float r = Mathf.Sqrt(UnityEngine.Random.Range(_innerRadius * _innerRadius, _outerRadius * _outerRadius));
                Vector3 point = center + new Vector3(dir.x, 0f, dir.y) * r;
                var sub = ctx.CloneAt(point);
                foreach (var e in payload) await e.ExecuteAsync(sub, token);
                if (_interval > 0f && i < _count - 1)
                    if (await UniTask.Delay(TimeSpan.FromSeconds(_interval), cancellationToken: token).SuppressCancellationThrow()) return;
            }
        }
    }
}
