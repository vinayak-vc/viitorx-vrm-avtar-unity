using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

using UnityEngine;

namespace VirtualMirror.Tests {
    /// <summary>
    /// Plays the engine for a test: sends body datagrams of the body wire (docs/WIRE.md section 6) over loopback UDP, about 30 per second of real time, from
    /// whatever pose it is given. Camera space on the wire is X right, Y down, Z forward; the points are hip-relative metres. Public so another test assembly
    /// (the application's) can drive its own receiver with exactly the same datagrams.
    /// </summary>
    public sealed class BodyWireSender : IDisposable {
        private const float SendIntervalSeconds = 0.033f;

        private readonly UdpClient client = new UdpClient();
        private readonly IPEndPoint target;
        private readonly string session = "wire-test-" + Guid.NewGuid().ToString("N");
        private long seq;
        private float nextAt;

        /// <summary>True: arms out to the sides (level). False: arms hanging at the sides.</summary>
        public bool ArmsOut;

        /// <summary>0 = no hand sent, 1 = open, 2 = fist.</summary>
        public int LeftHand;
        public int RightHand;

        public BodyWireSender(int port) {
            target = new IPEndPoint(IPAddress.Loopback, port);
        }

        public long Sent {
            get {
                return seq;
            }
        }

        /// <summary>Sends a datagram when 33 ms of real time have passed since the last one. Call it every frame.</summary>
        public void Pump() {
            if (Time.realtimeSinceStartup < nextAt) {
                return;
            }
            nextAt = Time.realtimeSinceStartup + SendIntervalSeconds;
            seq = seq + 1;
            byte[] data = Encoding.UTF8.GetBytes(Datagram());
            client.Send(data, data.Length, target);
        }

        public void Dispose() {
            client.Close();
        }

        private string Datagram() {
            StringBuilder sb = new StringBuilder(4096);
            sb.Append("{\"lm\":[");
            Vector3[] body = Body(ArmsOut);
            for (int i = 0; i < body.Length; i++) {
                if (i > 0) {
                    sb.Append(',');
                }
                sb.Append('[').Append(F(body[i].x)).Append(',').Append(F(body[i].y)).Append(',').Append(F(body[i].z)).Append(",0.99]");
            }
            sb.Append(']');
            if (LeftHand != 0) {
                sb.Append(",\"lh\":").Append(Hand(body[15], LeftHand == 2));
            }
            if (RightHand != 0) {
                sb.Append(",\"rh\":").Append(Hand(body[16], RightHand == 2));
            }
            sb.Append(",\"xyz\":[0,0,2500],\"seq\":").Append(seq);
            sb.Append(",\"sid\":\"").Append(session).Append('"');
            sb.Append(",\"t\":").Append(F((float)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0)));
            sb.Append('}');
            return sb.ToString();
        }

        private static string F(float value) {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        // A person standing square to the camera (their LEFT side at +X of the picture, as on a real camera), 33 BlazePose points. Index 11/12 shoulders, 13/14 elbows, 15/16 wrists, 23/24 hips, 25/26 knees, 27/28 ankles.
        private static Vector3[] Body(bool armsOut) {
            Vector3[] p = new Vector3[33];
            p[0] = new Vector3(0f, -0.66f, 0f);
            for (int i = 1; i <= 10; i++) {
                p[i] = new Vector3(i <= 6 ? (i <= 3 ? -0.03f : 0.03f) : (i == 7 ? -0.1f : (i == 8 ? 0.1f : (i == 9 ? -0.03f : 0.03f))), -0.68f, -0.03f);
            }
            float sx = 0.18f;
            p[11] = new Vector3(sx, -0.5f, 0f);
            p[12] = new Vector3(-sx, -0.5f, 0f);
            if (armsOut) {
                p[13] = new Vector3(0.45f, -0.5f, 0f);
                p[14] = new Vector3(-0.45f, -0.5f, 0f);
                p[15] = new Vector3(0.72f, -0.5f, 0f);
                p[16] = new Vector3(-0.72f, -0.5f, 0f);
            } else {
                p[13] = new Vector3(0.2f, -0.22f, 0f);
                p[14] = new Vector3(-0.2f, -0.22f, 0f);
                p[15] = new Vector3(0.2f, 0.05f, 0f);
                p[16] = new Vector3(-0.2f, 0.05f, 0f);
            }
            for (int i = 17; i <= 22; i++) {
                p[i] = p[i % 2 == 1 ? 15 : 16];
            }
            p[23] = new Vector3(0.1f, 0f, 0f);
            p[24] = new Vector3(-0.1f, 0f, 0f);
            p[25] = new Vector3(0.1f, 0.45f, 0f);
            p[26] = new Vector3(-0.1f, 0.45f, 0f);
            p[27] = new Vector3(0.1f, 0.88f, 0f);
            p[28] = new Vector3(-0.1f, 0.88f, 0f);
            p[29] = p[27] + new Vector3(0f, 0.03f, 0f);
            p[30] = p[28] + new Vector3(0f, 0.03f, 0f);
            p[31] = p[27] + new Vector3(0f, 0.03f, -0.1f);
            p[32] = p[28] + new Vector3(0f, 0.03f, -0.1f);
            return p;
        }

        // 21 hand points at the wrist: the palm's knuckles in a row, and for each finger the three joints the curl is measured from. An open finger is
        // straight; a fist bends each finger 160 degrees at its middle joint, the angle the receiver reads as a full curl.
        private static string Hand(Vector3 wrist, bool fist) {
            Vector3[] h = new Vector3[21];
            for (int i = 0; i < 21; i++) {
                h[i] = wrist;
            }
            int[][] chains = {
                new int[] { 2, 3, 4 },
                new int[] { 5, 6, 8 },
                new int[] { 9, 10, 12 },
                new int[] { 13, 14, 16 },
                new int[] { 17, 18, 20 }
            };
            float[] lateral = { -0.04f, -0.02f, 0f, 0.02f, 0.04f };
            for (int f = 0; f < chains.Length; f++) {
                Vector3 baseP = wrist + new Vector3(lateral[f], -0.09f, 0f);
                Vector3 lower = new Vector3(0f, -0.03f, 0f);
                float bend = fist ? 160f : 0f;
                Vector3 upper = Quaternion.AngleAxis(bend, Vector3.right) * lower;
                h[chains[f][0]] = baseP;
                h[chains[f][1]] = baseP + lower;
                h[chains[f][2]] = baseP + lower + upper;
            }
            StringBuilder sb = new StringBuilder(1024);
            sb.Append('[');
            for (int i = 0; i < 21; i++) {
                if (i > 0) {
                    sb.Append(',');
                }
                sb.Append('[').Append(F(h[i].x)).Append(',').Append(F(h[i].y)).Append(',').Append(F(h[i].z)).Append(']');
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
