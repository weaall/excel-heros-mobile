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
    // Repetition as its own part: the children, _count times, _interval apart.
    //   burst = Repeat{ 5, 0.1, [ SpawnProjectiles(1), SpawnVFX(muzzle) ] }
    [CreateAssetMenu(menuName = "ExcelHeroes/Effect/Repeat")]
    public class RepeatEffect : EffectData, IEffect
    {
        [SerializeField] private int _count = 1;
        [SerializeField] private float _interval = 0.1f;
        [SerializeField] private List<EffectData> _children = new();

        public static RepeatEffect Make(int count, float interval, params EffectData[] children)
        {
            var e = CreateInstance<RepeatEffect>(); e._count = count; e._interval = interval; e._children.AddRange(children); return e;
        }

        public override IEffect CreateRuntime() => this;

        public async UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token)
        {
            var effects = new List<IEffect>(_children.Count);
            foreach (var child in _children) if (child != null) effects.Add(child.CreateRuntime());
            for (int i = 0; i < _count; i++)
            {
                for (int e = 0; e < effects.Count; e++)
                {
                    if (token.IsCancellationRequested) return;
                    await effects[e].ExecuteAsync(ctx, token);
                }
                if (_interval > 0f && i < _count - 1)
                {
                    bool canceled = await UniTask.Delay(TimeSpan.FromSeconds(_interval), cancellationToken: token).SuppressCancellationThrow();
                    if (canceled) return;
                }
            }
        }
    }
}
