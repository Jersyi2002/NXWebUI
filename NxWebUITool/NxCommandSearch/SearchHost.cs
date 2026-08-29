using System.Runtime.InteropServices;
using System.Windows.Forms;
using NXOpen;
using NXOpen.UF;

namespace NxWebUITool
{
    interface IPendingCommandForm
    {
        string PendingCommandId { get; }
        string PendingCommandType { get; }
    }

    public static class SearchHost
    {
        static SearchForm _form;
        static RadialForm _radial;
        static SlotsForm _slots;
        static string _pendingId;
        static string _pendingType;
        static TimerProc _timerProc;
        static bool _openSlotsAfterRadial;
        static int _slotsFocus;
        static bool _radialShowing;
        static IntPtr _radialPrevFocus;
        static IntPtr _radialPrevForeground;
        static bool _radialWasTopMost;
        static System.Windows.Forms.Timer _prewarmTimer;
        static int _prewarmTries;
        static bool _prewarmDone;
        static int _interruptAttempts;
        static bool _pendingHoldSpaceUp;

        delegate void TimerProc(IntPtr hWnd, uint msg, UIntPtr idEvent, uint time);

        public static void Run()
        {
            CommandCatalog.Warmup();
            if (_form == null || _form.IsDisposed)
                _form = new SearchForm();
            _form.PrepareShow();
            PrewarmRadial();
            ShowLocked(_form);
        }

        public static void RunRadial()
        {
            RunRadialCore(fromHold: false);
        }

        // Called by the startup Space hook when NX has made the MenuScript
        // button insensitive because another command owns DA2.
        public static void RunRadialFromHotkey()
        {
            _pendingHoldSpaceUp = false;
            RunRadialCore(fromHold: true);
            if (_pendingHoldSpaceUp)
            {
                _pendingHoldSpaceUp = false;
                OnHoldSpaceUp();
            }
        }

        /// <summary>空格钩子在 hold 菜单弹出后收到 key-up。LL 吞键后
        /// GetAsyncKeyState 不可用，由这里通知圆盘松开执行。</summary>
        public static void OnHoldSpaceUp()
        {
            var form = _radial;
            if (form == null || form.IsDisposed || !form.Visible)
            {
                _pendingHoldSpaceUp = true;
                return;
            }
            if (form.InvokeRequired)
                form.BeginInvoke(new Action(form.NotifyHoldSpaceUp));
            else
                form.NotifyHoldSpaceUp();
        }

        static void RunRadialCore(bool fromHold)
        {
            if (_radialShowing)
            {
                if (_radial != null && !_radial.IsDisposed && _radial.Visible)
                    return;
                _radialShowing = false;
            }
            // IME 组字与修饰键仍抑制弹出；文本输入框不再抑制。
            // 不要用 GetAsyncKeyState 判断 fromHold：LL 钩子吞键后它经常是 0。
            // 也不要在这里 EnumWindows 扫 IME 候选窗：语言栏也会误伤，导致空格永远弹不出。
            if (NxUiFocus.ShouldSuppressRadialHud()) return;
            CommandCatalog.Warmup();
            if (_radial == null || _radial.IsDisposed)
                _radial = new RadialForm();
            _radial.PrepareShow(fromHold);
            ShowRadialOverlay(_radial, fromHold);
        }

        /// <summary>
        /// 首按空格预热：NX 启动 15 秒后由 RadialSpaceHook 经 EntryLoader 调用
        /// （空闲时），首次 Alt+Q 也会触发。WebView 初始化（约 1 秒）从首按
        /// 挪到启动空闲，命令运行中首次弹出不再卡 NX 主线程。
        /// </summary>
        public static void PrewarmRadial()
        {
            if (_prewarmDone) return;
            if (IsNxCommandBusy())
            {
                _prewarmTries++;
                if (_prewarmTries > 12) { _prewarmDone = true; return; }
                if (_prewarmTimer == null)
                {
                    _prewarmTimer = new System.Windows.Forms.Timer { Interval = 20000 };
                    _prewarmTimer.Tick += (_, __) =>
                    {
                        _prewarmTimer.Stop();
                        PrewarmRadial();
                    };
                }
                _prewarmTimer.Stop();
                _prewarmTimer.Start();
                return;
            }
            _prewarmDone = true;
            if (_prewarmTimer != null) _prewarmTimer.Dispose();
            try
            {
                if (_radial == null || _radial.IsDisposed)
                    _radial = new RadialForm();
                _ = _radial.EnsureWebViewAsync();
            }
            catch
            {
                /* 预热失败退回首按初始化的老路径 */
            }
        }

        public static void RunSlots()
        {
            CommandCatalog.Warmup();
            if (_slots == null || _slots.IsDisposed)
                _slots = new SlotsForm();
            _slots.PrepareShow(_slotsFocus);
            _slotsFocus = 0;
            ShowLocked(_slots);
        }

        public static void OpenSlotsAfterClose(int focusIndex)
        {
            _slotsFocus = focusIndex;
            _openSlotsAfterRadial = true;
        }

