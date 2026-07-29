# Animator Layer 説明ビュー Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 衣装/アバターの Animator layer の作用（トリガー・対象・構造パターン）を Costume Dashboard 上で説明する「Animator」ビューを追加する。

**Architecture:** AnimatorController を「ノード（状態）+ エッジ（遷移）の平坦なグラフ」C#構造モデルに変換し、機械分類器（boolトグル/int排他選択/float連続）はモデル直接、複雑layerは構造化JSONをLLM API（OpenAI互換/Anthropic）に一括で渡して説明を生成する。スペック: `docs~/superpowers/specs/2026-07-29-animator-layer-explain-design.md`

**Tech Stack:** Unity 2022.3 Editor / UIElements MultiColumnTreeView / Newtonsoft.Json（VRC SDK同梱・auto referenced）/ UnityWebRequest / NUnit EditMode

## Global Constraints

- C# 9.0 互換（C# 10+ 構文禁止: file-scoped namespace、global using、raw string 等）
- Unity オブジェクトの null 判定は `!= null` 明示（`?.` 禁止）
- namespace: `Narazaka.VRChat.CostumeDashboard.Editor`（テストは `.Test`）
- コメントは既存コードに合わせ日本語。複雑ロジック・Unity仕様の罠にのみ書く
- 読み取り専用機能（アセット書き込みなし、Undo不要）
- 新規 asmdef 参照は追加しない（VRC.SDK3A / nadena.dev.modular-avatar.core は参照済み。Newtonsoft.Json は auto referenced）
- テスト実行ハーネス: `../../.aibridge/cli/AIBridgeCLI.exe compile unity` → `../../.aibridge/cli/AIBridgeCLI.exe menu_item --menuPath "Tools/Costume Dashboard/Run Tests"` → 数秒待って `../../Library/CostumeDashboardTestResults.json` を Read（`"failed":0` を確認）。以下「**テスト実行**」と略記
- commit は本パッケージリポジトリ（cwd）で行う。version bump はしない

---

### Task 1: 構造モデルクラス + LayerModelBuilder（グラフ構築）

**Files:**
- Create: `Editor/Core/AnimatorAnalysis/AnimatorLayerModel.cs`
- Create: `Editor/Core/AnimatorAnalysis/LayerModelBuilder.cs`
- Test: `Test/LayerModelBuilderTest.cs`

**Interfaces:**
- Produces: `AnimatorLayerModel`（後続全タスクが使うモデル。フィールドは下記コード参照）、`LayerModelBuilder.Build(AnimatorController controller, int layerIndex, string pathPrefix, SourceInfo source)`
- 擬似ノード: 各 state machine スコープに Entry/Exit、layer ルートに AnyState。`StateNode.Pseudo` で区別
- バインディング（Task 2）・Expression Parameters 突合（Task 3）は後続タスクで埋める。本タスクでは `StateNode.Bindings` は空リスト、`ParameterInfo.InExpressionParameters` は false のまま

- [ ] **Step 1: モデルクラスを書く**

`Editor/Core/AnimatorAnalysis/AnimatorLayerModel.cs`:

```csharp
using System.Collections.Generic;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public enum AnimatorSourceKind { PlayableLayer, MergeAnimator }
    public enum PseudoNodeKind { None, Entry, AnyState, Exit }
    public enum BindingCategory { GameObjectActive, BlendShape, MaterialProperty, MaterialSwap, Transform, Humanoid, Other }

    public class SourceInfo
    {
        public AnimatorSourceKind Kind;
        /// <summary>PlayableLayer: "FX" 等の AnimLayerType 名。MergeAnimator: layerType 名</summary>
        public string LayerType;
        /// <summary>MergeAnimator のみ: コンポーネントのアバタールート相対パス</summary>
        public string ComponentPath;
        public string ControllerName;
    }

    public class ParameterInfo
    {
        public string Name;
        public string Type; // "Bool" / "Int" / "Float" / "Trigger"
        public float DefaultValue;
        public bool InExpressionParameters;
        public bool Synced;
        public bool Saved;
    }

    public class ConditionInfo
    {
        public string Parameter;
        public string Mode; // "If" / "IfNot" / "Greater" / "Less" / "Equals" / "NotEqual"
        public float Threshold;
    }

    public class TransitionEdge
    {
        public int FromId;
        public int ToId;
        public List<ConditionInfo> Conditions = new List<ConditionInfo>();
        public bool HasExitTime;
        public float ExitTime;
        public float Duration;
        /// <summary>Entry から defaultState への暗黙エッジ（条件なし・Entry の条件がどれも成立しないとき）</summary>
        public bool IsDefault;
    }

    public class BlendTreeChildInfo
    {
        public MotionInfo Motion;
        public float Threshold;      // 1D
        public float PositionX, PositionY; // 2D
        public string DirectParameter; // Direct
    }

    public class MotionInfo
    {
        public string ClipName; // クリップのとき。BlendTree のとき null
        public bool IsBlendTree;
        public string BlendType; // "Simple1D" / "Direct" 等
        public string BlendParameter;
        public string BlendParameterY;
        public List<BlendTreeChildInfo> Children;
    }

    public class BehaviourInfo
    {
        public string Type; // 型名 (例 "VRCAvatarParameterDriver")
        /// <summary>要点の要約行 (例 "Set Foo=1", "Random Bar 0..1")</summary>
        public List<string> Details = new List<string>();
    }

    public class BindingInfo
    {
        /// <summary>アバタールート絶対パス。Humanoid はパス無しで null</summary>
        public string Path;
        public string Type; // コンポーネント短型名 "GameObject" / "SkinnedMeshRenderer" 等
        public string Property;
        public BindingCategory Category;
        /// <summary>"1" (定数) / "0→1" (変化) / "MatA→MatB" (差し替え)</summary>
        public string ValueSummary;
    }

    public class StateNode
    {
        public int Id;
        public string Name;
        /// <summary>所属 sub-state machine のパス的表記 ("" = layer ルート, "Sub/Child" 等)。グラフ自体は平坦</summary>
        public string Scope = "";
        public PseudoNodeKind Pseudo;
        public bool IsDefault;
        public MotionInfo Motion;
        public string MotionTimeParameter; // timeParameterActive のときのみ
        public bool WriteDefaults;
        public float Speed;
        public List<BehaviourInfo> Behaviours = new List<BehaviourInfo>();
        public List<BindingInfo> Bindings = new List<BindingInfo>();
    }

    public class AnimatorLayerModel
    {
        public SourceInfo Source;
        public string LayerName;
        public int LayerIndex;
        /// <summary>layer 0 は Unity 仕様で常に実効 weight 1 なので補正済みの値</summary>
        public float Weight;
        public bool IsAdditive;
        public string MaskName;
        public List<ParameterInfo> Parameters = new List<ParameterInfo>();
        public List<StateNode> States = new List<StateNode>();
        public List<TransitionEdge> Transitions = new List<TransitionEdge>();
        /// <summary>このlayerが作用する登録衣装名（Task 3 で設定）</summary>
        public List<string> AffectedCostumes = new List<string>();
    }
}
```

- [ ] **Step 2: 失敗するテストを書く**

`Test/LayerModelBuilderTest.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class LayerModelBuilderTest
    {
        AnimatorController controller;

        [SetUp]
        public void SetUp()
        {
            // アセット保存せずメモリ上で構築（テスト後の掃除不要）
            controller = new AnimatorController();
            controller.AddLayer("TestLayer");
            controller.AddParameter("Toggle", AnimatorControllerParameterType.Bool);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(controller);
        }

        SourceInfo Src() => new SourceInfo { Kind = AnimatorSourceKind.MergeAnimator, LayerType = "FX", ControllerName = "TestController" };

        [Test]
        public void Build_TwoStateMutualToggle_BuildsGraph()
        {
            var sm = controller.layers[0].stateMachine;
            var on = sm.AddState("ON");
            var off = sm.AddState("OFF");
            sm.defaultState = off;
            var toOn = off.AddTransition(on);
            toOn.AddCondition(AnimatorConditionMode.If, 0, "Toggle");
            var toOff = on.AddTransition(off);
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0, "Toggle");

            var model = LayerModelBuilder.Build(controller, 0, "", Src());

            Assert.That(model.LayerName, Is.EqualTo("TestLayer"));
            Assert.That(model.Weight, Is.EqualTo(1f)); // layer 0 は常に実効 1
            var real = model.States.Where(s => s.Pseudo == PseudoNodeKind.None).ToList();
            Assert.That(real.Select(s => s.Name), Is.EquivalentTo(new[] { "ON", "OFF" }));
            Assert.That(real.Single(s => s.Name == "OFF").IsDefault, Is.True);
            // 擬似ノード: ルートの Entry/Exit/AnyState
            Assert.That(model.States.Count(s => s.Pseudo == PseudoNodeKind.Entry), Is.EqualTo(1));
            Assert.That(model.States.Count(s => s.Pseudo == PseudoNodeKind.AnyState), Is.EqualTo(1));
            // エッジ: ON→OFF, OFF→ON + Entry→OFF (default)
            var onId = real.Single(s => s.Name == "ON").Id;
            var offId = real.Single(s => s.Name == "OFF").Id;
            var e = model.Transitions.Single(t => t.FromId == offId && t.ToId == onId);
            Assert.That(e.Conditions.Single().Parameter, Is.EqualTo("Toggle"));
            Assert.That(e.Conditions.Single().Mode, Is.EqualTo("If"));
            var entryId = model.States.Single(s => s.Pseudo == PseudoNodeKind.Entry).Id;
            Assert.That(model.Transitions.Any(t => t.FromId == entryId && t.ToId == offId && t.IsDefault), Is.True);
            // 参照パラメーター
            var p = model.Parameters.Single();
            Assert.That(p.Name, Is.EqualTo("Toggle"));
            Assert.That(p.Type, Is.EqualTo("Bool"));
        }

        [Test]
        public void Build_ExitAndSubStateMachine_ResolvesPseudoNodes()
        {
            var sm = controller.layers[0].stateMachine;
            var a = sm.AddState("A");
            sm.defaultState = a;
            var exit = a.AddExitTransition();
            exit.AddCondition(AnimatorConditionMode.IfNot, 0, "Toggle");
            var sub = sm.AddStateMachine("Sub");
            var b = sub.AddState("B");
            var toSub = a.AddTransition(sub);
            toSub.AddCondition(AnimatorConditionMode.If, 0, "Toggle");

            var model = LayerModelBuilder.Build(controller, 0, "", Src());

            var bNode = model.States.Single(s => s.Name == "B");
            Assert.That(bNode.Scope, Is.EqualTo("Sub"));
            // Sub スコープにも Entry/Exit 擬似ノードがある
            Assert.That(model.States.Any(s => s.Pseudo == PseudoNodeKind.Entry && s.Scope == "Sub"), Is.True);
            // A→(root Exit)、A→(Sub の Entry) が張られる
            var aId = model.States.Single(s => s.Name == "A").Id;
            var rootExit = model.States.Single(s => s.Pseudo == PseudoNodeKind.Exit && s.Scope == "").Id;
            var subEntry = model.States.Single(s => s.Pseudo == PseudoNodeKind.Entry && s.Scope == "Sub").Id;
            Assert.That(model.Transitions.Any(t => t.FromId == aId && t.ToId == rootExit), Is.True);
            Assert.That(model.Transitions.Any(t => t.FromId == aId && t.ToId == subEntry), Is.True);
        }

        [Test]
        public void Build_MotionTimeAndBlendTree_CapturesMotionInfo()
        {
            controller.AddParameter("Radial", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;
            var s = sm.AddState("S");
            s.timeParameterActive = true;
            s.timeParameter = "Radial";
            var tree = new BlendTree { blendType = BlendTreeType.Simple1D, blendParameter = "Radial" };
            var clip = new AnimationClip { name = "clip0" };
            tree.AddChild(clip, 0f);
            s.motion = tree;

            var model = LayerModelBuilder.Build(controller, 0, "", Src());

            var node = model.States.Single(x => x.Name == "S");
            Assert.That(node.MotionTimeParameter, Is.EqualTo("Radial"));
            Assert.That(node.Motion.IsBlendTree, Is.True);
            Assert.That(node.Motion.BlendType, Is.EqualTo("Simple1D"));
            Assert.That(node.Motion.BlendParameter, Is.EqualTo("Radial"));
            Assert.That(node.Motion.Children.Single().Motion.ClipName, Is.EqualTo("clip0"));
            // BlendTree の駆動パラメーターも Parameters に入る
            Assert.That(model.Parameters.Any(p => p.Name == "Radial" && p.Type == "Float"), Is.True);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(tree);
        }
    }
}
```

- [ ] **Step 3: コンパイルしてテストが失敗することを確認**

`LayerModelBuilder` 未実装なのでコンパイルエラーになる（`AnimatorLayerModel.cs` だけ先に書いたのでモデル参照は通る）。Step 4 実装後にテスト実行へ進む。

- [ ] **Step 4: LayerModelBuilder を実装**

