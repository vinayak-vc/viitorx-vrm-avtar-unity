using System;

using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// High-level avatar session the UI drives. Loads and hot-swaps the active avatar by path.
    /// Exposed as an interface so UI depends on Core, never on the App composition root.
    /// <see cref="AvatarChanged"/> fires after a successful load (manual or auto).
    /// </summary>
    public interface IAvatarSession {
        event Action AvatarChanged;
        IAvatarInstance Current { get; }
        Awaitable<AvatarLoadResult> LoadFromPathAsync(string path, bool showMeshes);
    }
}
