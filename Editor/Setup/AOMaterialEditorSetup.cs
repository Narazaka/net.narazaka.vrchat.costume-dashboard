using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

        public static (bool Enabled, string Reason) Availability(GameObject avatarRoot, SlotGroup group)
        {
            if (!IsAvailable) return (false, "aoyon.material-editor が未導入");
            if (avatarRoot == null) return (false, "アバタールートが見つかりません");
            if (!group.SupportsFade) return (false, group.FadeDisabledReason);
            if (group.IsOneTwoTrans)
            {
                // onetrans/twotrans は shader override を行わないため 3rd 枠が使用済みでも成立するが、
                // 未知 family / マテリアル欠損は不可。
                // Main 枠（_Color 直接駆動）でも AO ME は必要: シェーダー自身の _Cutoff/_PreCutoff（既定0.5）を
                // 無効化しないとフェードがしきい値で clip されメッシュ全体が途中で消える
                if (group.Family == "unknown" || group.Slots.All(s => s.Material == null)) return (false, group.FadeDisabledReason ?? "対象外");
                return (true, null);
            }
            if (!group.CanSetupFade) return (false, group.FadeDisabledReason);
            return (true, null);
        }

        public static string HostSuffix(SlotGroup group)
        {
            // onetrans/twotrans は Preset==null（全枠使用済み）でも作成可能で DriverProps は Third を既定枠にする
            // （CreateForGroup の effectivePreset と同じ規則）。実効枠が異なれば DriverProps 内容も異なるため、
            // ホスト suffix にも実効枠を反映して同一ホストへの衝突を防ぐ
            var effectivePreset = group.IsOneTwoTrans ? (group.Preset ?? FadeFrame.Third) : group.Preset;
            var suffix = group.Variant;
            if (effectivePreset == FadeFrame.Second) suffix += "_2nd";
            else if (effectivePreset == FadeFrame.AlphaMask) suffix += "_alpha_mask";
            else if (effectivePreset == FadeFrame.Third) suffix += "_3rd";
            // AlphaMask 枠は調整 override を適用しない（DriverProps が mode=2 を設定済み）ため suffix も付けない
            if (group.Preset != FadeFrame.AlphaMask)
            {
                switch (group.AlphaMaskAdjust)
                {
                    case AlphaMaskAdjust.Neutralize: suffix += "_amoff"; break;
                    case AlphaMaskAdjust.ToMultiply: suffix += "_ammul"; break;
                }
            }
            return suffix;
        }

        /// <summary>グループへの AO Material Editor 作成。成功時 null、失敗時はエラーメッセージを返す</summary>
        public static string CreateForGroup(GameObject costume, GameObject avatarRoot, SlotGroup group)
        {
            var suffix = HostSuffix(group);

            var host = FindOrCreateChild(FindOrCreateChild(costume, "trans"), suffix);

            var slots = group.Slots
                .Where(s => s.Renderer != null)
                .Select(s => new SlotTarget
                {
                    RendererPath = AvatarUtil.RelativePath(avatarRoot, s.Renderer.gameObject),
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
                    return $"透過版シェーダーが見つかりません (GUID: {group.TransparentGuid})";
                }
            }

            // onetrans/twotrans は Preset==null（全枠使用済み）でも作成可能で DriverProps は Third を既定枠にするため、
            // AlphaMask 調整 override の判定も同じ実効枠で行う（raw Preset で判定すると null 時に override が落ちる）
            var effectivePreset = group.IsOneTwoTrans ? (group.Preset ?? FadeFrame.Third) : group.Preset;

            List<PresetProperty> properties;
            if (group.IsOneTwoTrans)
            {
                properties = TransparencyPresets.OneTwoTransProps(effectivePreset.Value, group.Variant.StartsWith("twotrans"));
            }
            else
            {
                properties = TransparencyPresets.For(group.Preset.Value);
                if (group.Family == "lilToon_multi") properties.Add(TransparencyPresets.TransparentModeOverride());
            }

            // 実効枠が Main/Third/Second のとき、AlphaMask 残存値による色フェードへの干渉を
            // AO ME 側で打ち消す。AlphaMask 枠自体は DriverProps が既に _AlphaMaskMode=2 を設定済みのため対象外
            if (effectivePreset == FadeFrame.Main || effectivePreset == FadeFrame.Third || effectivePreset == FadeFrame.Second)
            {
                switch (group.AlphaMaskAdjust)
                {
                    case AlphaMaskAdjust.Neutralize:
                        properties.Add(TransparencyPresets.AlphaMaskModeOverride(0));
                        break;
                    case AlphaMaskAdjust.ToMultiply:
                        properties.Add(TransparencyPresets.AlphaMaskModeOverride(2));
                        break;
                }
            }

            Apply(host, slots, shader, properties);
            return null;
        }

        /// <summary>groups のうち Availability が有効な全グループに CreateForGroup を実行し、作成数/スキップ数を返す</summary>
        public static (int Created, int Skipped) CreateBatch(GameObject costume, GameObject avatarRoot, List<SlotGroup> groups)
        {
            var created = 0;
            var skipped = 0;
            // グループキー/ホスト suffix の設計上、通常は同一バッチ内で suffix が重複することはないが、
            // 万一の回帰（キー正規化漏れ等）で衝突した場合に SlotTargets を後勝ちで上書きしてしまう事故を防ぐ防御線
            var usedSuffixes = new HashSet<string>();
            foreach (var group in groups)
            {
                var (enabled, _) = Availability(avatarRoot, group);
                if (!enabled)
                {
                    skipped++;
                    continue;
                }
                var suffix = HostSuffix(group);
                if (!usedSuffixes.Add(suffix))
                {
                    skipped++;
                    continue;
                }
                if (CreateForGroup(costume, avatarRoot, group) != null)
                {
                    skipped++;
                    continue;
                }
                created++;
            }
            return (created, skipped);
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
