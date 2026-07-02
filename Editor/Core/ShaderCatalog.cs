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
