using System.Text;
using System.Text.RegularExpressions;

namespace NxWebUITool
{
    static class CommandCatalog
    {
        static readonly Regex ButtonStart = new(
            @"^(BUTTON|TOGGLE|APPLICATION)\s+(\S+)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex Accel = new(@"\(&.?\)|&", RegexOptions.Compiled);

        static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "NXBIN", "UGOPEN", "LOCALIZATION", "HELP", "help", "pax",
            "Samples", "SAMPLE", "ugfiles", "FLEXLM", "tce", "python",
            "Python", "WebView2", "inst", "licensing", ".git", "obj", "bin"
        };

        static readonly object Gate = new();
        static CatalogDto _cached;
        static bool _warming;
        static Dictionary<string, CommandItem> _idIndex;

        public static CommandItem Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (Gate)
            {
                if (_idIndex == null)
                {
                    _idIndex = new Dictionary<string, CommandItem>(StringComparer.OrdinalIgnoreCase);
                    var dto = Load();
                    if (dto?.commands != null)
                        foreach (var cmd in dto.commands)
                            if (!string.IsNullOrWhiteSpace(cmd.id) && !_idIndex.ContainsKey(cmd.id))
                                _idIndex[cmd.id] = cmd;
                }
                _idIndex.TryGetValue(id, out var item);
                return item;
            }
        }

