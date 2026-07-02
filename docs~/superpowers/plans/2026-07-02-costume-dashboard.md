# Costume Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** アバター内の複数衣装prefabのマテリアル状況を一覧し、AO Material Editor / Avatar Toggle Menu Creator / Change Render Queue を一括設定できる EditorWindow パッケージ `net.narazaka.vrchat.costume-dashboard` を作る。

**Architecture:** Editor専用VPMパッケージ。Core（走査・フェード枠判定・シェーダーカタログ・プリセット: 純ロジック）/ Setup（AO ME・Toggle Menu・ChangeRenderQueue への書き込み）/ UI（MultiColumnTreeView の EditorWindow）の3層。AO Material Editor は internal 型のためリフレクションでアクセスするソフト依存。

**Tech Stack:** Unity 2022.3 / UIElements (MultiColumnTreeView) / Unity Test Runner (EditMode) / VPM

**Spec:** `docs~/superpowers/specs/2026-07-02-costume-dashboard-design.md`（本リポジトリ内）

## Global Constraints

- パッケージ名: `net.narazaka.vrchat.costume-dashboard`、リポジトリ: `D:\make\devel\vrchat-AVATAR-SANDBOX\Packages\net.narazaka.vrchat.costume-dashboard`（git リポジトリ初期化済み・スペックコミット済み）
- Unity バージョン: `2022.3`（sandbox プロジェクト = `D:\make\devel\vrchat-AVATAR-SANDBOX`、2022.3.22f1）
- ライセンス: Zlib（他の net.narazaka.* パッケージと同様）
- asmdef 名: `Narazaka.VRChat.CostumeDashboard.Editor` / テスト: `Narazaka.VRChat.CostumeDashboard.Test`
- namespace: `Narazaka.VRChat.CostumeDashboard.Editor`（全ファイル共通・サブ namespace なし）
- Runtime アセンブリなし（Editor 専用）
- `aoyon.material-editor` はソフト依存（vpmDependencies に**含めない**。リフレクションアクセス、未導入時は機能無効化）
- vpmDependencies: `net.narazaka.vrchat.avatar-menu-creater-for-ma >=1.38.2`, `net.narazaka.vrchat.change-render-queue >=1.0.5`
- UI 文言は日本語直書き（初版はローカライズ機構なし）
- コミットメッセージ: 日本語1行のみ。Co-Authored-By 等の署名・装飾は付けない
- 書き込み操作は Undo 対応（`Undo.AddComponent` / `Undo.RegisterCreatedObjectUndo` / `Undo.RecordObject`）
- Material アセット・shader アセットは書き換えない（コンポーネント設定のみ）

## 検証環境の前提

- sandbox プロジェクト（`D:\make\devel\vrchat-AVATar-SANDBOX` ※実際のパスは `vrchat-AVATAR-SANDBOX`）を Unity 2022.3.22f1 で開いておく（aibridge が CLI を `AIBridgeCache/CLI/` に配置する）
- コンパイル検証: sandbox ルートで `./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity` → `./AIBridgeCache/CLI/AIBridgeCLI.exe get_logs --logType Error`
- テスト実行: `./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"` → 結果ファイル `Library/CostumeDashboardTestResults.json` を読む（Task 1 でこのハーネスを作る）
- sandbox には `jp.lilxyzw.liltoon` / `net.narazaka.vrchat.avatar-menu-creater-for-ma` / `net.narazaka.vrchat.change-render-queue` / `com.vrchat.avatars` 導入済み。`aoyon.material-editor` は**未導入**（Task 7 の実動テストをフルに走らせたい場合のみ、Prefab Stage を開いていない状態で `cp -r x:/make/devel/vrchat-cynthia-av3/Packages/aoyon.material-editor "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/"` で複製導入できる。未導入でも Task 7 のテストは Assume で skip され成立する）
- git 操作は `git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard"` で行う
- Unity が生成する `.meta` ファイルは各タスクのコミット時に一緒に `git add -A` で含める（コンパイル検証後にコミットするため .meta は生成済みのはず）

---

### Task 1: パッケージスキャフォールドとテストハーネス

**Files:**
- Create: `package.json`
- Create: `Editor/Narazaka.VRChat.CostumeDashboard.Editor.asmdef`
- Create: `Editor/UI/CostumeDashboardWindow.cs`（メニュー項目だけの空ウィンドウ）
- Create: `Test/Narazaka.VRChat.CostumeDashboard.Test.asmdef`
- Create: `Test/TestRunnerCli.cs`
- Create: `Test/SmokeTest.cs`

**Interfaces:**
- Consumes: なし
- Produces: メニュー `Tools/Costume Dashboard`（ウィンドウ）、`Tools/Costume Dashboard/Run Tests`（テスト実行→ `Library/CostumeDashboardTestResults.json` に `{"passed":N,"failed":N,"failures":[...]}` を書く）。以後の全タスクはこのテストハーネスで検証する。

- [ ] **Step 1: package.json を書く**

```json
{
  "name": "net.narazaka.vrchat.costume-dashboard",
  "version": "0.1.0",
  "displayName": "Costume Dashboard",
  "description": "衣装マテリアル状況の一覧と AO Material Editor / Avatar Toggle Menu Creator / Change Render Queue の一括設定",
  "author": {
    "name": "Narazaka",
    "url": "https://github.com/Narazaka"
  },
  "license": "Zlib",
  "type": "tool",
  "unity": "2022.3",
  "vpmDependencies": {
    "net.narazaka.vrchat.avatar-menu-creater-for-ma": ">=1.38.2",
    "net.narazaka.vrchat.change-render-queue": ">=1.0.5"
  }
}
```

- [ ] **Step 2: Editor asmdef を書く**

`Editor/Narazaka.VRChat.CostumeDashboard.Editor.asmdef`:

```json
{
    "name": "Narazaka.VRChat.CostumeDashboard.Editor",
    "rootNamespace": "",
    "references": [
        "VRC.SDKBase",
        "VRC.SDK3A",
        "AvatarMenuCreatorForMA.Core",
        "AvatarMenuCreatorForMA.Components",
        "AvatarMenuCreatorForMA.Collections",
        "Narazaka.VRChat.ChangeRenderQueue"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: 空ウィンドウを書く**

`Editor/UI/CostumeDashboardWindow.cs`:

```csharp
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class CostumeDashboardWindow : EditorWindow
    {
        [MenuItem("Tools/Costume Dashboard")]
        public static void Open()
        {
            GetWindow<CostumeDashboardWindow>("Costume Dashboard");
        }
    }
}
```

- [ ] **Step 4: Test asmdef を書く**

`Test/Narazaka.VRChat.CostumeDashboard.Test.asmdef`:

```json
{
    "name": "Narazaka.VRChat.CostumeDashboard.Test",
    "rootNamespace": "",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "Narazaka.VRChat.CostumeDashboard.Editor",
        "AvatarMenuCreatorForMA.Core",
        "AvatarMenuCreatorForMA.Components",
        "AvatarMenuCreatorForMA.Collections",
        "Narazaka.VRChat.ChangeRenderQueue",
        "VRC.SDKBase",
        "VRC.SDK3A"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 5: テスト実行ハーネスを書く**

`Test/TestRunnerCli.cs`（aibridge の `menu_item` から EditMode テストを走らせ、結果を JSON ファイルに書く）:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public static class TestRunnerCli
    {
        const string ResultPath = "Library/CostumeDashboardTestResults.json";

        [MenuItem("Tools/Costume Dashboard/Run Tests")]
        public static void RunAll()
        {
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new ResultWriter());
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "Narazaka.VRChat.CostumeDashboard.Test" },
            }));
        }

        class ResultWriter : ICallbacks
        {
            readonly List<string> failures = new List<string>();
            int passed;
            int failed;

            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren) return;
                if (result.TestStatus == TestStatus.Passed) passed++;
                else if (result.TestStatus == TestStatus.Failed)
                {
                    failed++;
                    failures.Add($"{result.FullName}: {result.Message}");
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var sb = new StringBuilder();
                sb.Append("{\"passed\":").Append(passed).Append(",\"failed\":").Append(failed).Append(",\"failures\":[");
                for (var i = 0; i < failures.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(failures[i].Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "")).Append('"');
                }
                sb.Append("]}");
                File.WriteAllText(ResultPath, sb.ToString());
                Debug.Log($"[CostumeDashboard] Tests finished: passed={passed} failed={failed}");
            }
        }
    }
}
```

- [ ] **Step 6: スモークテストを書く**

`Test/SmokeTest.cs`:

```csharp
using NUnit.Framework;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class SmokeTest
    {
        [Test]
        public void Smoke()
        {
            Assert.That(true, Is.True);
        }
    }
}
```

- [ ] **Step 7: コンパイルとテスト実行を検証する**

sandbox ルート（`D:/make/devel/vrchat-AVATAR-SANDBOX`）で:

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe get_logs --logType Error
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

数秒待って `D:/make/devel/vrchat-AVATAR-SANDBOX/Library/CostumeDashboardTestResults.json` を Read。
Expected: エラーログなし、`{"passed":1,"failed":0,"failures":[]}`

- [ ] **Step 8: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "パッケージスキャフォールドとテストハーネス"
```

---

### Task 2: ShaderCatalog（シェーダーGUID→family/variant/透過版マッピング）

**Files:**
- Create: `Editor/Core/ShaderCatalog.cs`
- Test: `Test/ShaderCatalogTest.cs`

**Interfaces:**
- Consumes: なし
- Produces:
  - `class ShaderFamilyInfo { string Family; string Variant; string TransparentGuid; bool IsKnown; bool NeedsShaderOverride; }`
  - `static ShaderFamilyInfo ShaderCatalog.Resolve(Shader shader)`（null / 未知 GUID → `Family="unknown", Variant="unknown"`）
  - `static ShaderFamilyInfo ShaderCatalog.ResolveByGuid(string guid)`
  - family 値: `lilToon_std` / `lilToon_tess` / `lilToon_lite` / `lilToon_multi` / `motchiri_std` / `motchiri_tess` / `unknown`
  - variant 値: `opaque[_o]` / `cutout[_o]` / `trans[_o]` / `onetrans[_o]` / `twotrans[_o]` / `multi[_o]` / `unknown`

- [ ] **Step 1: 失敗するテストを書く**

`Test/ShaderCatalogTest.cs`:

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class ShaderCatalogTest
    {
        const string LtsGuid = "df12117ecd77c31469c224178886498e";
        const string LtsCutoutOGuid = "3b4aa19949601f046a20ca8bdaee929f";
        const string LtsTransOGuid = "3c79b10c7e0b2784aaa4c2f8dd17d55e";
        const string LtsMultiGuid = "9294844b15dca184d914a632279b24e1";

        [Test]
        public void ResolveByGuid_Lts()
        {
            var info = ShaderCatalog.ResolveByGuid(LtsGuid);
            Assert.That(info.Family, Is.EqualTo("lilToon_std"));
            Assert.That(info.Variant, Is.EqualTo("opaque"));
            Assert.That(info.TransparentGuid, Is.EqualTo("165365ab7100a044ca85fc8c33548a62"));
            Assert.That(info.NeedsShaderOverride, Is.True);
            Assert.That(info.IsKnown, Is.True);
        }

        [Test]
        public void ResolveByGuid_CutoutO_MapsToTransO()
        {
            var info = ShaderCatalog.ResolveByGuid(LtsCutoutOGuid);
            Assert.That(info.Variant, Is.EqualTo("cutout_o"));
            Assert.That(info.TransparentGuid, Is.EqualTo(LtsTransOGuid));
        }

        [Test]
        public void ResolveByGuid_TransO_NoOverride()
        {
            var info = ShaderCatalog.ResolveByGuid(LtsTransOGuid);
            Assert.That(info.Variant, Is.EqualTo("trans_o"));
            Assert.That(info.TransparentGuid, Is.Null);
            Assert.That(info.NeedsShaderOverride, Is.False);
        }

        [Test]
        public void ResolveByGuid_Multi()
        {
            var info = ShaderCatalog.ResolveByGuid(LtsMultiGuid);
            Assert.That(info.Family, Is.EqualTo("lilToon_multi"));
            Assert.That(info.Variant, Is.EqualTo("multi"));
        }

        [Test]
        public void ResolveByGuid_Unknown()
        {
            var info = ShaderCatalog.ResolveByGuid("0000000000000000000000000000dead");
            Assert.That(info.IsKnown, Is.False);
            Assert.That(info.Family, Is.EqualTo("unknown"));
        }

        [Test]
        public void Resolve_NullShader_Unknown()
        {
            Assert.That(ShaderCatalog.Resolve(null).IsKnown, Is.False);
        }

        [Test]
        public void Resolve_ActualLtsShaderAsset()
        {
            // sandbox には lilToon が導入されている前提
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid));
            Assert.That(shader, Is.Not.Null, "lilToon (lts.shader) が見つからない");
            var info = ShaderCatalog.Resolve(shader);
            Assert.That(info.Family, Is.EqualTo("lilToon_std"));
            Assert.That(info.Variant, Is.EqualTo("opaque"));
        }
    }
}
```

- [ ] **Step 2: テストが失敗する（コンパイルエラーになる）ことを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
```

Expected: `ShaderCatalog` 未定義のコンパイルエラー

- [ ] **Step 3: 実装を書く**

