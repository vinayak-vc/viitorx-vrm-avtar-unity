using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace VirtualMirror.Tests {
    /// <summary>
    /// The golden datagrams the Python sidecar's encoders produced (python-sidecar~/tools/video/make_vision_fixtures.py), read from
    /// <c>VisionFixtures</c> next to THIS file (not next to the caller: tests of other packages use them too). The path comes from the compiler
    /// (<c>CallerFilePath</c>), so it works in the Editor and in a headless runner without <c>Application.dataPath</c>.
    /// </summary>
    public static class VisionFixtures {
        private static readonly string Directory = ResolveDirectory();

        private static string ResolveDirectory([CallerFilePath] string ownPath = "") {
            return Path.Combine(Path.GetDirectoryName(ownPath), "VisionFixtures");
        }

        public static byte[] Load(string name) {
            return File.ReadAllBytes(Path.Combine(Directory, name));
        }

        public static string LoadText(string name) {
            return Encoding.UTF8.GetString(Load(name));
        }

        /// <summary>The fixture as a buffer larger than the datagram, as a receiver's socket buffer is: the parser must honour the length, not the array.</summary>
        public static byte[] LoadPadded(string name, out int length) {
            byte[] data = Load(name);
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
