using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NxWebUIDeployer
{
    public static class Deployer
    {
        static readonly string[] PayloadFolders = { "startup", "application" };
        static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        public static List<string> ResolveSourceCandidates(string exeDir, IEnumerable<string> extra = null)
        {
            var list = new List<string>();
            if (extra != null) list.AddRange(extra);
            var env = Environment.GetEnvironmentVariable("NXWEBUI_PAYLOAD");
            if (!string.IsNullOrWhiteSpace(env)) list.Add(env);
            if (!string.IsNullOrWhiteSpace(exeDir))
            {
                list.Add(Path.Combine(exeDir, "payload"));
                list.Add(Path.GetFullPath(Path.Combine(exeDir, "..", "payload")));
            }
            return Unique(list);
        }

        public static PluginStatus GetStatus(DeployContext ctx)
        {
            var installDir = Paths.DefaultInstallDir(ctx.LocalAppData);
            var customDirsFile = Paths.DefaultCustomDirsFile(ctx.LocalAppData);
            var sourceDir = ResolveSourceDir(ctx.SourceCandidates);
            var missing = MissingPayloadFiles(installDir);
            var filesPresent = missing.Count == 0;
            var nxRunning = ctx.IsNxRunning?.Invoke() ?? false;
            var nxInstallations = ctx.ScanNx?.Invoke()?.ToList() ?? new List<NxHint>();
            var preferred = nxInstallations.FirstOrDefault(item => Paths.IsNx2506(item.Release, item.Version))
                ?? nxInstallations.FirstOrDefault();

            var envMap = ctx.EnvStore.Read(new[] { PluginIds.UgiiCustomDirectoryFile });
            envMap.TryGetValue(PluginIds.UgiiCustomDirectoryFile, out var envCustomDirsFile);
            var officialFiles = nxInstallations
                .Where(item => Paths.IsNx2506(item.Release, item.Version))
                .Select(item => Paths.OfficialCustomDirsPath(item.BaseDir))
                .ToList();

            var listed = Unique(CollectListed(envCustomDirsFile, officialFiles, customDirsFile));
            var installKey = Paths.WindowsKey(installDir);
            var registeredFrom = new List<string>();
            if (!string.IsNullOrEmpty(envCustomDirsFile))
            {
                var text = ReadCustomDirs(envCustomDirsFile);
                if (text != null && CustomDirs.Parse(text).Any(dir => Paths.WindowsKey(dir) == installKey))
                    registeredFrom.Add("user-env");
            }
            foreach (var file in officialFiles)
            {
                var text = ReadCustomDirs(file);
                if (text != null && CustomDirs.Parse(text).Any(dir => Paths.WindowsKey(dir) == installKey))
                {
                    registeredFrom.Add("official-custom-dirs");
                    break;
                }
            }
            if (listed.Any(dir => Paths.IsLegacyWebUiDir(dir, installDir)))
                registeredFrom.Add("legacy-path");

            var registered = registeredFrom.Contains("user-env") || registeredFrom.Contains("official-custom-dirs");
            var legacy = registeredFrom.Contains("legacy-path");
            string state;
            if (filesPresent && registered && !legacy) state = "installed";
            else if (filesPresent && !registered && legacy) state = "legacy";
            else if (filesPresent || registered || legacy) state = "partial";
            else state = "not-installed";

            string warning = null;
            if (sourceDir == null && !filesPresent)
                warning = "未找到插件载荷。请把 NxWebUITool\\deploy 拷到部署器 payload\\ 后再试。";
            else if (nxRunning)
                warning = "Siemens NX（ugraf.exe）正在运行。请完全退出 NX 后再安装或修复。";
            else if (preferred != null && !Paths.IsNx2506(preferred.Release, preferred.Version))
                warning = $"本插件面向 NX 2506。检测到 {preferred.Release}（{preferred.Version}）。";
            else if (legacy && filesPresent)
                warning = "仍注册着旧的 NxWebUITool\\deploy 路径。请点「修复」只加载 LocalAppData 副本。";

            return new PluginStatus
            {
                SourceDir = sourceDir,
                InstallDir = installDir,
                CustomDirsFile = customDirsFile,
                Registered = registered,
                FilesPresent = filesPresent,
                MissingFiles = missing,
                State = state,
                NxRunning = nxRunning,
                NxInstallations = nxInstallations,
                PreferredNx = preferred,
                EnvCustomDirsFile = envCustomDirsFile,
                RegisteredFrom = registeredFrom,
                CustomDirectories = listed,
                Warning = warning,
            };
        }

        public static DeployResult Deploy(DeployContext ctx)
        {
            var log = new List<string>();
            var installDir = Paths.DefaultInstallDir(ctx.LocalAppData);
            var customDirsFile = Paths.DefaultCustomDirsFile(ctx.LocalAppData);
            var stateFile = Paths.DefaultStateFile(ctx.LocalAppData);
            try
            {
                if (ctx.IsNxRunning?.Invoke() == true)
                {
                    return Fail(ctx, log, "NX is running", "Siemens NX 正在运行（ugraf.exe）。请完全退出后再部署。");
                }

                var sourceDir = ResolveSourceDir(ctx.SourceCandidates);
                if (sourceDir == null)
                {
                    return Fail(ctx, log, "missing plugin payload", "未找到插件载荷（payload\\ 或 NXWEBUI_PAYLOAD）。");
                }
                log.Add("Source: " + sourceDir);
                log.Add("Install: " + installDir);

                if (Paths.WindowsKey(sourceDir) != Paths.WindowsKey(installDir))
                {
                    var copied = CopyPayload(sourceDir, installDir, log);
                    log.Add($"Copied {copied.Copied} files (skipped {copied.Skipped} pdb/slot files).");
                }
                else
                {
                    log.Add("Source is the install directory — skipped copy, registering in place.");
                }

                var markerPath = Path.Combine(installDir, Paths.MarkerRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(markerPath));
                WriteTextAtomic(markerPath, JsonSerializer.Serialize(new
                {
                    id = PluginIds.WebUi,
                    name = "NX WebUI",
                    installedBy = PluginIds.InstalledBy,
                    installedAt = DateTime.UtcNow.ToString("o"),
                }, JsonOpts) + "\n");

                var nxInstallations = ctx.ScanNx?.Invoke()?.ToList() ?? new List<NxHint>();
                var nx2506 = nxInstallations.Where(item => Paths.IsNx2506(item.Release, item.Version)).ToList();
                var officialFiles = (nx2506.Count > 0 ? nx2506 : nxInstallations)
                    .Select(item => Paths.OfficialCustomDirsPath(item.BaseDir)).ToList();

                var envMap = ctx.EnvStore.Read(new[] { PluginIds.UgiiCustomDirectoryFile });
                envMap.TryGetValue(PluginIds.UgiiCustomDirectoryFile, out var previousEnv);
                var listed = CollectListed(previousEnv, officialFiles, customDirsFile);
                var merged = CustomDirs.Merge(listed, installDir);
                var managedText = CustomDirs.Format(merged);
                WriteTextAtomic(customDirsFile, managedText);
                WriteTextAtomic(Path.Combine(installDir, "custom_dirs.dat"), managedText);
                log.Add("Wrote " + customDirsFile);
                log.Add("Registered directories:\n  " + string.Join("\n  ", merged));

                var previousState = ReadInstallState(stateFile);
                ctx.EnvStore.Write(new Dictionary<string, string>
                {
                    [PluginIds.UgiiCustomDirectoryFile] = customDirsFile,
                });
                log.Add("Set user env " + PluginIds.UgiiCustomDirectoryFile);

                var patchedOfficial = previousState?.PatchedOfficial ?? new List<string>();
                foreach (var file in officialFiles)
                {
                    var original = ReadCustomDirs(file);
                    if (original == null)
                    {
                        log.Add("Skip official custom_dirs (missing): " + file);
                        continue;
                    }
                    var patched = CustomDirs.Patch(original, installDir);
                    if (!patched.Changed)
                    {
                        log.Add("Official custom_dirs already lists the plugin: " + file);
                        if (!patchedOfficial.Any(item => Paths.WindowsKey(item) == Paths.WindowsKey(file)))
                            patchedOfficial.Add(file);
                        continue;
                    }
                    try
                    {
                        WriteTextAtomic(file, patched.Text);
                        if (!patchedOfficial.Any(item => Paths.WindowsKey(item) == Paths.WindowsKey(file)))
                            patchedOfficial.Add(file);
                        log.Add("Updated official custom_dirs: " + file);
                    }
                    catch (Exception ex)
                    {
                        log.Add($"Could not write official custom_dirs ({ex.Message}): {file}");
                    }
                }

                var state = new InstallState
                {
                    InstalledAt = DateTime.UtcNow.ToString("o"),
                    InstallDir = installDir,
                    CustomDirsFile = customDirsFile,
                    PreviousEnv = previousState?.PreviousEnv ?? previousEnv,
                    PatchedOfficial = patchedOfficial,
                };
                WriteTextAtomic(stateFile, JsonSerializer.Serialize(state, JsonOpts) + "\n");
                NxScan.InvalidateCache();

                var status = GetStatus(ctx);
                var ok = status.FilesPresent && status.Registered;
                log.Add(ok
                    ? "Deploy finished. Fully quit and restart NX 2506 (not just File → New)."
                    : "Deploy wrote files but registration is incomplete.");
                return new DeployResult { Ok = ok, Status = status, Log = log };
            }
            catch (Exception ex)
            {
                return new DeployResult
                {
                    Ok = false,
                    Status = SafeStatus(ctx, installDir, customDirsFile),
                    Log = log,
                    Error = ex.Message,
                };
            }
        }

        public static DeployResult Uninstall(DeployContext ctx)
        {
            var log = new List<string>();
            var installDir = Paths.DefaultInstallDir(ctx.LocalAppData);
            var customDirsFile = Paths.DefaultCustomDirsFile(ctx.LocalAppData);
            var stateFile = Paths.DefaultStateFile(ctx.LocalAppData);
            try
            {
                if (ctx.IsNxRunning?.Invoke() == true)
                {
                    return Fail(ctx, log, "NX is running", "Siemens NX 正在运行（ugraf.exe）。请完全退出后再卸载。");
                }

                var state = ReadInstallState(stateFile);
                var nxInstallations = ctx.ScanNx?.Invoke()?.ToList() ?? new List<NxHint>();
                var officialFiles = Unique(
                    (state?.PatchedOfficial ?? new List<string>())
                    .Concat(nxInstallations.Where(item => Paths.IsNx2506(item.Release, item.Version))
                        .Select(item => Paths.OfficialCustomDirsPath(item.BaseDir))));

                foreach (var file in officialFiles)
                {
                    var original = ReadCustomDirs(file);
                    if (original == null) continue;
                    var stripped = CustomDirs.Strip(original, installDir);
                    if (!stripped.Changed) continue;
                    try
                    {
                        WriteTextAtomic(file, stripped.Text);
                        log.Add("Removed plugin path from " + file);
                    }
                    catch (Exception ex)
                    {
                        log.Add($"Could not edit {file}: {ex.Message}");
                    }
                }

                var envMap = ctx.EnvStore.Read(new[] { PluginIds.UgiiCustomDirectoryFile });
                envMap.TryGetValue(PluginIds.UgiiCustomDirectoryFile, out var currentEnv);
                var managed = ReadCustomDirs(customDirsFile);
                var remaining = CustomDirs.Merge(managed != null ? CustomDirs.Parse(managed) : new List<string>(), installDir)
                    .Where(dir => Paths.WindowsKey(dir) != Paths.WindowsKey(installDir) && !Paths.IsLegacyWebUiDir(dir, installDir))
                    .ToList();

                if (remaining.Count > 0)
                {
                    WriteTextAtomic(customDirsFile, CustomDirs.Format(remaining));
                    log.Add($"Kept {remaining.Count} other custom directories in {customDirsFile}");
                    if (string.IsNullOrEmpty(currentEnv) || Paths.WindowsKey(currentEnv) == Paths.WindowsKey(customDirsFile))
                    {
                        ctx.EnvStore.Write(new Dictionary<string, string>
                        {
                            [PluginIds.UgiiCustomDirectoryFile] = customDirsFile,
                        });
                    }
                }
                else
                {
                    string restore = null;
                    if (!string.IsNullOrEmpty(state?.PreviousEnv)
                        && Paths.WindowsKey(state.PreviousEnv) != Paths.WindowsKey(customDirsFile))
                    {
                        restore = state.PreviousEnv;
                    }
                    ctx.EnvStore.Write(new Dictionary<string, string>
                    {
                        [PluginIds.UgiiCustomDirectoryFile] = restore,
                    });
                    try { File.Delete(customDirsFile); } catch { /* ignore */ }
                    log.Add(restore != null
                        ? "Restored " + PluginIds.UgiiCustomDirectoryFile + " to " + restore
                        : "Cleared " + PluginIds.UgiiCustomDirectoryFile);
                }

                if (Directory.Exists(installDir))
                {
                    Directory.Delete(installDir, true);
                    log.Add("Removed " + installDir);
                }
                try { File.Delete(stateFile); } catch { /* ignore */ }
                NxScan.InvalidateCache();
                log.Add("Uninstall finished. Fully quit and restart NX if it is open.");

                var status = GetStatus(ctx);
                return new DeployResult { Ok = status.State == "not-installed", Status = status, Log = log };
            }
            catch (Exception ex)
            {
                return new DeployResult
                {
                    Ok = false,
                    Status = GetStatus(ctx),
                    Log = log,
                    Error = ex.Message,
                };
            }
        }

        static DeployResult Fail(DeployContext ctx, List<string> log, string error, string message)
        {
            log.Add(message);
            return new DeployResult
            {
                Ok = false,
                Status = GetStatus(ctx),
                Log = log,
                Error = error,
            };
        }

        static PluginStatus SafeStatus(DeployContext ctx, string installDir, string customDirsFile)
        {
            try { return GetStatus(ctx); }
            catch
            {
                return new PluginStatus
                {
                    InstallDir = installDir,
                    CustomDirsFile = customDirsFile,
                    MissingFiles = RequiredFiles.All.ToList(),
                };
            }
        }

        static string ResolveSourceDir(IEnumerable<string> candidates)
        {
            foreach (var candidate in candidates ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                try
                {
                    var resolved = Path.GetFullPath(candidate);
                    if (MissingPayloadFiles(resolved).Count == 0) return resolved;
                }
                catch { /* skip */ }
            }
            return null;
        }

        public static List<string> MissingPayloadFiles(string root)
        {
            var missing = new List<string>();
            foreach (var relative in RequiredFiles.All)
            {
                var parts = relative.Split('/');
                var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
                if (!File.Exists(path)) missing.Add(relative);
            }
            return missing;
        }

        static (int Copied, int Skipped) CopyPayload(string sourceDir, string installDir, List<string> log)
        {
            var copied = 0;
            var skipped = 0;
            foreach (var folder in PayloadFolders)
            {
                var from = Path.Combine(sourceDir, folder);
                if (!Directory.Exists(from)) throw new InvalidOperationException("Plugin payload is missing " + folder + "\\.");
                var inner = CopyTree(from, Path.Combine(installDir, folder), folder);
                copied += inner.Copied;
                skipped += inner.Skipped;
                log.Add($"Copied {folder}\\ ({inner.Copied} files).");
            }
            return (copied, skipped);
        }

        static (int Copied, int Skipped) CopyTree(string from, string to, string relative)
        {
            Directory.CreateDirectory(to);
            var copied = 0;
            var skipped = 0;
            foreach (var entry in Directory.GetFileSystemEntries(from))
            {
                var name = Path.GetFileName(entry);
                var rel = string.IsNullOrEmpty(relative) ? name : relative + "/" + name;
                var dest = Path.Combine(to, name);
                if (Directory.Exists(entry))
                {
                    var inner = CopyTree(entry, dest, rel);
                    copied += inner.Copied;
                    skipped += inner.Skipped;
                    continue;
                }
                if (!File.Exists(entry)) continue;
                if (Paths.ShouldSkipPayloadFile(rel))
                {
                    skipped += 1;
                    continue;
                }
                File.Copy(entry, dest, true);
                copied += 1;
            }
            return (copied, skipped);
        }

        static List<string> CollectListed(string envFile, IEnumerable<string> officialFiles, string managedFile)
        {
            var files = new List<string>();
            files.AddRange(officialFiles ?? Array.Empty<string>());
            if (!string.IsNullOrEmpty(envFile)) files.Add(envFile);
            files.Add(managedFile);
            var dirs = new List<string>();
            foreach (var file in files)
            {
                var text = ReadCustomDirs(file);
                if (text != null) dirs.AddRange(CustomDirs.Parse(text));
            }
            return dirs;
        }

        static List<string> Unique(IEnumerable<string> dirs)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var output = new List<string>();
            foreach (var dir in dirs ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string key;
                try { key = Paths.WindowsKey(dir); }
                catch { continue; }
                if (!seen.Add(key)) continue;
                output.Add(Path.GetFullPath(dir).TrimEnd('\\', '/'));
            }
            return output;
        }

        static string ReadCustomDirs(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                return CustomDirs.Decode(File.ReadAllBytes(filePath));
            }
            catch { return null; }
        }

        static InstallState ReadInstallState(string stateFile)
        {
            try
            {
                if (!File.Exists(stateFile)) return null;
                var parsed = JsonSerializer.Deserialize<InstallState>(File.ReadAllText(stateFile), JsonOpts);
                if (parsed == null || parsed.Version != 1 || parsed.Plugin != PluginIds.WebUi) return null;
                if (string.IsNullOrEmpty(parsed.InstallDir) || string.IsNullOrEmpty(parsed.CustomDirsFile)) return null;
                return parsed;
            }
            catch { return null; }
        }

        static void WriteTextAtomic(string filePath, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
            var temporary = filePath + "." + ProcessId() + ".tmp";
            File.WriteAllText(temporary, text);
            try
            {
                if (File.Exists(filePath)) File.Delete(filePath);
                File.Move(temporary, filePath);
            }
            catch
            {
                try { File.Delete(temporary); } catch { /* ignore */ }
                throw;
            }
        }

        static int ProcessId()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().Id; }
            catch { return 0; }
        }
    }
}
