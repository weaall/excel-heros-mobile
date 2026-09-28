// Ported from OperationKivotos-Code by vfly1189 — https://github.com/vfly1189/OperationKivotos-Code
// Used with the author's permission (given to the project owner by phone, 2026-09-28; see
// THIRD_PARTY_NOTICES.md). Adapted for Excel Heroes: namespace ExcelHeroes.Ability, code-built
// effect assets (the static Make factories), and the prefab / physics payloads replaced by an
// IAbilityPresenter — the battle sim still resolves every hit; abilities choreograph how it looks.
using UnityEngine;

namespace ExcelHeroes.Ability
{
    // A gameplay tag is an asset: compared by reference, so no string typos.
    [CreateAssetMenu(fileName = "Tag_", menuName = "ExcelHeroes/Ability/GameplayTag")]
    public class GameplayTagSO : ScriptableObject
    {
        public GameplayTagSO parent;   // hierarchy, unused for now

        public static GameplayTagSO Make(string name) { var t = CreateInstance<GameplayTagSO>(); t.name = name; return t; }

        public override string ToString() => name;
    }
}
