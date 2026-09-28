// Ported from OperationKivotos-Code by vfly1189 — https://github.com/vfly1189/OperationKivotos-Code
// Used with the author's permission (given to the project owner by phone, 2026-09-28; see
// THIRD_PARTY_NOTICES.md). Adapted for Excel Heroes: namespace ExcelHeroes.Ability, code-built
// effect assets (the static Make factories), and the prefab / physics payloads replaced by an
// IAbilityPresenter — the battle sim still resolves every hit; abilities choreograph how it looks.
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ExcelHeroes.Ability
{
    // N pellets at once across a fan of _spreadAngle degrees (shotgun). Same class for everyone who
    // fans; only the values differ. Excel Heroes: the fan is a pasted range of cells.
    [CreateAssetMenu(menuName = "ExcelHeroes/Effect/SpreadProjectile")]
    public class SpreadProjectiles : EffectData, IEffect
    {
        [SerializeField] private int _count = 5;
        [SerializeField] private float _spreadAngle = 20f;

        public static SpreadProjectiles Make(int count = 5, float spreadAngle = 20f)
        {
            var e = CreateInstance<SpreadProjectiles>(); e._count = count; e._spreadAngle = spreadAngle; return e;
        }

        public override IEffect CreateRuntime() => this;

        public UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token)
        {
            float startAngle = -_spreadAngle * 0.5f;
            float step = _count > 1 ? _spreadAngle / (_count - 1) : 0f;
            var from = ctx.Object != null ? ctx.Object.position : ctx.CasterGO.transform.position;
            var to = ctx.Target != null ? ctx.Target.transform.position : ctx.TargetPoint;
            for (int i = 0; i < _count; i++)
            {
                float angle = startAngle + step * i;
                // lateral: the fan's offset at the target, as a fraction of the distance (tan of the angle)
                AbilityHost.Presenter?.Projectile(ctx, from, to, ctx.Flight, Mathf.Tan(angle * Mathf.Deg2Rad));
            }
            return UniTask.CompletedTask;
        }
    }
}