`Editor/Core/AnimatorAnalysis/LayerModelBuilder.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>AnimatorController の1layerを平坦なグラフ（ノード+エッジ）のモデルへ変換する。
    /// 状態遷移は樹状でなく循環を含む任意グラフなので、状態にIDを振りエッジリストで表現する</summary>
    public static class LayerModelBuilder
    {
        public static AnimatorLayerModel Build(AnimatorController controller, int layerIndex, string pathPrefix, SourceInfo source)
        {
            var layer = controller.layers[layerIndex];
            var model = new AnimatorLayerModel
            {
                Source = source,
                LayerName = layer.name,
                LayerIndex = layerIndex,
                // Unity 仕様: layer 0 の weight は常に 1 として扱われる
                Weight = layerIndex == 0 ? 1f : layer.defaultWeight,
                IsAdditive = layer.blendingMode == AnimatorLayerBlendingMode.Additive,
                MaskName = layer.avatarMask != null ? layer.avatarMask.name : null,
            };

            var ctx = new Context { Model = model, PathPrefix = pathPrefix };
            CollectNodes(ctx, layer.stateMachine, "");
            // AnyState はルート state machine にのみ存在する
            ctx.AnyStateId = AddPseudo(ctx, PseudoNodeKind.AnyState, "");
            CollectEdges(ctx, layer.stateMachine, "");

            CollectParameters(ctx, controller);
            return model;
        }

        class Context
        {
            public AnimatorLayerModel Model;
            public string PathPrefix;
            public int NextId;
            public readonly Dictionary<AnimatorState, int> StateIds = new Dictionary<AnimatorState, int>();
            public readonly Dictionary<AnimatorStateMachine, (int Entry, int Exit)> SmIds = new Dictionary<AnimatorStateMachine, (int, int)>();
            public int AnyStateId;
            public readonly HashSet<string> UsedParameters = new HashSet<string>();
        }

        static int AddPseudo(Context ctx, PseudoNodeKind kind, string scope)
        {
            var node = new StateNode { Id = ctx.NextId++, Name = kind.ToString(), Scope = scope, Pseudo = kind };
            ctx.Model.States.Add(node);
            return node.Id;
        }

        static void CollectNodes(Context ctx, AnimatorStateMachine sm, string scope)
        {
            ctx.SmIds[sm] = (AddPseudo(ctx, PseudoNodeKind.Entry, scope), AddPseudo(ctx, PseudoNodeKind.Exit, scope));
            foreach (var child in sm.states)
            {
                var state = child.state;
                var node = new StateNode
                {
                    Id = ctx.NextId++,
                    Name = state.name,
                    Scope = scope,
                    IsDefault = sm.defaultState == state,
                    Motion = BuildMotion(ctx, state.motion),
                    MotionTimeParameter = state.timeParameterActive ? state.timeParameter : null,
                    WriteDefaults = state.writeDefaultValues,
                    Speed = state.speed,
                    Behaviours = state.behaviours.Where(b => b != null).Select(BuildBehaviour).ToList(),
                };
                if (node.MotionTimeParameter != null) ctx.UsedParameters.Add(node.MotionTimeParameter);
                ctx.StateIds[state] = node.Id;
                ctx.Model.States.Add(node);
            }
            foreach (var child in sm.stateMachines)
            {
                CollectNodes(ctx, child.stateMachine, scope == "" ? child.stateMachine.name : scope + "/" + child.stateMachine.name);
            }
        }

        static MotionInfo BuildMotion(Context ctx, Motion motion)
        {
            if (motion == null) return null;
            if (motion is BlendTree tree)
            {
                var info = new MotionInfo
                {
                    IsBlendTree = true,
                    BlendType = tree.blendType.ToString(),
                    BlendParameter = tree.blendType == BlendTreeType.Direct ? null : tree.blendParameter,
                    BlendParameterY = tree.blendType == BlendTreeType.FreeformCartesian2D || tree.blendType == BlendTreeType.FreeformDirectional2D || tree.blendType == BlendTreeType.SimpleDirectional2D ? tree.blendParameterY : null,
                    Children = new List<BlendTreeChildInfo>(),
                };
                if (info.BlendParameter != null) ctx.UsedParameters.Add(info.BlendParameter);
                if (info.BlendParameterY != null) ctx.UsedParameters.Add(info.BlendParameterY);
                foreach (var child in tree.children)
                {
                    var c = new BlendTreeChildInfo
                    {
                        Motion = BuildMotion(ctx, child.motion),
                        Threshold = child.threshold,
                        PositionX = child.position.x,
                        PositionY = child.position.y,
                        DirectParameter = tree.blendType == BlendTreeType.Direct ? child.directBlendParameter : null,
                    };
                    if (c.DirectParameter != null) ctx.UsedParameters.Add(c.DirectParameter);
                    info.Children.Add(c);
                }
                return info;
            }
            return new MotionInfo { ClipName = motion.name };
        }

        static BehaviourInfo BuildBehaviour(StateMachineBehaviour behaviour)
        {
            var info = new BehaviourInfo { Type = behaviour.GetType().Name };
            if (behaviour is VRCAvatarParameterDriver driver)
            {
                foreach (var p in driver.parameters)
                {
                    switch (p.type)
                    {
                        case VRC_AvatarParameterDriver.ChangeType.Set:
                            info.Details.Add($"Set {p.name}={p.value}"); break;
                        case VRC_AvatarParameterDriver.ChangeType.Add:
                            info.Details.Add($"Add {p.name}+={p.value}"); break;
                        case VRC_AvatarParameterDriver.ChangeType.Random:
                            info.Details.Add($"Random {p.name} {p.valueMin}..{p.valueMax}"); break;
                        case VRC_AvatarParameterDriver.ChangeType.Copy:
                            info.Details.Add($"Copy {p.source}→{p.name}"); break;
                    }
                }
            }
            return info;
        }

        static void CollectEdges(Context ctx, AnimatorStateMachine sm, string scope)
        {
            var (entryId, exitId) = ctx.SmIds[sm];
            // Entry の明示遷移 + defaultState への暗黙エッジ
            foreach (var t in sm.entryTransitions)
            {
                AddEdge(ctx, entryId, ResolveTarget(ctx, sm, t.destinationState, t.destinationStateMachine, t.isExit), t.conditions, false, 0, 0, false);
            }
            if (sm.defaultState != null && ctx.StateIds.TryGetValue(sm.defaultState, out var defId))
            {
                ctx.Model.Transitions.Add(new TransitionEdge { FromId = entryId, ToId = defId, IsDefault = true });
            }
            // AnyState はルートのみ
            if (scope == "")
            {
                foreach (var t in sm.anyStateTransitions)
                {
                    AddEdge(ctx, ctx.AnyStateId, ResolveTarget(ctx, sm, t.destinationState, t.destinationStateMachine, t.isExit), t.conditions, t.hasExitTime, t.exitTime, t.duration, false);
                }
            }
            // 各状態からの遷移
            foreach (var child in sm.states)
            {
                foreach (var t in child.state.transitions)
                {
                    AddEdge(ctx, ctx.StateIds[child.state], ResolveTarget(ctx, sm, t.destinationState, t.destinationStateMachine, t.isExit), t.conditions, t.hasExitTime, t.exitTime, t.duration, false);
                }
            }
            // sub-state machine 自体からの遷移（子smが Exit に達したとき評価される）→ 子smの Exit 擬似ノードから張る
            foreach (var child in sm.stateMachines)
            {
                var childExit = ctx.SmIds[child.stateMachine].Exit;
                foreach (var t in sm.GetStateMachineTransitions(child.stateMachine))
                {
                    AddEdge(ctx, childExit, ResolveTarget(ctx, sm, t.destinationState, t.destinationStateMachine, t.isExit), t.conditions, false, 0, 0, false);
                }
                CollectEdges(ctx, child.stateMachine, scope == "" ? child.stateMachine.name : scope + "/" + child.stateMachine.name);
            }
        }

        /// <summary>遷移先を状態ID / sub-sm の Entry / 現スコープの Exit のいずれかに解決する</summary>
        static int ResolveTarget(Context ctx, AnimatorStateMachine currentSm, AnimatorState destState, AnimatorStateMachine destSm, bool isExit)
        {
            if (isExit) return ctx.SmIds[currentSm].Exit;
            if (destState != null) return ctx.StateIds[destState];
            if (destSm != null) return ctx.SmIds[destSm].Entry;
            return ctx.SmIds[currentSm].Exit;
        }

        static void AddEdge(Context ctx, int fromId, int toId, AnimatorCondition[] conditions, bool hasExitTime, float exitTime, float duration, bool isDefault)
        {
            var edge = new TransitionEdge { FromId = fromId, ToId = toId, HasExitTime = hasExitTime, ExitTime = exitTime, Duration = duration, IsDefault = isDefault };
            foreach (var c in conditions)
            {
                edge.Conditions.Add(new ConditionInfo { Parameter = c.parameter, Mode = c.mode.ToString(), Threshold = c.threshold });
                ctx.UsedParameters.Add(c.parameter);
            }
            ctx.Model.Transitions.Add(edge);
        }

        static void CollectParameters(Context ctx, AnimatorController controller)
        {
            foreach (var p in controller.parameters)
            {
                if (!ctx.UsedParameters.Contains(p.name)) continue;
                ctx.Model.Parameters.Add(new ParameterInfo
                {
                    Name = p.name,
                    Type = p.type.ToString(),
                    DefaultValue = p.type == UnityEngine.AnimatorControllerParameterType.Bool ? (p.defaultBool ? 1f : 0f)
                        : p.type == UnityEngine.AnimatorControllerParameterType.Int ? p.defaultInt
                        : p.defaultFloat,
                });
            }
        }
    }
}
```

注意: `AnimatorControllerParameterType` は `UnityEngine` 名前空間（`UnityEditor.Animations` ではない）。

- [ ] **Step 5: テスト実行して全パスを確認**

**テスト実行**（Global Constraints 記載の手順）。Expected: `"failed":0`

- [ ] **Step 6: Commit**

```bash
git add Editor/Core/AnimatorAnalysis Test/LayerModelBuilderTest.cs
git commit -m "Animator解析: layer構造モデルとグラフビルダー"
```

（.meta ファイルは Unity が生成するので `git add` 前に compile を済ませておくこと。以後のタスクも同様に新規ファイルの .meta を含めて add する）

---

### Task 2: バインディング展開（クリップ/BlendTree → BindingInfo）

**Files:**
- Create: `Editor/Core/AnimatorAnalysis/BindingExtractor.cs`
- Modify: `Editor/Core/AnimatorAnalysis/LayerModelBuilder.cs`（状態ノード構築後にバインディングを埋める）
- Test: `Test/BindingExtractorTest.cs`

**Interfaces:**
- Consumes: `StateNode.Motion`（Task 1）ではなく元の `Motion`（Unity オブジェクト）から抽出するため、`LayerModelBuilder` の状態収集ループ内で呼ぶ
- Produces: `BindingExtractor.Extract(Motion motion, string pathPrefix)` → `List<BindingInfo>`（クリップ/BlendTree配下の全カーブを展開。重複 path+type+property は値要約をマージせず初出のみ）
- `pathPrefix`: Merge Animator 相対パスをアバタールート絶対パスへ解決するための接頭辞（"" or "Outfits/Sailor" 形式、末尾スラッシュなし）

- [ ] **Step 1: 失敗するテストを書く**

`Test/BindingExtractorTest.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class BindingExtractorTest
    {
        AnimationClip clip;

        [SetUp]
        public void SetUp()
        {
            clip = new AnimationClip { name = "test" };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void Extract_GameObjectActive_ConstantValue()
        {
            clip.SetCurve("Costume/Top", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 1));
            var bindings = BindingExtractor.Extract(clip, "");
            var b = bindings.Single();
            Assert.That(b.Path, Is.EqualTo("Costume/Top"));
            Assert.That(b.Type, Is.EqualTo("GameObject"));
            Assert.That(b.Category, Is.EqualTo(BindingCategory.GameObjectActive));
            Assert.That(b.ValueSummary, Is.EqualTo("1"));
        }

        [Test]
        public void Extract_PathPrefix_Applied()
        {
            clip.SetCurve("Top", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 0));
            var bindings = BindingExtractor.Extract(clip, "Outfits/Sailor");
            Assert.That(bindings.Single().Path, Is.EqualTo("Outfits/Sailor/Top"));
        }

        [Test]
        public void Extract_Categories()
        {
            clip.SetCurve("Mesh", typeof(SkinnedMeshRenderer), "blendShape.Big", AnimationCurve.Linear(0, 0, 1, 100));
            clip.SetCurve("Mesh", typeof(SkinnedMeshRenderer), "material._Color.a", AnimationCurve.Linear(0, 0, 1, 1));
            clip.SetCurve("Obj", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0, 1, 2));
            // Humanoid: パス空 + Animator 型
            clip.SetCurve("", typeof(Animator), "LeftHand.Thumb.1 Stretched", AnimationCurve.Constant(0, 1, 0.5f));
            var bindings = BindingExtractor.Extract(clip, "");
            Assert.That(bindings.Single(b => b.Property == "blendShape.Big").Category, Is.EqualTo(BindingCategory.BlendShape));
            Assert.That(bindings.Single(b => b.Property == "blendShape.Big").ValueSummary, Is.EqualTo("0→100"));
            Assert.That(bindings.Single(b => b.Property == "material._Color.a").Category, Is.EqualTo(BindingCategory.MaterialProperty));
            Assert.That(bindings.Single(b => b.Property == "m_LocalPosition.x").Category, Is.EqualTo(BindingCategory.Transform));
            var humanoid = bindings.Single(b => b.Category == BindingCategory.Humanoid);
            Assert.That(humanoid.Path, Is.Null);
            Assert.That(humanoid.Property, Is.EqualTo("LeftHand.Thumb.1 Stretched"));
        }

        [Test]
        public void Extract_MaterialSwap_FromObjectReferenceCurve()
        {
            var mat1 = new Material(Shader.Find("Standard")) { name = "MatA" };
            var mat2 = new Material(Shader.Find("Standard")) { name = "MatB" };
            var binding = EditorCurveBinding.PPtrCurve("Mesh", typeof(MeshRenderer), "m_Materials.Array.data[0]");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, new[]
            {
                new ObjectReferenceKeyframe { time = 0, value = mat1 },
                new ObjectReferenceKeyframe { time = 1, value = mat2 },
            });
            var bindings = BindingExtractor.Extract(clip, "");
            var b = bindings.Single();
            Assert.That(b.Category, Is.EqualTo(BindingCategory.MaterialSwap));
            Assert.That(b.ValueSummary, Is.EqualTo("MatA→MatB"));
            Object.DestroyImmediate(mat1);
            Object.DestroyImmediate(mat2);
        }
    }
}
```

