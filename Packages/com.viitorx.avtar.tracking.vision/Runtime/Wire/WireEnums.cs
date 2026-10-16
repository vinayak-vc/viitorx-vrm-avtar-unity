namespace VirtualMirror.Vision {
    /// <summary>Which hand the sidecar says this is. Left and right follow the sender's --sides rule (see hand_tracking.py).</summary>
    public enum HandSide {
        Left = 0,
        Right = 1
    }

    /// <summary>Tracked: the sidecar read it this frame. Held: the last values, a frame or two stale.</summary>
    public enum TrackState {
        Tracked = 0,
        Held = 1
    }

    /// <summary>Where the sidecar's picture came from (the <c>src</c> field).</summary>
    public enum VisionSource {
        Unknown = 0,
        Camera = 1,
        Stream = 2,
        Lan = 3,
        File = 4
    }

    /// <summary>The closed gesture set of hand_gestures.py. Anything the sidecar does not name reads as None.</summary>
    public enum HandGesture {
        None = 0,
        Open = 1,
        Fist = 2,
        Point = 3,
        Victory = 4,
        Rock = 5,
        ThumbsUp = 6,
        Ok = 7
    }

    /// <summary>Why a datagram carries no 3D position (the <c>why</c> field). None when it does carry one.</summary>
    public enum PositionRefusal {
        None = 0,
        Shape = 1,
        Degenerate = 2,
        Scale = 3,
        Residual = 4,
        Range = 5,
        Unknown = 6
    }

    /// <summary>Why a datagram was not turned into a message. Returned instead of a string so a stream of bad datagrams allocates nothing.</summary>
    public enum WireParseResult {
        Ok = 0,
        Empty = 1,
        NotJson = 2,
        Truncated = 3,
        UnknownVersion = 4,
        WrongKind = 5,
        CountMismatch = 6,
        BadShape = 7,
        MissingField = 8
    }

    /// <summary>Whether datagrams are arriving. NoStream: none yet. Live: the newest is recent. Stale: it is older than the receiver's limit.</summary>
    public enum StreamState {
        NoStream = 0,
        Live = 1,
        Stale = 2
    }
}
