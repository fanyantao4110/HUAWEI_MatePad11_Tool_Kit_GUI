using System.Text.Json.Serialization;

namespace MatePadToolbox.Models
{
    /// <summary>
    /// 疑难解答条目：每个功能对应一个 JSON 文件，字段为 question/answer。
    /// </summary>
    public class TroubleshootEntry
    {
        [JsonPropertyName("question")]
        public string Question { get; set; } = string.Empty;

        [JsonPropertyName("answer")]
        public string Answer { get; set; } = string.Empty;

        public override string ToString() => Question;
    }
}
