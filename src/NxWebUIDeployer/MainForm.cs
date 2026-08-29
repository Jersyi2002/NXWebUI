using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NxWebUIDeployer
{
    public sealed class MainForm : Form
    {
        static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        readonly WebView2 _web = new();
        readonly string _webRoot;
        readonly string _exeDir;
        bool _ready;

        public MainForm()
        {
            _exeDir = AppDomain.CurrentDomain.BaseDirectory;
            _webRoot = Path.Combine(_exeDir, "webui");

            Text = "NX WebUI 部署器";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(720, 640);
            ClientSize = new Size(860, 780);
            BackColor = Color.FromArgb(245, 244, 239);
            Controls.Add(_web);
            _web.Dock = DockStyle.Fill;
            Load += async (_, __) =>
            {
                try { await InitWebView(); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.ToString(), "WebView2 初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
        }

        async System.Threading.Tasks.Task InitWebView()
        {
            var userData = Path.Combine(Path.GetTempPath(), "NxWebUIDeployer", "WebView2");
            Directory.CreateDirectory(userData);
            var loader = Path.Combine(_exeDir, "WebView2Loader.dll");
            if (File.Exists(loader))
                CoreWebView2Environment.SetLoaderDllFolderPath(_exeDir);
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _web.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "nxwebui-deployer.local",
                _webRoot,
                CoreWebView2HostResourceAccessKind.Allow);
            _web.CoreWebView2.WebMessageReceived += OnMessage;
            _web.CoreWebView2.NavigationCompleted += (_, __) =>
                System.Threading.Tasks.Task.Run(PushStatus);
            _web.CoreWebView2.Navigate("https://nxwebui-deployer.local/index.html?v=3");
            _ready = true;
        }

        void PushStatus()
        {
            try { Post(new { type = "response", id = 0, ok = true, data = StatusDto(quick: true) }); }
            catch (Exception ex) { Post(new { type = "response", id = 0, ok = false, error = ex.Message }); }
            try { Post(new { type = "response", id = 0, ok = true, data = StatusDto(quick: false) }); }
            catch (Exception ex) { Post(new { type = "response", id = 0, ok = false, error = ex.Message }); }
        }

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            BridgeMessage msg;
            try { msg = JsonSerializer.Deserialize<BridgeMessage>(e.WebMessageAsJson, JsonOpts); }
            catch { return; }
            if (msg == null || msg.type != "invoke") return;
            var id = msg.id;
            var action = msg.action;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (action == "status")
                    {
                        Post(new { type = "response", id, ok = true, data = StatusDto(quick: true) });
                        Post(new { type = "response", id = 0, ok = true, data = StatusDto(quick: false) });
                        return;
                    }
                    object data = action switch
                    {
                        "deploy" => DeployDto(),
                        "uninstall" => UninstallDto(),
                        "openInstallDir" => OpenInstallDir(),
                        "openSourceDir" => OpenSourceDir(),
                        _ => throw new InvalidOperationException("未知动作：" + action),
                    };
                    Post(new { type = "response", id, ok = true, data });
                }
                catch (Exception ex)
                {
                    Post(new { type = "response", id, ok = false, error = ex.Message });
                }
            });
        }

        DeployContext LiveContext(bool quickScan)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new DeployContext
            {
                LocalAppData = local,
                SourceCandidates = Deployer.ResolveSourceCandidates(_exeDir),
                EnvStore = new WindowsUserEnvStore(),
                ScanNx = quickScan
                    ? (System.Func<IReadOnlyList<NxHint>>)(() => NxScan.ScanQuick())
                    : () => NxScan.Scan(),
                IsNxRunning = NxScan.IsNxRunning,
            };
        }

        object StatusDto(bool quick) => ToDto(Deployer.GetStatus(LiveContext(quick)));

        object DeployDto()
        {
            var result = Deployer.Deploy(LiveContext(quickScan: false));
            return ToDto(result.Status, result);
        }

        object UninstallDto()
        {
            var result = Deployer.Uninstall(LiveContext(quickScan: false));
            return ToDto(result.Status, result);
        }

        object OpenInstallDir()
        {
            var dir = Paths.DefaultInstallDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            if (Directory.Exists(dir)) Process.Start("explorer.exe", dir);
            return new { opened = Directory.Exists(dir), path = dir };
        }

        object OpenSourceDir()
        {
            var source = Deployer.GetStatus(LiveContext(quickScan: true)).SourceDir;
            if (!string.IsNullOrEmpty(source) && Directory.Exists(source))
                Process.Start("explorer.exe", source);
            return new { opened = source != null, path = source };
        }

        static object ToDto(PluginStatus status, DeployResult result = null)
        {
            return new
            {
                ok = result?.Ok ?? true,
                error = result?.Error,
                log = result?.Log ?? new System.Collections.Generic.List<string>(),
                status = new
                {
                    status.Id,
                    status.Name,
                    status.SourceDir,
                    status.InstallDir,
                    status.CustomDirsFile,
                    status.Registered,
                    status.FilesPresent,
                    status.MissingFiles,
                    status.State,
                    status.NxRunning,
                    status.EnvCustomDirsFile,
                    status.RegisteredFrom,
                    status.CustomDirectories,
                    status.Warning,
                    preferredNx = status.PreferredNx == null ? null : new
                    {
                        baseDir = status.PreferredNx.BaseDir,
                        release = status.PreferredNx.Release,
                        version = status.PreferredNx.Version,
                    },
                    nxInstallations = status.NxInstallations.ConvertAll(item => new
                    {
                        baseDir = item.BaseDir,
                        release = item.Release,
                        version = item.Version,
                        source = item.Source,
                    }),
                },
            };
        }

        void Post(object obj)
        {
            if (!_ready) return;
            var json = JsonSerializer.Serialize(obj, JsonOpts);
            BeginInvoke(new Action(() =>
            {
                try { _web.CoreWebView2?.PostWebMessageAsJson(json); }
                catch { /* closed */ }
            }));
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
