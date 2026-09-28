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
    // Selection: asks the caster to pick one of N at random. Knows nothing of what the choices are.
    [CreateAssetMenu(menuName = "ExcelHeroes/Effect/SelectRandomChoice")]
    public class SelectRandomChoice : EffectData, IEffect
    {
        public static SelectRandomChoice Make() => CreateInstance<SelectRandomChoice>();

        public override IEffect CreateRuntime() => this;

        public UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token)
        {
            if (ctx.CasterGO != null && ctx.CasterGO.TryGetComponent<IChoiceHandler>(out var h) && h.ChoiceCount > 0)
                h.ApplyChoice(Random.Range(0, h.ChoiceCount));
            return UniTask.CompletedTask;
        }
    }
}