- [ ] **Step 2: BindingExtractor を実装**

`Editor/Core/AnimatorAnalysis/BindingExtractor.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>クリップ/BlendTree 配下の全カーブを BindingInfo（解決済みパス×型×プロパティ×値要約）へ展開する</summary>
    public static class BindingExtractor
    {
        public static List<BindingInfo> Extract(Motion motion, string pathPrefix)
        {
            var result = new List<BindingInfo>();
            var seen = new HashSet<(string, string, string)>();
            ExtractInto(motion, pathPrefix, result, seen);
            return result;
        }

        static void ExtractInto(Motion motion, string pathPrefix, List<BindingInfo> result, HashSet<(string, string, string)> seen)
        {
            if (motion == null) return;
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children) ExtractInto(child.motion, pathPrefix, result, seen);
                return;
            }
            if (!(motion is AnimationClip clip)) return;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!seen.Add((binding.path, binding.type.Name, binding.propertyName))) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                result.Add(Build(binding, pathPrefix, SummarizeCurve(curve)));
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (!seen.Add((binding.path, binding.type.Name, binding.propertyName))) continue;
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                result.Add(Build(binding, pathPrefix, SummarizeObjectCurve(keys)));
            }
        }

        static BindingInfo Build(EditorCurveBinding binding, string pathPrefix, string valueSummary)
        {
            var category = Categorize(binding);
            return new BindingInfo
            {
                // Humanoid（Animator 直・パス空）はパス概念がないので null のまま
                Path = category == BindingCategory.Humanoid ? null : ResolvePath(binding.path, pathPrefix),
                Type = binding.type.Name,
                Property = binding.propertyName,
                Category = category,
                ValueSummary = valueSummary,
            };
        }

        static string ResolvePath(string path, string pathPrefix)
        {
            if (string.IsNullOrEmpty(pathPrefix)) return path;
            return string.IsNullOrEmpty(path) ? pathPrefix : pathPrefix + "/" + path;
        }

        static BindingCategory Categorize(EditorCurveBinding binding)
        {
            if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive") return BindingCategory.GameObjectActive;
            if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path)) return BindingCategory.Humanoid;
            if (binding.propertyName.StartsWith("blendShape.")) return BindingCategory.BlendShape;
            if (binding.propertyName.StartsWith("material.")) return BindingCategory.MaterialProperty;
            if (binding.propertyName.StartsWith("m_Materials.Array")) return BindingCategory.MaterialSwap;
            if (binding.type == typeof(Transform)) return BindingCategory.Transform;
            return BindingCategory.Other;
        }

        static string SummarizeCurve(AnimationCurve curve)
        {
            if (curve == null || curve.keys.Length == 0) return "";
            var first = curve.keys[0].value;
            var last = curve.keys[curve.keys.Length - 1].value;
            if (curve.keys.All(k => Mathf.Approximately(k.value, first))) return Format(first);
            return Format(first) + "→" + Format(last);
        }

        static string SummarizeObjectCurve(ObjectReferenceKeyframe[] keys)
        {
            if (keys == null || keys.Length == 0) return "";
            var names = keys.Select(k => k.value != null ? k.value.name : "(なし)").ToList();
            if (names.Distinct().Count() == 1) return names[0];
            return string.Join("→", names);
        }

        static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 3: LayerModelBuilder に組み込む**

`LayerModelBuilder.CollectNodes` の状態ノード構築部で、`Bindings` を埋める（`node.Motion = ...` の後）:

```csharp
                    Behaviours = state.behaviours.Where(b => b != null).Select(BuildBehaviour).ToList(),
                };
                node.Bindings = BindingExtractor.Extract(state.motion, ctx.PathPrefix);
