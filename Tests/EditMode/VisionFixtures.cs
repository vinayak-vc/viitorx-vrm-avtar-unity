using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace VirtualMirror.Tests {
    /// <summary>
    /// The golden datagrams the Python sidecar's encoders produced (python-sidecar~/tools/video/make_vision_fixtures.py), read from
    /// <c>Tests/EditMode/VisionFixtures</c> next to this file. The path comes from the compiler (<c>CallerFilePath</c>), so it works in the Editor and in
    /// a headless runner without <c>Application.dataPath</c>.
    /// </summary>
    internal static class VisionFixtures {
        public static byte[] Load(string name, [CallerFilePath] string callerPath = "") {
            string directory = Path.Combine(Path.GetDirectoryName(callerPath), "VisionFixtures");
            return File.ReadAllBytes(Path.Combine(directory, name));
        }

        public static string LoadText(string name, [CallerFilePath] string callerPath = "") {
            return Encoding.UTF8.GetString(Load(name, callerPath));
        }

        /// <summary>The fixture as a buffer larger than the datagram, as a receiver's socket buffer is: the parser must honour the length, not the array.</summary>
        public static byte[] LoadPadded(string name, out int length, [CallerFilePath] string callerPath = "") {
            byte[] data = Load(name, callerPath);
            length = data.Length;
            byte[] padded = new byte[data.Length + 4096];
            Array.Copy(data, padded, data.Length);
            int i = data.Length;
            while (i < padded.Length) {
                padded[i] = (byte)'x';
                i = i + 1;
            }
            return padded;
        }

        public static byte[] Bytes(string text) {
            return Encoding.UTF8.GetBytes(text);
        }
    }
}
