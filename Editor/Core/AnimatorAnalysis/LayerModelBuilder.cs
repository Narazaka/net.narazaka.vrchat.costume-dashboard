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
                    BlendParameterY = tree.blendType == BlendTreeType.SimpleDirectional2D || tree.blendType == BlendTreeType.FreeformDirectional2D || tree.blendType == BlendTreeType.FreeformCartesian2D ? tree.blendParameterY : null,
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
                AddEdge(ctx, entryId, ResolveTarget(ctx, sm, t.destinationState, t.destinationStateMachine, t.isExit), t.conditions, false, 0, 0);
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
                    AddEdge(ctx, ctx.AnyStateId, ResolveTarget(ctx, sm, t.destinationState, t.destinationStateMachine, t.isExit), t.conditions, t.hasExitTime, t.exitTime, t.duration);
                }
            }
            // 各状態からの遷移
            foreach (var child in sm.states)
            {
                foreach (var t in child.state.transitions)
                {
                    AddEdge(ctx, ctx.StateIds[child.state], ResolveTarget(ctx, sm, t.destinationState, t.destinationStateMachine, t.isExit), t.conditions, t.hasExitTime, t.exitTime, t.duration);
                }
            }
            // sub-state machine 自体からの遷移（子smが Exit に達したとき評価される）→ 子smの Exit 擬似ノードから張る
            foreach (var child in sm.stateMachines)
            {
                var childExit = ctx.SmIds[child.stateMachine].Exit;
                foreach (var t in sm.GetStateMachineTransitions(child.stateMachine))
                {
                    AddEdge(ctx, childExit, ResolveTarget(ctx, sm, t.destinationState, t.destinationStateMachine, t.isExit), t.conditions, false, 0, 0);
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

        static void AddEdge(Context ctx, int fromId, int toId, AnimatorCondition[] conditions, bool hasExitTime, float exitTime, float duration)
        {
            var edge = new TransitionEdge { FromId = fromId, ToId = toId, HasExitTime = hasExitTime, ExitTime = exitTime, Duration = duration };
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
                    DefaultValue = p.type == AnimatorControllerParameterType.Bool ? (p.defaultBool ? 1f : 0f)
                        : p.type == AnimatorControllerParameterType.Int ? p.defaultInt
                        : p.defaultFloat,
                });
            }
        }
    }
}