```

- [ ] **Step 4: テスト実行して全パスを確認**

**テスト実行**。Expected: `"failed":0`（Task 1 のテストも含めて）

- [ ] **Step 5: Commit**

```bash
git add Editor/Core/AnimatorAnalysis Test/BindingExtractorTest.cs
git commit -m "Animator解析: バインディング展開（値要約・カテゴリ・パス解決）"
```

---

### Task 3: AnimatorSourceCollector（ソース列挙・ExpressionParameters突合・作用先衣装）

**Files:**
- Create: `Editor/Core/AnimatorAnalysis/AnimatorSourceCollector.cs`
- Test: `Test/AnimatorSourceCollectorTest.cs`

**Interfaces:**
- Consumes: `LayerModelBuilder.Build(controller, layerIndex, pathPrefix, source)`（Task 1）、`AvatarUtil.RelativePath` / `AvatarUtil.IsEditorOnly`（既存）
- Produces:
  - `class AnimatorSource { public SourceInfo Info; public UnityEditor.Animations.AnimatorController Controller; public string PathPrefix; public string Warning; }`（Warning != null のときは解析不能ソース。Controller は null）
  - `AnimatorSourceCollector.CollectCostumeSources(GameObject avatarRoot, IEnumerable<GameObject> costumeRoots)` → `List<AnimatorSource>`
  - `AnimatorSourceCollector.CollectAvatarSources(GameObject avatarRoot, IEnumerable<GameObject> costumeRoots)` → `List<AnimatorSource>`（Descriptor playable layers + 衣装外の Merge Animator）
  - `AnimatorSourceCollector.BuildModels(AnimatorSource source)` → `List<AnimatorLayerModel>`（全layerを Build）
  - `AnimatorSourceCollector.AnnotateExpressionParameters(AnimatorLayerModel model, VRCExpressionParameters parameters)`
  - `AnimatorSourceCollector.AnnotateAffectedCostumes(AnimatorLayerModel model, GameObject avatarRoot, IEnumerable<GameObject> costumeRoots)`

- [ ] **Step 1: 失敗するテストを書く**

`Test/AnimatorSourceCollectorTest.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class AnimatorSourceCollectorTest
    {
        GameObject avatar;
        GameObject costume;
        AnimatorController controller;

        [SetUp]
        public void SetUp()
        {
            avatar = new GameObject("Avatar");
            avatar.AddComponent<VRCAvatarDescriptor>();
            costume = new GameObject("Sailor");
            costume.transform.SetParent(avatar.transform);
            controller = new AnimatorController();
            controller.AddLayer("L1");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar);
            Object.DestroyImmediate(controller);
        }

        [Test]
        public void CollectCostumeSources_RelativePathMode_ResolvesPrefix()
        {
            var holder = new GameObject("Anim");
            holder.transform.SetParent(costume.transform);
            var merge = holder.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
            // relativePathRoot 未設定 → コンポーネントのオブジェクト自身が基準

            var sources = AnimatorSourceCollector.CollectCostumeSources(avatar, new[] { costume });

            var s = sources.Single();
            Assert.That(s.Info.Kind, Is.EqualTo(AnimatorSourceKind.MergeAnimator));
            Assert.That(s.Info.LayerType, Is.EqualTo("FX"));
            Assert.That(s.Info.ComponentPath, Is.EqualTo("Sailor/Anim"));
            Assert.That(s.PathPrefix, Is.EqualTo("Sailor/Anim"));
            Assert.That(s.Warning, Is.Null);
        }

        [Test]
        public void CollectCostumeSources_AbsolutePathMode_EmptyPrefix()
        {
            var merge = costume.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            var sources = AnimatorSourceCollector.CollectCostumeSources(avatar, new[] { costume });
            Assert.That(sources.Single().PathPrefix, Is.EqualTo(""));
        }

        [Test]
        public void CollectCostumeSources_NoController_Warns()
        {
            costume.AddComponent<ModularAvatarMergeAnimator>();
            var sources = AnimatorSourceCollector.CollectCostumeSources(avatar, new[] { costume });
            Assert.That(sources.Single().Warning, Is.Not.Null);
            Assert.That(sources.Single().Controller, Is.Null);
        }

        [Test]
        public void CollectAvatarSources_DescriptorAndOutsideMergeAnimators()
        {
            // Descriptor FX 枠
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    animatorController = controller,
                    isDefault = false,
                },
            };
            descriptor.specialAnimationLayers = new VRCAvatarDescriptor.CustomAnimLayer[0];
            // 衣装外の Merge Animator（アバター直下ギミック）
            var gimmick = new GameObject("Gimmick");
            gimmick.transform.SetParent(avatar.transform);
            var merge = gimmick.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            // 衣装配下の Merge Animator は avatar 側には入らない
            var inCostume = costume.AddComponent<ModularAvatarMergeAnimator>();
            inCostume.animator = controller;

            var sources = AnimatorSourceCollector.CollectAvatarSources(avatar, new[] { costume });

            Assert.That(sources.Count, Is.EqualTo(2));
            Assert.That(sources.Any(s => s.Info.Kind == AnimatorSourceKind.PlayableLayer && s.Info.LayerType == "FX"), Is.True);
            Assert.That(sources.Any(s => s.Info.Kind == AnimatorSourceKind.MergeAnimator && s.Info.ComponentPath == "Gimmick"), Is.True);
        }

        [Test]
        public void AnnotateAffectedCostumes_MatchesByPathPrefix()
        {
            var model = new AnimatorLayerModel();
            var state = new StateNode { Id = 0, Name = "S" };
            state.Bindings.Add(new BindingInfo { Path = "Sailor/Top", Category = BindingCategory.GameObjectActive });
            model.States.Add(state);

            AnimatorSourceCollector.AnnotateAffectedCostumes(model, avatar, new[] { costume });

            Assert.That(model.AffectedCostumes, Is.EquivalentTo(new[] { "Sailor" }));
        }

        [Test]
        public void AnnotateExpressionParameters_SetsFlags()
        {
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = "Toggle",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    saved = true,
                    networkSynced = true,
                },
            };
            var model = new AnimatorLayerModel();
            model.Parameters.Add(new ParameterInfo { Name = "Toggle", Type = "Bool" });
            model.Parameters.Add(new ParameterInfo { Name = "Internal", Type = "Bool" });

            AnimatorSourceCollector.AnnotateExpressionParameters(model, parameters);

            var toggle = model.Parameters.Single(p => p.Name == "Toggle");
            Assert.That(toggle.InExpressionParameters, Is.True);
            Assert.That(toggle.Saved, Is.True);
            Assert.That(toggle.Synced, Is.True);
            Assert.That(model.Parameters.Single(p => p.Name == "Internal").InExpressionParameters, Is.False);
            Object.DestroyImmediate(parameters);
        }
    }
}
```

- [ ] **Step 2: AnimatorSourceCollector を実装**

`Editor/Core/AnimatorAnalysis/AnimatorSourceCollector.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public class AnimatorSource
    {
        public SourceInfo Info;
        public AnimatorController Controller;
        public string PathPrefix;
        /// <summary>解析不能理由（Controller 未設定 / AnimatorOverrideController 等）。非 null のとき Controller は null</summary>
        public string Warning;
    }

    /// <summary>解析対象の AnimatorController ソースを列挙する。
    /// 既定は登録衣装配下の MA Merge Animator のみ、オプションで Descriptor playable layers + 衣装外 Merge Animator</summary>
    public static class AnimatorSourceCollector
    {
        public static List<AnimatorSource> CollectCostumeSources(GameObject avatarRoot, IEnumerable<GameObject> costumeRoots)
        {
            var result = new List<AnimatorSource>();
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                foreach (var merge in costume.GetComponentsInChildren<ModularAvatarMergeAnimator>(true))
                {
                    if (AvatarUtil.IsEditorOnly(merge.gameObject, avatarRoot)) continue;
                    result.Add(FromMergeAnimator(avatarRoot, merge));
                }
            }
            return result;
        }

        public static List<AnimatorSource> CollectAvatarSources(GameObject avatarRoot, IEnumerable<GameObject> costumeRoots)
        {
            var result = new List<AnimatorSource>();
            if (avatarRoot == null) return result;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null)
            {
                var layers = (descriptor.baseAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
                    .Concat(descriptor.specialAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0]);
                foreach (var layer in layers)
                {
                    if (layer.isDefault || layer.animatorController == null) continue;
                    var source = new AnimatorSource
                    {
                        Info = new SourceInfo
                        {
                            Kind = AnimatorSourceKind.PlayableLayer,
                            LayerType = layer.type.ToString(),
                            ControllerName = layer.animatorController.name,
                        },
                        PathPrefix = "",
                    };
                    if (layer.animatorController is AnimatorController ac) source.Controller = ac;
                    else source.Warning = "AnimatorOverrideController は解析対象外";
                    result.Add(source);
                }
            }
            // 衣装外の Merge Animator（衣装配下は CollectCostumeSources 側）
            var costumeSet = costumeRoots.Where(c => c != null).ToList();
            foreach (var merge in avatarRoot.GetComponentsInChildren<ModularAvatarMergeAnimator>(true))
            {
                if (AvatarUtil.IsEditorOnly(merge.gameObject, avatarRoot)) continue;
                if (costumeSet.Any(c => merge.transform == c.transform || merge.transform.IsChildOf(c.transform))) continue;
                result.Add(FromMergeAnimator(avatarRoot, merge));
            }
            return result;
        }

        static AnimatorSource FromMergeAnimator(GameObject avatarRoot, ModularAvatarMergeAnimator merge)
        {
            var source = new AnimatorSource
            {
                Info = new SourceInfo
                {
                    Kind = AnimatorSourceKind.MergeAnimator,
                    LayerType = merge.layerType.ToString(),
                    ComponentPath = AvatarUtil.RelativePath(avatarRoot, merge.gameObject),
                    ControllerName = merge.animator != null ? merge.animator.name : null,
                },
            };
            if (merge.animator == null)
            {
                source.Warning = "AnimatorController 未設定";
            }
            else if (merge.animator is AnimatorController ac)
            {
                source.Controller = ac;
                source.PathPrefix = ResolvePathPrefix(avatarRoot, merge);
            }
            else
            {
                source.Warning = "AnimatorOverrideController は解析対象外";
            }
            return source;
        }

        /// <summary>Relative パスモードの基準オブジェクト: relativePathRoot 設定時はそれ、未設定はコンポーネントのオブジェクト自身（MA 仕様）</summary>
        static string ResolvePathPrefix(GameObject avatarRoot, ModularAvatarMergeAnimator merge)
        {
            if (merge.pathMode == MergeAnimatorPathMode.Absolute) return "";
            GameObject rootGo = null;
            if (merge.relativePathRoot != null) rootGo = merge.relativePathRoot.Get(merge);
            if (rootGo == null) rootGo = merge.gameObject;
            return AvatarUtil.RelativePath(avatarRoot, rootGo) ?? "";
        }

        public static List<AnimatorLayerModel> BuildModels(AnimatorSource source)
        {
            var result = new List<AnimatorLayerModel>();
            if (source.Controller == null) return result;
            for (var i = 0; i < source.Controller.layers.Length; i++)
            {
                result.Add(LayerModelBuilder.Build(source.Controller, i, source.PathPrefix ?? "", source.Info));
            }
            return result;
        }

        public static void AnnotateExpressionParameters(AnimatorLayerModel model, VRCExpressionParameters parameters)
        {
            if (parameters == null || parameters.parameters == null) return;
            foreach (var p in model.Parameters)
            {
                var found = parameters.parameters.FirstOrDefault(x => x != null && x.name == p.Name);
                if (found == null) continue;
                p.InExpressionParameters = true;
                p.Saved = found.saved;
                p.Synced = found.networkSynced;
            }
        }

        public static void AnnotateAffectedCostumes(AnimatorLayerModel model, GameObject avatarRoot, IEnumerable<GameObject> costumeRoots)
        {
            model.AffectedCostumes.Clear();
            foreach (var costume in costumeRoots)
            {
                if (costume == null) continue;
                var costumePath = AvatarUtil.RelativePath(avatarRoot, costume);
                if (costumePath == null) continue;
                var hit = model.States.SelectMany(s => s.Bindings).Any(b =>
                    b.Path != null && (b.Path == costumePath || b.Path.StartsWith(costumePath + "/")));
                if (hit) model.AffectedCostumes.Add(costume.name);
            }
        }
    }
}
```

- [ ] **Step 3: テスト実行して全パスを確認**

**テスト実行**。Expected: `"failed":0`

もし `networkSynced` が存在せずコンパイルエラーになる場合（SDK バージョン差異）、テストと実装の `Synced` 突合部分を削除せず、`found.networkSynced` を リフレクションではなく そのまま使う前提を再確認する（com.vrchat.avatars >=3.10.3 では存在する）。

- [ ] **Step 4: Commit**

```bash
git add Editor/Core/AnimatorAnalysis Test/AnimatorSourceCollectorTest.cs
git commit -m "Animator解析: ソース列挙・ExpressionParameters突合・作用先衣装判定"
```

---

### Task 4: LayerPatternClassifier（機械分類 + 日本語要約）

**Files:**
- Create: `Editor/Core/AnimatorAnalysis/LayerPatternClassifier.cs`
- Test: `Test/LayerPatternClassifierTest.cs`

**Interfaces:**
- Consumes: `AnimatorLayerModel`（Task 1/2 の産物。テストはモデルを直接組み立てる＝Unity API 不要の純ロジックテスト）
- Produces:
  - `enum LayerPatternKind { BoolToggle, IntSelect, FloatContinuous, Complex, Empty }`
  - `class ClassificationResult { public LayerPatternKind Kind; public string Summary; public string TargetSummary; }`（Summary は機械的日本語要約。Complex のときは複雑理由）
  - `LayerPatternClassifier.Classify(AnimatorLayerModel model)` → `ClassificationResult`
  - `LayerPatternClassifier.BuildTargetSummary(IEnumerable<BindingInfo> bindings)` → `"オブジェクト2件、BlendShape 1件"` 形式（UI でも使用）

- [ ] **Step 1: 失敗するテストを書く**

`Test/LayerPatternClassifierTest.cs`:

```csharp
using NUnit.Framework;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class LayerPatternClassifierTest
    {
        static AnimatorLayerModel NewModel()
        {
            return new AnimatorLayerModel { LayerName = "L", Weight = 1f };
        }

        static StateNode AddState(AnimatorLayerModel m, int id, string name, PseudoNodeKind pseudo = PseudoNodeKind.None)
        {
            var node = new StateNode { Id = id, Name = name, Pseudo = pseudo };
            m.States.Add(node);
            return node;
        }

        static void AddEdge(AnimatorLayerModel m, int from, int to, string param = null, string mode = null, float threshold = 0)
        {
            var e = new TransitionEdge { FromId = from, ToId = to };
            if (param != null) e.Conditions.Add(new ConditionInfo { Parameter = param, Mode = mode, Threshold = threshold });
            m.Transitions.Add(e);
        }

        static BindingInfo Active(string path, string value) =>
            new BindingInfo { Path = path, Type = "GameObject", Property = "m_IsActive", Category = BindingCategory.GameObjectActive, ValueSummary = value };

        [Test]
        public void Classify_MutualBoolToggle()
        {
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "Toggle", Type = "Bool" });
            var on = AddState(m, 0, "ON");
            var off = AddState(m, 1, "OFF");
            on.Bindings.Add(Active("Costume/Top", "1"));
            off.Bindings.Add(Active("Costume/Top", "0"));
            AddEdge(m, 1, 0, "Toggle", "If");
            AddEdge(m, 0, 1, "Toggle", "IfNot");

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.BoolToggle));
            Assert.That(result.Summary, Does.Contain("`Toggle`"));
            Assert.That(result.Summary, Does.Contain("`Top`"));
            Assert.That(result.Summary, Does.Contain("ON/OFF"));
        }

        [Test]
        public void Classify_ExitEntryBoolToggle()
        {
            // Entry 再評価形: Entry→ON(If) / Entry→OFF(default) / 各状態→Exit
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "Toggle", Type = "Bool" });
            var entry = AddState(m, 0, "Entry", PseudoNodeKind.Entry);
            var exit = AddState(m, 1, "Exit", PseudoNodeKind.Exit);
            var on = AddState(m, 2, "ON");
            var off = AddState(m, 3, "OFF");
            on.Bindings.Add(Active("Top", "1"));
            off.Bindings.Add(Active("Top", "0"));
            AddEdge(m, 0, 2, "Toggle", "If");
            m.Transitions.Add(new TransitionEdge { FromId = 0, ToId = 3, IsDefault = true });
            AddEdge(m, 2, 1, "Toggle", "IfNot");
            AddEdge(m, 3, 1, "Toggle", "If");

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.BoolToggle));
        }

        [Test]
        public void Classify_IntSelect()
        {
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "Cloth", Type = "Int" });
            var a = AddState(m, 0, "Red");
            var b = AddState(m, 1, "Blue");
            a.Bindings.Add(Active("Red", "1"));
            b.Bindings.Add(Active("Blue", "1"));
            AddEdge(m, 1, 0, "Cloth", "Equals", 0);
            AddEdge(m, 0, 1, "Cloth", "Equals", 1);

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.IntSelect));
            Assert.That(result.Summary, Does.Contain("`Cloth`"));
            Assert.That(result.Summary, Does.Contain("0"));
            Assert.That(result.Summary, Does.Contain("`Red`"));
        }

        [Test]
        public void Classify_FloatContinuous_MotionTime()
        {
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "Radial", Type = "Float" });
            var s = AddState(m, 0, "S");
            s.MotionTimeParameter = "Radial";
            s.Bindings.Add(new BindingInfo { Path = "Mesh", Type = "SkinnedMeshRenderer", Property = "blendShape.Big", Category = BindingCategory.BlendShape, ValueSummary = "0→100" });

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.FloatContinuous));
            Assert.That(result.Summary, Does.Contain("`Radial`"));
            Assert.That(result.Summary, Does.Contain("連続変化"));
        }

        [Test]
        public void Classify_Empty_NoBindings()
        {
            var m = NewModel();
            AddState(m, 0, "S");
            var result = LayerPatternClassifier.Classify(m);
            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.Empty));
        }

        [Test]
        public void Classify_Complex_MultipleParameters()
        {
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "A", Type = "Bool" });
            m.Parameters.Add(new ParameterInfo { Name = "B", Type = "Bool" });
            var s1 = AddState(m, 0, "S1");
            var s2 = AddState(m, 1, "S2");
            s1.Bindings.Add(Active("X", "1"));
            s2.Bindings.Add(Active("X", "0"));
            AddEdge(m, 0, 1, "A", "If");
            AddEdge(m, 1, 0, "B", "If");

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.Complex));
        }

        [Test]
        public void Classify_Complex_ParameterDriver()
        {
            var m = NewModel();
            m.Parameters.Add(new ParameterInfo { Name = "Toggle", Type = "Bool" });
            var on = AddState(m, 0, "ON");
            var off = AddState(m, 1, "OFF");
            on.Bindings.Add(Active("Top", "1"));
            off.Bindings.Add(Active("Top", "0"));
            on.Behaviours.Add(new BehaviourInfo { Type = "VRCAvatarParameterDriver" });
            AddEdge(m, 1, 0, "Toggle", "If");
            AddEdge(m, 0, 1, "Toggle", "IfNot");

            var result = LayerPatternClassifier.Classify(m);

            Assert.That(result.Kind, Is.EqualTo(LayerPatternKind.Complex));
        }

        [Test]
        public void BuildTargetSummary_CountsByCategory()
        {
            var bindings = new[]
            {
                Active("A", "1"),
                Active("B", "1"),
                new BindingInfo { Path = "Mesh", Property = "blendShape.Big", Category = BindingCategory.BlendShape },
            };
            Assert.That(LayerPatternClassifier.BuildTargetSummary(bindings), Is.EqualTo("オブジェクト2件、BlendShape 1件"));
        }
    }
}
```

- [ ] **Step 2: LayerPatternClassifier を実装**

`Editor/Core/AnimatorAnalysis/LayerPatternClassifier.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public enum LayerPatternKind { BoolToggle, IntSelect, FloatContinuous, Complex, Empty }

    public class ClassificationResult
    {
        public LayerPatternKind Kind;
        /// <summary>機械的日本語要約。Complex のときは複雑理由（LLM 生成対象である旨は UI 側で付す）</summary>
        public string Summary;
        public string TargetSummary;
    }

    /// <summary>グラフモデル上のパターン判定。boolトグル各形 / int排他選択 / float連続 のみ機械分類し、他は Complex とする</summary>
    public static class LayerPatternClassifier
    {
        public static ClassificationResult Classify(AnimatorLayerModel model)
        {
            var real = model.States.Where(s => s.Pseudo == PseudoNodeKind.None).ToList();
            var allBindings = real.SelectMany(s => s.Bindings).ToList();
            var targetSummary = BuildTargetSummary(allBindings);
            var behaviours = real.SelectMany(s => s.Behaviours).ToList();

            if (model.Weight <= 0f) return new ClassificationResult { Kind = LayerPatternKind.Empty, Summary = "weight 0 のため無効", TargetSummary = targetSummary };
            if (real.Count == 0 || (allBindings.Count == 0 && behaviours.Count == 0))
                return new ClassificationResult { Kind = LayerPatternKind.Empty, Summary = "アニメーション対象なし", TargetSummary = targetSummary };

            var conditionParams = model.Transitions.SelectMany(t => t.Conditions).Select(c => c.Parameter).Distinct().ToList();
            var reasons = ComplexReasons(model, real, behaviours, conditionParams);
            if (reasons.Count > 0)
                return new ClassificationResult { Kind = LayerPatternKind.Complex, Summary = "複雑な構造: " + string.Join("、", reasons), TargetSummary = targetSummary };

            // float連続: 単一状態の Motion Time / float 1個の Simple1D BlendTree
            if (real.Count == 1)
            {
                var s = real[0];
                var param = s.MotionTimeParameter
                    ?? (s.Motion != null && s.Motion.IsBlendTree && s.Motion.BlendType == "Simple1D" ? s.Motion.BlendParameter : null);
                if (param != null && conditionParams.Count == 0)
                    return new ClassificationResult { Kind = LayerPatternKind.FloatContinuous, Summary = $"`{param}` の値で {JoinTargets(allBindings)} を連続変化", TargetSummary = targetSummary };
            }

            if (conditionParams.Count == 1)
            {
                var paramName = conditionParams[0];
                var paramType = model.Parameters.FirstOrDefault(p => p.Name == paramName)?.Type;
                if (paramType == "Bool" && real.Count == 2)
                {
                    var toggle = TryBoolToggle(model, real, paramName);
                    if (toggle != null) { toggle.TargetSummary = targetSummary; return toggle; }
                }
                if (paramType == "Int")
                {
                    var select = TryIntSelect(model, real, paramName);
                    if (select != null) { select.TargetSummary = targetSummary; return select; }
                }
            }

            return new ClassificationResult { Kind = LayerPatternKind.Complex, Summary = "複雑な構造: 既知パターンに一致しない", TargetSummary = targetSummary };
        }

        static List<string> ComplexReasons(AnimatorLayerModel model, List<StateNode> real, List<BehaviourInfo> behaviours, List<string> conditionParams)
        {
            var reasons = new List<string>();
            if (real.Any(s => s.Scope != "")) reasons.Add("sub-state machine");
            if (real.Any(s => s.Motion != null && s.Motion.IsBlendTree && s.Motion.BlendType == "Direct")) reasons.Add("Direct BlendTree");
            if (behaviours.Count > 0) reasons.Add(string.Join("/", behaviours.Select(b => b.Type).Distinct()));
            if (conditionParams.Count > 1) reasons.Add("複数パラメーター");
            return reasons;
        }

        static ClassificationResult TryBoolToggle(AnimatorLayerModel model, List<StateNode> real, string param)
        {
            // 各状態の「滞在時のパラメーター値」を入出エッジから投票で決める。
            // 入エッジ If→true / IfNot→false、出エッジは逆（If で離れる=滞在は false）
            var rest = new Dictionary<int, bool?>();
            foreach (var s in real) rest[s.Id] = null;
            foreach (var t in model.Transitions)
            {
                var cond = t.Conditions.FirstOrDefault(c => c.Parameter == param);
                if (cond == null) continue;
                bool? value = cond.Mode == "If" ? true : cond.Mode == "IfNot" ? (bool?)false : null;
                if (value == null) return null; // bool に Greater 等は想定外 → 複雑
                if (rest.ContainsKey(t.ToId))
                {
                    if (Vote(rest, t.ToId, value.Value) == false) return null;
                }
                if (rest.ContainsKey(t.FromId))
                {
                    if (Vote(rest, t.FromId, !value.Value) == false) return null;
                }
            }
            var a = real[0]; var b = real[1];
            if (rest[a.Id] == null || rest[b.Id] == null || rest[a.Id] == rest[b.Id]) return null;
            var onState = rest[a.Id] == true ? a : b;
            var offState = rest[a.Id] == true ? b : a;
            var diff = DiffBindings(onState, offState);
            var targets = diff.Count > 0 ? JoinTargets(diff) : JoinTargets(onState.Bindings.Concat(offState.Bindings).ToList());
            return new ClassificationResult
            {
                Kind = LayerPatternKind.BoolToggle,
                Summary = $"`{param}` で {targets} をON/OFF（ON時: `{onState.Name}`）",
            };
        }

        /// <summary>投票: 未決なら採用、決定済みで矛盾したら false</summary>
        static bool Vote(Dictionary<int, bool?> rest, int id, bool value)
        {
            if (rest[id] == null) { rest[id] = value; return true; }
            return rest[id] == value;
        }

        static ClassificationResult TryIntSelect(AnimatorLayerModel model, List<StateNode> real, string param)
        {
            // 各状態の対応値: 入エッジの Equals しきい値。Equals/NotEqual 以外が混ざれば複雑
            if (model.Transitions.SelectMany(t => t.Conditions).Any(c => c.Mode != "Equals" && c.Mode != "NotEqual")) return null;
            var values = new Dictionary<int, float>();
            foreach (var t in model.Transitions)
            {
                var cond = t.Conditions.FirstOrDefault(c => c.Parameter == param && c.Mode == "Equals");
                if (cond == null) continue;
                if (!values.ContainsKey(t.ToId) && real.Any(s => s.Id == t.ToId)) values[t.ToId] = cond.Threshold;
                else if (values.TryGetValue(t.ToId, out var existing) && existing != cond.Threshold) return null;
            }
            if (values.Count < real.Count - 1) return null; // default 状態1つを除き全状態に値が要る
            var parts = values.OrderBy(kv => kv.Value)
                .Select(kv => $"{kv.Value:0.#}=`{real.First(s => s.Id == kv.Key).Name}`");
            return new ClassificationResult
            {
                Kind = LayerPatternKind.IntSelect,
                Summary = $"`{param}` の値で切替: " + string.Join(" / ", parts),
            };
        }

        /// <summary>ON/OFF 状態間で値が異なる（または片方にしかない）バインディングを抽出</summary>
        static List<BindingInfo> DiffBindings(StateNode a, StateNode b)
        {
            var result = new List<BindingInfo>();
            var bMap = b.Bindings.ToDictionary(x => (x.Path, x.Property), x => x);
            foreach (var x in a.Bindings)
            {
                if (!bMap.TryGetValue((x.Path, x.Property), out var y) || y.ValueSummary != x.ValueSummary) result.Add(x);
            }
            var aKeys = new HashSet<(string, string)>(a.Bindings.Select(x => (x.Path, x.Property)));
            result.AddRange(b.Bindings.Where(x => !aKeys.Contains((x.Path, x.Property))));
            return result;
        }

        /// <summary>対象の代表表示: 末端名の distinct 先頭2件 + ほかN件</summary>
        static string JoinTargets(List<BindingInfo> bindings)
        {
            var names = bindings
                .Select(x => x.Path != null ? x.Path.Substring(x.Path.LastIndexOf('/') + 1) : x.Property)
                .Distinct().ToList();
            if (names.Count == 0) return "(対象なし)";
            var head = names.Take(2).Select(n => $"`{n}`");
            var rest = names.Count - 2;
            return string.Join("、", head) + (rest > 0 ? $" ほか{rest}件" : "");
        }

        static readonly (BindingCategory Category, string Label)[] CategoryLabels =
        {
            (BindingCategory.GameObjectActive, "オブジェクト"),
            (BindingCategory.BlendShape, "BlendShape "),
            (BindingCategory.MaterialProperty, "マテリアル"),
            (BindingCategory.MaterialSwap, "マテリアル差し替え"),
            (BindingCategory.Transform, "Transform "),
            (BindingCategory.Humanoid, "Humanoid "),
            (BindingCategory.Other, "その他"),
        };

        public static string BuildTargetSummary(IEnumerable<BindingInfo> bindings)
        {
            var list = bindings.ToList();
            var parts = new List<string>();
            foreach (var (category, label) in CategoryLabels)
            {
                var count = list.Where(b => b.Category == category).Select(b => (b.Path, b.Property)).Distinct().Count();
                if (count > 0) parts.Add($"{label}{count}件");
            }
            return parts.Count == 0 ? "なし" : string.Join("、", parts);
        }
    }
}
```

- [ ] **Step 3: テスト実行して全パスを確認**

**テスト実行**。Expected: `"failed":0`

- [ ] **Step 4: Commit**

```bash
git add Editor/Core/AnimatorAnalysis/LayerPatternClassifier.cs Test/LayerPatternClassifierTest.cs
git commit -m "Animator解析: グラフパターン分類器と日本語要約"
```

---

### Task 5: LayerModelSerializer（構造化JSON）

**Files:**
- Create: `Editor/Core/AnimatorAnalysis/LayerModelSerializer.cs`
- Test: `Test/LayerModelSerializerTest.cs`

**Interfaces:**
- Consumes: `AnimatorLayerModel`
- Produces:
  - `LayerModelSerializer.LayerId(AnimatorLayerModel model)` → 一意ID文字列（例 `"FX[Sailor/Anim]/TestLayer"`。PlayableLayer は `"FX/TestLayer"`）
  - `LayerModelSerializer.ToJson(AnimatorLayerModel model, bool indented)` → JSON文字列（enum は文字列、null プロパティは省略、ルートに `"id"` を含む）

- [ ] **Step 1: 失敗するテストを書く**

`Test/LayerModelSerializerTest.cs`:

```csharp
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class LayerModelSerializerTest
    {
        static AnimatorLayerModel Model()
        {
            var m = new AnimatorLayerModel
            {
                Source = new SourceInfo { Kind = AnimatorSourceKind.MergeAnimator, LayerType = "FX", ComponentPath = "Sailor/Anim", ControllerName = "C" },
                LayerName = "Toggle",
                Weight = 1f,
            };
            var s = new StateNode { Id = 0, Name = "ON" };
            s.Bindings.Add(new BindingInfo { Path = "Sailor/Top", Type = "GameObject", Property = "m_IsActive", Category = BindingCategory.GameObjectActive, ValueSummary = "1" });
            m.States.Add(s);
            m.Transitions.Add(new TransitionEdge { FromId = 1, ToId = 0 });
            m.Parameters.Add(new ParameterInfo { Name = "T", Type = "Bool" });
            return m;
        }

        [Test]
        public void LayerId_IncludesSourceAndName()
        {
            Assert.That(LayerModelSerializer.LayerId(Model()), Is.EqualTo("FX[Sailor/Anim]/Toggle"));
            var playable = Model();
            playable.Source = new SourceInfo { Kind = AnimatorSourceKind.PlayableLayer, LayerType = "FX" };
            Assert.That(LayerModelSerializer.LayerId(playable), Is.EqualTo("FX/Toggle"));
        }

        [Test]
        public void ToJson_StructuredOutput()
        {
            var json = JObject.Parse(LayerModelSerializer.ToJson(Model(), false));
            Assert.That((string)json["id"], Is.EqualTo("FX[Sailor/Anim]/Toggle"));
            Assert.That((string)json["LayerName"], Is.EqualTo("Toggle"));
            Assert.That((string)json["Source"]["Kind"], Is.EqualTo("MergeAnimator")); // enum は文字列
            Assert.That((string)json["States"][0]["Bindings"][0]["Category"], Is.EqualTo("GameObjectActive"));
            // null プロパティ（MaskName 等）は省略される
            Assert.That(json["MaskName"], Is.Null);
            // 擬似ノード情報は Pseudo=None のとき省略される（既定値）
            Assert.That(json["States"][0]["Pseudo"], Is.Null);
        }
    }
}
```

- [ ] **Step 2: LayerModelSerializer を実装**

`Editor/Core/AnimatorAnalysis/LayerModelSerializer.cs`:

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>layer構造モデルの構造化JSON。これだけ読めば Animator の直接読解が不要になる自己完結表現
    /// （LLM入力・クリップボードコピー・キャッシュキーに共用）</summary>
    public static class LayerModelSerializer
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Converters = { new StringEnumConverter() },
        };

        public static string LayerId(AnimatorLayerModel model)
        {
            var source = model.Source;
            var prefix = source != null && source.Kind == AnimatorSourceKind.MergeAnimator
                ? $"{source.LayerType}[{source.ComponentPath}]"
                : source != null ? source.LayerType : "?";
            return $"{prefix}/{model.LayerName}";
        }

        public static string ToJson(AnimatorLayerModel model, bool indented)
        {
            var obj = JObject.FromObject(model, JsonSerializer.Create(Settings));
            obj.AddFirst(new JProperty("id", LayerId(model)));
            return obj.ToString(indented ? Formatting.Indented : Formatting.None);
        }
    }
}
```

