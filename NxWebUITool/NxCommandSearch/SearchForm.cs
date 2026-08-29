using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NxWebUITool
{
    public sealed class SearchForm : Form, IPendingCommandForm
    {
        const int WsCaption = 0x00C00000;
        const int WsThickFrame = 0x00040000;
        const int WsExToolWindow = 0x00000080;
        const int CsDropShadow = 0x00020000;
        const int DwmwaWindowCornerPreference = 33;
        const int DwmwaBorderColor = 34;
        const int DwmWcpRound = 2;
        const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

        static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        readonly WebView2 _web = new();
        readonly string _webRoot;
        bool _placed;
        bool _webInited;

        public string PendingCommandId { get; private set; }
        public string PendingCommandType { get; private set; }

        public void PrepareShow()
        {
            PendingCommandId = null;
            PendingCommandType = "BUTTON";
            DialogResult = DialogResult.None;
            _placed = false;
        }

        public SearchForm()
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
            BackColor = Color.FromArgb(245, 244, 239);
            KeyPreview = true;
            Padding = Padding.Empty;

            var w = Dip(680);
            ClientSize = new Size(w, Dip(84));
            MinimumSize = new Size(Dip(480), Dip(76));
            MaximumSize = new Size(Dip(760), Dip(860));

            var asmDir = Path.GetDirectoryName(typeof(SearchForm).Assembly.Location) ?? ".";
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
                    Post(new { type = "shown" });
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

            var area = Owner != null ? Screen.FromControl(Owner).WorkingArea : Screen.FromControl(this).WorkingArea;
            if (Owner != null && Owner.Bounds.Width > 80 && Owner.Bounds.Height > 80)
                area = Owner.Bounds;

            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + Math.Max(Dip(72), area.Height / 6);
        }

        int Dip(int px)
        {
            return (int)Math.Round(px * DeviceDpi / 96.0);
        }

        async Task InitWebView()
        {
            if (!Directory.Exists(_webRoot))
                throw new DirectoryNotFoundException("未找到 WebUI 目录：\n" + _webRoot);

            var userData = Path.Combine(Path.GetTempPath(), "NxWebUITool", "WebView2");
            Directory.CreateDirectory(userData);

            var asmDir = Path.GetDirectoryName(typeof(SearchForm).Assembly.Location) ?? ".";
            var loader = Path.Combine(asmDir, "WebView2Loader.dll");
            if (File.Exists(loader))
                CoreWebView2Environment.SetLoaderDllFolderPath(asmDir);

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(env);

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
            _web.CoreWebView2.Navigate("https://nxwebui.local/index.html");
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
                    "execute" => Execute(msg.payload),
                    "resize" => ApplyResize(msg.payload),
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

        object ApplyResize(JsonElement payload)
        {
            int height = Dip(84);
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("height", out var el) &&
                el.ValueKind == JsonValueKind.Number)
            {
                height = el.GetInt32();
            }

            height = Math.Max(Dip(88), Math.Min(height + Dip(20), Dip(860)));
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                if (Math.Abs(ClientSize.Height - height) < 3) return;
                ClientSize = new Size(ClientSize.Width, height);
            }));
            return new { height };
        }

        object RequestClose()
        {
            BeginInvoke(new Action(Close));
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
            BeginInvoke(new Action(Close));
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
