using System;

using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Parses a hands datagram (<c>{"ver":1,"kind":"hands",...}</c>, hand_wire.py) into a <see cref="HandsMessage"/>.
    /// Strict where the contract is strict: 21 landmarks and 21 world landmarks, five curls and five flags, a side of L or R.
    /// Anything the datagram does not say about a hand's gesture or position refusal reads as None / Unknown, never as a guess.
    /// </summary>
    public sealed class HandsWireParser : VisionWireParser {
        protected override byte[] ExpectedKind {
            get {
                return WireKeys.KindHands;
            }
        }

        protected override byte[] ItemsKey {
            get {
                return WireKeys.Hands;
            }
        }

        protected override void ResetMessage(VisionMessage message) {
            HandsMessage hands = message as HandsMessage;
            if (hands != null) {
                hands.Clear();
            }
        }

        protected override int Capacity(VisionMessage message) {
            HandsMessage hands = message as HandsMessage;
            return hands == null ? 0 : hands.Capacity;
        }

        protected override bool ReadItem(VisionMessage message, int index) {
            HandsMessage hands = message as HandsMessage;
            if (hands == null) {
                return Scanner.Fail(WireParseResult.WrongKind);
            }
            return ReadHand(hands.Slot(index));
        }

        private bool ReadHand(HandData hand) {
            hand.Clear();
            if (!Scanner.BeginObject()) {
                return Scanner.Fail(WireParseResult.BadShape);
            }
            bool haveSide = false;
            bool haveImage = false;
            bool haveWorld = false;
            bool haveCurls = false;
            bool haveExtended = false;
            bool havePosition = false;
            bool haveWhy = false;
            bool first = true;
            ReadOnlySpan<byte> key;
            while (Scanner.NextMember(first, out key)) {
                first = false;
                if (WireKeys.Is(key, WireKeys.Id)) {
                    int id;
                    if (!Scanner.ReadInt(out id)) {
                        return false;
                    }
                    hand.Id = id;
                } else if (WireKeys.Is(key, WireKeys.Side)) {
                    ReadOnlySpan<byte> side;
                    if (!Scanner.ReadString(out side)) {
                        return false;
                    }
                    if (WireKeys.Is(side, WireKeys.SideLeft)) {
                        hand.Side = HandSide.Left;
                    } else if (WireKeys.Is(side, WireKeys.SideRight)) {
                        hand.Side = HandSide.Right;
                    } else {
                        return Scanner.Fail(WireParseResult.BadShape);
                    }
                    haveSide = true;
                } else if (WireKeys.Is(key, WireKeys.Hd)) {
                    float handedness;
                    if (!Scanner.ReadFloat(out handedness)) {
                        return false;
                    }
                    hand.Handedness = handedness;
                } else if (WireKeys.Is(key, WireKeys.Conf)) {
                    float confidence;
                    if (!Scanner.ReadFloat(out confidence)) {
                        return false;
                    }
                    hand.Confidence = confidence;
                } else if (WireKeys.Is(key, WireKeys.St)) {
                    int state;
                    if (!Scanner.ReadInt(out state)) {
                        return false;
                    }
                    hand.State = state == 0 ? TrackState.Tracked : TrackState.Held;
                } else if (WireKeys.Is(key, WireKeys.Lm)) {
                    if (!ReadLandmarks(hand.ImageLandmarks, false)) {
                        return false;
                    }
                    haveImage = true;
                } else if (WireKeys.Is(key, WireKeys.Wl)) {
                    if (!ReadLandmarks(hand.WorldLandmarks, true)) {
                        return false;
                    }
                    haveWorld = true;
                } else if (WireKeys.Is(key, WireKeys.Pos)) {
                    Vector3 wire;
                    if (!Scanner.ReadVector3(out wire)) {
                        return false;
                    }
                    hand.Position = VisionMath.WireToUnity(wire.x, wire.y, wire.z);
                    havePosition = true;
                } else if (WireKeys.Is(key, WireKeys.Why)) {
                    ReadOnlySpan<byte> why;
                    if (!Scanner.ReadString(out why)) {
                        return false;
                    }
                    hand.PositionRefusal = WireKeys.ReadRefusal(why);
                    haveWhy = true;
                } else if (WireKeys.Is(key, WireKeys.Fing)) {
                    if (!Scanner.ReadFloatArray(hand.FingerCurls, HandData.FingerCount)) {
                        return false;
                    }
                    haveCurls = true;
                } else if (WireKeys.Is(key, WireKeys.Ext)) {
                    if (!Scanner.ReadFlagArray(hand.FingerExtended, HandData.FingerCount)) {
                        return false;
                    }
                    haveExtended = true;
                } else if (WireKeys.Is(key, WireKeys.Pinch)) {
                    float pinch;
                    if (!Scanner.ReadFloat(out pinch)) {
                        return false;
                    }
                    hand.Pinch = pinch;
                } else if (WireKeys.Is(key, WireKeys.Ges)) {
                    ReadOnlySpan<byte> gesture;
                    if (!Scanner.ReadString(out gesture)) {
                        return false;
                    }
                    hand.Gesture = WireKeys.ReadGesture(gesture);
                } else if (!Scanner.SkipValue()) {
                    return false;
                }
            }
            if (Scanner.Failed) {
                return false;
            }
            if (!haveSide || !haveImage || !haveWorld || !haveCurls || !haveExtended) {
                return Scanner.Fail(WireParseResult.MissingField);
            }
            hand.HasPosition = havePosition;
            if (havePosition) {
                hand.PositionRefusal = PositionRefusal.None;
            } else if (!haveWhy) {
                hand.PositionRefusal = PositionRefusal.Unknown;
            }
            return true;
        }

        private bool ReadLandmarks(Vector3[] destination, bool convertToUnity) {
            int count;
            if (!Scanner.ReadVector3Array(destination, HandData.LandmarkCount, out count)) {
                return false;
            }
            if (count != HandData.LandmarkCount) {
                return Scanner.Fail(WireParseResult.BadShape);
            }
            if (convertToUnity) {
                int i = 0;
                while (i < count) {
                    Vector3 wire = destination[i];
                    destination[i] = VisionMath.WireToUnity(wire.x, wire.y, wire.z);
                    i = i + 1;
                }
            }
            return true;
        }
    }
}
