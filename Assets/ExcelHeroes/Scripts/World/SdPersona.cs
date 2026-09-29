using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// Who a character is, in the body. The pose library alone gave everyone the same bearing — the
    /// idle and the victory picked by a hash, the same trunk, the same feet — and a squad read as
    /// clones in different clothes (the user). A persona is a bearing kept through every state:
    /// a posture (chest out or slouched, feet planted wide or together, the head up, down or tilted),
    /// the idles and victories that person would do, and a tempo — an energetic one fidgets fast,
    /// a lazy one barely moves. The archetype is chosen per character from the bio and lines
    /// (tools/sdspec_persona_gemini.py → sdspec persona), balanced across the cast.
    /// </summary>
    public readonly struct SdPersona
    {
        public readonly string Name;
        public readonly float Lean, SpineBend, HeadPitch, HeadTilt, Spread, Shrug, HipRoll, Twist, In, Tempo;
        public readonly int[] Idles, Wins;

        SdPersona(string name, float lean, float bend, float pitch, float tilt, float spread, float shrug, float hip, float twist, float inw, float tempo, int[] idles, int[] wins)
        {
            Name = name; Lean = lean; SpineBend = bend; HeadPitch = pitch; HeadTilt = tilt; Spread = spread; Shrug = shrug; HipRoll = hip; Twist = twist; In = inw; Tempo = tempo;
            Idles = idles; Wins = wins;
        }

        // Idle: 0 stand · 1 hands on hips · 2 arms crossed · 3 chin · 4 relaxed lean · 5 hand at shoulder · 6 glasses ·
        // 7 stretch · 8 hands behind the back · 9 hands in pockets · 10 hands clasped in front · 11 phone · 12 on the toes · 13 yawn
        // Win: 0 arms up, hopping · 1 fist pump · 2 V by the cheek · 3 bow · 4 wave · 5 spin · 6 double V · 7 clap ·
        // 8 thumbs up · 9 salute · 10 point at the lens · 11 hand on the heart, a nod
        static readonly Dictionary<string, SdPersona> Table = new()
        {
            //                                   lean  bend pitch tilt  spread shrug hip  twist  in   tempo  idles                  wins
            ["confident"] = new("confident",    -3f,  -3f, -3f,   0f,   4f,   0f,   2f,   0f,   0f,  0.95f, new[] { 1, 2, 9 },    new[] { 1, 8, 10 }),
            ["elegant"]   = new("elegant",       0f,  -1f,  1f,   4f,  -3f,   0f,   3f,   0f,   4f,  0.85f, new[] { 10, 0, 6 },   new[] { 3, 2, 11 }),
            ["shy"]       = new("shy",           3f,   2f,  6f,   3f,  -4f,   5f,   0f,   0f,   8f,  0.9f,  new[] { 10, 3 },      new[] { 4, 7 }),
            ["energetic"] = new("energetic",     1f,  -1f, -2f,   0f,   3f,   0f,   0f,   0f,   0f,  1.35f, new[] { 12, 0, 5 },   new[] { 0, 5, 6 }),
            ["lazy"]      = new("lazy",          5f,   4f,  3f,   6f,   2f,  -3f,   4f,   0f,   0f,  0.65f, new[] { 13, 4, 9 },   new[] { 4, 8 }),
            ["stern"]     = new("stern",        -1f,  -1f, -1f,   0f,   4f,   0f,   0f,   0f,   0f,  0.8f,  new[] { 8, 2 },       new[] { 9, 3, 11 }),
            ["nerdy"]     = new("nerdy",         3f,   2f,  4f,   2f,   0f,   2f,   0f,   0f,   4f,  1.0f,  new[] { 6, 3, 11 },   new[] { 2, 10 }),
            ["cool"]      = new("cool",          0f,  -1f, -2f,  -4f,   3f,   0f,   5f,   6f,   0f,  0.8f,  new[] { 9, 4 },       new[] { 8, 10 }),
            ["cheerful"]  = new("cheerful",      0f,  -1f, -1f,   6f,   1f,   2f,   2f,   0f,   0f,  1.15f, new[] { 0, 5, 12 },   new[] { 6, 2, 4 }),
            ["caring"]    = new("caring",        1f,   0f,  2f,   5f,  -2f,   0f,   2f,   0f,   4f,  0.9f,  new[] { 10, 1 },      new[] { 7, 4 }),
            ["executive"] = new("executive",    -4f,  -4f, -4f,   0f,   3f,   0f,   0f,   0f,   0f,  0.75f, new[] { 8, 2 },       new[] { 11, 3, 9 }),
            ["playful"]   = new("playful",       0f,   0f,  0f,   7f,   1f,   0f,   6f,  -4f,   0f,  1.1f,  new[] { 5, 11, 1 },   new[] { 2, 6, 10 }),
        };

        public static IEnumerable<string> Names => Table.Keys;

        /// <summary>The character's persona; a neutral one when the spec names none.</summary>
        public static SdPersona For(string id)
        {
            var s = SdSpec.For(id)?.persona;
            return !string.IsNullOrEmpty(s) && Table.TryGetValue(s, out var p) ? p : Neutral;
        }

        static readonly SdPersona Neutral = new("", 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, null, null);

        public bool Has => Idles != null;

        /// <summary>
        /// The bearing laid over a pose, by how much the state allows it (1 standing about, ~0.6 in a
        /// guard, ~0.4 running or celebrating, 0 mid-blow).
        /// </summary>
        public void Shape(ref Pose p, float w)
        {
            if (!Has || w <= 0f) return;
            p.Lean += Lean * w; p.SpineBend += SpineBend * w; p.HeadPitch += HeadPitch * w; p.HeadTilt += HeadTilt * w;
            p.Spread += Spread * w; p.ShrugL += Shrug * w; p.ShrugR += Shrug * w; p.HipRoll += HipRoll * w; p.Twist += Twist * w;
            p.InL += In * w; p.InR += In * w;
        }
    }
}
