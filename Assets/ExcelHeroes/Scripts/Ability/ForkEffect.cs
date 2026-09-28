// Excel Heroes addition to the ability system ported from OperationKivotos-Code (vfly1189; see
// THIRD_PARTY_NOTICES.md). Not in the original.
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ExcelHeroes.Ability
{
    // Starts its children and returns at once. Patterns await each point's payload in turn, so a
    // payload that waits (AreaStrike's warning) would make a 24-point RadialBurst land one strike
    // at a time over ten seconds; wrapped in a Fork, the points keep the pattern's own rhythm.
    [CreateAssetMenu(menuName = "ExcelHeroes/Effect/Fork")]
    public class ForkEffect : EffectData, IEffect
    {
        [SerializeField] private List<EffectData> _children = new();

        public static ForkEffect Make(params EffectData[] children) { var e = CreateInstance<ForkEffect>(); e._children.AddRange(children); return e; }

        public override IEffect CreateRuntime() => this;

        public UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token)
        {
            foreach (var c in _children)
                if (c != null) c.CreateRuntime().ExecuteAsync(ctx, token).Forget();
            return UniTask.CompletedTask;
        }
    }
}
