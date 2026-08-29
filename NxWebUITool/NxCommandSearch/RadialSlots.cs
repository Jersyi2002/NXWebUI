using System.Text.Json;
using System.Text.Json.Serialization;

namespace NxWebUITool
{
    sealed class RadialSlotDto
    {
        public string id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public string cat { get; set; }
        public string bitmap { get; set; }
        public string icon { get; set; }
        public RadialSlotDto[] children { get; set; }
        // 旧版单子槽兼容字段；NormalizeChildren 会迁移到 children 并清空。
        public RadialSlotDto sub { get; set; }
    }

    static class RadialSlots
    {
        /// <summary>NX 原生 Radial 固定 8 槽。空格环形父槽见 MinCount/MaxCount。</summary>
        public const int Count = 8;
        public const int MinCount = 4;
        public const int MaxCount = 10;
        public const int DefaultCount = 8;

        public static readonly string[] DefaultIds =
        {
            "UG_CREATE_SKETCH",
            "UG_MODELING_EXTRUDED_FEATURE",
            "UG_MODELING_REVOLVED_FEATURE",
            "UG_MODELING_HOLE_FEATURE",
            "UG_MODELING_BLEND_FEATURE",
            "UG_MODELING_SUBTRACT_FEATURE",
            "UG_MODELING_UNITE_FEATURE",
            "UG_VIEW_FIT"
        };

        static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NxWebUITool",
            "radial-slots.json");

