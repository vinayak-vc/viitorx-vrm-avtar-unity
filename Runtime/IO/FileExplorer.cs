using System;
using System.Runtime.InteropServices;

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
        private static extern IntPtr OpenFileWithExtension(string filter, string initialDir);

        [DllImport(WindowsFileDialog, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenFolderDialog(string initialDir);

        public static string OpenFileExplorer(string initialDir = null) {
            IntPtr ptr = OpenWindowsFile(initialDir);
            return Marshal.PtrToStringUni(ptr);
        }

        public static string OpenFileExplorer(string filter, string initialDir = null) {
            IntPtr ptr = OpenFileWithExtension(filter, initialDir);
            return Marshal.PtrToStringUni(ptr);
        }

        public static string OpenFolder(string initialDir = null) {
            IntPtr ptr = OpenFolderDialog(initialDir);
            return Marshal.PtrToStringUni(ptr);
        }
    }
}