注意: `DefaultValueHandling.Ignore` で `Pseudo = None` や `false`/`0` の既定値が省略され JSON が小さくなる（LLM 入力サイズ対策）。`Weight = 1` も省略されるが、モデル契約として「省略時は既定値」なのでプロンプトで補足する（Task 7）。

- [ ] **Step 3: テスト実行して全パスを確認**

**テスト実行**。Expected: `"failed":0`

- [ ] **Step 4: Commit**

```bash
git add Editor/Core/AnimatorAnalysis/LayerModelSerializer.cs Test/LayerModelSerializerTest.cs
git commit -m "Animator解析: 構造化JSONシリアライザ"
```

---

### Task 6: LlmClient（設定・リクエスト構築・送信）

**Files:**
- Create: `Editor/Core/Llm/LlmSettings.cs`
- Create: `Editor/Core/Llm/LlmClient.cs`
- Test: `Test/LlmClientTest.cs`

**Interfaces:**
- Produces:
  - `enum LlmProvider { OpenAiCompatible, Anthropic }`
  - `class LlmSettings { public LlmProvider Provider; public string Endpoint; public string Model; public string ApiKey; public int MaxInputChars; public string EffectiveEndpoint { get; } public static LlmSettings Load(); public void Save(); }`（EditorPrefs 保存。キーはプロジェクト外＝リポジトリに入らない）
  - `class LlmRequest { public string Url; public List<(string Name, string Value)> Headers; public string Body; }`
  - `LlmClient.BuildRequest(LlmSettings settings, string system, string user)` → `LlmRequest`
  - `LlmClient.ParseContent(LlmProvider provider, string responseJson)` → 応答本文テキスト（取れなければ null）
  - `LlmClient.Send(LlmRequest request, Action<string, string> onDone)` → 非同期送信。成功時 `(content, null)`、失敗時 `(null, error)`。メインスレッドの completed コールバックで呼ばれる

- [ ] **Step 1: 失敗するテストを書く**

`Test/LlmClientTest.cs`（実API呼び出しなし。リクエスト構築・応答パースのみ）:

```csharp
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class LlmClientTest
    {
        static LlmSettings OpenAi() => new LlmSettings { Provider = LlmProvider.OpenAiCompatible, Model = "gpt-x", ApiKey = "sk-test" };
        static LlmSettings Anthropic() => new LlmSettings { Provider = LlmProvider.Anthropic, Model = "claude-x", ApiKey = "sk-ant-test" };

        [Test]
        public void BuildRequest_OpenAiCompatible()
        {
            var req = LlmClient.BuildRequest(OpenAi(), "sys", "user text");
            Assert.That(req.Url, Is.EqualTo("https://api.openai.com/v1/chat/completions"));
            Assert.That(req.Headers.Single(h => h.Name == "Authorization").Value, Is.EqualTo("Bearer sk-test"));
            var body = JObject.Parse(req.Body);
            Assert.That((string)body["model"], Is.EqualTo("gpt-x"));
            Assert.That((string)body["messages"][0]["role"], Is.EqualTo("system"));
            Assert.That((string)body["messages"][0]["content"], Is.EqualTo("sys"));
            Assert.That((string)body["messages"][1]["role"], Is.EqualTo("user"));
        }

        [Test]
        public void BuildRequest_Anthropic()
        {
            var req = LlmClient.BuildRequest(Anthropic(), "sys", "user text");
            Assert.That(req.Url, Is.EqualTo("https://api.anthropic.com/v1/messages"));
            Assert.That(req.Headers.Single(h => h.Name == "x-api-key").Value, Is.EqualTo("sk-ant-test"));
            Assert.That(req.Headers.Single(h => h.Name == "anthropic-version").Value, Is.EqualTo("2023-06-01"));
            var body = JObject.Parse(req.Body);
            Assert.That((string)body["model"], Is.EqualTo("claude-x"));
            Assert.That((string)body["system"], Is.EqualTo("sys"));
            Assert.That((int)body["max_tokens"], Is.GreaterThan(0));
            Assert.That((string)body["messages"][0]["role"], Is.EqualTo("user"));
        }

        [Test]
        public void BuildRequest_CustomEndpoint()
        {
            var settings = OpenAi();
            settings.Endpoint = "http://localhost:1234/v1/chat/completions";
            Assert.That(LlmClient.BuildRequest(settings, "s", "u").Url, Is.EqualTo("http://localhost:1234/v1/chat/completions"));
        }

        [Test]
        public void ParseContent_OpenAiCompatible()
        {
            var content = LlmClient.ParseContent(LlmProvider.OpenAiCompatible,
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"hello\"}}]}");
            Assert.That(content, Is.EqualTo("hello"));
        }

        [Test]
        public void ParseContent_Anthropic()
        {
            var content = LlmClient.ParseContent(LlmProvider.Anthropic,
                "{\"content\":[{\"type\":\"text\",\"text\":\"hello\"}]}");
            Assert.That(content, Is.EqualTo("hello"));
        }

        [Test]
        public void ParseContent_Malformed_ReturnsNull()
        {
            Assert.That(LlmClient.ParseContent(LlmProvider.OpenAiCompatible, "{}"), Is.Null);
            Assert.That(LlmClient.ParseContent(LlmProvider.OpenAiCompatible, "not json"), Is.Null);
        }
    }
}
```

- [ ] **Step 2: LlmSettings / LlmClient を実装**

`Editor/Core/Llm/LlmSettings.cs`:

```csharp
using UnityEditor;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public enum LlmProvider { OpenAiCompatible, Anthropic }

    /// <summary>LLM API 設定。EditorPrefs 保存（プロジェクト外＝リポジトリに入らない・マシン内共有）</summary>
    public class LlmSettings
    {
        const string KeyPrefix = "CostumeDashboard.Llm.";

        public LlmProvider Provider;
        /// <summary>空ならプロバイダー既定エンドポイント</summary>
        public string Endpoint = "";
        public string Model = "";
        public string ApiKey = "";
        /// <summary>1リクエストの入力サイズ上限（文字数）。超過時は layer 境界で分割</summary>
        public int MaxInputChars = 200000;

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
                MaxInputChars = EditorPrefs.GetInt(KeyPrefix + "MaxInputChars", 200000),
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
```

`Editor/Core/Llm/LlmClient.cs`:

```csharp
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
                    var content = json["content"] as JArray;
                    if (content == null) return null;
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

        /// <summary>非同期送信。completed コールバック（メインスレッド）で onDone(content, error) を呼ぶ</summary>
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
                    // ParseContent はリクエスト元がプロバイダーを知っているので Send では生テキストを返す
                    content = www.downloadHandler.text;
                }
                www.Dispose();
                onDone(content, error);
            };
        }
    }
}
```

注意: `Send` が返すのは**応答JSON生テキスト**。呼び出し側（Task 7）が `ParseContent` で本文を取り出す（`onDone(rawJson, null)`）。

- [ ] **Step 3: テスト実行して全パスを確認**

**テスト実行**。Expected: `"failed":0`

- [ ] **Step 4: Commit**

```bash
git add Editor/Core/Llm Test/LlmClientTest.cs
git commit -m "LLM: 設定とOpenAI互換/Anthropic対応クライアント"
```

---

### Task 7: LayerExplainer（プロンプト・一括生成・分割・キャッシュ）

**Files:**
- Create: `Editor/Core/Llm/LayerExplainer.cs`
- Test: `Test/LayerExplainerTest.cs`

**Interfaces:**
- Consumes: `LlmClient.BuildRequest` / `LlmClient.Send` / `LlmClient.ParseContent`（Task 6）、`LayerModelSerializer.ToJson` / `LayerId`（Task 5）
- Produces:
  - `LayerExplainer.PromptVersion`（const string。プロンプト変更時にインクリメントしてキャッシュを無効化）
  - `LayerExplainer.CacheKey(string layerJson, string model)` → SHA1 hex
  - `LayerExplainer.SystemPrompt()` / `LayerExplainer.UserPrompt(List<(string Id, string Json)> layers)`
  - `LayerExplainer.Chunk(List<(string Id, string Json)> layers, int maxChars)` → 分割リスト（1件で超過しても単独チャンクにする）
  - `LayerExplainer.ParseResponse(string content)` → `Dictionary<string, string>`（コードフェンス除去 + JSONパース。失敗時 null）
  - `LayerExplainer.LoadCache(string path)` / `SaveCache(string path, Dictionary<string, string> cache)`、`LayerExplainer.DefaultCachePath = "UserSettings/CostumeDashboardLlmCache.json"`
  - `LayerExplainer.GenerateAsync(LlmSettings settings, List<(string Id, string Json)> layers, Action<int, int> onProgress, Action<Dictionary<string, string>, string> onDone)` → チャンクを逐次送信し id→説明 を集約。途中エラーは中断して部分結果 + エラーを返す

- [ ] **Step 1: 失敗するテストを書く**

