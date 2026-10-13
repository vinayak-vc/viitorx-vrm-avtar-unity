using System.Text;

using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// An on-screen readout of the two streams: whether datagrams are arriving, how old the newest is, how many parsed and how many were rejected,
    /// then each hand (side, id, gesture, estimated distance) and the first face (head pose, distance, its strongest blendshapes). The first thing to
    /// look at when "nothing moves": NoStream means the sidecar is not sending to this port; Stale means it stopped; a rising Rejected count means
    /// the sender and this receiver disagree about the wire version.
    ///
    /// Immediate-mode GUI, demo only. NOT verified in the Editor.
    /// </summary>
    [AddComponentMenu("Vision/Status HUD")]
    public sealed class VisionStatusHud : MonoBehaviour {
        private const int StrongestBlendshapes = 8;

        [SerializeField] private HandsReceiverBehaviour hands;
        [SerializeField] private FaceReceiverBehaviour face;
        [SerializeField] private Vector2 position = new Vector2(10f, 10f);

        private readonly StringBuilder text = new StringBuilder(512);
        private readonly int[] strongest = new int[StrongestBlendshapes];
        private GUIStyle style;

        public void Bind(HandsReceiverBehaviour handsReceiver, FaceReceiverBehaviour faceReceiver) {
            hands = handsReceiver;
            face = faceReceiver;
        }

        private void OnGUI() {
            if (style == null) {
                style = new GUIStyle(GUI.skin.box);
                style.alignment = TextAnchor.UpperLeft;
                style.fontSize = 14;
                style.richText = false;
            }
            text.Length = 0;
            AppendHands();
            AppendFace();
            GUI.Box(new Rect(position.x, position.y, 520f, 28f + 18f * CountLines()), text.ToString(), style);
        }

        private void AppendHands() {
            if (hands == null) {
                return;
            }
            HandsReceiver receiver = hands.Receiver;
            if (receiver == null) {
                text.Append("Hands: receiver not running\n");
                return;
            }
            text.Append("Hands  ").Append(receiver.State).Append("  UDP ").Append(receiver.Port)
                .Append("  age ").Append(AgeText(receiver.AgeSeconds))
                .Append("  ok ").Append(receiver.Accepted).Append("  rejected ").Append(receiver.Rejected)
                .Append("  sessions+").Append(receiver.SessionChanges).Append('\n');
            if (!hands.HasMessage) {
                return;
            }
            HandsMessage message = hands.Message;
            text.Append("  n=").Append(message.Count).Append("  sidecar ").Append(message.LatencyMs.ToString("F0")).Append(" ms\n");
            int i = 0;
            while (i < message.Count) {
                HandData hand = message.Hand(i);
                text.Append("  ").Append(hand.Side == HandSide.Left ? "L" : "R").Append(" #").Append(hand.Id).Append("  ")
                    .Append(hand.Gesture).Append("  pinch ").Append(hand.Pinch.ToString("F2"));
                if (hand.HasPosition) {
                    text.Append("  z ").Append(hand.Position.z.ToString("F2")).Append(" m (est.)");
                } else {
                    text.Append("  z n/a (").Append(hand.PositionRefusal).Append(')');
                }
                if (hand.State == TrackState.Held) {
                    text.Append("  HELD");
                }
                text.Append('\n');
                i = i + 1;
            }
        }

        private void AppendFace() {
            if (face == null) {
                return;
            }
            FaceReceiver receiver = face.Receiver;
            if (receiver == null) {
                text.Append("Face: receiver not running\n");
                return;
            }
            text.Append("Face  ").Append(receiver.State).Append("  UDP ").Append(receiver.Port)
                .Append("  age ").Append(AgeText(receiver.AgeSeconds))
                .Append("  ok ").Append(receiver.Accepted).Append("  rejected ").Append(receiver.Rejected)
                .Append("  sessions+").Append(receiver.SessionChanges).Append('\n');
            if (!face.HasMessage || face.Message.Count < 1) {
                return;
            }
            FaceData data = face.Message.Face(0);
            text.Append("  #").Append(data.Id).Append("  yaw ").Append(data.Yaw.ToString("F0")).Append("  pitch ").Append(data.Pitch.ToString("F0"))
                .Append("  roll ").Append(data.Roll.ToString("F0"));
            if (data.HasPosition) {
                text.Append("  z ").Append(data.Position.z.ToString("F2")).Append(" m (est.)");
            }
            text.Append('\n');
            if (!data.HasBlendshapes) {
                return;
            }
            int found = TopBlendshapes(data.Blendshapes);
            int i = 0;
            while (i < found) {
                text.Append("  ").Append(ArkitBlendshapes.NameOf(strongest[i])).Append(' ').Append(data.Blendshapes[strongest[i]].ToString("F2")).Append('\n');
                i = i + 1;
            }
        }

        private int TopBlendshapes(float[] scores) {
            int found = 0;
            int channel = 1;
            while (channel < scores.Length) {
                int slot = found;
                if (found < strongest.Length) {
                    found = found + 1;
                } else if (scores[channel] <= scores[strongest[strongest.Length - 1]]) {
                    channel = channel + 1;
                    continue;
                } else {
                    slot = strongest.Length - 1;
                }
                strongest[slot] = channel;
                while (slot > 0 && scores[strongest[slot]] > scores[strongest[slot - 1]]) {
                    int swap = strongest[slot - 1];
                    strongest[slot - 1] = strongest[slot];
                    strongest[slot] = swap;
                    slot = slot - 1;
                }
                channel = channel + 1;
            }
            return found;
        }

        private int CountLines() {
            int lines = 1;
            int i = 0;
            while (i < text.Length) {
                if (text[i] == '\n') {
                    lines = lines + 1;
                }
                i = i + 1;
            }
            return lines;
        }

        private static string AgeText(double seconds) {
            if (double.IsInfinity(seconds)) {
                return "none yet";
            }
            return (seconds * 1000.0).ToString("F0") + " ms";
        }
    }
}
