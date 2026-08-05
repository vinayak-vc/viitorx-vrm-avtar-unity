using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VirtualMirror.IO {
    /// <summary>
    /// File and folder selection dialog helper leveraging WindowsFileDialog.dll native plugin.
    /// Supports Unicode paths and extension filtering.
    /// </summary>
    public static class FileExplorer {
        private const string WindowsFileDialog = "WindowsFileDialog";

        [DllImport(WindowsFileDialog, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenWindowsFile(string initialDir);

        [DllImport(WindowsFileDialog, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenFileWithExtension(IntPtr filterPtr, string initialDir);

        [DllImport(WindowsFileDialog, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenFolderDialog(string initialDir);

        public static string OpenFileExplorer(string initialDir = null) {
            try {
                IntPtr ptr = OpenWindowsFile(initialDir);
                return Marshal.PtrToStringUni(ptr);
            } catch (Exception) {
                return null;
            }
        }

        public static string OpenFileExplorer(string filter, string initialDir = null) {
            if (string.IsNullOrEmpty(filter)) {
                return OpenFileExplorer(initialDir);
            }

            string formattedFilter = filter.Replace('|', '\0') + "\0\0";
            byte[] filterBytes = Encoding.Unicode.GetBytes(formattedFilter);
            IntPtr filterPtr = Marshal.AllocHGlobal(filterBytes.Length);

            try {
                Marshal.Copy(filterBytes, 0, filterPtr, filterBytes.Length);
                IntPtr ptr = OpenFileWithExtension(filterPtr, initialDir);
                string result = Marshal.PtrToStringUni(ptr);
                if (string.IsNullOrEmpty(result)) {
                    return OpenFileExplorer(initialDir);
                }
                return result;
            } catch (Exception) {
                return OpenFileExplorer(initialDir);
            } finally {
                Marshal.FreeHGlobal(filterPtr);
            }
        }

        public static string OpenFolder(string initialDir = null) {
            try {
                IntPtr ptr = OpenFolderDialog(initialDir);
                return Marshal.PtrToStringUni(ptr);
            } catch (Exception) {
                return null;
            }
        }
    }
}
