using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NXOpen;

namespace NxWebUITool
{
    /// <summary>
    /// Loaded by NX from UGII_USER_DIR/startup. Hold-Space（≥110ms）弹出空格
    /// 环形菜单；短按空格原样输入。
    ///
    /// 为了让输入框里也能按住空格弹菜单而不污染输入，本钩子会在 NX 前台、
    /// 无修饰键、无 IME 组字时「吞掉」空格 keydown：110ms 内松开（短按）由
    /// sink 补发一次空格（注入事件，本钩子放行）；按住触发菜单则不再补发，
    /// 按住期间的所有自动重复 keydown 一并吞掉。重放连续失败 3 次就永久
    /// 退回纯观察模式，保底空格永远可用。
    /// </summary>
    public static class RadialStartupPlugin
    {
        public static int Startup()
        {
            RadialSpaceHook.Install();
            return 0;
        }

        public static void Main()
        {
            Startup();
        }

        public static int GetUnloadOption(string unused)
        {
            return (int)Session.LibraryUnloadOption.AtTermination;
        }
    }

    internal static class RadialSpaceHook
    {
        const int WhKeyboardLl = 13;
        const int WmKeyDown = 0x0100;
        const int WmKeyUp = 0x0101;
        const int WmSysKeyDown = 0x0104;
        const int WmSysKeyUp = 0x0105;
        const int VkSpace = 0x20;
        const int LlkhfInjected = 0x10;
        const int WmArm = 0x8000 + 0x0458;
        const int WmDisarm = WmArm + 1;      // wParam: 0 = 结束计时, 1 = 结束计时并补发空格（短按）
        const int HoldDelayMs = 110;
        const int PrewarmDelayMs = 15000;

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        static readonly HookProc HookCallbackDelegate = HookCallback;
        static IntPtr _hook;
        static HotkeySink _sink;
        static uint _processId;
        static bool _spaceDown;       // 物理按下锁存（key-up 被吞时靠轮询自愈）
        static bool _consumed;        // 本次按下被我们吞掉（短按需补发空格）
        static bool _holdFired;       // 长按已触发（或已决定触发）→ 松开不补发
        static int _reinjectFailures;
        static bool _reinjectBroken;  // 补发连续失败 → 永久退回观察模式

        public static void Install()
        {
            if (_hook != IntPtr.Zero) return;
            _processId = (uint)Process.GetCurrentProcess().Id;
            _sink = new HotkeySink();
            _hook = SetWindowsHookEx(WhKeyboardLl, HookCallbackDelegate, GetModuleHandle(null), 0);
            _sink.StartPrewarmCountdown(PrewarmDelayMs);
        }

        /// <summary>WH_KEYBOARD_LL 吞掉 keydown 后 GetAsyncKeyState(VK_SPACE)
        /// 经常仍是 0，不能当「是否按住」用。以钩子自己看到的按下为准。</summary>
        internal static bool IsCapturedDown() => _spaceDown;

        /// <summary>hold 计时结束的结论：fired=true 表示菜单已触发，松开空格
        /// 不再补发；fired=false（已松开/前台切走/IME 组字）由松开路径补发。</summary>
        internal static void NoteHoldOutcome(bool fired)
        {
            _holdFired = fired;
        }

        static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            bool swallow = false;
            try
            {
                // IME 可能吞掉 key-up。仅在我们没吞 keydown 时才信
                // GetAsyncKeyState：吞键后它会一直是 0，误当成已经松开。
                if (_spaceDown && !_consumed && !NxUiFocus.IsSpaceDown())
                    HandleSpaceReleased();

                if (nCode >= 0 && lParam != IntPtr.Zero && Marshal.ReadInt32(lParam) == VkSpace)
                {
                    var message = wParam.ToInt32();
                    var injected = (Marshal.ReadInt32(lParam, 8) & LlkhfInjected) != 0;
                    if (message == WmKeyUp || message == WmSysKeyUp)
                    {
                        // 吞掉了 keydown 的那次按键，其 key-up 也成对吞掉
                        swallow = _consumed;
                        HandleSpaceReleased();
                    }
                    else if ((message == WmKeyDown || message == WmSysKeyDown) && !injected)
                    {
                        if (!_spaceDown)
                        {
                            _spaceDown = true;
                            _holdFired = false;
                            var gate = !_reinjectBroken
                                && IsNxForeground()
                                && !NxUiFocus.IsModifierDown()
                                && !NxUiFocus.IsImeComposing()
                                && !NxUiFocus.IsSlotsEditorForeground();
                            _consumed = gate;
                            swallow = gate;
                            if (gate && _sink != null)
                                PostMessage(_sink.Handle, WmArm, IntPtr.Zero, IntPtr.Zero);
                        }
                        else
                        {
                            // 自动重复：一旦本次按下被吞，重复键也继续吞，
                            // 否则按住期间空格会漏进输入框
                            swallow = _consumed;
                        }
                    }
                }
            }
            catch
            {
                /* never throw out of a hook */
            }
            return swallow ? (IntPtr)1 : CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>一次按下结束。短按（未触发菜单且被我们吞掉）补发空格。</summary>
        static void HandleSpaceReleased()
        {
            _spaceDown = false;
            var consumed = _consumed;
            var fired = _holdFired;
            _consumed = false;
            _holdFired = false;
            if (_sink == null) return;
            IntPtr wparam;
            if (consumed && !fired) wparam = new IntPtr(1);      // 短按：补发空格
            else if (fired) wparam = new IntPtr(2);              // 已弹出：通知圆盘松开
            else wparam = IntPtr.Zero;
            PostMessage(_sink.Handle, WmDisarm, wparam, IntPtr.Zero);
        }

