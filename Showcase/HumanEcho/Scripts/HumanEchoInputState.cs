using UnityEngine;

namespace HumanEcho {
    /// <summary>What the HUD may truthfully say about where the picture comes from.</summary>
    public enum HumanEchoInputKind {
        /// <summary>No cutout frame has ever arrived.</summary>
        Waiting = 0,
        /// <summary>Frames arrived before but none within the freshness window (the sender died, stalled or
        /// was restarted).</summary>
        Stale = 1,
        /// <summary>Fresh frames from a camera or a stream.</summary>
        Live = 2,
        /// <summary>Fresh frames, but from a video file: real data, not a live camera.</summary>
        VideoFile = 3
    }

    /// <summary>
    /// Decides whether the input is LIVE from evidence only: a cutout frame counts as fresh when the texture
    /// set's version counter changed within the last few seconds. "The stream object exists" or "the sender
    /// was started" never makes it live. Plain data, no scene objects, so the rule is unit-tested.
    /// </summary>
    public sealed class HumanEchoInputState {
        /// <summary>A frame older than this no longer counts. The sidecar delivers about 17 frames a second,
        /// so two seconds is a long silence.</summary>
        public const float DefaultFreshSeconds = 2f;

        private readonly float freshSeconds;
        private bool everSeen;
        private bool present;
        private bool live;
        private int lastVersion;
        private float lastChangeTime;

        public HumanEchoInputState(float freshSeconds) {
            this.freshSeconds = Mathf.Max(0.1f, freshSeconds);
        }

        /// <summary>
        /// Feeds one observation per frame. <paramref name="hasFrame"/> is whether the texture set holds a
        /// frame at all, <paramref name="version"/> its upload counter, <paramref name="isLive"/> whether the
        /// source is a camera or stream (false for a file), <paramref name="now"/> a monotonic time in
        /// seconds.
        /// </summary>
        public void Observe(bool hasFrame, int version, bool isLive, float now) {
            bool wasPresent = present;
            present = hasFrame;
            if (!hasFrame) {
                return;
            }
            live = isLive;
            // A set that appears after having been absent is a new stream, even if its counter happens to
            // equal the old one.
            if (!everSeen || !wasPresent || version != lastVersion) {
                everSeen = true;
                lastVersion = version;
                lastChangeTime = now;
            }
        }

        public HumanEchoInputKind Kind(float now) {
            if (!everSeen) {
                return HumanEchoInputKind.Waiting;
            }
            if (!present) {
                return HumanEchoInputKind.Stale;
            }
            if (now - lastChangeTime > freshSeconds) {
                return HumanEchoInputKind.Stale;
            }
            return live ? HumanEchoInputKind.Live : HumanEchoInputKind.VideoFile;
        }

        /// <summary>Seconds since the last new frame; positive infinity when none ever arrived.</summary>
        public float SecondsSinceFrame(float now) {
            if (!everSeen) {
                return float.PositiveInfinity;
            }
            return Mathf.Max(0f, now - lastChangeTime);
        }
    }
}
