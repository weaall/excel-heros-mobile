using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// Every sound in the game is synthesised at startup — there are no audio files.
    ///
    /// That is a deliberate choice rather than a shortcut. The web build does the same, the whole
    /// palette is a few hundred lines, it adds nothing to the download, and a UI sound that is a
    /// clean 40ms envelope beats a licensed sample that does not fit. Where it would NOT hold up is
    /// music: there is no BGM here, and a real soundtrack is a licensing job, not a synthesis one.
    ///
    /// The reveal sounds carry the most weight. Rarity is a rising arpeggio — one note for D up to a
    /// full chord with shimmer for S — so a player hears what they pulled before the card resolves.
    /// </summary>
    public static class AudioService
    {
        const int SampleRate = 44100;

        static AudioSource _source;
        static readonly Dictionary<string, AudioClip> Cache = new();

        public static bool Muted { get; set; }
        public static float Volume { get; set; } = 0.7f;

        /// <summary>Called once by AppRoot. Safe to call again — it no-ops.</summary>
        public static void Init(GameObject host)
        {
            if (_source != null) return;
            _source = host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;   // UI audio, never positional
        }

        public static void Play(string id, float gain = 1f)
        {
            if (Muted || _source == null) return;
            var clip = Get(id);
            if (clip != null) _source.PlayOneShot(clip, Mathf.Clamp01(Volume * gain));
        }

        static AudioClip Get(string id)
        {
            if (Cache.TryGetValue(id, out var c)) return c;
            c = Build(id);
            Cache[id] = c;
            return c;
        }

        // --- the palette ---------------------------------------------------------------------

        // An octave of a pentatonic-ish scale. Using fixed intervals rather than arbitrary
        // frequencies is what keeps the whole set sounding like one instrument.
        const float C5 = 523.25f, D5 = 587.33f, E5 = 659.25f, G5 = 783.99f, A5 = 880.00f;
        const float C6 = 1046.50f, E6 = 1318.51f, G6 = 1567.98f, C7 = 2093.00f;

        static AudioClip Build(string id) => id switch
        {
            // 또로롱 — three quick rising pings rather than the two-note 또롱 this had. A UI
            // click wants to be a flick, so the decay is more than twice as fast as a note in a
            // reveal chord: each ping is gone before the next one lands.
            // 퐁 — one short rounded pop whose pitch drops as it sounds, the reference game's
            // button click. The three-note 또로롱 this replaced was pretty and too long: pressing
            // five buttons in a row played a tune.
            "tap"      => Pop(0.075f, 1480f, 980f, 0.55f),
            // The same figure a fourth lower and a touch longer, so moving between sheets reads as
            // a bigger gesture than pressing a button without sounding like a different instrument.
            // Moving between screens: the same pop a step lower with a second one above it — a
            // bigger gesture in the same voice.
            "nav"      => Pops((0f, 1180f, 820f, 0.5f), (0.055f, 1580f, 1150f, 0.42f)),
            // Back and close: the pair falling instead of rising.
            "back"     => Pops((0f, 1500f, 1100f, 0.45f), (0.05f, 1050f, 720f, 0.45f)),
            // Confirm and collect: three pops climbing.
            "confirm"  => Pops((0f, 1100f, 900f, 0.45f), (0.05f, 1400f, 1150f, 0.45f), (0.1f, 1800f, 1500f, 0.42f)),
            "pull"     => Sweep(0.45f, 180f, 900f, 0.4f),          // the summon winding up
            "reveal_D" => Notes(0.30f, (C5, 0f, 0.5f)),
            "reveal_C" => Notes(0.40f, (C5, 0f, 0.45f), (E5, 0.08f, 0.45f)),
            "reveal_B" => Notes(0.55f, (C5, 0f, 0.4f), (E5, 0.08f, 0.4f), (G5, 0.16f, 0.45f)),
            "reveal_A" => Notes(0.80f, (C5, 0f, 0.4f), (E5, 0.07f, 0.4f), (G5, 0.14f, 0.4f),
                                       (C6, 0.21f, 0.5f), (E6, 0.30f, 0.35f)),
            // S gets the full ladder plus a high shimmer that keeps ringing after the rest decays.
            "reveal_S" => Notes(1.40f, (C5, 0f, 0.4f), (E5, 0.06f, 0.4f), (G5, 0.12f, 0.4f),
                                       (C6, 0.18f, 0.5f), (E6, 0.26f, 0.45f), (G6, 0.34f, 0.45f),
                                       (C7, 0.44f, 0.5f), (E6, 0.62f, 0.25f), (C7, 0.78f, 0.3f)),
            "promote"  => Notes(0.6f, (G5, 0f, 0.4f), (C6, 0.09f, 0.45f), (E6, 0.18f, 0.4f)),
            "spark"    => Notes(0.7f, (A5, 0f, 0.4f), (C6, 0.1f, 0.4f), (E6, 0.2f, 0.4f), (A5, 0.34f, 0.3f)),
            "skill"    => Sweep(0.22f, 700f, 1500f, 0.3f),
            "hit"      => Noise(0.05f, 0.22f, 2600f),
            "crit"     => Noise(0.09f, 0.3f, 1500f),
            "heal"     => Notes(0.35f, (E5, 0f, 0.3f), (A5, 0.09f, 0.3f)),
            "victory"  => Notes(1.0f, (C5, 0f, 0.4f), (E5, 0.1f, 0.4f), (G5, 0.2f, 0.4f), (C6, 0.3f, 0.5f)),
            "defeat"   => Notes(0.9f, (E5, 0f, 0.4f), (D5, 0.18f, 0.38f), (C5, 0.36f, 0.42f)),
            "bond"     => Notes(0.5f, (A5, 0f, 0.35f), (C6, 0.1f, 0.35f), (E6, 0.22f, 0.3f)),
            _ => null,
        };

        public static string RevealId(string grade) => $"reveal_{grade}";

        // --- synthesis -----------------------------------------------------------------------

        /// <summary>
        /// Additive notes on one buffer. Each note is (frequency, start seconds, gain) and gets its
        /// own percussive envelope, so overlapping notes ring together like a struck instrument
        /// rather than stepping on each other.
        /// </summary>
        static AudioClip Notes(float seconds, params (float freq, float at, float gain)[] notes)
            => Notes(seconds, 4.2f, notes);

        /// <summary>
        /// As above, but with the decay rate exposed. UI clicks need a much faster one than the
        /// reveal chords: at 4.2 a note is still at half volume after 160ms, which is long enough
        /// for three of them to blur into one chord instead of reading as three taps.
        /// </summary>
        static AudioClip Notes(float seconds, float decay, params (float freq, float at, float gain)[] notes)
        {
            var n = Mathf.CeilToInt(SampleRate * seconds);
            var data = new float[n];

            foreach (var (freq, at, gain) in notes)
            {
                var start = Mathf.Clamp(Mathf.RoundToInt(at * SampleRate), 0, n - 1);
                var life = n - start;
                for (var i = 0; i < life; i++)
                {
                    var t = i / (float)SampleRate;
                    // Fast attack, exponential decay — the shape of anything plucked or struck.
                    var env = Mathf.Min(1f, t / 0.006f) * Mathf.Exp(-t * decay);
                    var phase = 2f * Mathf.PI * freq * t;
                    // A little second harmonic gives it body without turning into a square wave.
                    var s = Mathf.Sin(phase) + 0.25f * Mathf.Sin(phase * 2f);
                    data[start + i] += s * env * gain * 0.42f;
                }
            }

            return Finish($"n{seconds}{decay}{notes.Length}{notes[0].freq}", data);
        }

        /// <summary>
        /// A pop: a sine whose pitch glides down fast from `from` to `to`, with a 2ms attack and a
        /// quick exponential tail. The glide is what makes it read as a soft plastic "pon" rather
        /// than a beep — a fixed pitch at this length is a beep.
        /// </summary>
        static AudioClip Pop(float seconds, float from, float to, float gain)
            => Pops((0f, from, to, gain));

        static AudioClip Pops(params (float at, float from, float to, float gain)[] pops)
        {
            const float each = 0.08f;
            var total = 0f;
            foreach (var p in pops) total = Mathf.Max(total, p.at + each);
            var n = Mathf.CeilToInt(SampleRate * total);
            var data = new float[n];
            foreach (var (at, from, to, gain) in pops)
            {
                var start = Mathf.Clamp(Mathf.RoundToInt(at * SampleRate), 0, n - 1);
                var len = Mathf.Min(n - start, Mathf.CeilToInt(SampleRate * each));
                var phase = 0f;
                for (var i = 0; i < len; i++)
                {
                    var t = i / (float)SampleRate;
                    var freq = to + (from - to) * Mathf.Exp(-t * 60f);
                    phase += 2f * Mathf.PI * freq / SampleRate;
                    var env = Mathf.Min(1f, t / 0.002f) * Mathf.Exp(-t * 55f);
                    data[start + i] += (Mathf.Sin(phase) + 0.18f * Mathf.Sin(phase * 2f)) * env * gain;
                }
            }
            return Finish($"p{pops.Length}{pops[0].from}{pops[0].to}{total}", data);
        }

        /// <summary>A pitch sweep — the sound of something charging or being released.</summary>
        static AudioClip Sweep(float seconds, float from, float to, float gain)
        {
            var n = Mathf.CeilToInt(SampleRate * seconds);
            var data = new float[n];
            var phase = 0f;

            for (var i = 0; i < n; i++)
            {
                var u = i / (float)n;
                var freq = Mathf.Lerp(from, to, u * u);          // accelerating, so it feels like it lifts
                phase += 2f * Mathf.PI * freq / SampleRate;
                var env = Mathf.Min(1f, i / (SampleRate * 0.01f)) * (1f - u);
                data[i] = Mathf.Sin(phase) * env * gain;
            }

            return Finish($"s{seconds}{from}{to}", data);
        }

        /// <summary>Filtered noise for impacts. The low-pass is what stops it sounding like static.</summary>
        static AudioClip Noise(float seconds, float gain, float cutoff)
        {
            var n = Mathf.CeilToInt(SampleRate * seconds);
            var data = new float[n];
            var rng = new System.Random(12345);
            var last = 0f;
            var k = Mathf.Clamp01(cutoff / (SampleRate * 0.5f));

            for (var i = 0; i < n; i++)
            {
                var white = (float)(rng.NextDouble() * 2.0 - 1.0);
                last += k * (white - last);                       // one-pole low pass
                var t = i / (float)SampleRate;
                data[i] = last * Mathf.Exp(-t / Mathf.Max(0.005f, seconds * 0.35f)) * gain;
            }

            return Finish($"z{seconds}{cutoff}", data);
        }

        static AudioClip Finish(string name, float[] data)
        {
            // Normalise to a predictable ceiling so one sound never jumps out over the others, and
            // fade the tail so the buffer never ends on a non-zero sample (which clicks).
            var peak = 0f;
            foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 0f)
            {
                var scale = 0.85f / peak;
                for (var i = 0; i < data.Length; i++) data[i] *= scale;
            }

            var fade = Mathf.Min(400, data.Length);
            for (var i = 0; i < fade; i++)
                data[data.Length - fade + i] *= 1f - i / (float)fade;

            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
