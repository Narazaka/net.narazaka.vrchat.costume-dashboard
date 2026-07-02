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
