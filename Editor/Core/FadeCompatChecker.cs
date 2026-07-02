using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public enum FadeFrame
    {
        Main,
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
        public string ShortReason;
    }

    public class FadeCompatResult
    {
        public FadeFrameState Main;
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
            var main = CheckMain(material);
            var third = CheckFrame(material, ThirdProps);
            var second = CheckFrame(material, SecondProps);
            var alphaMask = CheckFrame(material, AlphaMaskProps);
            third.ShortReason = third.Compatible ? null : MakeShortReason(third);
            second.ShortReason = second.Compatible ? null : MakeShortReason(second);
            alphaMask.ShortReason = alphaMask.Compatible ? null : MakeShortReason(alphaMask);
            FadeFrame? recommended = null;
            if (main.Compatible) recommended = FadeFrame.Main;
            else if (alphaMask.Compatible) recommended = FadeFrame.AlphaMask;
            else if (third.Compatible) recommended = FadeFrame.Third;
            else if (second.Compatible) recommended = FadeFrame.Second;
            return new FadeCompatResult { Main = main, Third = third, Second = second, AlphaMask = alphaMask, Recommended = recommended };
        }

        // Main 枠: _Color が白 (1,1,1,1) のときのみ空き。RGB焼き込み不要の (1,1,1,0)↔(1,1,1,1) 駆動を成立させるための条件
        static FadeFrameState CheckMain(Material m)
        {
            var state = new FadeFrameState();
            if (m.HasProperty("_Color"))
            {
                var color = m.GetColor("_Color");
                if (!Approx(color, Color.white))
                {
                    state.NonDefaultProps.Add(new NonDefaultProp
                    {
                        Name = "_Color",
                        Current = color.ToString(),
                        Default = Color.white.ToString(),
                    });
                    state.ShortReason = $"_Color が #{ColorUtility.ToHtmlStringRGBA(color)} (白のみ可)";
                }
            }
            state.Compatible = state.NonDefaultProps.Count == 0;
            return state;
        }

        // 代表的な差分を優先して1行要約を作る: 有効化フラグ > テクスチャ割当 > その他
        static string MakeShortReason(FadeFrameState state)
        {
            NonDefaultProp Pick(System.Func<NonDefaultProp, bool> pred) => state.NonDefaultProps.FirstOrDefault(pred);
            var rep = Pick(p => p.Name.StartsWith("_UseMain") || p.Name == "_AlphaMaskMode")
                ?? Pick(p => p.Current.StartsWith("tex=") && !p.Current.StartsWith("tex=null"))
                ?? state.NonDefaultProps[0];
            var others = state.NonDefaultProps.Count - 1;
            var suffix = others > 0 ? $" (他{others}件)" : "";
            return $"{rep.Name}={Summarize(rep.Current)}{suffix}";
        }

        static string Summarize(string current)
        {
            if (current.StartsWith("tex=")) return current.Split(' ')[0];
            return current;
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