        static void ShowRadialOverlay(RadialForm form, bool fromHold)
        {
            // Nested WaitMessage/DoEvents on NX's UI thread greyed every
            // command after Space-up. Stay modeless and let NX pump.
            WriteRadialStamp();
            _radialShowing = true;
            _radialPrevFocus = GetFocus();
            _radialPrevForeground = GetForegroundWindow();
            _radialWasTopMost = form.TopMost;
            form.VisibleChanged -= OnRadialVisibleChanged;
            form.VisibleChanged += OnRadialVisibleChanged;
            form.TopMost = true;
            if (!form.Visible)
                form.Show();
            if (!fromHold && form.Visible)
                form.Activate();
        }

        static void OnRadialVisibleChanged(object sender, EventArgs e)
        {
            var form = sender as RadialForm;
            if (form == null || form.Visible) return;
            form.VisibleChanged -= OnRadialVisibleChanged;
            form.TopMost = _radialWasTopMost;
            RestoreFocus(_radialPrevForeground, _radialPrevFocus);
            _radialShowing = false;

            var pendingId = form.PendingCommandId;
            var pendingType = form.PendingCommandType;
            if (!string.IsNullOrWhiteSpace(pendingId))
                ScheduleCommand(pendingId, pendingType);

            if (_openSlotsAfterRadial)
            {
                _openSlotsAfterRadial = false;
                RunSlots();
            }
        }

