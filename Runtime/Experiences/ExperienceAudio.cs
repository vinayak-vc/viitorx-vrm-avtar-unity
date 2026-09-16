using UnityEngine;

using VirtualMirror.SkeletonShow;

namespace VirtualMirror.Experiences {
    /// <summary>The discrete sounds an experience can ask for. Deliberately named for what HAPPENED,
    /// not for what it sounds like — an experience should say "a thing was struck", and this class
    /// decides what that is, so the whole set can be retuned in one place.</summary>
    public enum SoundCue {
        Pop,
        Bloom,
        Hit,
        Match,
        Grab,
        Release,
        Arrive,
        Depart,
    }

    /// <summary>
    /// F-31 — SOUND, as a layer every experience shares rather than an experience of its own.
    ///
    /// WHY THIS IS NOT A SCENE. F-28 §7.4 listed "no audio" as missing when there were three modes.
    /// There are now eight scenes and still silence, and an installation without sound is half an
    /// installation — people feel the absence before they can name it. Every hook this needs already
    /// exists in the other experiences: a bubble pops, a footprint blooms, an object is struck, a
    /// pose is matched. They simply made no noise. Adding a ninth scene would have been a far worse
    /// return than giving the eight that exist a voice.
    ///
    /// EVERY SOUND IS SYNTHESISED AT RUNTIME. No .wav assets, no import settings, no licence
    /// questions, and the scene stays a camera and one GameObject — the same rule the visuals follow.
    /// Clips are built once in <see cref="Build"/>; nothing allocates per frame.
    ///
    /// THE ONE DECISION THAT MAKES IT LISTENABLE: every pitch is drawn from a PENTATONIC scale. A
    /// person moving is an effectively random trigger source, and random semitones sound like a
    /// mistake within about four notes. A pentatonic scale has no interval that clashes, so any
    /// combination the person happens to play is consonant — an hour of an installation being hammered
    /// by strangers still sounds intentional. This is the difference between an exhibit people stay
    /// at and one the staff mute by lunchtime.
    ///
    /// THE DRONE IS THE BODY, THE CUES ARE THE EVENTS. A continuous pad tracks whole-body energy, so
    /// the room responds to a person simply BEING there and moving, before they have triggered
    /// anything. Cues sit on top for things that actually happened. Without the drone the space is
    /// silent between events, which reads as broken rather than as quiet.
    /// </summary>
    public sealed class ExperienceAudio {
        private const int SampleRate = 44100;

        /// <summary>Root of the pentatonic set. A2-ish: low enough that the drone sits under
        /// everything without masking the cues, which live two octaves up.</summary>
        private const float RootHz = 110f;

        /// <summary>Major pentatonic degrees, in semitones. No interval in this set clashes with any
        /// other, which is the whole point — see the class note.</summary>
        private static readonly int[] Pentatonic = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21 };

        private AudioSource droneSource;
        private AudioSource whooshSource;
        private AudioSource cueSource;

        private AudioClip droneClip;
        private AudioClip whooshClip;
        private AudioClip[] cueClips;

        private float droneLevel;
        private float whooshLevel;
        private int cueIndex;

        /// <summary>Master volume. Zero silences everything without changing any other behaviour, so
        /// an operator can mute a noisy room without stopping the experience.</summary>
        public float Volume = 1f;

        public bool Enabled = true;

