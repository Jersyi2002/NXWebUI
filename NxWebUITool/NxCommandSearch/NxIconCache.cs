using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace NxWebUITool
{
    /// <summary>
    /// 从 UGII/bitmaps/*.bma 取出 NX 原生日录图标，转成 PNG data URL。
    /// </summary>
    static class NxIconCache
    {
        static readonly object Gate = new();
        static BmaArchive _archive;
        static bool _archiveProbed;
        // name → data URL（null = 解不出，也缓存，避免每次弹出重复解码）
        static readonly Dictionary<string, string> UrlCache =
            new(StringComparer.OrdinalIgnoreCase);

        public static string DirectoryPath { get; } =
            Path.Combine(Path.GetTempPath(), "NxWebUITool", "icons");

        public static string DataUrl(string bitmapName)
        {
            if (string.IsNullOrWhiteSpace(bitmapName)) return null;
            lock (Gate)
            {
                if (UrlCache.TryGetValue(bitmapName, out var cached)) return cached;
                var png = EnsurePngBytes(bitmapName);
                var url = (png != null && png.Length >= 32)
                    ? "data:image/png;base64," + Convert.ToBase64String(png)
                    : null;
                UrlCache[bitmapName] = url;
                return url;
            }
        }

        public static int EnsureMany(IEnumerable<string> names)
        {
            int n = 0;
            if (names == null) return 0;
            foreach (var name in names)
            {
                if (EnsurePngBytes(name) != null) n++;
            }
            return n;
        }

        public static Dictionary<string, string> DataUrls(IEnumerable<string> names)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (names == null) return map;
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || map.ContainsKey(name)) continue;
                var url = DataUrl(name);
                if (url != null) map[name] = url;
            }
            return map;
        }

        static byte[] EnsurePngBytes(string bitmapName)
        {
            var name = Sanitize(bitmapName);
            if (name == null) return null;

            lock (Gate)
            {
                try
                {
                    var archive = Archive();
                    if (archive == null) return null;
                    var bmp = archive.GetBmp(name);
                    if (bmp == null || bmp.Length < 32) return null;
                    return BmpToPng(bmp);
                }
                catch
                {
                    return null;
                }
            }
        }

        static BmaArchive Archive()
        {
            if (_archive != null || _archiveProbed) return _archive;
            _archiveProbed = true;
            var baseDir = Environment.GetEnvironmentVariable("UGII_BASE_DIR");
            if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
                baseDir = @"E:\NX2506";
            var dir = Path.Combine(baseDir, "UGII", "bitmaps");
            foreach (var file in new[]
            {
                Path.Combine(dir, "high_quality.2s.bma"),
                Path.Combine(dir, "darkstyle_high_quality.2s.bma"),
                Path.Combine(dir, "high_quality.sc.bma")
            })
            {
                if (!File.Exists(file)) continue;
                try
                {
                    _archive = BmaArchive.Load(file);
                    if (_archive != null && _archive.Count > 0)
                        return _archive;
                }
                catch
                {
                    _archive = null;
                }
            }
            return _archive;
        }

        static readonly string[] NameSuffixes =
        {
            ".2s.bmp", ".2l.bmp", ".sc.bmp", ".bmp", ".png"
        };

        static string Sanitize(string bitmapName)
        {
            if (string.IsNullOrWhiteSpace(bitmapName)) return null;
            var name = Path.GetFileName(bitmapName.Trim());
            foreach (var suffix in NameSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                    break;
                }
            }
            if (name.Length == 0) return null;
            return name;
        }

        static byte[] BmpToPng(byte[] bmpBytes)
        {
            var png = TryBmp32ToPng(bmpBytes);
            if (png != null) return png;

            using var ms = new MemoryStream(bmpBytes);
            using var src = Image.FromStream(ms, false, false);
            using var copy = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            copy.MakeTransparent(Color.FromArgb(255, 0, 255));
            copy.MakeTransparent(Color.Magenta);
            using var outMs = new MemoryStream();
            copy.Save(outMs, ImageFormat.Png);
            return outMs.ToArray();
        }

        static byte[] TryBmp32ToPng(byte[] bmp)
        {
            if (bmp == null || bmp.Length < 54) return null;
            if (bmp[0] != (byte)'B' || bmp[1] != (byte)'M') return null;
            int off = BitConverter.ToInt32(bmp, 10);
            int w = BitConverter.ToInt32(bmp, 18);
            int hRaw = BitConverter.ToInt32(bmp, 22);
            short bits = BitConverter.ToInt16(bmp, 28);
            int compression = BitConverter.ToInt32(bmp, 30);
            if (bits != 32 || compression != 0) return null;
            int h = Math.Abs(hRaw);
            if (w <= 0 || h <= 0 || w > 1024 || h > 1024) return null;
            int stride = w * 4;
            if (off < 54 || off + (long)stride * h > bmp.Length) return null;
            bool bottomUp = hRaw > 0;

            using var img = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = img.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    int srcY = bottomUp ? h - 1 - y : y;
                    Marshal.Copy(bmp, off + srcY * stride, IntPtr.Add(data.Scan0, y * data.Stride), stride);
                }
            }
            finally
            {
                img.UnlockBits(data);
            }

            FixIconAlpha(img);
            using var png = new MemoryStream();
            img.Save(png, ImageFormat.Png);
            return png.ToArray();
        }

        static void FixIconAlpha(Bitmap img)
        {
            var rect = new Rectangle(0, 0, img.Width, img.Height);
            var data = img.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                var buf = new byte[stride * img.Height];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                int maxA = 0;
                for (int y = 0; y < img.Height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < img.Width; x++)
                    {
                        int i = row + x * 4;
                        if (buf[i + 3] > maxA) maxA = buf[i + 3];
                    }
                }

                for (int y = 0; y < img.Height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < img.Width; x++)
                    {
                        int i = row + x * 4;
                        byte b = buf[i];
                        byte g = buf[i + 1];
                        byte r = buf[i + 2];
                        if (r == 255 && g == 0 && b == 255)
                        {
                            buf[i + 3] = 0;
                            continue;
                        }
                        if (maxA == 0)
                            buf[i + 3] = 255;
                    }
                }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                img.UnlockBits(data);
            }
        }

        sealed class BmaArchive
        {
            readonly byte[] _data;
            readonly Dictionary<string, (int Start, int End)> _index;
            readonly string _suffix;

            BmaArchive(byte[] data, Dictionary<string, (int, int)> index, string suffix)
            {
                _data = data;
                _index = index;
                _suffix = suffix;
            }

            public int Count => _index.Count;

            public static BmaArchive Load(string path)
            {
                var data = File.ReadAllBytes(path);
                if (data.Length < 32) return null;
                if (ReadBe(data, 0) != 0x12343210) return null;

                int count = (int)ReadBe(data, 8);
                int nameOff = (int)ReadBe(data, 12);
                int dataOff = (int)ReadBe(data, 16);
                if (count <= 0 || count > 200000) return null;
                if (nameOff < 20 || dataOff <= nameOff || dataOff >= data.Length) return null;

                var suffix = SuffixFromFile(path);
                var index = new Dictionary<string, (int Start, int End)>(count, StringComparer.OrdinalIgnoreCase);
                int prevName = nameOff;
                int prevData = dataOff;
                int rec = 20;
                for (int i = 0; i < count; i++, rec += 12)
                {
                    if (rec + 12 > nameOff) break;
                    int nameEnd = (int)ReadBe(data, rec + 4);
                    int dataEnd = (int)ReadBe(data, rec + 8);
                    if (nameEnd <= prevName || nameEnd > data.Length) break;
                    if (dataEnd < prevData || dataEnd > data.Length) break;

                    var rawName = Encoding.ASCII.GetString(data, prevName, nameEnd - prevName);
                    var key = StripSuffix(rawName, suffix);
                    if (!string.IsNullOrEmpty(key) && !index.ContainsKey(key))
                        index[key] = (prevData, dataEnd);

                    prevName = nameEnd;
                    prevData = dataEnd;
                }

                return new BmaArchive(data, index, suffix);
            }

            public byte[] GetBmp(string name)
            {
                if (string.IsNullOrEmpty(name)) return null;
                if (!_index.TryGetValue(name, out var span))
                {
                    var stripped = StripSuffix(name, _suffix);
                    if (stripped == null || !_index.TryGetValue(stripped, out span))
                        return null;
                }
                int len = span.End - span.Start;
                if (len <= 8) return null;
                var comp = new byte[len];
                Buffer.BlockCopy(_data, span.Start, comp, 0, len);
                return ZlibInflate(comp);
            }

            static string SuffixFromFile(string path)
            {
                var n = Path.GetFileNameWithoutExtension(path) ?? "";
                if (n.EndsWith(".2s", StringComparison.OrdinalIgnoreCase) || n.Contains("2s"))
                    return ".2s.bmp";
                if (n.EndsWith(".2l", StringComparison.OrdinalIgnoreCase) || n.Contains("2l"))
                    return ".2l.bmp";
                if (n.EndsWith(".sc", StringComparison.OrdinalIgnoreCase) || n.Contains(".sc") || n.EndsWith("sc"))
                    return ".sc.bmp";
                return ".2s.bmp";
            }

            static string StripSuffix(string raw, string suffix)
            {
                if (string.IsNullOrEmpty(raw)) return null;
                raw = raw.Trim();
                if (suffix != null && raw.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return raw.Substring(0, raw.Length - suffix.Length);
                if (raw.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                    return Path.GetFileNameWithoutExtension(raw);
                return raw;
            }

            static uint ReadBe(byte[] data, int offset)
            {
                return ((uint)data[offset] << 24)
                     | ((uint)data[offset + 1] << 16)
                     | ((uint)data[offset + 2] << 8)
                     | data[offset + 3];
            }

            static byte[] ZlibInflate(byte[] input)
            {
                if (input == null || input.Length < 2) return null;
                if (input[0] == (byte)'B' && input[1] == (byte)'M')
                    return input;

                try
                {
                    return Inflate(input, skipZlibWrapper: input[0] == 0x78);
                }
                catch
                {
                    try { return Inflate(input, skipZlibWrapper: false); }
                    catch { return null; }
                }
            }

            static byte[] Inflate(byte[] input, bool skipZlibWrapper)
            {
                int start = 0;
                int count = input.Length;
                if (skipZlibWrapper)
                {
                    start = 2;
                    count = input.Length - 6;
                    if (count <= 0) return null;
                }
                using var src = new MemoryStream(input, start, count);
                using var deflate = new DeflateStream(src, CompressionMode.Decompress);
                using var dst = new MemoryStream();
                deflate.CopyTo(dst);
                return dst.ToArray();
            }
        }
    }
}
