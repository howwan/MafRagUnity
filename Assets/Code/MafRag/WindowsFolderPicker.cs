// Windows 原生文件夹选择对话框（D10 / FR-1.1 / FR-10.1）。
// 仅在 Windows 平台编译/调用；Android 端走路径输入 + 内置知识库（见 SettingsSceneUI）。

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MafRag
{
    public static class WindowsFolderPicker
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct BROWSEINFO
        {
            public IntPtr hwndOwner;
            public IntPtr pidlRoot;
            public IntPtr pszDisplayName;
            public string lpszTitle;
            public uint ulFlags;
            public IntPtr lpfn;
            public IntPtr lParam;
            public int iImage;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        // 弹出系统文件夹选择框，返回所选绝对路径；用户取消返回 null。
        public static string Show()
        {
            var bi = new BROWSEINFO
            {
                lpszTitle = "选择 Markdown 文件夹",
                ulFlags = 0x0001 // BIF_RETURNONLYFSDIRS
            };
            IntPtr pidl = SHBrowseForFolder(ref bi);
            if (pidl == IntPtr.Zero) return null;
            var sb = new StringBuilder(260);
            string path = SHGetPathFromIDList(pidl, sb) ? sb.ToString() : null;
            CoTaskMemFree(pidl);
            return path;
        }
#else
        public static string Show() => null;
#endif
    }
}