        /// <summary>Build the sources and synthesise every clip. Call once, from an experience's
        /// build step.</summary>
        public void Build(Transform parent) {
            GameObject root = new GameObject("Audio");
            root.transform.SetParent(parent, false);

            // An AudioListener must exist or nothing is audible. The scene template carries one on
            // the camera, but ShowStage will happily create a bare camera if none was there, so this
            // cannot be assumed - and a silent installation with no error is the worst way to find out.
            if (Object.FindObjectOfType<AudioListener>() == null) {
                Camera main = Camera.main;
                if (main != null) {
                    main.gameObject.AddComponent<AudioListener>();
                } else {
                    root.AddComponent<AudioListener>();
                }
            }

            droneClip = BuildDrone();
            whooshClip = BuildWhoosh();
            cueClips = new AudioClip[8];
            cueClips[(int)SoundCue.Pop] = BuildPop();
            cueClips[(int)SoundCue.Bloom] = BuildBloom();
            cueClips[(int)SoundCue.Hit] = BuildHit();
            cueClips[(int)SoundCue.Match] = BuildMatch();
            cueClips[(int)SoundCue.Grab] = BuildGrab();
            cueClips[(int)SoundCue.Release] = BuildRelease();
            cueClips[(int)SoundCue.Arrive] = BuildArrive();
            cueClips[(int)SoundCue.Depart] = BuildDepart();

            droneSource = MakeSource(root.transform, "drone", droneClip, true, 0f);
            whooshSource = MakeSource(root.transform, "whoosh", whooshClip, true, 0f);
            cueSource = MakeSource(root.transform, "cues", null, false, 1f);
            droneSource.Play();
            whooshSource.Play();
        }

        private static AudioSource MakeSource(Transform parent, string name, AudioClip clip,
                                              bool loop, float volume) {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.volume = volume;
            source.playOnAwake = false;
            // 2D: the sound belongs to the room, not to a point in it. Spatialising a drone would
            // make it swing across the speakers as the camera or the body moved, which is a
            // distraction in an installation where the screen is the subject.
            source.spatialBlend = 0f;
            return source;
        }

        /// <summary>
        /// Per-frame. Drives the continuous layers from the body; cues are separate and event-driven.
        /// Safe to call when nothing is tracked — everything fades out rather than cutting.
        /// </summary>
        public void Tick(SkeletonPose pose, float deltaSeconds) {
            if (droneSource == null) {
                return;
            }
            float energy = (pose != null && pose.Valid) ? pose.Energy : 0f;
            float fastest = 0f;
            if (pose != null && pose.Valid) {
                int i = 0;
                while (i < SkeletonPose.Extremities.Length) {
                    int joint = SkeletonPose.Extremities[i];
                    if (pose.Present[joint] && pose.Speed[joint] > fastest) {
                        fastest = pose.Speed[joint];
                    }
                    i = i + 1;
                }
            }

            // Both layers are smoothed hard. Audio amplitude reacts far more harshly to a step than
            // a colour does - an un-smoothed gain follows the tracker's own jitter and the result is
            // a crackle, not a swell.
            float k = 1f - Mathf.Exp(-deltaSeconds / 0.35f);
            droneLevel = droneLevel + k * (Mathf.Clamp01(0.12f + energy * 0.75f) - droneLevel);
            float whooshTarget = Mathf.Clamp01((fastest - 0.6f) / 3.0f);
            whooshLevel = whooshLevel + (1f - Mathf.Exp(-deltaSeconds / 0.12f)) * (whooshTarget - whooshLevel);

            float master = Enabled ? Mathf.Clamp01(Volume) : 0f;
            droneSource.volume = droneLevel * 0.30f * master;
            // A small pitch lift with energy, well under a semitone. Enough that the room feels like
            // it is leaning in; more than this and it reads as a tape speeding up.
            droneSource.pitch = 1f + energy * 0.04f;
            whooshSource.volume = whooshLevel * 0.22f * master;
            whooshSource.pitch = 0.8f + whooshLevel * 0.6f;
        }

