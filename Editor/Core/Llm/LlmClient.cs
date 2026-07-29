using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class LlmRequest
    {
        public string Url;
        public List<(string Name, string Value)> Headers = new List<(string, string)>();
        public string Body;
    }

    /// <summary>OpenAI互換 chat completions / Anthropic messages の2形式に対応した最小HTTPクライアント</summary>
    public static class LlmClient
    {
        const int MaxTokens = 8192;

        public static LlmRequest BuildRequest(LlmSettings settings, string system, string user)
        {
            var request = new LlmRequest { Url = settings.EffectiveEndpoint };
            request.Headers.Add(("Content-Type", "application/json"));
            if (settings.Provider == LlmProvider.Anthropic)
            {
                request.Headers.Add(("x-api-key", settings.ApiKey));
                request.Headers.Add(("anthropic-version", "2023-06-01"));
                request.Body = new JObject
                {
                    ["model"] = settings.Model,
                    ["max_tokens"] = MaxTokens,
                    ["system"] = system,
                    ["messages"] = new JArray { new JObject { ["role"] = "user", ["content"] = user } },
                }.ToString();
            }
            else
            {
                request.Headers.Add(("Authorization", "Bearer " + settings.ApiKey));
                request.Body = new JObject
                {
                    ["model"] = settings.Model,
                    ["messages"] = new JArray
                    {
                        new JObject { ["role"] = "system", ["content"] = system },
                        new JObject { ["role"] = "user", ["content"] = user },
                    },
                }.ToString();
            }
            return request;
        }

        public static string ParseContent(LlmProvider provider, string responseJson)
        {
            try
            {
                var json = JObject.Parse(responseJson);
                if (provider == LlmProvider.Anthropic)
                {
                    if (!(json["content"] is JArray content)) return null;
                    var sb = new StringBuilder();
                    foreach (var block in content)
                    {
                        if ((string)block["type"] == "text") sb.Append((string)block["text"]);
                    }
                    return sb.Length > 0 ? sb.ToString() : null;
                }
                return (string)json.SelectToken("choices[0].message.content");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>非同期送信。completed コールバック（メインスレッド）で onDone(応答JSON生テキスト, error) を呼ぶ。
        /// 本文抽出は呼び出し側が ParseContent で行う</summary>
        public static void Send(LlmRequest request, Action<string, string> onDone)
        {
            var www = new UnityWebRequest(request.Url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 600,
            };
            foreach (var (name, value) in request.Headers) www.SetRequestHeader(name, value);
            var op = www.SendWebRequest();
            op.completed += _ =>
            {
                string content = null;
                string error = null;
                if (www.result != UnityWebRequest.Result.Success)
                {
                    error = $"{www.result}: {www.error} {www.downloadHandler.text}";
                }
                else
                {
                    content = www.downloadHandler.text;
                }
                www.Dispose();
                onDone(content, error);
            };
        }
    }
}
