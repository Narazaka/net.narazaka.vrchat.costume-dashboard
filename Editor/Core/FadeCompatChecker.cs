using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Narazaka.VRChat.CostumeDashboard.Editor
{
    public enum FadeFrame
    {
        Main,
        Third,
        Second,
        AlphaMask,
    }

    public enum AlphaMaskAdjust
    {
        None,
        Neutralize,
        ToMultiply,
    }

    public class ColorFadeImpact
    {
        public AlphaMaskAdjust Adjust;
        public bool Blocked;
        public bool Warning;
        public string Reason;
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
        public bool Warning;
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
        public ColorFadeImpact ColorFadeImpact;

        /// <summary>枠指定で対応する FadeFrameState を引く</summary>
        public FadeFrameState GetFrame(FadeFrame frame) => frame switch
        {
            FadeFrame.Main => Main,
            FadeFrame.Third => Third,
            FadeFrame.Second => Second,
            FadeFrame.AlphaMask => AlphaMask,
            _ => null,
        };
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

        /// <summary>枠選択の優先順（メッシュ単位の共通推奨判定にも使う）: Main > AlphaMask > Third > Second</summary>
        static readonly FadeFrame[] FramePriority = { FadeFrame.Main, FadeFrame.AlphaMask, FadeFrame.Third, FadeFrame.Second };

        /// <summary>
        /// 複数スロット（同一レンダラー内の全マテリアルスロットを想定）に共通で使える推奨フェード枠を求める。
        /// マテリアルプロパティアニメーションはレンダラー単位でしかスロットを選べないため、
        /// フェードメソッドはメッシュ（レンダラー）につき単一枠にする必要がある。
        /// slot.FadeCompat が null（未知シェーダー）のスロットは判定から除外する。
        /// 既知スロットが1つも無ければ null。既知スロット全てで Compatible（Warning含む）な最初の枠
        /// （Main > AlphaMask > Third > Second）を返す。1件も無ければ null
        /// </summary>
        public static FadeFrame? CommonRecommended(IEnumerable<SlotInfo> slots)
        {
            var known = slots.Where(s => s.FadeCompat != null).ToList();
            if (known.Count == 0) return null;
            foreach (var frame in FramePriority)
            {
                if (known.All(s => s.FadeCompat.GetFrame(frame).Compatible)) return frame;
            }
            return null;
        }

        public static FadeCompatResult Check(Material material)
        {
            var main = CheckMain(material);
            var alphaMaskInert = IsAlphaMaskInertShader(material);
            var third = CheckFrame(material, ThirdProps, "_UseMain3rdTex", false);
            var second = CheckFrame(material, SecondProps, "_UseMain2ndTex", false);
            var alphaMask = CheckFrame(material, AlphaMaskProps, "_AlphaMaskMode", alphaMaskInert);
            var colorFadeImpact = AnalyzeColorFadeImpact(material, alphaMaskInert);
            ApplyColorFadeImpact(colorFadeImpact, main, third, second);
            FadeFrame? recommended = null;
            if (main.Compatible) recommended = FadeFrame.Main;
            else if (alphaMask.Compatible) recommended = FadeFrame.AlphaMask;
            else if (third.Compatible) recommended = FadeFrame.Third;
            else if (second.Compatible) recommended = FadeFrame.Second;
            return new FadeCompatResult { Main = main, Third = third, Second = second, AlphaMask = alphaMask, Recommended = recommended, ColorFadeImpact = colorFadeImpact };
        }

        // AlphaMask 使用中(Mode!=0)が Main/3rd/2nd の色フェードに与える干渉を分析する。
        // Mode=0(未使用) や不活性シェーダー(Neutralize)は枠表示に影響させない。
        static ColorFadeImpact AnalyzeColorFadeImpact(Material m, bool alphaMaskInert)
        {
            var impact = new ColorFadeImpact();
            if (!m.HasProperty("_AlphaMaskMode")) return impact;
            var mode = Mathf.RoundToInt(m.GetFloat("_AlphaMaskMode"));
            if (mode == 0) return impact;
            if (alphaMaskInert)
            {
                impact.Adjust = AlphaMaskAdjust.Neutralize;
                return impact;
            }
            if (mode == 2) return impact; // Multiply: 色フェードへの干渉なし
            if (mode == 1)
            {
                impact.Adjust = AlphaMaskAdjust.ToMultiply;
                if (MainTexHasAlpha(m))
                {
                    impact.Warning = true;
                    impact.Reason = "AlphaMask 置き換え→乗算に変換されます（メインテクスチャの透過と干渉する可能性）";
                }
                return impact;
            }
            impact.Blocked = true;
            impact.Reason = "AlphaMask が特殊モードで使用中";
            return impact;
        }

        // ColorFadeImpact を Main/3rd/2nd の3枠へ反映する（AlphaMask 枠自体は対象外）
        static void ApplyColorFadeImpact(ColorFadeImpact impact, FadeFrameState main, FadeFrameState third, FadeFrameState second)
        {
            var frames = new[] { main, third, second };
            if (impact.Blocked)
            {
                foreach (var frame in frames)
                {
                    if (!frame.Compatible) continue; // 既に非互換ならそのまま
                    frame.Compatible = false;
                    frame.Warning = false;
                    frame.ShortReason = impact.Reason;
                }
                return;
            }
            if (impact.Warning)
            {
                foreach (var frame in frames)
                {
                    if (!frame.Compatible) continue;
                    frame.ShortReason = frame.Warning && !string.IsNullOrEmpty(frame.ShortReason)
                        ? frame.ShortReason + "; " + impact.Reason
                        : impact.Reason;
                    frame.Warning = true;
                }
            }
        }

        // _MainTex の alpha チャンネル有無判定。
        // tex==null→false / Texture2D かつアセット化済みなら TextureImporter.DoesSourceTextureHaveAlpha /
        // importer 不在なら GraphicsFormatUtility.HasAlphaChannel / 非Texture2D→true（安全側）
        static bool MainTexHasAlpha(Material m)
        {
            if (!m.HasProperty("_MainTex")) return false;
            var tex = m.GetTexture("_MainTex");
            if (tex == null) return false;
            var tex2D = tex as Texture2D;
            if (tex2D == null) return true;
            var path = AssetDatabase.GetAssetPath(tex2D);
            if (!string.IsNullOrEmpty(path))
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    return importer.DoesSourceTextureHaveAlpha();
                }
            }
            return GraphicsFormatUtility.HasAlphaChannel(tex2D.graphicsFormat);
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

        // gateName: このグループの有効化ゲートプロパティ（_UseMainNTex / _AlphaMaskMode）。
        // gateInert: ゲートが有効でもレンダリングに影響しない状況（不透明シェーダーの AlphaMask）
        static FadeFrameState CheckFrame(Material m, List<PropDef> defs, string gateName, bool gateInert)
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
            if (state.NonDefaultProps.Count == 0)
            {
                state.Compatible = true;
                return state;
            }
            var gateProp = state.NonDefaultProps.FirstOrDefault(p => p.Name == gateName);
            var gateOff = gateProp == null; // ゲート自体がデフォルト(無効)のまま
            if (gateOff || gateInert)
            {
                state.Compatible = true;
                state.Warning = true;
                state.ShortReason = "未使用の設定値あり（プリセット適用で有効化されます）: " + MakeShortReason(state);
            }
            else
            {
                state.Compatible = false;
                state.ShortReason = MakeShortReason(state);
            }
            return state;
        }

        // AlphaMask が出力に効かない(=緩和対象の)シェーダーかどうか。
        // lilToon はアルファマスクを LIL_RENDER != 0 (Cutout / Transparent) で適用するため、
        // マスクが効かないのは Opaque のみ。Cutout は対象外(緩和しない)。
        // lilToon_multi は _TransparentMode 0 (Opaque相当) のみが該当。
        // unknown family は緩和しない安全側で透過扱い(false)
        static bool IsAlphaMaskInertShader(Material m)
        {
            var family = ShaderCatalog.Resolve(m.shader);
            switch (family.Variant)
            {
                case "opaque":
                case "opaque_o":
                    return true;
            }
            if (family.Family == "lilToon_multi" && m.HasProperty("_TransparentMode"))
            {
                var mode = Mathf.RoundToInt(m.GetFloat("_TransparentMode"));
                return mode == 0;
            }
            return false;
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
