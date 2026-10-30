using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyReloadGuard.Editor
{
    internal enum PrePlayCheck
    {
        /// <summary>不在进入 Play 前检查（仍可手动或 CLI 检查）。</summary>
        Off,

        /// <summary>存在未基线错误时输出警告并放行。</summary>
        Warn,

        /// <summary>存在未基线错误时阻止进入 Play。</summary>
        Block,
    }

    /// <summary>项目级设置，保存在 ProjectSettings/EasyReloadGuardSettings.asset，随版本管理共享。</summary>
    [FilePath("ProjectSettings/EasyReloadGuardSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ReloadGuardSettings : ScriptableSingleton<ReloadGuardSettings>
    {
        public PrePlayCheck prePlayCheck = PrePlayCheck.Warn;
        public bool reloadOnResetFailure = true;
        public bool verboseLog;

        [Tooltip("关闭 Domain Reload 时，每次退出 Play 回到编辑模式后探测已销毁对象的引用与跨会话增长的订阅/集合。")]
        public bool residueProbe = true;

        [Tooltip("残留探测也读取有静态构造的项目类型。读取会触发尚未执行过的静态构造（例如本次 Play 没用到的类型），默认关闭。")]
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
