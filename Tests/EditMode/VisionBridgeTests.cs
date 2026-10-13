using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;

using NUnit.Framework;

using UnityEngine;

using VirtualMirror.Core;
using VirtualMirror.Vision;

namespace VirtualMirror.Tests {
    /// <summary>
    /// The conversions between the sidecar's world and this project's: the Y-flip and rotation maths (<see cref="VisionMath"/>), the ARKit blendshape
    /// names and their matching onto a mesh, the bridges to the existing <see cref="HandFrame"/> and <see cref="FaceFrame"/>, and the providers that
    /// wrap the receivers behind <see cref="IHandTrackingProvider"/> and <see cref="IFaceTrackingProvider"/>.
    /// </summary>
    public sealed class VisionBridgeTests {
        private const float Close = 1e-4f;

        private sealed class Clock {
            public double Seconds;

            public double Now() {
                return Seconds;
            }
        }

        private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance, string message) {
            Assert.AreEqual(expected.x, actual.x, tolerance, message + " (x)");
            Assert.AreEqual(expected.y, actual.y, tolerance, message + " (y)");
            Assert.AreEqual(expected.z, actual.z, tolerance, message + " (z)");
        }

        private static HandsMessage HandsFrom(string fixture) {
            int length;
            byte[] buffer = VisionFixtures.LoadPadded(fixture, out length);
            HandsMessage message = new HandsMessage();
            Assert.AreEqual(WireParseResult.Ok, new HandsWireParser().TryParse(buffer, length, message));
            return message;
        }

        private static FaceMessage FaceFrom(string fixture) {
            int length;
            byte[] buffer = VisionFixtures.LoadPadded(fixture, out length);
            FaceMessage message = new FaceMessage();
            Assert.AreEqual(WireParseResult.Ok, new FaceWireParser().TryParse(buffer, length, message));
            return message;
        }

        // ---------------------------------------------------------------- VisionMath

        [Test]
        public void WireToUnity_FlipsOnlyY() {
            AssertVector(new Vector3(1f, -2f, 3f), VisionMath.WireToUnity(1f, 2f, 3f), 0f, "y flipped");
            AssertVector(new Vector3(0f, 0f, 0f), VisionMath.WireToUnity(0f, 0f, 0f), 0f, "origin");
        }

        [Test]
        public void ImageToViewport_FlipsTheVerticalAxis() {
            Vector2 topLeft = VisionMath.ImageToViewport(new Vector3(0f, 0f, 0.3f));
            Vector2 bottomRight = VisionMath.ImageToViewport(new Vector3(1f, 1f, -0.3f));
            Assert.AreEqual(0f, topLeft.x);
            Assert.AreEqual(1f, topLeft.y, 0f, "the image's top is the viewport's top");
            Assert.AreEqual(1f, bottomRight.x);
            Assert.AreEqual(0f, bottomRight.y);
        }

        [Test]
        public void HeadRotationFromWire_YawPitchAndRollMoveTheFaceAsTheWireDocumentsThem() {
            float c = Mathf.Cos(30f * Mathf.Deg2Rad);
            float s = Mathf.Sin(30f * Mathf.Deg2Rad);
            Vector3 neutralLook = new Vector3(0f, 0f, -1f);

            // The sidecar's rotation for yaw +30 (the nose moves to the image's right): rows of a rotation about the vertical axis.
            Quaternion yaw = VisionMath.HeadRotationFromWire(new float[] { c, 0f, -s, 0f, 1f, 0f, s, 0f, c });
            AssertVector(new Vector3(s, 0f, -c), VisionMath.Rotate(yaw, neutralLook), Close, "yaw +30: the look direction moves toward +x");

            // Pitch +30 (the nose moves DOWN). On the wire y is down, so the look direction gains +y there; in Unity it must LOSE y.
            Quaternion pitch = VisionMath.HeadRotationFromWire(new float[] { 1f, 0f, 0f, 0f, c, -s, 0f, s, c });
            AssertVector(new Vector3(0f, -s, -c), VisionMath.Rotate(pitch, neutralLook), Close, "pitch +30: the nose goes down, Unity's y is up");

            // Roll +30 (the eye line tilts clockwise on screen): the head's right axis drops.
            Quaternion roll = VisionMath.HeadRotationFromWire(new float[] { c, -s, 0f, s, c, 0f, 0f, 0f, 1f });
            AssertVector(new Vector3(c, -s, 0f), VisionMath.Rotate(roll, Vector3.right), Close, "roll +30: clockwise on screen");
            AssertVector(neutralLook, VisionMath.Rotate(roll, neutralLook), Close, "the look direction is on the roll axis and does not move");
        }

