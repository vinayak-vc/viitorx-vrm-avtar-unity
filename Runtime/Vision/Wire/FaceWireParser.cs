using System;

using UnityEngine;

namespace VirtualMirror.Vision {
    /// <summary>
    /// Parses a face datagram (<c>{"ver":1,"kind":"face",...}</c>, face_wire.py) into a <see cref="FaceMessage"/>.
    /// Strict where the contract is strict: 52 blendshapes when present, 468 or 478 landmarks when present, nine numbers in <c>rot</c>, and the three head
    /// angles. A face without <c>bs</c> or <c>lm</c> (the sender ran with --no-blendshapes or --landmarks none) is valid and says so
    /// (<see cref="FaceData.HasBlendshapes"/>, <see cref="FaceData.LandmarkCount"/>).
    /// </summary>
    public sealed class FaceWireParser : VisionWireParser {
        private readonly float[] rotation = new float[9];

        protected override byte[] ExpectedKind {
            get {
                return WireKeys.KindFace;
            }
        }

        protected override byte[] ItemsKey {
            get {
                return WireKeys.Faces;
            }
        }

        protected override void ResetMessage(VisionMessage message) {
            FaceMessage faces = message as FaceMessage;
            if (faces != null) {
                faces.Clear();
            }
        }

        protected override int Capacity(VisionMessage message) {
            FaceMessage faces = message as FaceMessage;
            return faces == null ? 0 : faces.Capacity;
        }

        protected override bool ReadExtraMember(ReadOnlySpan<byte> key, VisionMessage message, out bool consumed) {
            consumed = false;
            if (!WireKeys.Is(key, WireKeys.Bsv)) {
                return true;
            }
            consumed = true;
            int ordering;
            if (!Scanner.ReadInt(out ordering)) {
                return false;
            }
            FaceMessage faces = message as FaceMessage;
            if (faces != null) {
                faces.BlendshapeOrdering = ordering;
            }
            return true;
        }

        protected override bool ReadItem(VisionMessage message, int index) {
            FaceMessage faces = message as FaceMessage;
            if (faces == null) {
                return Scanner.Fail(WireParseResult.WrongKind);
            }
            return ReadFace(faces.Slot(index));
        }

        private bool ReadFace(FaceData face) {
            face.Clear();
            if (!Scanner.BeginObject()) {
                return Scanner.Fail(WireParseResult.BadShape);
            }
            bool haveRotation = false;
            bool haveYaw = false;
            bool havePitch = false;
            bool haveRoll = false;
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
                    face.Id = id;
                } else if (WireKeys.Is(key, WireKeys.Conf)) {
                    float confidence;
                    if (!Scanner.ReadFloat(out confidence)) {
                        return false;
                    }
                    face.Confidence = confidence;
                } else if (WireKeys.Is(key, WireKeys.St)) {
                    int state;
                    if (!Scanner.ReadInt(out state)) {
                        return false;
                    }
                    face.State = state == 0 ? TrackState.Tracked : TrackState.Held;
                } else if (WireKeys.Is(key, WireKeys.Bs)) {
                    if (!Scanner.ReadFloatArray(face.Blendshapes, ArkitBlendshapes.Count)) {
                        return false;
                    }
                    face.HasBlendshapes = true;
                } else if (WireKeys.Is(key, WireKeys.Lm)) {
                    int count;
                    if (!Scanner.ReadVector3Array(face.ImageLandmarks, FaceData.LandmarkCapacity, out count)) {
                        return false;
                    }
                    if (count != FaceData.MeshLandmarkCount && count != FaceData.LandmarkCapacity) {
                        return Scanner.Fail(WireParseResult.BadShape);
                    }
                    face.LandmarkCount = count;
                } else if (WireKeys.Is(key, WireKeys.Pos)) {
                    Vector3 wire;
                    if (!Scanner.ReadVector3(out wire)) {
                        return false;
                    }
                    face.Position = VisionMath.WireToUnity(wire.x, wire.y, wire.z);
                    havePosition = true;
                } else if (WireKeys.Is(key, WireKeys.Why)) {
                    ReadOnlySpan<byte> why;
                    if (!Scanner.ReadString(out why)) {
                        return false;
                    }
                    face.PositionRefusal = WireKeys.ReadRefusal(why);
                    haveWhy = true;
                } else if (WireKeys.Is(key, WireKeys.Yaw)) {
                    float yaw;
                    if (!Scanner.ReadFloat(out yaw)) {
                        return false;
                    }
                    face.Yaw = yaw;
                    haveYaw = true;
                } else if (WireKeys.Is(key, WireKeys.Pitch)) {
                    float pitch;
                    if (!Scanner.ReadFloat(out pitch)) {
                        return false;
                    }
                    face.Pitch = pitch;
                    havePitch = true;
                } else if (WireKeys.Is(key, WireKeys.Roll)) {
                    float roll;
                    if (!Scanner.ReadFloat(out roll)) {
                        return false;
                    }
                    face.Roll = roll;
                    haveRoll = true;
                } else if (WireKeys.Is(key, WireKeys.Rot)) {
                    if (!Scanner.ReadFloatArray(rotation, rotation.Length)) {
                        return false;
                    }
                    haveRotation = true;
                } else if (WireKeys.Is(key, WireKeys.Pose)) {
                    int pose;
                    if (!Scanner.ReadInt(out pose)) {
                        return false;
                    }
                    face.PoseValid = pose != 0;
                } else if (!Scanner.SkipValue()) {
                    return false;
                }
            }
            if (Scanner.Failed) {
                return false;
            }
            if (!haveRotation || !haveYaw || !havePitch || !haveRoll) {
                return Scanner.Fail(WireParseResult.MissingField);
            }
            face.HeadRotation = face.PoseValid ? VisionMath.HeadRotationFromWire(rotation) : Quaternion.identity;
            face.HasPosition = havePosition;
            if (havePosition) {
                face.PositionRefusal = PositionRefusal.None;
            } else if (!haveWhy) {
                face.PositionRefusal = PositionRefusal.Unknown;
            }
            return true;
        }
    }
}
