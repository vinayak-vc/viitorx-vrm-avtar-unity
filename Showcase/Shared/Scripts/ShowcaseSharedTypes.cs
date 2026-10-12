namespace ShowcaseShared {
    /// <summary>Where the body comes from. Auto falls back to the simulation visibly, never silently.</summary>
    public enum ShowcaseSource {
        Live,
        Simulated,
        Auto
    }

    /// <summary>What the status chip may truthfully say about the input.</summary>
    public enum SignalStatus {
        /// <summary>Fresh, accepted tracking data is arriving from the sidecar.</summary>
        Live,
        /// <summary>The simulated pose was selected on purpose.</summary>
        Simulated,
        /// <summary>Auto mode: live data is absent, so the simulation is standing in.</summary>
        SimulatedFallback,
        /// <summary>Live was selected and no accepted pose has arrived (yet, or any more).</summary>
        NoSignal,
        /// <summary>Live was selected but the UDP socket could not be opened.</summary>
        PortInUse
    }

    /// <summary>BlazePose landmark indices used by the showcase, plus the two extra hand slots.</summary>
    public static class Joint {
        public const int Nose = 0;
        public const int LeftEar = 7;
        public const int RightEar = 8;
        public const int LeftShoulder = 11;
        public const int RightShoulder = 12;
        public const int LeftElbow = 13;
        public const int RightElbow = 14;
        public const int LeftWrist = 15;
        public const int RightWrist = 16;
        public const int LeftHip = 23;
        public const int RightHip = 24;
        public const int LeftKnee = 25;
        public const int RightKnee = 26;
        public const int LeftAnkle = 27;
        public const int RightAnkle = 28;
        public const int LeftHeel = 29;
        public const int RightHeel = 30;
        public const int LeftToe = 31;
        public const int RightToe = 32;

        /// <summary>Extra slot: the end of the left hand (palm plus a little finger length).</summary>
        public const int LeftHandEnd = 33;
        /// <summary>Extra slot: the end of the right hand.</summary>
        public const int RightHandEnd = 34;

        public const int BodyCount = 33;
        public const int SlotCount = 35;
    }
}
