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
    public interface IEffect
    {
        UniTask ExecuteAsync(AbilityContext ctx, CancellationToken token);
    }

    // Caster identity marker (AbilityContext.Caster).
    public interface IAbilityCaster { }

    // The boss's pick-one-of-N (relic colour): SelectRandomChoice sets it, BranchByChoice reads it.
    public interface IChoiceHandler
    {
        int ChoiceCount { get; }
        void ApplyChoice(int index);
        int CurrentChoice { get; }
        void ClearChoice();
    }

    public class AbilityContext
    {
        public IAbilityCaster Caster;   // who casts
        public GameObject CasterGO;     // the caster's root
        public Transform Object;        // the cast point (muzzle; here the tablet / sheet)
        public GameObject Target;       // the target, if any
        public Vector3 TargetPoint;     // aim / impact point in world space (AoE, strikes)
        public GameplayTagContainer Tags; // the caster's tags (gates, GrantsTags); null = no constraint
        public float CoolDown = -1f;

        // Excel Heroes: what the presenter needs to draw it
        public Color Accent = Color.white;
        public float Flight = 0.28f;    // seconds a projectile spends in the air
        public string Payload;          // the look of the projectile (cell, sum, trace, coffee …)

        public AbilityContext CloneAt(Vector3 point) => new AbilityContext
        {
            Caster = Caster, CasterGO = CasterGO, Object = Object, Target = Target,
            Tags = Tags, TargetPoint = point, CoolDown = CoolDown,
            Accent = Accent, Flight = Flight, Payload = Payload,
        };
    }

    // Current time source; the runner's cooldowns read only this.
    public interface IClock { float Now { get; } }

    public sealed class UnityClock : IClock
    {
        public static readonly UnityClock Instance = new UnityClock();
        public float Now => Time.time;
    }

    // One per caster: cooldown state, the tag gate, the cast.
    public class AbilityRunner
    {
        private readonly Dictionary<AbilityData, (float end, float dur)> _cd = new();
        private readonly IClock _clock;

        public AbilityRunner(IClock clock = null) => _clock = clock ?? UnityClock.Instance;

        public bool IsOnCooldown(AbilityData a) => _cd.TryGetValue(a, out var value) && _clock.Now < value.end;

        private void StartCooldown(AbilityData a, AbilityContext ctx)
        {
            float cooldown = ctx.CoolDown >= 0f ? ctx.CoolDown : a.Cooldown;
            _cd[a] = (_clock.Now + cooldown, cooldown);
        }

        public float CooldownRemaining(AbilityData a)
        {
            _cd.TryGetValue(a, out var value);
            return Mathf.Max(0f, value.end - _clock.Now);
        }

        public float CooldownDuration(AbilityData a) => _cd.TryGetValue(a, out var c) ? c.dur : 0f;

        // admission (gate + commit): starts the cooldown on success, runs no effects.
        // Cooldown, then tags (all Required, no Blocked); any failure → false with no side effects.
        public bool Commit(AbilityData ability, AbilityContext ctx)
        {
            if (ability == null || IsOnCooldown(ability)) return false;
            GameplayTagContainer owned = ctx.Tags;
            if (owned != null && (!owned.HasAll(ability.RequiredTags) || !owned.HasNone(ability.BlockedTags)))
                return false;
            StartCooldown(ability, ctx);
            return true;
        }

        // fire (ungated): cast time → the effects in order → GrantsTags released in try/finally.
        public async UniTask Fire(AbilityData ability, AbilityContext ctx, CancellationToken token)
        {
            if (ability == null) return;
            GameplayTagContainer owned = ctx.Tags;
            List<GameplayTagSO> granted = ability.GrantsTags;
            bool hasGrants = owned != null && granted != null && granted.Count > 0;
            if (hasGrants) for (int i = 0; i < granted.Count; i++) owned.Add(granted[i]);
            try
            {
                if (ability.CastTime > 0f)
                {
                    bool canceled = await UniTask.Delay(TimeSpan.FromSeconds(ability.CastTime), cancellationToken: token)
                        .SuppressCancellationThrow();
                    if (canceled) return;
                }
                foreach (var effect in ability.BuildRuntimeEffects())
                {
                    if (token.IsCancellationRequested) return;
                    await effect.ExecuteAsync(ctx, token);
                }
            }
            finally
            {
                if (hasGrants) for (int i = 0; i < granted.Count; i++) owned.Remove(granted[i]);
            }
        }

        // Commit + Fire in one moment (monsters, bosses).
        public async UniTask TryCast(AbilityData ability, AbilityContext ctx, CancellationToken token)
        {
            if (!Commit(ability, ctx)) return;
            await Fire(ability, ctx, token);
        }
    }

    // Counted multiset of the tags a caster holds (the same tag from two sources = held until both lift).
    public class GameplayTagContainer
    {
        private readonly Dictionary<GameplayTagSO, int> _counts = new();

        public void Add(GameplayTagSO tag)
        {
            if (tag == null) return;
            _counts.TryGetValue(tag, out int c);
            _counts[tag] = c + 1;
        }

        public void Remove(GameplayTagSO tag)
        {
            if (tag == null || !_counts.TryGetValue(tag, out int c)) return;
            if (c <= 1) _counts.Remove(tag);
            else _counts[tag] = c - 1;
        }

        public bool Has(GameplayTagSO tag) => tag != null && _counts.ContainsKey(tag);

        public bool HasAll(IReadOnlyList<GameplayTagSO> tags)
        {
            if (tags == null) return true;
            for (int i = 0; i < tags.Count; i++) if (tags[i] != null && !Has(tags[i])) return false;
            return true;
        }

        public bool HasNone(IReadOnlyList<GameplayTagSO> tags)
        {
            if (tags == null) return true;
            for (int i = 0; i < tags.Count; i++) if (tags[i] != null && Has(tags[i])) return false;
            return true;
        }

        public void Clear() => _counts.Clear();
    }

    // Where a positioned effect happens: one type, so no effect duplicates anchor/offset logic.
    [Serializable]
    public class Anchor
    {
        public enum Source { Muzzle, CasterRoot, TargetPoint, Target, CasterForward }

        [SerializeField] private Source _source = Source.TargetPoint;
        [SerializeField] private float _forwardDistance = 0f;
        [SerializeField] private Vector3 _localOffset;
        [SerializeField] private Vector3 _localEuler;
        [SerializeField] private bool _useCasterRotation = false;

        public static Anchor At(Source source, Vector3 offset = default, float forward = 0f) =>
            new Anchor { _source = source, _localOffset = offset, _forwardDistance = forward };

        public Transform Resolve(AbilityContext ctx, out Vector3 pos, out Quaternion rot)
        {
            Transform t = null;
            Vector3 basePos; Quaternion baseRot;
            switch (_source)
            {
                case Source.CasterRoot:
                    t = ctx.CasterGO.transform; basePos = t.position; baseRot = t.rotation; break;
                case Source.CasterForward:
                    t = ctx.CasterGO.transform; basePos = t.position + t.forward * _forwardDistance; baseRot = t.rotation; break;
                case Source.Target:   // Excel Heroes: the target's root (the original resolves named anchors here)
                    t = ctx.Target != null ? ctx.Target.transform : null;
                    basePos = t != null ? t.position : ctx.TargetPoint; baseRot = Quaternion.identity; break;
                case Source.TargetPoint:
                    basePos = ctx.TargetPoint; baseRot = Quaternion.identity; break;
                default:
                    t = ctx.Object != null ? ctx.Object : ctx.CasterGO.transform; basePos = t.position; baseRot = t.rotation; break;
            }
            if (_useCasterRotation && ctx.CasterGO != null) baseRot = ctx.CasterGO.transform.rotation;
            rot = baseRot * Quaternion.Euler(_localEuler);
            pos = basePos + rot * _localOffset;
            return t;
        }
    }

    /// <summary>
    /// Excel Heroes: what the payload effects draw through. BattleWorld implements it; with no
    /// presenter set, payloads do nothing (the sim is unaffected either way).
    /// </summary>
    public interface IAbilityPresenter
    {
        void Projectile(AbilityContext ctx, Vector3 from, Vector3 to, float flight, float lateral);
        void Warn(Vector3 at, float radius, float seconds, bool danger);
        void Blast(Vector3 at, float radius, Color color);
        void Vfx(string name, Vector3 at, AbilityContext ctx);
    }

    public static class AbilityHost
    {
        public static IAbilityPresenter Presenter;
    }
}
