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
    // Waits mid-chain (CastTime only waits before everything): charge → Delay(1) → fire.
    [CreateAssetMenu(menuName = "ExcelHeroes/Effect/Delay")]
    public class DelayEffect : EffectData, IEffect
    {
        [SerializeField] private float _seconds = 1f;

        public static DelayEffect Make(float seconds) { var e = CreateInstance<DelayEffect>(); e._seconds = seconds; return e; }

        public override IEffect CreateRuntime() => this;

        public async UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token)
        {
            if (_seconds <= 0f) return;
            await UniTask.Delay(TimeSpan.FromSeconds(_seconds), cancellationToken: token).SuppressCancellationThrow();
        }
    }
}
