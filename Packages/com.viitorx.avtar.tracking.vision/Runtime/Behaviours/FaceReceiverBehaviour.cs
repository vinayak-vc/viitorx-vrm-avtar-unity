using System;

using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Puts a <see cref="FaceReceiver"/> in a scene: add it to a GameObject and it listens on the face port while enabled. Each frame it pulls the
    /// newest datagram and raises <see cref="Updated"/> on the MAIN thread, so subscribers can touch the scene. <see cref="Message"/> is the latest
    /// message, owned by this component; copy what you keep.
    ///
    /// The Python side is <c>python-sidecar~/tools/face/face_udp_sender.py</c> (or <c>scripts/run_face_demo.bat</c>). Nothing runs in the player
    /// but a socket and a parser.
    /// </summary>
    [AddComponentMenu("Vision/Face Receiver")]
    public sealed class FaceReceiverBehaviour : MonoBehaviour {
        [Header("Socket")]
        [Tooltip("UDP port the sidecar's face sender writes to (face_udp_sender.py --port).")]
        [SerializeField] private int port = FaceReceiver.DefaultPort;
        [Tooltip("Listen on 127.0.0.1 only. Untick to accept datagrams from another machine on the network.")]
        [SerializeField] private bool loopbackOnly = true;
        [Tooltip("Seconds without a datagram after which the stream counts as stale.")]
        [SerializeField] private float staleSeconds = (float)FaceReceiver.DefaultStaleSeconds;
        [Tooltip("The most faces one datagram can hold here. The sidecar's --max-faces should not exceed it; extra faces are counted and ignored.")]
        [SerializeField] private int capacity = FaceMessage.DefaultCapacity;

        private FaceReceiver receiver;
        private FaceMessage message;
        private bool hasMessage;
        private long updateCount;

        /// <summary>Raised on the main thread, once per frame in which a new datagram arrived.</summary>
        public event Action<FaceMessage> Updated;

        public FaceReceiver Receiver {
            get {
                return receiver;
            }
        }

        /// <summary>The latest message. Valid only when <see cref="HasMessage"/>.</summary>
        public FaceMessage Message {
            get {
                return message;
            }
        }

        public bool HasMessage {
            get {
                return hasMessage;
            }
        }

        /// <summary>Counts frames in which a new message arrived. A consumer compares it to what it last saw to know whether <see cref="Message"/> changed.</summary>
        public long UpdateCount {
            get {
                return updateCount;
            }
        }

        public StreamState State {
            get {
                return receiver == null ? StreamState.NoStream : receiver.State;
            }
        }

        private void OnEnable() {
            message = new FaceMessage(capacity);
            hasMessage = false;
            receiver = new FaceReceiver(port, loopbackOnly, staleSeconds, new VisionDebugLog(), null, capacity);
            receiver.Start();
        }

        private void OnDisable() {
            if (receiver != null) {
                receiver.Dispose();
                receiver = null;
            }
        }

        private void Update() {
            if (receiver == null) {
                return;
            }
            if (receiver.TryGetNew(message)) {
                hasMessage = true;
                updateCount = updateCount + 1L;
                Action<FaceMessage> handler = Updated;
                if (handler != null) {
                    handler(message);
                }
            }
        }
    }
}
