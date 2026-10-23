using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    /// <summary>
    /// SO-01/02：资产持久存活与运行副本采用不同所有权策略；哈希只比较此测试创建的 .asset。
    /// 补例阶段未执行。运行时所建唯一目录由 TearDown 删除（包括各文件 .meta），不触碰用户资产。
    /// </summary>
    public sealed class ScriptableStateTests : ReloadCaseTestBase
    {
        string m_Folder;
        string m_AssetPath;
        ScriptableStateCase m_Asset;

        [UnityTearDown]
        public IEnumerator RemoveOwnedAsset()
        {
            if (Application.isPlaying) yield return LeavePlay();
            ScriptableStateOwner.ReleaseTestResources();
            if (!string.IsNullOrEmpty(m_Folder) && AssetDatabase.IsValidFolder(m_Folder))
                Assert.IsTrue(AssetDatabase.DeleteAsset(m_Folder), "只删除此用例创建的目录及其 .meta");
            else if (m_Asset != null && !EditorUtility.IsPersistent(m_Asset))
                UnityEngine.Object.DestroyImmediate(m_Asset);
            m_Asset = null;
            m_Folder = null;
            m_AssetPath = null;
        }

        [UnityTest, Description("SO-01：持续加载的 .asset/static 引用，非序列化会话状态和订阅 reset，磁盘 SHA256 不变；R1 三轮")]
        public IEnumerator SO01_LoadedAssetKeepsAuthorConfigurationWithoutPersistingRuntimeState()
        {
            CreateOwnedAsset();
            var originalHash = AssetHash();
            var originalInstanceId = m_Asset.GetInstanceID();
            ScriptableStateOwner.BindTestAsset(m_Asset);
            var cumulativeCallbacks = 0;

            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return EnterFastPlay();
                Assert.AreSame(m_Asset, ScriptableStateOwner.RetainedAsset,
                    "Scene Reload 不应被当作持续加载资产一定重建的保证");
                Assert.AreEqual(originalInstanceId, m_Asset.GetInstanceID());
                Assert.AreEqual(17, m_Asset.AuthorValue);
                Assert.AreEqual(0, m_Asset.RuntimeCount);
                Assert.AreEqual(0, m_Asset.SubscriberCount);
                m_Asset.Changed += () => cumulativeCallbacks++;
                m_Asset.RuntimeCount = 100 + cycle;
                m_Asset.Raise();
                Assert.AreEqual(cycle + 1, cumulativeCallbacks, "每局只响应当前的一个订阅");
                Assert.AreEqual(1, m_Asset.SubscriberCount);
                yield return LeavePlay();

                Assert.IsTrue(m_Asset != null);
                Assert.AreEqual(0, m_Asset.RuntimeCount);
                Assert.AreEqual(0, m_Asset.SubscriberCount);
                m_Asset.Raise();
                Assert.AreEqual(cycle + 1, cumulativeCallbacks, "退出后的旧订阅已经失效");
                Assert.AreEqual(originalHash, AssetHash(), "本例只写运行状态，不能偷偷保存作者资产");
                Assert.AreEqual(17, m_Asset.AuthorValue);
            }
        }

        [UnityTest, Description("SO-02：Instantiate 与 CreateInstance 副本，退出退订和销毁，仅本轮 owner 接受事件；R1 三轮")]
        public IEnumerator SO02_RuntimeCopiesUnsubscribeAndAreDestroyedBeforeNextSession()
        {
            CreateOwnedAsset();
            var originalHash = AssetHash();
            ScriptableStateOwner.BindTestAsset(m_Asset);
            ScriptableStateCase oldClone = null;
            ScriptableStateCase oldTransient = null;

            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return EnterFastPlay();
                Assert.AreEqual(0, ScriptableStateOwner.OwnedCopyCount);
                Assert.AreEqual(0, m_Asset.SubscriberCount);
                if (cycle > 0)
                {
                    Assert.IsTrue(oldClone == null && oldTransient == null);
                    Assert.IsFalse(ReferenceEquals(oldClone, null), "旧副本有托管壳，Unity 对象已销毁");
                }

                var clone = ScriptableStateOwner.CreateRuntimeCopy(true);
                var transient = ScriptableStateOwner.CreateRuntimeCopy(false);
                Assert.AreNotSame(m_Asset, clone);
                Assert.AreNotSame(m_Asset, transient);
                Assert.AreEqual(17, clone.AuthorValue, "Instantiate 复制作者配置");
                Assert.AreEqual(7, transient.AuthorValue, "CreateInstance 使用声明初值");
                clone.AuthorValue = 200 + cycle;
                Assert.AreEqual(17, m_Asset.AuthorValue, "副本配置修改不能回写源资产");
                Assert.AreEqual(0, clone.RuntimeCount);
                Assert.AreEqual(0, transient.RuntimeCount);
                Assert.AreEqual(2, ScriptableStateOwner.OwnedCopyCount);
                Assert.AreEqual(2, m_Asset.SubscriberCount);
                m_Asset.Raise();
                Assert.AreEqual(1, clone.RuntimeCount);
                Assert.AreEqual(1, transient.RuntimeCount);
                Assert.AreEqual(0, m_Asset.RuntimeCount);

                oldClone = clone;
                oldTransient = transient;
                yield return LeavePlay();
                Assert.IsTrue(clone == null && transient == null, "退出入口同步释放全部自有副本");
                Assert.AreEqual(0, ScriptableStateOwner.OwnedCopyCount);
                Assert.AreEqual(0, m_Asset.SubscriberCount);
                Assert.IsTrue(m_Asset != null, "源资产不属于运行副本的销毁范围");
                m_Asset.Raise();
                Assert.AreEqual(originalHash, AssetHash());
            }
        }

        void CreateOwnedAsset()
        {
            const string parent = "Assets/DomainReloadCases/GeneratedTestAssets";
            if (!AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder("Assets/DomainReloadCases", "GeneratedTestAssets");
            var leaf = "ScriptableState_" + Guid.NewGuid().ToString("N");
            m_Folder = parent + "/" + leaf;
            Assert.IsNotEmpty(AssetDatabase.CreateFolder(parent, leaf));
            m_AssetPath = m_Folder + "/Source.asset";
            m_Asset = ScriptableObject.CreateInstance<ScriptableStateCase>();
            m_Asset.AuthorValue = 17;
            AssetDatabase.CreateAsset(m_Asset, m_AssetPath);
            EditorUtility.SetDirty(m_Asset);
            AssetDatabase.SaveAssetIfDirty(m_Asset);
            Assert.IsTrue(EditorUtility.IsPersistent(m_Asset));
            Assert.IsTrue(File.Exists(AbsoluteAssetPath()));
        }

        string AssetHash()
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(AbsoluteAssetPath())));
        }

        string AbsoluteAssetPath() => Path.Combine(Application.dataPath, m_AssetPath.Substring("Assets/".Length));
    }
}
