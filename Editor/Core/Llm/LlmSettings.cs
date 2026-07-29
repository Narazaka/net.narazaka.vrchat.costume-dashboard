using UnityEditor;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public enum LlmProvider { OpenAiCompatible, Anthropic }

    /// <summary>LLM API 設定。EditorPrefs 保存（プロジェクト外＝リポジトリに入らない・マシン内共有）</summary>
    public class LlmSettings
    {
        const string KeyPrefix = "CostumeDashboard.Llm.";
        const int DefaultMaxInputChars = 200000;

        public LlmProvider Provider;
        /// <summary>空ならプロバイダー既定エンドポイント</summary>
        public string Endpoint = "";
        public string Model = "";
        public string ApiKey = "";
        /// <summary>1リクエストの入力サイズ上限（文字数）。超過時は layer 境界で分割</summary>
        public int MaxInputChars = DefaultMaxInputChars;

        public string EffectiveEndpoint
        {
            get
            {
                if (!string.IsNullOrEmpty(Endpoint)) return Endpoint;
                return Provider == LlmProvider.Anthropic
                    ? "https://api.anthropic.com/v1/messages"
                    : "https://api.openai.com/v1/chat/completions";
            }
        }

        public static LlmSettings Load()
        {
            return new LlmSettings
            {
                Provider = (LlmProvider)EditorPrefs.GetInt(KeyPrefix + "Provider", 0),
                Endpoint = EditorPrefs.GetString(KeyPrefix + "Endpoint", ""),
                Model = EditorPrefs.GetString(KeyPrefix + "Model", ""),
                ApiKey = EditorPrefs.GetString(KeyPrefix + "ApiKey", ""),
                MaxInputChars = EditorPrefs.GetInt(KeyPrefix + "MaxInputChars", DefaultMaxInputChars),
            };
        }

        public void Save()
        {
            EditorPrefs.SetInt(KeyPrefix + "Provider", (int)Provider);
            EditorPrefs.SetString(KeyPrefix + "Endpoint", Endpoint ?? "");
            EditorPrefs.SetString(KeyPrefix + "Model", Model ?? "");
            EditorPrefs.SetString(KeyPrefix + "ApiKey", ApiKey ?? "");
            EditorPrefs.SetInt(KeyPrefix + "MaxInputChars", MaxInputChars);
        }
    }
}
