using System.Text.Json;

namespace NxWebUITool
{
    /// <summary>
    /// 环形菜单执行日志：%LOCALAPPDATA%\NxWebUITool\radial-usage.json。
    /// 每次真实激活命令时记一笔（id → 次数/最近时间/类型）；空槽动态填充
    /// 与圆心「重复上次」都从这里取最近命令。文件保持小（≤200 条）。
    /// </summary>
    static class RadialUsage
    {
        const int MaxItems = 200;

        static readonly object Gate = new();
        static Dictionary<string, Entry> _items;
        static bool _loaded;

        sealed class Entry
        {
            public int n { get; set; }
            public long t { get; set; }
            public string type { get; set; }
        }

        public sealed class UsageItem
        {
            public string id { get; set; }
            public string type { get; set; }
            public string name { get; set; }
            public string cat { get; set; }
            public string bitmap { get; set; }
            public long t { get; set; }
        }

        static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NxWebUITool",
            "radial-usage.json");

        public static void Record(string id, string type)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (Gate)
            {
                EnsureLoaded();
                _items ??= new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                if (!_items.TryGetValue(id, out var entry))
                    _items[id] = entry = new Entry();
                entry.n += 1;
                entry.t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                entry.type = string.IsNullOrWhiteSpace(type) ? "BUTTON" : type;
                TrimLocked();
                SaveLocked();
            }
        }

        /// <summary>按最近使用时间倒序取前 max 条（带目录元数据，供圆盘展示）。</summary>
        public static List<UsageItem> TopRecent(int max)
        {
            lock (Gate)
            {
                EnsureLoaded();
                if (_items == null || _items.Count == 0) return new List<UsageItem>();
                var picked = _items
                    .OrderByDescending(kv => kv.Value.t)
                    .Take(Math.Max(0, max))
                    .Select(kv => new UsageItem
                    {
                        id = kv.Key,
                        type = string.IsNullOrWhiteSpace(kv.Value.type) ? "BUTTON" : kv.Value.type,
                        t = kv.Value.t,
                    })
                    .ToList();
                foreach (var item in picked)
                {
                    var cmd = CommandCatalog.Find(item.id);
                    if (cmd == null) continue;
                    item.name = cmd.name;
                    item.cat = cmd.cat;
                    item.bitmap = cmd.bitmap;
                    if (!string.IsNullOrWhiteSpace(cmd.type)) item.type = cmd.type;
                }
                return picked;
            }
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
                if (!doc.RootElement.TryGetProperty("items", out var itemsEl)) return;
                _items = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in itemsEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                    var entry = new Entry();
                    if (prop.Value.TryGetProperty("n", out var nEl) && nEl.ValueKind == JsonValueKind.Number)
                        entry.n = nEl.GetInt32();
                    if (prop.Value.TryGetProperty("t", out var tEl) && tEl.ValueKind == JsonValueKind.Number)
                        entry.t = tEl.GetInt64();
                    if (prop.Value.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                        entry.type = typeEl.GetString();
                    _items[prop.Name] = entry;
                }
            }
            catch
            {
                _items = null; // 坏文件当空处理，不影响主流程
            }
        }

        static void TrimLocked()
        {
            if (_items == null || _items.Count <= MaxItems) return;
            var drop = _items.OrderByDescending(kv => kv.Value.t).Skip(MaxItems).Select(kv => kv.Key).ToList();
            foreach (var key in drop) _items.Remove(key);
        }

        static void SaveLocked()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using var buffer = new MemoryStream();
                using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("v", 1);
                    writer.WriteStartObject("items");
                    if (_items != null)
                        foreach (var kv in _items.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            writer.WriteStartObject(kv.Key);
                            writer.WriteNumber("n", kv.Value.n);
                            writer.WriteNumber("t", kv.Value.t);
                            if (!string.IsNullOrEmpty(kv.Value.type)) writer.WriteString("type", kv.Value.type);
                            writer.WriteEndObject();
                        }
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                var tmp = FilePath + ".tmp";
                File.WriteAllBytes(tmp, buffer.ToArray());
                File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch
            {
                /* 日志失败绝不影响命令执行 */
            }
        }
    }
}
