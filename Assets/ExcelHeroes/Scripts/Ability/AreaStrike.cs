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
    // Delayed area strike: a warning at ctx.TargetPoint → the delay → the blast.
    // Excel Heroes: the warning is the #REF! floor mark, the blast a flash; the sim's hit lands the damage.
    [CreateAssetMenu(menuName = "ExcelHeroes/Effect/AreaStrike")]
    public class AreaStrike : EffectData, IEffect
    {
        [SerializeField] private float _delay = 1f;
        [SerializeField] private float _radius = 1f;
        [SerializeField] private bool _danger = true;
        [SerializeField] private Color _blast = new Color(1f, 0.45f, 0.35f, 0.95f);

        public static AreaStrike Make(float delay, float radius, bool danger = true)
        {
            var e = CreateInstance<AreaStrike>(); e._delay = delay; e._radius = radius; e._danger = danger; return e;
        }

        public override IEffect CreateRuntime() => this;

        public async UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token)
        {
            var pos = ctx.TargetPoint;
            AbilityHost.Presenter?.Warn(pos, _radius, _delay, _danger);
            bool canceled = await UniTask.Delay(TimeSpan.FromSeconds(_delay), cancellationToken: token).SuppressCancellationThrow();
            if (canceled) return;
            AbilityHost.Presenter?.Blast(pos, _radius, _blast);
        }
    }
}
