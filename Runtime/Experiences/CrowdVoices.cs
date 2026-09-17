using UnityEngine;

namespace VirtualMirror.Experiences {
    /// <summary>
    /// F-34 — ONE SUSTAINED VOICE PER PERSON, on the same scale as everything else in the show.
    ///
    /// WHY <see cref="ExperienceAudio"/> COULD NOT DO THIS. Its drone is one voice that follows the
    /// primary body's energy, and its cues are one-shots. A chord needs several notes held at once,
    /// each tied to a different person, each able to change pitch while it sounds. That is a
    /// different shape of thing — a polyphonic bank — so it is a separate class rather than a
    /// fourth mode bolted onto the shared layer every other scene uses.
    ///
    /// EVERY PITCH STILL COMES FROM <see cref="ExperienceAudio.PentatonicHz"/>, which is the whole
    /// point and is why that method was exposed rather than the scale copied. ADR-068's argument
    /// holds exactly as much here as it does for the cues: a person moving is an effectively random
    /// trigger source, and random semitones sound like a mistake within about four notes. On a
    /// pentatonic scale no interval clashes with any other, so ANY number of people standing in ANY
    /// arrangement produces a chord that sounds intentional. A room of three is a triad; nobody can
    /// make it a wrong one.
    ///
    /// ONE CLIP, RETUNED BY PLAYBACK RATE. A four-second pad is synthesised once and every voice
    /// plays it at a different pitch. Building a clip per degree would allocate ten times the audio
    /// memory to hold the same waveform, and changing a voice's note would mean restarting its
    /// source — which clicks. Changing `pitch` on a playing source does not.
    ///
    /// A VOICE IS A RESOURCE, NOT A SCORE. Slots are handed out per track id and given back when that
    /// id leaves, which is what <see cref="CrowdRoster"/> is for. An identity switch would move a
    /// slot to the wrong human — and it does not matter, because the NOTE a slot plays is recomputed
    /// every frame from where people are standing, and the mix is 2D so no voice is audibly located
    /// anywhere. The listener hears the same chord either way.
    /// </summary>
    public sealed class CrowdVoices {
        private const int SampleRate = 44100;

        /// <summary>Most voices ever sounding. Matches the crowd cap on the wire.</summary>
        public const int MaxVoices = 8;

        /// <summary>Seconds for a voice to fade in or out. Long enough that a person walking into
        /// frame does not produce a click, short enough that the chord follows the room.</summary>
        private const float GlideSeconds = 0.35f;

        private AudioSource[] sources;
        private float[] targetLevel;
        private float[] level;
        private float[] targetPitch;
        private float clipHz;

        /// <summary>Master volume, taken from the experience so M still mutes the whole room.</summary>
        public float Volume = 1f;

        public bool Enabled = true;

