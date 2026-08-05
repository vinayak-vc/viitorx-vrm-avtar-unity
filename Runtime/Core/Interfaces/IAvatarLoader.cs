using System.Threading;

using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// Loads an avatar from disk or memory into a live <see cref="IAvatarInstance"/>.
    /// Implementations read bytes off the main thread and instantiate on the main thread,
    /// parenting the result under <paramref name="parent"/> when supplied.
    /// </summary>
    public interface IAvatarLoader {
        Awaitable<AvatarLoadResult> LoadAsync(AvatarLoadRequest request, Transform parent, CancellationToken cancellationToken);
    }
}
