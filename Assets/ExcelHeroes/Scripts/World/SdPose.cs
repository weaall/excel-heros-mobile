using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The pose library for the sample rig (SdRef). The FBX rests in an A-pose, which is only a
    /// neutral binding pose — nobody stands like that — so every state is a real pose built on
    /// the measured joint axes (SD_POSETEST): right shoulder raise = −Y, forward swing = +Z, the
    /// left mirrored; elbows and knees flex about +Z on both sides; the elbow's Y folds the
    /// forearm inward (hands to the belly/hips). Arms hang at raise ≈ −36.
    ///
    /// Characters get their own idle and victory variants (a stable hash of the id) and an
    /// attack by role, so a squad never stands or celebrates in unison.
    /// </summary>
    public struct Pose
    {
        public float RaiseL, RaiseR, SwingL, SwingR, ElbowL, ElbowR, InL, InR, KneeL, KneeR, ThighL, ThighR;
        public float Lean, Twist, HeadPitch, HeadRoll, Y, Yaw;
        public string Expr;

        /// <summary>Arms hanging, everything else neutral.</summary>
        public static Pose Rest => new() { RaiseL = -36f, RaiseR = -36f, SwingL = 3f, SwingR = 3f, ElbowL = 14f, ElbowR = 14f, Expr = "" };
    }

    public static class SdPose
    {
        public const int IdleCount = 6, WinCount = 6;

        public static int Hash(string id)
        {
            var h = 17; foreach (var c in id ?? "") h = h * 31 + c;
            return Mathf.Abs(h);
        }

        public static int IdleOf(string id) => Hash(id) % IdleCount;
        public static int WinOf(string id) => (Hash(id) / 7) % WinCount;
        public static int AttackOf(string role) => role switch { "ranged" => 1, "healer" => 2, _ => 0 };

        // ------------------------------------------------------------------ idle --

        /// <summary>
        /// 0 stand (weight on one leg, sway) · 1 hands on hips · 2 arms crossed · 3 hand at the chin ·
        /// 4 a relaxed lean, weight back · 5 one hand up at the shoulder, the other on the hip.
        /// </summary>
        public static Pose Idle(int variant, float t, float phase)
        {
            var p = Pose.Rest;
            var br = Mathf.Sin(t * 2.4f + phase);              // breathing
            var sway = Mathf.Sin(t * 0.8f + phase);
            p.Lean = br * 1.2f; p.HeadPitch = br * 1.5f;
            switch (variant % IdleCount)
            {
                case 0:
                    p.RaiseL += br * 2f; p.RaiseR -= br * 2f; p.Twist = sway * 3f; p.HeadRoll = sway * 3f;
                    p.KneeL = 6f; break;
                case 1:
                    // hands on the hips: upper arms a little back, forearms folded in to the sides of the waist
                    p.RaiseL = p.RaiseR = -22f; p.SwingL = p.SwingR = -12f; p.ElbowL = p.ElbowR = 22f; p.InL = p.InR = 52f;
                    p.Twist = sway * 2f; p.HeadRoll = -4f; break;
                case 2:
                    p.RaiseL = p.RaiseR = -22f; p.SwingL = p.SwingR = 30f; p.ElbowL = p.ElbowR = 50f; p.InL = p.InR = 95f;
                    p.HeadPitch += 3f; p.HeadRoll = sway * 2f; break;
                case 3:
                    p.RaiseR = -8f; p.SwingR = 42f; p.ElbowR = 125f; p.InR = 35f;
                    p.RaiseL = -20f; p.SwingL = 12f; p.ElbowL = 40f; p.InL = 80f;
                    p.HeadRoll = 7f; p.HeadPitch += 2f; p.Twist = 4f; break;
                case 4:
                    // relaxed lean: weight back on one leg, arms hanging a little behind, shoulders turned
                    p.RaiseL = p.RaiseR = -38f; p.SwingL = p.SwingR = -14f; p.ElbowL = p.ElbowR = 10f;
                    p.KneeR = 16f; p.Twist = 8f + sway * 2f; p.Lean -= 3f; p.HeadRoll = -5f + sway * 2f; break;
                case 5:
                    p.RaiseR = 6f; p.SwingR = 22f; p.ElbowR = 95f + br * 3f; p.InR = 10f;
                    p.RaiseL = -14f; p.SwingL = 8f; p.ElbowL = 30f; p.InL = 78f;
                    p.HeadRoll = -5f; p.Twist = -3f; break;
            }
            return p;
        }

        // ------------------------------------------------------------------ win --

        /// <summary>
        /// 0 both arms up, hopping · 1 fist pump · 2 V-sign by the cheek · 3 a bow · 4 a wave ·
        /// 5 a spin with arms out. <paramref name="t"/> is time since the win.
        /// </summary>
        public static Pose Victory(int variant, float t)
        {
            var p = Pose.Rest; p.Expr = "happy";
            var hop = Mathf.Abs(Mathf.Sin(t * 7f));
            switch (variant % WinCount)
            {
                case 0:
                    p.RaiseL = p.RaiseR = 100f; p.SwingL = p.SwingR = -40f; p.ElbowL = p.ElbowR = 10f;
                    p.Y = hop * 0.14f; p.KneeL = p.KneeR = hop * 30f; p.HeadPitch = -6f; break;
                case 1:
                    var pump = Mathf.Abs(Mathf.Sin(t * 9f));
                    p.RaiseR = 70f + pump * 25f; p.SwingR = -20f; p.ElbowR = 70f - pump * 40f;
                    p.RaiseL = -14f; p.SwingL = 8f; p.ElbowL = 30f; p.InL = 78f;
                    p.Y = hop * 0.08f; p.Lean = -4f; p.Twist = -8f; break;
                case 2:
                    p.RaiseR = 25f; p.SwingR = 55f; p.ElbowR = 110f; p.InR = 25f;
                    p.RaiseL = -30f; p.SwingL = 5f; p.ElbowL = 20f;
                    p.HeadRoll = 10f; p.Twist = 6f; p.Y = hop * 0.04f; break;
                case 3:
                    var bow = Mathf.Clamp01(Mathf.Sin(Mathf.Min(t * 2.2f, Mathf.PI)));
                    p.Lean = 32f * bow; p.HeadPitch = 10f * bow;
                    p.RaiseL = p.RaiseR = -34f; p.SwingL = p.SwingR = 6f; p.ElbowL = p.ElbowR = 12f; break;
                case 4:
                    p.RaiseR = 100f; p.SwingR = -40f + Mathf.Sin(t * 12f) * 12f; p.ElbowR = 8f + Mathf.Abs(Mathf.Sin(t * 12f)) * 15f;
                    p.RaiseL = -14f; p.SwingL = 8f; p.ElbowL = 30f; p.InL = 78f;
                    p.Y = hop * 0.1f; p.KneeL = p.KneeR = hop * 25f; break;
                case 5:
                    p.RaiseL = p.RaiseR = 12f; p.SwingL = p.SwingR = 18f; p.ElbowL = p.ElbowR = 30f;
                    p.Yaw = Mathf.Min(t * 240f, 720f); p.Y = Mathf.Sin(Mathf.Min(t * 3.5f, Mathf.PI)) * 0.25f; break;
            }
            return p;
        }

        // ------------------------------------------------------------------ fight --

        /// <summary>0 melee punch · 1 ranged shot (arm thrown out, recoil) · 2 caster (both hands raised, thrust). a = 0..1.</summary>
        public static Pose Attack(int kind, float a)
        {
            var p = Pose.Rest; p.Expr = "angry";
            switch (kind)
            {
                default:
                    if (a < 0.3f) { var k = a / 0.3f; p.SwingR = Mathf.Lerp(3f, -45f, k); p.ElbowR = Mathf.Lerp(14f, 95f, k); p.RaiseR = Mathf.Lerp(-36f, -10f, k); p.Twist = -12f * k; p.Lean = 5f * k; }
                    else if (a < 0.6f) { var k = (a - 0.3f) / 0.3f; p.SwingR = Mathf.Lerp(-45f, 100f, k); p.ElbowR = Mathf.Lerp(95f, 5f, k); p.RaiseR = -5f; p.KneeL = 18f; p.Twist = Mathf.Lerp(-12f, 16f, k); p.Lean = -10f * k; }
                    else { var k = (a - 0.6f) / 0.4f; p.SwingR = Mathf.Lerp(100f, 3f, k); p.ElbowR = Mathf.Lerp(5f, 14f, k); p.RaiseR = Mathf.Lerp(-5f, -36f, k); p.KneeL = 18f * (1f - k); p.Twist = 16f * (1f - k); p.Lean = -10f * (1f - k); }
                    p.RaiseL = -20f; p.SwingL = 30f; p.ElbowL = 70f; p.InL = 40f;          // the guard hand
                    break;
                case 1:
                    {
                        // arm out straight at the target, a recoil kick at the shot, then lowered
                        var up = a < 0.25f ? a / 0.25f : a < 0.75f ? 1f : 1f - (a - 0.75f) / 0.25f;
                        var kick = a > 0.3f && a < 0.5f ? Mathf.Sin((a - 0.3f) / 0.2f * Mathf.PI) : 0f;
                        p.RaiseR = Mathf.Lerp(-36f, -2f, up); p.SwingR = Mathf.Lerp(3f, 92f, up) - kick * 18f; p.ElbowR = Mathf.Lerp(14f, 4f, up) + kick * 30f;
                        p.RaiseL = -25f; p.SwingL = 20f; p.ElbowL = 60f; p.InL = 50f;
                        p.Lean = -6f * up + kick * 6f; p.Twist = 14f * up; p.HeadRoll = 3f * up;
                        break;
                    }
                case 2:
                    {
                        // both hands raised in front, then thrust forward with a small hop
                        var up = a < 0.4f ? Mathf.SmoothStep(0f, 1f, a / 0.4f) : 1f;
                        var thrust = a > 0.4f ? Mathf.Sin(Mathf.Clamp01((a - 0.4f) / 0.6f) * Mathf.PI) : 0f;
                        p.RaiseL = p.RaiseR = Mathf.Lerp(-36f, 10f, up); p.SwingL = p.SwingR = Mathf.Lerp(3f, 45f, up) + thrust * 40f;
                        p.ElbowL = p.ElbowR = Mathf.Lerp(14f, 70f, up) - thrust * 50f; p.InL = p.InR = 20f * up;
                        p.Y = thrust * 0.08f; p.Lean = -8f * thrust; p.HeadPitch = -4f * up;
                        break;
                    }
            }
            return p;
        }

        public static Pose Walk(float ph)
        {
            var p = Pose.Rest;
            var sw = Mathf.Sin(ph);
            p.KneeL = Mathf.Max(0f, sw) * 45f; p.KneeR = Mathf.Max(0f, -sw) * 45f;
            p.ThighL = sw * 30f; p.ThighR = sw * 30f;
            p.SwingL = -sw * 28f; p.SwingR = sw * 28f; p.ElbowL = p.ElbowR = 35f;
            p.RaiseL = p.RaiseR = -30f; p.Lean = -6f; p.Y = Mathf.Abs(sw) * 0.06f;
            return p;
        }

        public static Pose Hit(float k)
        {
            var p = Pose.Rest; p.Expr = "hurt";
            p.RaiseL = p.RaiseR = -36f + 40f * k; p.SwingL = p.SwingR = -15f * k; p.ElbowL = p.ElbowR = 14f + 50f * k;
            p.Lean = 14f * k; p.HeadPitch = 6f * k;
            return p;
        }

        public static Pose Skill(float k)
        {
            var p = Pose.Rest; p.Expr = "angry";
            var up = Mathf.Sin(k * Mathf.PI);
            p.RaiseL = p.RaiseR = Mathf.Lerp(-36f, 100f, up); p.SwingL = p.SwingR = Mathf.Lerp(3f, -40f, up); p.ElbowL = p.ElbowR = 12f;
            p.KneeL = p.KneeR = up * 50f; p.Y = up * 0.45f; p.Yaw = k * 360f;
            return p;
        }

        public static Pose Dead(float k)
        {
            var p = Pose.Rest; p.Expr = "hurt";
            p.RaiseL = p.RaiseR = -20f; p.SwingL = p.SwingR = 25f; p.ElbowL = p.ElbowR = 50f;
            p.Lean = 80f * Mathf.SmoothStep(0f, 1f, k);
            return p;
        }

        /// <summary>Puts a pose on the sample rig (offsets on the rest rotations).</summary>
        public static void Apply(ChibiRig rig, in Pose p)
        {
            rig.Pose(rig.Body, Quaternion.Euler(0f, 0f, -p.Lean) * Quaternion.Euler(p.Twist, 0f, 0f));
            rig.Pose(rig.Head, Quaternion.Euler(0f, 0f, -p.HeadPitch) * Quaternion.Euler(p.HeadRoll, 0f, 0f));
            rig.Pose(rig.ArmR, Quaternion.Euler(0f, -p.RaiseR, p.SwingR));
            rig.Pose(rig.ArmL, Quaternion.Euler(0f, p.RaiseL, -p.SwingL));
            rig.Pose(rig.ForearmR, Quaternion.Euler(0f, p.InR, p.ElbowR));
            rig.Pose(rig.ForearmL, Quaternion.Euler(0f, -p.InL, p.ElbowL));
            rig.Pose(rig.LegL, Quaternion.Euler(0f, 0f, -p.ThighL));
            rig.Pose(rig.LegR, Quaternion.Euler(0f, 0f, p.ThighR));
            rig.Pose(rig.CalfL, Quaternion.Euler(0f, 0f, p.KneeL));
            rig.Pose(rig.CalfR, Quaternion.Euler(0f, 0f, p.KneeR));
        }
    }
}