`Editor/Core/ShaderCatalog.cs`（GUID 表は `claude-vrchat-avatar-skills/skills/vrchat-avatar-agent-tools-commands/scripts/GroupRendererSlotsByShader/Program.cs` の shaderMap の完全移植）:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class ShaderFamilyInfo
    {
        public string Family;
        public string Variant;
        /// <summary>不透明/cutout の透過版シェーダー GUID。null = 変更不要（既に透過系）または不明</summary>
        public string TransparentGuid;
        public bool IsKnown => Family != "unknown";
        public bool NeedsShaderOverride => TransparentGuid != null;
    }

    public static class ShaderCatalog
    {
        static readonly ShaderFamilyInfo UnknownInfo = new ShaderFamilyInfo { Family = "unknown", Variant = "unknown" };

        static ShaderFamilyInfo E(string family, string variant, string transparentGuid = null) =>
            new ShaderFamilyInfo { Family = family, Variant = variant, TransparentGuid = transparentGuid };

        static readonly Dictionary<string, ShaderFamilyInfo> Map = new Dictionary<string, ShaderFamilyInfo>(System.StringComparer.OrdinalIgnoreCase)
        {
            // lilToon 標準
            ["df12117ecd77c31469c224178886498e"] = E("lilToon_std", "opaque",     "165365ab7100a044ca85fc8c33548a62"),
            ["efa77a80ca0344749b4f19fdd5891cbe"] = E("lilToon_std", "opaque_o",   "3c79b10c7e0b2784aaa4c2f8dd17d55e"),
            ["85d6126cae43b6847aff4b13f4adb8ec"] = E("lilToon_std", "cutout",     "165365ab7100a044ca85fc8c33548a62"),
            ["3b4aa19949601f046a20ca8bdaee929f"] = E("lilToon_std", "cutout_o",   "3c79b10c7e0b2784aaa4c2f8dd17d55e"),
            ["165365ab7100a044ca85fc8c33548a62"] = E("lilToon_std", "trans"),
            ["3c79b10c7e0b2784aaa4c2f8dd17d55e"] = E("lilToon_std", "trans_o"),
            ["b269573b9937b8340b3e9e191a3ba5a8"] = E("lilToon_std", "onetrans"),
            ["7171688840c632447b22ec14e2bdef7e"] = E("lilToon_std", "onetrans_o"),
            ["6a77405f7dfdc1447af58854c7f43f39"] = E("lilToon_std", "twotrans"),
            ["9cf054060007d784394b8b0bb703e441"] = E("lilToon_std", "twotrans_o"),
            // lilToon テッセレーション
            ["3eef4aee6ba0de047b0d40409ea2891c"] = E("lilToon_tess", "opaque",     "afa1a194f5a2fd243bda3a17bca1b36e"),
            ["c6d605ee23b18fc46903f38c67db701f"] = E("lilToon_tess", "opaque_o",   "9b0c2630b12933248922527d4507cfa9"),
            ["bbfffd5515b843c41a85067191cbf687"] = E("lilToon_tess", "cutout",     "afa1a194f5a2fd243bda3a17bca1b36e"),
            ["5ba517885727277409feada18effa4a6"] = E("lilToon_tess", "cutout_o",   "9b0c2630b12933248922527d4507cfa9"),
            ["afa1a194f5a2fd243bda3a17bca1b36e"] = E("lilToon_tess", "trans"),
            ["9b0c2630b12933248922527d4507cfa9"] = E("lilToon_tess", "trans_o"),
            ["90f83c35b0769a748abba5d0880f36d5"] = E("lilToon_tess", "onetrans"),
            ["67ed0252d63362a4ab23707a720508b7"] = E("lilToon_tess", "onetrans_o"),
            ["7e398ea50f9b70045b1774e05b46a39f"] = E("lilToon_tess", "twotrans"),
            ["7e61dbad981ad4f43a03722155db1c6a"] = E("lilToon_tess", "twotrans_o"),
            // lilToon Lite
            ["381af8ba8e1740a41b9768ccfb0416c2"] = E("lilToon_lite", "opaque",     "0e3ece1bd59542743bccadb21f68318e"),
            ["583a88005abb81a4ebbce757b4851a0d"] = E("lilToon_lite", "opaque_o",   "1c12a37046f07ac4486881deaf0187ea"),
            ["b957dce3d03ff5445ac989f8de643c7f"] = E("lilToon_lite", "cutout",     "0e3ece1bd59542743bccadb21f68318e"),
            ["8cf5267d397b04846856f6d3d9561da0"] = E("lilToon_lite", "cutout_o",   "1c12a37046f07ac4486881deaf0187ea"),
            ["0e3ece1bd59542743bccadb21f68318e"] = E("lilToon_lite", "trans"),
            ["1c12a37046f07ac4486881deaf0187ea"] = E("lilToon_lite", "trans_o"),
            // もっちりシェーダー std
            ["8433e8048ed58354e9fb6624442f504f"] = E("motchiri_std", "opaque",     "2db6a99b3d46dba4bbf40a992528822e"),
            ["3274f6b718410034b8ebef59e2c8daa6"] = E("motchiri_std", "opaque_o",   "99989615c3866f74e9176dce204c0f57"),
            ["4ec130a0da49df8488f4b374526c6708"] = E("motchiri_std", "cutout",     "2db6a99b3d46dba4bbf40a992528822e"),
            ["92d89b91cc2624548a7af9291dccc28e"] = E("motchiri_std", "cutout_o",   "99989615c3866f74e9176dce204c0f57"),
            ["2db6a99b3d46dba4bbf40a992528822e"] = E("motchiri_std", "trans"),
            ["99989615c3866f74e9176dce204c0f57"] = E("motchiri_std", "trans_o"),
            ["574c858ca04bcda41b4c39b66bfa006a"] = E("motchiri_std", "onetrans"),
            ["0030998926684054d9c49159756f1cc4"] = E("motchiri_std", "onetrans_o"),
            ["32beaf088a4b8884ca0a0834ff7e1b32"] = E("motchiri_std", "twotrans"),
            ["7b58f46254eb8a84eb286257420f2f8a"] = E("motchiri_std", "twotrans_o"),
            // もっちりシェーダー tess
            ["3f4730d5aac1a3541904d05394299634"] = E("motchiri_tess", "opaque",     "90291565ebae0eb47b2fce5844ad5c83"),
            ["4273468959aff3d46b7da8861ab81fdc"] = E("motchiri_tess", "opaque_o",   "37a31dfbf395e77439136efa51361908"),
            ["6a81cc02aa3a54b4db87ac5fdbb494ac"] = E("motchiri_tess", "cutout",     "90291565ebae0eb47b2fce5844ad5c83"),
            ["620438ee028caab4dbb23544b0e0709b"] = E("motchiri_tess", "cutout_o",   "37a31dfbf395e77439136efa51361908"),
            ["90291565ebae0eb47b2fce5844ad5c83"] = E("motchiri_tess", "trans"),
            ["37a31dfbf395e77439136efa51361908"] = E("motchiri_tess", "trans_o"),
            ["c5574ba4c0ff3a24d97ad550ad25b338"] = E("motchiri_tess", "onetrans"),
            ["a7309075e672a5546930b75bfdbce7f9"] = E("motchiri_tess", "onetrans_o"),
            ["e1e4f3a6e2d532547877ed21bc09a222"] = E("motchiri_tess", "twotrans"),
            ["d4020e6a7e47b8648a91e6fbd88bcfa6"] = E("motchiri_tess", "twotrans_o"),
            // lilToon Multi: 単一 shader、_TransparentMode プロパティ駆動。透過専用 shader なし
            ["9294844b15dca184d914a632279b24e1"] = E("lilToon_multi", "multi"),
            ["51b2dee0ab07bd84d8147601ff89e511"] = E("lilToon_multi", "multi_o"),
        };

        public static ShaderFamilyInfo ResolveByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return UnknownInfo;
            return Map.TryGetValue(guid, out var e) ? e : UnknownInfo;
        }

        public static ShaderFamilyInfo Resolve(Shader shader)
        {
            if (shader == null) return UnknownInfo;
            var path = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(path)) return UnknownInfo;
            return ResolveByGuid(AssetDatabase.AssetPathToGUID(path));
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` を Read。Expected: `"failed":0`

- [ ] **Step 5: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "ShaderCatalog: シェーダーGUID→family/variant/透過版マッピング"
```

---

### Task 3: AvatarUtil（アバタールート検出・相対パス）

**Files:**
- Create: `Editor/Core/AvatarUtil.cs`
- Test: `Test/AvatarUtilTest.cs`

**Interfaces:**
- Consumes: なし
- Produces:
  - `static GameObject AvatarUtil.FindAvatarRoot(GameObject any)` — 自身を含め親方向に `VRCAvatarDescriptor` を探す。なければ null
  - `static string AvatarUtil.RelativePath(GameObject root, GameObject target)` — root からの `/` 区切り相対パス。root==target なら `""`。target が root 配下でなければ null

- [ ] **Step 1: 失敗するテストを書く**

`Test/AvatarUtilTest.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class AvatarUtilTest
    {
        GameObject avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = new GameObject("Avatar");
            avatar.AddComponent<VRCAvatarDescriptor>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar);
        }

        GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            return go;
        }

        [Test]
        public void FindAvatarRoot_FromDescendant()
        {
            var costume = Child(avatar, "Costume");
            var mesh = Child(costume, "Mesh");
            Assert.That(AvatarUtil.FindAvatarRoot(mesh), Is.EqualTo(avatar));
        }

        [Test]
        public void FindAvatarRoot_Self()
        {
            Assert.That(AvatarUtil.FindAvatarRoot(avatar), Is.EqualTo(avatar));
        }

        [Test]
        public void FindAvatarRoot_NotFound()
        {
            var orphan = new GameObject("Orphan");
            try
            {
                Assert.That(AvatarUtil.FindAvatarRoot(orphan), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(orphan);
            }
        }

        [Test]
        public void RelativePath_Nested()
        {
            var costume = Child(avatar, "Costume");
            var mesh = Child(costume, "Mesh");
            Assert.That(AvatarUtil.RelativePath(avatar, mesh), Is.EqualTo("Costume/Mesh"));
        }

        [Test]
        public void RelativePath_Same()
        {
            Assert.That(AvatarUtil.RelativePath(avatar, avatar), Is.EqualTo(""));
        }

        [Test]
        public void RelativePath_Outside_Null()
        {
            var orphan = new GameObject("Orphan");
            try
            {
                Assert.That(AvatarUtil.RelativePath(avatar, orphan), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(orphan);
            }
        }
    }
}
```

