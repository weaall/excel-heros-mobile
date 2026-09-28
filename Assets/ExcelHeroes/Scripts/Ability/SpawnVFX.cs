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
    // A one-shot effect at an anchor (muzzle flash, hit spark, cast aura). Does not wait.
    [CreateAssetMenu(menuName = "ExcelHeroes/Effect/SpawnVFX")]
    public class SpawnVFX : EffectData, IEffect
    {
        [SerializeField] private string _vfx;
        [SerializeField] private Anchor _placement = new();

        public static SpawnVFX Make(string vfx, Anchor placement) { var e = CreateInstance<SpawnVFX>(); e._vfx = vfx; e._placement = placement; return e; }

        public override IEffect CreateRuntime() => this;

        public UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token)
        {
            if (string.IsNullOrEmpty(_vfx)) return UniTask.CompletedTask;
            _placement.Resolve(ctx, out Vector3 pos, out _);
            AbilityHost.Presenter?.Vfx(_vfx, pos, ctx);
            return UniTask.CompletedTask;
        }
    }
}
