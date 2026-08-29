using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace NxWebUITool
{
    sealed class NativeRadialAppDto
    {
        public string id { get; set; }
        public string name { get; set; }
    }

    sealed class NativeRadialState
    {
        public Dictionary<string, NativeRadialAppState> apps { get; set; } =
            new Dictionary<string, NativeRadialAppState>(StringComparer.OrdinalIgnoreCase);
    }

    sealed class NativeRadialAppState
    {
        public Dictionary<string, RadialSlotDto[]> bars { get; set; } =
            new Dictionary<string, RadialSlotDto[]>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 读写 NX 原生 Application Radial 1/2/3。配置写入自定义目录的应用
    /// Profile，不触碰 NX 安装目录和 NX 自己维护的 user.mtx。
    /// </summary>
    static class NativeRadialProfiles
    {
        const int BarCount = 3;
        const string ListPrefix = "NX_RADIALBAR_";

        static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        static readonly Dictionary<string, string> AppNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["UG_APP_MODELING"] = "建模",
                ["UG_APP_GATEWAY"] = "基础环境",
                ["UG_APP_DRAFTING"] = "制图",
                ["UG_APP_STUDIO"] = "艺术曲面",
                ["UG_APP_SBSM"] = "钣金",
                ["UG_APP_MANUFACTURING"] = "加工",
                ["UG_APP_SKETCH_TASK"] = "草图任务",
                ["UG_APP_SKETCH_LEGACY_TASK"] = "传统草图",
                ["UG_APP_MECHANISMS"] = "运动仿真",
                ["UG_APP_MECHATRONICS"] = "机电概念设计",
                ["UG_APP_INSPECTION"] = "检测编程",
                ["UG_APP_NX_LAYOUT"] = "二维布局"
            };

        sealed class ProfileDefinition
        {
            public string AppId { get; set; }
            public string SourcePath { get; set; }
            public RadialSlotDto[][] Bars { get; set; }
        }

        static Dictionary<string, ProfileDefinition> _profiles;

        static string ConfigPath
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable("NXWEBUI_NATIVE_RADIAL_CONFIG");
                if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NxWebUITool",
                    "native-radial.json");
            }
        }

        static string OutputProfileRoot
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable("NXWEBUI_NATIVE_RADIAL_PROFILE_ROOT");
                if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
                var appDir = Path.GetDirectoryName(typeof(NativeRadialProfiles).Assembly.Location) ?? ".";
                return Path.Combine(appDir, "profiles");
            }
        }

        public static object GetInfo()
        {
            var profiles = Profiles();
            var apps = profiles.Values
                .Select(p => new NativeRadialAppDto { id = p.AppId, name = DisplayName(p.AppId) })
                .OrderBy(a => AppRank(a.id))
                .ThenBy(a => a.name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            if (apps.Length == 0)
                throw new InvalidOperationException("未在当前 NX 安装中找到 Application Radial 配置。");
            var defaultApp = apps.Any(a => a.id.Equals("UG_APP_MODELING", StringComparison.OrdinalIgnoreCase))
                ? "UG_APP_MODELING"
                : apps[0].id;
            return new { apps, defaultApplication = defaultApp, barCount = BarCount };
        }

        public static object Load(JsonElement payload)
        {
            var appId = GetString(payload, "application");
            var bar = GetBar(payload);
            var profile = RequireProfile(appId);
            var state = ReadState();
            var custom = TryGetCustom(state, profile.AppId, bar, out var saved);
            var slots = CloneSlots(custom ? saved : profile.Bars[bar - 1]);
            RadialSlots.EnrichForDisplay(slots);
            AddIcons(slots);
            return new
            {
                application = profile.AppId,
                bar,
                source = custom ? "custom" : "default",
                slots,
                restartRequired = true
            };
        }

        public static object Save(JsonElement payload)
        {
            var appId = GetString(payload, "application");
            var bar = GetBar(payload);
            var profile = RequireProfile(appId);
            var slots = ParseSlots(payload);
            RemoveDuplicates(slots);

            var state = ReadState();
            if (!state.apps.TryGetValue(profile.AppId, out var appState) || appState == null)
            {
                appState = new NativeRadialAppState();
                state.apps[profile.AppId] = appState;
            }
            appState.bars[bar.ToString()] = slots;
            WriteState(state);
            WriteProfile(profile, appState);
            return new { saved = true, restartRequired = true, profile = ProfileOutputPath(profile.AppId) };
        }

        public static object Reset(JsonElement payload)
        {
            var appId = GetString(payload, "application");
            var bar = GetBar(payload);
            var profile = RequireProfile(appId);
            var state = ReadState();

            if (state.apps.TryGetValue(profile.AppId, out var appState) && appState != null)
            {
                appState.bars.Remove(bar.ToString());
                if (appState.bars.Count == 0)
                    state.apps.Remove(profile.AppId);
            }
            WriteState(state);
            if (state.apps.TryGetValue(profile.AppId, out appState) && appState != null)
                WriteProfile(profile, appState);
            else
                DeleteGeneratedProfile(profile.AppId);
            return Load(payload);
        }

        static Dictionary<string, ProfileDefinition> Profiles()
        {
            if (_profiles != null) return _profiles;
            var result = new Dictionary<string, ProfileDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in DiscoverProfileFiles())
            {
                try
                {
                    var profile = ParseProfile(path);
                    if (profile != null && !result.ContainsKey(profile.AppId))
                        result[profile.AppId] = profile;
                }
                catch
                {
                    /* 一个可选模块的损坏配置不应阻塞其它 NX 应用。 */
                }
            }
            _profiles = result;
            return result;
        }

        static IEnumerable<string> DiscoverProfileFiles()
        {
            var baseDir = Environment.GetEnvironmentVariable("UGII_BASE_DIR");
            if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
                baseDir = @"E:\NX2506";
            if (!Directory.Exists(baseDir)) yield break;

            var roots = new List<string>();
            AddProfileRoot(roots, Path.Combine(baseDir, "UGII", "menus", "profiles"));
            AddProfileRoot(roots, Path.Combine(baseDir, "DRAFTING", "application", "profiles"));
            AddProfileRoot(roots, Path.Combine(baseDir, "MACH", "application", "profiles"));

            foreach (var first in SafeDirectories(baseDir))
            {
                AddProfileRoot(roots, Path.Combine(first, "application", "profiles"));
                AddProfileRoot(roots, Path.Combine(first, "menus", "profiles"));
                foreach (var second in SafeDirectories(first))
                {
                    AddProfileRoot(roots, Path.Combine(second, "application", "profiles"));
                    AddProfileRoot(roots, Path.Combine(second, "menus", "profiles"));
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                foreach (var appDir in SafeDirectories(root))
                {
                    var appId = Path.GetFileName(appDir);
                    var path = Path.Combine(appDir, appId + ".dtx");
                    if (File.Exists(path) && seen.Add(path)) yield return path;
                }
            }
        }

        static IEnumerable<string> SafeDirectories(string path)
        {
            try { return Directory.Exists(path) ? Directory.EnumerateDirectories(path).ToArray() : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        static void AddProfileRoot(List<string> roots, string path)
        {
            if (Directory.Exists(path) && !roots.Contains(path, StringComparer.OrdinalIgnoreCase))
                roots.Add(path);
        }

        static ProfileDefinition ParseProfile(string path)
        {
            var doc = XDocument.Load(path, LoadOptions.None);
            var profileEl = doc.Descendants("Profile").FirstOrDefault();
            var appId = (string)profileEl?.Attribute("name") ?? Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(appId)) return null;

            var bars = new RadialSlotDto[BarCount][];
            var found = false;
            for (var bar = 1; bar <= BarCount; bar++)
            {
                bars[bar - 1] = new RadialSlotDto[RadialSlots.Count];
                var list = doc.Descendants("ActionList")
                    .FirstOrDefault(x => string.Equals((string)x.Attribute("name"), ListPrefix + bar, StringComparison.OrdinalIgnoreCase));
                if (list == null) continue;
                found = true;
                foreach (var item in list.Descendants("ActionItem"))
                {
                    if (string.Equals((string)item.Attribute("visibility"), "0", StringComparison.Ordinal)) continue;
                    if (!int.TryParse((string)item.Attribute("radial_position"), out var position))
                        int.TryParse((string)item.Attribute("index"), out position);
                    if (position < 0 || position >= RadialSlots.Count) continue;
                    var id = (string)item.Attribute("name");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    bars[bar - 1][position] = new RadialSlotDto
                    {
                        id = id,
                        type = ((string)item.Attribute("type") ?? "button").ToUpperInvariant()
                    };
                }
            }
            return found ? new ProfileDefinition { AppId = appId.Trim(), SourcePath = path, Bars = bars } : null;
        }

        static ProfileDefinition RequireProfile(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId) || !Profiles().TryGetValue(appId, out var profile))
                throw new InvalidOperationException("NX 应用配置不存在：" + (appId ?? ""));
            return profile;
        }

        static int GetBar(JsonElement payload)
        {
            if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("bar", out var value) &&
                value.TryGetInt32(out var bar) && bar >= 1 && bar <= BarCount)
                return bar;
            throw new InvalidOperationException("Radial 编号必须为 1、2 或 3。");
        }

        static string GetString(JsonElement payload, string name)
        {
            if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
                return value.GetString();
            return null;
        }

        static RadialSlotDto[] ParseSlots(JsonElement payload)
        {
            var result = new RadialSlotDto[RadialSlots.Count];
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("slots", out var arr) ||
                arr.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("缺少 8 槽配置。");
            var i = 0;
            foreach (var el in arr.EnumerateArray())
            {
                if (i >= result.Length) break;
                if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("id", out var idEl))
                {
                    var id = idEl.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        result[i] = new RadialSlotDto
                        {
                            id = id.Trim(),
                            type = GetString(el, "type") ?? "BUTTON",
                            name = GetString(el, "name"),
                            cat = GetString(el, "cat"),
                            bitmap = GetString(el, "bitmap")
                        };
                    }
                }
                i++;
            }
            if (i != RadialSlots.Count)
                throw new InvalidOperationException("NX 原生菜单必须包含 8 个槽位记录。");
            return result;
        }

        static void RemoveDuplicates(RadialSlotDto[] slots)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < slots.Length; i++)
            {
                var id = slots[i]?.id;
                if (!string.IsNullOrWhiteSpace(id) && !seen.Add(id)) slots[i] = null;
            }
        }

        static NativeRadialState ReadState()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new NativeRadialState();
                var state = JsonSerializer.Deserialize<NativeRadialState>(File.ReadAllText(ConfigPath), JsonOpts);
                if (state?.apps == null) return new NativeRadialState();
                foreach (var app in state.apps.Values)
                    if (app != null && app.bars == null)
                        app.bars = new Dictionary<string, RadialSlotDto[]>(StringComparer.OrdinalIgnoreCase);
                return state;
            }
            catch
            {
                return new NativeRadialState();
            }
        }

        static void WriteState(NativeRadialState state)
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            WriteUtf8Atomic(ConfigPath, JsonSerializer.Serialize(state, JsonOpts));
        }

        static bool TryGetCustom(NativeRadialState state, string appId, int bar, out RadialSlotDto[] slots)
        {
            slots = null;
            return state.apps.TryGetValue(appId, out var app) && app != null &&
                   app.bars.TryGetValue(bar.ToString(), out slots) && slots != null &&
                   slots.Length == RadialSlots.Count;
        }

        static void WriteProfile(ProfileDefinition profile, NativeRadialAppState appState)
        {
            var lists = new XElement("ActionLists");
            for (var bar = 1; bar <= BarCount; bar++)
            {
                if (!appState.bars.TryGetValue(bar.ToString(), out var target) || target == null ||
                    target.Length != RadialSlots.Count) continue;
                lists.Add(BuildActionList(bar, profile.Bars[bar - 1], target));
            }

            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("NX_PROFILES",
                    new XElement("Profile", new XAttribute("name", profile.AppId),
                        new XElement("Content", new XAttribute("mode", "edit"), lists))));
            var path = ProfileOutputPath(profile.AppId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var writer = new Utf8StringWriter();
            doc.Save(writer, SaveOptions.None);
            WriteUtf8Atomic(path, writer.ToString());
        }

        static XElement BuildActionList(int bar, RadialSlotDto[] defaults, RadialSlotDto[] target)
        {
            var items = new XElement("ActionItems");
            var visible = new HashSet<string>(
                target.Where(x => x != null && !string.IsNullOrWhiteSpace(x.id)).Select(x => x.id),
                StringComparer.OrdinalIgnoreCase);

            foreach (var original in defaults)
            {
                if (original == null || string.IsNullOrWhiteSpace(original.id) || visible.Contains(original.id)) continue;
                items.Add(new XElement("ActionItem",
                    new XAttribute("name", original.id),
                    new XAttribute("type", DtxType(original.type)),
                    new XAttribute("visibility", "0")));
            }
            for (var position = 0; position < target.Length; position++)
            {
                var slot = target[position];
                if (slot == null || string.IsNullOrWhiteSpace(slot.id)) continue;
                items.Add(new XElement("ActionItem",
                    new XAttribute("index", position),
                    new XAttribute("radial_position", position),
                    new XAttribute("name", slot.id),
                    new XAttribute("type", DtxType(slot.type)),
                    new XAttribute("visibility", "1")));
            }
            return new XElement("ActionList",
                new XAttribute("name", ListPrefix + bar),
                new XAttribute("type", "popupmenu"),
                items);
        }

        static string DtxType(string type)
        {
            var value = (type ?? "BUTTON").Trim().ToLowerInvariant();
            return value == "toggle" || value == "application" ? value : "button";
        }

        static string ProfileOutputPath(string appId) =>
            Path.Combine(OutputProfileRoot, appId, appId + ".dtx");

        static void DeleteGeneratedProfile(string appId)
        {
            var path = ProfileOutputPath(appId);
            if (File.Exists(path)) File.Delete(path);
        }

        static void AddIcons(RadialSlotDto[] slots)
        {
            foreach (var slot in slots)
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.bitmap)) continue;
                slot.icon = NxIconCache.DataUrl(slot.bitmap);
            }
        }

        static RadialSlotDto[] CloneSlots(RadialSlotDto[] source)
        {
            var result = new RadialSlotDto[RadialSlots.Count];
            if (source == null) return result;
            for (var i = 0; i < Math.Min(result.Length, source.Length); i++)
            {
                var slot = source[i];
                if (slot == null) continue;
                result[i] = new RadialSlotDto
                {
                    id = slot.id,
                    type = slot.type,
                    name = slot.name,
                    cat = slot.cat,
                    bitmap = slot.bitmap
                };
            }
            return result;
        }

        static string DisplayName(string appId) =>
            AppNames.TryGetValue(appId, out var name) ? name : appId;

        static int AppRank(string appId)
        {
            if (appId.Equals("UG_APP_MODELING", StringComparison.OrdinalIgnoreCase)) return 0;
            if (appId.Equals("UG_APP_DRAFTING", StringComparison.OrdinalIgnoreCase)) return 1;
            if (appId.Equals("UG_APP_GATEWAY", StringComparison.OrdinalIgnoreCase)) return 2;
            if (appId.Equals("UG_APP_SBSM", StringComparison.OrdinalIgnoreCase)) return 3;
            if (appId.Equals("UG_APP_MANUFACTURING", StringComparison.OrdinalIgnoreCase)) return 4;
            return 20;
        }

        static void WriteUtf8Atomic(string path, string content)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }

        sealed class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => new UTF8Encoding(false);
        }
    }
}
