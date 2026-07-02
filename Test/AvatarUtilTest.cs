using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Narazaka.VRChat.CostumeDashboard.Editor.Test
{
    public class AvatarUtilTest
    {
        GameObject avatar;

        [SetUp]
        public void SetUp()
        {
            avatar = new GameObject("Avatar");
            avatar.AddComponent<VRCAvatarDescriptor>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar);
        }

        GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            return go;
        }

        [Test]
        public void FindAvatarRoot_FromDescendant()
        {
            var costume = Child(avatar, "Costume");
            var mesh = Child(costume, "Mesh");
            Assert.That(AvatarUtil.FindAvatarRoot(mesh), Is.EqualTo(avatar));
        }

        [Test]
        public void FindAvatarRoot_Self()
        {
            Assert.That(AvatarUtil.FindAvatarRoot(avatar), Is.EqualTo(avatar));
        }

        [Test]
        public void FindAvatarRoot_NotFound()
        {
            var orphan = new GameObject("Orphan");
            try
            {
                Assert.That(AvatarUtil.FindAvatarRoot(orphan), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(orphan);
            }
        }

        [Test]
        public void RelativePath_Nested()
        {
            var costume = Child(avatar, "Costume");
            var mesh = Child(costume, "Mesh");
            Assert.That(AvatarUtil.RelativePath(avatar, mesh), Is.EqualTo("Costume/Mesh"));
        }

        [Test]
        public void RelativePath_Same()
        {
            Assert.That(AvatarUtil.RelativePath(avatar, avatar), Is.EqualTo(""));
        }

        [Test]
        public void RelativePath_Outside_Null()
        {
            var orphan = new GameObject("Orphan");
            try
            {
                Assert.That(AvatarUtil.RelativePath(avatar, orphan), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(orphan);
            }
        }
    }
}
