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
        // Lean: the whole trunk forward (+) — split 40 % hips / 60 % waist; Twist: hips yaw;
        // SpineBend/SpineTwist/SpineSide: the waist on its own (measured: Spine1 +Z forward, +X yaw, +Y side);
        // HeadPitch + = chin down (Head +Z), HeadYaw (Head +X), HeadTilt (Head +Y); HeadRoll = HeadTilt (old name)
        public float Lean, Twist, SpineBend, SpineTwist, SpineSide, HeadPitch, HeadYaw, HeadTilt, Y, Yaw;
        public float HeadRoll { get => HeadTilt; set => HeadTilt = value; }
        // clavicles: Shrug + = the shoulder up (R Clavicle +Z), Reach + = forward (R Clavicle −Y); the left mirrors
        public float ShrugL, ShrugR, ReachL, ReachR;
        // root: a step along the facing (metres), for lunges
        public float Step;
        // wrists: Flex folds the hand toward the body (+Y on the right hand, measured), Dev bends it
        // sideways (+Z out); feet: Toe > 0 points the toes down (+Z right foot), < 0 lifts them
        public float HandFlexL, HandFlexR, HandDevL, HandDevR, ToeL, ToeR;
        // fingers: 0 open and spread, 0.25 relaxed curl (rest), 1 a fist — measured: the finger joints curl about -Y
        public float FistL, FistR;
        // weight: the whole figure shifted sideways (metres, +x = the figure's right) and a hip tilt
        public float Sway, HipRoll;
        // squash & stretch: the whole figure scaled, volume kept — + stretches tall and thin, − squashes
        // short and wide (a crouch before a jump, a landing, a hit); feet stay on the floor
        public float Squash;
        // Free: off the ground on purpose (lying down, spinning) — SdPose.Apply does not plant the feet
        public bool Free;
        public string Expr;

        /// <summary>Every numeric field in a fixed order, for blends and springs.</summary>
        public const int Count = 40;
        public void ToArray(float[] o)
        {
            o[0] = RaiseL; o[1] = RaiseR; o[2] = SwingL; o[3] = SwingR; o[4] = ElbowL; o[5] = ElbowR; o[6] = InL; o[7] = InR;
            o[8] = KneeL; o[9] = KneeR; o[10] = ThighL; o[11] = ThighR; o[12] = Lean; o[13] = Twist; o[14] = SpineBend; o[15] = SpineTwist;
            o[16] = SpineSide; o[17] = HeadPitch; o[18] = HeadYaw; o[19] = HeadTilt; o[20] = Y; o[21] = HandFlexL; o[22] = HandFlexR;
            o[23] = HandDevL; o[24] = HandDevR; o[25] = ToeL; o[26] = ToeR; o[27] = FistL; o[28] = FistR; o[29] = Sway; o[30] = HipRoll;
            o[31] = ShrugL; o[32] = ShrugR; o[33] = ReachL; o[34] = ReachR; o[35] = Step; o[36] = Squash; o[37] = 0f; o[38] = 0f; o[39] = 0f;
        }
        public void FromArray(float[] o)
        {
            RaiseL = o[0]; RaiseR = o[1]; SwingL = o[2]; SwingR = o[3]; ElbowL = o[4]; ElbowR = o[5]; InL = o[6]; InR = o[7];
            KneeL = o[8]; KneeR = o[9]; ThighL = o[10]; ThighR = o[11]; Lean = o[12]; Twist = o[13]; SpineBend = o[14]; SpineTwist = o[15];
            SpineSide = o[16]; HeadPitch = o[17]; HeadYaw = o[18]; HeadTilt = o[19]; Y = o[20]; HandFlexL = o[21]; HandFlexR = o[22];
            HandDevL = o[23]; HandDevR = o[24]; ToeL = o[25]; ToeR = o[26]; FistL = o[27]; FistR = o[28]; Sway = o[29]; HipRoll = o[30];
            ShrugL = o[31]; ShrugR = o[32]; ReachL = o[33]; ReachR = o[34]; Step = o[35]; Squash = o[36];
        }

        /// <summary>
        /// A damped spring on every field: <paramref name="cur"/> chases <paramref name="target"/>
        /// at ω rad/s with damping ζ (0.7–0.8 overshoots a little, the way a body settles after a
        /// move). The expression and the spin are copied straight from the target.
        /// </summary>
        // follow-through: the joints down the chain are softer, so a forearm trails its upper arm and
        // a hand trails the forearm by a few frames (indices as in ToArray)
        static readonly float[] OmegaScale =
        {
            1f, 1f, 1f, 1f,            // shoulders
            0.82f, 0.82f, 0.82f, 0.82f,// elbows, forearm folds
            1f, 1f, 1f, 1f,            // knees, thighs
            1f, 1f, 1f, 1f, 1f,        // trunk
            0.78f, 0.78f, 0.78f,       // head
            1f,                        // Y
            0.66f, 0.66f, 0.66f, 0.66f,// wrists
            0.9f, 0.9f,                // toes
            0.6f, 0.6f,                // fingers
            1f, 1f,                    // sway, hip roll
            1f, 1f, 1f, 1f,            // clavicles
            1f, 1f, 1f, 1f, 1f,        // step, spare
        };

        public static void Spring(ref Pose cur, float[] vel, in Pose target, float dt, float omega, float zeta)
        {
            var c = new float[Count]; var tg = new float[Count];
            cur.ToArray(c); target.ToArray(tg);
            var n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.012f), 1, 8); var h = dt / n;
            for (var k = 0; k < n; k++)
                for (var i = 0; i < Count; i++)
                {
                    var w = omega * OmegaScale[i];
                    vel[i] += (tg[i] - c[i]) * w * w * h - 2f * zeta * w * vel[i] * h;
                    c[i] += vel[i] * h;
                }
            cur.FromArray(c);
            cur.Yaw = target.Yaw; cur.Expr = target.Expr; cur.Free = target.Free;
        }

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
                HeadPitch = Mathf.Lerp(a.HeadPitch, b.HeadPitch, t), HeadTilt = Mathf.Lerp(a.HeadTilt, b.HeadTilt, t), HeadYaw = Mathf.Lerp(a.HeadYaw, b.HeadYaw, t),
                SpineBend = Mathf.Lerp(a.SpineBend, b.SpineBend, t), SpineTwist = Mathf.Lerp(a.SpineTwist, b.SpineTwist, t), SpineSide = Mathf.Lerp(a.SpineSide, b.SpineSide, t),
                ShrugL = Mathf.Lerp(a.ShrugL, b.ShrugL, t), ShrugR = Mathf.Lerp(a.ShrugR, b.ShrugR, t), ReachL = Mathf.Lerp(a.ReachL, b.ReachL, t), ReachR = Mathf.Lerp(a.ReachR, b.ReachR, t),
                Step = Mathf.Lerp(a.Step, b.Step, t), Squash = Mathf.Lerp(a.Squash, b.Squash, t),
                Y = Mathf.Lerp(a.Y, b.Y, t), Yaw = b.Yaw, Expr = b.Expr, Free = b.Free,
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

    /// <summary>
    /// The expression on a sample-rig figure: swaps the eye/mouth sheet on the face renderer's
    /// eye submesh, and blinks — a 0.13 s shut sheet every 3.2–5.5 s (period and phase from the
    /// id, so a line-up never blinks in unison) whenever the state's own expression is neutral.
    /// </summary>
    public static class SdExpr
    {
        public static void Tick(ChibiRig rig, string heroId, string expr, float time)
        {
            var e = expr ?? "";
            if (e == "")
            {
                var h = SdPose.Hash(heroId);
                var period = 3.2f + (h % 100) / 100f * 2.3f;
                var phase = (h / 100 % 100) / 100f * period;
                if ((time + phase) % period < 0.13f) e = "blink";
            }
            Set(rig, heroId, e);
        }

        public static void Set(ChibiRig rig, string heroId, string expr)
        {
            if (rig != null && rig.Blink != null && rig.Expression != expr) { rig.Expression = expr; rig.Blink.Express(expr); return; }
            if (rig == null || rig.EyeSub < 0 || rig.FaceRenderer == null || rig.Expression == expr) return;
            rig.Expression = expr;
            var b = new MaterialPropertyBlock();
            rig.FaceRenderer.GetPropertyBlock(b, rig.EyeSub);
            b.SetTexture("_MainTex", SdRefLook.For(heroId).EyeSheet(expr));
            rig.FaceRenderer.SetPropertyBlock(b, rig.EyeSub);
        }
    }

    public static class SdPose
    {
        public const int IdleCount = 8, WinCount = 8;
        /// <summary>Metres of travel per walk cycle (2π) at root scale 1 — BattleWorld drives the phase by distance with it, so the planted foot stays put. Calibrated by SdMotionTest.Calibrate.</summary>
        public const float WalkCycle = 0.85f;   // SdMotionTest.Calibrate: slip 0.26 of the travel (was ~1.0 at the fixed 11 rad/s)

        // easing: a body accelerates into a move and settles out of it
        static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }
        static float EaseIn(float t) { t = Mathf.Clamp01(t); return t * t * t; }
        static float EaseInOut(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        /// <summary>Overshoots past 1 and settles (a strike that snaps straight).</summary>
        static float EaseOutBack(float t, float k = 1.7f) { t = Mathf.Clamp01(t); var c = k + 1f; return 1f + c * Mathf.Pow(t - 1f, 3f) + k * Mathf.Pow(t - 1f, 2f); }
        /// <summary>0 → 1 → 0, peaking at <paramref name="peak"/> (0..1), sharp in and soft out.</summary>
        static float Impulse(float t, float peak = 0.3f) { t = Mathf.Clamp01(t); return t < peak ? EaseOut(t / peak) : 1f - EaseInOut((t - peak) / (1f - peak)); }

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
        /// 4 a relaxed lean, weight back · 5 one hand up at the shoulder, the other on the hip ·
        /// 6 a hand up to the temple (adjusting glasses / tucking hair) every few seconds, else
        /// hands loose · 7 a slow stretch: arms up and back, then dropped with a shrug.
        /// </summary>
        public static Pose Idle(int variant, float t, float phase)
        {
            var p = Pose.Rest;
            var br = Mathf.Sin(t * 2.4f + phase);              // breathing
            var sway = Mathf.Sin(t * 0.8f + phase);
            // the weight moves from one leg to the other every few seconds, and the eyes wander:
            // a slow side signal (−1..1) that the stance variants scale, and a head turn that
            // holds, glances, and returns
            var side = Mathf.Sin(t * 0.32f + phase * 0.7f);
            var glance = Mathf.Sin(t * 0.55f + phase) * Mathf.Sin(t * 0.21f + phase * 1.3f);
            // breathing a chibi can be SEEN doing: 1–1.5° read as a statue at battle distance, so the
            // chest, shoulders and head move about twice that, and the whole figure bobs a little
            p.Lean = br * 2.4f; p.SpineBend = br * 2f; p.HeadPitch = -br * 2.6f;
            p.HeadYaw = glance * 10f; p.HeadTilt = glance * 3f;
            p.ShrugL = br * 4f; p.ShrugR = br * 4f; p.Squash = br * 0.008f;   // the chest rises by stretching, not by lifting the feet (MotionTest: floats)
            p.Sway = side * 0.012f; p.HipRoll = side * 4f; p.SpineSide = -side * 2.5f;
            p.KneeL = Mathf.Max(0f, side) * 12f; p.KneeR = Mathf.Max(0f, -side) * 12f;
            switch (variant % IdleCount)
            {
                case 0:
                    // contrapposto: weight over the right leg, the left knee soft, hips out to the right,
                    // the shoulders tilted the other way, a slow sway of the whole weight
                    p.RaiseL += br * 2f; p.RaiseR -= br * 2f; p.Twist = sway * 3f; p.HeadTilt += -3f + sway * 2f;
                    p.HandFlexL = 22f; p.HandFlexR = 16f; break;
                case 1:
                    // hands on the hips: upper arms a little back, forearms folded in to the sides of the waist,
                    // wrists bent so the palms sit on the hips; weight on the left leg
                    p.RaiseL = p.RaiseR = -22f; p.SwingL = p.SwingR = -12f; p.ElbowL = p.ElbowR = 22f; p.InL = p.InR = 52f;
                    p.HandFlexL = p.HandFlexR = 45f; p.HandDevL = p.HandDevR = -15f; p.FistL = p.FistR = 0.15f;
                    p.Twist = sway * 2f; p.HeadTilt += -4f; p.SpineTwist = 3f; break;
                case 2:
                    p.RaiseL = p.RaiseR = -22f; p.SwingL = p.SwingR = 30f; p.ElbowL = p.ElbowR = 50f; p.InL = p.InR = 95f;
                    p.HandFlexL = p.HandFlexR = 30f; p.ShrugL += 3f; p.ShrugR += 3f;
                    p.HeadPitch += 3f; p.HeadTilt += sway * 2f; break;
                case 3:
                    p.RaiseR = -8f; p.SwingR = 42f; p.ElbowR = 125f; p.InR = 35f; p.HandFlexR = 40f;
                    p.RaiseL = -20f; p.SwingL = 12f; p.ElbowL = 40f; p.InL = 80f; p.HandFlexL = 30f;
                    p.HeadTilt += 7f; p.HeadPitch += 2f; p.Twist = 4f; p.SpineTwist = 4f; break;
                case 4:
                    // relaxed lean: weight back on one leg, arms hanging a little behind, shoulders turned
                    p.RaiseL = p.RaiseR = -38f; p.SwingL = p.SwingR = -14f; p.ElbowL = p.ElbowR = 10f;
                    p.HandFlexL = p.HandFlexR = 12f;
                    p.Twist = 8f + sway * 2f; p.SpineBend -= 3f; p.HeadTilt += -5f + sway * 2f; break;
                case 5:
                    p.RaiseR = 6f; p.SwingR = 22f; p.ElbowR = 95f + br * 3f; p.InR = 10f; p.HandFlexR = -10f; p.HandDevR = 20f;
                    p.RaiseL = -22f; p.SwingL = -12f; p.ElbowL = 22f; p.InL = 52f; p.HandFlexL = 45f; p.HandDevL = -15f;
                    p.ShrugR += 6f; p.HeadTilt += -5f; p.Twist = -3f; break;
                case 6:
                    {
                        // every ~7 s the right hand comes up to the temple for a second (glasses, hair), the head dips to meet it
                        var cyc = (t * 0.14f + phase * 0.1f) % 1f;
                        var up = cyc < 0.16f ? EaseInOut(cyc / 0.16f) : cyc < 0.30f ? 1f : cyc < 0.42f ? 1f - EaseInOut((cyc - 0.30f) / 0.12f) : 0f;
                        p.RaiseR = Mathf.Lerp(-36f, 2f, up); p.SwingR = Mathf.Lerp(3f, 48f, up); p.ElbowR = Mathf.Lerp(14f, 128f, up); p.InR = 30f * up;
                        p.HandFlexR = Mathf.Lerp(18f, 30f, up); p.HandDevR = 10f * up; p.FistR = Mathf.Lerp(0.25f, 0.55f, up);
                        p.RaiseL = -34f; p.SwingL = 4f; p.ElbowL = 16f;
                        p.HeadTilt += 6f * up; p.HeadPitch += 3f * up; p.HeadYaw += -4f * up; p.ShrugR += 4f * up;
                        break;
                    }
                case 7:
                    {
                        // a stretch every ~9 s: both arms rise and go back, the chest opens, then they drop with a shrug
                        var cyc = (t * 0.11f + phase * 0.1f) % 1f;
                        var up = cyc < 0.22f ? EaseInOut(cyc / 0.22f) : cyc < 0.38f ? 1f : cyc < 0.5f ? 1f - EaseInOut((cyc - 0.38f) / 0.12f) : 0f;
                        var drop = cyc > 0.5f && cyc < 0.62f ? Impulse((cyc - 0.5f) / 0.12f, 0.3f) : 0f;
                        p.RaiseL = p.RaiseR = Mathf.Lerp(-36f, 86f, up); p.SwingL = p.SwingR = Mathf.Lerp(3f, -14f, up); p.ElbowL = p.ElbowR = Mathf.Lerp(14f, 10f, up);   // up and out (up and back went through the head)
                        p.FistL = p.FistR = Mathf.Lerp(0.25f, 0.9f, up); p.HandFlexL = p.HandFlexR = Mathf.Lerp(18f, 40f, up);
                        p.SpineBend += -8f * up; p.HeadPitch += -8f * up; p.ShrugL += 8f * up + 9f * drop; p.ShrugR += 8f * up + 9f * drop;
                        p.Lean += 3f * drop; p.HeadPitch += 4f * drop;
                        break;
                    }
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
                    p.KneeL = 24f + Mathf.Abs(br) * 5f; p.KneeR = 20f + Mathf.Abs(br) * 5f; p.Lean = 8f; p.Twist = 14f; p.SpineTwist = 8f; p.Y = 0f; p.Squash = -0.03f + Mathf.Abs(br) * 0.02f;
                    p.HeadPitch = 2f; p.Sway = 0.008f; p.ReachL = 8f; p.ReachR = 8f; p.ShrugL = 4f; p.ShrugR = 4f;
                    break;
                case 1:
                    p.RaiseR = -6f; p.SwingR = 62f; p.ElbowR = 55f; p.InR = 35f; p.HandFlexR = 10f; p.FistR = 0.6f;
                    p.RaiseL = -10f; p.SwingL = 55f; p.ElbowL = 70f; p.InL = 45f; p.HandFlexL = 20f; p.FistL = 0.6f;
                    p.KneeL = 16f; p.KneeR = 8f; p.Lean = 5f; p.Twist = 16f + br * 1.5f; p.SpineTwist = 8f; p.HeadPitch = 3f; p.Sway = 0.012f; p.ReachR = 8f; p.Squash = -0.02f + Mathf.Abs(br) * 0.015f;
                    break;
                case 2:
                    p.RaiseR = -14f; p.SwingR = 45f; p.ElbowR = 100f; p.InR = 15f; p.HandFlexR = 0f; p.FistR = 0.7f;
                    p.RaiseL = -30f; p.SwingL = 10f; p.ElbowL = 35f; p.InL = 60f; p.HandFlexL = 30f; p.FistL = 0.3f;
                    p.KneeR = 8f; p.Sway = -0.01f; p.HipRoll = -3f; p.HeadTilt = 4f + br * 1.5f;
                    break;
            }
            return p;
        }

        // ------------------------------------------------------------------ win --

        /// <summary>
        /// 0 both arms up, hopping · 1 fist pump · 2 V-sign by the cheek · 3 a bow · 4 a wave ·
        /// 5 a spin with arms out · 6 a double V, hands out beside the face, bouncing · 7 clapping,
        /// then a small bow. <paramref name="t"/> is time since the win.
        /// </summary>
        public static Pose Victory(int variant, float t)
        {
            // "smile": eyes OPEN with a smiling mouth — BA's shut ^^ arcs are a hairline at the result
            // camera's distance and read as a face with no eyes (the user)
            var p = Pose.Rest; p.Expr = "smile";
            var hop = Mathf.Abs(Mathf.Sin(t * 7f));
            switch (variant % WinCount)
            {
                case 0:
                    p.RaiseL = p.RaiseR = 84f; p.SwingL = p.SwingR = -12f; p.ElbowL = p.ElbowR = 16f;   // up and OUT: straight up and back, the arms went behind the big head
                    p.HandFlexL = p.HandFlexR = -20f; p.HandDevL = p.HandDevR = 25f; p.FistL = p.FistR = 0f;   // palms open, fingers spread up
                    p.Y = hop * 0.14f; p.Squash = (hop - 0.45f) * 0.18f; p.KneeL = p.KneeR = (1f - hop) * 22f; p.ToeL = p.ToeR = hop * 20f; p.HeadPitch = -6f; p.ShrugL = p.ShrugR = 10f; p.SpineBend = -4f; break;
                case 1:
                    var pump = Mathf.Abs(Mathf.Sin(t * 9f));
                    p.RaiseR = 70f + pump * 25f; p.SwingR = -20f; p.ElbowR = 70f - pump * 40f; p.HandFlexR = 50f; p.FistR = 1f;   // a fist
                    p.RaiseL = -22f; p.SwingL = -12f; p.ElbowL = 22f; p.InL = 52f; p.HandFlexL = 45f; p.HandDevL = -15f;
                    p.KneeL = 8f + (1f - hop) * 10f; p.KneeR = (1f - hop) * 10f; p.Sway = 0.01f; p.HipRoll = 3f;
                    p.Y = hop * 0.08f; p.Lean = -4f; p.Twist = -8f; p.SpineTwist = -6f; p.ShrugR = 8f + pump * 6f; break;
                case 2:
                    // hand by the cheek, head tilted onto it, weight on the near leg
                    p.RaiseR = 42f; p.SwingR = 34f; p.ElbowR = 104f; p.InR = 4f; p.HandFlexR = 35f; p.HandDevR = 10f;   // the elbow out, so the hand sits beside the cheek, not in it
                    p.RaiseL = -30f; p.SwingL = 5f; p.ElbowL = 20f; p.HandFlexL = 20f;
                    p.KneeL = 10f; p.Sway = 0.012f; p.HipRoll = 3f;
                    p.HeadTilt = 10f; p.Twist = 6f; p.Y = hop * 0.04f; p.ShrugR = 6f; break;
                case 3:
                    var bow = Mathf.Clamp01(Mathf.Sin(Mathf.Min(t * 2.2f, Mathf.PI)));
                    p.Lean = 32f * bow; p.HeadPitch = 12f * bow;
                    p.RaiseL = p.RaiseR = -34f; p.SwingL = p.SwingR = 6f; p.ElbowL = p.ElbowR = 12f; break;
                case 4:
                    p.RaiseR = 72f; p.SwingR = 4f + Mathf.Sin(t * 12f) * 10f; p.ElbowR = 38f + Mathf.Abs(Mathf.Sin(t * 12f)) * 14f;   // out to the side, the forearm up: raised straight the arm vanished into the hair
                    p.HandFlexR = -15f; p.HandDevR = Mathf.Sin(t * 12f) * 25f; p.FistR = 0f;   // the hand itself waves, open
                    p.RaiseL = -22f; p.SwingL = -12f; p.ElbowL = 22f; p.InL = 52f; p.HandFlexL = 45f; p.HandDevL = -15f;
                    p.Y = hop * 0.1f; p.Squash = (hop - 0.45f) * 0.12f; p.KneeL = p.KneeR = (1f - hop) * 20f; p.ShrugR = 8f; break;
                case 5:
                    p.RaiseL = p.RaiseR = 12f; p.SwingL = p.SwingR = 18f; p.ElbowL = p.ElbowR = 30f;
                    p.Yaw = Mathf.Min(t * 240f, 720f); p.Y = Mathf.Sin(Mathf.Min(t * 3.5f, Mathf.PI)) * 0.25f; break;
                case 6:
                    {
                        // both hands up beside the cheeks in a V, the elbows tucked, a bounce with the head tilting side to side
                        var tilt = Mathf.Sin(t * 5f) * 8f;
                        p.RaiseL = p.RaiseR = 34f; p.SwingL = p.SwingR = 38f; p.ElbowL = p.ElbowR = 108f; p.InL = p.InR = 0f;   // the hands beside the cheeks — folded inward they covered the face
                        p.HandFlexL = p.HandFlexR = 30f; p.HandDevL = p.HandDevR = 12f; p.FistL = p.FistR = 0.55f;
                        p.HeadTilt = tilt; p.SpineSide = -tilt * 0.3f; p.Y = hop * 0.06f; p.KneeL = p.KneeR = (1f - hop) * 14f;
                        p.ShrugL = p.ShrugR = 8f; break;
                    }
                case 7:
                    {
                        // clapping in front of the chest for a second and a half, then a small bow with the hands together
                        var clap = t < 1.5f ? Mathf.Abs(Mathf.Sin(t * 11f)) : 0f;
                        var bow2 = t > 1.5f ? Mathf.Clamp01(Mathf.Sin(Mathf.Min((t - 1.5f) * 2.0f, Mathf.PI))) : 0f;
                        p.RaiseL = p.RaiseR = -14f; p.SwingL = p.SwingR = 36f + 6f * clap; p.ElbowL = p.ElbowR = 78f;   // the hands at the chest (at 58° / 100° they clapped in front of the face)
                        p.InL = p.InR = 76f - 16f * clap; p.HandFlexL = p.HandFlexR = -10f; p.HandDevL = p.HandDevR = 20f; p.FistL = p.FistR = 0f;
                        p.Lean = 22f * bow2; p.HeadPitch = 8f * bow2 - 2f * clap; p.SpineBend = 6f * bow2;
                        p.ShrugL = p.ShrugR = 5f * clap; p.Y = 0f; break;
                    }
            }
            return p;
        }

        // ------------------------------------------------------------------ fight --

        /// <summary>
        /// 0 melee punch · 1 ranged shot · 2 caster · and one per Excel attack (BattleWorld.FireOf):
        /// 3 셀 입력 (three taps) · 4 자동 채우기 (five) · 5 범위 붙여넣기 (a wide sweep) · 6 =SUM (gather, push) ·
        /// 7 참조 추적 (a point) · 8 커피 (an overhand lob) · 9 backhand cut · 10 chop down · 11 X · 12 잘라내기 (scissors). a = 0..1.
        /// </summary>
        public static Pose Attack(int kind, float a)
        {
            var p = Pose.Rest; p.Expr = "angry";
            switch (kind)
            {
                default:
                    {
                        // wind-up (ease-in: the arm draws back, the waist turns away, weight onto the back
                        // foot), strike (ease-out-back: the fist snaps past straight, the waist whips
                        // through, a step in), recovery (ease-in-out back to the stance)
                        float wind, strike, rec;
                        if (a < 0.28f) { wind = EaseInOut(a / 0.28f); strike = 0f; rec = 0f; }
                        else if (a < 0.5f) { wind = 1f; strike = EaseOutBack((a - 0.28f) / 0.22f, 0.9f); rec = 0f; }   // peaks ≈ 1.1: a snap, not a swing past
                        else { wind = 1f; strike = 1f; rec = EaseInOut((a - 0.5f) / 0.5f); }
                        var swing = Mathf.Lerp(3f, -45f, wind); swing = Mathf.Lerp(swing, 100f, strike); swing = Mathf.Lerp(swing, 3f, rec);
                        var elbow = Mathf.Lerp(14f, 95f, wind); elbow = Mathf.Max(2f, Mathf.Lerp(elbow, 4f, strike)); elbow = Mathf.Lerp(elbow, 14f, rec);
                        var raise = Mathf.Lerp(-36f, -12f, wind); raise = Mathf.Lerp(raise, -4f, strike); raise = Mathf.Lerp(raise, -36f, rec);
                        p.SwingR = swing; p.ElbowR = elbow; p.RaiseR = raise;
                        p.ReachR = Mathf.Lerp(Mathf.Lerp(0f, -6f, wind), 12f, strike) * (1f - rec);
                        p.ShrugR = Mathf.Lerp(4f * wind, 8f, strike) * (1f - rec);
                        p.Twist = Mathf.Lerp(Mathf.Lerp(0f, -10f, wind), 14f, strike) * (1f - rec);
                        p.SpineTwist = Mathf.Lerp(Mathf.Lerp(0f, -14f, wind), 18f, strike) * (1f - rec);
                        p.Lean = Mathf.Lerp(Mathf.Lerp(0f, -4f, wind), 12f, strike) * (1f - rec);
                        p.Sway = Mathf.Lerp(Mathf.Lerp(0f, -0.02f, wind), 0.03f, strike) * (1f - rec);
                        p.Step = Mathf.Lerp(0f, 0.16f, strike) * (1f - EaseIn(rec));
                        p.KneeL = Mathf.Lerp(Mathf.Lerp(0f, 8f, wind), 24f, strike) * (1f - rec); p.KneeR = 10f * wind * (1f - rec);
                        p.HeadPitch = 4f * strike * (1f - rec); p.HeadYaw = -6f * wind * (1f - strike);
                        p.RaiseL = -20f; p.SwingL = 30f; p.ElbowL = 70f; p.InL = 40f; p.HandFlexL = 50f;   // the guard hand, a fist
                        p.HandFlexR = 50f; p.FistR = 1f; p.FistL = 1f;
                        break;
                    }
                case 1:
                    {
                        // arm out straight at the target, a recoil kick at the shot, then lowered
                        // raise (ease-out), a recoil kick at the shot (sharp in, soft out), lower (ease-in-out)
                        // anticipation (the arm drawn back, the body leaning away) -> a snap up in a few
                        // frames -> the recoil -> a forward overshoot -> lowered
                        var anti = a < 0.16f ? EaseInOut(a / 0.16f) : a < 0.24f ? 1f - EaseOut((a - 0.16f) / 0.08f) : 0f;
                        var up = a < 0.16f ? 0f : a < 0.26f ? EaseOutBack((a - 0.16f) / 0.1f, 1.2f) : a < 0.75f ? 1f : 1f - EaseInOut((a - 0.75f) / 0.25f);
                        var kick = a > 0.3f && a < 0.55f ? Impulse((a - 0.3f) / 0.25f, 0.2f) : 0f;
                        var over = a > 0.5f && a < 0.8f ? Impulse((a - 0.5f) / 0.3f, 0.3f) : 0f;
                        p.RaiseR = Mathf.Lerp(-36f, -2f, up); p.SwingR = Mathf.Lerp(3f, 92f, up) - kick * 22f; p.ElbowR = Mathf.Lerp(14f, 4f, up) + kick * 34f;
                        p.RaiseL = -25f; p.SwingL = 20f; p.ElbowL = 60f; p.InL = 50f;
                        p.Lean = -2f * up + kick * 7f; p.SpineBend = -3f * up + kick * 5f; p.Twist = 14f * up; p.SpineTwist = 8f * up; p.HeadTilt = 3f * up;
                        p.ReachR = 10f * up - kick * 8f; p.ShrugR = 4f * up + kick * 6f; p.HeadPitch = kick * 3f;
                        p.HandFlexR = 10f; p.HandFlexL = 40f; p.FistR = 0.7f; p.FistL = 1f; p.Sway = 0.012f * up - kick * 0.02f; p.KneeL = 10f * up + kick * 6f;
                        p.SwingR -= anti * 30f; p.ElbowR += anti * 40f; p.Lean += -8f * anti + over * 6f; p.SpineBend += -6f * anti + over * 4f;
                        p.SpineTwist -= anti * 10f; p.HeadPitch += over * 3f; p.Squash = -0.04f * anti - 0.03f * kick + 0.02f * over;
                        break;
                    }
                case 3:
                case 4:
                    {
                        // 셀 입력 (3) / 자동 채우기 (4): the tablet held up in front of the chest, the
                        // other hand tapping it — three taps, or five quick ones — eyes down on the
                        // screen, then the tablet pushed at the target on the last tap
                        var n = kind == 3 ? 3 : 5;
                        var up = a < 0.18f ? EaseOutBack(a / 0.18f, 1.1f) : a < 0.8f ? 1f : 1f - EaseInOut((a - 0.8f) / 0.2f);
                        var tapT = Mathf.Clamp01((a - 0.16f) / 0.6f) * n;
                        var tap = a > 0.16f && a < 0.76f ? Mathf.Pow(Mathf.Abs(Mathf.Sin(tapT * Mathf.PI)), 0.6f) : 0f;
                        var push = a > 0.62f && a < 0.9f ? Impulse((a - 0.62f) / 0.28f, 0.3f) : 0f;
                        p.RaiseR = Mathf.Lerp(-36f, -22f, up); p.SwingR = Mathf.Lerp(3f, 40f, up) + push * 30f; p.ElbowR = Mathf.Lerp(14f, 62f, up) - push * 36f; p.InR = 26f * up;   // the tablet at the chest, not the face
                        p.HandFlexR = 12f; p.FistR = 0.55f; p.ReachR = push * 10f;
                        p.RaiseL = Mathf.Lerp(-36f, -16f, up); p.SwingL = Mathf.Lerp(3f, 38f, up) + tap * 8f; p.ElbowL = Mathf.Lerp(14f, 70f, up) - tap * 12f; p.InL = 50f * up;
                        p.HandFlexL = 20f + tap * 38f; p.FistL = 0.3f;
                        p.HeadPitch = 12f * up * (1f - push) - 2f * push; p.SpineBend = 4f * up; p.Lean = 3f * up + push * 6f;
                        p.Twist = 6f * up; p.Squash = -0.015f * tap - 0.02f * push; p.KneeL = 6f * up;
                        break;
                    }
                case 5:
                    {
                        // 범위 붙여넣기: the tablet arm drawn across the body, then swept wide and out
                        // in one flat arc, the waist turning through it
                        var wind = a < 0.26f ? EaseInOut(a / 0.26f) : 1f;
                        var sweep = a < 0.26f ? 0f : a < 0.56f ? EaseOutBack((a - 0.26f) / 0.3f, 1.2f) : 1f;
                        var rec = a < 0.62f ? 0f : EaseInOut((a - 0.62f) / 0.38f);
                        p.SwingR = Mathf.Lerp(3f, 70f, wind); p.RaiseR = Mathf.Lerp(-36f, -8f, wind);
                        p.InR = Mathf.Lerp(0f, 55f, wind); p.InR = Mathf.Lerp(p.InR, -35f, sweep); p.InR = Mathf.Lerp(p.InR, 0f, rec);
                        p.SwingR = Mathf.Lerp(p.SwingR, 3f, rec); p.RaiseR = Mathf.Lerp(p.RaiseR, -36f, rec);
                        p.ElbowR = Mathf.Lerp(Mathf.Lerp(14f, 70f, wind), 12f, sweep); p.ElbowR = Mathf.Lerp(p.ElbowR, 14f, rec);
                        p.Twist = (Mathf.Lerp(0f, -16f, wind) + 34f * sweep) * (1f - rec); p.SpineTwist = (Mathf.Lerp(0f, -12f, wind) + 26f * sweep) * (1f - rec);
                        p.Lean = (2f + 6f * sweep) * (1f - rec); p.Step = 0.08f * sweep * (1f - rec); p.KneeL = 16f * sweep * (1f - rec);
                        p.RaiseL = -30f; p.SwingL = 12f; p.ElbowL = 40f; p.HandFlexR = 10f; p.FistR = 0.5f;
                        p.HeadYaw = (-8f * wind + 10f * sweep) * (1f - rec);
                        break;
                    }
                case 6:
                    {
                        // =SUM: both hands brought together low in front and held, trembling, as the
                        // formula gathers (a small crouch), then pushed out with a step
                        var gather = a < 0.45f ? EaseInOut(a / 0.45f) : 1f;
                        var shake = a > 0.15f && a < 0.5f ? Mathf.Sin(a * 140f) * 0.5f : 0f;
                        var push = a > 0.45f ? Impulse((a - 0.45f) / 0.55f, 0.28f) : 0f;
                        p.RaiseL = p.RaiseR = Mathf.Lerp(-36f, -18f, gather) + push * 14f;
                        p.SwingL = p.SwingR = Mathf.Lerp(3f, 40f, gather) + push * 50f;
                        p.ElbowL = p.ElbowR = Mathf.Lerp(14f, 90f, gather) - push * 76f;
                        p.InL = p.InR = 38f * gather * (1f - push * 0.6f);
                        p.HandFlexL = p.HandFlexR = -10f * gather - 20f * push; p.FistL = p.FistR = 0.2f * (1f - push);
                        p.KneeL = p.KneeR = 18f * gather * (1f - push) + 4f * shake; p.Squash = -0.05f * gather * (1f - push) + 0.03f * push;
                        p.Lean = 4f * gather + 10f * push; p.SpineBend = 6f * gather * (1f - push); p.HeadPitch = 8f * gather * (1f - push) - 4f * push;
                        p.Step = 0.12f * push; p.ReachL = p.ReachR = 12f * push;
                        break;
                    }
                case 7:
                    {
                        // 참조 추적: a point — the arm straight out at the target and held while the
                        // trace line draws, the other hand on the hip, a small head tilt
                        var up = a < 0.2f ? EaseOutBack(a / 0.2f, 1.3f) : a < 0.78f ? 1f : 1f - EaseInOut((a - 0.78f) / 0.22f);
                        p.SwingR = Mathf.Lerp(3f, 88f, up); p.RaiseR = Mathf.Lerp(-36f, 2f, up); p.ElbowR = Mathf.Lerp(14f, 2f, up); p.ReachR = 14f * up;
                        p.HandFlexR = -6f; p.FistR = 0.8f;
                        p.RaiseL = -8f * up - 36f * (1f - up); p.SwingL = -18f * up; p.ElbowL = Mathf.Lerp(14f, 95f, up); p.InL = -28f * up; p.HandFlexL = 30f * up;
                        p.Twist = 18f * up; p.SpineTwist = 10f * up; p.HeadTilt = 5f * up; p.HeadYaw = 6f * up; p.Lean = -2f * up;
                        p.KneeL = 4f * up; p.Sway = 0.01f * up;
                        break;
                    }
                case 8:
                    {
                        // 커피 투척: an overhand lob — the cup drawn back over the shoulder, then the arm
                        // whipped over and through, the body following
                        var back = a < 0.32f ? EaseInOut(a / 0.32f) : 1f;
                        var thr = a < 0.32f ? 0f : a < 0.58f ? EaseOutBack((a - 0.32f) / 0.26f, 1f) : 1f;
                        var rec = a < 0.62f ? 0f : EaseInOut((a - 0.62f) / 0.38f);
                        var sw = Mathf.Lerp(3f, 150f, back); sw = Mathf.Lerp(sw, 60f, thr); sw = Mathf.Lerp(sw, 3f, rec);
                        p.SwingR = sw; p.RaiseR = Mathf.Lerp(Mathf.Lerp(-36f, -10f, back), -20f, thr); p.RaiseR = Mathf.Lerp(p.RaiseR, -36f, rec);
                        p.ElbowR = Mathf.Lerp(Mathf.Lerp(14f, 100f, back), 10f, thr); p.ElbowR = Mathf.Lerp(p.ElbowR, 14f, rec);
                        p.Lean = (Mathf.Lerp(0f, -8f, back) + 16f * thr) * (1f - rec); p.SpineBend = (-6f * back + 10f * thr) * (1f - rec);
                        p.Twist = (-12f * back + 18f * thr) * (1f - rec); p.Step = 0.08f * thr * (1f - rec); p.KneeL = 14f * thr * (1f - rec);
                        p.RaiseL = -20f; p.SwingL = 30f * back * (1f - thr); p.ElbowL = 40f; p.HandFlexR = 20f; p.FistR = 0.6f;
                        p.HeadPitch = (-6f * back + 4f * thr) * (1f - rec);
                        break;
                    }
                case 9:
                case 10:
                case 11:
                    {
                        // blades by hand: 9 a backhand cut across (the arm from the far shoulder out),
                        // 10 a chop down from overhead, 11 two quick chops crossing (an X)
                        float Chop(float t, bool down, bool mirror)
                        {
                            var w = t < 0.35f ? EaseInOut(t / 0.35f) : 1f;
                            var c = t < 0.35f ? 0f : t < 0.62f ? EaseOutBack((t - 0.35f) / 0.27f, 1.1f) : 1f;
                            var r = t < 0.66f ? 0f : EaseInOut((t - 0.66f) / 0.34f);
                            if (down)
                            {
                                p.SwingR = Mathf.Lerp(Mathf.Lerp(3f, 160f, w), 40f, c); p.RaiseR = Mathf.Lerp(Mathf.Lerp(-36f, -6f, w), -24f, c);
                                p.ElbowR = Mathf.Lerp(Mathf.Lerp(14f, 60f, w), 6f, c);
                                p.Lean = -6f * w * (1f - c) + 16f * c; p.SpineBend = 10f * c; p.KneeL = p.KneeR = 18f * c; p.Squash = -0.04f * c;
                            }
                            else
                            {
                                var sgn = mirror ? -1f : 1f;
                                p.SwingR = Mathf.Lerp(Mathf.Lerp(3f, 80f, w), 84f, c); p.RaiseR = Mathf.Lerp(-36f, -4f, w);
                                p.InR = sgn * Mathf.Lerp(Mathf.Lerp(0f, 60f, w), -40f, c); p.ElbowR = Mathf.Lerp(Mathf.Lerp(14f, 90f, w), 8f, c);
                                p.Twist = sgn * (-18f * w * (1f - c) + 26f * c); p.SpineTwist = sgn * (-14f * w * (1f - c) + 20f * c);
                                p.Lean = 10f * c; p.KneeL = 16f * c;
                            }
                            p.HandFlexR = -10f; p.FistR = 0.1f;   // a flat hand, the edge leading
                            p.Step = 0.14f * c;
                            return r;
                        }
                        float rr;
                        if (kind == 9) rr = Chop(a, false, false);
                        else if (kind == 10) rr = Chop(a, true, false);
                        else rr = a < 0.5f ? Chop(a * 2f, false, false) : Chop((a - 0.5f) * 2f, false, true);
                        p.RaiseL = -24f; p.SwingL = 26f; p.ElbowL = 72f; p.InL = 36f; p.FistL = 1f; p.HandFlexL = 40f;
                        // ease every channel home on the recovery
                        var keep = 1f - rr;
                        p.SwingR = Mathf.Lerp(3f, p.SwingR, keep); p.RaiseR = Mathf.Lerp(-36f, p.RaiseR, keep); p.ElbowR = Mathf.Lerp(14f, p.ElbowR, keep); p.InR *= keep;
                        p.Twist *= keep; p.SpineTwist *= keep; p.Lean *= keep; p.SpineBend *= keep; p.KneeL *= keep; p.KneeR *= keep; p.Step *= keep; p.Squash *= keep;
                        break;
                    }
                case 12:
                    {
                        // 잘라내기: both hands out crossed like closing scissors, snapped open and shut twice
                        var up = a < 0.2f ? EaseOutBack(a / 0.2f, 1f) : a < 0.8f ? 1f : 1f - EaseInOut((a - 0.8f) / 0.2f);
                        var snip = a > 0.2f && a < 0.8f ? Mathf.Abs(Mathf.Sin((a - 0.2f) / 0.6f * Mathf.PI * 2f)) : 0f;
                        p.SwingL = p.SwingR = Mathf.Lerp(3f, 80f, up); p.RaiseL = p.RaiseR = Mathf.Lerp(-36f, -6f, up);
                        p.ElbowL = p.ElbowR = Mathf.Lerp(14f, 20f, up);
                        p.InL = p.InR = Mathf.Lerp(0f, 30f, up) + snip * 24f - (1f - snip) * 10f * up;
                        p.HandFlexL = p.HandFlexR = -8f; p.FistL = p.FistR = 0.15f;
                        p.Lean = 8f * up; p.Step = 0.1f * up; p.KneeL = 12f * up; p.HeadPitch = -3f * up; p.ReachL = p.ReachR = 8f * up;
                        break;
                    }
                case 2:
                    {
                        // both hands raised in front, then thrust forward with a small hop
                        var up = a < 0.4f ? EaseInOut(a / 0.4f) : 1f;
                        var thrust = a > 0.4f ? Impulse((a - 0.4f) / 0.6f, 0.35f) : 0f;
                        p.RaiseL = p.RaiseR = Mathf.Lerp(-36f, 10f, up); p.SwingL = p.SwingR = Mathf.Lerp(3f, 45f, up) + thrust * 40f;
                        p.ElbowL = p.ElbowR = Mathf.Lerp(14f, 70f, up) - thrust * 50f; p.InL = p.InR = 20f * up;
                        p.ReachL = p.ReachR = thrust * 12f; p.ShrugL = p.ShrugR = up * 4f;
                        p.Y = thrust * 0.08f; p.Lean = 6f * thrust; p.SpineBend = 5f * thrust; p.HeadPitch = 4f * up - 3f * thrust; p.Step = thrust * 0.06f;
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
            p.Lean = 9f;                                        // into the walk (upright, it slid)
            p.Y = 0f;                                           // the rise and fall comes from planting the feet (SdPose.Apply) — a hand-made bob floated the walk 5–9 cm
            p.Squash = (Mathf.Abs(pass) - 0.5f) * 0.05f;        // a little squash at each contact
            p.Sway = -pass * 0.012f;                            // over the stance leg (the right when the left swings)
            p.HipRoll = pass * 4f; p.Twist = sw * 5f;           // pelvis with the legs, shoulders against
            p.SpineTwist = -sw * 7f; p.SpineSide = pass * 2f;   // the waist counters the hips; the trunk over the stance leg
            p.ReachL = sw * 4f; p.ReachR = -sw * 4f;            // the shoulders with the arm swing
            p.HeadPitch = Mathf.Abs(pass) * 1.5f - 1f; p.HeadYaw = -sw * 2f;
            return p;
        }

        /// <summary>k runs 1 → 0 from the impact: the flinch snaps in (peak at 25 % of the way) and settles out.</summary>
        public static Pose Hit(float k)
        {
            var p = Pose.Rest; p.Expr = "hurt";
            // the impact snaps in (peak at 12 %), HOLDS as a readable key pose, then eases back slowly;
            // the old flinch peaked and left at once and read as a nod (Gemini's read of the strip)
            var t = 1f - k;
            var f = t < 0.12f ? EaseOut(t / 0.12f) : t < 0.4f ? 1f : 1f - EaseInOut((t - 0.4f) / 0.6f);
            p.RaiseL = p.RaiseR = -36f + 44f * f; p.SwingL = p.SwingR = -18f * f; p.ElbowL = p.ElbowR = 14f + 60f * f;
            p.ShrugL = p.ShrugR = 16f * f; p.ReachL = p.ReachR = -8f * f;
            p.Lean = -10f * f; p.SpineBend = -18f * f; p.HeadPitch = -14f * f; p.HeadTilt = 7f * f;     // bent back in a C, chin up
            p.KneeL = p.KneeR = 16f * f; p.Step = -0.09f * f;
            p.Squash = -0.1f * f + 0.04f * Mathf.Sin(Mathf.Clamp01((t - 0.12f) / 0.3f) * Mathf.PI);
            return p;
        }

        /// <summary>
        /// The EX move, by attack kind. 0 melee: crouch, leap with the fist drawn back, smash down
        /// two-handed, land low. 1 ranged: the tablet raised overhead in both hands, a spin, then
        /// hurled forward. 2 healer: the cup raised high with a hop, the other arm flung out, happy.
        /// </summary>
        public static Pose Skill(int kind, float k)
        {
            var p = Pose.Rest; p.Expr = "angry";
            switch (kind)
            {
                default:
                    {
                        if (k < 0.2f) { var c = k / 0.2f; p.Squash = -0.16f * EaseOut(c); p.KneeL = p.KneeR = 45f * c; p.Lean = 12f * c; p.RaiseR = -36f + 10f * c; p.SwingR = -30f * c; p.ElbowR = 14f + 80f * c; p.FistR = 1f; p.FistL = 1f; p.SwingL = 20f * c; p.ElbowL = 60f * c; }
                        else if (k < 0.55f)
                        {
                            var c = (k - 0.2f) / 0.35f; var arc = Mathf.Sin(c * Mathf.PI);
                            // stretched on the way up, a beat of hang time at the apex, tucking to fall
                            p.Squash = c < 0.35f ? 0.14f * (1f - c / 0.35f) : c < 0.6f ? 0f : -0.04f * (c - 0.6f) / 0.4f;
                            p.Y = (c < 0.5f ? EaseOut(c / 0.5f) : c < 0.62f ? 1f : 1f - EaseIn((c - 0.62f) / 0.38f)) * 0.55f; p.KneeL = p.KneeR = 45f * (1f - c) + 30f * arc; p.Lean = Mathf.Lerp(12f, -15f, c);
                            p.RaiseR = Mathf.Lerp(-26f, 86f, c); p.SwingR = Mathf.Lerp(-30f, -12f, c); p.ElbowR = Mathf.Lerp(94f, 72f, c); p.FistR = p.FistL = 1f;
                            p.RaiseL = Mathf.Lerp(-36f, 80f, c); p.SwingL = -10f; p.ElbowL = 70f; p.HeadPitch = -8f;   // fists up beside the head (behind it they went through it)
                        }
                        else if (k < 0.7f)
                        {
                            // the smash: both fists driven down, the body folding forward
                            var c = (k - 0.55f) / 0.15f;
                            p.RaiseL = p.RaiseR = Mathf.Lerp(84f, -10f, c); p.SwingL = p.SwingR = Mathf.Lerp(-12f, 70f, c); p.ElbowL = p.ElbowR = Mathf.Lerp(72f, 20f, c);
                            p.FistL = p.FistR = 1f; p.Lean = Mathf.Lerp(-15f, 28f, c); p.KneeL = p.KneeR = 50f + 18f * c; p.Y = Mathf.Lerp(0.15f, 0f, c); p.HeadPitch = 10f * c;
                            p.Squash = -0.2f * EaseOut(c);   // the landing: knees all the way, the body squashed
                        }
                        else
                        {
                            var c = (k - 0.7f) / 0.3f;
                            p.RaiseL = p.RaiseR = Mathf.Lerp(-10f, -36f, c); p.SwingL = p.SwingR = Mathf.Lerp(70f, 3f, c); p.ElbowL = p.ElbowR = Mathf.Lerp(20f, 14f, c);
                            p.FistL = p.FistR = Mathf.Lerp(1f, 0.25f, c); p.Lean = 28f * (1f - c); p.KneeL = p.KneeR = 68f * (1f - EaseOut(c)); p.HeadPitch = 10f * (1f - c);
                            p.Squash = -0.2f * (1f - EaseOut(c)) + 0.05f * Mathf.Sin(c * Mathf.PI);
                        }
                        break;
                    }
                case 1:
                    {
                        var up = k < 0.35f ? Mathf.SmoothStep(0f, 1f, k / 0.35f) : 1f;
                        var spin = k > 0.3f && k < 0.75f ? Mathf.SmoothStep(0f, 1f, (k - 0.3f) / 0.45f) : k >= 0.75f ? 1f : 0f;
                        var hurl = k > 0.75f ? Mathf.Sin(Mathf.Clamp01((k - 0.75f) / 0.25f) * Mathf.PI) : 0f;
                        p.RaiseL = p.RaiseR = Mathf.Lerp(-36f, 86f, up) - hurl * 60f; p.SwingL = p.SwingR = Mathf.Lerp(3f, -14f, up) + hurl * 90f;
                        p.ElbowL = p.ElbowR = Mathf.Lerp(14f, 25f, up) - hurl * 15f; p.FistL = p.FistR = 0.6f;
                        p.Yaw = spin * 360f; p.Y = Mathf.Sin(spin * Mathf.PI) * 0.3f + hurl * 0.08f; p.Lean = -6f * up + hurl * 22f;
                        p.KneeL = p.KneeR = Mathf.Sin(spin * Mathf.PI) * 30f + hurl * 20f; p.HeadPitch = -8f * up + hurl * 12f;
                        break;
                    }
                case 2:
                    {
                        p.Expr = "smile";
                        var up = k < 0.3f ? Mathf.SmoothStep(0f, 1f, k / 0.3f) : k > 0.8f ? 1f - Mathf.SmoothStep(0f, 1f, (k - 0.8f) / 0.2f) : 1f;
                        var hop = k > 0.3f && k < 0.8f ? Mathf.Sin((k - 0.3f) / 0.5f * Mathf.PI) : 0f;
                        p.RaiseR = Mathf.Lerp(-36f, 84f, up); p.SwingR = Mathf.Lerp(3f, -12f, up); p.ElbowR = 16f; p.HandFlexR = -10f; p.FistR = 0.7f;
                        p.RaiseL = Mathf.Lerp(-36f, 40f, up); p.SwingL = Mathf.Lerp(3f, -20f, up); p.ElbowL = 20f; p.HandFlexL = -20f; p.HandDevL = 20f; p.FistL = 0f;
                        p.Y = hop * 0.35f; p.Squash = hop > 0f ? (0.5f - Mathf.Abs(hop - 0.5f)) * 0.2f - 0.02f : 0f; p.KneeL = p.KneeR = hop * 45f; p.ToeL = p.ToeR = hop * 25f; p.HeadPitch = -10f * up; p.HeadRoll = 8f * up;
                        break;
                    }
            }
            return p;
        }

        /// <summary>k 0 → 1: the knees buckle, the body folds and falls forward, bounces once and lies.</summary>
        public static Pose Dead(float k)
        {
            var p = Pose.Rest; p.Expr = k > 0.3f ? "dizzy" : "hurt";   // the feet stay planted: the body folds forward over them (MotionTest: a hand-set drop sank them 4 cm)      // spiral eyes once down, the knocked-out face
            var buckle = EaseOut(k / 0.3f);
            var fall = k < 0.3f ? 0f : EaseIn((k - 0.3f) / 0.5f);
            var bounce = k > 0.8f ? Mathf.Sin(Mathf.Clamp01((k - 0.8f) / 0.2f) * Mathf.PI) * (1f - (k - 0.8f) / 0.2f) : 0f;
            p.KneeL = 20f + 40f * buckle; p.KneeR = 25f + 35f * buckle;
            p.RaiseL = Mathf.Lerp(-36f, -10f, buckle) + fall * 20f; p.RaiseR = Mathf.Lerp(-36f, -14f, buckle) + fall * 16f;
            p.SwingL = 10f * buckle + fall * 30f; p.SwingR = 8f * buckle + fall * 34f; p.ElbowL = 14f + 40f * buckle; p.ElbowR = 14f + 30f * buckle;
            p.Lean = 10f * buckle + 74f * fall + bounce * 6f; p.SpineBend = 6f * buckle + 10f * fall;
            p.HeadPitch = -8f * buckle + 14f * fall; p.HeadTilt = 6f * fall; p.ShrugL = p.ShrugR = 8f * buckle;
            p.Step = 0.05f * fall;
            return p;
        }

        /// <summary>
        /// Curls the fingers (thumb0, thumb1, index0, index1, middle0, middle1): 0 open, 1 a fist.
        /// Measured (SD_HANDDUMP): on the hand bone the fingers run along −X and the palm faces +Y;
        /// a finger joint curls towards the palm about its own −Z (index and middle alike). The
        /// thumb opposes about −Z (across the palm) with a little +Y (into it). The old version
        /// turned the fingers about the HAND's Y, which swept them sideways instead of closing them.
        /// </summary>
        static void Fingers(ChibiRig rig, Transform[] f, float fist)
        {
            if (f == null) return;
            for (var i = 0; i < f.Length; i++)
            {
                if (f[i] == null) continue;
                var q = i switch
                {
                    0 => Quaternion.Euler(0f, 18f * fist, -38f * fist),                 // thumb base: across and in
                    1 => Quaternion.Euler(0f, 0f, -30f * fist),                         // thumb tip
                    2 or 4 => Quaternion.Euler(0f, 0f, Mathf.Lerp(8f, -80f, fist)),    // knuckles: a little back when open
                    _ => Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, -85f, fist)),          // middle joints
                };
                rig.Pose(f[i], q);
            }
        }

        /// <summary>Puts a pose on the sample rig (offsets on the rest rotations).</summary>
        public static void Apply(ChibiRig rig, in Pose p)
        {
            // the trunk: 40 % of the lean at the hips (Pelvis −Z forward), 60 % at the waist (Spine1 +Z forward)
            rig.Pose(rig.Body, Quaternion.Euler(0f, 0f, -p.Lean * 0.4f) * Quaternion.Euler(p.Twist, 0f, 0f));
            if (rig.Spine != null) rig.Pose(rig.Spine, Quaternion.Euler(0f, 0f, p.Lean * 0.6f + p.SpineBend) * Quaternion.Euler(p.SpineTwist, p.SpineSide, 0f));
            // the head (measured): +X yaw, +Y tilt, +Z chin down
            rig.Pose(rig.Head, Quaternion.Euler(0f, 0f, p.HeadPitch) * Quaternion.Euler(p.HeadYaw, p.HeadTilt, 0f));
            // the clavicles (measured on the right): +Z shrug up, −Y reach forward; the left mirrors
            if (rig.ClavR != null) rig.Pose(rig.ClavR, Quaternion.Euler(0f, -p.ReachR, p.ShrugR));
            if (rig.ClavL != null) rig.Pose(rig.ClavL, Quaternion.Euler(0f, p.ReachL, -p.ShrugL));
            rig.Pose(rig.ArmR, Quaternion.Euler(0f, -p.RaiseR, p.SwingR));
            // NOT mirrored on the swing (measured, SD_POSE=set:SwingL=…): −SwingL here swung the left
            // arm BACK, so every pose's left hand went behind the body and the walk swung both arms together
            rig.Pose(rig.ArmL, Quaternion.Euler(0f, p.RaiseL, p.SwingL));
            rig.Pose(rig.ForearmR, Quaternion.Euler(0f, p.InR, p.ElbowR));
            rig.Pose(rig.ForearmL, Quaternion.Euler(0f, -p.InL, p.ElbowL));
            rig.Pose(rig.LegL, Quaternion.Euler(0f, 0f, -p.ThighL));
            // NOT mirrored, like the knees (SdMotionTest.DumpWalk): with +ThighR here the right foot
            // swung back while lifted and slid forward while planted — every walk was half a moonwalk
            rig.Pose(rig.LegR, Quaternion.Euler(0f, 0f, -p.ThighR));
            rig.Pose(rig.CalfL, Quaternion.Euler(0f, 0f, p.KneeL));
            rig.Pose(rig.CalfR, Quaternion.Euler(0f, 0f, p.KneeR));
            if (rig.HandR != null) rig.Pose(rig.HandR, Quaternion.Euler(0f, p.HandFlexR, p.HandDevR));
            if (rig.HandL != null) rig.Pose(rig.HandL, Quaternion.Euler(0f, p.HandFlexL, p.HandDevL));   // measured: the hands are NOT mirrored
            // a hand holding a prop keeps the prop's grip whatever the pose asks of it
            Fingers(rig, rig.FingersR, rig.GripR >= 0f ? rig.GripR : p.FistR); Fingers(rig, rig.FingersL, p.FistL);
            // plant the feet: the lowest foot on the floor (unless the pose is Free); p.Y then means
            // "how far the feet are off the floor", so a jump is still a jump and a crouch is just
            // bent knees. Read by the caller as rig.FootDrop, in root units.
            if (rig.FootL != null && rig.FootR != null)
            {
                if (float.IsNaN(rig.RestFootY)) rig.RestFootY = float.MinValue;   // set by SdRef.Build; unknown = no planting
                var low = Mathf.Min(rig.Root.InverseTransformPoint(rig.FootL.position).y, rig.Root.InverseTransformPoint(rig.FootR.position).y);
                rig.FootDrop = p.Free || rig.RestFootY == float.MinValue ? 0f : rig.RestFootY - low;
            }
            // squash & stretch on the whole model, volume kept, feet on the floor (its local position
            // scales with it: the model was placed so the feet land at 0 at its rest scale)
            if (rig.Model != null)
            {
                var k = 1f + Mathf.Clamp(p.Squash, -0.35f, 0.35f); var side = 1f / Mathf.Sqrt(k);
                rig.Model.localScale = new Vector3(rig.ModelScale.x * side, rig.ModelScale.y * k, rig.ModelScale.z * side);
                rig.Model.localPosition = new Vector3(rig.ModelPos.x * side, rig.ModelPos.y * k, rig.ModelPos.z * side);
            }
            if (rig.FootR != null) rig.Pose(rig.FootR, Quaternion.Euler(0f, 0f, p.ToeR));
            if (rig.FootL != null) rig.Pose(rig.FootL, Quaternion.Euler(0f, 0f, p.ToeL));
            // weight: the figure over its stance leg, and the hips rocking (roll about the pelvis's forward)
            if (rig.Pelvis != null)
            {
                var worldOff = rig.Root.rotation * new Vector3(p.Sway, 0f, 0f);
                rig.Pelvis.localPosition = rig.PelvisRest + rig.Pelvis.parent.InverseTransformVector(worldOff);
                if (p.HipRoll != 0f) rig.Pose(rig.Body, Quaternion.Euler(0f, 0f, -p.Lean * 0.4f) * Quaternion.Euler(p.Twist, p.HipRoll, 0f));
            }
        }
    }
}
