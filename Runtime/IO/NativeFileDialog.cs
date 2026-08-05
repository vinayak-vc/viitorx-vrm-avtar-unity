using System;
using System.Runtime.InteropServices;

namespace VirtualMirror.IO {
    /// <summary>
    /// Native Windows file dialog helper using Win32 GetOpenFileName API.
    /// </summary>
    public static class NativeFileDialog {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct OpenFileName {
            public int structSize;
            public IntPtr dlgOwner;
            public IntPtr instance;
            public string filter;
            public string customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public string file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDir;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr reservedPtr;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileName([In, Out] ref OpenFileName ofn);

        public static string OpenFile(string title, string filterPattern, string defaultExtension) {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = filterPattern;
            ofn.file = new string(new char[512]);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string(new char[128]);
            ofn.maxFileTitle = ofn.fileTitle.Length;
            ofn.initialDir = UnityEngine.Application.streamingAssetsPath;
            ofn.title = title;
            ofn.defExt = defaultExtension;
            ofn.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000200 | 0x00000008; // OFN_EXPLORER | OFN_FILEMUSTEXIST

            if (GetOpenFileName(ref ofn)) {
                return ofn.file;
            }
#endif
            return null;
        }
    }
}