        public static void Warmup()
        {
            if (_cached != null || _warming) return;
            _warming = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Load(); }
                catch { /* 首次打开时再同步加载 */ }
                finally { _warming = false; }
            });
        }

        public static CatalogDto Load()
        {
            lock (Gate)
            {
                if (_cached != null) return _cached;
                var baseDir = ResolveBaseDir();
                _cached = TryReadCache(baseDir) ?? Build(baseDir);
                TryWriteCache(baseDir, _cached);
                return _cached;
            }
        }

        static string ResolveBaseDir()
        {
            var baseDir = Environment.GetEnvironmentVariable("UGII_BASE_DIR");
            if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
                baseDir = @"E:\NX2506";
            return baseDir;
        }

        static string CachePath(string baseDir)
        {
            var key = Convert.ToBase64String(Encoding.UTF8.GetBytes(baseDir))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            return Path.Combine(Path.GetTempPath(), "NxWebUITool", "catalog-v2-" + key + ".json");
        }

        static CatalogDto TryReadCache(string baseDir)
        {
            try
            {
                var path = CachePath(baseDir);
                if (!File.Exists(path)) return null;
                var stamp = File.GetLastWriteTimeUtc(path);
                var newest = CatalogStamp(baseDir);
                if (newest > stamp.AddSeconds(2)) return null;
                var json = File.ReadAllText(path, Encoding.UTF8);
                return System.Text.Json.JsonSerializer.Deserialize<CatalogDto>(json);
            }
            catch
            {
                return null;
            }
        }

        static void TryWriteCache(string baseDir, CatalogDto dto)
        {
            try
            {
                var path = CachePath(baseDir);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var json = System.Text.Json.JsonSerializer.Serialize(dto);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch
            {
                /* 缓存失败不影响使用 */
            }
        }

        static DateTime CatalogStamp(string baseDir)
        {
            var main = Path.Combine(baseDir, "UGII", "menus", "definitions_main.btn");
            return File.Exists(main) ? File.GetLastWriteTimeUtc(main) : DateTime.MinValue;
        }

        static IEnumerable<string> EnumerateBtnFiles(string baseDir)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in WalkBtn(baseDir, 0))
            {
                if (seen.Add(file))
                    yield return file;
            }
        }

        static IEnumerable<string> WalkBtn(string dir, int depth)
        {
            if (depth > 6 || !Directory.Exists(dir)) yield break;
            var name = Path.GetFileName(dir);
            if (depth > 0 && SkipDirs.Contains(name)) yield break;

            if (name.Equals("startup", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("menus", StringComparison.OrdinalIgnoreCase))
            {
                IEnumerable<string> files = Array.Empty<string>();
                try { files = Directory.EnumerateFiles(dir, "*.btn"); }
                catch { yield break; }
                foreach (var file in files) yield return file;
                yield break;
            }

            IEnumerable<string> subs = Array.Empty<string>();
            try { subs = Directory.EnumerateDirectories(dir); }
            catch { yield break; }
            foreach (var sub in subs)
            {
                foreach (var file in WalkBtn(sub, depth + 1))
                    yield return file;
            }
        }

        static CatalogDto Build(string baseDir)
        {
            var items = new Dictionary<string, CommandItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in EnumerateBtnFiles(baseDir))
                ParseBtn(file, items);

            ApplyLocalization(baseDir, items);

            var list = items.Values
                .Where(c => !string.IsNullOrWhiteSpace(c.id))
                .OrderBy(c => c.cat, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.name ?? c.id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var categories = list
                .Select(c => c.cat)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new CatalogDto { commands = list, categories = categories };
        }

        static void ParseBtn(string path, Dictionary<string, CommandItem> items)
        {
            string currentId = null;
            string currentType = "BUTTON";
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fileName = Path.GetFileNameWithoutExtension(path);

            void Flush()
            {
                if (string.IsNullOrWhiteSpace(currentId)) return;
                if (currentId.StartsWith("NXWEBUI_", StringComparison.OrdinalIgnoreCase)) return;

                fields.TryGetValue("LABEL", out var label);
                fields.TryGetValue("TOOLBAR_LABEL", out var tb);
                fields.TryGetValue("RIBBON_LABEL", out var rb);
                fields.TryGetValue("MESSAGE", out var msg);
                fields.TryGetValue("SYNONYMS", out var syn);
                fields.TryGetValue("ACCELERATOR", out var key);
                fields.TryGetValue("BITMAP", out var bitmap);

                var display = FirstNonEmpty(StripAccel(tb), StripAccel(rb), StripAccel(label), currentId);
                var category = GuessCategory(currentId, fileName);

                if (!items.ContainsKey(currentId))
                {
                    items[currentId] = new CommandItem
                    {
                        id = currentId,
                        type = currentType,
                        name = display,
                        nameEn = FirstNonEmpty(StripAccel(tb), StripAccel(rb), StripAccel(label)),
                        desc = msg ?? "",
                        synonyms = syn ?? "",
                        cat = category,
                        key = key ?? "",
                        bitmap = bitmap ?? "",
                        source = Path.GetFileName(path)
                    };
                }

                currentId = null;
                currentType = "BUTTON";
                fields.Clear();
            }

            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("!") || line.StartsWith("#")) continue;
                if (line.StartsWith("VERSION", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.Equals("END_OF_FILE", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("END", StringComparison.OrdinalIgnoreCase))
                {
                    Flush();
                    continue;
                }

                var m = ButtonStart.Match(line);
                if (m.Success)
                {
                    Flush();
                    currentType = m.Groups[1].Value.ToUpperInvariant();
                    currentId = m.Groups[2].Value;
                    continue;
                }

                var sp = line.IndexOf(' ');
                if (sp > 0 && currentId != null)
                    fields[line.Substring(0, sp)] = line.Substring(sp + 1).Trim();
            }

            Flush();
        }

        static void ApplyLocalization(string baseDir, Dictionary<string, CommandItem> items)
        {
            var lang = Environment.GetEnvironmentVariable("UGII_LANG") ?? "simpl_chinese";
            var locRoot = Path.Combine(baseDir, "LOCALIZATION", lang);
            if (!Directory.Exists(locRoot)) return;

            var hasToolbar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(locRoot, "*_btn_" + lang + ".txt", SearchOption.TopDirectoryOnly))
            {
                foreach (var raw in File.ReadLines(file, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] != '"') continue;
                    var parts = SplitQuoted(line);
                    if (parts.Count < 4) continue;

                    var key = parts[1];
                    var en = StripAccel(parts[2]);
                    var zh = StripAccel(parts[3]);
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    string id;
                    if (key.EndsWith("_TOOLBAR_LABEL", StringComparison.OrdinalIgnoreCase))
                    {
                        id = key.Substring(0, key.Length - "_TOOLBAR_LABEL".Length);
                        if (!items.TryGetValue(id, out var item)) continue;
                        if (!string.IsNullOrWhiteSpace(zh)) item.name = zh;
                        if (!string.IsNullOrWhiteSpace(en)) item.nameEn = en;
                        hasToolbar.Add(id);
                    }
                    else if (key.EndsWith("_RIBBON_LABEL", StringComparison.OrdinalIgnoreCase))
                    {
                        id = key.Substring(0, key.Length - "_RIBBON_LABEL".Length);
                        if (!items.TryGetValue(id, out var item) || hasToolbar.Contains(id)) continue;
                        if (!string.IsNullOrWhiteSpace(zh)) item.name = zh;
                        if (!string.IsNullOrWhiteSpace(en) && string.IsNullOrWhiteSpace(item.nameEn)) item.nameEn = en;
                    }
                    else if (key.EndsWith("_LABEL", StringComparison.OrdinalIgnoreCase) &&
                             key.IndexOf("POPUP", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        id = key.Substring(0, key.Length - "_LABEL".Length);
                        if (!items.TryGetValue(id, out var item) || hasToolbar.Contains(id)) continue;
                        if (!string.IsNullOrWhiteSpace(zh)) item.name = zh;
                        if (!string.IsNullOrWhiteSpace(en) && string.IsNullOrWhiteSpace(item.nameEn)) item.nameEn = en;
                    }
                    else if (key.EndsWith("_MESSAGE", StringComparison.OrdinalIgnoreCase) &&
                             key.IndexOf("POPUP", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        id = key.Substring(0, key.Length - "_MESSAGE".Length);
                        if (!items.TryGetValue(id, out var item)) continue;
                        if (!string.IsNullOrWhiteSpace(zh)) item.desc = zh;
                    }
                    else if (key.EndsWith("_SYNONYMS", StringComparison.OrdinalIgnoreCase))
                    {
                        id = key.Substring(0, key.Length - "_SYNONYMS".Length);
                        if (!items.TryGetValue(id, out var item)) continue;
                        if (!string.IsNullOrWhiteSpace(zh))
                            item.synonyms = string.IsNullOrWhiteSpace(item.synonyms) ? zh : item.synonyms + ", " + zh;
                    }
                }
            }
        }

        static List<string> SplitQuoted(string line)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            var inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQ)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                        inQ = false;
                    }
                    else inQ = true;
                    continue;
                }
                if (inQ) sb.Append(c);
            }
            return list;
        }

        static string StripAccel(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            return Accel.Replace(s, "").Trim().TrimEnd('.').Trim();
        }

        static string FirstNonEmpty(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v;
            return "";
        }

        static string GuessCategory(string id, string fileName)
        {
            var u = (id ?? "").ToUpperInvariant();
            var f = (fileName ?? "").ToUpperInvariant();

            if (u.Contains("SKETCH") || f.Contains("SKETCH")) return "草图";
            if (u.Contains("ASSEM") || u.Contains("WAVE") || f.Contains("ASSEM")) return "装配";
            if (u.Contains("DRAFT") || u.Contains("DRAWING") || u.Contains("PMI") || f.Contains("DRAFT")) return "制图";
            if (u.Contains("CAM") || u.Contains("MFG") || u.Contains("MACHIN") || f.Contains("CAM") || f.Contains("MFG")) return "加工";
            if (u.Contains("CAE") || u.Contains("SIM") || u.Contains("NAS") || f.Contains("CAE") || f.Contains("SIM")) return "仿真";
            if (u.Contains("SHEET_METAL") || u.Contains("SFM") || f.Contains("SHEET")) return "钣金";
            if (u.Contains("ROUTING") || u.Contains("PIPE") || f.Contains("ROUT")) return "管路";
            if (u.Contains("SHIP") || f.Contains("SHIP")) return "船舶";
            if (u.Contains("MOLD") || u.Contains("DIE") || f.Contains("MOLD") || f.Contains("PROG_DIE")) return "模具";
            if (u.Contains("CMM") || u.Contains("INSPECT") || f.Contains("CMM")) return "检测";
            if (u.Contains("RENDER") || u.Contains("STUDIO") || u.Contains("VIS") || f.Contains("STUDIO")) return "外观";
            if (u.Contains("MODEL") || u.Contains("FEATURE") || u.Contains("SOLID") || u.Contains("EXTRUDE") ||
                u.Contains("BLEND") || u.Contains("HOLE") || u.Contains("BOOLEAN") ||
                u.Contains("REVOLVE") || u.Contains("SWEEP") || u.Contains("PATTERN") ||
                f.Contains("MODELING") || f.Contains("FEATURES"))
                return "建模";
            if (u.Contains("FILE") || u.Contains("PRINT") || u.Contains("PLOT") || u.Contains("EXPORT") || u.Contains("IMPORT"))
                return "文件";
            if (u.Contains("VIEW") || u.Contains("ORIENT") || u.Contains("ZOOM") || u.Contains("FIT"))
                return "视图";
            if (u.Contains("PREF") || u.Contains("CUSTOM") || u.Contains("ROLE"))
                return "首选项";
            if (f.Contains("GATEWAY") || f.Contains("FILE") || f.Contains("VIEW")) return "网关";
            return "其他";
        }

        public sealed class CatalogDto
        {
            public List<CommandItem> commands { get; set; }
            public List<string> categories { get; set; }
        }

        public sealed class CommandItem
        {
            public string id { get; set; }
            public string type { get; set; }
            public string name { get; set; }
            public string nameEn { get; set; }
            public string desc { get; set; }
            public string synonyms { get; set; }
            public string cat { get; set; }
            public string key { get; set; }
            public string bitmap { get; set; }
            public string source { get; set; }
        }
    }
}
