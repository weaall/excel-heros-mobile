// Ported from OperationKivotos-Code by vfly1189 — https://github.com/vfly1189/OperationKivotos-Code
// Used with the author's permission (given to the project owner by phone, 2026-09-28; see
// THIRD_PARTY_NOTICES.md). Adapted for Excel Heroes: namespace ExcelHeroes.Ability, code-built
// effect assets (the static Make factories), and the prefab / physics payloads replaced by an
// IAbilityPresenter — the battle sim still resolves every hit; abilities choreograph how it looks.
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ExcelHeroes.Ability
{
    // Branch: runs the child for the caster's current choice, then clears it.
    [CreateAssetMenu(menuName = "ExcelHeroes/Effect/BranchByChoice")]
    public class BranchByChoice : EffectData, IEffect
    {
        [SerializeField] private List<EffectData> _branches = new();

        public static BranchByChoice Make(params EffectData[] branches) { var e = CreateInstance<BranchByChoice>(); e._branches.AddRange(branches); return e; }

        public override IEffect CreateRuntime() => this;

        public async UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token)
        {
            if (ctx.CasterGO == null || !ctx.CasterGO.TryGetComponent<IChoiceHandler>(out var h)) return;
            int i = h.CurrentChoice;
            if (i < 0 || i >= _branches.Count || _branches[i] == null) return;
            await _branches[i].CreateRuntime().ExecuteAsync(ctx, token);
            h.ClearChoice();
        }
    }
}
