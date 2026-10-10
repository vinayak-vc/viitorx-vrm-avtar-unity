using UnityEngine;
using VirtualMirror.Core;

namespace VirtualMirror.App {
    /// <summary>
    /// Inspector section "Debug": the debug skeleton and pipeline logging.
    /// Declarations only - the behaviour that uses them stays in <see cref="AppBootstrap"/>. One file per
    /// section so a setting is found by name; <c>[InspectorGroup]</c> draws it as a collapsible section.
    /// </summary>
    public sealed partial class AppBootstrap {
        [InspectorGroup("Debug", Order = 80)]
        [Tooltip("Draw a live line-skeleton of the raw tracked pose beside the avatar to validate tracking by eye.")]
        [SerializeField] private bool showDebugSkeleton = true;
        [Tooltip("Uniform size of the debug skeleton.")]
        [SerializeField] private float debugSkeletonScale = 1.5f;
        [Tooltip("Offset of the debug skeleton from the avatar root.")]
        [SerializeField] private Vector3 debugSkeletonOffset = new Vector3(1.4f, 1.0f, 0f);
        [Tooltip("Pipeline logging: write recv_log.jsonl (received) + model_log.jsonl (applied) to compare against the sidecar's sender_log.jsonl. Run the sidecar with --log-dir <the same dir>, then diff with compare_logs.py.")]
        [SerializeField] private bool pipelineLogging = false;
        [SerializeField] private string pipelineLogDir = "D:/Unity/viitorx-vrm-avtar-unity-base-project/Assets/Games/viitorx-vrm-avtar-unity/python-sidecar~/pipeline_logs";
    }
}
