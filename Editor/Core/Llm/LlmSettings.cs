using UnityEditor;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public enum LlmProvider { OpenAiCompatible, Anthropic, Cli }

    /// <summary>LLM API 設定。EditorPrefs 保存（プロジェクト外＝リポジトリに入らない・マシン内共有）</summary>
    public class LlmSettings
    {
        const string KeyPrefix = "CostumeDashboard.Llm.";
        // 一括送信が原則。JSONはほぼASCIIなので 600,000 文字 ≈ 150k-200k トークン級モデルの入力に収まる想定
        const int DefaultMaxInputChars = 600000;
        const int OldDefaultMaxInputChars = 200000;

        public LlmProvider Provider;
        /// <summary>空ならプロバイダー既定エンドポイント</summary>
        public string Endpoint = "";
        public string Model = "";
        public string ApiKey = "";
        /// <summary>Cli プロバイダーのコマンドライン（claude -p / codex exec 等）。プロンプトはstdinで渡される</summary>
        public string Command = "claude -p";
        /// <summary>1リクエストの入力サイズ上限（文字数）。超過時は layer 境界で分割</summary>
        public int MaxInputChars = DefaultMaxInputChars;

        /// <summary>キャッシュキーに使うモデル識別子（Cli はコマンドラインが実質モデル指定）</summary>
        public string CacheKeyModel => Provider == LlmProvider.Cli ? Command : Model;

        /// <summary>UI表示用の生成先ラベル</summary>
        public string DisplayTarget => Provider == LlmProvider.Cli ? Command : Model;

        /// <summary>実行に必要な設定が揃っているか</summary>
        public bool IsReady => Provider == LlmProvider.Cli
            ? !string.IsNullOrEmpty(Command)
            : !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(Model);

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
            var settings = new LlmSettings
            {
                Provider = (LlmProvider)EditorPrefs.GetInt(KeyPrefix + "Provider", 0),
                Endpoint = EditorPrefs.GetString(KeyPrefix + "Endpoint", ""),
                Model = EditorPrefs.GetString(KeyPrefix + "Model", ""),
                ApiKey = EditorPrefs.GetString(KeyPrefix + "ApiKey", ""),
                Command = EditorPrefs.GetString(KeyPrefix + "Command", "claude -p"),
                MaxInputChars = EditorPrefs.GetInt(KeyPrefix + "MaxInputChars", DefaultMaxInputChars),
            };
            // 旧既定値(200,000)のまま保存されている場合は新既定値へ移行（明示設定と区別できないが旧既定は配布直後のみ）
            if (settings.MaxInputChars == OldDefaultMaxInputChars) settings.MaxInputChars = DefaultMaxInputChars;
            return settings;
        }

        public void Save()
        {
            EditorPrefs.SetInt(KeyPrefix + "Provider", (int)Provider);
            EditorPrefs.SetString(KeyPrefix + "Endpoint", Endpoint ?? "");
            EditorPrefs.SetString(KeyPrefix + "Model", Model ?? "");
            EditorPrefs.SetString(KeyPrefix + "ApiKey", ApiKey ?? "");
            EditorPrefs.SetString(KeyPrefix + "Command", Command ?? "");
            EditorPrefs.SetInt(KeyPrefix + "MaxInputChars", MaxInputChars);
        }
    }
}