- [ ] **Step 2: コンパイルエラー（AvatarUtil 未定義）を確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
```

- [ ] **Step 3: 実装を書く**

`Editor/Core/AvatarUtil.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public static class AvatarUtil
    {
        public static GameObject FindAvatarRoot(GameObject any)
        {
            if (any == null) return null;
            for (var t = any.transform; t != null; t = t.parent)
            {
                if (t.GetComponent<VRCAvatarDescriptor>() != null) return t.gameObject;
            }
            return null;
        }

        public static string RelativePath(GameObject root, GameObject target)
        {
            if (root == null || target == null) return null;
            if (root == target) return "";
            var names = new List<string>();
            for (var t = target.transform; t != null; t = t.parent)
            {
                if (t.gameObject == root) 
                {
                    names.Reverse();
                    return string.Join("/", names);
                }
                names.Add(t.name);
            }
            return null;
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` → `"failed":0`

- [ ] **Step 5: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "AvatarUtil: アバタールート検出と相対パス計算"
```

---

### Task 4: FadeCompatChecker（2nd/3rd/AlphaMask 駆動枠の空き判定）

**Files:**
- Create: `Editor/Core/FadeCompatChecker.cs`
- Test: `Test/FadeCompatCheckerTest.cs`

**Interfaces:**
- Consumes: なし
- Produces:
  - `enum FadeFrame { Third, Second, AlphaMask }`
  - `class NonDefaultProp { string Name; string Current; string Default; }`
  - `class FadeFrameState { bool Compatible; List<NonDefaultProp> NonDefaultProps; }`
  - `class FadeCompatResult { FadeFrameState Third; FadeFrameState Second; FadeFrameState AlphaMask; FadeFrame? Recommended; }`（Recommended は 3rd > 2nd > AlphaMask 優先、全滅なら null）
  - `static FadeCompatResult FadeCompatChecker.Check(Material material)`

判定基準（`CheckMaterialFadeCompat/Program.cs` の移植）: 各枠に属するプロパティ群が、マテリアル上で公開されている（`HasProperty`）ものすべて lts.shader デフォルト値と一致していれば `Compatible`。Texture 型は「テクスチャ未割り当て かつ offset=(0,0) かつ scale=(1,1)」でデフォルト扱い。数値比較は誤差 1e-5。

- [ ] **Step 1: 失敗するテストを書く**

`Test/FadeCompatCheckerTest.cs`:

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class FadeCompatCheckerTest
    {
        const string LtsGuid = "df12117ecd77c31469c224178886498e";
        Material mat;

        [SetUp]
        public void SetUp()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid));
            Assert.That(shader, Is.Not.Null, "lilToon (lts.shader) が見つからない");
            mat = new Material(shader);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(mat);
        }

        [Test]
        public void Default_AllCompatible_RecommendThird()
        {
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Compatible, Is.True);
            Assert.That(result.Second.Compatible, Is.True);
            Assert.That(result.AlphaMask.Compatible, Is.True);
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Third));
        }

        [Test]
        public void ThirdUsed_RecommendSecond()
        {
            mat.SetFloat("_UseMain3rdTex", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Third.Compatible, Is.False);
            Assert.That(result.Third.NonDefaultProps, Has.Some.Matches<NonDefaultProp>(p => p.Name == "_UseMain3rdTex"));
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Second));
        }

        [Test]
        public void ThirdAndSecondUsed_RecommendAlphaMask()
        {
            mat.SetFloat("_UseMain3rdTex", 1);
            mat.SetColor("_Color2nd", new Color(1, 0, 0, 1));
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Recommended, Is.EqualTo(FadeFrame.AlphaMask));
        }

        [Test]
        public void AllUsed_RecommendNull()
        {
            mat.SetFloat("_UseMain3rdTex", 1);
            mat.SetFloat("_UseMain2ndTex", 1);
            mat.SetFloat("_AlphaMaskMode", 1);
            var result = FadeCompatChecker.Check(mat);
            Assert.That(result.Recommended, Is.Null);
        }

        [Test]
        public void TextureAssigned_Incompatible()
        {
            var tex = new Texture2D(4, 4);
            try
            {
                mat.SetTexture("_Main3rdTex", tex);
                var result = FadeCompatChecker.Check(mat);
                Assert.That(result.Third.Compatible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void NonLilToonMaterial_AllCompatible()
        {
            // 判定対象プロパティを持たないマテリアルは全枠 compatible（HasProperty=false は対象外）
            var std = new Material(Shader.Find("Standard"));
            try
            {
                var result = FadeCompatChecker.Check(std);
                Assert.That(result.Third.Compatible, Is.True);
                Assert.That(result.Recommended, Is.EqualTo(FadeFrame.Third));
            }
            finally
            {
                Object.DestroyImmediate(std);
            }
        }
    }
}
```

- [ ] **Step 2: コンパイルエラーを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
```

- [ ] **Step 3: 実装を書く**

`Editor/Core/FadeCompatChecker.cs`（プロパティ表は `CheckMaterialFadeCompat/Program.cs` の完全移植）:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public enum FadeFrame
    {
        Third,
        Second,
        AlphaMask,
    }

    public class NonDefaultProp
    {
        public string Name;
        public string Current;
        public string Default;
    }

    public class FadeFrameState
    {
        public bool Compatible;
        public List<NonDefaultProp> NonDefaultProps = new List<NonDefaultProp>();
    }

    public class FadeCompatResult
    {
        public FadeFrameState Third;
        public FadeFrameState Second;
        public FadeFrameState AlphaMask;
        public FadeFrame? Recommended;
    }

    public static class FadeCompatChecker
    {
        enum Kind { Number, Color, Vector, Texture }

        class PropDef
        {
            public string Name;
            public Kind Kind;
            public float Num;
            public Vector4 Vec;
        }

        static PropDef N(string name, float v) => new PropDef { Name = name, Kind = Kind.Number, Num = v };
        static PropDef C(string name, float r, float g, float b, float a) => new PropDef { Name = name, Kind = Kind.Color, Vec = new Vector4(r, g, b, a) };
        static PropDef V(string name, float x, float y, float z, float w) => new PropDef { Name = name, Kind = Kind.Vector, Vec = new Vector4(x, y, z, w) };
        static PropDef T(string name) => new PropDef { Name = name, Kind = Kind.Texture };

        static List<PropDef> MainTexProps(string n) => new List<PropDef>
        {
            N($"_UseMain{n}Tex", 0),
            C($"_Color{n}", 1, 1, 1, 1),
            T($"_Main{n}Tex"),
            N($"_Main{n}TexAngle", 0),
            V($"_Main{n}Tex_ScrollRotate", 0, 0, 0, 0),
            N($"_Main{n}Tex_UVMode", 0),
            N($"_Main{n}Tex_Cull", 0),
            V($"_Main{n}TexDecalAnimation", 1, 1, 1, 30),
            V($"_Main{n}TexDecalSubParam", 1, 1, 0, 1),
            N($"_Main{n}TexIsDecal", 0),
            N($"_Main{n}TexIsLeftOnly", 0),
            N($"_Main{n}TexIsRightOnly", 0),
            N($"_Main{n}TexShouldCopy", 0),
            N($"_Main{n}TexShouldFlipMirror", 0),
            N($"_Main{n}TexShouldFlipCopy", 0),
            N($"_Main{n}TexIsMSDF", 0),
            T($"_Main{n}BlendMask"),
            N($"_Main{n}TexBlendMode", 0),
            N($"_Main{n}TexAlphaMode", 0),
            N($"_Main{n}EnableLighting", 1),
            T($"_Main{n}DissolveMask"),
            T($"_Main{n}DissolveNoiseMask"),
            V($"_Main{n}DissolveNoiseMask_ScrollRotate", 0, 0, 0, 0),
            N($"_Main{n}DissolveNoiseStrength", 0.1f),
            C($"_Main{n}DissolveColor", 1, 1, 1, 1),
            V($"_Main{n}DissolveParams", 0, 0, 0.5f, 0.1f),
            V($"_Main{n}DissolvePos", 0, 0, 0, 0),
            V($"_Main{n}DistanceFade", 0.1f, 0.01f, 0, 0),
        };

        static readonly List<PropDef> ThirdProps = MainTexProps("3rd");
        static readonly List<PropDef> SecondProps = MainTexProps("2nd");
        static readonly List<PropDef> AlphaMaskProps = new List<PropDef>
        {
            N("_AlphaMaskMode", 0),
            T("_AlphaMask"),
            N("_AlphaMaskScale", 1),
            N("_AlphaMaskValue", 0),
        };

        public static FadeCompatResult Check(Material material)
        {
            var third = CheckFrame(material, ThirdProps);
            var second = CheckFrame(material, SecondProps);
            var alphaMask = CheckFrame(material, AlphaMaskProps);
            FadeFrame? recommended = null;
            if (third.Compatible) recommended = FadeFrame.Third;
            else if (second.Compatible) recommended = FadeFrame.Second;
            else if (alphaMask.Compatible) recommended = FadeFrame.AlphaMask;
            return new FadeCompatResult { Third = third, Second = second, AlphaMask = alphaMask, Recommended = recommended };
        }

        static FadeFrameState CheckFrame(Material m, List<PropDef> defs)
        {
            var state = new FadeFrameState();
            foreach (var def in defs)
            {
                if (!m.HasProperty(def.Name)) continue;
                if (IsDefault(m, def)) continue;
                state.NonDefaultProps.Add(new NonDefaultProp
                {
                    Name = def.Name,
                    Current = CurrentString(m, def),
                    Default = DefaultString(def),
                });
            }
            state.Compatible = state.NonDefaultProps.Count == 0;
            return state;
        }

        static bool IsDefault(Material m, PropDef def)
        {
            switch (def.Kind)
            {
                case Kind.Number:
                    return Approx(m.GetFloat(def.Name), def.Num);
                case Kind.Color:
                    return Approx(m.GetColor(def.Name), (Color)def.Vec);
                case Kind.Vector:
                    return Approx(m.GetVector(def.Name), def.Vec);
                case Kind.Texture:
                    return m.GetTexture(def.Name) == null
                        && Approx(m.GetTextureOffset(def.Name), Vector2.zero)
                        && Approx(m.GetTextureScale(def.Name), Vector2.one);
            }
            return false;
        }

        static bool Approx(float a, float b) => Mathf.Abs(a - b) < 1e-5f;
        static bool Approx(Vector2 a, Vector2 b) => Approx(a.x, b.x) && Approx(a.y, b.y);
        static bool Approx(Vector4 a, Vector4 b) => Approx(a.x, b.x) && Approx(a.y, b.y) && Approx(a.z, b.z) && Approx(a.w, b.w);
        static bool Approx(Color a, Color b) => Approx(a.r, b.r) && Approx(a.g, b.g) && Approx(a.b, b.b) && Approx(a.a, b.a);

        static string CurrentString(Material m, PropDef def)
        {
            switch (def.Kind)
            {
                case Kind.Number: return m.GetFloat(def.Name).ToString();
                case Kind.Color: return m.GetColor(def.Name).ToString();
                case Kind.Vector: return m.GetVector(def.Name).ToString();
                case Kind.Texture:
                    var tex = m.GetTexture(def.Name);
                    return $"tex={(tex == null ? "null" : tex.name)} offset={m.GetTextureOffset(def.Name)} scale={m.GetTextureScale(def.Name)}";
            }
            return "";
        }

        static string DefaultString(PropDef def)
        {
            switch (def.Kind)
            {
                case Kind.Number: return def.Num.ToString();
                case Kind.Color: return ((Color)def.Vec).ToString();
                case Kind.Vector: return def.Vec.ToString();
                case Kind.Texture: return "tex=null offset=(0.0, 0.0) scale=(1.0, 1.0)";
            }
            return "";
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` → `"failed":0`

- [ ] **Step 5: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "FadeCompatChecker: 2nd/3rd/AlphaMask フェード駆動枠の空き判定"
```

---

### Task 5: MaterialSlotScanner（走査とシェーダーグルーピング）

**Files:**
- Create: `Editor/Core/MaterialSlotScanner.cs`
- Test: `Test/MaterialSlotScannerTest.cs`

**Interfaces:**
- Consumes: `ShaderCatalog.Resolve`, `FadeCompatChecker.Check`, `FadeFrame`, `FadeCompatResult`, `ShaderFamilyInfo`
- Produces:
  - `class SlotInfo { Renderer Renderer; int SlotIndex; Material Material; ShaderFamilyInfo Family; int MultiTransparentMode; FadeCompatResult FadeCompat; }`
    - `MultiTransparentMode`: family が `lilToon_multi` のとき `_TransparentMode` の値（int化）。それ以外 `-1`
    - `FadeCompat`: `Family.IsKnown` のときのみ非 null
  - `class SlotGroup { string Family; string Variant; string ShaderGuid; FadeFrame? Preset; List<SlotInfo> Slots; bool NeedsShaderOverride; string TransparentGuid; bool CanSetupFade; string FadeDisabledReason; }`
  - `static List<SlotInfo> MaterialSlotScanner.Scan(GameObject costumeRoot)` — 配下の全 Renderer（非アクティブ含む）× 全スロット
  - `static List<SlotGroup> MaterialSlotScanner.GroupByShader(IEnumerable<SlotInfo> slots)` — グループキー = (Family, Variant, ShaderGuid, Recommended)。CanSetupFade 判定を含む

CanSetupFade の判定:
- family `unknown` → false（理由 "未知のシェーダー"）
- Material null / shader null → false（理由 "マテリアル未設定"）
- family `lilToon_multi` で `MultiTransparentMode` が 3〜6 → false（理由 "_TransparentMode が Refraction/Fur/Gem 系"）
- `FadeCompat.Recommended == null` → false（理由 "3rd/2nd/AlphaMask 全枠使用済み"）
- それ以外 → true

- [ ] **Step 1: 失敗するテストを書く**

`Test/MaterialSlotScannerTest.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class MaterialSlotScannerTest
    {
        const string LtsGuid = "df12117ecd77c31469c224178886498e";
        const string LtsCutoutOGuid = "3b4aa19949601f046a20ca8bdaee929f";

        GameObject root;
        Material ltsMat;
        Material cutoutOMat;
        Material unknownMat;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Costume");
            ltsMat = new Material(AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsGuid)));
            cutoutOMat = new Material(AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(LtsCutoutOGuid)));
            unknownMat = new Material(Shader.Find("Standard"));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(ltsMat);
            Object.DestroyImmediate(cutoutOMat);
            Object.DestroyImmediate(unknownMat);
        }

        GameObject AddMesh(string name, params Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMaterials = mats;
            return go;
        }

        [Test]
        public void Scan_CollectsAllSlots()
        {
            AddMesh("Top", ltsMat, unknownMat);
            AddMesh("Skirt", cutoutOMat);
            var slots = MaterialSlotScanner.Scan(root);
            Assert.That(slots.Count, Is.EqualTo(3));
            var top0 = slots.First(s => s.Renderer.name == "Top" && s.SlotIndex == 0);
            Assert.That(top0.Family.Family, Is.EqualTo("lilToon_std"));
            Assert.That(top0.Family.Variant, Is.EqualTo("opaque"));
            Assert.That(top0.FadeCompat, Is.Not.Null);
            var top1 = slots.First(s => s.Renderer.name == "Top" && s.SlotIndex == 1);
            Assert.That(top1.Family.IsKnown, Is.False);
            Assert.That(top1.FadeCompat, Is.Null);
        }

        [Test]
        public void Scan_IncludesInactive()
        {
            var mesh = AddMesh("Hidden", ltsMat);
            mesh.SetActive(false);
            Assert.That(MaterialSlotScanner.Scan(root).Count, Is.EqualTo(1));
        }

        [Test]
        public void GroupByShader_GroupsByFamilyVariant()
        {
            AddMesh("Top", ltsMat);
            AddMesh("Skirt", ltsMat);
            AddMesh("Ribbon", cutoutOMat);
            var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
            Assert.That(groups.Count, Is.EqualTo(2));
            var opaqueGroup = groups.First(g => g.Variant == "opaque");
            Assert.That(opaqueGroup.Slots.Count, Is.EqualTo(2));
            Assert.That(opaqueGroup.NeedsShaderOverride, Is.True);
            Assert.That(opaqueGroup.Preset, Is.EqualTo(FadeFrame.Third));
            Assert.That(opaqueGroup.CanSetupFade, Is.True);
        }

        [Test]
        public void GroupByShader_SplitsByRecommendedPreset()
        {
            var thirdUsed = new Material(ltsMat);
            thirdUsed.SetFloat("_UseMain3rdTex", 1);
            try
            {
                AddMesh("Top", ltsMat);
                AddMesh("Skirt", thirdUsed);
                var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
                Assert.That(groups.Count, Is.EqualTo(2));
                Assert.That(groups.Any(g => g.Preset == FadeFrame.Third), Is.True);
                Assert.That(groups.Any(g => g.Preset == FadeFrame.Second), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(thirdUsed);
            }
        }

        [Test]
        public void GroupByShader_UnknownShader_CannotSetupFade()
        {
            AddMesh("Prop", unknownMat);
            var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(groups[0].CanSetupFade, Is.False);
            Assert.That(groups[0].FadeDisabledReason, Is.Not.Empty);
        }

        [Test]
        public void GroupByShader_NullMaterial_CannotSetupFade()
        {
            AddMesh("Broken", new Material[] { null });
            var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(root));
            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(groups[0].CanSetupFade, Is.False);
        }
    }
}
```

- [ ] **Step 2: コンパイルエラーを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
```

- [ ] **Step 3: 実装を書く**

`Editor/Core/MaterialSlotScanner.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class SlotInfo
    {
        public Renderer Renderer;
        public int SlotIndex;
        public Material Material;
        public ShaderFamilyInfo Family;
        /// <summary>family が lilToon_multi のときの _TransparentMode 値。それ以外 -1</summary>
        public int MultiTransparentMode = -1;
        /// <summary>Family.IsKnown のときのみ非 null</summary>
        public FadeCompatResult FadeCompat;
    }

    public class SlotGroup
    {
        public string Family;
        public string Variant;
        public string ShaderGuid;
        /// <summary>グループ内マテリアルの推奨フェード枠（グループ分割キー）。null = フェード不可</summary>
        public FadeFrame? Preset;
        public List<SlotInfo> Slots = new List<SlotInfo>();
        public bool NeedsShaderOverride;
        public string TransparentGuid;
        public bool CanSetupFade;
        public string FadeDisabledReason;
    }

    public static class MaterialSlotScanner
    {
        public static List<SlotInfo> Scan(GameObject costumeRoot)
        {
            var result = new List<SlotInfo>();
            if (costumeRoot == null) return result;
            foreach (var renderer in costumeRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];
                    var family = ShaderCatalog.Resolve(mat == null ? null : mat.shader);
                    var info = new SlotInfo
                    {
                        Renderer = renderer,
                        SlotIndex = i,
                        Material = mat,
                        Family = family,
                    };
                    if (family.Family == "lilToon_multi" && mat != null && mat.HasProperty("_TransparentMode"))
                    {
                        info.MultiTransparentMode = Mathf.RoundToInt(mat.GetFloat("_TransparentMode"));
                    }
                    if (family.IsKnown && mat != null)
                    {
                        info.FadeCompat = FadeCompatChecker.Check(mat);
                    }
                    result.Add(info);
                }
            }
            return result;
        }

        public static List<SlotGroup> GroupByShader(IEnumerable<SlotInfo> slots)
        {
            var groups = new Dictionary<(string, string, string, FadeFrame?), SlotGroup>();
            foreach (var slot in slots)
            {
                var guid = ShaderGuidOf(slot.Material);
                var preset = slot.FadeCompat?.Recommended;
                var key = (slot.Family.Family, slot.Family.Variant, guid, preset);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new SlotGroup
                    {
                        Family = slot.Family.Family,
                        Variant = slot.Family.Variant,
                        ShaderGuid = guid,
                        Preset = preset,
                        NeedsShaderOverride = slot.Family.NeedsShaderOverride,
                        TransparentGuid = slot.Family.TransparentGuid,
                    };
                    SetFadeAvailability(group, slot);
                    groups.Add(key, group);
                }
                group.Slots.Add(slot);
            }
            return groups.Values
                .OrderBy(g => g.Family).ThenBy(g => g.Variant).ThenBy(g => g.Preset)
                .ToList();
        }

        static void SetFadeAvailability(SlotGroup group, SlotInfo sample)
        {
            if (sample.Material == null || sample.Material.shader == null)
            {
                group.CanSetupFade = false;
                group.FadeDisabledReason = "マテリアル未設定";
                return;
            }
            if (!sample.Family.IsKnown)
            {
                group.CanSetupFade = false;
                group.FadeDisabledReason = "未知のシェーダー";
                return;
            }
            if (sample.Family.Family == "lilToon_multi" && sample.MultiTransparentMode >= 3)
            {
                group.CanSetupFade = false;
                group.FadeDisabledReason = "_TransparentMode が Refraction/Fur/Gem 系";
                return;
            }
            if (group.Preset == null)
            {
                group.CanSetupFade = false;
                group.FadeDisabledReason = "3rd/2nd/AlphaMask 全枠使用済み";
                return;
            }
            group.CanSetupFade = true;
            group.FadeDisabledReason = null;
        }

        static string ShaderGuidOf(Material mat)
        {
            if (mat == null || mat.shader == null) return "";
            var path = AssetDatabase.GetAssetPath(mat.shader);
            return string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` → `"failed":0`

- [ ] **Step 5: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "MaterialSlotScanner: 走査とシェーダーグルーピング"
```

---

### Task 6: TransparencyPresets（透過プリセットのプロパティ定義）

**Files:**
- Create: `Editor/Core/TransparencyPresets.cs`
- Test: `Test/TransparencyPresetsTest.cs`

**Interfaces:**
- Consumes: `FadeFrame`
- Produces:
  - `enum PresetPropertyType { Float, Range, Int, Color, Vector, Texture }`（AO ME の `ShaderPropertyType` と同名メンバー。リフレクション時に `Enum.Parse` で変換する）
  - `class PresetProperty { string Name; PresetPropertyType Type; float FloatValue; int IntValue; Color ColorValue; Vector4 VectorValue; Texture TextureValue; }`
  - `static List<PresetProperty> TransparencyPresets.For(FadeFrame frame)` — 透過共通プロパティ + frame 別駆動プロパティ
  - `static List<PresetProperty> TransparencyPresets.OneTwoTransOverrides()` — onetrans/twotrans 用の 3rd Tex 個別 override（3項目のみ）
  - `static PresetProperty TransparencyPresets.TransparentModeOverride()` — multi 用 `_TransparentMode=2`

- [ ] **Step 1: 失敗するテストを書く**

`Test/TransparencyPresetsTest.cs`:

```csharp
using System.Linq;
using NUnit.Framework;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class TransparencyPresetsTest
    {
        [Test]
        public void For_Third_ContainsCommonAndThirdDriver()
        {
            var props = TransparencyPresets.For(FadeFrame.Third);
            Assert.That(props.First(p => p.Name == "_DstBlend").FloatValue, Is.EqualTo(10));
            Assert.That(props.First(p => p.Name == "_UseMain3rdTex").FloatValue, Is.EqualTo(1));
            Assert.That(props.First(p => p.Name == "_Main3rdTexBlendMode").FloatValue, Is.EqualTo(3));
            Assert.That(props.First(p => p.Name == "_Main3rdTexAlphaMode").FloatValue, Is.EqualTo(2));
            Assert.That(props.Any(p => p.Name == "_UseMain2ndTex"), Is.False);
            Assert.That(props.Any(p => p.Name == "_AlphaMaskMode"), Is.False);
        }

        [Test]
        public void For_Second_ContainsSecondDriver()
        {
            var props = TransparencyPresets.For(FadeFrame.Second);
            Assert.That(props.First(p => p.Name == "_UseMain2ndTex").FloatValue, Is.EqualTo(1));
            Assert.That(props.Any(p => p.Name == "_UseMain3rdTex"), Is.False);
        }

        [Test]
        public void For_AlphaMask_ContainsAlphaMaskDriver()
        {
            var props = TransparencyPresets.For(FadeFrame.AlphaMask);
            Assert.That(props.First(p => p.Name == "_AlphaMaskMode").FloatValue, Is.EqualTo(2));
            Assert.That(props.First(p => p.Name == "_AlphaMaskValue").FloatValue, Is.EqualTo(0));
        }

        [Test]
        public void OneTwoTransOverrides_OnlyThirdTexDriver()
        {
            var props = TransparencyPresets.OneTwoTransOverrides();
            Assert.That(props.Count, Is.EqualTo(3));
            Assert.That(props.First(p => p.Name == "_UseMain3rdTex").FloatValue, Is.EqualTo(1));
            Assert.That(props.Any(p => p.Name == "_DstBlend"), Is.False);
        }

        [Test]
        public void TransparentModeOverride()
        {
            var p = TransparencyPresets.TransparentModeOverride();
            Assert.That(p.Name, Is.EqualTo("_TransparentMode"));
            Assert.That(p.FloatValue, Is.EqualTo(2));
        }
    }
}
```

- [ ] **Step 2: コンパイルエラーを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
```

- [ ] **Step 3: 実装を書く**

`Editor/Core/TransparencyPresets.cs`（プロパティ表は `SetupAOMaterialEditorCommand.cs` の `LilToonTransparentPresetForFadeKey` の完全移植。lilToon 純正の opaque→transparent 変換 `lilMaterialUtils.SetupMaterialWithRenderingMode` で書き換わる項目に準拠。`_Pre*` 系は透過版シェーダーの Properties デフォルトに任せるため含めない。`_Cutoff`・`_AlphaMask*` 系は純正変換でも touch しないため共通部には含めない）:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public enum PresetPropertyType
    {
        Float,
        Range,
        Int,
        Color,
        Vector,
        Texture,
    }

    public class PresetProperty
    {
        public string Name;
        public PresetPropertyType Type;
        public float FloatValue;
        public int IntValue;
        public Color ColorValue = Color.white;
        public Vector4 VectorValue = Vector4.zero;
        public Texture TextureValue;
    }

    public static class TransparencyPresets
    {
        static PresetProperty F(string name, float v) => new PresetProperty { Name = name, Type = PresetPropertyType.Float, FloatValue = v };

        public static List<PresetProperty> For(FadeFrame frame)
        {
            var props = new List<PresetProperty>
            {
                // SetupMaterialWithRenderingMode の Transparent ケース
                F("_SrcBlend", 1),                    // BlendMode.One
                F("_DstBlend", 10),                   // BlendMode.OneMinusSrcAlpha
                F("_AlphaToMask", 0),
                // Outline 系 Transparent (isoutl 時のみ意味あるが、共通で書いても害なし)
                F("_OutlineSrcBlend", 5),             // BlendMode.SrcAlpha
                F("_OutlineDstBlend", 10),            // BlendMode.OneMinusSrcAlpha
                F("_OutlineAlphaToMask", 0),
                // SetupMaterialWithRenderingMode の共通処理
                F("_ZWrite", 1),
                F("_ZTest", 4),                       // CompareFunction.LessEqual
                F("_OffsetFactor", 0),
                F("_OffsetUnits", 0),
                F("_ColorMask", 15),
                F("_SrcBlendAlpha", 1),               // One
                F("_DstBlendAlpha", 10),              // OneMinusSrcAlpha
                F("_BlendOp", 0),                     // Add
                F("_BlendOpAlpha", 0),                // Add
                F("_SrcBlendFA", 1),                  // One
                F("_DstBlendFA", 1),                  // One
                F("_SrcBlendAlphaFA", 0),             // Zero
                F("_DstBlendAlphaFA", 1),             // One
                F("_BlendOpFA", 4),                   // Max
                F("_BlendOpAlphaFA", 4),              // Max
                // Outline 系 共通処理
                F("_OutlineCull", 1),                 // Front
                F("_OutlineZWrite", 1),
                F("_OutlineZTest", 2),                // CompareFunction.Less
                F("_OutlineOffsetFactor", 0),
                F("_OutlineOffsetUnits", 0),
                F("_OutlineColorMask", 15),
                F("_OutlineSrcBlendAlpha", 1),
                F("_OutlineDstBlendAlpha", 10),
                F("_OutlineBlendOp", 0),
                F("_OutlineBlendOpAlpha", 0),
                F("_OutlineSrcBlendFA", 1),
                F("_OutlineDstBlendFA", 1),
                F("_OutlineSrcBlendAlphaFA", 0),
                F("_OutlineDstBlendAlphaFA", 1),
                F("_OutlineBlendOpFA", 4),
                F("_OutlineBlendOpAlphaFA", 4),
            };

            switch (frame)
            {
                case FadeFrame.Third:
                    props.Add(F("_UseMain3rdTex", 1));
                    props.Add(F("_Main3rdTexBlendMode", 3));
                    props.Add(F("_Main3rdTexAlphaMode", 2));
                    break;
                case FadeFrame.Second:
                    props.Add(F("_UseMain2ndTex", 1));
                    props.Add(F("_Main2ndTexBlendMode", 3));
                    props.Add(F("_Main2ndTexAlphaMode", 2));
                    break;
                case FadeFrame.AlphaMask:
                    // _AlphaMaskMode = 2 (multiply)、_AlphaMaskValue は toggle-menu の -1↔0 駆動の初期値 0
                    props.Add(F("_AlphaMaskMode", 2));
                    props.Add(F("_AlphaMaskValue", 0));
                    break;
            }
            return props;
        }

        /// <summary>onetrans/twotrans 用: ブレンド設定に触らず 3rd Tex 駆動だけ有効化する</summary>
        public static List<PresetProperty> OneTwoTransOverrides() => new List<PresetProperty>
        {
            F("_UseMain3rdTex", 1),
            F("_Main3rdTexBlendMode", 3),
            F("_Main3rdTexAlphaMode", 2),
        };

        /// <summary>lilToon Multi 用: _TransparentMode を Transparent(2) に</summary>
        public static PresetProperty TransparentModeOverride() => F("_TransparentMode", 2);
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` → `"failed":0`

- [ ] **Step 5: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "TransparencyPresets: 透過プリセットのプロパティ定義"
```

---

### Task 7: AOMaterialEditorSetup（リフレクションによる AO ME 作成）

**Files:**
- Create: `Editor/Setup/AOMaterialEditorSetup.cs`
- Test: `Test/AOMaterialEditorSetupTest.cs`

**Interfaces:**
- Consumes: `PresetProperty`, `PresetPropertyType`
- Produces:
  - `class AOMaterialEditorSetup.SlotTarget { string RendererPath; int MaterialIndex; }`（RendererPath は**アバタールート相対**。ビルド時 `BaseObject.transform.Find` で解決されるため root 名プレフィクスを含めない）
  - `static bool AOMaterialEditorSetup.IsAvailable` — `Aoyon.MaterialEditor.MaterialEditorComponent` 型が見つかるか
  - `static bool AOMaterialEditorSetup.HasComponent(GameObject host)` — host に AO ME コンポーネントが付いているか（未導入環境では常に false）
  - `static Component AOMaterialEditorSetup.Apply(GameObject host, IReadOnlyList<SlotTarget> slots, Shader overrideShader, IReadOnlyList<PresetProperty> properties)` — host に AO ME を get-or-add（Undo 対応）し SlotTargets モードで設定。`overrideShader` null なら OverrideShader=false。`OverrideRenderQueue` は常に false。IsAvailable=false なら例外 `InvalidOperationException`

実装は `claude-vrchat-avatar-skills/Packages/net.narazaka.vrchat.avatar-agent-tools/Editor/SetupAOMaterialEditorCommand.cs` のリフレクションロジックの移植。対象型（すべて internal、`FindType` で全アセンブリ走査）:
- `Aoyon.MaterialEditor.MaterialEditorComponent` — フィールド `TargetSettings` / `OverrideSettings`、基底クラスに `DataVersion`（1 を設定）
- `TargetSettings.Mode`（enum、`"SlotTargets"` を `Enum.Parse`）、`TargetSettings.SlotTargets.TargetSlots`（`List<MaterialSlotReference>`）
- `Aoyon.MaterialEditor.MaterialSlotReference` — `RendererReference`（Modular Avatar の `AvatarObjectReference`、その public フィールド `referencePath` に相対パスを設定）と `MaterialIndex`
- `OverrideSettings` — `OverrideShader`(bool) / `TargetShader`(Shader) / `OverrideRenderQueue`(bool) / `RenderQueueValue`(int) / `PropertyOverrides`（`List<MaterialProperty>`）
- `Aoyon.MaterialEditor.MaterialProperty` — `PropertyName`(string) / `PropertyType`(`UnityEngine.Rendering.ShaderPropertyType`) / `FloatValue` / `IntValue` / `ColorValue` / `VectorValue` / `TextureValue` / `TextureOffsetValue`(Vector2.zero) / `TextureScaleValue`(Vector2.one)

- [ ] **Step 1: 失敗するテストを書く**

`Test/AOMaterialEditorSetupTest.cs`（`aoyon.material-editor` 未導入環境では `Assume` により skip される。導入方法は本計画冒頭「検証環境の前提」参照）:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class AOMaterialEditorSetupTest
    {
        GameObject host;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("trans_host");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        [Test]
        public void IsAvailable_DoesNotThrow()
        {
            // 導入有無に関わらず bool を返す（例外にならない）
            Assert.DoesNotThrow(() => { var _ = AOMaterialEditorSetup.IsAvailable; });
        }

        [Test]
        public void Apply_Unavailable_Throws()
        {
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.False, "aoyon.material-editor 導入環境では skip");
            Assert.Throws<System.InvalidOperationException>(() =>
                AOMaterialEditorSetup.Apply(host, new List<AOMaterialEditorSetup.SlotTarget>(), null, new List<PresetProperty>()));
        }

        [Test]
        public void Apply_CreatesComponentWithSlotTargets()
        {
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.True, "aoyon.material-editor 未導入なら skip");
            var slots = new List<AOMaterialEditorSetup.SlotTarget>
            {
                new AOMaterialEditorSetup.SlotTarget { RendererPath = "Costume/Top", MaterialIndex = -1 },
                new AOMaterialEditorSetup.SlotTarget { RendererPath = "Costume/Skirt", MaterialIndex = 1 },
            };
            var shader = Shader.Find("Standard");
            var props = new List<PresetProperty>
            {
                new PresetProperty { Name = "_UseMain3rdTex", Type = PresetPropertyType.Float, FloatValue = 1 },
            };
            var comp = AOMaterialEditorSetup.Apply(host, slots, shader, props);
            Assert.That(comp, Is.Not.Null);

            // SerializedObject でシリアライズ結果を検証（internal 型のため）
            var so = new SerializedObject(comp);
            Assert.That(so.FindProperty("TargetSettings.Mode").enumNames[so.FindProperty("TargetSettings.Mode").enumValueIndex], Is.EqualTo("SlotTargets"));
            var targetSlots = so.FindProperty("TargetSettings.SlotTargets.TargetSlots");
            Assert.That(targetSlots.arraySize, Is.EqualTo(2));
            Assert.That(targetSlots.GetArrayElementAtIndex(0).FindPropertyRelative("RendererReference.referencePath").stringValue, Is.EqualTo("Costume/Top"));
            Assert.That(targetSlots.GetArrayElementAtIndex(1).FindPropertyRelative("MaterialIndex").intValue, Is.EqualTo(1));
            Assert.That(so.FindProperty("OverrideSettings.OverrideShader").boolValue, Is.True);
            Assert.That(so.FindProperty("OverrideSettings.TargetShader").objectReferenceValue, Is.EqualTo(shader));
            Assert.That(so.FindProperty("OverrideSettings.OverrideRenderQueue").boolValue, Is.False);
            var overrides = so.FindProperty("OverrideSettings.PropertyOverrides");
            Assert.That(overrides.arraySize, Is.EqualTo(1));
            Assert.That(overrides.GetArrayElementAtIndex(0).FindPropertyRelative("PropertyName").stringValue, Is.EqualTo("_UseMain3rdTex"));
        }

        [Test]
        public void Apply_NullShader_NoOverrideShader()
        {
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.True, "aoyon.material-editor 未導入なら skip");
            var comp = AOMaterialEditorSetup.Apply(host, new List<AOMaterialEditorSetup.SlotTarget>(), null, new List<PresetProperty>());
            var so = new SerializedObject(comp);
            Assert.That(so.FindProperty("OverrideSettings.OverrideShader").boolValue, Is.False);
        }

        [Test]
        public void Apply_Twice_ReusesComponent()
        {
            Assume.That(AOMaterialEditorSetup.IsAvailable, Is.True, "aoyon.material-editor 未導入なら skip");
            Assert.That(AOMaterialEditorSetup.HasComponent(host), Is.False);
            var c1 = AOMaterialEditorSetup.Apply(host, new List<AOMaterialEditorSetup.SlotTarget>(), null, new List<PresetProperty>());
            var c2 = AOMaterialEditorSetup.Apply(host, new List<AOMaterialEditorSetup.SlotTarget>(), null, new List<PresetProperty>());
            Assert.That(c1, Is.EqualTo(c2));
            Assert.That(AOMaterialEditorSetup.HasComponent(host), Is.True);
        }
    }
}
```

- [ ] **Step 2: コンパイルエラーを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
```

- [ ] **Step 3: 実装を書く**

`Editor/Setup/AOMaterialEditorSetup.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public static class AOMaterialEditorSetup
    {
        public class SlotTarget
        {
            /// <summary>アバタールート相対パス（root 名プレフィクスなし）</summary>
            public string RendererPath;
            /// <summary>-1 で全スロット</summary>
            public int MaterialIndex = -1;
        }

        const string ComponentTypeName = "Aoyon.MaterialEditor.MaterialEditorComponent";
        const string MaterialSlotReferenceTypeName = "Aoyon.MaterialEditor.MaterialSlotReference";
        const string MaterialPropertyTypeName = "Aoyon.MaterialEditor.MaterialProperty";

        public static bool IsAvailable => FindType(ComponentTypeName) != null;

        public static bool HasComponent(GameObject host)
        {
            var t = FindType(ComponentTypeName);
            return t != null && host != null && host.GetComponent(t) != null;
        }

        public static Component Apply(GameObject host, IReadOnlyList<SlotTarget> slots, Shader overrideShader, IReadOnlyList<PresetProperty> properties)
        {
            var componentType = FindType(ComponentTypeName);
            if (componentType == null) throw new InvalidOperationException("AO Material Editor (aoyon.material-editor) が見つかりません");

            var comp = host.GetComponent(componentType) as Component;
            if (comp == null) comp = Undo.AddComponent(host, componentType);
            else Undo.RecordObject(comp, "Setup AO Material Editor");

            // DataVersion = 1 (current)
            componentType.BaseType?.GetField("DataVersion", BindingFlags.Public | BindingFlags.Instance)?.SetValue(comp, 1);

            ConfigureTargetSettings(comp, componentType, slots);
            ConfigureOverrideSettings(comp, componentType, overrideShader, properties);

            EditorUtility.SetDirty(comp);
            return comp;
        }

        static void ConfigureTargetSettings(Component comp, Type componentType, IReadOnlyList<SlotTarget> slots)
        {
            var targetSettings = componentType.GetField("TargetSettings", BindingFlags.Public | BindingFlags.Instance).GetValue(comp);
            var t = targetSettings.GetType();
            var modeField = t.GetField("Mode", BindingFlags.Public | BindingFlags.Instance);
            modeField.SetValue(targetSettings, Enum.Parse(modeField.FieldType, "SlotTargets"));

            var slotTargets = t.GetField("SlotTargets", BindingFlags.Public | BindingFlags.Instance).GetValue(targetSettings);
            var targetSlotsField = slotTargets.GetType().GetField("TargetSlots", BindingFlags.Public | BindingFlags.Instance);
            var list = (IList)Activator.CreateInstance(targetSlotsField.FieldType);

            var slotRefType = FindType(MaterialSlotReferenceTypeName);
            foreach (var slot in slots)
            {
                if (string.IsNullOrEmpty(slot.RendererPath)) continue;
                var slotRef = Activator.CreateInstance(slotRefType);
                var rendererRef = slotRefType.GetField("RendererReference", BindingFlags.Public | BindingFlags.Instance).GetValue(slotRef);
                rendererRef.GetType().GetField("referencePath", BindingFlags.Public | BindingFlags.Instance).SetValue(rendererRef, slot.RendererPath);
                slotRefType.GetField("MaterialIndex", BindingFlags.Public | BindingFlags.Instance).SetValue(slotRef, slot.MaterialIndex);
                list.Add(slotRef);
            }
            targetSlotsField.SetValue(slotTargets, list);
        }

        static void ConfigureOverrideSettings(Component comp, Type componentType, Shader overrideShader, IReadOnlyList<PresetProperty> properties)
        {
            var overrideSettings = componentType.GetField("OverrideSettings", BindingFlags.Public | BindingFlags.Instance).GetValue(comp);
            var t = overrideSettings.GetType();

            t.GetField("OverrideShader", BindingFlags.Public | BindingFlags.Instance).SetValue(overrideSettings, overrideShader != null);
            if (overrideShader != null)
            {
                t.GetField("TargetShader", BindingFlags.Public | BindingFlags.Instance).SetValue(overrideSettings, overrideShader);
            }
            t.GetField("OverrideRenderQueue", BindingFlags.Public | BindingFlags.Instance).SetValue(overrideSettings, false);

            var propertyOverridesField = t.GetField("PropertyOverrides", BindingFlags.Public | BindingFlags.Instance);
            var list = (IList)Activator.CreateInstance(propertyOverridesField.FieldType);
            foreach (var prop in properties)
            {
                list.Add(BuildMaterialProperty(prop));
            }
            propertyOverridesField.SetValue(overrideSettings, list);
        }

        static object BuildMaterialProperty(PresetProperty prop)
        {
            var mpType = FindType(MaterialPropertyTypeName);
            var mp = Activator.CreateInstance(mpType);
            mpType.GetField("PropertyName", BindingFlags.Public | BindingFlags.Instance).SetValue(mp, prop.Name);
            mpType.GetField("PropertyType", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(mp, (ShaderPropertyType)Enum.Parse(typeof(ShaderPropertyType), prop.Type.ToString()));

            mpType.GetField("TextureOffsetValue", BindingFlags.Public | BindingFlags.Instance).SetValue(mp, Vector2.zero);
            mpType.GetField("TextureScaleValue", BindingFlags.Public | BindingFlags.Instance).SetValue(mp, Vector2.one);
            mpType.GetField("ColorValue", BindingFlags.Public | BindingFlags.Instance).SetValue(mp, Color.white);
            mpType.GetField("VectorValue", BindingFlags.Public | BindingFlags.Instance).SetValue(mp, Vector4.zero);

            switch (prop.Type)
            {
                case PresetPropertyType.Float:
                case PresetPropertyType.Range:
                    mpType.GetField("FloatValue", BindingFlags.Public | BindingFlags.Instance).SetValue(mp, prop.FloatValue);
                    break;
                case PresetPropertyType.Int:
                    mpType.GetField("IntValue", BindingFlags.Public | BindingFlags.Instance).SetValue(mp, prop.IntValue);
                    break;
                case PresetPropertyType.Color:
                    mpType.GetField("ColorValue", BindingFlags.Public | BindingFlags.Instance).SetValue(mp, prop.ColorValue);
                    break;
                case PresetPropertyType.Vector:
                    mpType.GetField("VectorValue", BindingFlags.Public | BindingFlags.Instance).SetValue(mp, prop.VectorValue);
                    break;
                case PresetPropertyType.Texture:
                    mpType.GetField("TextureValue", BindingFlags.Public | BindingFlags.Instance).SetValue(mp, prop.TextureValue);
                    break;
            }
            return mp;
        }

        static Type FindType(string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(typeName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` → `"failed":0`（aome 未導入なら実動テストは skip 扱い＝failed に数えられない）

- [ ] **Step 5: （任意・推奨）aoyon.material-editor を sandbox に導入してフルテスト**

Prefab Stage を開いていないことを確認してから:

```bash
cp -r x:/make/devel/vrchat-cynthia-av3/Packages/aoyon.material-editor "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/"
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` → `"failed":0`

- [ ] **Step 6: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "AOMaterialEditorSetup: リフレクションによる AO ME 一括設定"
```

---

### Task 8: ToggleMenuSetup（Avatar Toggle Menu Creator 作成）

**Files:**
- Create: `Editor/Setup/ToggleMenuSetup.cs`
- Test: `Test/ToggleMenuSetupTest.cs`

**Interfaces:**
- Consumes: `FadeFrame`
- Produces:
  - `class ToggleMenuSetup.FadeTarget { string MeshPath; FadeFrame Frame; }`（MeshPath はアバタールート相対）
  - `static AvatarToggleMenuCreator ToggleMenuSetup.Create(GameObject host, IEnumerable<string> togglePaths, IEnumerable<FadeTarget> fades, float transitionSeconds)` — host に get-or-add（Undo 対応）。フェード駆動:
    - Third: `ToggleShaderVectorParameters[(meshPath, "_Color3rd")]` = Inactive `(1,1,1,0)` / Active `(1,1,1,1)`
    - Second: 同上 `"_Color2nd"`
    - AlphaMask: `ToggleShaderParameters[(meshPath, "_AlphaMaskValue")]` = Inactive `-1` / Active `0`
    - 共通: TransitionOffsetPercent=0, TransitionDurationPercent=100
    - `AvatarToggleMenu.TransitionSeconds = transitionSeconds`, `Saved=true`, `Synced=true`, `ToggleDefaultValue=true`
    - togglePaths は `ToggleObjects[path] = ToggleType.ON`

使用する公開 API（`net.narazaka.vrchat.avatar-menu-creater-for-ma`）:
- `net.narazaka.avatarmenucreator.components.AvatarToggleMenuCreator`（`.AvatarToggleMenu` フィールド）
- `AvatarToggleMenu.ToggleObjects` : `ToggleTypeDictionary` = `Dictionary<string, ToggleType>`
- `AvatarToggleMenu.ToggleShaderVectorParameters` : `ToggleShaderVectorParameterDictionary` = `Dictionary<(string, string), ToggleVector4>`
- `AvatarToggleMenu.ToggleShaderParameters` : `ToggleBlendShapeDictionary` = `Dictionary<(string, string), ToggleBlendShape>`
- `ToggleVector4` / `ToggleBlendShape` : フィールド `Inactive` / `Active` / `TransitionOffsetPercent` / `TransitionDurationPercent`
- `ToggleType.ON`

- [ ] **Step 1: 失敗するテストを書く**

`Test/ToggleMenuSetupTest.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using net.narazaka.avatarmenucreator;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class ToggleMenuSetupTest
    {
        GameObject host;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("トップス");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        [Test]
        public void Create_SetsToggleObjectsAndFades()
        {
            var creator = ToggleMenuSetup.Create(
                host,
                new[] { "Costume/Top", "Costume/Ribbon" },
                new[]
                {
                    new ToggleMenuSetup.FadeTarget { MeshPath = "Costume/Top", Frame = FadeFrame.Third },
                    new ToggleMenuSetup.FadeTarget { MeshPath = "Costume/Ribbon", Frame = FadeFrame.AlphaMask },
                },
                1f);

            var menu = creator.AvatarToggleMenu;
            Assert.That(menu.TransitionSeconds, Is.EqualTo(1f));
            Assert.That(menu.Saved, Is.True);
            Assert.That(menu.Synced, Is.True);
            Assert.That(menu.ToggleDefaultValue, Is.True);
            Assert.That(menu.ToggleObjects[("Costume/Top")], Is.EqualTo(ToggleType.ON));
            Assert.That(menu.ToggleObjects[("Costume/Ribbon")], Is.EqualTo(ToggleType.ON));

            var vec = menu.ToggleShaderVectorParameters[("Costume/Top", "_Color3rd")];
            Assert.That(vec.Inactive, Is.EqualTo(new Vector4(1, 1, 1, 0)));
            Assert.That(vec.Active, Is.EqualTo(new Vector4(1, 1, 1, 1)));
            Assert.That(vec.TransitionDurationPercent, Is.EqualTo(100f));

            var am = menu.ToggleShaderParameters[("Costume/Ribbon", "_AlphaMaskValue")];
            Assert.That(am.Inactive, Is.EqualTo(-1f));
            Assert.That(am.Active, Is.EqualTo(0f));
        }

        [Test]
        public void Create_SecondFrame_UsesColor2nd()
        {
            var creator = ToggleMenuSetup.Create(
                host,
                new string[0],
                new[] { new ToggleMenuSetup.FadeTarget { MeshPath = "Costume/Top", Frame = FadeFrame.Second } },
                1f);
            Assert.That(creator.AvatarToggleMenu.ToggleShaderVectorParameters.ContainsKey(("Costume/Top", "_Color2nd")), Is.True);
        }

        [Test]
        public void Create_Twice_ReusesComponent()
        {
            var c1 = ToggleMenuSetup.Create(host, new string[0], new ToggleMenuSetup.FadeTarget[0], 1f);
            var c2 = ToggleMenuSetup.Create(host, new string[0], new ToggleMenuSetup.FadeTarget[0], 1f);
            Assert.That(c1, Is.EqualTo(c2));
        }
    }
}
```

- [ ] **Step 2: コンパイルエラーを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
```

- [ ] **Step 3: 実装を書く**

`Editor/Setup/ToggleMenuSetup.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using net.narazaka.avatarmenucreator;
using net.narazaka.avatarmenucreator.components;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public static class ToggleMenuSetup
    {
        public class FadeTarget
        {
            /// <summary>アバタールート相対パス（root 名プレフィクスなし）</summary>
            public string MeshPath;
            public FadeFrame Frame;
        }

        public static AvatarToggleMenuCreator Create(GameObject host, IEnumerable<string> togglePaths, IEnumerable<FadeTarget> fades, float transitionSeconds)
        {
            var creator = host.GetComponent<AvatarToggleMenuCreator>();
            if (creator == null) creator = Undo.AddComponent<AvatarToggleMenuCreator>(host);
            else Undo.RecordObject(creator, "Setup Toggle Menu");

            var menu = creator.AvatarToggleMenu;
            menu.TransitionSeconds = transitionSeconds;
            menu.Saved = true;
            menu.Synced = true;
            menu.ToggleDefaultValue = true;

            foreach (var path in togglePaths)
            {
                menu.ToggleObjects[path] = ToggleType.ON;
            }

            foreach (var fade in fades)
            {
                switch (fade.Frame)
                {
                    case FadeFrame.Third:
                        menu.ToggleShaderVectorParameters[(fade.MeshPath, "_Color3rd")] = FadeVector();
                        break;
                    case FadeFrame.Second:
                        menu.ToggleShaderVectorParameters[(fade.MeshPath, "_Color2nd")] = FadeVector();
                        break;
                    case FadeFrame.AlphaMask:
                        menu.ToggleShaderParameters[(fade.MeshPath, "_AlphaMaskValue")] = new ToggleBlendShape
                        {
                            Inactive = -1f,
                            Active = 0f,
                            TransitionOffsetPercent = 0f,
                            TransitionDurationPercent = 100f,
                        };
                        break;
                }
            }

            EditorUtility.SetDirty(creator);
            return creator;
        }

        static ToggleVector4 FadeVector() => new ToggleVector4
        {
            Inactive = new Vector4(1, 1, 1, 0),
            Active = new Vector4(1, 1, 1, 1),
            TransitionOffsetPercent = 0f,
            TransitionDurationPercent = 100f,
        };
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` → `"failed":0`

- [ ] **Step 5: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "ToggleMenuSetup: Avatar Toggle Menu Creator のプリセット透過設定"
```

---

### Task 9: RenderQueueSetup（Change Render Queue の一覧・設定）

**Files:**
- Create: `Editor/Setup/RenderQueueSetup.cs`
- Test: `Test/RenderQueueSetupTest.cs`

**Interfaces:**
- Consumes: なし
- Produces:
  - `static int RenderQueueSetup.EffectiveQueue(Renderer renderer, int slotIndex, out ChangeRenderQueue.ChangeRenderQueue source)` — その Renderer 上の `ChangeRenderQueue` コンポーネントのうち `MaterialIndex == slotIndex` または `-1` の**最後の**ものの値。なければ Material の `renderQueue`（Material null なら -1）。source は該当コンポーネント（なければ null）
  - `static ChangeRenderQueue.ChangeRenderQueue RenderQueueSetup.Set(Renderer renderer, int materialIndex, int queue)` — 同一 `MaterialIndex` の既存コンポーネントがあれば値更新、なければ追加（Undo 対応）
  - `static void RenderQueueSetup.Remove(ChangeRenderQueue.ChangeRenderQueue component)`（Undo 対応）

`ChangeRenderQueue` コンポーネント（`net.narazaka.vrchat.change-render-queue`、namespace `Narazaka.VRChat.ChangeRenderQueue`）: public フィールド `int RenderQueue = 2460` / `int MaterialIndex = -1`、`[RequireComponent(typeof(Renderer))]`。

※ namespace `Narazaka.VRChat.ChangeRenderQueue` と クラス名 `ChangeRenderQueue` が同名のため、本パッケージ側では `using CRQ = Narazaka.VRChat.ChangeRenderQueue.ChangeRenderQueue;` のエイリアスで参照する。

- [ ] **Step 1: 失敗するテストを書く**

`Test/RenderQueueSetupTest.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using CRQ = Narazaka.VRChat.ChangeRenderQueue.ChangeRenderQueue;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class RenderQueueSetupTest
    {
        GameObject go;
        SkinnedMeshRenderer renderer;
        Material mat;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("Mesh");
            renderer = go.AddComponent<SkinnedMeshRenderer>();
            mat = new Material(Shader.Find("Standard"));
            mat.renderQueue = 2000;
            renderer.sharedMaterials = new[] { mat, mat };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(mat);
        }

        [Test]
        public void EffectiveQueue_NoComponent_MaterialValue()
        {
            var queue = RenderQueueSetup.EffectiveQueue(renderer, 0, out var source);
            Assert.That(queue, Is.EqualTo(2000));
            Assert.That(source, Is.Null);
        }

        [Test]
        public void Set_AddsComponent()
        {
            var comp = RenderQueueSetup.Set(renderer, 0, 2460);
            Assert.That(comp.RenderQueue, Is.EqualTo(2460));
            Assert.That(comp.MaterialIndex, Is.EqualTo(0));
            var queue = RenderQueueSetup.EffectiveQueue(renderer, 0, out var source);
            Assert.That(queue, Is.EqualTo(2460));
            Assert.That(source, Is.EqualTo(comp));
            // slot 1 には効かない
            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 1, out _), Is.EqualTo(2000));
        }

        [Test]
        public void Set_MinusOne_AppliesToAllSlots()
        {
            RenderQueueSetup.Set(renderer, -1, 2450);
            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 0, out _), Is.EqualTo(2450));
            Assert.That(RenderQueueSetup.EffectiveQueue(renderer, 1, out _), Is.EqualTo(2450));
        }

        [Test]
        public void Set_SameIndexTwice_UpdatesExisting()
        {
            var c1 = RenderQueueSetup.Set(renderer, 0, 2460);
            var c2 = RenderQueueSetup.Set(renderer, 0, 2470);
            Assert.That(c1, Is.EqualTo(c2));
            Assert.That(c2.RenderQueue, Is.EqualTo(2470));
            Assert.That(renderer.GetComponents<CRQ>().Length, Is.EqualTo(1));
        }

        [Test]
        public void Remove_DeletesComponent()
        {
            var comp = RenderQueueSetup.Set(renderer, 0, 2460);
            RenderQueueSetup.Remove(comp);
            Assert.That(renderer.GetComponents<CRQ>().Length, Is.EqualTo(0));
        }
    }
}
```

- [ ] **Step 2: コンパイルエラーを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
```

- [ ] **Step 3: 実装を書く**

`Editor/Setup/RenderQueueSetup.cs`:

```csharp
using UnityEditor;
using UnityEngine;
using CRQ = Narazaka.VRChat.ChangeRenderQueue.ChangeRenderQueue;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public static class RenderQueueSetup
    {
        public static int EffectiveQueue(Renderer renderer, int slotIndex, out CRQ source)
        {
            source = null;
            foreach (var comp in renderer.GetComponents<CRQ>())
            {
                if (comp.MaterialIndex == slotIndex || comp.MaterialIndex == -1) source = comp;
            }
            if (source != null) return source.RenderQueue;
            var materials = renderer.sharedMaterials;
            if (slotIndex < 0 || slotIndex >= materials.Length || materials[slotIndex] == null) return -1;
            return materials[slotIndex].renderQueue;
        }

        public static CRQ Set(Renderer renderer, int materialIndex, int queue)
        {
            CRQ target = null;
            foreach (var comp in renderer.GetComponents<CRQ>())
            {
                if (comp.MaterialIndex == materialIndex) target = comp;
            }
            if (target == null)
            {
                target = Undo.AddComponent<CRQ>(renderer.gameObject);
                target.MaterialIndex = materialIndex;
            }
            else
            {
                Undo.RecordObject(target, "Set Render Queue");
            }
            target.RenderQueue = queue;
            EditorUtility.SetDirty(target);
            return target;
        }

        public static void Remove(CRQ component)
        {
            Undo.DestroyObjectImmediate(component);
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` → `"failed":0`

- [ ] **Step 5: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "RenderQueueSetup: Change Render Queue の実効値取得と設定"
```

---

### Task 10: CostumeDashboardWindow — 衣装リストとツリー表示（読み取り専用）

**Files:**
- Modify: `Editor/UI/CostumeDashboardWindow.cs`（Task 1 の空ウィンドウを置き換え）

**Interfaces:**
- Consumes: `MaterialSlotScanner.Scan` / `GroupByShader`, `AvatarUtil.FindAvatarRoot` / `RelativePath`, `SlotInfo`, `SlotGroup`, `FadeFrame`, `RenderQueueSetup.EffectiveQueue`
- Produces: メニュー `Tools/Costume Dashboard` の EditorWindow。以後のタスク（11）がボタン列・アクションを追加する。行データ型 `Row { RowKind Kind; GameObject Costume; SlotGroup Group; SlotInfo Slot; }` / `enum RowKind { Costume, Group, Slot }` を内部クラスとして持つ。

仕様:
- `[SerializeField] List<GameObject> costumeRoots` — ObjectField リスト（＋「選択から追加」ボタン、各行に削除ボタン）。ドメインリロード後も保持される（EditorWindow の SerializeField）
- Refresh ボタンと `OnFocus()` で再走査
- MultiColumnTreeView。行階層: 衣装ルート > SlotGroup > SlotInfo
- 列: 「オブジェクト」（衣装名 / `family/variant[/preset]` / レンダラー名）、「スロット」、「マテリアル」、「シェーダー」（variant）、「3rd」「2nd」「AM」（空=○ / 使用済=×。ツールチップに非デフォルトプロパティ一覧）、「推奨」（3rd/2nd/AM/なし）、「Queue」（実効値。ChangeRenderQueue 由来なら `*` 付き）
- アバタールート不明の衣装は行に「⚠ アバタールートが見つかりません」を表示
- 衣装が Destroy されている（null）場合はリスト行を薄表示（この Task ではリスト表示のみで操作なしのため、null スキップで走査）

- [ ] **Step 1: 実装を書く**

`Editor/UI/CostumeDashboardWindow.cs` 全体を以下に置き換え:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class CostumeDashboardWindow : EditorWindow
    {
        [SerializeField] List<GameObject> costumeRoots = new List<GameObject>();

        MultiColumnTreeView tree;
        VisualElement costumeListContainer;

        internal enum RowKind { Costume, Group, Slot }

        internal class Row
        {
            public RowKind Kind;
            public GameObject Costume;
            public GameObject AvatarRoot;
            public SlotGroup Group;
            public SlotInfo Slot;
        }

        [MenuItem("Tools/Costume Dashboard")]
        public static void Open()
        {
            GetWindow<CostumeDashboardWindow>("Costume Dashboard");
        }

        void CreateGUI()
        {
            var root = rootVisualElement;

            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0 } };
            toolbar.Add(new Button(AddSelectedCostumes) { text = "選択から衣装を追加" });
            toolbar.Add(new Button(Refresh) { text = "更新" });
            root.Add(toolbar);

            costumeListContainer = new VisualElement { style = { flexShrink = 0 } };
            root.Add(costumeListContainer);

            tree = BuildTree();
            tree.style.flexGrow = 1;
            root.Add(tree);

            Refresh();
        }

        void OnFocus()
        {
            if (tree != null) Refresh();
        }

        void AddSelectedCostumes()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go != null && !costumeRoots.Contains(go)) costumeRoots.Add(go);
            }
            Refresh();
        }

        void Refresh()
        {
            RebuildCostumeList();
            tree.SetRootItems(BuildTreeItems());
            tree.Rebuild();
        }

        void RebuildCostumeList()
        {
            costumeListContainer.Clear();
            for (var i = 0; i < costumeRoots.Count; i++)
            {
                var index = i;
                var line = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                var field = new ObjectField { objectType = typeof(GameObject), value = costumeRoots[index], allowSceneObjects = true };
                field.style.flexGrow = 1;
                field.RegisterValueChangedCallback(e =>
                {
                    costumeRoots[index] = e.newValue as GameObject;
                    Refresh();
                });
                line.Add(field);
                line.Add(new Button(() => { costumeRoots.RemoveAt(index); Refresh(); }) { text = "×" });
                costumeListContainer.Add(line);
            }
            var addLine = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var addField = new ObjectField("衣装を追加") { objectType = typeof(GameObject), allowSceneObjects = true };
            addField.style.flexGrow = 1;
            addField.RegisterValueChangedCallback(e =>
            {
                var go = e.newValue as GameObject;
                if (go != null && !costumeRoots.Contains(go))
                {
                    costumeRoots.Add(go);
                    Refresh();
                }
            });
            addLine.Add(addField);
            costumeListContainer.Add(addLine);
        }

        List<TreeViewItemData<Row>> BuildTreeItems()
        {
            var items = new List<TreeViewItemData<Row>>();
            var id = 0;
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                var avatarRoot = AvatarUtil.FindAvatarRoot(costume);
                var groups = MaterialSlotScanner.GroupByShader(MaterialSlotScanner.Scan(costume));
                var groupItems = new List<TreeViewItemData<Row>>();
                foreach (var group in groups)
                {
                    var slotItems = group.Slots
                        .Select(slot => new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Slot, Costume = costume, AvatarRoot = avatarRoot, Group = group, Slot = slot }))
                        .ToList();
                    groupItems.Add(new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Group, Costume = costume, AvatarRoot = avatarRoot, Group = group }, slotItems));
                }
                items.Add(new TreeViewItemData<Row>(id++, new Row { Kind = RowKind.Costume, Costume = costume, AvatarRoot = avatarRoot }, groupItems));
            }
            return items;
        }

        MultiColumnTreeView BuildTree()
        {
            var columns = new Columns();
            columns.Add(MakeLabelColumn("object", "オブジェクト", 240, row =>
            {
                switch (row.Kind)
                {
                    case RowKind.Costume:
                        var warn = row.AvatarRoot == null ? " ⚠ アバタールートが見つかりません" : "";
                        var toggleCount = row.Costume.GetComponentsInChildren<net.narazaka.avatarmenucreator.components.AvatarToggleMenuCreator>(true).Length;
                        var toggleInfo = toggleCount > 0 ? $" [Toggle Menu: {toggleCount}]" : "";
                        return row.Costume.name + toggleInfo + warn;
                    case RowKind.Group:
                        var preset = row.Group.Preset switch
                        {
                            FadeFrame.Third => "3rd",
                            FadeFrame.Second => "2nd",
                            FadeFrame.AlphaMask => "AM",
                            _ => "×",
                        };
                        var reason = row.Group.CanSetupFade ? "" : $" ({row.Group.FadeDisabledReason})";
                        return $"{row.Group.Family}/{row.Group.Variant} [{preset}] ({row.Group.Slots.Count}){reason}";
                    default:
                        return row.Slot.Renderer == null ? "(missing)" : row.Slot.Renderer.name;
                }
            }));
            columns.Add(MakeLabelColumn("slot", "スロット", 50, row => row.Kind == RowKind.Slot ? row.Slot.SlotIndex.ToString() : ""));
            columns.Add(MakeLabelColumn("material", "マテリアル", 150, row => row.Kind == RowKind.Slot ? (row.Slot.Material == null ? "(なし)" : row.Slot.Material.name) : ""));
            columns.Add(MakeLabelColumn("shader", "シェーダー", 130, row => row.Kind == RowKind.Slot ? FormatShader(row.Slot) : ""));
            columns.Add(MakeFrameColumn("third", "3rd", row => row.FadeCompat?.Third));
            columns.Add(MakeFrameColumn("second", "2nd", row => row.FadeCompat?.Second));
            columns.Add(MakeFrameColumn("alphaMask", "AM", row => row.FadeCompat?.AlphaMask));
            columns.Add(MakeLabelColumn("recommended", "推奨", 50, row =>
            {
                if (row.Kind != RowKind.Slot || row.Slot.FadeCompat == null) return "";
                return row.Slot.FadeCompat.Recommended switch
                {
                    FadeFrame.Third => "3rd",
                    FadeFrame.Second => "2nd",
                    FadeFrame.AlphaMask => "AM",
                    _ => "なし",
                };
            }));
            columns.Add(MakeLabelColumn("queue", "Queue", 60, row =>
            {
                if (row.Kind != RowKind.Slot || row.Slot.Renderer == null) return "";
                var queue = RenderQueueSetup.EffectiveQueue(row.Slot.Renderer, row.Slot.SlotIndex, out var source);
                return source != null ? $"{queue}*" : queue.ToString();
            }));

            var view = new MultiColumnTreeView(columns);
            view.SetRootItems(new List<TreeViewItemData<Row>>());
            return view;
        }

        static string FormatShader(SlotInfo slot)
        {
            if (slot.Material == null || slot.Material.shader == null) return "(なし)";
            if (!slot.Family.IsKnown) return slot.Material.shader.name;
            var multi = slot.MultiTransparentMode >= 0 ? $" tm={slot.MultiTransparentMode}" : "";
            return $"{slot.Family.Variant}{multi}";
        }

        Column MakeLabelColumn(string name, string title, float width, System.Func<Row, string> text)
        {
            return new Column
            {
                name = name,
                title = title,
                width = width,
                makeCell = () => new Label(),
                bindCell = (element, index) =>
                {
                    var row = tree.GetItemDataForIndex<Row>(index);
                    ((Label)element).text = text(row);
                },
            };
        }

        Column MakeFrameColumn(string name, string title, System.Func<SlotInfo, FadeFrameState> stateOf)
        {
            return new Column
            {
                name = name,
                title = title,
                width = 36,
                makeCell = () => new Label(),
                bindCell = (element, index) =>
                {
                    var label = (Label)element;
                    var row = tree.GetItemDataForIndex<Row>(index);
                    if (row.Kind != RowKind.Slot || row.Slot.FadeCompat == null)
                    {
                        label.text = "";
                        label.tooltip = "";
                        return;
                    }
                    var state = stateOf(row.Slot);
                    label.text = state.Compatible ? "○" : "×";
                    label.tooltip = state.Compatible
                        ? "空き"
                        : string.Join("\n", state.NonDefaultProps.Select(p => $"{p.Name}: {p.Current} (default: {p.Default})"));
                },
            };
        }
    }
}
```

- [ ] **Step 2: コンパイル検証**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe get_logs --logType Error
```

