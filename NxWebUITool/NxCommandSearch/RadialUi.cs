using System.Text.Json;

namespace NxWebUITool
{
    /// <summary>
    /// Visual style for the space-bar radial menu. Stored beside the slots
    /// file so NX SaveSlots cannot drop it. AgentManager writes the same path.
    /// </summary>
    static class RadialUi
    {
        public const string Classic = "classic";
        public const string RadialZ = "radialz";

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NxWebUITool",
            "radial-ui.json");

        public static string Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return Classic;
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return Classic;
                if (!root.TryGetProperty("style", out var style) &&
                    !root.TryGetProperty("Style", out style))
                    return Classic;
                return Normalize(style.GetString());
            }
            catch
            {
                return Classic;
            }
        }

        public static string Write(string value)
        {
            var style = Normalize(value);
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(
                new StyleFile { style = style },
                new JsonSerializerOptions { WriteIndented = true });
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
            return style;
        }

        public static string WriteFromJson(JsonElement payload)
        {
            if (payload.ValueKind == JsonValueKind.String)
                return Write(payload.GetString());
            if (payload.ValueKind == JsonValueKind.Object &&
                (payload.TryGetProperty("style", out var style) ||
                 payload.TryGetProperty("Style", out style)))
                return Write(style.GetString());
            return Write(Classic);
        }

        public static string Normalize(string value)
        {
            return IsRadialZ(value) ? RadialZ : Classic;
        }

        public static bool IsRadialZ(string value)
        {
            return string.Equals(value, RadialZ, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "radial-z", StringComparison.OrdinalIgnoreCase);
        }

        sealed class StyleFile
        {
            public string style { get; set; }
        }
    }
}