        /// <summary>
        /// Fire one event sound. <paramref name="step"/> picks a degree of the pentatonic scale, so a
        /// caller can make a rising run (a combo, petals opening) by counting up — and cannot produce
        /// a wrong note whatever it passes.
        /// </summary>
        public void Play(SoundCue cue, int step = 0, float volume = 1f) {
            if (!Enabled || cueSource == null || cueClips == null) {
                return;
            }
            AudioClip clip = cueClips[(int)cue];
            if (clip == null) {
                return;
            }
            int degree = Mathf.Abs(step) % Pentatonic.Length;
            cueSource.pitch = Mathf.Pow(2f, Pentatonic[degree] / 12f);
            cueSource.PlayOneShot(clip, Mathf.Clamp01(volume) * Mathf.Clamp01(Volume));
            cueIndex = cueIndex + 1;
        }

        // ---------------------------------------------------------------- synthesis

        // A seamless loop needs a WHOLE number of cycles in the buffer, or the wrap produces a click
        // every time round. This returns the nearest frequency to `target` that fits exactly.
        private static float LoopableHz(float target, int samples) {
            float cycles = Mathf.Max(1f, Mathf.Round(target * samples / SampleRate));
            return cycles * SampleRate / samples;
        }

        private static AudioClip FromSamples(string name, float[] data) {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // A slow pad: root, fifth and octave, each slightly detuned against a twin so the sound
        // breathes instead of sitting perfectly still. Four seconds, so the loop is not recognisable.
        private AudioClip BuildDrone() {
            int samples = SampleRate * 4;
            float[] data = new float[samples];
            float[] partials = {
                LoopableHz(RootHz, samples),
                LoopableHz(RootHz * 1.5f, samples),
                LoopableHz(RootHz * 2f, samples),
                LoopableHz(RootHz * 3f, samples),
            };
            float[] gains = { 0.50f, 0.26f, 0.16f, 0.06f };
            int p = 0;
            while (p < partials.Length) {
                float w = 2f * Mathf.PI * partials[p] / SampleRate;
                // The detune twin is one loopable step away, which beats slowly against its partner.
                float wDetune = 2f * Mathf.PI * LoopableHz(partials[p] + 0.5f, samples) / SampleRate;
                int i = 0;
                while (i < samples) {
                    data[i] = data[i] + gains[p] * 0.5f * (Mathf.Sin(w * i) + Mathf.Sin(wDetune * i));
                    i = i + 1;
                }
                p = p + 1;
            }
            Normalise(data, 0.55f);
            return FromSamples("drone", data);
        }

        // Filtered noise for movement. A one-pole low-pass takes the hiss off white noise and leaves
        // something that reads as air being moved rather than as static.
        private AudioClip BuildWhoosh() {
            int samples = SampleRate * 2;
            float[] data = new float[samples];
            float last = 0f;
            Random.State state = Random.state;
            Random.InitState(20260916);
            int i = 0;
            while (i < samples) {
                float noise = Random.Range(-1f, 1f);
                last = last + 0.06f * (noise - last);
                data[i] = last;
                i = i + 1;
            }
            Random.state = state;
            // Cross-fade the tail into the head so the loop point is inaudible.
            int fade = SampleRate / 10;
            i = 0;
            while (i < fade) {
                float t = i / (float)fade;
                data[i] = Mathf.Lerp(data[samples - fade + i], data[i], t);
                i = i + 1;
            }
            Normalise(data, 0.8f);
            return FromSamples("whoosh", data);
        }

        // Short, bright, fast decay, with the pitch falling a little as it dies - which is what makes
        // it read as a "pop" rather than a "beep".
        private AudioClip BuildPop() {
            int samples = SampleRate / 8;
            float[] data = new float[samples];
            float phase = 0f;
            int i = 0;
            while (i < samples) {
                float t = i / (float)samples;
                float freq = 880f * (1f - 0.35f * t);
                phase = phase + 2f * Mathf.PI * freq / SampleRate;
                data[i] = Mathf.Sin(phase) * Mathf.Exp(-9f * t);
                i = i + 1;
            }
            return FromSamples("pop", data);
        }

        // Slow attack, slow release, rising. For things that GROW rather than happen.
        private AudioClip BuildBloom() {
            int samples = (int)(SampleRate * 0.9f);
            float[] data = new float[samples];
            float phase = 0f;
            int i = 0;
            while (i < samples) {
                float t = i / (float)samples;
                float freq = 330f * (1f + 0.5f * t);
                phase = phase + 2f * Mathf.PI * freq / SampleRate;
                float env = Mathf.Sin(Mathf.PI * t);   // soft in, soft out - no click at either end
                data[i] = (Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 2f) * 0.3f) * env;
                i = i + 1;
            }
            return FromSamples("bloom", data);
        }

