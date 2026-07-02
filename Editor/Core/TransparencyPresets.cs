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

        /// <summary>枠別の駆動プロパティのみ。Main は共通ブレンド設定のみで駆動側は無いため空リスト</summary>
        public static List<PresetProperty> DriverProps(FadeFrame frame)
        {
            switch (frame)
            {
                case FadeFrame.Third:
                    return new List<PresetProperty>
                    {
                        F("_UseMain3rdTex", 1),
                        F("_Main3rdTexBlendMode", 3),
                        F("_Main3rdTexAlphaMode", 2),
                    };
                case FadeFrame.Second:
                    return new List<PresetProperty>
                    {
                        F("_UseMain2ndTex", 1),
                        F("_Main2ndTexBlendMode", 3),
                        F("_Main2ndTexAlphaMode", 2),
                    };
                case FadeFrame.AlphaMask:
                    // _AlphaMaskMode = 2 (multiply)、_AlphaMaskValue は toggle-menu の -1↔0 駆動の初期値 0
                    return new List<PresetProperty>
                    {
                        F("_AlphaMaskMode", 2),
                        F("_AlphaMaskValue", 0),
                    };
                default:
                    return new List<PresetProperty>();
            }
        }

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

            props.AddRange(DriverProps(frame));
            return props;
        }

        /// <summary>lilToon Multi 用: _TransparentMode を Transparent(2) に</summary>
        public static PresetProperty TransparentModeOverride() => F("_TransparentMode", 2);
    }
}
