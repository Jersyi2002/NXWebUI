using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NxWebUIDeployer
{
    /// <summary>
    /// Same discovery order as AgentManager <c>electron/main/workbench/nxInstallations.ts</c>:
    /// UGII_BASE_DIR → <c>reg.exe query ... /s /v UGII_BASE_DIR</c> (15s) →
    /// PowerShell fixed drives → folders matching nx|dc|designcenter + year →
    /// <c>NXBIN\ugraf.exe</c> FileVersion.
    /// Quick path (env + well-known folders) runs first so the UI is not blocked.
    /// </summary>
    public static class NxScan
    {
        const int ProcessTimeoutMs = 15_000;

        static readonly object Gate = new();
        static List<NxHint> Cache;
        static DateTime CacheAtUtc;
        static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

        public static readonly string[] WellKnownBases =
        {
            @"E:\NX2506",
            @"D:\NX2506",
            @"C:\NX2506",
            @"F:\NX2506",
            @"C:\Program Files\Siemens\NX2506",
            @"C:\Program Files\Siemens\NX 2506",
            @"C:\Program Files\Siemens\NX2312",
            @"D:\Program Files\Siemens\NX2506",
        };

        public static bool IsNxRunning()
        {
            try { return Process.GetProcessesByName("ugraf").Length > 0; }
            catch { return false; }
        }

        public static void InvalidateCache()
        {
            lock (Gate)
            {
                Cache = null;
                CacheAtUtc = DateTime.MinValue;
            }
        }

        public static bool IsNxRegistryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var n = name.ToUpperInvariant();
            return n.Contains("NX")
                || n.Contains("UNIGRAPHICS")
                || n.Contains("UGII")
                || n.StartsWith("UG ", StringComparison.Ordinal)
                || n.Contains("PLM SOFTWARE");
        }

        public static List<NxHint> ScanQuick()
        {
            return InspectCandidates(QuickCandidates(), allowCache: false);
        }

        public static List<NxHint> Scan(IEnumerable<string> extraBases = null)
        {
            var extras = extraBases?.Where(item => !string.IsNullOrWhiteSpace(item)).ToList()
                ?? new List<string>();
            if (extras.Count == 0)
            {
                lock (Gate)
                {
                    if (Cache != null && DateTime.UtcNow - CacheAtUtc < CacheTtl)
                        return Cache;
                }
            }

            var candidates = new List<(string BaseDir, string Source)>();
            candidates.AddRange(QuickCandidates());
            try { candidates.AddRange(QueryRegistryCandidates()); } catch { /* optional */ }
            try { candidates.AddRange(QueryDriveCandidates()); } catch { /* optional */ }
            foreach (var extra in extras)
                candidates.Add((extra, "manual"));

            var installations = InspectCandidates(candidates, allowCache: extras.Count == 0);
            return installations;
        }

        static List<(string BaseDir, string Source)> QuickCandidates()
        {
            var candidates = new List<(string, string)>();
            foreach (var env in ReadUgiiBaseDirs())
                candidates.Add((env, "environment"));
            foreach (var known in WellKnownBases)
                candidates.Add((known, "drive-scan"));
            return candidates;
        }

        static List<NxHint> InspectCandidates(IEnumerable<(string BaseDir, string Source)> candidates, bool allowCache)
        {
            var unique = new Dictionary<string, (string BaseDir, string Source, int Priority)>(StringComparer.Ordinal);
            int PriorityOf(string source) => source switch
            {
                "manual" => 4,
                "environment" => 3,
                "registry" => 2,
                _ => 1,
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    var normalized = NormalizeBaseDir(candidate.BaseDir);
                    var key = Paths.WindowsKey(normalized);
                    var priority = PriorityOf(candidate.Source);
                    if (!unique.TryGetValue(key, out var existing) || priority > existing.Priority)
                        unique[key] = (normalized, candidate.Source, priority);
                }
                catch
                {
                    // skip malformed paths
                }
            }

            var installations = new List<NxHint>();
            foreach (var item in unique.Values)
            {
                try { installations.Add(Inspect(item.BaseDir, item.Source)); }
                catch { /* invalid candidate */ }
            }

            installations.Sort((left, right) =>
            {
                var cmp = CompareVersion(right.Version, left.Version);
                return cmp != 0 ? cmp : string.Compare(left.BaseDir, right.BaseDir, StringComparison.OrdinalIgnoreCase);
            });

            if (allowCache)
            {
                lock (Gate)
                {
                    Cache = installations;
                    CacheAtUtc = DateTime.UtcNow;
                }
            }
            return installations;
        }

        public static NxHint Inspect(string baseDir, string source)
        {
            var normalized = NormalizeBaseDir(baseDir);
            var ugrafPath = Path.Combine(normalized, "NXBIN", "ugraf.exe");
            if (!File.Exists(ugrafPath)) throw new InvalidOperationException("NXBIN\\ugraf.exe was not found.");
            var version = GetExecutableVersion(ugrafPath);
            return new NxHint
            {
                BaseDir = normalized,
                Release = ReleaseFromVersion(version, normalized),
                Version = version,
                Source = source,
            };
        }

        static string GetExecutableVersion(string executable)
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(executable).FileVersion;
                if (!string.IsNullOrWhiteSpace(version)) return version.Trim();
            }
            catch { /* fall through to PowerShell, same as AgentManager */ }

            try
            {
                var stdout = RunCaptured(
                    "powershell.exe",
                    "-NoProfile -NonInteractive -Command \"(Get-Item -LiteralPath $env:NXWEBUI_NX_VERSION_FILE).VersionInfo.FileVersion\"",
                    ProcessTimeoutMs,
                    extraEnv: new Dictionary<string, string> { ["NXWEBUI_NX_VERSION_FILE"] = executable });
                var line = stdout.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(line)) return line.Trim();
            }
            catch { /* ignore */ }
            return "Unknown";
        }

        static IEnumerable<string> ReadUgiiBaseDirs()
        {
            foreach (var target in new[]
            {
                EnvironmentVariableTarget.Process,
                EnvironmentVariableTarget.User,
                EnvironmentVariableTarget.Machine,
            })
            {
                string value;
                try { value = Environment.GetEnvironmentVariable("UGII_BASE_DIR", target); }
                catch { continue; }
                if (!string.IsNullOrWhiteSpace(value)) yield return value;
            }
        }

        static string NormalizeBaseDir(string candidate)
        {
            var resolved = Path.GetFullPath((candidate ?? "").Trim().Trim('"'));
            if (string.Equals(Path.GetFileName(resolved), "ugraf.exe", StringComparison.OrdinalIgnoreCase))
                resolved = Path.GetDirectoryName(Path.GetDirectoryName(resolved)) ?? resolved;
            if (string.Equals(Path.GetFileName(resolved), "nxbin", StringComparison.OrdinalIgnoreCase))
                resolved = Path.GetDirectoryName(resolved) ?? resolved;
            return resolved.TrimEnd('\\', '/');
        }

        static string ReleaseFromVersion(string version, string baseDir)
        {
            var modern = Regex.Match(version ?? "", @"^(\d{4})");
            if (modern.Success) return "NX " + modern.Groups[1].Value;
            var legacy = Regex.Match(version ?? "", @"^(\d+)(?:\.(\d+))?");
            if (legacy.Success)
                return "NX " + legacy.Groups[1].Value + (legacy.Groups[2].Success ? "." + legacy.Groups[2].Value : "");
            var folder = Regex.Match(Path.GetFileName(baseDir) ?? "", @"(\d{4,6})");
            return folder.Success ? "NX " + folder.Groups[1].Value : "Siemens NX";
        }

        static int CompareVersion(string left, string right)
        {
            var leftParts = (left ?? "").Split('.').Select(part => int.TryParse(part, out var n) ? n : 0).ToArray();
            var rightParts = (right ?? "").Split('.').Select(part => int.TryParse(part, out var n) ? n : 0).ToArray();
            var count = Math.Max(leftParts.Length, rightParts.Length);
            for (var i = 0; i < count; i++)
            {
                var l = i < leftParts.Length ? leftParts[i] : 0;
                var r = i < rightParts.Length ? rightParts[i] : 0;
                if (l != r) return l.CompareTo(r);
            }
            return 0;
        }

        static List<(string BaseDir, string Source)> QueryRegistryCandidates()
        {
            var results = new List<(string, string)>();
            foreach (var root in new[] { @"HKLM\SOFTWARE\Siemens", @"HKLM\SOFTWARE\WOW6432Node\Siemens" })
            {
                try
                {
                    var stdout = RunCaptured("reg.exe", "query \"" + root + "\" /s /v UGII_BASE_DIR", ProcessTimeoutMs);
                    foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var match = Regex.Match(line, @"^\s*UGII_BASE_DIR\s+REG_\w+\s+(.+?)\s*$", RegexOptions.IgnoreCase);
                        if (match.Success) results.Add((match.Groups[1].Value.Trim(), "registry"));
                    }
                }
                catch { /* registry discovery is optional */ }
            }
            return results;
        }

        static List<(string BaseDir, string Source)> QueryDriveCandidates()
        {
            var results = new List<(string, string)>();
            foreach (var root in FixedDriveRoots())
            {
                var parents = new[]
                {
                    root,
                    Path.Combine(root, "Siemens"),
                    Path.Combine(root, "Program Files", "Siemens"),
                    Path.Combine(root, "Program Files (x86)", "Siemens"),
                };
                foreach (var parent in parents)
                {
                    foreach (var child in MatchingChildDirectories(parent))
                        results.Add((child, "drive-scan"));
                }
            }
            return results;
        }

        static List<string> FixedDriveRoots()
        {
            try
            {
                const string script = "[System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 3 -and $_.IsReady } | ForEach-Object { $_.RootDirectory.FullName }";
                var stdout = RunCaptured("powershell.exe", "-NoProfile -NonInteractive -Command \"" + script + "\"", ProcessTimeoutMs);
                return stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        static List<string> MatchingChildDirectories(string parent)
        {
            var results = new List<string>();
            try
            {
                var pattern = new Regex(@"^(?:nx|dc|designcenter)[ _-]?\d{4,6}$", RegexOptions.IgnoreCase);
                foreach (var child in Directory.GetDirectories(parent))
                {
                    if (pattern.IsMatch(Path.GetFileName(child)))
                        results.Add(child);
                }
            }
            catch { /* missing parent */ }
            return results;
        }

        static string RunCaptured(string fileName, string arguments, int timeoutMs, Dictionary<string, string> extraEnv = null)
        {
            var start = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (extraEnv != null)
            {
                foreach (var pair in extraEnv)
                    start.EnvironmentVariables[pair.Key] = pair.Value;
            }

            using var proc = new Process { StartInfo = start, EnableRaisingEvents = true };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(); } catch { /* ignore */ }
                try { proc.WaitForExit(2000); } catch { /* ignore */ }
            }
            return stdout.ToString();
        }
    }
}
