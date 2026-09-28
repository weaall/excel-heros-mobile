// Ported from OperationKivotos-Code by vfly1189 — https://github.com/vfly1189/OperationKivotos-Code
// Used with the author's permission (given to the project owner by phone, 2026-09-28; see
// THIRD_PARTY_NOTICES.md). Adapted for Excel Heroes: namespace ExcelHeroes.Ability, code-built
// effect assets (the static Make factories), and the prefab / physics payloads replaced by an
// IAbilityPresenter — the battle sim still resolves every hit; abilities choreograph how it looks.
using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.Ability
{
    [CreateAssetMenu(fileName = "AbilityDataSO", menuName = "ExcelHeroes/Ability/AbilityData")]
    public class AbilityData : ScriptableObject
    {
        [Header("Cost / Timing")]
        public float Cooldown = 1f;
        public int Cost = 0;
        public float CastTime = 0f;

        [Header("Tags (gating)")]
        public List<GameplayTagSO> RequiredTags = new();
        public List<GameplayTagSO> BlockedTags = new();
        public List<GameplayTagSO> GrantsTags = new();

        [Header("Effects (run in order)")]
        [SerializeField] private List<EffectData> _effects = new();

        public static AbilityData Make(string name, float cooldown, params EffectData[] effects)
        {
            var a = CreateInstance<AbilityData>();
            a.name = name; a.Cooldown = cooldown; a._effects.AddRange(effects);
            return a;
        }

        public List<IEffect> BuildRuntimeEffects()
        {
            var list = new List<IEffect>(_effects.Count);
            foreach (var data in _effects) if (data != null) list.Add(data.CreateRuntime());
            return list;
        }
    }
}
