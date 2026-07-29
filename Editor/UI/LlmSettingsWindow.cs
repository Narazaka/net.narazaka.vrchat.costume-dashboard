using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class LlmSettingsWindow : EditorWindow
    {
        LlmSettings settings;

        public static void Open()
        {
            var window = GetWindow<LlmSettingsWindow>(true, "LLM設定");
            window.minSize = new Vector2(420, 180);
        }

        void OnEnable()
        {
            settings = LlmSettings.Load();
        }

        void OnGUI()
        {
            settings.Provider = (LlmProvider)EditorGUILayout.EnumPopup("プロバイダー", settings.Provider);
            settings.Endpoint = EditorGUILayout.TextField(new GUIContent("エンドポイント", "空なら既定URL"), settings.Endpoint);
            settings.Model = EditorGUILayout.TextField("モデル名", settings.Model);
            settings.ApiKey = EditorGUILayout.PasswordField("APIキー", settings.ApiKey);
            settings.MaxInputChars = EditorGUILayout.IntField(new GUIContent("入力上限(文字)", "超過時はlayer境界で分割リクエスト"), settings.MaxInputChars);
            EditorGUILayout.HelpBox("設定は EditorPrefs（マシン内・プロジェクト外）に保存されます。", MessageType.Info);
            if (GUILayout.Button("保存"))
            {
                settings.Save();
                Close();
            }
        }
    }
}
