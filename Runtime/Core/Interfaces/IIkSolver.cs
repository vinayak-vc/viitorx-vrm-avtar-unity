using System;

using UnityEngine;

namespace VirtualMirror.Core {
    /// <summary>
    /// Contract for an IK solver that applies target positions/rotations and hints onto an avatar.
    /// </summary>
    public interface IIkSolver : IDisposable {
        bool IsBound { get; }
        void Bind(Animator animator, Transform targetParent);
        void Apply(PoseFrame frame, float minConfidence);
        void Unbind();
    }
}
