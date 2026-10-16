using System;

using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// <see cref="HandsWireParser"/> and <see cref="FaceWireParser"/> against the datagrams the Python encoders actually produced
    /// (Tests/EditMode/VisionFixtures, written by python-sidecar~/tools/video/make_vision_fixtures.py): the cross-language contract. Every value
    /// asserted here was put on the wire by the Python side from a synthetic hand or face of known shape; the real_* fixtures are what the real
    /// networks emitted once.
    /// </summary>
    public sealed class VisionParserTests {
        private const float Tolerance = 1e-4f;

        private static WireParseResult ParseHands(string fixture, HandsMessage message) {
            int length;
            byte[] buffer = VisionFixtures.LoadPadded(fixture, out length);
            return new HandsWireParser().TryParse(buffer, length, message);
        }

        private static WireParseResult ParseFace(string fixture, FaceMessage message) {
            int length;
            byte[] buffer = VisionFixtures.LoadPadded(fixture, out length);
            return new FaceWireParser().TryParse(buffer, length, message);
        }

        private static WireParseResult ParseFaceBytes(byte[] bytes, FaceMessage message) {
            return new FaceWireParser().TryParse(bytes, bytes.Length, message);
        }

        private static string ReplaceIn(string fixture, string from, string to) {
            string text = VisionFixtures.LoadText(fixture);
            Assert.IsTrue(text.Contains(from), "the fixture contains " + from);
            return text.Replace(from, to);
        }

        // ---------------------------------------------------------------- hands

        [Test]
        public void Hands_HeaderFieldsAreRead() {
            HandsMessage message = new HandsMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseHands("hands_two.json", message));
            Assert.AreEqual("hd0123456789", message.SessionId);
            Assert.AreEqual(1042L, message.Sequence);
            Assert.AreEqual(1791890123.456, message.TimestampUnix, 1e-3);
            Assert.AreEqual(38.2f, message.LatencyMs, 0.01f);
            Assert.AreEqual(VisionSource.Camera, message.Source);
            Assert.AreEqual(1280, message.ImageWidth);
            Assert.AreEqual(720, message.ImageHeight);
            Assert.AreEqual(1108.5f, message.FocalPixels, 0.05f);
            Assert.IsFalse(message.Mirrored);
            Assert.AreEqual(2, message.Count);
            Assert.AreEqual(2, message.DeclaredCount);
        }

        [Test]
        public void Hands_TwoHandsKeepTheirOwnValues() {
            HandsMessage message = new HandsMessage();
            ParseHands("hands_two.json", message);
            HandData open = message.Hand(0);
            Assert.AreEqual(3, open.Id);
            Assert.AreEqual(HandSide.Right, open.Side);
            Assert.AreEqual(0.93f, open.Handedness, 1e-3f);
            Assert.AreEqual(0.97f, open.Confidence, 1e-3f);
            Assert.AreEqual(TrackState.Tracked, open.State);
            Assert.AreEqual(HandGesture.Open, open.Gesture);
            Assert.IsTrue(open.HasPosition);
            Assert.AreEqual(PositionRefusal.None, open.PositionRefusal);
            int finger = 0;
            while (finger < HandData.FingerCount) {
                Assert.Less(open.FingerCurls[finger], 0.2f, "an open hand's finger " + finger + " is straight");
                Assert.IsTrue(open.FingerExtended[finger], "and extended");
                finger = finger + 1;
            }
            HandData fist = message.Hand(1);
            Assert.AreEqual(4, fist.Id);
            Assert.AreEqual(HandSide.Left, fist.Side);
            Assert.AreEqual(HandGesture.Fist, fist.Gesture);
            finger = 1;
            while (finger < HandData.FingerCount) {
                Assert.Greater(fist.FingerCurls[finger], 0.75f, "a fist's finger " + finger + " is closed");
                Assert.IsFalse(fist.FingerExtended[finger]);
                finger = finger + 1;
            }
        }

        [Test]
        public void Hands_PositionAndWorldLandmarksAreFlippedToUnityAxes_ImageLandmarksAreNot() {
            HandsMessage message = new HandsMessage();
            ParseHands("hands_two.json", message);
            HandData open = message.Hand(0);
            Assert.AreEqual(0.10f, open.Position.x, Tolerance);
            Assert.AreEqual(0.02f, open.Position.y, Tolerance, "the wire sent y = -0.02 (up is negative on the wire); Unity's y is up");
            Assert.AreEqual(0.60f, open.Position.z, Tolerance);
            Vector3 wrist = open.WorldLandmarks[0];
            Vector3 indexTip = open.WorldLandmarks[8];
            Assert.Greater(indexTip.y, wrist.y, "the index fingertip is ABOVE the wrist in Unity's world landmarks");
            Assert.Less(open.ImageLandmarks[8].y, open.ImageLandmarks[0].y, "and ABOVE it in the image (a smaller y: image y grows downward)");
            Assert.Greater(open.ImageLandmarks[0].x, 0f);
            Assert.Less(open.ImageLandmarks[0].x, 1f, "image coordinates are fractions of the frame");
        }

        [Test]
        public void Hands_CameraSpaceLandmarkIsThePositionPlusTheWorldOffset() {
            HandsMessage message = new HandsMessage();
            ParseHands("hands_two.json", message);
            HandData open = message.Hand(0);
            Vector3 point;
            Assert.IsTrue(open.TryGetCameraSpaceLandmark(8, out point));
            Assert.AreEqual((open.Position + open.WorldLandmarks[8]).x, point.x, 1e-6f);
            Assert.AreEqual((open.Position + open.WorldLandmarks[8]).y, point.y, 1e-6f);
            Assert.AreEqual((open.Position + open.WorldLandmarks[8]).z, point.z, 1e-6f);
            Assert.IsFalse(open.TryGetCameraSpaceLandmark(21, out point));
            Assert.IsFalse(open.TryGetCameraSpaceLandmark(-1, out point));
        }

        [Test]
        public void Hands_AHeldHandAndARefusedPosition() {
            HandsMessage message = new HandsMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseHands("hands_held_refused.json", message));
            HandData held = message.Hand(0);
            Assert.AreEqual(7, held.Id);
            Assert.AreEqual(TrackState.Held, held.State);
            Assert.AreEqual(0.55f, held.Confidence, 1e-3f);
            Assert.AreEqual(HandGesture.Point, held.Gesture);
            HandData refused = message.Hand(1);
            Assert.AreEqual(8, refused.Id);
            Assert.AreEqual(TrackState.Tracked, refused.State);
            Assert.IsFalse(refused.HasPosition, "the sidecar refused the 3D fit and sent no position");
            Assert.AreEqual(PositionRefusal.Residual, refused.PositionRefusal);
            Vector3 point;
            Assert.IsFalse(refused.TryGetCameraSpaceLandmark(0, out point), "so there is no camera-space landmark either");
            Assert.AreEqual(HandGesture.Victory, refused.Gesture);
        }

        [Test]
        public void Hands_TheGestureSetAndTheSidesSurvive() {
            HandsMessage message = new HandsMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseHands("hands_four_gestures.json", message));
            Assert.AreEqual(4, message.Count);
            HandGesture[] gestures = { HandGesture.Rock, HandGesture.Victory, HandGesture.ThumbsUp, HandGesture.Ok };
            HandSide[] sides = { HandSide.Right, HandSide.Left, HandSide.Right, HandSide.Left };
            int i = 0;
            while (i < 4) {
                Assert.AreEqual(gestures[i], message.Hand(i).Gesture, "gesture of hand " + i);
                Assert.AreEqual(sides[i], message.Hand(i).Side, "side of hand " + i);
                i = i + 1;
            }
            Assert.Greater(message.Hand(3).Pinch, 0.9f, "the OK hand's thumb and index tips touch");
        }

        [Test]
        public void Hands_AnEmptyFrameIsAnOkMessageWithNoHands() {
            HandsMessage message = new HandsMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseHands("hands_empty.json", message));
            Assert.AreEqual(0, message.Count);
            Assert.AreEqual(0, message.DeclaredCount);
            Assert.AreEqual(1042L, message.Sequence, "the header still arrives: 'no hand' is distinguishable from 'no sender'");
        }

        [Test]
        public void Hands_WhitespaceReorderedKeysAndUnknownMembersAreTolerated() {
            HandsMessage plain = new HandsMessage();
            HandsMessage odd = new HandsMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseHands("hands_two.json", plain));
            Assert.AreEqual(WireParseResult.Ok, ParseHands("hands_pretty_reordered_extra.json", odd));
            Assert.AreEqual(plain.SessionId, odd.SessionId);
            Assert.AreEqual(plain.Sequence, odd.Sequence);
            Assert.AreEqual(plain.Count, odd.Count);
            int h = 0;
            while (h < plain.Count) {
                HandData a = plain.Hand(h);
                HandData b = odd.Hand(h);
                Assert.AreEqual(a.Id, b.Id);
                Assert.AreEqual(a.Side, b.Side);
                Assert.AreEqual(a.Gesture, b.Gesture);
                Assert.AreEqual(a.Position.z, b.Position.z, 1e-6f);
                int j = 0;
                while (j < HandData.LandmarkCount) {
                    Assert.AreEqual(a.WorldLandmarks[j].x, b.WorldLandmarks[j].x, 1e-6f);
                    Assert.AreEqual(a.ImageLandmarks[j].y, b.ImageLandmarks[j].y, 1e-6f);
                    j = j + 1;
                }
                h = h + 1;
            }
        }

        [Test]
        public void Hands_ThePaddedBufferIsReadByLengthNotByArray() {
            HandsMessage message = new HandsMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseHands("hands_two.json", message), "4 KB of 'x' follow the datagram in the buffer");
            Assert.AreEqual(2, message.Count);
        }

        [Test]
        public void Hands_AMessageWithFewerHandsThanBeforeIsFullyCleared() {
            HandsMessage message = new HandsMessage();
            ParseHands("hands_two.json", message);
            Assert.AreEqual(2, message.Count);
            ParseHands("hands_empty.json", message);
            Assert.AreEqual(0, message.Count);
            Assert.AreEqual(0, message.Hand(0).Id, "the old hand's slot was cleared");
            Assert.IsFalse(message.Hand(0).HasPosition);
            ParseHands("hands_held_refused.json", message);
            Assert.IsFalse(message.Hand(1).HasPosition, "a hand without a position does not inherit the previous hand's");
        }

        [Test]
        public void Hands_MoreHandsThanTheMessageHoldsAreCountedNotStored() {
            HandsMessage small = new HandsMessage(2);
            Assert.AreEqual(WireParseResult.Ok, ParseHands("hands_four_gestures.json", small));
            Assert.AreEqual(2, small.Count);
            Assert.AreEqual(4, small.DeclaredCount);
            Assert.AreEqual(HandGesture.Victory, small.Hand(1).Gesture);
        }

        [Test]
        public void Hands_BadDatagramsAreRefusedForTheRightReason() {
            Assert.AreEqual(WireParseResult.UnknownVersion, ParseHands("bad_version.json", new HandsMessage()));
            Assert.AreEqual(WireParseResult.WrongKind, ParseHands("bad_kind.json", new HandsMessage()));
            Assert.AreEqual(WireParseResult.CountMismatch, ParseHands("bad_count.json", new HandsMessage()));
            Assert.AreEqual(WireParseResult.BadShape, ParseHands("bad_landmarks.json", new HandsMessage()));
            Assert.AreEqual(WireParseResult.BadShape, ParseHands("bad_side.json", new HandsMessage()));
            Assert.AreEqual(WireParseResult.Truncated, ParseHands("bad_truncated.json", new HandsMessage()));
            Assert.AreEqual(WireParseResult.NotJson, ParseHands("bad_not_json.txt", new HandsMessage()));
            Assert.AreEqual(WireParseResult.Empty, new HandsWireParser().TryParse(new byte[10], 0, new HandsMessage()));
            Assert.AreEqual(WireParseResult.Empty, new HandsWireParser().TryParse(null, 10, new HandsMessage()));
        }

        [Test]
        public void Hands_ADatagramMissingAnyRequiredFieldIsRefusedAsIncomplete() {
            string[] required = { "side", "lm", "wl", "fing", "ext" };
            int i = 0;
            while (i < required.Length) {
                string broken = ReplaceIn("hands_two.json", "\"" + required[i] + "\":", "\"" + required[i] + "X\":");
                byte[] bytes = VisionFixtures.Bytes(broken);
                Assert.AreEqual(WireParseResult.MissingField, new HandsWireParser().TryParse(bytes, bytes.Length, new HandsMessage()),
                    "a hand without " + required[i] + " is not a hand");
                i = i + 1;
            }
        }

        [Test]
        public void Hands_AFaceDatagramIsNotAHandsDatagram_AndTheOtherWayRound() {
            Assert.AreEqual(WireParseResult.WrongKind, ParseHands("face_full.json", new HandsMessage()));
            Assert.AreEqual(WireParseResult.WrongKind, ParseFace("hands_two.json", new FaceMessage()));
        }

        [Test]
        public void Hands_AnUnknownVersionIsReportedEvenWhenTheRestWouldNotParse() {
            string v2 = ReplaceIn("hands_two.json", "\"ver\":1", "\"ver\":2").Replace("\"lm\":", "\"landmarks_v2\":");
            Assert.AreEqual(WireParseResult.UnknownVersion, new HandsWireParser().TryParse(VisionFixtures.Bytes(v2), VisionFixtures.Bytes(v2).Length, new HandsMessage()),
                "a receiver must not guess at a format it does not know");
        }

        [Test]
        public void Hands_RealNetworkOutputParsesAndLooksLikeHands() {
            HandsMessage message = new HandsMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseHands("real_hands.json", message));
            Assert.AreEqual(2, message.Count);
            Assert.AreEqual(VisionSource.File, message.Source);
            int h = 0;
            while (h < message.Count) {
                HandData hand = message.Hand(h);
                Assert.Greater(hand.Confidence, 0.5f);
                int i = 0;
                while (i < HandData.LandmarkCount) {
                    Assert.IsFalse(float.IsNaN(hand.WorldLandmarks[i].x) || float.IsNaN(hand.ImageLandmarks[i].y));
                    Assert.Less(Mathf.Abs(hand.WorldLandmarks[i].x), 0.25f, "a hand is under half a metre across");
                    i = i + 1;
                }
                int f = 0;
                while (f < HandData.FingerCount) {
                    Assert.GreaterOrEqual(hand.FingerCurls[f], 0f);
                    Assert.LessOrEqual(hand.FingerCurls[f], 1f);
                    f = f + 1;
                }
                if (hand.HasPosition) {
                    Assert.Greater(hand.Position.z, 0.1f);
                    Assert.Less(hand.Position.z, 3f);
                }
                h = h + 1;
            }
        }

        // ---------------------------------------------------------------- face

        [Test]
        public void Face_HeaderFieldsAreRead() {
            FaceMessage message = new FaceMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseFace("face_full.json", message));
            Assert.AreEqual("fc0123456789", message.SessionId);
            Assert.AreEqual(1042L, message.Sequence);
            Assert.AreEqual(VisionSource.Camera, message.Source);
            Assert.AreEqual(1280, message.ImageWidth);
            Assert.AreEqual(720, message.ImageHeight);
            Assert.AreEqual(1108.5f, message.FocalPixels, 0.05f);
            Assert.AreEqual(1, message.BlendshapeOrdering);
            Assert.AreEqual(1, message.Count);
        }

        [Test]
        public void Face_BlendshapesAreReadByTheirNames() {
            FaceMessage message = new FaceMessage();
            ParseFace("face_full.json", message);
            FaceData face = message.Face(0);
            Assert.IsTrue(face.HasBlendshapes);
            Assert.AreEqual(0.7f, face.Blendshape(ArkitBlendshapes.JawOpen), 1e-3f);
            Assert.AreEqual(0.9f, face.Blendshape(ArkitBlendshapes.EyeBlinkLeft), 1e-3f);
            Assert.AreEqual(0.1f, face.Blendshape(ArkitBlendshapes.EyeBlinkRight), 1e-3f);
            Assert.AreEqual(0.6f, face.Blendshape(ArkitBlendshapes.MouthSmileLeft), 1e-3f);
            Assert.AreEqual(0.4f, face.Blendshape(ArkitBlendshapes.MouthSmileRight), 1e-3f);
            Assert.AreEqual(0.5f, face.Blendshape(ArkitBlendshapes.BrowInnerUp), 1e-3f);
            Assert.AreEqual(0.05f, face.Blendshape(ArkitBlendshapes.Neutral), 1e-3f);
            Assert.AreEqual(0f, face.Blendshape(ArkitBlendshapes.CheekPuff), 1e-6f);
            Assert.AreEqual(0f, face.Blendshape(-1), "out of range reads zero");
            Assert.AreEqual(0f, face.Blendshape(52));
            Assert.AreEqual(ArkitBlendshapes.Count, face.Blendshapes.Length);
        }

        [Test]
        public void Face_LandmarksPositionAndPoseAreRead() {
            FaceMessage message = new FaceMessage();
            ParseFace("face_full.json", message);
            FaceData face = message.Face(0);
            Assert.AreEqual(1, face.Id);
            Assert.AreEqual(0.987f, face.Confidence, 1e-3f);
            Assert.AreEqual(TrackState.Tracked, face.State);
            Assert.AreEqual(FaceData.LandmarkCapacity, face.LandmarkCount);
            Assert.IsTrue(face.HasPosition);
            Assert.AreEqual(0.02f, face.Position.x, Tolerance);
            Assert.AreEqual(0.05f, face.Position.y, Tolerance, "the wire sent y = -0.05; Unity's y is up");
            Assert.AreEqual(0.65f, face.Position.z, Tolerance);
            Assert.IsTrue(face.PoseValid);
            Assert.AreEqual(30.94f, face.Yaw, 0.05f);
            Assert.AreEqual(-6.1f, face.Pitch, 0.05f);
            Assert.AreEqual(10.73f, face.Roll, 0.05f);
            Assert.Less(face.ImageLandmarks[33].x, face.ImageLandmarks[263].x, "the subject's right eye (33) is on the image's left, the left (263) on its right");
            Assert.Less(face.ImageLandmarks[10].y, face.ImageLandmarks[152].y, "the forehead is above the chin in the image (smaller y)");
        }

        [Test]
        public void Face_TheHeadRotationTurnsTheNeutralLookDirectionToWhereTheFaceLooks() {
            FaceMessage message = new FaceMessage();
            ParseFace("face_full.json", message);
            FaceData face = message.Face(0);
            Vector3 look = VisionMath.Rotate(face.HeadRotation, new Vector3(0f, 0f, -1f));
            Assert.Greater(look.x, 0.3f, "yaw is +31 degrees: the nose moved toward the image's right");
            Assert.Greater(look.y, 0f, "pitch is -6 degrees: the nose is slightly up (Unity y is up)");
            Assert.Less(look.z, -0.5f, "and the face still looks mostly toward the camera");
            Assert.AreEqual(1f, look.magnitude, 1e-4f);
            Quaternion identity = Quaternion.identity;
            ParseFace("face_refused.json", message);
            Assert.AreEqual(identity.w, message.Face(0).HeadRotation.w, 1e-4f, "a frontal face is the identity rotation");
        }

        [Test]
        public void Face_LandmarkModesAndAbsentBlendshapesAreReportedNotGuessed() {
            FaceMessage message = new FaceMessage();
            ParseFace("face_mesh_only.json", message);
            Assert.AreEqual(FaceData.MeshLandmarkCount, message.Face(0).LandmarkCount);
            Assert.IsTrue(message.Face(0).HasBlendshapes);
            ParseFace("face_no_landmarks.json", message);
            Assert.AreEqual(0, message.Face(0).LandmarkCount);
            Assert.IsTrue(message.Face(0).HasBlendshapes, "blendshapes and head pose still arrive without landmarks");
            Assert.AreEqual(0.7f, message.Face(0).Blendshape(ArkitBlendshapes.JawOpen), 1e-3f);
            Assert.Less(message.Face(0).Yaw, -10f);
            ParseFace("face_no_blendshapes.json", message);
            Assert.IsFalse(message.Face(0).HasBlendshapes);
            Assert.AreEqual(0f, message.Face(0).Blendshape(ArkitBlendshapes.JawOpen), "and they read zero, with HasBlendshapes saying why");
            Assert.AreEqual(FaceData.LandmarkCapacity, message.Face(0).LandmarkCount);
        }

        [Test]
        public void Face_ARefusedPositionIsAbsentWithItsReason() {
            FaceMessage message = new FaceMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseFace("face_refused.json", message));
            Assert.AreEqual(2, message.Face(0).Id);
            Assert.IsFalse(message.Face(0).HasPosition);
            Assert.AreEqual(PositionRefusal.Range, message.Face(0).PositionRefusal);
            Assert.IsTrue(message.Face(0).PoseValid, "the pose does not depend on the position");
        }

        [Test]
        public void Face_TwoFacesAndAHeldOne() {
            FaceMessage message = new FaceMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseFace("face_two.json", message));
            Assert.AreEqual(2, message.Count);
            Assert.AreEqual(1, message.Face(0).Id);
            Assert.AreEqual(TrackState.Tracked, message.Face(0).State);
            Assert.AreEqual(-0.15f, message.Face(0).Position.x, Tolerance);
            Assert.AreEqual(5, message.Face(1).Id);
            Assert.AreEqual(TrackState.Held, message.Face(1).State);
            Assert.AreEqual(0.5f, message.Face(1).Confidence, 1e-3f);
            Assert.AreEqual(0.2f, message.Face(1).Blendshape(ArkitBlendshapes.JawOpen), 1e-3f);
            Assert.AreEqual(0f, message.Face(1).Blendshape(ArkitBlendshapes.EyeBlinkLeft), 1e-6f, "the second face does not inherit the first one's scores");
        }

        [Test]
        public void Face_NumbersInExponentNotationAreRead() {
            FaceMessage message = new FaceMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseFace("face_exponent.json", message));
            Vector3 tiny = message.Face(0).ImageLandmarks[5];
            Assert.AreEqual(1e-5f, tiny.x, 1e-9f);
            Assert.AreEqual(2e-5f, tiny.y, 1e-9f);
            Assert.AreEqual(-1e-5f, tiny.z, 1e-9f);
            StringAssert.Contains("e-05", VisionFixtures.LoadText("face_exponent.json"), "the fixture really contains Python's exponent form");
        }

        [Test]
        public void Face_AnEmptyFrameIsAnOkMessageWithNoFaces() {
            FaceMessage message = new FaceMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseFace("face_empty.json", message));
            Assert.AreEqual(0, message.Count);
        }

        [Test]
        public void Face_MoreFacesThanTheMessageHoldsAreCountedNotStored() {
            FaceMessage one = new FaceMessage(1);
            Assert.AreEqual(WireParseResult.Ok, ParseFace("face_two.json", one));
            Assert.AreEqual(1, one.Count);
            Assert.AreEqual(2, one.DeclaredCount);
        }

        [Test]
        public void Face_BadDatagramsAreRefusedForTheRightReason() {
            Assert.AreEqual(WireParseResult.UnknownVersion, ParseFaceBytes(VisionFixtures.Bytes(ReplaceIn("face_full.json", "\"ver\":1", "\"ver\":3")), new FaceMessage()));
            Assert.AreEqual(WireParseResult.CountMismatch, ParseFaceBytes(VisionFixtures.Bytes(ReplaceIn("face_full.json", "\"n\":1", "\"n\":2")), new FaceMessage()));
            Assert.AreEqual(WireParseResult.BadShape, ParseFaceBytes(VisionFixtures.Bytes(ReplaceIn("face_no_landmarks.json", "\"bs\":[", "\"bs\":[0.5,")), new FaceMessage()),
                "53 blendshapes");
            byte[] full = VisionFixtures.Load("face_full.json");
            byte[] cut = new byte[full.Length / 2];
            Array.Copy(full, cut, cut.Length);
            Assert.AreEqual(WireParseResult.Truncated, ParseFaceBytes(cut, new FaceMessage()));
            Assert.AreEqual(WireParseResult.MissingField, ParseFaceBytes(VisionFixtures.Bytes(ReplaceIn("face_no_landmarks.json", "\"rot\":", "\"rotX\":")), new FaceMessage()),
                "a face without its rotation is incomplete");
        }

        [Test]
        public void Face_ALandmarkListOfAnyOtherLengthThanTheTwoModesIsRefused() {
            string text = VisionFixtures.LoadText("face_mesh_only.json");
            string oneShort = System.Text.RegularExpressions.Regex.Replace(text, "(\"lm\":\\[)\\[[^\\]]*\\],", "$1");
            Assert.AreNotEqual(text, oneShort, "one landmark was removed");
            Assert.AreEqual(WireParseResult.BadShape, ParseFaceBytes(VisionFixtures.Bytes(oneShort), new FaceMessage()),
                "467 landmarks is neither the 468 mesh nor the 478 mesh with irises");
        }

        [Test]
        public void Face_RealNetworkOutputParsesAndLooksLikeAFace() {
            FaceMessage message = new FaceMessage();
            Assert.AreEqual(WireParseResult.Ok, ParseFace("real_face.json", message));
            Assert.AreEqual(1, message.Count);
            FaceData face = message.Face(0);
            Assert.Greater(face.Confidence, 0.9f);
            Assert.AreEqual(FaceData.LandmarkCapacity, face.LandmarkCount);
            Assert.IsTrue(face.HasBlendshapes);
            int i = 0;
            while (i < ArkitBlendshapes.Count) {
                Assert.GreaterOrEqual(face.Blendshapes[i], 0f);
                Assert.LessOrEqual(face.Blendshapes[i], 1f);
                i = i + 1;
            }
            Assert.Less(face.Blendshape(ArkitBlendshapes.JawOpen), 0.2f, "the clip is a closed-mouth smile");
            Assert.IsTrue(face.HasPosition);
            Assert.Greater(face.Position.z, 0.15f);
            Assert.Less(face.Position.z, 4f);
            int j = 0;
            while (j < face.LandmarkCount) {
                Assert.IsFalse(float.IsNaN(face.ImageLandmarks[j].x));
                Assert.Greater(face.ImageLandmarks[j].x, -0.1f);
                Assert.Less(face.ImageLandmarks[j].x, 1.1f);
                j = j + 1;
            }
            Assert.Less(Mathf.Abs(face.Yaw), 45f);
        }

        [Test]
        public void Face_SessionChangesAreVisibleAndTheStringIsOnlyRebuiltWhenItChanges() {
            FaceMessage message = new FaceMessage();
            FaceWireParser parser = new FaceWireParser();
            byte[] first = VisionFixtures.Load("face_full.json");
            parser.TryParse(first, first.Length, message);
            string session = message.SessionId;
            parser.TryParse(first, first.Length, message);
            Assert.AreSame(session, message.SessionId, "the same session keeps the same string object: no allocation per datagram");
            byte[] other = VisionFixtures.Bytes(ReplaceIn("face_full.json", "fc0123456789", "fc9999999999"));
            parser.TryParse(other, other.Length, message);
            Assert.AreEqual("fc9999999999", message.SessionId);
        }

        // ---------------------------------------------------------------- shared

        [Test]
        public void CopyFrom_MakesAnIndependentEqualMessage() {
            HandsMessage source = new HandsMessage();
            ParseHands("hands_four_gestures.json", source);
            HandsMessage copy = new HandsMessage();
            copy.CopyFrom(source);
            source.Clear();
            Assert.AreEqual(4, copy.Count);
            Assert.AreEqual(HandGesture.ThumbsUp, copy.Hand(2).Gesture);
            Assert.AreEqual("hd0123456789", copy.SessionId);
            Assert.AreEqual(11, copy.Hand(0).Id);
            Assert.AreNotEqual(Vector3.zero, copy.Hand(0).ImageLandmarks[9], "the copy carries the image landmarks");
            Assert.AreNotEqual(Vector3.zero, copy.Hand(0).WorldLandmarks[9], "and the world landmarks");

            FaceMessage faceSource = new FaceMessage();
            ParseFace("face_full.json", faceSource);
            FaceMessage faceCopy = new FaceMessage();
            faceCopy.CopyFrom(faceSource);
            faceSource.Clear();
            Assert.AreEqual(1, faceCopy.Count);
            Assert.AreEqual(0.7f, faceCopy.Face(0).Blendshape(ArkitBlendshapes.JawOpen), 1e-3f);
            Assert.AreEqual(FaceData.LandmarkCapacity, faceCopy.Face(0).LandmarkCount);
            Assert.AreNotEqual(Vector3.zero, faceCopy.Face(0).ImageLandmarks[5], "the copy carries the landmarks, not only their count");
            Assert.AreEqual(faceCopy.Face(0).Yaw, 30.94f, 0.05f);
        }

        [Test]
        public void TryFindSide_PrefersATrackedHandThenTheMoreConfidentOne() {
            HandsMessage message = new HandsMessage();
            ParseHands("hands_held_refused.json", message);
            HandData found;
            Assert.IsTrue(message.TryFindSide(HandSide.Left, out found));
            Assert.AreEqual(7, found.Id);
            Assert.IsTrue(message.TryFindSide(HandSide.Right, out found));
            Assert.AreEqual(8, found.Id);
            string twoLeft = ReplaceIn("hands_held_refused.json", "\"side\":\"R\"", "\"side\":\"L\"").Replace("\"conf\":0.55", "\"conf\":0.99");
            byte[] bytes = VisionFixtures.Bytes(twoLeft);
            Assert.AreEqual(WireParseResult.Ok, new HandsWireParser().TryParse(bytes, bytes.Length, message));
            Assert.IsTrue(message.TryFindSide(HandSide.Left, out found));
            Assert.AreEqual(8, found.Id, "a tracked hand beats a held one even when the held one reported more confidence");
            ParseHands("hands_empty.json", message);
            Assert.IsFalse(message.TryFindSide(HandSide.Left, out found));
        }
    }
}