Expected: エラーなし

- [ ] **Step 3: 手動スモーク確認（sandbox）**

sandbox のシーンに lilToon マテリアル付きのアバター（`Airi_Mochi_Bao.prefab` 等 Assets 内の既存アバター）を配置し、`Tools/Costume Dashboard` を開き、衣装（またはアバター配下の適当な GameObject）を登録して:
- ツリーに family/variant グループとスロット行が出る
- 3rd/2nd/AM 列と推奨列が表示される
- Queue 列に値が出る

を目視確認する。ユーザーに UI 確認を依頼するのはこのタスク完了時。

- [ ] **Step 4: 既存テストのリグレッション確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` → `"failed":0`

- [ ] **Step 5: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "CostumeDashboardWindow: 衣装リストとマテリアル状況ツリー表示"
```

---

### Task 11: Select ハイライト・チェック選択・アクション接続

**Files:**
- Modify: `Editor/UI/CostumeDashboardWindow.cs`

**Interfaces:**
- Consumes: `AOMaterialEditorSetup`, `ToggleMenuSetup`, `RenderQueueSetup`, `TransparencyPresets`, `AvatarUtil.RelativePath`
- Produces: 完成した UI（本計画の最終形。UX 調整はリリース後にユーザーと実物を見ながら行う）

