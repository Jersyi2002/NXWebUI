using System.Collections.Generic;

namespace NxWebUIDeployer
{
    public static class PluginIds
    {
        public const string WebUi = "webui";
        public const string UgiiCustomDirectoryFile = "UGII_CUSTOM_DIRECTORY_FILE";
        public const string InstalledBy = "NX WebUI Deployer";
    }

    public static class RequiredFiles
    {
        public static readonly string[] All =
        {
            "startup/NxWebUI.men",
            "startup/NxWebUI.btn",
            "startup/NxRadialStartup.dll",
            "application/NxCommandSearch.dll",
            "application/NxCommandSearch.UI.dll",
            "application/NxRadialMenu.dll",
            "application/NxRadialSlots.dll",
            "application/NxProjectInit.dll",
            "application/WebView2Loader.dll",
            "application/webui/index.html",
            "application/webui/radial/index.html",
            "application/profiles/All/NxWebUI.rtb",
        };
    }

    public sealed class NxHint
    {
        public string BaseDir { get; set; }
        public string Release { get; set; }
        public string Version { get; set; }
        public string Source { get; set; }
    }

    public sealed class PluginStatus
    {
        public string Id { get; set; } = PluginIds.WebUi;
        public string Name { get; set; } = "NX WebUI";
        public string SourceDir { get; set; }
        public string InstallDir { get; set; }
        public string CustomDirsFile { get; set; }
        public bool Registered { get; set; }
        public bool FilesPresent { get; set; }
        public List<string> MissingFiles { get; set; } = new();
        public string State { get; set; } = "not-installed";
        public bool NxRunning { get; set; }
        public List<NxHint> NxInstallations { get; set; } = new();
        public NxHint PreferredNx { get; set; }
        public string EnvCustomDirsFile { get; set; }
        public List<string> RegisteredFrom { get; set; } = new();
        public List<string> CustomDirectories { get; set; } = new();
        public string Warning { get; set; }
    }

    public sealed class DeployResult
    {
        public bool Ok { get; set; }
        public PluginStatus Status { get; set; }
        public List<string> Log { get; set; } = new();
        public string Error { get; set; }
    }

    public sealed class InstallState
    {
        public int Version { get; set; } = 1;
        public string Plugin { get; set; } = PluginIds.WebUi;
        public string InstalledAt { get; set; }
        public string InstallDir { get; set; }
        public string CustomDirsFile { get; set; }
        public string PreviousEnv { get; set; }
        public List<string> PatchedOfficial { get; set; } = new();
    }

    public sealed class DeployContext
    {
        public string LocalAppData { get; set; }
        public List<string> SourceCandidates { get; set; } = new();
        public IEnvStore EnvStore { get; set; }
        public System.Func<IReadOnlyList<NxHint>> ScanNx { get; set; }
        public System.Func<bool> IsNxRunning { get; set; }
    }
}
