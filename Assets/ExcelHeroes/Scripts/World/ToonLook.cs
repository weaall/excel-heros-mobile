namespace ExcelHeroes.World
{
    /// <summary>
    /// The cast's toon look, shared by the street and the formation (Toon.shader globals): the outline's
    /// floor in screen pixels (1.2: A/B vs BA beat 1.8 on the street 5/6, formation 4/6, result 6/6; 0.8 lost the street 0/6) and the albedo lift (0; ±0.25 lost). Env overrides (EH_OUTLINE, EH_LIFT) for A/B captures.
    /// </summary>
    public static class ToonLook
    {
        static float Env(string k, float d) => float.TryParse(System.Environment.GetEnvironmentVariable(k), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
        public static readonly float OutlinePx = Env("EH_OUTLINE", 1.2f);
        public static readonly float Lift = Env("EH_LIFT", 0f);
        public static readonly float ShadeMul = Env("EH_SHADE", 1f), RimMul = Env("EH_RIM", 1f), SpecMul = Env("EH_SPEC", 1f);
        public static readonly float Bloom = Env("EH_BLOOM", 0.55f), Saturation = Env("EH_SAT", 22f), Contrast = Env("EH_CONTRAST", 10f);

        /// <summary>Sets the Toon.shader globals.</summary>
        public static void Apply()
        {
            UnityEngine.Shader.SetGlobalFloat("_EhOutlinePx", OutlinePx);
            UnityEngine.Shader.SetGlobalFloat("_EhLift", Lift);
            UnityEngine.Shader.SetGlobalFloat("_EhShadeD", ShadeMul - 1f);
            UnityEngine.Shader.SetGlobalFloat("_EhRimD", RimMul - 1f);
            UnityEngine.Shader.SetGlobalFloat("_EhSpecD", SpecMul - 1f);
        }
    }
}