`Test/LayerExplainerTest.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class LayerExplainerTest
    {
        [Test]
        public void CacheKey_DiffersByContentAndModel()
        {
            var a = LayerExplainer.CacheKey("{\"x\":1}", "model-a");
            Assert.That(a, Is.EqualTo(LayerExplainer.CacheKey("{\"x\":1}", "model-a"))); // 決定的
            Assert.That(a, Is.Not.EqualTo(LayerExplainer.CacheKey("{\"x\":2}", "model-a")));
            Assert.That(a, Is.Not.EqualTo(LayerExplainer.CacheKey("{\"x\":1}", "model-b")));
            Assert.That(a, Does.Match("^[0-9a-f]{40}$"));
        }

        [Test]
        public void UserPrompt_ContainsIdsAndJson()
        {
            var prompt = LayerExplainer.UserPrompt(new List<(string, string)>
            {
                ("FX/L1", "{\"id\":\"FX/L1\"}"),
                ("FX/L2", "{\"id\":\"FX/L2\"}"),
            });
            Assert.That(prompt, Does.Contain("FX/L1"));
            Assert.That(prompt, Does.Contain("{\"id\":\"FX/L2\"}"));
        }

        [Test]
        public void Chunk_SplitsBySize()
        {
            var layers = new List<(string, string)>
            {
                ("a", new string('x', 60)),
                ("b", new string('x', 60)),
                ("c", new string('x', 200)), // 単独で上限超過でも単独チャンクとして送る
            };
            var chunks = LayerExplainer.Chunk(layers, 100);
            Assert.That(chunks.Count, Is.EqualTo(3));
            Assert.That(chunks[0][0].Id, Is.EqualTo("a"));
            Assert.That(chunks[2][0].Id, Is.EqualTo("c"));
        }

        [Test]
        public void Chunk_PacksWithinLimit()
        {
            var layers = new List<(string, string)> { ("a", "12345"), ("b", "12345"), ("c", "12345") };
            var chunks = LayerExplainer.Chunk(layers, 12);
            Assert.That(chunks.Count, Is.EqualTo(2)); // 5+5 <= 12, 次の5で超過
        }

        [Test]
        public void ParseResponse_PlainAndFenced()
        {
            var expected = new Dictionary<string, string> { { "FX/L1", "説明文" } };
            Assert.That(LayerExplainer.ParseResponse("{\"FX/L1\":\"説明文\"}"), Is.EqualTo(expected));
            Assert.That(LayerExplainer.ParseResponse("```json\n{\"FX/L1\":\"説明文\"}\n```"), Is.EqualTo(expected));
            Assert.That(LayerExplainer.ParseResponse("これは説明: ```json\n{\"FX/L1\":\"説明文\"}\n``` 以上"), Is.EqualTo(expected));
            Assert.That(LayerExplainer.ParseResponse("not json"), Is.Null);
        }

        [Test]
        public void Cache_SaveAndLoadRoundtrip()
        {
            var path = "Library/CostumeDashboardLlmCacheTest.json";
            if (File.Exists(path)) File.Delete(path);
            Assert.That(LayerExplainer.LoadCache(path), Is.Empty); // ファイル無し → 空
            var cache = new Dictionary<string, string> { { "key1", "説明1" }, { "key2", "説明2" } };
            LayerExplainer.SaveCache(path, cache);
            Assert.That(LayerExplainer.LoadCache(path), Is.EqualTo(cache));
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: LayerExplainer を実装**

`Editor/Core/Llm/LayerExplainer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>複雑layerのLLM説明生成。未生成分を一括で1リクエストに乗せ（上限超過時のみ分割）、
    /// layer単位で内容ハッシュキャッシュする</summary>
    public static class LayerExplainer
    {
        /// <summary>プロンプト変更時にインクリメントして全キャッシュを無効化する</summary>
        public const string PromptVersion = "1";
        public const string DefaultCachePath = "UserSettings/CostumeDashboardLlmCache.json";

        public static string CacheKey(string layerJson, string model)
        {
            using (var sha1 = SHA1.Create())
            {
                var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(PromptVersion + "\n" + model + "\n" + layerJson));
                var sb = new StringBuilder();
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string SystemPrompt()
        {
            return
"あなたはVRChatアバター改変の専門家です。VRChatアバターのAnimator layerを構造化JSONで受け取り、改変ユーザー向けに日本語で説明します。\n" +
"JSONは layer ごとに1オブジェクトで、状態(States)と遷移(Transitions)はノードIDで結ばれた平坦なグラフです。Pseudo は Entry/AnyState/Exit の擬似ノード、Bindings は各状態のアニメーション対象（アバタールート相対パス×プロパティ×値要約）です。省略されたプロパティは既定値(0/false/null)です。\n" +
"各layerについて以下を数行で説明してください:\n" +
"1. 何で動くか（どのパラメーターのどの値で。Expression Parameters 登録状況も考慮）\n" +
"2. 何を動かすか（どのオブジェクト/BlendShape/マテリアルを）\n" +
"3. 振る舞い（トグル・切替・連続変化・多段ギミック等、遷移構造の意味）\n" +
"複数layerが同じパラメーターを使う・VRCAvatarParameterDriver で連携する場合は、その関係にも触れてください。\n" +
"出力は入力の id をキーとするJSONオブジェクトのみを返します: {\"<id>\": \"説明文\", ...}";
        }

        public static string UserPrompt(List<(string Id, string Json)> layers)
        {
            var sb = new StringBuilder();
            sb.AppendLine("以下のAnimator layer群を説明してください。");
            foreach (var (id, json) in layers)
            {
                sb.AppendLine($"### {id}");
                sb.AppendLine(json);
            }
            return sb.ToString();
        }

        public static List<List<(string Id, string Json)>> Chunk(List<(string Id, string Json)> layers, int maxChars)
        {
            var result = new List<List<(string Id, string Json)>>();
            var current = new List<(string Id, string Json)>();
            var size = 0;
            foreach (var layer in layers)
            {
                var len = layer.Json.Length;
                if (current.Count > 0 && size + len > maxChars)
                {
                    result.Add(current);
                    current = new List<(string Id, string Json)>();
                    size = 0;
                }
                current.Add(layer);
                size += len;
            }
            if (current.Count > 0) result.Add(current);
            return result;
        }

        public static Dictionary<string, string> ParseResponse(string content)
        {
            if (content == null) return null;
            var text = content.Trim();
            // コードフェンスや前置きが混ざっても最初の { から最後の } までを取り出す
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            try
            {
                var json = JObject.Parse(text.Substring(start, end - start + 1));
                var result = new Dictionary<string, string>();
                foreach (var prop in json.Properties()) result[prop.Name] = (string)prop.Value;
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static Dictionary<string, string> LoadCache(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, string>();
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path))
                    ?? new Dictionary<string, string>();
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }

        public static void SaveCache(string path, Dictionary<string, string> cache)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(cache, Formatting.Indented));
        }

        /// <summary>チャンクを逐次送信して id→説明 を集約。エラー時は中断し部分結果とエラーを返す</summary>
        public static void GenerateAsync(LlmSettings settings, List<(string Id, string Json)> layers, Action<int, int> onProgress, Action<Dictionary<string, string>, string> onDone)
        {
            var chunks = Chunk(layers, settings.MaxInputChars);
            var results = new Dictionary<string, string>();
            void SendChunk(int index)
            {
                if (index >= chunks.Count) { onDone(results, null); return; }
                if (onProgress != null) onProgress(index, chunks.Count);
                var request = LlmClient.BuildRequest(settings, SystemPrompt(), UserPrompt(chunks[index]));
                LlmClient.Send(request, (rawJson, error) =>
                {
                    if (error != null) { onDone(results, error); return; }
                    var content = LlmClient.ParseContent(settings.Provider, rawJson);
                    var parsed = ParseResponse(content);
                    if (parsed == null) { onDone(results, "応答のJSONパースに失敗: " + Truncate(content, 200)); return; }
                    foreach (var kv in parsed) results[kv.Key] = kv.Value;
                    SendChunk(index + 1);
                });
            }
            SendChunk(0);
        }

        static string Truncate(string text, int max)
        {
            if (text == null) return "(空応答)";
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }
}
```

- [ ] **Step 3: テスト実行して全パスを確認**

**テスト実行**。Expected: `"failed":0`

- [ ] **Step 4: Commit**

```bash
git add Editor/Core/Llm/LayerExplainer.cs Test/LayerExplainerTest.cs
git commit -m "LLM: 一括説明生成（プロンプト・分割・内容ハッシュキャッシュ）"
```

---

### Task 8: UI（ビュー切替ボタングループ化 + AnimatorLayersView）

**Files:**
- Create: `Editor/UI/AnimatorLayersView.cs`
- Create: `Editor/UI/LlmSettingsWindow.cs`
- Modify: `Editor/UI/CostumeDashboardWindow.cs`（enum拡張・Popup→ボタングループ・Animatorビュー組み込み）
- Test: `Test/AnimatorLayersViewTest.cs`

**Interfaces:**
- Consumes: Task 3〜7 の全API + 既存 `AvatarUtil`
- Produces:
  - `AnimatorLayersView : VisualElement` — `Refresh(List<GameObject> costumeRoots)`、`public bool AnalyzeAvatar` / `public bool FilterCostumeOnly`（変更時 `StateChanged` を発火。ウィンドウが SerializeField へ永続化）
  - `AnimatorLayersView.SourceEntry` / `LayerEntry`（public。テストから使用）
  - `AnimatorLayersView.BuildEntries(List<GameObject> costumeRoots, bool analyzeAvatar)` → `List<SourceEntry>`（純ロジック部。UI非依存でテスト可能）
  - `LlmSettingsWindow` — LLM設定編集用のユーティリティウィンドウ

- [ ] **Step 1: 失敗するテストを書く**

`Test/AnimatorLayersViewTest.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using nadena.dev.modular_avatar.core;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class AnimatorLayersViewTest
    {
        GameObject avatar;
        GameObject costume;
        AnimatorController controller;

        [SetUp]
        public void SetUp()
        {
            avatar = new GameObject("Avatar");
            avatar.AddComponent<VRCAvatarDescriptor>();
            costume = new GameObject("Sailor");
            costume.transform.SetParent(avatar.transform);
            var top = new GameObject("Top");
            top.transform.SetParent(costume.transform);

            controller = new AnimatorController();
            controller.AddLayer("TopToggle");
            controller.AddParameter("TopOn", AnimatorControllerParameterType.Bool);
            var sm = controller.layers[0].stateMachine;
            var onClip = new AnimationClip { name = "on" };
            onClip.SetCurve("Top", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 1));
            var offClip = new AnimationClip { name = "off" };
            offClip.SetCurve("Top", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 0));
            var on = sm.AddState("ON"); on.motion = onClip;
            var off = sm.AddState("OFF"); off.motion = offClip;
            sm.defaultState = off;
            var toOn = off.AddTransition(on); toOn.AddCondition(AnimatorConditionMode.If, 0, "TopOn");
            var toOff = on.AddTransition(off); toOff.AddCondition(AnimatorConditionMode.IfNot, 0, "TopOn");

            var merge = costume.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar);
            Object.DestroyImmediate(controller);
        }

        [Test]
        public void BuildEntries_CostumeMergeAnimator_ClassifiedAndAnnotated()
        {
            var entries = AnimatorLayersView.BuildEntries(new List<GameObject> { costume }, false);

            var source = entries.Single();
            Assert.That(source.Source.Info.Kind, Is.EqualTo(AnimatorSourceKind.MergeAnimator));
            var layer = source.Layers.Single();
            Assert.That(layer.Id, Is.EqualTo("FX[Sailor]/TopToggle"));
            Assert.That(layer.Classification.Kind, Is.EqualTo(LayerPatternKind.BoolToggle));
            // Relative パス解決: Sailor/Top に作用 → 登録衣装 Sailor が作用先
            Assert.That(layer.Model.AffectedCostumes, Is.EquivalentTo(new[] { "Sailor" }));
            Assert.That(layer.Json, Does.Contain("Sailor/Top"));
        }

        [Test]
        public void BuildEntries_AnalyzeAvatarOff_ExcludesDescriptorLayers()
        {
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    animatorController = controller,
                    isDefault = false,
                },
            };
            descriptor.specialAnimationLayers = new VRCAvatarDescriptor.CustomAnimLayer[0];

            Assert.That(AnimatorLayersView.BuildEntries(new List<GameObject> { costume }, false).Count, Is.EqualTo(1));
            var withAvatar = AnimatorLayersView.BuildEntries(new List<GameObject> { costume }, true);
            Assert.That(withAvatar.Count, Is.EqualTo(2));
            Assert.That(withAvatar.Any(e => e.Source.Info.Kind == AnimatorSourceKind.PlayableLayer), Is.True);
        }
    }
}
```

- [ ] **Step 2: AnimatorLayersView を実装**

`Editor/UI/AnimatorLayersView.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    /// <summary>Animator layer の作用説明ビュー。既定は登録衣装配下の MA Merge Animator のみ解析、
    /// [アバターも解析] で Descriptor playable layers + 衣装外 Merge Animator を追加</summary>
    public class AnimatorLayersView : VisualElement
    {
        public class SourceEntry
        {
            public GameObject AvatarRoot;
            public AnimatorSource Source;
            public List<LayerEntry> Layers = new List<LayerEntry>();
        }

        public class LayerEntry
        {
            public AnimatorLayerModel Model;
            public ClassificationResult Classification;
            public string Id;
            public string Json;
            /// <summary>Expression Menu の該当コントロール名（ベストエフォート。パラメーター名 → コントロール名）</summary>
            public string TriggerTooltip;
        }

        class Row
        {
            public SourceEntry Source;
            public LayerEntry Layer; // null = ソース行
        }

        public bool AnalyzeAvatar;
        public bool FilterCostumeOnly;
        /// <summary>AnalyzeAvatar / FilterCostumeOnly 変更時。ウィンドウ側で SerializeField へ永続化する</summary>
        public Action StateChanged;

        MultiColumnTreeView tree;
        TextField detail;
        Label statusLabel;
        Button generateButton;
        List<GameObject> costumeRoots = new List<GameObject>();
        List<SourceEntry> entries = new List<SourceEntry>();
        Dictionary<string, string> cache;
        bool generating;

        public AnimatorLayersView()
        {
            cache = LayerExplainer.LoadCache(LayerExplainer.DefaultCachePath);

            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0 } };
            var analyzeAvatarToggle = new Toggle("アバターも解析");
            analyzeAvatarToggle.RegisterValueChangedCallback(e => { AnalyzeAvatar = e.newValue; if (StateChanged != null) StateChanged(); Refresh(costumeRoots); });
            toolbar.Add(analyzeAvatarToggle);
            var filterToggle = new Toggle("登録衣装に作用のみ");
            filterToggle.RegisterValueChangedCallback(e => { FilterCostumeOnly = e.newValue; if (StateChanged != null) StateChanged(); Refresh(costumeRoots); });
            toolbar.Add(filterToggle);
            generateButton = new Button(GenerateAll) { text = "未生成を一括生成" };
            toolbar.Add(generateButton);
            toolbar.Add(new Button(() => LlmSettingsWindow.Open()) { text = "LLM設定" });
            statusLabel = new Label("");
            toolbar.Add(statusLabel);
            Add(toolbar);
            // ウィンドウ側が SerializeField から復元した値をトグルUIへ反映するために公開
            RegisterCallback<AttachToPanelEvent>(_ => { analyzeAvatarToggle.SetValueWithoutNotify(AnalyzeAvatar); filterToggle.SetValueWithoutNotify(FilterCostumeOnly); });

            tree = BuildTree();
            tree.style.flexGrow = 1;
            Add(tree);

            detail = new TextField { multiline = true, isReadOnly = true, style = { flexShrink = 0, height = 120, whiteSpace = WhiteSpace.Normal } };
            Add(detail);
        }

        /// <summary>純ロジック部（UI非依存・テスト対象）: 衣装群からソース列挙 → モデル化 → 分類・注記</summary>
        public static List<SourceEntry> BuildEntries(List<GameObject> costumeRoots, bool analyzeAvatar)
        {
            var result = new List<SourceEntry>();
            var byAvatar = costumeRoots
                .Where(c => c != null)
                .Select(c => (Costume: c, Avatar: AvatarUtil.FindAvatarRoot(c)))
                .Where(x => x.Avatar != null)
                .GroupBy(x => x.Avatar);
            foreach (var group in byAvatar)
            {
                var avatarRoot = group.Key;
                var costumes = group.Select(x => x.Costume).ToList();
                var sources = AnimatorSourceCollector.CollectCostumeSources(avatarRoot, costumes);
                if (analyzeAvatar) sources.AddRange(AnimatorSourceCollector.CollectAvatarSources(avatarRoot, costumes));
                var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
                var parameters = descriptor != null ? descriptor.expressionParameters : null;
                var menuIndex = BuildMenuNameIndex(descriptor);
                foreach (var source in sources)
                {
                    var entry = new SourceEntry { AvatarRoot = avatarRoot, Source = source };
                    foreach (var model in AnimatorSourceCollector.BuildModels(source))
                    {
                        AnimatorSourceCollector.AnnotateExpressionParameters(model, parameters);
                        AnimatorSourceCollector.AnnotateAffectedCostumes(model, avatarRoot, costumes);
                        entry.Layers.Add(new LayerEntry
                        {
                            Model = model,
                            Classification = LayerPatternClassifier.Classify(model),
                            Id = LayerModelSerializer.LayerId(model),
                            Json = LayerModelSerializer.ToJson(model, false),
                            TriggerTooltip = BuildTriggerTooltip(model, menuIndex),
                        });
                    }
                    result.Add(entry);
                }
            }
            return result;
        }

        /// <summary>Expression Menu を再帰走査して パラメーター名 → コントロール名 を引く（ベストエフォート。MA Menu 合成は追わない）</summary>
        static Dictionary<string, string> BuildMenuNameIndex(VRCAvatarDescriptor descriptor)
        {
            var result = new Dictionary<string, string>();
            if (descriptor == null || descriptor.expressionsMenu == null) return result;
            var visited = new HashSet<VRCExpressionsMenu>();
            void Walk(VRCExpressionsMenu menu)
            {
                if (menu == null || !visited.Add(menu)) return;
                foreach (var control in menu.controls)
                {
                    if (control == null) continue;
                    if (control.parameter != null && !string.IsNullOrEmpty(control.parameter.name) && !result.ContainsKey(control.parameter.name))
                        result[control.parameter.name] = control.name;
                    if (control.subParameters != null)
                    {
                        foreach (var sub in control.subParameters)
                        {
                            if (sub != null && !string.IsNullOrEmpty(sub.name) && !result.ContainsKey(sub.name)) result[sub.name] = control.name;
                        }
                    }
                    if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu) Walk(control.subMenu);
                }
            }
            Walk(descriptor.expressionsMenu);
            return result;
        }

        static string BuildTriggerTooltip(AnimatorLayerModel model, Dictionary<string, string> menuIndex)
        {
            var lines = model.Parameters.Select(p =>
            {
                var ep = p.InExpressionParameters ? $" [EP{(p.Synced ? " synced" : "")}{(p.Saved ? " saved" : "")}]" : "";
                var menu = menuIndex.TryGetValue(p.Name, out var name) ? $" メニュー: {name}" : "";
                return $"{p.Name} ({p.Type}){ep}{menu}";
            }).ToList();
            return lines.Count == 0 ? "" : string.Join("\n", lines);
        }

        public void Refresh(List<GameObject> roots)
        {
            costumeRoots = roots.ToList();
            entries = BuildEntries(costumeRoots, AnalyzeAvatar);
            var items = new List<TreeViewItemData<Row>>();
            var id = 0;
            foreach (var source in entries)
            {
                var children = new List<TreeViewItemData<Row>>();
                foreach (var layer in source.Layers)
                {
                    if (FilterCostumeOnly && layer.Model.AffectedCostumes.Count == 0) continue;
                    children.Add(new TreeViewItemData<Row>(id++, new Row { Source = source, Layer = layer }));
                }
                if (FilterCostumeOnly && children.Count == 0 && source.Source.Warning == null) continue;
                items.Add(new TreeViewItemData<Row>(id++, new Row { Source = source }, children));
            }
            tree.SetRootItems(items);
            tree.Rebuild();
            tree.ExpandAll();
        }

        MultiColumnTreeView BuildTree()
        {
            var columns = new Columns();
            columns.Add(MakeColumn("layer", "レイヤー", 220, row =>
                row.Layer != null ? row.Layer.Model.LayerName : SourceTitle(row.Source)));
            columns.Add(MakeColumn("kind", "分類", 70, row =>
                row.Layer != null ? KindLabel(row.Layer.Classification.Kind) : (row.Source.Source.Warning ?? "")));
            columns.Add(MakeColumn("trigger", "トリガー", 120, row =>
                row.Layer != null ? string.Join(", ", row.Layer.Model.Parameters.Select(p => p.Name)) : "",
                row => row.Layer != null ? row.Layer.TriggerTooltip : null));
            columns.Add(MakeColumn("target", "作用先", 160, row =>
                row.Layer != null ? row.Layer.Classification.TargetSummary : ""));
            columns.Add(MakeColumn("costume", "衣装", 100, row =>
                row.Layer != null ? string.Join(", ", row.Layer.Model.AffectedCostumes) : ""));
            columns.Add(MakeColumn("summary", "説明", 300, row =>
                row.Layer != null ? FirstLine(ExplanationText(row.Layer)) : ""));
            columns.Add(new Column
            {
                name = "ops", title = "操作", width = 130,
                makeCell = () =>
                {
                    var container = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                    container.Add(new Button { text = "生成" });
                    container.Add(new Button { text = "JSON" });
                    return container;
                },
                bindCell = (cell, index) =>
                {
                    var row = tree.GetItemDataForIndex<Row>(index);
                    var generate = (Button)cell[0];
                    var copy = (Button)cell[1];
                    var visible = row.Layer != null;
                    generate.style.display = visible && row.Layer.Classification.Kind == LayerPatternKind.Complex ? DisplayStyle.Flex : DisplayStyle.None;
                    copy.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                    if (!visible) return;
                    var layer = row.Layer;
                    generate.clickable = new Clickable(() => GenerateOne(layer));
                    generate.SetEnabled(!generating);
                    copy.clickable = new Clickable(() => { EditorGUIUtility.systemCopyBuffer = LayerModelSerializer.ToJson(layer.Model, true); });
                },
            });
            var view = new MultiColumnTreeView(columns) { selectionType = SelectionType.Single };
            view.selectionChanged += _ => UpdateDetail();
            return view;
        }

        Column MakeColumn(string name, string title, float width, Func<Row, string> text, Func<Row, string> tooltip = null)
        {
            return new Column
            {
                name = name, title = title, width = width,
                makeCell = () => new Label { style = { unityTextOverflowPosition = TextOverflowPosition.End, overflow = Overflow.Hidden } },
                bindCell = (cell, index) =>
                {
                    var row = tree.GetItemDataForIndex<Row>(index);
                    var label = (Label)cell;
                    label.text = text(row) ?? "";
                    label.tooltip = tooltip != null ? tooltip(row) : label.text;
                },
            };
        }

        static string SourceTitle(SourceEntry source)
        {
            var info = source.Source.Info;
            return info.Kind == AnimatorSourceKind.PlayableLayer
                ? $"{info.LayerType} (Descriptor) {info.ControllerName}"
                : $"Merge Animator ({info.ComponentPath}) [{info.LayerType}] {info.ControllerName}";
        }

        static string KindLabel(LayerPatternKind kind)
        {
            switch (kind)
            {
                case LayerPatternKind.BoolToggle: return "トグル";
                case LayerPatternKind.IntSelect: return "排他選択";
                case LayerPatternKind.FloatContinuous: return "連続";
                case LayerPatternKind.Complex: return "複雑";
                default: return "空";
            }
        }

        string ExplanationText(LayerEntry layer)
        {
            if (layer.Classification.Kind != LayerPatternKind.Complex) return layer.Classification.Summary;
            var settings = LlmSettings.Load();
            if (cache.TryGetValue(LayerExplainer.CacheKey(layer.Json, settings.Model), out var text)) return text;
            return layer.Classification.Summary + "（LLM説明は未生成）";
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var i = text.IndexOf('\n');
            return i < 0 ? text : text.Substring(0, i);
        }

        void UpdateDetail()
        {
            var row = tree.selectedItem as Row;
            if (row == null || row.Layer == null) { detail.value = ""; return; }
            var layer = row.Layer;
            var bindings = layer.Model.States.SelectMany(s => s.Bindings)
                .Select(b => $"  {(b.Path ?? "(Humanoid)")} {b.Property} = {b.ValueSummary}")
                .Distinct().Take(50).ToList();
            detail.value =
                $"{layer.Id}\n" +
                $"分類: {KindLabel(layer.Classification.Kind)} / 作用先: {layer.Classification.TargetSummary}\n" +
                (string.IsNullOrEmpty(layer.TriggerTooltip) ? "" : $"トリガー:\n{layer.TriggerTooltip}\n") +
                $"説明: {ExplanationText(layer)}\n" +
                $"バインディング:\n{string.Join("\n", bindings)}";
        }

        List<LayerEntry> UncachedComplexLayers(string model)
        {
            return entries.SelectMany(e => e.Layers)
                .Where(l => l.Classification.Kind == LayerPatternKind.Complex)
                .Where(l => !cache.ContainsKey(LayerExplainer.CacheKey(l.Json, model)))
                .ToList();
        }

        void GenerateAll()
        {
            var settings = LlmSettings.Load();
            if (string.IsNullOrEmpty(settings.ApiKey) || string.IsNullOrEmpty(settings.Model))
            {
                EditorUtility.DisplayDialog("LLM設定が必要", "LLM設定（APIキー・モデル名）を設定してください。", "OK");
                return;
            }
            var targets = UncachedComplexLayers(settings.Model);
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("一括生成", "未生成の複雑layerはありません。", "OK");
                return;
            }
            var payload = targets.Select(t => (t.Id, t.Json)).ToList();
            var chunks = LayerExplainer.Chunk(payload, settings.MaxInputChars);
            var totalChars = payload.Sum(p => p.Json.Length);
            if (!EditorUtility.DisplayDialog("一括生成",
                $"複雑layer {targets.Count} 件を {chunks.Count} リクエスト（合計約 {totalChars:N0} 文字）で {settings.Model} に送信します。実行しますか？",
                "実行", "キャンセル")) return;
            Generate(settings, targets, payload);
        }

        void GenerateOne(LayerEntry layer)
        {
            var settings = LlmSettings.Load();
            if (string.IsNullOrEmpty(settings.ApiKey) || string.IsNullOrEmpty(settings.Model))
            {
                EditorUtility.DisplayDialog("LLM設定が必要", "LLM設定（APIキー・モデル名）を設定してください。", "OK");
                return;
            }
            // 個別ボタンは強制再生成を兼ねる（キャッシュ有無を見ない）
            Generate(settings, new List<LayerEntry> { layer }, new List<(string, string)> { (layer.Id, layer.Json) });
        }

        void Generate(LlmSettings settings, List<LayerEntry> targets, List<(string Id, string Json)> payload)
        {
            generating = true;
            generateButton.SetEnabled(false);
            var jsonById = targets.ToDictionary(t => t.Id, t => t.Json);
            LayerExplainer.GenerateAsync(settings, payload,
                (index, total) => { statusLabel.text = $"生成中… ({index + 1}/{total})"; },
                (results, error) =>
                {
                    generating = false;
                    generateButton.SetEnabled(true);
                    foreach (var kv in results)
                    {
                        // 応答の id からキャッシュキー（内容ハッシュ）へ引き直して保存
                        if (jsonById.TryGetValue(kv.Key, out var json)) cache[LayerExplainer.CacheKey(json, settings.Model)] = kv.Value;
                    }
                    LayerExplainer.SaveCache(LayerExplainer.DefaultCachePath, cache);
                    statusLabel.text = error != null ? "エラー: " + FirstLine(error) : $"{results.Count} 件生成完了";
                    if (error != null) Debug.LogError("[CostumeDashboard] LLM生成エラー: " + error);
                    tree.RefreshItems();
                    UpdateDetail();
                });
        }
    }
}
```

`Editor/UI/LlmSettingsWindow.cs`:

```csharp
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
            window.minSize = new Vector2(400, 160);
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
```

- [ ] **Step 3: CostumeDashboardWindow にビュー切替ボタングループと Animator ビューを組み込む**

`Editor/UI/CostumeDashboardWindow.cs` の変更（該当箇所のみ）:

1. enum とラベルに Animator を追加:

```csharp
        /// <summary>表示: メッシュ（既定、衣装 > メッシュ > スロット） / AO ME（衣装 > グループ > メッシュ > スロット） / Animator（layer作用説明）</summary>
        internal enum DashboardViewMode { Mesh, Group, Animator }

        static readonly List<string> ViewModeChoices = new List<string> { "メッシュ", "AO ME", "Animator" };
```

2. フィールド追加（`MultiColumnTreeView tree;` の近く）:

```csharp
        AnimatorLayersView animatorView;
        readonly List<Button> viewModeButtons = new List<Button>();
        [SerializeField] bool animatorAnalyzeAvatar;
        [SerializeField] bool animatorFilterCostume;
```

3. `CreateGUI` の PopupField ブロック（`var viewModePopup = ...` 〜 `toolbar.Add(viewModePopup);`）を**ボタングループ**へ置換:

```csharp
            viewModeButtons.Clear();
            for (var i = 0; i < ViewModeChoices.Count; i++)
            {
                var mode = (DashboardViewMode)i;
                var button = new Button(() => SetViewMode(mode)) { text = ViewModeChoices[i] };
                viewModeButtons.Add(button);
                toolbar.Add(button);
            }
```

4. `CreateGUI` の `tree` 追加後（`root.Add(tree);` の後）に Animator ビューを追加し、初期表示状態を反映:

```csharp
            animatorView = new AnimatorLayersView { AnalyzeAvatar = animatorAnalyzeAvatar, FilterCostumeOnly = animatorFilterCostume };
            animatorView.StateChanged = () => { animatorAnalyzeAvatar = animatorView.AnalyzeAvatar; animatorFilterCostume = animatorView.FilterCostumeOnly; };
            animatorView.style.flexGrow = 1;
            root.Add(animatorView);
            UpdateViewMode();
```

5. メソッド追加:

```csharp
        void SetViewMode(DashboardViewMode mode)
        {
            viewMode = mode;
            UpdateViewMode();
            Refresh();
        }

        /// <summary>ビュー切替はボタングループ（1クリック）。選択中ボタンをハイライトし、対象ビューのみ表示する</summary>
        void UpdateViewMode()
        {
            for (var i = 0; i < viewModeButtons.Count; i++)
            {
                viewModeButtons[i].style.backgroundColor = i == (int)viewMode ? new StyleColor(new Color(0.25f, 0.35f, 0.55f)) : new StyleColor(StyleKeyword.Null);
            }
            var animator = viewMode == DashboardViewMode.Animator;
            tree.style.display = animator ? DisplayStyle.None : DisplayStyle.Flex;
            baseMeshContainer.style.display = animator ? DisplayStyle.None : DisplayStyle.Flex;
            animatorView.style.display = animator ? DisplayStyle.Flex : DisplayStyle.None;
        }
```

6. `Refresh()` の冒頭に Animator ビュー分岐を追加（既存のキャッシュ再構築・ツリー構築はメッシュ/AO ME ビューのときだけ実行）:

```csharp
        void Refresh()
        {
            RebuildCostumeList();
            if (viewMode == DashboardViewMode.Animator)
            {
                animatorView.Refresh(costumeRoots);
                return;
            }
            // （以下は既存の処理。RebuildCostumeList の既存呼び出し行は重複しないよう削除する）
```

既存 `Refresh()` 内の `RebuildCostumeList();` は先頭へ移した1回だけにする。他の処理順（`aomeConfiguredCache.Clear();` 〜 `tree.Rebuild();`）は変更しない。

7. 既存の `BuildTreeItems()` 呼び出し `viewMode == DashboardViewMode.Mesh ? BuildMeshViewItems() : BuildGroupViewItems()`（314行付近）は Animator のとき Refresh が early return するため到達しない。変更不要だが、三項演算子の意味が「Mesh 以外は Group」なので `viewMode == DashboardViewMode.Group ? BuildGroupViewItems() : BuildMeshViewItems()` に直して既定をメッシュにしておく。

- [ ] **Step 4: テスト実行して全パスを確認**

**テスト実行**。Expected: `"failed":0`（既存の全テスト + AnimatorLayersViewTest）

- [ ] **Step 5: 実機確認（手動）**

Unity で `Tools/Costume Dashboard` を開き:
- ボタングループでメッシュ / AO ME / Animator が1クリック切替できること（既存2ビューの表示内容が変わらないこと）
- 衣装を登録すると Animator ビューに Merge Animator の layer 行が出て、分類・トリガー・作用先・衣装列が埋まること
- [アバターも解析] ON で Descriptor FX の layer が追加されること
- [JSON] ボタンでクリップボードに整形JSONが入ること
- LLM設定未設定で [未生成を一括生成] を押すと設定を促すダイアログが出ること

- [ ] **Step 6: Commit**

```bash
git add Editor/UI Test/AnimatorLayersViewTest.cs
git commit -m "Animatorビュー追加とビュー切替のボタングループ化"
```

---

### Task 9: README 更新 + 最終検証

**Files:**
- Modify: `README.md`（機能セクションに追記）

- [ ] **Step 1: README の「## 機能」末尾に追記**

```markdown
- Animator ビュー：衣装が持ち込む MA Merge Animator の各 layer について「何で動くか（パラメーター・Expression Parameters 登録状況）」「何を動かすか（オブジェクト/BlendShape/マテリアル等の作用先と登録衣装との対応）」「振る舞い（トグル / 排他選択 / 連続変化）」を機械判定して日本語で要約表示。[アバターも解析] でアバター本体（Avatar Descriptor の playable layers・衣装外の Merge Animator）も対象にできる。機械分類できない複雑な layer は、layer 構造の自己完結 JSON（クリップボードコピー可）を LLM API（OpenAI 互換 / Anthropic、APIキーは EditorPrefs 保存）へ一括送信して説明を生成（結果は内容ハッシュでキャッシュ）。ビュー切替（メッシュ / AO ME / Animator）はツールバーのボタングループ
```

- [ ] **Step 2: 最終検証**

1. `../../.aibridge/cli/AIBridgeCLI.exe compile unity` → エラーなし
2. **テスト実行** → `"failed":0`
3. `../../.aibridge/cli/AIBridgeCLI.exe get_logs --logType Error` → 新規エラーなし
4. aibridge-development-workflow の検査清単（C# 9.0 互換 / Unity null 判定 / ハードコード / 重複コード / 修正範囲）を確認して報告

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "README: Animatorビューの説明を追加"
```

---

## Self-Review 記録

- スペック全要件とタスクの対応: グラフモデル(T1)/バインディング(T2)/ソース列挙・EP突合・作用先(T3)/機械分類(T4)/JSON(T5)/LLMクライアント(T6)/一括生成・分割・キャッシュ(T7)/UI・ボタングループ・フィルタ・詳細ペイン・JSONコピー(T8)/README(T9)
- 非スコープ確認: Expression Menu 完全解決はベストエフォート tooltip のみ（スペック通り）。NDMFビルド後解析・エクスポートは含めない
- 型整合: `AnimatorSource.Info/Controller/PathPrefix/Warning`、`LayerEntry.Id/Json/Model/Classification`、`LayerExplainer.GenerateAsync(settings, payload, onProgress, onDone)` の署名を全タスクで統一済み