        /// <summary>补发一次空格（短按）。在 sink 窗口过程里执行而非钩子回调，
        /// 保持回调轻量。前台已不是 NX 时宁可不发，避免把空格注进别的应用。</summary>
        internal static void ReinjectSpace()
        {
            if (_reinjectBroken) return;
            try
            {
                if (!IsNxForeground()) return;
                var down = new INPUT { type = InputKeyboard, wVk = VkSpace };
                var up = new INPUT { type = InputKeyboard, wVk = VkSpace, dwFlags = KeyEventfKeyUp };
                var sent = SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
                if (sent != 2)
                {
                    _reinjectFailures++;
                    if (_reinjectFailures >= 3) _reinjectBroken = true;
                }
                else
                {
                    _reinjectFailures = 0;
                }
            }
            catch
            {
                _reinjectBroken = true;
            }
        }

        static bool IsNxForeground()
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            GetWindowThreadProcessId(foreground, out var processId);
            return processId == _processId;
        }

        sealed class HotkeySink : NativeWindow
        {
            readonly System.Windows.Forms.Timer _holdTimer = new() { Interval = HoldDelayMs };
            readonly System.Windows.Forms.Timer _prewarmTimer = new() { Interval = PrewarmDelayMs };

            public HotkeySink()
            {
                CreateHandle(new CreateParams
                {
                    Caption = "NxWebUITool.RadialHotkey",
                    Parent = new IntPtr(-3), // HWND_MESSAGE
                });
                _holdTimer.Tick += (_, __) =>
                {
                    _holdTimer.Stop();
                    if (!RadialSpaceHook.IsCapturedDown())
                    {
                        RadialSpaceHook.NoteHoldOutcome(false);
                        return;
                    }
                    if (!IsNxForeground())
                    {
                        RadialSpaceHook.NoteHoldOutcome(false);
                        return;
                    }
                    if (NxUiFocus.ShouldSuppressRadialHud())
                    {
                        // 组字/修饰键在按住期间出现：不算菜单触发，松开补发空格
                        RadialSpaceHook.NoteHoldOutcome(false);
                        return;
                    }
                    RadialSpaceHook.NoteHoldOutcome(true);
                    EntryLoader.Run("RunRadialFromHotkey");
                };
            }

            public void StartPrewarmCountdown(int delay)
            {
                _prewarmTimer.Tick += (_, __) =>
                {
                    _prewarmTimer.Stop();
                    EntryLoader.Run("PrewarmRadial");
                };
                _prewarmTimer.Interval = delay;
                _prewarmTimer.Start();
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmArm)
                {
                    _holdTimer.Stop();
                    _holdTimer.Start();
                    return;
                }
                if (m.Msg == WmDisarm)
                {
                    _holdTimer.Stop();
                    var kind = m.WParam.ToInt64();
                    if (kind == 1)
                        RadialSpaceHook.ReinjectSpace();
                    else if (kind == 2)
                        EntryLoader.Run("OnHoldSpaceUp");
                    return;
                }
                base.WndProc(ref m);
            }
        }

        const uint InputKeyboard = 1;
        const uint KeyEventfKeyUp = 0x0002;

        [StructLayout(LayoutKind.Explicit)]
        struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public ushort wVk;
            [FieldOffset(10)] public ushort wScan;
            [FieldOffset(12)] public uint dwFlags;
            [FieldOffset(16)] public uint time;
            [FieldOffset(24)] public IntPtr dwExtraInfo;
            // x64 的 INPUT 是 40 字节（4 头 + 4 对齐 + 32 union），显式补齐
            [FieldOffset(32)] public uint pad0;
            [FieldOffset(36)] public uint pad1;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);
    }
}
