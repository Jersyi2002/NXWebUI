using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NxWebUITool
{
    public sealed class SlotsForm : Form, IPendingCommandForm
    {
        const int WsCaption = 0x00C00000;
        const int WsThickFrame = 0x00040000;
        const int WsExToolWindow = 0x00000080;
        const int CsDropShadow = 0x00020000;
        const int DwmwaWindowCornerPreference = 33;
        const int DwmwaBorderColor = 34;
        const int DwmWcpRound = 2;
        const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
        const int FormWidthDip = 980;
        const int FormHeightDip = 600;
        const int ScreenMarginDip = 24;

        static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        readonly WebView2 _web = new();
        readonly string _webRoot;
        bool _placed;
        bool _webInited;
        float _pageScale = 1f;
        int _focusIndex;

        public string PendingCommandId { get; private set; }
        public string PendingCommandType { get; private set; }

        public void PrepareShow(int focusIndex)
        {
            PendingCommandId = null;
            PendingCommandType = "BUTTON";
            DialogResult = DialogResult.None;
            _placed = false;
            _focusIndex = Math.Max(0, Math.Min(7, focusIndex));
            if (_webInited)
                _ = SyncScaleFromPageAsync();
        }

        public SlotsForm()
        {
            Text = NxUiFocus.SlotsWindowTitle;
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            SizeGripStyle = SizeGripStyle.Hide;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(245, 244, 239);
            KeyPreview = true;
            Padding = Padding.Empty;

            ClientSize = new Size(FormWidthDip, FormHeightDip);

            var asmDir = Path.GetDirectoryName(typeof(SlotsForm).Assembly.Location) ?? ".";
            _webRoot = Path.Combine(asmDir, "webui");

            _web.Dock = DockStyle.Fill;
            _web.DefaultBackgroundColor = BackColor;
            Controls.Add(_web);

            Load += async (_, __) =>
            {
                PlaceOverOwner();
                ApplyWindowChrome();
                if (_webInited) return;
                try
                {
                    await InitWebView();
                    _webInited = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.ToString(), "WebView2 初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            Shown += (_, __) =>
            {
                PlaceOverOwner();
                ApplyWindowChrome();
                if (_webInited)
                    PostShown();
            };

            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
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
                cp.ClassStyle |= CsDropShadow;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowChrome();
        }

        void ApplyWindowChrome()
        {
            if (!IsHandleCreated) return;
            try
            {
                int round = DwmWcpRound;
                DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
                int none = DwmwaColorNone;
                DwmSetWindowAttribute(Handle, DwmwaBorderColor, ref none, sizeof(int));
            }
            catch
            {
                /* 非 Win11 时忽略圆角 */
            }
        }

        void PlaceOverOwner()
        {
            if (_placed) return;
            _placed = true;

            ApplyScaledBounds();
        }

        void ApplyScaledBounds()
        {
            if (IsDisposed) return;

            var area = Owner != null ? Screen.FromControl(Owner).WorkingArea : Screen.FromControl(this).WorkingArea;
            if (Owner != null && Owner.Bounds.Width > 80 && Owner.Bounds.Height > 80)
                area = Owner.Bounds;

            var margin = Math.Max(8, Scaled(ScreenMarginDip));
            var width = Math.Min(Scaled(FormWidthDip), Math.Max(1, area.Width - margin * 2));
            var height = Math.Min(Scaled(FormHeightDip), Math.Max(1, area.Height - margin * 2));
            var x = area.Left + (area.Width - width) / 2;
            var y = area.Top + Math.Max(margin, (area.Height - height) / 5);
            if (y + height > area.Bottom - margin)
                y = Math.Max(area.Top, area.Bottom - margin - height);
            Bounds = new Rectangle(x, y, width, height);
        }

        int Scaled(int dip)
        {
            return (int)Math.Round(dip * _pageScale);
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
                if (Math.Abs((float)dpr - _pageScale) < 0.001f && _placed) return;
                _pageScale = (float)dpr;
                ApplyScaledBounds();
            }
            catch
            {
                /* 页面未就绪时保留当前尺寸，NavigationCompleted 会再次同步。 */
            }
        }

        void PostShown()
        {
            Post(new { type = "shown", focus = _focusIndex });
        }

        async Task InitWebView()
        {
            if (!Directory.Exists(_webRoot))
                throw new DirectoryNotFoundException("未找到 WebUI 目录：\n" + _webRoot);

            var userData = Path.Combine(Path.GetTempPath(), "NxWebUITool", "WebView2Slots");
            Directory.CreateDirectory(userData);

            var asmDir = Path.GetDirectoryName(typeof(SlotsForm).Assembly.Location) ?? ".";
            var loader = Path.Combine(asmDir, "WebView2Loader.dll");
            if (File.Exists(loader))
                CoreWebView2Environment.SetLoaderDllFolderPath(asmDir);

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(env);

            // NX 宿主可能是 DPI-unaware，DeviceDpi 会固定为 96；WebView2 却按显示器
            // 的真实缩放绘制。用页面 dpr 反推物理窗口尺寸，保证 980x600 CSS 设计
            // 视口不会在 125%/150%/200% 缩放下缩成窄栏并裁掉圆盘和下拉菜单。
            _web.CoreWebView2.NavigationCompleted += async (_, __) => await SyncScaleFromPageAsync();
            await SyncScaleFromPageAsync();

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
            _web.CoreWebView2.Navigate("https://nxwebui.local/slots/index.html?v=11");
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
                    "loadSlots" => new { slots = RadialSlots.LoadWithIcons(), stash = RadialSlots.ReadStash(), style = RadialUi.Read() },
                    "saveSlots" => SaveSlots(msg.payload),
                    "loadUi" => new { style = RadialUi.Read() },
                    "saveUi" => new { style = RadialUi.WriteFromJson(msg.payload) },
                    "getNativeRadialInfo" => NativeRadialProfiles.GetInfo(),
                    "loadNativeRadial" => NativeRadialProfiles.Load(msg.payload),
                    "saveNativeRadial" => NativeRadialProfiles.Save(msg.payload),
                    "resetNativeRadial" => NativeRadialProfiles.Reset(msg.payload),
                    "ensureIcons" => EnsureIcons(msg.payload),
                    "close" => RequestClose(),
                    _ => throw new InvalidOperationException("未知动作：" + msg.action)
                };
                Post(new { type = "response", msg.id, ok = true, data });
            }
            catch (Exception ex)
            {
                Post(new { type = "response", msg.id, ok = false, error = ex.Message });
            }
        }

        static object SaveSlots(JsonElement payload)
        {
            RadialSlots.SaveFromJson(payload);
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("stash", out var stashEl))
            {
                RadialSlots.SaveStashFromJson(stashEl);
            }
            return new { saved = true };
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

        object RequestClose()
        {
            BeginInvoke(new Action(Close));
            return new { closed = true };
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

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        sealed class BridgeMessage
        {
            public string type { get; set; }
            public int id { get; set; }
            public string action { get; set; }
            public JsonElement payload { get; set; }
        }
    }
}
