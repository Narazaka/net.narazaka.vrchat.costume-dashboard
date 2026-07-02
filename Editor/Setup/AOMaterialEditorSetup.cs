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