        /// <summary>Sibling overflow file: slots trimmed by a parent-count
        /// decrease, keyed by position, so growing back restores them instead
        /// of showing empty slots. Never read by NX core.</summary>
        public static string StashPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NxWebUITool",
            "radial-slots-stash.json");

        static string LegacyPath
        {
            get
            {
                var dir = Path.GetDirectoryName(typeof(RadialSlots).Assembly.Location) ?? ".";
                return Path.Combine(dir, "radial-slots.json");
            }
        }

        // ---- 弹窗载荷记忆化：空格菜单每次弹出都会 loadSlots，逐次重读文件 +
        // 逐槽解码图标是弹出延迟的主要来源。按槽位文件的 (mtime, size) 失效；
        // 命令目录本身会话内记忆化，无需进键。
        static readonly object MemoGate = new();
        static RadialSlotDto[] _memoSlots;
        static DateTime _memoStamp;
        static long _memoLen;

        public static RadialSlotDto[] LoadWithIcons()
        {
            lock (MemoGate)
            {
                DateTime stamp;
                long len;
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var info = new FileInfo(FilePath);
                        stamp = info.LastWriteTimeUtc;
                        len = info.Length;
                    }
                    else
                    {
                        stamp = DateTime.MinValue;
                        len = 0;
                    }
                }
                catch
                {
                    stamp = DateTime.MinValue;
                    len = 0;
                }
                if (_memoSlots == null || stamp != _memoStamp || len != _memoLen)
                {
                    var slots = Read() ?? Defaults();
                    EnrichForDisplay(slots);
                    foreach (var slot in EnumerateAll(slots))
                    {
                        if (slot == null || string.IsNullOrWhiteSpace(slot.bitmap)) continue;
                        slot.icon = NxIconCache.DataUrl(slot.bitmap);
                    }
                    _memoSlots = slots;
                    _memoStamp = stamp;
                    _memoLen = len;
                }
                return _memoSlots;
            }
        }

        /// <summary>圆盘弹窗的瘦身载荷：槽位只带 bitmap 名，图标集中一张
        /// 去重映射，另附最近使用列表（空槽动态填充 / 圆心重复上次）。</summary>
        public sealed class RadialPayloadDto
        {
            public RadialSlotDto[] slots { get; set; }
            public Dictionary<string, string> icons { get; set; }
            public List<RadialUsage.UsageItem> usage { get; set; }
        }

        public static RadialPayloadDto LoadForRadial()
        {
            var enriched = LoadWithIcons();
            var slots = new RadialSlotDto[enriched.Length];
            var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < enriched.Length; i++)
                slots[i] = CloneWithoutIcon(enriched[i], icons);
            var usage = RadialUsage.TopRecent(8);
            if (usage != null)
            {
                foreach (var item in usage)
                {
                    if (string.IsNullOrWhiteSpace(item.bitmap) || icons.ContainsKey(item.bitmap)) continue;
                    var url = NxIconCache.DataUrl(item.bitmap);
                    if (url != null) icons[item.bitmap] = url;
                }
            }
            return new RadialPayloadDto { slots = slots, icons = icons, usage = usage };
        }

        static RadialSlotDto CloneWithoutIcon(RadialSlotDto source, Dictionary<string, string> icons)
        {
            if (source == null) return null;
            if (!string.IsNullOrWhiteSpace(source.bitmap) && !string.IsNullOrEmpty(source.icon))
                icons[source.bitmap] = source.icon;
            var clone = new RadialSlotDto
            {
                id = source.id,
                type = source.type,
                name = source.name,
                cat = source.cat,
                bitmap = source.bitmap,
            };
            if (source.children != null && source.children.Length > 0)
            {
                clone.children = new RadialSlotDto[source.children.Length];
                for (int i = 0; i < source.children.Length; i++)
                    clone.children[i] = CloneWithoutIcon(source.children[i], icons);
            }
            return clone;
        }

        static IEnumerable<RadialSlotDto> EnumerateAll(RadialSlotDto[] slots)
        {
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                yield return slot;
                if (slot.children == null) continue;
                foreach (var child in slot.children)
                    if (child != null) yield return child;
            }
        }

        public static bool IsValidCount(int n) => n >= MinCount && n <= MaxCount;

        public static void SaveFromJson(JsonElement payload)
        {
            JsonElement arr = default;
            bool hasArr = false;
            if (payload.ValueKind == JsonValueKind.Array)
            {
                arr = payload;
                hasArr = true;
            }
            else if (payload.ValueKind == JsonValueKind.Object &&
                     (payload.TryGetProperty("slots", out arr) || payload.TryGetProperty("Slots", out arr)) &&
                     arr.ValueKind == JsonValueKind.Array)
            {
                hasArr = true;
            }

            int n = hasArr ? arr.GetArrayLength() : DefaultCount;
            if (n < MinCount) n = MinCount;
            if (n > MaxCount) n = MaxCount;
            var slots = new RadialSlotDto[n];
            if (hasArr)
            {
                int i = 0;
                foreach (var el in arr.EnumerateArray())
                {
                    if (i >= n) break;
                    slots[i++] = FromJson(el);
                }
            }

            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(slots, JsonOpts));
            lock (MemoGate)
            {
                _memoSlots = null;
            }
        }

        /// <summary>Lenient stash read: missing or corrupt file degrades to an
        /// empty stash — a broken overflow file must never hide saved slots.</summary>
        public static RadialSlotDto[] ReadStash()
        {
            try
            {
                if (!File.Exists(StashPath)) return new RadialSlotDto[MaxCount];
                var raw = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(StashPath));
                return StashFromJson(raw.ValueKind == JsonValueKind.Array ? raw : default);
            }
            catch
            {
                return new RadialSlotDto[MaxCount];
            }
        }

        /// <summary>Writes the stash when the payload carries one; absent field
        /// leaves the file untouched (older callers stay compatible).</summary>
        public static void SaveStashFromJson(JsonElement payload)
        {
            if (payload.ValueKind != JsonValueKind.Array) return;
            var dir = Path.GetDirectoryName(StashPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(StashPath, JsonSerializer.Serialize(StashFromJson(payload), JsonOpts));
        }

        static RadialSlotDto[] StashFromJson(JsonElement arr)
        {
            var slots = new RadialSlotDto[MaxCount];
            if (arr.ValueKind != JsonValueKind.Array) return slots;
            int i = 0;
            foreach (var el in arr.EnumerateArray())
            {
                if (i >= MaxCount) break;
                slots[i++] = el.ValueKind == JsonValueKind.Object &&
                             el.TryGetProperty("id", out var idEl) &&
                             !string.IsNullOrWhiteSpace(idEl.GetString())
                    ? FromJson(el)
                    : null;
            }
            return slots;
        }

        static RadialSlotDto[] Read()
        {
            foreach (var path in new[] { FilePath, LegacyPath })
            {
                try
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    var slots = JsonSerializer.Deserialize<RadialSlotDto[]>(File.ReadAllText(path), JsonOpts);
                    if (slots == null || !IsValidCount(slots.Length)) continue;
                    NormalizeChildren(slots);
                    return slots;
                }
                catch
                {
                    /* 读下一个位置 */
                }
            }
            return null;
        }

        // 读入侧收敛：最多三个叶子子槽；旧 sub 自动迁移为 children[0]。
        static void NormalizeChildren(RadialSlotDto[] slots)
        {
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                var normalized = new List<RadialSlotDto>(3);
                if (slot.children != null)
                {
                    foreach (var child in slot.children)
                    {
                        if (normalized.Count >= 3) break;
                        if (child == null || string.IsNullOrWhiteSpace(child.id)) continue;
                        child.children = null;
                        child.sub = null;
                        normalized.Add(child);
                    }
                }
                if (normalized.Count == 0 && slot.sub != null && !string.IsNullOrWhiteSpace(slot.sub.id))
                {
                    slot.sub.children = null;
                    slot.sub.sub = null;
                    normalized.Add(slot.sub);
                }
                slot.children = normalized.Count == 0 ? null : normalized.ToArray();
                slot.sub = null;
            }
        }

        static RadialSlotDto[] Defaults()
        {
            var slots = new RadialSlotDto[DefaultCount];
            for (int i = 0; i < DefaultCount; i++)
                slots[i] = new RadialSlotDto { id = DefaultIds[i], type = "BUTTON" };
            return slots;
        }

        internal static void EnrichForDisplay(RadialSlotDto[] slots)
        {
            var catalog = CommandCatalog.Load();
            var map = new Dictionary<string, CommandCatalog.CommandItem>(StringComparer.OrdinalIgnoreCase);
            if (catalog?.commands != null)
            {
                foreach (var cmd in catalog.commands)
                {
                    if (!string.IsNullOrWhiteSpace(cmd.id) && !map.ContainsKey(cmd.id))
                        map[cmd.id] = cmd;
                }
            }

            foreach (var slot in EnumerateAll(slots))
            {
                if (string.IsNullOrWhiteSpace(slot.id)) continue;
                if (!map.TryGetValue(slot.id, out var cmd)) continue;
                if (string.IsNullOrWhiteSpace(slot.name)) slot.name = cmd.name;
                if (string.IsNullOrWhiteSpace(slot.cat)) slot.cat = cmd.cat;
                if (string.IsNullOrWhiteSpace(slot.type)) slot.type = cmd.type;
                if (string.IsNullOrWhiteSpace(slot.bitmap)) slot.bitmap = cmd.bitmap;
            }
        }

        static RadialSlotDto FromJson(JsonElement el)
        {
            var slot = LeafFromJson(el);
            if (slot == null) return null;
            if (el.TryGetProperty("children", out var childrenEl) && childrenEl.ValueKind == JsonValueKind.Array)
            {
                var children = new List<RadialSlotDto>(3);
                foreach (var childEl in childrenEl.EnumerateArray())
                {
                    if (children.Count >= 3) break;
                    var child = LeafFromJson(childEl);
                    if (child != null) children.Add(child);
                }
                if (children.Count > 0) slot.children = children.ToArray();
            }
            if ((slot.children == null || slot.children.Length == 0) && el.TryGetProperty("sub", out var subEl))
                slot.children = LeafFromJson(subEl) is RadialSlotDto legacy ? new[] { legacy } : null;
            return slot;
        }

        static RadialSlotDto LeafFromJson(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) return null;
            return new RadialSlotDto
            {
                id = id,
                type = el.TryGetProperty("type", out var tEl) ? tEl.GetString() : "BUTTON",
                name = el.TryGetProperty("name", out var nEl) ? nEl.GetString() : null,
                cat = el.TryGetProperty("cat", out var cEl) ? cEl.GetString() : null,
                bitmap = el.TryGetProperty("bitmap", out var bEl) ? bEl.GetString() : null
            };
        }
    }
}