追加する機能:
1. **Select 列**: Slot 行に [Select] ボタン。クリックで `Selection.activeGameObject = renderer.gameObject`（Ctrl/Cmd 押下時は `Selection.objects` に追加）+ `EditorGUIUtility.PingObject`。Unity 標準の SceneView 輪郭ハイライトが出る
2. **チェック列**: Slot 行に Toggle。チェック状態は `HashSet<int>`（renderer の instanceID × slot を `(instanceID << 8) | slotIndex` でキー化）で保持
3. **グループ行の [AO ME作成] ボタン**:
   - 有効条件: `group.CanSetupFade && row.AvatarRoot != null && AOMaterialEditorSetup.IsAvailable`（無効時 tooltip に理由。AO ME 未導入なら「aoyon.material-editor が未導入」）
   - ただし `onetrans`/`twotrans` variant は `CanSetupFade` 判定に依らず `Preset != null` 不要（`propertyPreset` なしで作る）ため、`group.Variant` が `onetrans[_o]`/`twotrans[_o]` で `FadeDisabledReason == "3rd/2nd/AlphaMask 全枠使用済み"` 以外なら有効
   - 動作: `costume` 配下に `trans/<suffix>` GameObject を find-or-create（`Undo.RegisterCreatedObjectUndo`）。suffix は `variant`、`Preset` が Second なら `variant_2nd`、AlphaMask なら `variant_alpha_mask`
   - slots: `AvatarUtil.RelativePath(avatarRoot, slot.Renderer.gameObject)` と `slot.SlotIndex`
   - shader: `group.NeedsShaderOverride` なら `AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(group.TransparentGuid))`、それ以外 null
   - properties の variant 分岐:
     - `opaque[_o]` / `cutout[_o]` / `trans[_o]`: `TransparencyPresets.For(group.Preset.Value)`
     - `onetrans[_o]` / `twotrans[_o]`: `TransparencyPresets.OneTwoTransOverrides()`
     - `multi[_o]`（`MultiTransparentMode` 0〜2）: `TransparencyPresets.For(group.Preset.Value)` + `TransparencyPresets.TransparentModeOverride()`
