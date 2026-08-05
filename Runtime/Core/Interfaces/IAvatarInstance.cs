using System;

using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// A loaded avatar the mirror can drive. Abstracts the concrete VRM implementation so
    /// feature code (retarget, IK, UI) never references UniVRM directly. Disposing releases
    /// the instantiated GameObject and its resources.
    /// </summary>
    public interface IAvatarInstance : IDisposable {
        GameObject Root { get; }
        Animator Animator { get; }
        bool IsHumanoid { get; }
    }
}
