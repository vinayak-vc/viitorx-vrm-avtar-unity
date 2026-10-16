using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace VirtualMirror.Tests {
    /// <summary>
    /// The command lines of the engine's body command that <c>BodyEngineArguments</c> must produce, read from <c>BodyFixtures</c> next to THIS file. The Python
    /// side (python-sidecar~/tests/test_body_cli_contract.py) parses the same files with the body sender's own argparse parser.
    /// </summary>
    public static class BodyFixtures {
        private static readonly string Directory = ResolveDirectory();

        private static string ResolveDirectory([CallerFilePath] string ownPath = "") {
            return Path.Combine(Path.GetDirectoryName(ownPath), "BodyFixtures");
        }

        public static string LoadText(string name) {
            return Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(Directory, name)));
        }
    }
}