4. **ツールバーの [Toggle Menu作成] ボタン**:
   - 有効条件: チェックされた Slot 行が1つ以上、全チェック行の AvatarRoot が同一かつ非 null
   - 動作: 小ダイアログ（`EditorWindow.ShowModalUtility` ではなくシンプルに `UnityEditor.EditorInputDialog` は無いため、専用のミニ EditorWindow `ToggleMenuCreateDialog` を同ファイル内に定義）でメニュー名（既定: 衣装ルート名）とフェード秒数（既定: 1）を入力 → 衣装ルート配下に `<メニュー名>` GameObject を作成し `ToggleMenuSetup.Create` を呼ぶ
   - togglePaths: チェックされた Renderer の GameObject（アバタールート相対パス、重複除去）
   - fades: 各チェック行の `slot.FadeCompat?.Recommended` が非 null のものについて `FadeTarget { MeshPath, Frame }`（同一 mesh の重複は除去。同一 mesh に複数 Frame が出た場合は最初の1つ）
5. **Queue 列の編集**: Slot 行の Queue セルを Label から `IntegerField` + [設定] ボタンに変更…ではなく、初版は Slot 行に [Q] ボタンを置き、クリックでポップアップ（`UnityEditor.PopupWindow` + `PopupWindowContent`）を出して IntField（現在の実効値を初期値）・[適用]（`RenderQueueSetup.Set(renderer, slotIndex, value)`）・[コンポーネント削除]（source があるときのみ、`RenderQueueSetup.Remove`）を提供
6. アクション実行後は `Refresh()`

