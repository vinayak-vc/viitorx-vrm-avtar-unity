namespace VirtualMirror.Core {
    /// <summary>
    /// Outcome codes for an avatar load attempt.
    /// </summary>
    public enum AvatarLoadStatus {
        Success,
        FileNotFound,
        FileTooLarge,
        InvalidVrm,
        NotHumanoid,
        Cancelled,
        Error
    }
}
