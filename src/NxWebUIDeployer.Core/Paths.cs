using System;
using System.Globalization;
using System.IO;

namespace NxWebUIDeployer
{
    public static class Paths
    {
        public static string WindowsKey(string value)
        {
            var trimmed = (value ?? "").Trim().Trim('"');
            var resolved = Path.GetFullPath(trimmed).TrimEnd('\\', '/');
            return resolved.ToLower(CultureInfo.GetCultureInfo("en-US"));
        }

        public static string NxWebUiHome(string localAppData) =>
            Path.Combine(localAppData, "NxWebUITool");

        public static string DefaultInstallDir(string localAppData) =>
            Path.Combine(NxWebUiHome(localAppData), "deploy");

        public static string DefaultCustomDirsFile(string localAppData) =>
            Path.Combine(NxWebUiHome(localAppData), "custom_dirs.dat");

        public static string DefaultStateFile(string localAppData) =>
            Path.Combine(NxWebUiHome(localAppData), "install-state.json");

        public static string OfficialCustomDirsPath(string nxBaseDir) =>
            Path.Combine(nxBaseDir, "UGII", "menus", "custom_dirs.dat");

        public static string MarkerRelative => Path.Combine("application", "nxwebui-plugin.json");

        public static bool IsNx2506(string release, string version) =>
            System.Text.RegularExpressions.Regex.IsMatch(release ?? "", @"\b2506\b")
            || System.Text.RegularExpressions.Regex.IsMatch(version ?? "", @"^2506\b");

        public static bool ShouldSkipPayloadFile(string relativePath)
        {
            var baseName = Path.GetFileName((relativePath ?? "").Replace('\\', '/')).ToLowerInvariant();
            return baseName.EndsWith(".pdb", StringComparison.Ordinal)
                || baseName == "radial-slots.json"
                || baseName == "custom_dirs.dat";
        }

        public static bool IsLegacyWebUiDir(string dir, string installDir)
        {
            var key = WindowsKey(dir);
            if (key == WindowsKey(installDir)) return false;
            var normalized = key.Replace('/', '\\');
            return normalized.EndsWith("\\nxwebuitool\\deploy", StringComparison.Ordinal);
        }
    }
}
