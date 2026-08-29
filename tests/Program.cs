using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NxWebUIDeployer;

int failures = 0;
void Check(string name, bool cond, string detail = "")
{
    Console.WriteLine($"{(cond ? "PASS" : "FAIL")}  {name}{(cond ? "" : "  " + detail)}");
    if (!cond) failures += 1;
}

void WritePayload(string root)
{
    foreach (var relative in RequiredFiles.All)
    {
        var file = Path.Combine(new[] { root }.Concat(relative.Split('/')).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(file, "payload:" + relative);
    }
    File.WriteAllText(Path.Combine(root, "application", "extra.dll"), "keep-me");
    File.WriteAllText(Path.Combine(root, "application", "skip.pdb"), "nope");
    File.WriteAllText(Path.Combine(root, "application", "radial-slots.json"), "{\"slots\":[]}");
    File.WriteAllText(Path.Combine(root, "custom_dirs.dat"), "F:\\should-not-copy\n");
}

DeployContext CtxFor(string sandbox, string officialWebUi = @"F:\AgentManager\NxWebUITool\deploy")
{
    var localAppData = Path.Combine(sandbox, "local");
    var source = Path.Combine(sandbox, "source-deploy");
    WritePayload(source);
    var store = new MemoryEnvStore();
    var nxBase = Path.Combine(sandbox, "NX2506");
    var official = Paths.OfficialCustomDirsPath(nxBase);
    Directory.CreateDirectory(Path.GetDirectoryName(official));
    File.WriteAllText(official, string.Join("\r\n", new[]
    {
        "# Siemens header",
        @"D:\QuickCAM",
        @"F:\NXPL001",
        officialWebUi,
        "",
    }));
    return new DeployContext
    {
        LocalAppData = localAppData,
        SourceCandidates = new List<string> { source },
        EnvStore = store,
        ScanNx = () => new List<NxHint>
        {
            new() { BaseDir = nxBase, Release = "NX 2506", Version = "2506.4000.0" },
        },
        IsNxRunning = () => false,
    };
}

Check("parse: skips comments and blanks",
    string.Join("|", CustomDirs.Parse("# heading\r\n\r\nD:\\QuickCAM\r\nF:\\NXPL001\r\n# skip\r\nF:\\AgentManager\\NxWebUITool\\deploy\n"))
    == @"D:\QuickCAM|F:\NXPL001|F:\AgentManager\NxWebUITool\deploy");

Check("parse: #include is not a directory",
    string.Join("|", CustomDirs.Parse("#include other.dat\nC:\\plugins\n")) == @"C:\plugins");

var formatted = CustomDirs.Format(new[] { @"D:\QuickCAM", @"C:\Users\me\AppData\Local\NxWebUITool\deploy" });
Check("format: deployer header and CRLF",
    formatted.StartsWith("# Managed by NX WebUI Deployer") && formatted.Contains("\r\nD:\\QuickCAM\r\n"));

var installDir = @"C:\Users\me\AppData\Local\NxWebUITool\deploy";
var merged = CustomDirs.Merge(new[]
{
    @"D:\QuickCAM", @"F:\NXPL001", @"F:\AgentManager\NxWebUITool\deploy", @"F:\NxWebUITool\deploy", @"D:\QuickCAM",
}, installDir);
Check("merge: keeps third-party dirs",
    merged[0].ToLowerInvariant().EndsWith("\\quickcam") && merged.Any(dir => dir.ToLowerInvariant().EndsWith("\\nxpl001")));
Check("merge: drops leftover NxWebUITool\\deploy",
    !merged.Any(dir => System.Text.RegularExpressions.Regex.IsMatch(dir, @"agentmanager\\nxwebuitool\\deploy$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(dir, @"f:\\nxwebuitool\\deploy$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
Check("merge: adds LocalAppData install dir once",
    merged.Count(dir => Paths.WindowsKey(dir) == Paths.WindowsKey(installDir)) == 1);

Check("legacy heuristic: repo deploy", Paths.IsLegacyWebUiDir(@"F:\AgentManager\NxWebUITool\deploy", installDir));
Check("legacy heuristic: old F:\\NxWebUITool", Paths.IsLegacyWebUiDir(@"F:\NxWebUITool\deploy", installDir));
Check("legacy heuristic: install dir is not legacy", !Paths.IsLegacyWebUiDir(installDir, installDir));

Check("skip pdb", Paths.ShouldSkipPayloadFile("application/NxCommandSearch.pdb"));
Check("skip radial-slots.json", Paths.ShouldSkipPayloadFile("application/radial-slots.json"));
Check("skip custom_dirs.dat", Paths.ShouldSkipPayloadFile("custom_dirs.dat"));
Check("keep dll", !Paths.ShouldSkipPayloadFile("application/NxProjectInit.dll"));

Check("isNx2506 release", Paths.IsNx2506("NX 2506", "2506.4000"));
Check("isNx2506 other", !Paths.IsNx2506("NX 2312", "2312.1700"));

Check("registry name: NX 2506", NxScan.IsNxRegistryName("NX 2506"));
Check("registry name: Unigraphics V32", NxScan.IsNxRegistryName("Unigraphics V32.0"));
Check("registry name: skips Teamcenter", !NxScan.IsNxRegistryName("Teamcenter"));
Check("registry name: skips Solid Edge", !NxScan.IsNxRegistryName("Solid Edge"));
Check("well-known includes E:\\NX2506", NxScan.WellKnownBases.Any(path => path.Equals(@"E:\NX2506", StringComparison.OrdinalIgnoreCase)));
if (File.Exists(@"E:\NX2506\NXBIN\ugraf.exe"))
{
    var quick = NxScan.ScanQuick();
    Check("quick scan finds E:\\NX2506",
        quick.Any(item => item.BaseDir.Equals(@"E:\NX2506", StringComparison.OrdinalIgnoreCase)),
        quick.Count == 0 ? "none" : string.Join(" | ", quick.Select(item => item.BaseDir)));
    Check("quick scan has release", quick.Any(item => (item.Release ?? "").Contains("2506") || (item.Version ?? "").StartsWith("2506")));
}

var officialSample = string.Join("\r\n", new[]
{
    "#  custom_dirs.dat: Directories to search",
    "# Customer modifications can follow on here",
    @"D:\QuickCAM",
    @"F:\NXPL001",
    @"F:\AgentManager\NxWebUITool\deploy",
    "",
});
var patched = CustomDirs.Patch(officialSample, installDir);
Check("patch: keeps Siemens comments", patched.Text.Contains("#  custom_dirs.dat: Directories to search"));
Check("patch: keeps QuickCAM and NXPL001", patched.Text.Contains(@"D:\QuickCAM") && patched.Text.Contains(@"F:\NXPL001"));
Check("patch: replaces repo deploy with LocalAppData",
    patched.Changed && patched.Text.Contains(installDir) && !patched.Text.Contains(@"F:\AgentManager\NxWebUITool\deploy"));
var patchedAgain = CustomDirs.Patch(patched.Text, installDir);
Check("patch: second pass is a no-op", !patchedAgain.Changed);

var stripped = CustomDirs.Strip(patched.Text, installDir);
Check("strip: removes plugin path, keeps others",
    stripped.Changed && !stripped.Text.Contains(installDir) && stripped.Text.Contains(@"D:\QuickCAM"));

var sandbox = Path.Combine(Path.GetTempPath(), "nxwebui-deployer-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
try
{
    var harness = CtxFor(sandbox);

    var missingSource = Deployer.GetStatus(new DeployContext
    {
        LocalAppData = harness.LocalAppData,
        SourceCandidates = new List<string> { Path.Combine(sandbox, "nope") },
        EnvStore = harness.EnvStore,
        ScanNx = () => new List<NxHint>(),
        IsNxRunning = () => false,
    });
    Check("status: not installed when dest empty", missingSource.State == "not-installed" && !missingSource.FilesPresent);

    var leftoverOfficial = Deployer.GetStatus(new DeployContext
    {
        LocalAppData = harness.LocalAppData,
        SourceCandidates = new List<string> { Path.Combine(sandbox, "nope") },
        EnvStore = harness.EnvStore,
        ScanNx = harness.ScanNx,
        IsNxRunning = () => false,
    });
    Check("status: leftover official path without files is partial",
        leftoverOfficial.State == "partial" && leftoverOfficial.RegisteredFrom.Contains("legacy-path"));

    var refused = Deployer.Deploy(new DeployContext
    {
        LocalAppData = harness.LocalAppData,
        SourceCandidates = harness.SourceCandidates,
        EnvStore = harness.EnvStore,
        ScanNx = harness.ScanNx,
        IsNxRunning = () => true,
    });
    Check("deploy: refuses while NX is running", !refused.Ok && (refused.Error ?? "").IndexOf("NX is running", StringComparison.OrdinalIgnoreCase) >= 0);
    Check("deploy: running NX does not copy files",
        !File.Exists(Path.Combine(Paths.DefaultInstallDir(harness.LocalAppData), "startup", "NxWebUI.men")));

    var deployed = Deployer.Deploy(harness);
    Check("deploy: ok", deployed.Ok, deployed.Error ?? string.Join(" | ", deployed.Log.Skip(Math.Max(0, deployed.Log.Count - 3))));
    Check("deploy: state installed", deployed.Status.State == "installed", deployed.Status.State);
    Check("deploy: files present", deployed.Status.FilesPresent);
    Check("deploy: registered via user env", deployed.Status.RegisteredFrom.Contains("user-env"));
    Check("deploy: env points at managed custom_dirs",
        Paths.WindowsKey(deployed.Status.EnvCustomDirsFile) == Paths.WindowsKey(Paths.DefaultCustomDirsFile(harness.LocalAppData)));

    var copiedMen = File.ReadAllText(Path.Combine(deployed.Status.InstallDir, "startup", "NxWebUI.men"));
    Check("deploy: copied startup men", copiedMen == "payload:startup/NxWebUI.men");
    Check("deploy: copied extra dll", File.Exists(Path.Combine(deployed.Status.InstallDir, "application", "extra.dll")));
    Check("deploy: skipped pdb", !File.Exists(Path.Combine(deployed.Status.InstallDir, "application", "skip.pdb")));
    Check("deploy: skipped radial-slots.json", !File.Exists(Path.Combine(deployed.Status.InstallDir, "application", "radial-slots.json")));

    var officialAfter = File.ReadAllText(Paths.OfficialCustomDirsPath(Path.Combine(sandbox, "NX2506")));
    Check("deploy: official keeps QuickCAM", officialAfter.Contains(@"D:\QuickCAM"));
    Check("deploy: official keeps NXPL001", officialAfter.Contains(@"F:\NXPL001"));
    Check("deploy: official lists LocalAppData deploy", officialAfter.ToLowerInvariant().Contains(deployed.Status.InstallDir.ToLowerInvariant()));
    Check("deploy: official no longer lists repo deploy", !officialAfter.Contains(@"F:\AgentManager\NxWebUITool\deploy"));

    var managed = File.ReadAllText(deployed.Status.CustomDirsFile);
    Check("deploy: managed custom_dirs keeps QuickCAM", managed.Contains(@"D:\QuickCAM") && managed.Contains(@"F:\NXPL001"));
    Check("deploy: env store updated",
        ((MemoryEnvStore)harness.EnvStore).Values[PluginIds.UgiiCustomDirectoryFile] == deployed.Status.CustomDirsFile);

    var marker = File.ReadAllText(Path.Combine(deployed.Status.InstallDir, "application", "nxwebui-plugin.json"));
    Check("deploy: wrote independent marker", marker.Contains("\"id\": \"webui\"") && marker.Contains(PluginIds.InstalledBy));

    var repair = Deployer.Deploy(harness);
    Check("repair: idempotent ok", repair.Ok && repair.Status.State == "installed");

    var gone = Deployer.Uninstall(harness);
    Check("uninstall: ok", gone.Ok, gone.Error ?? gone.Status.State);
    Check("uninstall: state not-installed", gone.Status.State == "not-installed");
    Check("uninstall: removed install dir", !Directory.Exists(deployed.Status.InstallDir));
    var officialUn = File.ReadAllText(Paths.OfficialCustomDirsPath(Path.Combine(sandbox, "NX2506")));
    Check("uninstall: official still has QuickCAM", officialUn.Contains(@"D:\QuickCAM") && officialUn.Contains(@"F:\NXPL001"));
    Check("uninstall: official no longer lists plugin",
        officialUn.IndexOf(Path.Combine("NxWebUITool", "deploy"), StringComparison.OrdinalIgnoreCase) < 0);

    var leftoverBox = Path.Combine(sandbox, "legacy");
    var leftover = CtxFor(leftoverBox, @"F:\NxWebUITool\deploy");
    ((MemoryEnvStore)leftover.EnvStore).Values[PluginIds.UgiiCustomDirectoryFile] = leftover.SourceCandidates[0];
    File.WriteAllText(Path.Combine(leftover.SourceCandidates[0], "custom_dirs.dat"), leftover.SourceCandidates[0] + "\r\nD:\\QuickCAM\r\n");
    var migrated = Deployer.Deploy(leftover);
    Check("migrate: from leftover env file to LocalAppData", migrated.Ok && migrated.Status.State == "installed", migrated.Status.State);
}
finally
{
    try { Directory.Delete(sandbox, true); } catch { /* ignore */ }
}

Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : $"\n{failures} FAILURES");
Environment.Exit(failures == 0 ? 0 : 1);
