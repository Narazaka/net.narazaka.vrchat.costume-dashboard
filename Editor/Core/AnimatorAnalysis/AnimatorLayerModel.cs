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
        /// <summary>Entry から defaultState への暗黙エッジ（Entry の明示遷移がどれも成立しないとき）</summary>
        public bool IsDefault;
    }

    public class BlendTreeChildInfo
    {
        public MotionInfo Motion;
        public float Threshold;            // 1D
        public float PositionX, PositionY; // 2D
        public string DirectParameter;     // Direct
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
        /// <summary>アバタールート絶対パス。Humanoid はパス概念がないため null</summary>
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

    /// <summary>Animator layer の自己完結モデル。状態遷移は樹状でなく循環を含む任意グラフなので、
    /// ノード（状態）+ エッジ（遷移）の平坦な表現で持つ</summary>
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
        /// <summary>このlayerが作用する登録衣装名（AnimatorSourceCollector.AnnotateAffectedCostumes で設定）</summary>
        public List<string> AffectedCostumes = new List<string>();
    }
}
