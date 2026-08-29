using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace NxWebUITool
{
    /// <summary>
    /// Detects typing / IME so Space stays a character instead of opening
    /// the radial overlay. NX Block Styler and Qt often have no Win32 "Edit"
    /// class; Chinese IMEs hide the caret and host the candidate UI in
    /// another process (TextInputHost), so class-name checks alone miss them.
    /// </summary>
    public static class NxUiFocus
    {
        const int VkSpace = 0x20;
        const int VkShift = 0x10;
        const int VkControl = 0x11;
        const int VkMenu = 0x12;
        const int VkLWin = 0x5B;
        const int VkRWin = 0x5C;
        const int WmGetDlgCode = 0x0087;
        const int DlgcWantChars = 0x0080;
        const int DlgcHasSetSel = 0x0008;
        const int GuiCaretBlinking = 0x00000001;
        const uint GcsCompStr = 8;
        const uint ObjIdClient = 0xFFFFFFFC;
        const int RoleSystemText = 42;
        const int RoleSystemComboBox = 46;
        const uint GaRoot = 2;

        public const string SlotsWindowTitle = "NxWebUITool.Slots";

        /// <summary>
        /// Cheap checks safe to run inside WH_KEYBOARD_LL. Must not EnumWindows
        /// or open processes — those deadlock or stall NX when IME is up.
        /// </summary>
        public static bool ShouldSuppressRadialFast()
        {
            try
            {
                // Do not treat a blinking caret as typing. NX command dialogs
                // always have a caret in the current parameter, which blocked
                // hold-Space during Extrude etc.
                if (IsModifierDown()) return true;
                if (IsEditClassFocused()) return true;
                if (IsImeComposing()) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// HUD（空格环形菜单）级抑制：只有修饰键与 IME 组字会拦。文本输入框
        /// 不再抑制弹出——按住期间的空间字符由 RadialSpaceHook 吞掉并按需
        /// 重放，短按仍是普通空格；Edit 类判定只保留给旧的保守路径。
        /// </summary>
        public static bool ShouldSuppressRadialHud()
        {
            try
            {
                if (IsModifierDown()) return true;
                if (IsImeComposing()) return true;
                if (IsSlotsEditorForeground()) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool ShouldSuppressRadial()
        {
            try
            {
                if (ShouldSuppressRadialFast()) return true;
                if (IsAccessibleTextFocused()) return true;
                if (IsImeCandidateVisible()) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsSlotsEditorTitle(string title)
        {
            return string.Equals(title, SlotsWindowTitle, StringComparison.Ordinal);
        }

        /// <summary>
        /// Cheap: the slot editor is a same-process WinForms dialog. Space must
        /// stay a search character there instead of opening the radial overlay.
        /// </summary>
        public static bool IsSlotsEditorForeground()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;
                if (WindowTitleEquals(hwnd, SlotsWindowTitle)) return true;
                var root = GetAncestor(hwnd, GaRoot);
                if (root != IntPtr.Zero && WindowTitleEquals(root, SlotsWindowTitle)) return true;
                var depth = 0;
                while (hwnd != IntPtr.Zero && depth < 16)
                {
                    if (WindowTitleEquals(hwnd, SlotsWindowTitle)) return true;
                    hwnd = GetParent(hwnd);
                    depth++;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool WindowTitleEquals(IntPtr hwnd, string title)
        {
            if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(title)) return false;
            var buffer = new StringBuilder(256);
            if (GetWindowText(hwnd, buffer, buffer.Capacity) <= 0) return false;
            return string.Equals(buffer.ToString(), title, StringComparison.Ordinal);
        }

        public static bool IsSpaceDown()
        {
            return (GetAsyncKeyState(VkSpace) & 0x8000) != 0;
        }

        public static bool IsModifierDown()
        {
            return (GetAsyncKeyState(VkShift) & 0x8000) != 0
                || (GetAsyncKeyState(VkControl) & 0x8000) != 0
                || (GetAsyncKeyState(VkMenu) & 0x8000) != 0
                || (GetAsyncKeyState(VkLWin) & 0x8000) != 0
                || (GetAsyncKeyState(VkRWin) & 0x8000) != 0;
        }

        public static bool IsTextInputFocused()
        {
            try
            {
                var foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;
                uint unusedPid;
                var threadId = GetWindowThreadProcessId(foreground, out unusedPid);
                var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
                if (GetGUIThreadInfo(threadId, ref info))
                {
                    if ((info.flags & GuiCaretBlinking) != 0) return true;
                    if (info.hwndCaret != IntPtr.Zero) return true;
                    if (LooksLikeTextWindow(info.hwndFocus)) return true;
                    if (LooksLikeTextWindow(info.hwndCaret)) return true;
                }
                return LooksLikeTextWindow(GetFocus());
            }
            catch
            {
                return false;
            }
        }

        public static bool IsEditClassName(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            if (className.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.IndexOf("Scintilla", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.Equals("ComboBox", StringComparison.OrdinalIgnoreCase)) return true;
            if (className.StartsWith("QLine", StringComparison.OrdinalIgnoreCase)) return true;
            if (className.StartsWith("QPlainText", StringComparison.OrdinalIgnoreCase)) return true;
            if (className.StartsWith("QText", StringComparison.OrdinalIgnoreCase)) return true;
            if (className.StartsWith("QCombo", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsImeUiClassName(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            if (className.Equals("IME", StringComparison.OrdinalIgnoreCase)) return true;
            if (className.StartsWith("IME", StringComparison.OrdinalIgnoreCase)) return true;
            if (className.IndexOf("MSCTFIME", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.IndexOf("Candidate", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.IndexOf("Composition", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.IndexOf("Sogou", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.IndexOf("QQPinyin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.IndexOf("soPY", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.IndexOf("BaiduPin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool IsImeProcessName(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            var name = processName;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            if (name.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("ctfmon", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("ChsIME", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.IndexOf("Sogou", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("QQPinyin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("BaiduPinyin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("iFly", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.Equals("WeaselServer", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("Rime", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool WantsTextInputDlgCode(int dlgCode)
        {
            return (dlgCode & (DlgcWantChars | DlgcHasSetSel)) != 0;
        }

        public static bool IsTextAccRole(int role)
        {
            return role == RoleSystemText || role == RoleSystemComboBox;
        }

        static bool IsEditClassFocused()
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            uint unusedPid;
            var threadId = GetWindowThreadProcessId(foreground, out unusedPid);
            var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
            if (GetGUIThreadInfo(threadId, ref info))
            {
                if (IsEditClass(info.hwndFocus)) return true;
                if (IsEditClass(info.hwndCaret)) return true;
            }
            return IsEditClass(GetFocus());
        }

        static bool LooksLikeTextWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (IsEditClass(hwnd)) return true;
            var dlgCode = SendMessage(hwnd, WmGetDlgCode, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (WantsTextInputDlgCode(dlgCode)) return true;
            return false;
        }

        static bool IsEditClass(IntPtr hwnd)
        {
            var buffer = new StringBuilder(256);
            var depth = 0;
            while (hwnd != IntPtr.Zero && depth < 8)
            {
                buffer.Clear();
                if (GetClassName(hwnd, buffer, buffer.Capacity) > 0
                    && IsEditClassName(buffer.ToString()))
                    return true;
                hwnd = GetParent(hwnd);
                depth++;
            }
            return false;
        }

        static bool IsAccessibleTextFocused()
        {
            var foreground = GetForegroundWindow();
            uint unusedPid;
            var threadId = GetWindowThreadProcessId(foreground, out unusedPid);
            var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
            var hwnd = GetFocus();
            if (GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
                hwnd = info.hwndFocus;
            if (hwnd == IntPtr.Zero) hwnd = foreground;
            return IsAccessibleTextWindow(hwnd);
        }

        static bool IsAccessibleTextWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
            IntPtr unk;
            if (AccessibleObjectFromWindow(hwnd, ObjIdClient, ref iid, out unk) != 0
                || unk == IntPtr.Zero)
                return false;
            object acc = null;
            try
            {
                acc = Marshal.GetObjectForIUnknown(unk);
                var role = acc.GetType().InvokeMember(
                    "accRole",
                    BindingFlags.GetProperty | BindingFlags.Public,
                    null,
                    acc,
                    new object[] { null });
                if (role == null) return false;
                int value;
                try { value = Convert.ToInt32(role); }
                catch { return false; }
                return IsTextAccRole(value);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (unk != IntPtr.Zero) Marshal.Release(unk);
                if (acc != null)
                {
                    try { Marshal.ReleaseComObject(acc); }
                    catch { /* already released */ }
                }
            }
        }

        public static bool IsImeComposing()
        {
            var foreground = GetForegroundWindow();
            uint unusedPid;
            var threadId = GetWindowThreadProcessId(foreground, out unusedPid);
            var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
            var caret = IntPtr.Zero;
            var focus = GetFocus();
            if (GetGUIThreadInfo(threadId, ref info))
            {
                caret = info.hwndCaret;
                if (info.hwndFocus != IntPtr.Zero) focus = info.hwndFocus;
            }

            if (HasComposition(focus)) return true;
            if (HasComposition(caret)) return true;
            if (HasComposition(foreground)) return true;
            return false;
        }

        static bool HasComposition(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            var imc = ImmGetContext(hwnd);
            if (imc == IntPtr.Zero) return false;
            try
            {
                var bytes = ImmGetCompositionStringW(imc, GcsCompStr, IntPtr.Zero, 0);
                return bytes > 0;
            }
            finally
            {
                ImmReleaseContext(hwnd, imc);
            }
        }

        public static bool IsImeCandidateVisible()
        {
            var found = false;
            EnumWindows((hwnd, _) =>
            {
                if (found) return false;
                if (!IsWindowVisible(hwnd)) return true;
                if (!LooksLikeCandidateRect(hwnd)) return true;

                var buffer = new StringBuilder(256);
                if (GetClassName(hwnd, buffer, buffer.Capacity) > 0
                    && IsImeUiClassName(buffer.ToString()))
                {
                    found = true;
                    return false;
                }

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return true;
                try
                {
                    using (var process = Process.GetProcessById((int)pid))
                    {
                        // TextInputHost/ctfmon stay resident with a language
                        // bar — only third-party IME processes count here.
                        var name = process.ProcessName;
                        if (!name.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase)
                            && !name.Equals("ctfmon", StringComparison.OrdinalIgnoreCase)
                            && IsImeProcessName(name)
                            && LooksLikeCandidateRect(hwnd))
                        {
                            found = true;
                            return false;
                        }
                    }
                }
                catch
                {
                    /* process exited */
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static bool LooksLikeCandidateRect(IntPtr hwnd)
        {
            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) return false;
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width < 60 || height < 24) return false;
            if (width > 1600 || height > 800) return false;
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GuiThreadInfo
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public int caretLeft;
            public int caretTop;
            public int caretRight;
            public int caretBottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("imm32.dll")]
        static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
        static extern int ImmGetCompositionStringW(IntPtr hIMC, uint dwIndex, IntPtr lpBuf, int dwBufLen);

        [DllImport("oleacc.dll")]
        static extern int AccessibleObjectFromWindow(
            IntPtr hwnd, uint dwObjectID, ref Guid riid, out IntPtr ppvObject);
    }
}
