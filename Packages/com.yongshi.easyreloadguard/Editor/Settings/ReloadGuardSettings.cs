using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyReloadGuard.Editor
{
    internal enum PrePlayCheck
    {
        /// <summary>不在进入 Play 前检查（仍可手动或 CLI 检查）。</summary>
        [InspectorName("不检查")]
        Off,

        /// <summary>存在不在暂缓清单中的不符合项时输出警告，仍然进入 Play。</summary>
        [InspectorName("只输出警告")]
        Warn,

        /// <summary>存在不在暂缓清单中的不符合项时阻止进入 Play。</summary>
        [InspectorName("阻止进入 Play")]
        Block,
    }

    /// <summary>项目级设置，保存在 ProjectSettings/EasyReloadGuardSettings.asset，随版本管理共享。</summary>
    [FilePath("ProjectSettings/EasyReloadGuardSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ReloadGuardSettings : ScriptableSingleton<ReloadGuardSettings>
    {
        [Tooltip("关闭 Domain Reload 时，进入 Play 前用最近一次检查结果判断：有不在暂缓清单中的不符合项时只输出警告，或阻止进入 Play。")]
        public PrePlayCheck prePlayCheck = PrePlayCheck.Warn;

        [Tooltip("进入 Play 的重置有步骤失败时，本次 Play 作废：退出、重新加载脚本后自动重新进入（再次失败则只退出）。退出 Play 的释放失败时，下次进入前重新加载脚本。关闭后重置失败只退出 Play。")]
        public bool reloadOnResetFailure = true;

        [Tooltip("在 Console 输出每次 Play 的重置与释放统计。")]
        public bool verboseLog;

        [Tooltip("关闭 Domain Reload 时，每次停止 Play 后检查：静态字段是否还引用已销毁的对象，订阅和集合是否每次 Play 都在增加。")]
        public bool residueProbe = true;

        [Tooltip("停止后检查也读取静态构造有副作用的项目类型（只创建集合、字符串、值类型的静态构造总是读取）。读取会触发尚未执行过的静态构造，默认关闭。")]
        public bool residueProbeReadTypeInitializers;

        [Tooltip("不参与检查的包名（如 com.unity.render-pipelines.universal）。默认检查 Assets 与全部包。")]
        public List<string> excludedPackages = new List<string>();

        [Tooltip("不参与检查的程序集名。")]
        public List<string> excludedAssemblies = new List<string>();

        public void SaveAndInvalidate()
        {
            Save(true);
            ReloadAudit.Invalidate();
        }
    }
}
