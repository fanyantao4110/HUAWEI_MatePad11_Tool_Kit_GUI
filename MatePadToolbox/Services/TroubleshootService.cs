using MatePadToolbox.Models;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace MatePadToolbox.Services
{
    /// <summary>
    /// 疑难解答服务：从功能对应的本地 JSON 文件加载问答列表，
    /// 并提供下载/更新本地 JSON 文件的方法。
    /// </summary>
    public static class TroubleshootService
    {
        /// <summary>疑难解答文件存放目录。</summary>
        public static string FaqDir => Path.Combine(AppConfig.BaseDir, "faq");

        /// <summary>远程 JSON 基地址（与 RomConfigService 保持一致）。</summary>
        public const string JsonBaseUrl =
            "https://gitee.com/fanyantao4110/matepad11-system-config/releases/download/1/";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        /// <summary>获取指定功能对应的本地 JSON 文件路径。</summary>
        public static string GetJsonPath(string featureKey) => Path.Combine(FaqDir, $"{featureKey}.json");

        /// <summary>
        /// 加载指定功能的疑难解答列表。本地缺失或解析失败时返回空列表。
        /// </summary>
        public static async Task<List<TroubleshootEntry>> LoadAsync(string featureKey, Action<string> log)
        {
            var path = GetJsonPath(featureKey);
            if (!File.Exists(path))
            {
                log($"[提示] 未找到 {featureKey}.json，尝试从 Gitee 下载...");
                if (!await DownloadAsync(featureKey, log)) return new List<TroubleshootEntry>();
            }

            try
            {
                var content = await Task.Run(() => RomConfigService.ReadTextAutoDetect(path));
                return Parse(content);
            }
            catch (Exception ex)
            {
                log($"[错误] 解析 {featureKey}.json 失败：{ex.Message}");
                return new List<TroubleshootEntry>();
            }
        }

        /// <summary>
        /// 从 Gitee 下载指定功能的疑难解答 JSON 文件。
        /// </summary>
        public static async Task<bool> DownloadAsync(string featureKey, Action<string> log)
        {
            var url = JsonBaseUrl + $"{featureKey}.json";
            var outPath = GetJsonPath(featureKey);
            log($"正在下载 {featureKey}.json ...");
            try
            {
                Directory.CreateDirectory(FaqDir);
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                var bytes = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(outPath, bytes);
                log($"[OK] {featureKey}.json 下载成功。");
                return true;
            }
            catch (Exception ex)
            {
                log($"[错误] 下载失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 保存疑难解答列表到本地 JSON 文件。
        /// </summary>
        public static async Task SaveAsync(string featureKey, List<TroubleshootEntry> entries, Action<string>? log = null)
        {
            Directory.CreateDirectory(FaqDir);
            var path = GetJsonPath(featureKey);
            var json = JsonSerializer.Serialize(entries, JsonOptions);
            await File.WriteAllTextAsync(path, json);
            log?.Invoke($"[OK] 已保存 {featureKey}.json");
        }

        /// <summary>
        /// 解析疑难解答 JSON 内容。兼容标准 JSON 数组和按行 JSON 对象。
        /// </summary>
        public static List<TroubleshootEntry> Parse(string jsonContent)
        {
            var list = new List<TroubleshootEntry>();

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

        private static TroubleshootEntry? ReadEntry(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            string question = "", answer = "";
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var key = prop.Name.ToLowerInvariant();
                var val = prop.Value.GetString() ?? "";
                if (key == "question") question = val;
                else if (key == "answer") answer = val;
            }
            if (string.IsNullOrEmpty(question)) return null;
            return new TroubleshootEntry { Question = question, Answer = answer };
        }
    }
}