        [Test]
        public void HeadRotationFromWire_IdentityAndBadInput() {
            Quaternion identity = VisionMath.HeadRotationFromWire(new float[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f });
            AssertVector(Vector3.right, VisionMath.Rotate(identity, Vector3.right), Close, "identity keeps x");
            AssertVector(Vector3.up, VisionMath.Rotate(identity, Vector3.up), Close, "identity keeps y");
            Assert.AreEqual(1f, identity.w, Close);
            Assert.AreEqual(Quaternion.identity.w, VisionMath.HeadRotationFromWire(null).w);
            Assert.AreEqual(Quaternion.identity.w, VisionMath.HeadRotationFromWire(new float[4]).w);
        }

        [Test]
        public void FromMatrix_IsRightInEveryBranchAndKeepsUnitLength() {
            float[] angles = { 0f, 35f, 90f, 135f, 170f, 180f, 200f };
            int axis = 0;
            while (axis < 3) {
                int a = 0;
                while (a < angles.Length) {
                    float r = angles[a] * Mathf.Deg2Rad;
                    float c = Mathf.Cos(r);
                    float s = Mathf.Sin(r);
                    Quaternion q;
                    if (axis == 0) {
                        q = VisionMath.FromMatrix(1f, 0f, 0f, 0f, c, -s, 0f, s, c);
                    } else if (axis == 1) {
                        q = VisionMath.FromMatrix(c, 0f, s, 0f, 1f, 0f, -s, 0f, c);
                    } else {
                        q = VisionMath.FromMatrix(c, -s, 0f, s, c, 0f, 0f, 0f, 1f);
                    }
                    Assert.AreEqual(1f, q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w, 1e-4f, "unit length, axis " + axis + " angle " + angles[a]);
                    Vector3 probe = new Vector3(0.3f, -0.5f, 0.8f);
                    Vector3 expected = axis == 0 ? new Vector3(probe.x, c * probe.y - s * probe.z, s * probe.y + c * probe.z)
                        : axis == 1 ? new Vector3(c * probe.x + s * probe.z, probe.y, -s * probe.x + c * probe.z)
                        : new Vector3(c * probe.x - s * probe.y, s * probe.x + c * probe.y, probe.z);
                    AssertVector(expected, VisionMath.Rotate(q, probe), 2e-4f, "rotating by " + angles[a] + " about axis " + axis);
                    a = a + 1;
                }
                axis = axis + 1;
            }
        }

        [Test]
        public void LookRotation_PointsZAtTheForwardVectorAndYTowardUp() {
            Quaternion q = VisionMath.LookRotation(new Vector3(0f, 0f, 1f), Vector3.up);
            AssertVector(new Vector3(0f, 0f, 1f), VisionMath.Rotate(q, new Vector3(0f, 0f, 1f)), Close, "forward +z is the identity");
            AssertVector(Vector3.up, VisionMath.Rotate(q, Vector3.up), Close, "identity keeps up");
            Quaternion toRight = VisionMath.LookRotation(Vector3.right, Vector3.up);
            AssertVector(Vector3.right, VisionMath.Rotate(toRight, new Vector3(0f, 0f, 1f)), Close, "z now points at +x");
            AssertVector(Vector3.up, VisionMath.Rotate(toRight, Vector3.up), Close, "and y stays up");
            Quaternion degenerate = VisionMath.LookRotation(Vector3.up, Vector3.up);
            AssertVector(Vector3.up, VisionMath.Rotate(degenerate, new Vector3(0f, 0f, 1f)), Close, "forward parallel to up still points z at forward");
        }