- [ ] **Step 1: 実装を書く**

`Editor/UI/CostumeDashboardWindow.cs` に以下を追加・変更する（完全なコード。Task 10 のクラスへの追記分）:

(a) フィールド追加:

```csharp
        readonly HashSet<long> checkedSlots = new HashSet<long>();

        static long SlotKey(SlotInfo slot) => ((long)slot.Renderer.GetInstanceID() << 8) | (uint)(slot.SlotIndex & 0xff);
```

(b) `BuildTree()` の columns に以下を追加（"queue" 列の後）:

```csharp
            columns.Add(new Column
            {
                name = "select",
                title = "選択",
                width = 60,
                makeCell = () => new Button { text = "Select" },
                bindCell = (element, index) =>
                {
                    var button = (Button)element;
                    var row = tree.GetItemDataForIndex<Row>(index);
                    button.style.display = row.Kind == RowKind.Slot && row.Slot.Renderer != null ? DisplayStyle.Flex : DisplayStyle.None;
                    button.clickable = new Clickable(() => SelectRenderer(row));
                },
            });
            columns.Add(new Column
            {
                name = "check",
                title = "✓",
                width = 30,
                makeCell = () => new Toggle(),
                bindCell = (element, index) =>
                {
                    var toggle = (Toggle)element;
                    var row = tree.GetItemDataForIndex<Row>(index);
                    if (row.Kind != RowKind.Slot || row.Slot.Renderer == null)
                    {
                        toggle.style.display = DisplayStyle.None;
                        return;
                    }
                    toggle.style.display = DisplayStyle.Flex;
                    toggle.SetValueWithoutNotify(checkedSlots.Contains(SlotKey(row.Slot)));
                    toggle.RegisterValueChangedCallback(e =>
                    {
                        if (e.newValue) checkedSlots.Add(SlotKey(row.Slot));
                        else checkedSlots.Remove(SlotKey(row.Slot));
                    });
                },
            });
            columns.Add(new Column
            {
                name = "actions",
                title = "操作",
                width = 110,
                makeCell = () => new VisualElement { style = { flexDirection = FlexDirection.Row } },
                bindCell = (element, index) => BindActionsCell((VisualElement)element, index),
            });
```

