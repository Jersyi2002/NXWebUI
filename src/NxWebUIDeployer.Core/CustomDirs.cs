using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NxWebUIDeployer
{
    public static class CustomDirs
    {
        static CustomDirs()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static List<string> Parse(string text)
        {
            var dirs = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return dirs;
            foreach (var raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = raw.Trim().Trim('"');
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//")) continue;
                if (line.ToLowerInvariant().StartsWith("#include")) continue;
                string key;
                try { key = Paths.WindowsKey(line); }
                catch { continue; }
                if (!seen.Add(key)) continue;
                dirs.Add(line.TrimEnd('\\', '/'));
            }
            return dirs;
        }

        public static string Format(IEnumerable<string> dirs)
        {
            var lines = new List<string>
            {
                "# Managed by NX WebUI Deployer — NX WebUI plugin registration.",
                "# Other product directories are preserved. Comments start with #.",
                "",
            };
            lines.AddRange(dirs);
            lines.Add("");
            return string.Join("\r\n", lines);
        }

        public static List<string> Merge(IEnumerable<string> existing, string installDir)
        {
            var output = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var installKey = Paths.WindowsKey(installDir);
            foreach (var dir in existing ?? Array.Empty<string>())
            {
                var trimmed = (dir ?? "").Trim();
                if (trimmed.Length == 0) continue;
                string key;
                try { key = Paths.WindowsKey(trimmed); }
                catch { continue; }
                if (seen.Contains(key)) continue;
                if (key == installKey || Paths.IsLegacyWebUiDir(trimmed, installDir)) continue;
                seen.Add(key);
                output.Add(Path.GetFullPath(trimmed).TrimEnd('\\', '/'));
            }
            if (!seen.Contains(installKey))
                output.Add(Path.GetFullPath(installDir).TrimEnd('\\', '/'));
            return output;
        }

        public static (string Text, bool Changed) Patch(string original, string installDir)
        {
            var installKey = Paths.WindowsKey(installDir);
            var resolvedInstall = Path.GetFullPath(installDir).TrimEnd('\\', '/');
            var newline = original.Contains("\r\n") ? "\r\n" : "\n";
            var endsWithNewline = original.EndsWith("\n");
            var rawLines = original.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            if (rawLines.Count > 0 && rawLines[rawLines.Count - 1] == "") rawLines.RemoveAt(rawLines.Count - 1);

            var next = new List<string>();
            var present = false;
            foreach (var line in rawLines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("//")
                    || trimmed.ToLowerInvariant().StartsWith("#include"))
                {
                    next.Add(line);
                    continue;
                }
                string key;
                try { key = Paths.WindowsKey(trimmed.Trim('"')); }
                catch
                {
                    next.Add(line);
                    continue;
                }
                if (key == installKey)
                {
                    if (!present)
                    {
                        next.Add(resolvedInstall);
                        present = true;
                    }
                    continue;
                }
                if (Paths.IsLegacyWebUiDir(trimmed.Trim('"'), installDir)) continue;
                next.Add(line);
            }
            if (!present) next.Add(resolvedInstall);
            var text = string.Join(newline, next);
            if (endsWithNewline || next.Count > 0) text += newline;
            return (text, text != original);
        }

        public static (string Text, bool Changed) Strip(string original, string installDir)
        {
            var installKey = Paths.WindowsKey(installDir);
            var newline = original.Contains("\r\n") ? "\r\n" : "\n";
            var endsWithNewline = original.EndsWith("\n");
            var rawLines = original.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            if (rawLines.Count > 0 && rawLines[rawLines.Count - 1] == "") rawLines.RemoveAt(rawLines.Count - 1);
            var next = new List<string>();
            var removed = false;
            foreach (var line in rawLines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("//")
                    || trimmed.ToLowerInvariant().StartsWith("#include"))
                {
                    next.Add(line);
                    continue;
                }
                string key;
                try { key = Paths.WindowsKey(trimmed.Trim('"')); }
                catch
                {
                    next.Add(line);
                    continue;
                }
                if (key == installKey || Paths.IsLegacyWebUiDir(trimmed.Trim('"'), installDir))
                {
                    removed = true;
                    continue;
                }
                next.Add(line);
            }
            var text = string.Join(newline, next);
            if (endsWithNewline && next.Count > 0) text += newline;
            return (text, removed || text != original);
        }

        public static string Decode(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0) return "";
            if (buffer.Length >= 2 && buffer[0] == 0xff && buffer[1] == 0xfe)
                return Encoding.Unicode.GetString(buffer, 2, buffer.Length - 2);
            if (buffer.Length >= 3 && buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf)
                return Encoding.UTF8.GetString(buffer, 3, buffer.Length - 3);
            try
            {
                return new UTF8Encoding(false, true).GetString(buffer);
            }
            catch
            {
                try { return Encoding.GetEncoding("GBK").GetString(buffer); }
                catch { return Encoding.UTF8.GetString(buffer); }
            }
        }
    }
}