        static void WriteRadialStamp()
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "NxWebUITool");
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, "last-radial.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " modeless-no-pump\n");
            }
            catch { /* ignore */ }
        }

        static void ShowLocked(Form form)
        {
            var pending = (IPendingCommandForm)form;
            var session = Session.GetSession();
            var uf = UFSession.GetUFSession();
            // The Open C contract requires UF_UI_FROM_CUSTOM here.  These forms
            // are passive WebView overlays, so they may still be shown when NX
            // owns DA2; only interactive Open C dialogs must stop on lock failure.
            var lockStatus = uf.Ui.LockUgAccess(UFConstants.UF_UI_FROM_CUSTOM);

            string pendingId = null;
            string pendingType = "BUTTON";
            IntPtr nxHwnd = IntPtr.Zero;
            var previousFocus = GetFocus();
            var previousForeground = GetForegroundWindow();
            var enabledSnapshot = SnapshotEnabledWindows();
            var wasTopMost = form.TopMost;

            try
            {
                IWin32Window owner = null;
                try
                {
                    nxHwnd = uf.Ui.GetDefaultParent();
                    if (nxHwnd != IntPtr.Zero)
                        owner = new NativeWindowHandle(nxHwnd);
                }
                catch
                {
                    /* 部分会话没有父窗口 */
                }

                // When NX already owns DA2, ShowDialog(nx) DisableWindow's the
                // owner (often already disabled by an NX dialog) and EnableWindow
                // on close, which tears down NX's modal. Keep the overlay above
                // the dialog but restore enable/focus afterwards.
                form.TopMost = lockStatus != UFConstants.UF_UI_LOCK_SET;
                if (owner != null && lockStatus == UFConstants.UF_UI_LOCK_SET)
                    form.ShowDialog(owner);
                else
                    form.ShowDialog();

                pendingId = pending.PendingCommandId;
                pendingType = pending.PendingCommandType;
            }
            catch (Exception ex)
            {
                WriteListing(session, ex);
            }
            finally
            {
                form.TopMost = wasTopMost;
                if (lockStatus == UFConstants.UF_UI_LOCK_SET)
                {
                    try { uf.Ui.UnlockUgAccess(UFConstants.UF_UI_FROM_CUSTOM); }
                    catch { /* ignore */ }
                }
                RestoreEnabledWindows(enabledSnapshot);
                RestoreFocus(previousForeground, previousFocus);
            }

            if (!string.IsNullOrWhiteSpace(pendingId))
                ScheduleCommand(pendingId, pendingType);
        }

        static List<WindowEnableState> SnapshotEnabledWindows()
        {
            var states = new List<WindowEnableState>();
            var seen = new HashSet<IntPtr>();
            var pid = GetCurrentProcessId();
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var windowPid);
                if (windowPid != pid) return true;
                AddEnabledState(states, seen, hwnd);
                return true;
            }, IntPtr.Zero);
            AddEnabledState(states, seen, GetFocus());
            AddEnabledState(states, seen, GetForegroundWindow());
            return states;
        }

        static void AddEnabledState(List<WindowEnableState> states, HashSet<IntPtr> seen, IntPtr hwnd)
        {
            while (hwnd != IntPtr.Zero && seen.Add(hwnd))
            {
                states.Add(new WindowEnableState { Handle = hwnd, Enabled = IsWindowEnabled(hwnd) });
                hwnd = GetParent(hwnd);
            }
        }

        static void RestoreEnabledWindows(List<WindowEnableState> states)
        {
            if (states == null) return;
            foreach (var state in states)
            {
                if (state.Handle == IntPtr.Zero || !IsWindow(state.Handle)) continue;
                if (IsWindowEnabled(state.Handle) != state.Enabled)
                    EnableWindow(state.Handle, state.Enabled);
            }
        }

        static void RestoreFocus(IntPtr foreground, IntPtr focus)
        {
            try
            {
                if (foreground != IntPtr.Zero && IsWindow(foreground))
                    SetForegroundWindow(foreground);
                if (focus != IntPtr.Zero && IsWindow(focus))
                    SetFocus(focus);
            }
            catch
            {
                /* NX may have destroyed the dialog */
            }
        }

        static void ScheduleCommand(string id, string type)
        {
            // 新选择直接替换尚未执行的旧命令（后选优先），不打队列。
            if (_timerProc != null)
            {
                try { KillTimer(IntPtr.Zero, new UIntPtr(0x4E58)); }
                catch { /* ignore stale timer */ }
            }
            _pendingId = id;
            _pendingType = type;
            _interruptAttempts = 0;
            _timerProc = OnTimer;
            SetTimer(IntPtr.Zero, new UIntPtr(0x4E58), 80, _timerProc);
        }

        static void OnTimer(IntPtr hWnd, uint msg, UIntPtr idEvent, uint time)
        {
            var id = _pendingId;
            var type = _pendingType;
            if (string.IsNullOrWhiteSpace(id))
            {
                StopCommandTimer(hWnd, idEvent);
                return;
            }

            try
            {
                // AskLockStatus does not take DA2. LockUgAccess+Unlock as a
                // probe, and posting Escape to "free" a live command, both
                // left NX with every button greyed out.
                if (IsNxCommandBusy())
                {
                    // 中断替换：用户从圆盘选了新命令即视为明确意图——给 NX
                    // 当前的模态弹窗发 WM_CLOSE（等同点 X/取消），对话框关闭
                    // 后 DA2 释放、新命令立即执行。长计算/无弹窗时不打断，
                    // 维持排队语义；确认弹窗的连锁关闭由尝试上限兜住。
                    if (_interruptAttempts < 3 && TryInterruptNxModal())
                        _interruptAttempts++;
                    return;
                }

                StopCommandTimer(hWnd, idEvent);
                RadialUsage.Record(id, type);
                CommandInvoker.Run(id, type);
            }
            catch (Exception ex)
            {
                StopCommandTimer(hWnd, idEvent);
                try { WriteListing(Session.GetSession(), ex); }
                catch { /* ignore */ }
            }
        }

        /// <summary>NX 当前前台是否为「有 owner 的 NX 进程弹窗」（模态对话框
        /// 的典型形态）。NX 主框架/图形区没有 owner，不会被误关；本插件自己
        /// 的 WebView 浮层也排除。</summary>
        static bool TryInterruptNxModal()
        {
            try
            {
                var foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;
                GetWindowThreadProcessId(foreground, out var pid);
                if (pid != GetCurrentProcessId()) return false;
                if (IsOwnOverlayRoot(foreground)) return false;
                var root = GetAncestor(foreground, GetAncestorRoot);
                if (root != IntPtr.Zero && root != foreground && IsOwnOverlayRoot(root)) return false;
                if (GetWindowLong(foreground, GwlHwndParent) == IntPtr.Zero) return false;
                if (!IsWindowVisible(foreground) || !IsWindowEnabled(foreground)) return false;
                PostMessage(foreground, WmClose, IntPtr.Zero, IntPtr.Zero);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool IsOwnOverlayRoot(IntPtr hwnd)
        {
            foreach (var form in new Form[] { _form, _radial, _slots })
            {
                if (form == null || form.IsDisposed) continue;
                try
                {
                    if (form.Handle == hwnd || GetAncestor(form.Handle, GetAncestorRoot) == hwnd) return true;
                }
                catch
                {
                    /* 句柄未建则跳过 */
                }
            }
            return false;
        }

        static bool IsNxCommandBusy()
        {
            try
            {
                return UFSession.GetUFSession().Ui.AskLockStatus() == UFConstants.UF_UI_LOCK;
            }
            catch
            {
                return true;
            }
        }

        static void StopCommandTimer(IntPtr hwnd, UIntPtr idEvent)
        {
            try { KillTimer(IntPtr.Zero, idEvent); }
            catch { /* ignore */ }
            _pendingId = null;
        }

        static void WriteListing(Session session, Exception ex)
        {
            try
            {
                session.ListingWindow.Open();
                session.ListingWindow.WriteLine(ex.ToString());
            }
            catch
            {
                MessageBox.Show(ex.ToString(), "NX WebUI", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        sealed class NativeWindowHandle : IWin32Window
        {
            public NativeWindowHandle(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }

        struct WindowEnableState
        {
            public IntPtr Handle;
            public bool Enabled;
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, TimerProc lpTimerFunc);

        [DllImport("user32.dll", ExactSpelling = true)]
        static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

        const int WmClose = 0x0010;
        const int GwlHwndParent = -8;
        const uint GetAncestorRoot = 2; // GA_ROOT

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentProcessId();

        [DllImport("user32.dll")]
        static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool EnableWindow(IntPtr hWnd, bool enable);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr SetFocus(IntPtr hWnd);

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    }
}