(c) メソッド追加:

```csharp
        void SelectRenderer(Row row)
        {
            var go = row.Slot.Renderer.gameObject;
            var e = Event.current;
            var additive = e != null && (e.control || e.command);
            if (additive)
            {
                var objects = new List<Object>(Selection.objects);
                if (!objects.Contains(go)) objects.Add(go);
                Selection.objects = objects.ToArray();
            }
            else
            {
                Selection.activeGameObject = go;
            }
            EditorGUIUtility.PingObject(go);
        }

        void BindActionsCell(VisualElement cell, int index)
        {
            cell.Clear();
            var row = tree.GetItemDataForIndex<Row>(index);
            if (row.Kind == RowKind.Group)
            {
                var existingHost = FindAOMEHost(row);
                var configured = AOMaterialEditorSetup.HasComponent(existingHost);
                var button = new Button(() => CreateAOMaterialEditor(row)) { text = configured ? "AO ME✓" : "AO ME" };
                var (enabled, reason) = AOMEAvailability(row);
                button.SetEnabled(enabled);
                button.tooltip = !enabled ? reason : configured ? "設定済み（再実行で上書き更新）" : "AO Material Editor を作成";
                cell.Add(button);
            }
            else if (row.Kind == RowKind.Slot && row.Slot.Renderer != null)
            {
                var button = new Button(() => ShowQueuePopup(row)) { text = "Q" };
                button.tooltip = "Render Queue 設定";
                cell.Add(button);
            }
        }

        (bool, string) AOMEAvailability(Row row)
        {
            if (!AOMaterialEditorSetup.IsAvailable) return (false, "aoyon.material-editor が未導入");
            if (row.AvatarRoot == null) return (false, "アバタールートが見つかりません");
            var group = row.Group;
            var isOneTwoTrans = group.Variant.StartsWith("onetrans") || group.Variant.StartsWith("twotrans");
            if (isOneTwoTrans)
            {
                // onetrans/twotrans は preset なしで作るため、3rd 枠が使用済みでも成立するが
                // 未知 family / マテリアル欠損は不可
                if (group.Family == "unknown" || group.Slots.All(s => s.Material == null)) return (false, group.FadeDisabledReason ?? "対象外");
                return (true, null);
            }
            if (!group.CanSetupFade) return (false, group.FadeDisabledReason);
            return (true, null);
        }

        static string AOMEHostSuffix(SlotGroup group)
        {
            var isOneTwoTrans = group.Variant.StartsWith("onetrans") || group.Variant.StartsWith("twotrans");
            var suffix = group.Variant;
            if (!isOneTwoTrans && group.Preset == FadeFrame.Second) suffix += "_2nd";
            if (!isOneTwoTrans && group.Preset == FadeFrame.AlphaMask) suffix += "_alpha_mask";
            return suffix;
        }

        GameObject FindAOMEHost(Row row)
        {
            var t = row.Costume.transform.Find($"trans/{AOMEHostSuffix(row.Group)}");
            return t == null ? null : t.gameObject;
        }

        void CreateAOMaterialEditor(Row row)
        {
            var group = row.Group;
            var isOneTwoTrans = group.Variant.StartsWith("onetrans") || group.Variant.StartsWith("twotrans");
            var suffix = AOMEHostSuffix(group);

            var host = FindOrCreateChild(FindOrCreateChild(row.Costume, "trans"), suffix);

            var slots = group.Slots
                .Where(s => s.Renderer != null)
                .Select(s => new AOMaterialEditorSetup.SlotTarget
                {
                    RendererPath = AvatarUtil.RelativePath(row.AvatarRoot, s.Renderer.gameObject),
                    MaterialIndex = s.SlotIndex,
                })
                .Where(s => !string.IsNullOrEmpty(s.RendererPath))
                .ToList();

            Shader shader = null;
            if (group.NeedsShaderOverride)
            {
                shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(group.TransparentGuid));
                if (shader == null)
                {
                    EditorUtility.DisplayDialog("Costume Dashboard", $"透過版シェーダーが見つかりません (GUID: {group.TransparentGuid})", "OK");
                    return;
                }
            }

            List<PresetProperty> properties;
            if (isOneTwoTrans)
            {
                properties = TransparencyPresets.OneTwoTransOverrides();
            }
            else
            {
                properties = TransparencyPresets.For(group.Preset.Value);
                if (group.Family == "lilToon_multi") properties.Add(TransparencyPresets.TransparentModeOverride());
            }

            AOMaterialEditorSetup.Apply(host, slots, shader, properties);
            Refresh();
        }

        static GameObject FindOrCreateChild(GameObject parent, string name)
        {
            var t = parent.transform.Find(name);
            if (t != null) return t.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            return go;
        }

        void ShowQueuePopup(Row row)
        {
            PopupWindow.Show(new Rect(Event.current != null ? Event.current.mousePosition : Vector2.zero, Vector2.zero),
                new QueuePopup(row.Slot, Refresh));
        }

        class QueuePopup : PopupWindowContent
        {
            readonly SlotInfo slot;
            readonly System.Action onApplied;
            int value;

            public QueuePopup(SlotInfo slot, System.Action onApplied)
            {
                this.slot = slot;
                this.onApplied = onApplied;
                value = RenderQueueSetup.EffectiveQueue(slot.Renderer, slot.SlotIndex, out _);
            }

            public override Vector2 GetWindowSize() => new Vector2(220, 76);

            public override void OnGUI(Rect rect)
            {
                value = EditorGUILayout.IntField("Render Queue", value);
                if (GUILayout.Button("このスロットに設定"))
                {
                    RenderQueueSetup.Set(slot.Renderer, slot.SlotIndex, value);
                    onApplied();
                    editorWindow.Close();
                }
                RenderQueueSetup.EffectiveQueue(slot.Renderer, slot.SlotIndex, out var source);
                using (new EditorGUI.DisabledScope(source == null))
                {
                    if (GUILayout.Button("ChangeRenderQueue を削除"))
                    {
                        RenderQueueSetup.Remove(source);
                        onApplied();
                        editorWindow.Close();
                    }
                }
            }
        }
```

(d) `CreateGUI()` の toolbar に [Toggle Menu作成] を追加:

```csharp
            toolbar.Add(new Button(CreateToggleMenu) { text = "✓ から Toggle Menu作成" });
```

(e) Toggle Menu 作成の実装を追加:

```csharp
        void CreateToggleMenu()
        {
            var slots = CollectCheckedSlots();
            if (slots.Count == 0)
            {
                EditorUtility.DisplayDialog("Costume Dashboard", "✓ 列でメッシュをチェックしてください", "OK");
                return;
            }
            var avatarRoots = slots.Select(s => s.avatarRoot).Distinct().ToList();
            if (avatarRoots.Count != 1 || avatarRoots[0] == null)
            {
                EditorUtility.DisplayDialog("Costume Dashboard", "チェックしたメッシュは同一アバター配下である必要があります", "OK");
                return;
            }
            ToggleMenuCreateDialog.Show(slots[0].costume, avatarRoots[0], slots.Select(s => s.slot).ToList(), () =>
            {
                checkedSlots.Clear();
                Refresh();
            });
        }

        List<(SlotInfo slot, GameObject costume, GameObject avatarRoot)> CollectCheckedSlots()
        {
            var result = new List<(SlotInfo slot, GameObject costume, GameObject avatarRoot)>();
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                var avatarRoot = AvatarUtil.FindAvatarRoot(costume);
                foreach (var slot in MaterialSlotScanner.Scan(costume))
                {
                    if (slot.Renderer != null && checkedSlots.Contains(SlotKey(slot)))
                    {
                        result.Add((slot, costume, avatarRoot));
                    }
                }
            }
            return result;
        }

        class ToggleMenuCreateDialog : EditorWindow
        {
            GameObject costume;
            GameObject avatarRoot;
            List<SlotInfo> slots;
            System.Action onCreated;
            string menuName;
            float transitionSeconds = 1f;

            public static void Show(GameObject costume, GameObject avatarRoot, List<SlotInfo> slots, System.Action onCreated)
            {
                var window = CreateInstance<ToggleMenuCreateDialog>();
                window.titleContent = new GUIContent("Toggle Menu作成");
                window.costume = costume;
                window.avatarRoot = avatarRoot;
                window.slots = slots;
                window.onCreated = onCreated;
                window.menuName = costume.name;
                window.minSize = window.maxSize = new Vector2(320, 100);
                window.ShowUtility();
            }

            void OnGUI()
            {
                menuName = EditorGUILayout.TextField("メニュー名", menuName);
                transitionSeconds = EditorGUILayout.FloatField("フェード秒数", transitionSeconds);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(menuName)))
                {
                    if (GUILayout.Button("作成"))
                    {
                        Create();
                        Close();
                    }
                }
            }

            void Create()
            {
                var togglePaths = slots
                    .Select(s => AvatarUtil.RelativePath(avatarRoot, s.Renderer.gameObject))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct()
                    .ToList();
                var fades = new List<ToggleMenuSetup.FadeTarget>();
                var fadeMeshes = new HashSet<string>();
                foreach (var slot in slots)
                {
                    var frame = slot.FadeCompat?.Recommended;
                    if (frame == null) continue;
                    var meshPath = AvatarUtil.RelativePath(avatarRoot, slot.Renderer.gameObject);
                    if (string.IsNullOrEmpty(meshPath) || !fadeMeshes.Add(meshPath)) continue;
                    fades.Add(new ToggleMenuSetup.FadeTarget { MeshPath = meshPath, Frame = frame.Value });
                }

                var host = new GameObject(menuName);
                host.transform.SetParent(costume.transform, false);
                Undo.RegisterCreatedObjectUndo(host, "Create Toggle Menu");
                ToggleMenuSetup.Create(host, togglePaths, fades, transitionSeconds);
                onCreated();
            }
        }
```

- [ ] **Step 2: コンパイル検証**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe get_logs --logType Error
```

Expected: エラーなし

- [ ] **Step 3: 手動スモーク確認（sandbox）**

VRCAvatarDescriptor 付きアバター配下の衣装で:
- [Select] → Hierarchy がハイライトされ SceneView に輪郭が出る
- グループ行 [AO ME] → `<衣装>/trans/<variant>` に AO ME が付く（aome 未導入ならボタン無効＋tooltip）。Undo で消える
- ✓ を2つ付けて [✓ から Toggle Menu作成] → ダイアログ → 作成で AvatarToggleMenuCreator 付き GameObject ができ、Inspector でフェード設定が見える
- [Q] → ポップアップで 2460 を設定 → Queue 列が `2460*` になる。削除で戻る

- [ ] **Step 4: 既存テストのリグレッション確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` → `"failed":0`

- [ ] **Step 5: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "Select/チェック選択/AO ME・Toggle Menu・Queue アクション接続"
```

---

### Task 12: README と最終確認

**Files:**
- Create: `README.md`
- Create: `LICENSE.txt`（Zlib、`net.narazaka.vrchat.change-render-queue/LICENSE.txt` の本文を年・名前そのままコピー）

**Interfaces:**
- Consumes: 全タスクの成果
- Produces: リリース可能なパッケージ一式

- [ ] **Step 1: README.md を書く**

```markdown
# Costume Dashboard

アバター内の衣装prefabのマテリアル状況を一覧し、透過（着脱フェード）まわりの設定を一括で行う Unity エディタ拡張。

## 機能

- 衣装prefab（複数登録可）配下の全マテリアルスロットを shader family/variant 別に一覧
- lilToon の 2nd / 3rd / AlphaMask 枠の使用状況とフェード駆動枠の推奨を表示
- 同一シェーダーのスロット群に対する AO Material Editor 設定 GameObject の一括作成（要 aoyon.material-editor）
- メッシュの Select ボタン（SceneView ハイライト）
- チェックしたメッシュ群への Avatar Toggle Menu Creator 一括設定（プリセット透過フェード付き）
- Render Queue 一覧と Change Render Queue コンポーネント設定

## 使い方

`Tools/Costume Dashboard` を開き、衣装prefabルートを登録する。アバタールートは親方向に自動検出される。

## 依存

- Avatar Menu Creator for Modular Avatar
- Change Render Queue
- AO Material Editor（任意。未導入時は AO ME 機能が無効化される）

## License

[Zlib License](LICENSE.txt)
```

- [ ] **Step 2: LICENSE.txt を配置**

```bash
cp "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.change-render-queue/LICENSE.txt" "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard/LICENSE.txt"
```

- [ ] **Step 3: 全テスト・コンパイル最終確認**

```bash
./AIBridgeCache/CLI/AIBridgeCLI.exe compile unity
./AIBridgeCache/CLI/AIBridgeCLI.exe get_logs --logType Error
./AIBridgeCache/CLI/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"
```

`Library/CostumeDashboardTestResults.json` → `"failed":0`、エラーログなし

- [ ] **Step 4: コミット**

```bash
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" add -A
git -C "D:/make/devel/vrchat-AVATAR-SANDBOX/Packages/net.narazaka.vrchat.costume-dashboard" commit -m "README と LICENSE"
```

- [ ] **Step 5: ユーザーへの引き渡し**

ユーザーに UI を実際に触ってもらい、UX 調整タスク（スペックの「非スコープ」参照）を次のサイクルとして相談する。GitHub リポジトリ作成・push・VPM 配布はユーザー指示があるまで行わない。
