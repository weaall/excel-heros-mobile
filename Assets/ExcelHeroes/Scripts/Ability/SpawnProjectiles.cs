// Ported from OperationKivotos-Code by vfly1189 — https://github.com/vfly1189/OperationKivotos-Code
// Used with the author's permission (given to the project owner by phone, 2026-09-28; see
// THIRD_PARTY_NOTICES.md). Adapted for Excel Heroes: namespace ExcelHeroes.Ability, code-built
// effect assets (the static Make factories), and the prefab / physics payloads replaced by an
// IAbilityPresenter — the battle sim still resolves every hit; abilities choreograph how it looks.
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ExcelHeroes.Ability
{
    // Straight shots from the muzzle at the target, _count of them _interval apart.
    // Excel Heroes: the bullet prefab is the presenter's projectile — a cell, a formula, a trace
    // arrow, a coffee — picked by ctx.Payload; the sim lands the damage.
    [CreateAssetMenu(menuName = "ExcelHeroes/Effect/SpawnProjectile")]
    public class SpawnProjectiles : EffectData, IEffect
    {
        [SerializeField] public int _count = 1;
        [SerializeField] public float _interval = 0.1f;

        public static SpawnProjectiles Make(int count = 1, float interval = 0.1f)
        {
            var e = CreateInstance<SpawnProjectiles>(); e._count = count; e._interval = interval; return e;
        }

        public override IEffect CreateRuntime() => this;

        public async UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token)
        {
            for (int i = 0; i < _count; i++)
            {
                var from = ctx.Object != null ? ctx.Object.position : ctx.CasterGO.transform.position;
                var to = ctx.Target != null ? ctx.Target.transform.position : ctx.TargetPoint;
                AbilityHost.Presenter?.Projectile(ctx, from, to, ctx.Flight, 0f);
                if (_interval > 0f && i < _count - 1)
                {
                    bool canceled = await UniTask.Delay(TimeSpan.FromSeconds(_interval), cancellationToken: token).SuppressCancellationThrow();
                    if (canceled) return;
                }
            }
        }
    }
}
