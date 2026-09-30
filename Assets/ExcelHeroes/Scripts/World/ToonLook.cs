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
    }
}