        /// <summary>Build the voice bank. Call once, from an experience's build step.</summary>
        public void Build(Transform parent) {
            GameObject root = new GameObject("Voices");
            root.transform.SetParent(parent, false);

            AudioClip pad = BuildPad();
            sources = new AudioSource[MaxVoices];
            targetLevel = new float[MaxVoices];
            level = new float[MaxVoices];
            targetPitch = new float[MaxVoices];

            int i = 0;
            while (i < MaxVoices) {
                GameObject go = new GameObject("voice_" + i);
                go.transform.SetParent(root.transform, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.clip = pad;
                source.loop = true;
                source.volume = 0f;
                source.playOnAwake = false;
                // 2D, for the same reason the drone is: a voice that swings across the speakers as a
                // person walks is a distraction in a room where the screen is the subject - and it
                // would also make an identity switch audible, which it otherwise is not.
                source.spatialBlend = 0f;
                // Start every voice at a random point in the loop so eight voices do not beat in
                // lockstep, which sounds like one thick voice rather than several.
                source.time = Random.Range(0f, pad.length);
                source.Play();
                sources[i] = source;
                targetPitch[i] = 1f;
                i = i + 1;
            }
        }

        /// <summary>
        /// Set one voice's note and loudness. <paramref name="step"/> is a pentatonic degree, so no
        /// value can produce a wrong note.
        /// </summary>
        public void SetVoice(int index, int step, float loudness) {
            if (sources == null || index < 0 || index >= sources.Length) {
                return;
            }
            targetPitch[index] = ExperienceAudio.PentatonicHz(step) / clipHz;
            targetLevel[index] = Mathf.Clamp01(loudness);
        }

        /// <summary>Fade one voice out. The source keeps playing at zero volume rather than stopping,
        /// so re-using the slot never restarts the clip and never clicks.</summary>
        public void Silence(int index) {
            if (targetLevel != null && index >= 0 && index < targetLevel.Length) {
                targetLevel[index] = 0f;
            }
        }

        /// <summary>Fade every voice out.</summary>
        public void SilenceAll() {
            int i = 0;
            while (targetLevel != null && i < targetLevel.Length) {
                targetLevel[i] = 0f;
                i = i + 1;
            }
        }

        /// <summary>Per-frame glide toward the targets. Pitch is eased as well as level: a voice that
        /// jumps a degree the instant somebody sidesteps sounds like a fault, where a fast portamento
        /// sounds like the note being bent, which is what is actually happening.</summary>
        public void Tick(float deltaSeconds) {
            if (sources == null) {
                return;
            }
            float k = 1f - Mathf.Exp(-Mathf.Max(1e-4f, deltaSeconds) / GlideSeconds);
            float master = Enabled ? Mathf.Clamp01(Volume) : 0f;
            int i = 0;
            while (i < sources.Length) {
                level[i] = level[i] + k * (targetLevel[i] - level[i]);
                // Divided across the bank so eight people are not eight times as loud as one; the
                // chord gets richer rather than louder as the room fills.
                sources[i].volume = level[i] * master * (0.34f / Mathf.Sqrt(MaxVoices));
                sources[i].pitch = Mathf.Lerp(sources[i].pitch, targetPitch[i], k);
                i = i + 1;
            }
        }

        // A soft pad at the scale root: fundamental, fifth and octave, with a slow tremolo so a held
        // note breathes instead of sitting perfectly still. Four seconds, so the loop is not
        // recognisable as one.
        private AudioClip BuildPad() {
            int samples = SampleRate * 4;
            float[] data = new float[samples];
            float root = LoopableHz(ExperienceAudio.PentatonicHz(0), samples);
            clipHz = root;
            float[] partials = {
                root,
                LoopableHz(root * 1.5f, samples),
                LoopableHz(root * 2f, samples),
                LoopableHz(root * 3f, samples),
            };
            float[] gains = { 0.55f, 0.22f, 0.15f, 0.05f };

            int p = 0;
            while (p < partials.Length) {
                float w = 2f * Mathf.PI * partials[p] / SampleRate;
                int n = 0;
                while (n < samples) {
                    data[n] = data[n] + Mathf.Sin(w * n) * gains[p];
                    n = n + 1;
                }
                p = p + 1;
            }

            // Two whole tremolo cycles across the buffer, so the amplitude envelope also loops
            // seamlessly - an envelope that does not close produces a thump every four seconds.
            int s = 0;
            while (s < samples) {
                float phase = 2f * Mathf.PI * 2f * s / samples;
                data[s] = data[s] * (0.82f + 0.18f * Mathf.Sin(phase)) * 0.5f;
                s = s + 1;
            }

            AudioClip clip = AudioClip.Create("CrowdVoicePad", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // A seamless loop needs a WHOLE number of cycles in the buffer, or the wrap produces a click
        // every time round. Returns the nearest frequency to `target` that fits exactly.
        private static float LoopableHz(float target, int samples) {
            float cycles = Mathf.Max(1f, Mathf.Round(target * samples / SampleRate));
            return cycles * SampleRate / samples;
        }
    }
}
