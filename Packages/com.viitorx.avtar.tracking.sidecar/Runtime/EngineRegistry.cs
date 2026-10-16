using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VirtualMirror.Tracking.OakD {
    /// <summary>
    /// Reads where the installer put the engine (<c>HKLM\Software\Viitorx\TrackingEngine\InstallDir</c>). Windows only; empty everywhere else and on any error.
    /// Raw advapi32 calls rather than <c>Microsoft.Win32.Registry</c>: that assembly is not part of Unity's default .NET Standard 2.1 profile, so a
    /// project on the default setting could not compile this package with it (found by the first compile in a fresh project).
    /// </summary>
    public static class EngineRegistry {
        private const int RegSz = 1;
        private const int RegExpandSz = 2;
        private const uint KeyRead = 0x20019;
        private const uint Wow6464Key = 0x0100;
        private static readonly IntPtr HkeyLocalMachine = new IntPtr(unchecked((int)0x80000002));

        public static string ReadInstallDir() {
            return ReadMachineString(EngineLocator.RegistryKeyPath, EngineLocator.RegistryValueName);
        }

        /// <summary>A text value under HKEY_LOCAL_MACHINE (64-bit view), or empty when the key or the value is missing, is not text, or anything goes wrong.</summary>
        public static string ReadMachineString(string subKey, string valueName) {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            IntPtr key = IntPtr.Zero;
            try {
                if (RegOpenKeyEx(HkeyLocalMachine, subKey, 0, KeyRead | Wow6464Key, out key) != 0) {
                    return string.Empty;
                }
                uint type;
                uint bytes = 0;
                if (RegQueryValueEx(key, valueName, IntPtr.Zero, out type, null, ref bytes) != 0 || bytes == 0) {
                    return string.Empty;
                }
                if (type != RegSz && type != RegExpandSz) {
                    return string.Empty;
                }
                StringBuilder buffer = new StringBuilder((int)(bytes / 2) + 1);
                if (RegQueryValueEx(key, valueName, IntPtr.Zero, out type, buffer, ref bytes) != 0) {
                    return string.Empty;
                }
                return buffer.ToString().TrimEnd('\0');
            } catch (Exception) {
                return string.Empty;
            } finally {
                if (key != IntPtr.Zero) {
                    RegCloseKey(key);
                }
            }
#else
            return string.Empty;
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
        private static extern int RegOpenKeyEx(IntPtr hKey, string subKey, uint options, uint samDesired, out IntPtr result);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegQueryValueExW")]
        private static extern int RegQueryValueEx(IntPtr hKey, string valueName, IntPtr reserved, out uint type, StringBuilder data, ref uint dataSize);

        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr hKey);
#endif
    }
}