        // A thump plus a noise transient: the low sine gives it weight, the noise gives it contact.
        private AudioClip BuildHit() {
            int samples = SampleRate / 5;
            float[] data = new float[samples];
            float phase = 0f;
            Random.State state = Random.state;
            Random.InitState(4242);
            int i = 0;
            while (i < samples) {
                float t = i / (float)samples;
                float freq = 180f * (1f - 0.4f * t);
                phase = phase + 2f * Mathf.PI * freq / SampleRate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-7f * t);
                float click = Random.Range(-1f, 1f) * Mathf.Exp(-45f * t) * 0.5f;
                data[i] = body * 0.8f + click;
                i = i + 1;
            }
            Random.state = state;
            Normalise(data, 0.9f);
            return FromSamples("hit", data);
        }

        // Three pentatonic notes in sequence. Reserved for an actual achievement, so it stays special.
        private AudioClip BuildMatch() {
            int samples = (int)(SampleRate * 1.1f);
            float[] data = new float[samples];
            int[] degrees = { 4, 7, 12 };
            int n = 0;
            while (n < degrees.Length) {
                float freq = RootHz * 4f * Mathf.Pow(2f, degrees[n] / 12f);
                int start = (int)(samples * 0.22f * n);
                int length = samples - start;
                float phase = 0f;
                int i = 0;
                while (i < length) {
                    float t = i / (float)length;
                    phase = phase + 2f * Mathf.PI * freq / SampleRate;
                    data[start + i] = data[start + i] + Mathf.Sin(phase) * Mathf.Exp(-4f * t) * 0.45f;
                    i = i + 1;
                }
                n = n + 1;
            }
            Normalise(data, 0.85f);
            return FromSamples("match", data);
        }

        private AudioClip BuildGrab() {
            return BuildBlip(0.09f, 520f, 780f);
        }

        private AudioClip BuildRelease() {
            return BuildBlip(0.09f, 780f, 420f);
        }

        // Arrival and departure are a rising and a falling pair, so the room's response to somebody
        // walking up and walking away is symmetrical and obviously deliberate.
        private AudioClip BuildArrive() {
            return BuildBlip(0.55f, 220f, 660f);
        }

        private AudioClip BuildDepart() {
            return BuildBlip(0.7f, 440f, 165f);
        }

        private AudioClip BuildBlip(float seconds, float fromHz, float toHz) {
            int samples = (int)(SampleRate * seconds);
            float[] data = new float[samples];
            float phase = 0f;
            int i = 0;
            while (i < samples) {
                float t = i / (float)samples;
                float freq = Mathf.Lerp(fromHz, toHz, t * t);
                phase = phase + 2f * Mathf.PI * freq / SampleRate;
                float env = Mathf.Sin(Mathf.PI * t);
                data[i] = Mathf.Sin(phase) * env * 0.8f;
                i = i + 1;
            }
            return FromSamples("blip", data);
        }

        private static void Normalise(float[] data, float peak) {
            float max = 0f;
            int i = 0;
            while (i < data.Length) {
                float a = data[i] < 0f ? -data[i] : data[i];
                if (a > max) {
                    max = a;
                }
                i = i + 1;
            }
            if (max < 1e-6f) {
                return;
            }
            float scale = peak / max;
            i = 0;
            while (i < data.Length) {
                data[i] = data[i] * scale;
                i = i + 1;
            }
        }
    }
}