        [Test]
        public void PalmRotation_FollowsTheWristToKnuckleDirectionAndTheNormal() {
            Quaternion q = VisionMath.PalmRotation(Vector3.zero, new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f), new Vector3(-1f, 1f, 0f));
            AssertVector(Vector3.up, VisionMath.Rotate(q, new Vector3(0f, 0f, 1f)), Close, "the palm's forward is wrist -> middle knuckle");
            AssertVector(new Vector3(0f, 0f, -1f), VisionMath.Rotate(q, Vector3.up), Close, "its up is the normal, forward x (index - little)");
            Assert.AreEqual(Quaternion.identity.w, VisionMath.PalmRotation(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero).w, "degenerate landmarks: identity");
            Assert.AreEqual(Quaternion.identity.w, VisionMath.PalmRotation(Vector3.zero, new Vector3(1f, 1f, 0f), new Vector3(2f, 2f, 0f), new Vector3(1f, 1f, 0f)).w,
                "no across-vector: identity");
        }

        // ---------------------------------------------------------------- ARKit names

        [Test]
        public void ArkitBlendshapes_AreTheSidecarsFiftyTwoInTheSidecarsOrder() {
            string text = VisionFixtures.LoadText("blendshape_names.json");
            string[] parts = text.Split('"');
            int count = 0;
            int i = 1;
            while (i < parts.Length) {
                Assert.AreEqual(parts[i], ArkitBlendshapes.NameOf(count), "name " + count + " as the Python source of truth has it");
                count = count + 1;
                i = i + 2;
            }
            Assert.AreEqual(ArkitBlendshapes.Count, count);
            Assert.AreEqual(52, ArkitBlendshapes.Names.Length);
        }

        [Test]
        public void ArkitBlendshapes_EveryConstantIsItsNamesIndex() {
            FieldInfo[] fields = typeof(ArkitBlendshapes).GetFields(BindingFlags.Public | BindingFlags.Static);
            int constants = 0;
            int f = 0;
            while (f < fields.Length) {
                FieldInfo field = fields[f];
                f = f + 1;
                if (!field.IsLiteral || field.FieldType != typeof(int) || field.Name == "Count") {
                    continue;
                }
                int index = (int)field.GetRawConstantValue();
                string expected = field.Name == "Neutral" ? "_neutral" : char.ToLowerInvariant(field.Name[0]) + field.Name.Substring(1);
                Assert.AreEqual(expected, ArkitBlendshapes.NameOf(index), "constant " + field.Name);
                constants = constants + 1;
            }
            Assert.AreEqual(52, constants, "one constant per blendshape");
        }

        [Test]
        public void ArkitBlendshapes_IndexOfIgnoresCaseAndMirrorSwapsSides() {
            Assert.AreEqual(ArkitBlendshapes.JawOpen, ArkitBlendshapes.IndexOf("jawOpen"));
            Assert.AreEqual(ArkitBlendshapes.JawOpen, ArkitBlendshapes.IndexOf("JAWOPEN"));
            Assert.AreEqual(-1, ArkitBlendshapes.IndexOf("tongueOut"), "there is no tongueOut");
            Assert.AreEqual(-1, ArkitBlendshapes.IndexOf(null));
            Assert.AreEqual(-1, ArkitBlendshapes.IndexOf(string.Empty));
            Assert.AreEqual(ArkitBlendshapes.EyeBlinkRight, ArkitBlendshapes.Mirror(ArkitBlendshapes.EyeBlinkLeft));
            Assert.AreEqual(ArkitBlendshapes.EyeBlinkLeft, ArkitBlendshapes.Mirror(ArkitBlendshapes.EyeBlinkRight));
            Assert.AreEqual(ArkitBlendshapes.JawOpen, ArkitBlendshapes.Mirror(ArkitBlendshapes.JawOpen), "a shape with no side is its own mirror");
            Assert.AreEqual(ArkitBlendshapes.BrowInnerUp, ArkitBlendshapes.Mirror(ArkitBlendshapes.BrowInnerUp));
            int i = 0;
            while (i < ArkitBlendshapes.Count) {
                Assert.AreEqual(i, ArkitBlendshapes.Mirror(ArkitBlendshapes.Mirror(i)), "mirroring twice returns " + ArkitBlendshapes.NameOf(i));
                i = i + 1;
            }
            Assert.AreEqual(string.Empty, ArkitBlendshapes.NameOf(-1));
            Assert.AreEqual(string.Empty, ArkitBlendshapes.NameOf(52));
        }

        [Test]
        public void ArkitBlendshapeMap_MatchesExactPrefixedAndCaseVariedNames() {
            string[] names = { "Face.eyeBlinkLeft", "blendShape1.jawOpen", "Body_mouthSmileLeft", "EYEBLINKRIGHT", "smile", "myjawOpen", "browInnerUp", "cheekPuff" };
            int[] map = ArkitBlendshapeMap.Build(names, false);
            Assert.AreEqual(0, map[ArkitBlendshapes.EyeBlinkLeft]);
            Assert.AreEqual(1, map[ArkitBlendshapes.JawOpen]);
            Assert.AreEqual(2, map[ArkitBlendshapes.MouthSmileLeft]);
            Assert.AreEqual(3, map[ArkitBlendshapes.EyeBlinkRight], "case does not matter");
            Assert.AreEqual(6, map[ArkitBlendshapes.BrowInnerUp]);
            Assert.AreEqual(7, map[ArkitBlendshapes.CheekPuff]);
            Assert.AreEqual(6, ArkitBlendshapeMap.CountMapped(map), "'smile' and 'myjawOpen' (no separator before the name) match nothing");
            Assert.AreEqual(ArkitBlendshapeMap.NoShape, map[ArkitBlendshapes.MouthSmileRight]);
            Assert.AreEqual(ArkitBlendshapeMap.NoShape, map[ArkitBlendshapes.Neutral], "_neutral is not a shape a mesh has");
        }

        [Test]
        public void ArkitBlendshapeMap_ANameInsideALongerWordIsNotThatShape() {
            Assert.AreEqual(ArkitBlendshapes.JawOpen, ArkitBlendshapeMap.Match("Face.jawOpen"));
            Assert.AreEqual(-1, ArkitBlendshapeMap.Match("myjawOpen"), "no separator before the name");
            Assert.AreEqual(-1, ArkitBlendshapeMap.Match("xjawOpen"));
            Assert.AreEqual(ArkitBlendshapes.JawOpen, ArkitBlendshapeMap.Match("a b:jawOpen"), "a colon is a separator");
        }

        [Test]
        public void ArkitBlendshapeMap_MirrorSwapsSides_TheFirstShapeOfANameWins_AndNothingIsFine() {
            int[] mirrored = ArkitBlendshapeMap.Build(new string[] { "eyeBlinkLeft", "mouthSmileRight", "jawOpen" }, true);
            Assert.AreEqual(0, mirrored[ArkitBlendshapes.EyeBlinkRight], "the subject's left eye drives the character's right shape");
            Assert.AreEqual(1, mirrored[ArkitBlendshapes.MouthSmileLeft]);
            Assert.AreEqual(2, mirrored[ArkitBlendshapes.JawOpen], "sideless shapes are unaffected");
            Assert.AreEqual(ArkitBlendshapeMap.NoShape, mirrored[ArkitBlendshapes.EyeBlinkLeft]);
            int[] duplicate = ArkitBlendshapeMap.Build(new string[] { "jawOpen", "Face.jawOpen" }, false);
            Assert.AreEqual(0, duplicate[ArkitBlendshapes.JawOpen], "the first wins");
            Assert.AreEqual(0, ArkitBlendshapeMap.CountMapped(ArkitBlendshapeMap.Build(null, false)));
            Assert.AreEqual(0, ArkitBlendshapeMap.CountMapped(ArkitBlendshapeMap.Build(new string[0], false)));
            Assert.AreEqual(0, ArkitBlendshapeMap.CountMapped(null));
            Assert.AreEqual(-1, ArkitBlendshapeMap.Match(null));
            Assert.AreEqual(-1, ArkitBlendshapeMap.Match("_neutral"));
        }

        // ---------------------------------------------------------------- HandFrame bridge

        [Test]
        public void HandFrameMapper_PutsEachHandOnItsSideWithItsCurlsAndAPalmRotation() {
            HandFrame frame = new HandFrame();
            HandFrameMapper.Fill(frame, HandsFrom("hands_two.json"), false, 12.5);
            Assert.IsTrue(frame.IsValid);
            Assert.AreEqual(12.5, frame.TimestampSeconds);
            Assert.Greater(frame.LeftIndexCurl, 0.75f, "the wire's Left hand is the fist");
            Assert.Less(frame.RightIndexCurl, 0.2f, "the wire's Right hand is open");
            Assert.Less(frame.RightThumbCurl, 0.2f);
            Assert.IsTrue(frame.LeftWristTracked);
            Assert.IsTrue(frame.RightWristTracked);
            Assert.AreEqual(1f, Quaternion.Dot(frame.RightWristRotation, frame.RightWristRotation), 1e-4f, "a unit quaternion");
        }

        [Test]
        public void HandFrameMapper_SwapSidesExchangesLeftAndRight() {
            HandFrame frame = new HandFrame();
            HandFrameMapper.Fill(frame, HandsFrom("hands_two.json"), true, 1.0);
            Assert.Less(frame.LeftIndexCurl, 0.2f, "swapped: the open hand is now Left");
            Assert.Greater(frame.RightIndexCurl, 0.75f);
        }

        [Test]
        public void HandFrameMapper_ASideWithNoHandIsUntrackedWithZeroCurls_AndNoHandsIsInvalid() {
            HandsMessage single = HandsFrom("hands_two.json");
            single.Count = 1;
            HandFrame frame = new HandFrame();
            HandFrameMapper.Fill(frame, single, false, 2.0);
            Assert.IsTrue(frame.IsValid, "one hand is enough");
            Assert.IsTrue(frame.RightWristTracked);
            Assert.IsFalse(frame.LeftWristTracked);
            Assert.AreEqual(0f, frame.LeftIndexCurl);
            HandFrameMapper.Fill(frame, HandsFrom("hands_empty.json"), false, 3.0);
            Assert.IsFalse(frame.IsValid);
            Assert.IsFalse(frame.LeftWristTracked);
            Assert.IsFalse(frame.RightWristTracked);
            Assert.AreEqual(0f, frame.RightIndexCurl);
            Assert.AreEqual(3.0, frame.TimestampSeconds);
        }

        // ---------------------------------------------------------------- FaceFrame bridge

        [Test]
        public void FaceFrameMapper_UsesTheSameFormulasAsTheMediaPipeProvider() {
            FaceFrame frame = new FaceFrame();
            FaceFrameMapper.Fill(frame, FaceFrom("face_full.json"), false, 4.0);
            Assert.IsTrue(frame.IsValid);
            Assert.AreEqual(4.0, frame.TimestampSeconds);
            Assert.AreEqual(0.9f, frame.BlinkLeft, 1e-3f);
            Assert.AreEqual(0.1f, frame.BlinkRight, 1e-3f);
            Assert.AreEqual(0.7f, frame.MouthOpen, 1e-3f, "mouthOpen = jawOpen");
            Assert.AreEqual(0.6f, frame.Smile, 1e-3f, "smile = max(mouthSmileLeft .6, mouthSmileRight .4)");
            Assert.AreEqual(0.3f, frame.Angry, 1e-3f, "angry = max(browDownLeft .3, browDownRight .2)");
            Assert.AreEqual(0.5f, frame.Surprised, 1e-3f, "surprised = max(browInnerUp .5, eyeWideLeft .25, eyeWideRight .35)");
        }

        [Test]
        public void FaceFrameMapper_SurprisedFollowsWhicheverOfBrowAndEyesIsWidest() {
            FaceMessage message = FaceFrom("face_full.json");
            message.Face(0).Blendshapes[ArkitBlendshapes.BrowInnerUp] = 0.1f;
            message.Face(0).Blendshapes[ArkitBlendshapes.EyeWideLeft] = 0.8f;
            message.Face(0).Blendshapes[ArkitBlendshapes.EyeWideRight] = 0.2f;
            FaceFrame frame = new FaceFrame();
            FaceFrameMapper.Fill(frame, message, false, 1.0);
            Assert.AreEqual(0.8f, frame.Surprised, 1e-3f);
            message.Face(0).Blendshapes[ArkitBlendshapes.EyeWideLeft] = 0.2f;
            message.Face(0).Blendshapes[ArkitBlendshapes.EyeWideRight] = 0.9f;
            FaceFrameMapper.Fill(frame, message, false, 1.0);
            Assert.AreEqual(0.9f, frame.Surprised, 1e-3f);
        }

        [Test]
        public void FaceFrameMapper_SwapSidesExchangesTheEyes() {
            FaceFrame frame = new FaceFrame();
            FaceFrameMapper.Fill(frame, FaceFrom("face_full.json"), true, 1.0);
            Assert.AreEqual(0.1f, frame.BlinkLeft, 1e-3f);
            Assert.AreEqual(0.9f, frame.BlinkRight, 1e-3f);
            Assert.AreEqual(0.7f, frame.MouthOpen, 1e-3f, "the mouth has no side");
        }

        [Test]
        public void FaceFrameMapper_NoFaceOrNoBlendshapesIsAnInvalidNeutralFrame() {
            FaceFrame frame = new FaceFrame();
            FaceFrameMapper.Fill(frame, FaceFrom("face_full.json"), false, 1.0);
            Assert.IsTrue(frame.IsValid);
            FaceFrameMapper.Fill(frame, FaceFrom("face_empty.json"), false, 2.0);
            Assert.IsFalse(frame.IsValid);
            Assert.AreEqual(0f, frame.MouthOpen);
            Assert.AreEqual(0f, frame.BlinkLeft);
            FaceFrameMapper.Fill(frame, FaceFrom("face_full.json"), false, 3.0);
            FaceFrameMapper.Fill(frame, FaceFrom("face_no_blendshapes.json"), false, 4.0);
            Assert.IsFalse(frame.IsValid, "a sender running without the blendshape network cannot drive an expression");
            Assert.AreEqual(0f, frame.Smile);
        }

        // ---------------------------------------------------------------- providers

        private static void Send(int port, byte[] data) {
            UdpClient client = new UdpClient();
            try {
                client.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, port));
            } finally {
                client.Close();
            }
        }

        private static void TickUntil(IHandTrackingProvider provider, Func<bool> condition) {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(3000);
            while (!condition()) {
                provider.Tick(0.016f);
                if (DateTime.UtcNow > deadline) {
                    Assert.Fail("timed out");
                }
                Thread.Sleep(2);
            }
        }

        [Test]
        public void UdpHandTrackingProvider_DeliversValidFramesThenGoesInvalidWhenTheStreamGoesStale() {
            Clock clock = new Clock();
            HandsReceiver receiver = new HandsReceiver(0, true, 0.5, null, clock.Now, 4);
            using (UdpHandTrackingProvider provider = new UdpHandTrackingProvider(receiver, true, false)) {
                HandFrame frame;
                Assert.IsFalse(provider.TryGetFrame(out frame), "no frame before the first datagram");
                Assert.IsFalse(provider.IsTracking);
                Assert.IsTrue(provider.StartTracking());
                Assert.IsTrue(provider.StartTracking(), "starting twice is harmless");
                Send(receiver.Port, VisionFixtures.Load("hands_two.json"));
                TickUntil(provider, delegate () { return provider.TryGetFrame(out frame); });
                Assert.IsTrue(frame.IsValid);
                Assert.Greater(frame.LeftIndexCurl, 0.75f);
                Assert.IsTrue(provider.IsTracking);
                clock.Seconds = 5.0;
                provider.Tick(0.016f);
                Assert.IsFalse(provider.IsTracking, "5 s of silence: the stream is stale");
                Assert.IsTrue(provider.TryGetFrame(out frame), "a frame is still reported ...");
                Assert.IsFalse(frame.IsValid, "... but it says no hand is in view, so the avatar's fingers reset instead of freezing");
                Send(receiver.Port, VisionFixtures.Bytes(VisionFixtures.LoadText("hands_empty.json").Replace("\"seq\":1042", "\"seq\":1043")));
                clock.Seconds = 5.1;
                TickUntil(provider, delegate () { return receiver.Accepted >= 2; });
                provider.Tick(0.016f);
                Assert.IsTrue(provider.TryGetFrame(out frame));
                Assert.IsFalse(frame.IsValid, "a datagram with no hands is a live stream with nobody in it");
                Assert.IsTrue(provider.IsTracking);
                provider.StopTracking();
                Assert.IsFalse(provider.IsTracking);
            }
        }

        [Test]
        public void UdpFaceTrackingProvider_DeliversExpressionsThenResetsWhenTheStreamGoesStale() {
            Clock clock = new Clock();
            FaceReceiver receiver = new FaceReceiver(0, true, 0.5, null, clock.Now, 4);
            using (UdpFaceTrackingProvider provider = new UdpFaceTrackingProvider(receiver, true, false)) {
                FaceFrame frame;
                Assert.IsFalse(provider.TryGetFrame(out frame));
                Assert.IsTrue(provider.StartTracking());
                Send(receiver.Port, VisionFixtures.Load("face_full.json"));
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(3000);
                while (!provider.TryGetFrame(out frame)) {
                    provider.Tick(0.016f);
                    Assert.Less(DateTime.UtcNow, deadline, "timed out");
                    Thread.Sleep(2);
                }
                Assert.IsTrue(frame.IsValid);
                Assert.AreEqual(0.7f, frame.MouthOpen, 1e-3f);
                Assert.IsTrue(provider.IsTracking);
                clock.Seconds = 5.0;
                provider.Tick(0.016f);
                Assert.IsFalse(provider.IsTracking);
                Assert.IsFalse(frame.IsValid, "stale: the expression retargeter will reset the avatar to neutral");
            }
        }

        [Test]
        public void ABorrowedReceiverSurvivesTheProvider() {
            HandsReceiver receiver = new HandsReceiver(0, true, 0.5, null, null, 4);
            Assert.IsTrue(receiver.Start());
            UdpHandTrackingProvider provider = new UdpHandTrackingProvider(receiver, false, false);
            provider.Dispose();
            Assert.IsTrue(receiver.IsRunning, "the provider does not own it, so it does not stop it");
            Assert.AreSame(receiver, provider.Receiver);
            receiver.Dispose();
        }
    }
}
