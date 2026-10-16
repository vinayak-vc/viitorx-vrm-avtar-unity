namespace VirtualMirror.Core {
    /// <summary>
    /// Outcome of an avatar load. On success <see cref="Instance"/> is non-null and owned by
    /// the caller (caller disposes it). On failure <see cref="Instance"/> is null.
    /// </summary>
    public sealed class AvatarLoadResult {
        private readonly AvatarLoadStatus status;
        private readonly IAvatarInstance instance;
        private readonly string message;

        private AvatarLoadResult(AvatarLoadStatus status, IAvatarInstance instance, string message) {
            this.status = status;
            this.instance = instance;
            this.message = message;
        }

        public AvatarLoadStatus Status {
            get {
                return status;
            }
        }

        public IAvatarInstance Instance {
            get {
                return instance;
            }
        }

        public string Message {
            get {
                return message;
            }
        }

        public bool IsSuccess {
            get {
                return status == AvatarLoadStatus.Success;
            }
        }

        public static AvatarLoadResult Success(IAvatarInstance instance) {
            return new AvatarLoadResult(AvatarLoadStatus.Success, instance, string.Empty);
        }

        public static AvatarLoadResult Failure(AvatarLoadStatus status, string message) {
            return new AvatarLoadResult(status, null, message);
        }
    }
}
