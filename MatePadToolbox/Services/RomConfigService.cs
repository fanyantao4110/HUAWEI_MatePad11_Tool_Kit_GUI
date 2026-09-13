using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MatePadToolbox.Models;

namespace MatePadToolbox.Services
{
    /// <summary>
    /// 第三方系统下载配置服务：
    /// 从 Gitee Releases 下载 {版本号}.json 配置并解析出系统列表。
    /// 对应 bat 中的 :download_json / :update_json_files / :download_rom_list。
    /// </summary>
    public static class RomConfigService
    {
        /// <summary>JSON 配置文件下载基地址（与 bat 保持一致）。</summary>
        public const string JsonBaseUrl =
            "https://gitee.com/fanyantao4110/matepad11-system-config/releases/download/1/";

        /// <summary>支持的系统版本列表。</summary>
        public static readonly string[] Versions = { "2.0", "3.0", "4.0", "4.2", "4.3" };

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

        /// <summary>某个版本的 JSON 配置文件本地路径。</summary>
        public static string GetJsonPath(string version) => Path.Combine(AppConfig.SystemDir, $"{version}.json");

        /// <summary>
        /// 下载指定版本的 JSON 配置文件。
        /// </summary>
        public static async Task<bool> DownloadJsonAsync(string version, Action<string> log)
        {
            var url = JsonBaseUrl + $"{version}.json";
            var outPath = GetJsonPath(version);
            log($"正在下载 {version}.json ...");
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                Directory.CreateDirectory(AppConfig.SystemDir);
                await File.WriteAllBytesAsync(outPath, bytes);
                log($"[OK] {version}.json 下载成功。");
                return true;
            }
            catch (Exception ex)
            {
                log($"[错误] 下载失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 在线更新全部版本的 JSON 配置文件。
        /// </summary>
        public static async Task<bool> UpdateAllJsonAsync(Action<string> log)
        {
            log("正在更新全部配置文件（2.0 / 3.0 / 4.0 / 4.2 / 4.3）...");
            var allOk = true;
            foreach (var v in Versions)
            {
                if (!await DownloadJsonAsync(v, log)) allOk = false;
            }
            log(allOk ? "[OK] 全部配置文件更新成功！" : "[警告] 部分配置文件更新失败，可稍后重试或手动放入 system 目录。");
            return allOk;
        }

        /// <summary>
        /// 解析 JSON 配置为系统列表。
        /// 兼容两种格式：标准 JSON 数组，或每行一个 JSON 对象（与 bat 的按行解析行为一致）。
        /// </summary>
        public static List<RomEntry> ParseRomList(string jsonContent)
        {
            var list = new List<RomEntry>();

            // 尝试整体作为 JSON 数组解析
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var entry = ReadEntry(el);
                        if (entry != null) list.Add(entry);
                    }
                    return list;
                }
            }
            catch { /* 不是标准 JSON，退回按行解析 */ }

            // 按行解析：每行形如 {"name":"xxx","url":"https://..."}
            foreach (var rawLine in jsonContent.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("{")) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var entry = ReadEntry(doc.RootElement);
                    if (entry != null) list.Add(entry);
                }
                catch { /* 忽略无效行 */ }
            }
            return list;
        }

        private static RomEntry? ReadEntry(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            string name = "", url = "";
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var key = prop.Name.ToLowerInvariant();
                var val = prop.Value.GetString() ?? "";
                if (key == "name") name = val;
                else if (key == "url") url = val;
            }
            // bat 的逻辑是取前两个字段；这里做兜底：没有 name/url 键时取前两个字符串字段
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
            {
                var strings = el.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.String)
                    .Select(p => p.Value.GetString() ?? "")
                    .Take(2)
                    .ToList();
                if (strings.Count >= 2)
                {
                    name = strings[0];
                    url = strings[1];
                }
            }
            if (string.IsNullOrEmpty(name)) return null;
            return new RomEntry { Name = name, Url = url };
        }

        /// <summary>
        /// 加载指定版本的系统列表；本地缺失时自动尝试下载。
        /// </summary>
        public static async Task<List<RomEntry>> LoadRomListAsync(string version, Action<string> log)
        {
            var path = GetJsonPath(version);
            if (!File.Exists(path))
            {
                log($"[提示] 未找到 {version}.json，尝试从 Gitee 下载...");
                if (!await DownloadJsonAsync(version, log)) return new List<RomEntry>();
            }
            try
            {
                var content = await Task.Run(() => ReadTextAutoDetect(path));
                return ParseRomList(content);
            }
            catch (Exception ex)
            {
                log($"[错误] 解析 {version}.json 失败：{ex.Message}");
                return new List<RomEntry>();
            }
        }

        /// <summary>
        /// 自动识别编码读取文本文件：
        /// 优先识别 UTF-8 BOM，再尝试严格 UTF-8 解码，失败则按 ANSI(GBK) 解码。
        /// 这样既支持在线下载的 UTF-8 配置，也支持用户手工保存的 ANSI 配置。
        /// </summary>
        public static string ReadTextAutoDetect(string path)
        {
            var bytes = File.ReadAllBytes(path);

            // 1. UTF-8 BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            // 2. UTF-16 BOM（记事本另存为 Unicode 的情况）
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            // 3. 严格 UTF-8 解码：出现非法字节序列说明不是 UTF-8 → 按 ANSI(GBK) 处理
            try
            {
                var utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
                return utf8Strict.GetString(bytes);
            }
            catch (ArgumentException)
            {
                return ProcessRunner.Gbk.GetString(bytes);
            }
        }
    }
}
