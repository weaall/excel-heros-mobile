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
        // wrists: Flex folds the hand toward the body (+Y on the right hand, measured), Dev bends it
        // sideways (+Z out); feet: Toe > 0 points the toes down (+Z right foot), < 0 lifts them
        public float HandFlexL, HandFlexR, HandDevL, HandDevR, ToeL, ToeR;
        // fingers: 0 open and spread, 0.25 relaxed curl (rest), 1 a fist — measured: the finger joints curl about -Y
        public float FistL, FistR;
        // weight: the whole figure shifted sideways (metres, +x = the figure's right) and a hip tilt
        public float Sway, HipRoll;
        public string Expr;

        /// <summary>Blend of two poses (the joint angles; the expression and the spin come from b).</summary>
        public static Pose Lerp(in Pose a, in Pose b, float t)
        {
            return new Pose
            {
                RaiseL = Mathf.Lerp(a.RaiseL, b.RaiseL, t), RaiseR = Mathf.Lerp(a.RaiseR, b.RaiseR, t),
                SwingL = Mathf.Lerp(a.SwingL, b.SwingL, t), SwingR = Mathf.Lerp(a.SwingR, b.SwingR, t),
                ElbowL = Mathf.Lerp(a.ElbowL, b.ElbowL, t), ElbowR = Mathf.Lerp(a.ElbowR, b.ElbowR, t),
                InL = Mathf.Lerp(a.InL, b.InL, t), InR = Mathf.Lerp(a.InR, b.InR, t),
                KneeL = Mathf.Lerp(a.KneeL, b.KneeL, t), KneeR = Mathf.Lerp(a.KneeR, b.KneeR, t),
                ThighL = Mathf.Lerp(a.ThighL, b.ThighL, t), ThighR = Mathf.Lerp(a.ThighR, b.ThighR, t),
                Lean = Mathf.Lerp(a.Lean, b.Lean, t), Twist = Mathf.Lerp(a.Twist, b.Twist, t),
                HeadPitch = Mathf.Lerp(a.HeadPitch, b.HeadPitch, t), HeadRoll = Mathf.Lerp(a.HeadRoll, b.HeadRoll, t),
                Y = Mathf.Lerp(a.Y, b.Y, t), Yaw = b.Yaw, Expr = b.Expr,
                HandFlexL = Mathf.Lerp(a.HandFlexL, b.HandFlexL, t), HandFlexR = Mathf.Lerp(a.HandFlexR, b.HandFlexR, t),
                HandDevL = Mathf.Lerp(a.HandDevL, b.HandDevL, t), HandDevR = Mathf.Lerp(a.HandDevR, b.HandDevR, t),
                ToeL = Mathf.Lerp(a.ToeL, b.ToeL, t), ToeR = Mathf.Lerp(a.ToeR, b.ToeR, t),
                Sway = Mathf.Lerp(a.Sway, b.Sway, t), HipRoll = Mathf.Lerp(a.HipRoll, b.HipRoll, t),
                FistL = Mathf.Lerp(a.FistL, b.FistL, t), FistR = Mathf.Lerp(a.FistR, b.FistR, t),
            };
        }

        /// <summary>Arms hanging, everything else neutral.</summary>
        public static Pose Rest => new() { RaiseL = -36f, RaiseR = -36f, SwingL = 3f, SwingR = 3f, ElbowL = 14f, ElbowR = 14f, HandFlexL = 18f, HandFlexR = 18f, FistL = 0.25f, FistR = 0.25f, Expr = "" };
    }

    public static class SdPose
    {
        public const int IdleCount = 6, WinCount = 6;

        public static int Hash(string id)
        {
            var h = 17; foreach (var c in id ?? "") h = h * 31 + c;
            return Mathf.Abs(h);
        }

        public static int IdleOf(string id) { var k = SdLook.For(id); return k.Idle >= 0 ? k.Idle % IdleCount : Hash(id) % IdleCount; }
        public static int WinOf(string id) { var k = SdLook.For(id); return k.Win >= 0 ? k.Win % WinCount : (Hash(id) / 7) % WinCount; }
        public static int AttackOf(string role) => role switch { "ranged" => 1, "healer" => 2, "caster" => 2, _ => 0 };
        /// <summary>The attack kind for a character: the spec's, else by role.</summary>
        public static int AttackOf(string id, string role) { var k = SdLook.For(id); return AttackOf(k.Attack is { Length: > 0 } ? k.Attack : role); }

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
                    // contrapposto: weight over the right leg, the left knee soft, hips out to the right,
                    // the shoulders tilted the other way, a slow sway of the whole weight
                    p.RaiseL += br * 2f; p.RaiseR -= br * 2f; p.Twist = sway * 3f; p.HeadRoll = -3f + sway * 2f;
                    p.KneeL = 12f; p.Sway = 0.014f + sway * 0.004f; p.HipRoll = 4f;
                    p.HandFlexL = 22f; p.HandFlexR = 16f; break;
                case 1:
                    // hands on the hips: upper arms a little back, forearms folded in to the sides of the waist,
                    // wrists bent so the palms sit on the hips; weight on the left leg
                    p.RaiseL = p.RaiseR = -22f; p.SwingL = p.SwingR = -12f; p.ElbowL = p.ElbowR = 22f; p.InL = p.InR = 52f;
                    p.HandFlexL = p.HandFlexR = 45f; p.HandDevL = p.HandDevR = -15f; p.FistL = p.FistR = 0.15f;
                    p.KneeR = 10f; p.Sway = -0.012f; p.HipRoll = -3f;
                    p.Twist = sway * 2f; p.HeadRoll = -4f; break;
                case 2:
                    p.RaiseL = p.RaiseR = -22f; p.SwingL = p.SwingR = 30f; p.ElbowL = p.ElbowR = 50f; p.InL = p.InR = 95f;
                    p.HandFlexL = p.HandFlexR = 30f; p.KneeL = 8f; p.Sway = 0.01f; p.HipRoll = 3f;
                    p.HeadPitch += 3f; p.HeadRoll = sway * 2f; break;
                case 3:
                    p.RaiseR = -8f; p.SwingR = 42f; p.ElbowR = 125f; p.InR = 35f; p.HandFlexR = 40f;
                    p.RaiseL = -20f; p.SwingL = 12f; p.ElbowL = 40f; p.InL = 80f; p.HandFlexL = 30f;
                    p.KneeR = 10f; p.Sway = -0.012f; p.HipRoll = -3f;
                    p.HeadRoll = 7f; p.HeadPitch += 2f; p.Twist = 4f; break;
                case 4:
                    // relaxed lean: weight back on one leg, arms hanging a little behind, shoulders turned
                    p.RaiseL = p.RaiseR = -38f; p.SwingL = p.SwingR = -14f; p.ElbowL = p.ElbowR = 10f;
                    p.HandFlexL = p.HandFlexR = 12f;
                    p.KneeR = 16f; p.Sway = 0.016f; p.HipRoll = 5f; p.Twist = 8f + sway * 2f; p.Lean -= 3f; p.HeadRoll = -5f + sway * 2f; break;
                case 5:
                    p.RaiseR = 6f; p.SwingR = 22f; p.ElbowR = 95f + br * 3f; p.InR = 10f; p.HandFlexR = -10f; p.HandDevR = 20f;
                    p.RaiseL = -22f; p.SwingL = -12f; p.ElbowL = 22f; p.InL = 52f; p.HandFlexL = 45f; p.HandDevL = -15f;
                    p.KneeL = 10f; p.Sway = 0.012f; p.HipRoll = 3f;
                    p.HeadRoll = -5f; p.Twist = -3f; break;
            }
            return p;
        }

        // ------------------------------------------------------------------ ready --

        /// <summary>
        /// The combat stance, by attack kind: 0 melee — feet apart, fists up, bouncing on the
        /// toes; 1 ranged — the tablet raised in both hands at chest height, sighting; 2 healer —
        /// the cup held up, the other hand ready. Held while waiting between actions in a fight.
        /// </summary>
        public static Pose Ready(int kind, float t, float phase)
        {
            var p = Pose.Rest;
            var br = Mathf.Sin(t * 3f + phase);
            switch (kind)
            {
                default:
                    p.RaiseL = -12f; p.SwingL = 40f; p.ElbowL = 95f; p.InL = 30f; p.FistL = 1f; p.HandFlexL = 45f;
                    p.RaiseR = -8f; p.SwingR = 30f; p.ElbowR = 105f; p.InR = 20f; p.FistR = 1f; p.HandFlexR = 45f;
                    p.KneeL = 14f; p.KneeR = 14f; p.Lean = -6f; p.Twist = 10f; p.Y = Mathf.Abs(br) * 0.015f;
                    p.HeadPitch = 2f; p.Sway = 0.008f;
                    break;
                case 1:
                    p.RaiseR = -6f; p.SwingR = 62f; p.ElbowR = 55f; p.InR = 35f; p.HandFlexR = 10f; p.FistR = 0.6f;
                    p.RaiseL = -10f; p.SwingL = 55f; p.ElbowL = 70f; p.InL = 45f; p.HandFlexL = 20f; p.FistL = 0.6f;
                    p.KneeL = 8f; p.Lean = -4f; p.Twist = 12f + br * 1f; p.HeadPitch = 3f; p.Sway = 0.01f;
                    break;
                case 2:
                    p.RaiseR = -14f; p.SwingR = 45f; p.ElbowR = 100f; p.InR = 15f; p.HandFlexR = 0f; p.FistR = 0.7f;
                    p.RaiseL = -30f; p.SwingL = 10f; p.ElbowL = 35f; p.InL = 60f; p.HandFlexL = 30f; p.FistL = 0.3f;
                    p.KneeR = 8f; p.Sway = -0.01f; p.HipRoll = -3f; p.HeadRoll = 4f + br * 1.5f;
                    break;
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
                    p.HandFlexL = p.HandFlexR = -20f; p.HandDevL = p.HandDevR = 25f; p.FistL = p.FistR = 0f;   // palms open, fingers spread up
                    p.Y = hop * 0.14f; p.KneeL = p.KneeR = hop * 30f; p.ToeL = p.ToeR = hop * 20f; p.HeadPitch = -6f; break;
                case 1:
                    var pump = Mathf.Abs(Mathf.Sin(t * 9f));
                    p.RaiseR = 70f + pump * 25f; p.SwingR = -20f; p.ElbowR = 70f - pump * 40f; p.HandFlexR = 50f; p.FistR = 1f;   // a fist
                    p.RaiseL = -22f; p.SwingL = -12f; p.ElbowL = 22f; p.InL = 52f; p.HandFlexL = 45f; p.HandDevL = -15f;
                    p.KneeL = 8f; p.Sway = 0.01f; p.HipRoll = 3f;
                    p.Y = hop * 0.08f; p.Lean = -4f; p.Twist = -8f; break;
                case 2:
                    // hand by the cheek, head tilted onto it, weight on the near leg
                    p.RaiseR = 25f; p.SwingR = 55f; p.ElbowR = 110f; p.InR = 25f; p.HandFlexR = 35f; p.HandDevR = 10f;
                    p.RaiseL = -30f; p.SwingL = 5f; p.ElbowL = 20f; p.HandFlexL = 20f;
                    p.KneeL = 10f; p.Sway = 0.012f; p.HipRoll = 3f;
                    p.HeadRoll = 10f; p.Twist = 6f; p.Y = hop * 0.04f; break;
                case 3:
                    var bow = Mathf.Clamp01(Mathf.Sin(Mathf.Min(t * 2.2f, Mathf.PI)));
                    p.Lean = 32f * bow; p.HeadPitch = 10f * bow;
                    p.RaiseL = p.RaiseR = -34f; p.SwingL = p.SwingR = 6f; p.ElbowL = p.ElbowR = 12f; break;
                case 4:
                    p.RaiseR = 100f; p.SwingR = -40f + Mathf.Sin(t * 12f) * 12f; p.ElbowR = 8f + Mathf.Abs(Mathf.Sin(t * 12f)) * 15f;
                    p.HandFlexR = -15f; p.HandDevR = Mathf.Sin(t * 12f) * 25f; p.FistR = 0f;   // the hand itself waves, open
                    p.RaiseL = -22f; p.SwingL = -12f; p.ElbowL = 22f; p.InL = 52f; p.HandFlexL = 45f; p.HandDevL = -15f;
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
                    p.RaiseL = -20f; p.SwingL = 30f; p.ElbowL = 70f; p.InL = 40f; p.HandFlexL = 50f;   // the guard hand, a fist
                    p.HandFlexR = 50f; p.FistR = 1f; p.FistL = 1f;                          // the punching fist, the guard fist
                    p.Sway = a < 0.3f ? -0.02f * (a / 0.3f) : a < 0.6f ? Mathf.Lerp(-0.02f, 0.03f, (a - 0.3f) / 0.3f) : Mathf.Lerp(0.03f, 0f, (a - 0.6f) / 0.4f);
                    p.HipRoll = a < 0.6f ? -4f : 0f;
                    break;
                case 1:
                    {
                        // arm out straight at the target, a recoil kick at the shot, then lowered
                        var up = a < 0.25f ? a / 0.25f : a < 0.75f ? 1f : 1f - (a - 0.75f) / 0.25f;
                        var kick = a > 0.3f && a < 0.5f ? Mathf.Sin((a - 0.3f) / 0.2f * Mathf.PI) : 0f;
                        p.RaiseR = Mathf.Lerp(-36f, -2f, up); p.SwingR = Mathf.Lerp(3f, 92f, up) - kick * 18f; p.ElbowR = Mathf.Lerp(14f, 4f, up) + kick * 30f;
                        p.RaiseL = -25f; p.SwingL = 20f; p.ElbowL = 60f; p.InL = 50f;
                        p.Lean = -6f * up + kick * 6f; p.Twist = 14f * up; p.HeadRoll = 3f * up;
                        p.HandFlexR = 10f; p.HandFlexL = 40f; p.FistR = 0.7f; p.FistL = 1f; p.Sway = 0.012f * up - kick * 0.02f; p.KneeL = 10f * up;
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
                        p.HandFlexL = p.HandFlexR = -25f * up; p.HandDevL = p.HandDevR = 15f * up; p.FistL = p.FistR = 0f;   // palms pushed forward, open
                        break;
                    }
            }
            return p;
        }

        /// <summary>
        /// A walk with weight in it. ph runs one cycle per 2π: the LEFT thigh is forward at π/2,
        /// back at 3π/2. The swinging leg bends at the knee as it passes and its toes point down
        /// at toe-off; the stance leg stays straight; the arms swing against the legs with the
        /// elbow bending on the forward swing; the body rises at mid-stance and drops at each
        /// contact, shifts over the stance leg and rocks at the hips; the head counter-bobs.
        /// </summary>
        public static Pose Walk(float ph)
        {
            var p = Pose.Rest;
            var sw = Mathf.Sin(ph);                             // +1 left thigh forward
            var pass = Mathf.Cos(ph);                           // +1 left leg swinging forward through the pass
            p.ThighL = sw * 28f; p.ThighR = -sw * 28f;
            p.KneeL = Mathf.Max(0f, pass) * 55f + 4f; p.KneeR = Mathf.Max(0f, -pass) * 55f + 4f;
            p.ToeL = Mathf.Max(0f, -sw) * 28f - Mathf.Max(0f, sw) * 8f;     // down at toe-off, up a little at heel strike
            p.ToeR = Mathf.Max(0f, sw) * 28f - Mathf.Max(0f, -sw) * 8f;
            p.SwingL = -sw * 16f; p.SwingR = sw * 16f;
            p.ElbowL = 20f + Mathf.Max(0f, -sw) * 18f; p.ElbowR = 20f + Mathf.Max(0f, sw) * 18f;
            p.RaiseL = p.RaiseR = -34f;
            p.HandFlexL = p.HandFlexR = 22f;
            p.Lean = -3f;
            p.Y = 0.012f + Mathf.Abs(pass) * 0.03f;             // up at mid-stance, down at double support
            p.Sway = -pass * 0.012f;                            // over the stance leg (the right when the left swings)
            p.HipRoll = pass * 4f; p.Twist = sw * 5f;           // pelvis with the legs, shoulders against
            p.HeadPitch = -Mathf.Abs(pass) * 2f + 2f;
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

        /// <summary>Curls the three fingers: 0 spread open (+12), 1 a fist (-75 per joint); the thumb folds half as far.</summary>
        static void Fingers(Transform[] f, float fist)
        {
            if (f == null) return;
            for (var i = 0; i < f.Length; i++)
            {
                if (f[i] == null) continue;
                var thumb = i < 2;
                var deg = Mathf.Lerp(12f, -75f, fist) * (thumb ? 0.5f : 1f);
                f[i].localRotation = Quaternion.Euler(0f, deg, 0f) * (RestOf(f[i]));
            }
        }
        static readonly System.Collections.Generic.Dictionary<Transform, Quaternion> FingerRest = new();
        static Quaternion RestOf(Transform t) { if (!FingerRest.TryGetValue(t, out var q)) FingerRest[t] = q = t.localRotation; return q; }

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
            if (rig.HandR != null) rig.Pose(rig.HandR, Quaternion.Euler(0f, p.HandFlexR, p.HandDevR));
            if (rig.HandL != null) rig.Pose(rig.HandL, Quaternion.Euler(0f, p.HandFlexL, p.HandDevL));   // measured: the hands are NOT mirrored
            Fingers(rig.FingersR, p.FistR); Fingers(rig.FingersL, p.FistL);
            if (rig.FootR != null) rig.Pose(rig.FootR, Quaternion.Euler(0f, 0f, p.ToeR));
            if (rig.FootL != null) rig.Pose(rig.FootL, Quaternion.Euler(0f, 0f, p.ToeL));
            // weight: the figure over its stance leg, and the hips rocking (roll about the pelvis's forward)
            if (rig.Pelvis != null)
            {
                var worldOff = rig.Root.rotation * new Vector3(p.Sway, 0f, 0f);
                rig.Pelvis.localPosition = rig.PelvisRest + rig.Pelvis.parent.InverseTransformVector(worldOff);
                if (p.HipRoll != 0f) rig.Pose(rig.Body, Quaternion.Euler(0f, 0f, -p.Lean) * Quaternion.Euler(p.Twist, p.HipRoll, 0f));
            }
        }
    }
}
