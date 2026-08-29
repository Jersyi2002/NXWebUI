using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NxWebUITool
{
    public sealed class RadialForm : Form, IPendingCommandForm
    {
        const int WsCaption = 0x00C00000;
        const int WsThickFrame = 0x00040000;
        const int WsExToolWindow = 0x00000080;
        const int WsExNoRedirectionBitmap = 0x00200000;
        const int DwmwaWindowCornerPreference = 33;
        const int DwmWcpDonotround = 1;
        const int FormDip = 640;

        static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        readonly WebView2 _web = new();
        readonly string _webRoot;
        readonly System.Windows.Forms.Timer _spaceTimer = new() { Interval = 16 };
        bool _webInited;
        bool _webInitStarted;
        bool _spaceArmed;
        bool _holdOpen;
        bool _sawAsyncSpaceDown;
        bool _holdReleasePosted;
        float _scale = 1f; // WebView2 RasterizationScale；宿主 DPI-unaware 时 DeviceDpi 恒 96，不能用它换算
        Point _cursorScreen;

        public string PendingCommandId { get; private set; }
        public string PendingCommandType { get; private set; }

        // Overlay must not steal IME / dialog focus. Hold-Space still tracks
        // the physical key via GetAsyncKeyState.
        protected override bool ShowWithoutActivation => true;

        public void PrepareShow(bool holdOpen = false)
        {
            PendingCommandId = null;
            PendingCommandType = "BUTTON";
            DialogResult = DialogResult.None;
            _holdOpen = holdOpen;
            _spaceArmed = false;
            _sawAsyncSpaceDown = false;
            _holdReleasePosted = false;
            _spaceTimer.Stop();
            _cursorScreen = Control.MousePosition;
            PlaceAtCursor();
            if (_webInited)
                _ = SyncScaleFromPageAsync();
        }

        public void Dismiss()
        {
            _spaceArmed = false;
            _spaceTimer.Stop();
            if (IsDisposed) return;
            if (Visible) Hide();
            if (Modal) DialogResult = DialogResult.Cancel;
        }

        public RadialForm()
        {
            Text = "";
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            SizeGripStyle = SizeGripStyle.Hide;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.Black;
            KeyPreview = true;
            Padding = Padding.Empty;

            var side = Scaled(FormDip);
            ClientSize = new Size(side, side);

            var asmDir = Path.GetDirectoryName(typeof(RadialForm).Assembly.Location) ?? ".";
            _webRoot = Path.Combine(asmDir, "webui");

            _web.Dock = DockStyle.Fill;
            _web.DefaultBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            Controls.Add(_web);

            _spaceTimer.Tick += (_, __) => PollSpaceRelease();

            Load += async (_, __) =>
            {
                PlaceAtCursor();
                if (_webInitStarted) return;
                try
                {
                    await EnsureWebViewAsync();
                    if (IsDisposed) return;
                    if (Visible) PostShown();
                }
                catch (Exception ex)
                {
                    if (!IsDisposed && Visible)
                        MessageBox.Show(this, ex.ToString(), "WebView2 初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            Shown += (_, __) =>
            {
                PlaceAtCursor();
                ArmHoldOrDismiss();
                if (_webInited)
                    PostShown();
            };

            VisibleChanged += (_, __) =>
            {
                if (!Visible)
                {
                    _spaceArmed = false;
                    _spaceTimer.Stop();
                    return;
                }
                if (_webInited) PostShown();
            };

            Resize += (_, __) => ApplyCircleRegion();
            FormClosed += (_, __) => _spaceTimer.Stop();
            FormClosing += (_, e) =>
            {
                if (e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                Dismiss();
            };

            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.Handled = true;
                    Dismiss();
                }
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style &= ~WsCaption;
                cp.Style &= ~WsThickFrame;
                cp.ExStyle |= WsExToolWindow;
                cp.ExStyle |= WsExNoRedirectionBitmap;
                return cp;
            }
        }

        int Scaled(int dip)
        {
            return (int)Math.Round(dip * _scale);
        }

        async Task SyncScaleFromPageAsync()
        {
            try
            {
                if (_web.CoreWebView2 == null) return;
                var json = await _web.CoreWebView2.ExecuteScriptAsync("window.devicePixelRatio");
                if (!double.TryParse(json, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var dpr) || dpr <= 0)
                    return;
                if (Math.Abs((float)dpr - _scale) < 0.001f) return;
                _scale = (float)dpr;
                PlaceAtCursor();
            }
            catch { /* 页面未就绪时保持当前缩放 */ }
        }

        void PlaceAtCursor()
        {
            var screen = Screen.FromPoint(_cursorScreen).WorkingArea;
            // Host is 640 DIP so outer-child hover glow is not clipped by the
            // elliptic region. The page still designs to 600 DIP and only
            // scales down when the work area is smaller.
            int side = Math.Min(Scaled(FormDip), Math.Min(screen.Width, screen.Height));
            side = Math.Max(1, side);
            int x = _cursorScreen.X - side / 2;
            int y = _cursorScreen.Y - side / 2;
            x = Math.Max(screen.Left, Math.Min(x, screen.Right - side));
            y = Math.Max(screen.Top, Math.Min(y, screen.Bottom - side));
            Bounds = new Rectangle(x, y, side, side);
            ApplyCircleRegion();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCircleRegion();
            try
            {
                int donotround = DwmWcpDonotround;
                DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref donotround, sizeof(int));
                var glass = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(Handle, ref glass);
            }
            catch
            {
                /* 非 Aero/Win11 忽略 */
            }
        }

        void ApplyCircleRegion()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
            IntPtr rgn = CreateEllipticRgn(0, 0, Width, Height);
            if (rgn != IntPtr.Zero)
                SetWindowRgn(Handle, rgn, true);
        }

        void ArmHoldOrDismiss()
        {
            if (!_holdOpen) return;
            if (NxUiFocus.ShouldSuppressRadialHud())
            {
                Dismiss();
                return;
            }
            // 吞键后 GetAsyncKeyState 为 0，不能当成「已经松开」。
            _sawAsyncSpaceDown = NxUiFocus.IsSpaceDown();
            _spaceArmed = true;
            _spaceTimer.Start();
        }

        /// <summary>首按空格不再冷启动 WebView：SearchHost.PrewarmRadial 在 NX
        /// 启动空闲后提前调用本方法完成初始化（窗体保持隐藏）。显示路径的
        /// Load 事件也会走这里，语义不变。
        /// 建句柄会同步触发 Load：若预热已在跑，Load 不得再 await 自己，否则死锁。</summary>
        public async Task EnsureWebViewAsync()
        {
            if (_webInited || IsDisposed) return;
            if (_webInitStarted)
            {
                while (!_webInited && !IsDisposed && _webInitStarted)
                    await Task.Delay(16);
                return;
            }
            _webInitStarted = true;
            try
            {
                if (!IsHandleCreated) _ = Handle; // 不显示窗体也先建句柄，WebView2 需要
                if (_webInited || IsDisposed) return;
                await InitWebView();
                _webInited = true;
            }
            catch
            {
                _webInitStarted = false;
                throw;
            }
        }

        void PostShown()
        {
            if (IsDisposed || !Visible) return;
            if (_holdOpen)
            {
                if (NxUiFocus.ShouldSuppressRadialHud())
                {
                    Dismiss();
                    return;
                }
                if (NxUiFocus.IsSpaceDown()) _sawAsyncSpaceDown = true;
                _spaceArmed = true;
                _spaceTimer.Start();
            }
            else
            {
                var spaceHeld = NxUiFocus.IsSpaceDown();
                _spaceArmed = spaceHeld;
                if (spaceHeld) _spaceTimer.Start();
                else _spaceTimer.Stop();
            }

            Post(new { type = "shown", spaceHeld = _holdOpen || _spaceArmed, style = RadialUi.Read() });
        }

        public void NotifyHoldSpaceUp()
        {
            FinishHoldRelease();
        }

        void FinishHoldRelease()
        {
            if (IsDisposed || !Visible || _holdReleasePosted) return;
            _holdReleasePosted = true;
            _spaceArmed = false;
            _spaceTimer.Stop();
            if (!_webInited)
            {
                Dismiss();
                return;
            }
            Post(new { type = "spaceup" });
        }

        void PollSpaceRelease()
        {
            if (IsDisposed || !Visible || _holdReleasePosted) return;
            if (_holdOpen)
            {
                if (NxUiFocus.ShouldSuppressRadialHud())
                {
                    Dismiss();
                    return;
                }
                if (NxUiFocus.IsSpaceDown()) _sawAsyncSpaceDown = true;
                if (_webInited) PostGlobalPointer();
                // 仅当异步键态曾经为按下（未吞键）时，才用它检测松开。
                if (_sawAsyncSpaceDown && !NxUiFocus.IsSpaceDown())
                    FinishHoldRelease();
                return;
            }
            if (!_spaceArmed) return;
            if (_webInited) PostGlobalPointer();
            if (NxUiFocus.IsSpaceDown()) return;
            FinishHoldRelease();
        }

        // WebView pointer events stop at the window edge. While the
        // space marking gesture is armed, forward the OS cursor in page CSS
        // coordinates so every point on the current screen remains selectable.
        void PostGlobalPointer()
        {
            if (!_webInited || _web.CoreWebView2 == null || _scale <= 0) return;
            var cursor = Control.MousePosition;
            var x = (cursor.X - Left) / _scale;
            var y = (cursor.Y - Top) / _scale;
            var json = JsonSerializer.Serialize(new { type = "pointer", x, y }, JsonOpts);
            try { _web.CoreWebView2.PostWebMessageAsJson(json); }
            catch { /* 窗体已隐藏或页面尚未就绪 */ }
        }

        async Task InitWebView()
        {
            if (!Directory.Exists(_webRoot))
                throw new DirectoryNotFoundException("未找到 WebUI 目录：\n" + _webRoot);

            var userData = Path.Combine(Path.GetTempPath(), "NxWebUITool", "WebView2Radial");
            Directory.CreateDirectory(userData);

            var asmDir = Path.GetDirectoryName(typeof(RadialForm).Assembly.Location) ?? ".";
            var loader = Path.Combine(asmDir, "WebView2Loader.dll");
            if (File.Exists(loader))
                CoreWebView2Environment.SetLoaderDllFolderPath(asmDir);

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(env);

            // 宿主进程 DPI-unaware 时 DeviceDpi 恒 96，而 WebView2 按显示器真实缩放光栅化：
            // 必须用页面 devicePixelRatio 反推物理尺寸，否则设计页面对应不上视口
            // （150% 缩放时视口只有 400 CSS px，外环整圈在窗口外）。
            // 引用的 WebView2 SDK 无 RasterizationScale API，用 ExecuteScript 取 dpr 代替。
            _web.CoreWebView2.NavigationCompleted += async (_, __) => await SyncScaleFromPageAsync();
            await SyncScaleFromPageAsync();

            _web.DefaultBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _web.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            _web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "nxwebui.local",
                _webRoot,
                CoreWebView2HostResourceAccessKind.Allow);

            _web.CoreWebView2.WebMessageReceived += OnMessage;
            _web.CoreWebView2.Navigate("https://nxwebui.local/radial/index.html?v=22");
            ApplyCircleRegion();
        }

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            BridgeMessage msg = null;
            try { msg = JsonSerializer.Deserialize<BridgeMessage>(e.WebMessageAsJson, JsonOpts); }
            catch { return; }
            if (msg == null || msg.type != "invoke") return;

            try
            {
                object data = msg.action switch
                {
                    "getCatalog" => CommandCatalog.Load(),
                    "loadSlots" => LoadRadialPayload(),
                    "saveSlots" => SaveSlots(msg.payload),
                    "ensureIcons" => EnsureIcons(msg.payload),
                    "manage" => RequestManage(msg.payload),
                    "execute" => Execute(msg.payload),
                    "close" => RequestClose(),
                    _ => throw new InvalidOperationException("未知动作：" + msg.action)
                };
                Post(new { type = "response", msg.id, ok = true, data });
                if (msg.action == "execute" || msg.action == "close" || msg.action == "manage")
                    BeginInvoke(new Action(Dismiss));
            }
            catch (Exception ex)
            {
                Post(new { type = "response", msg.id, ok = false, error = ex.Message });
            }
        }

        static object EnsureIcons(JsonElement payload)
        {
            var names = new List<string>();
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("names", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                        names.Add(el.GetString());
                }
            }
            return new { icons = NxIconCache.DataUrls(names) };
        }

        static object LoadRadialPayload()
        {
            var payload = RadialSlots.LoadForRadial();
            return new { payload.slots, payload.icons, payload.usage, style = RadialUi.Read() };
        }

        static object SaveSlots(JsonElement payload)
        {
            RadialSlots.SaveFromJson(payload);
            return new { saved = true };
        }

        object RequestManage(JsonElement payload)
        {
            int index = 0;
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("index", out var el) &&
                el.ValueKind == JsonValueKind.Number &&
                el.TryGetInt32(out var i))
            {
                index = i;
            }

            SearchHost.OpenSlotsAfterClose(index);
            return new { opened = true };
        }

        object RequestClose()
        {
            return new { closed = true };
        }

        object Execute(JsonElement payload)
        {
            string id = payload.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            string type = payload.TryGetProperty("type", out var tEl) ? tEl.GetString() : "BUTTON";
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("缺少命令 id");

            PendingCommandId = id;
            PendingCommandType = type;
            return new { started = true };
        }

        void Post(object obj)
        {
            var json = JsonSerializer.Serialize(obj, JsonOpts);
            BeginInvoke(new Action(() =>
            {
                try { _web.CoreWebView2?.PostWebMessageAsJson(json); }
                catch { /* 窗体已关 */ }
            }));
        }

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateEllipticRgn(int x1, int y1, int x2, int y2);

        [DllImport("user32.dll")]
        static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMarInset);

        [StructLayout(LayoutKind.Sequential)]
        struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        sealed class BridgeMessage
        {
            public string type { get; set; }
            public int id { get; set; }
            public string action { get; set; }
            public JsonElement payload { get; set; }
        }
    }
}
