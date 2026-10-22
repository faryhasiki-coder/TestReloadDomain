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

        [Tooltip("额外纳入检查的包名（如 com.company.gameplay）。默认只检查 Assets 下的程序集。")]
        public List<string> includedPackages = new List<string>();

        [Tooltip("不参与检查的程序集名。")]
        public List<string> excludedAssemblies = new List<string>();

        public void SaveAndInvalidate()
        {
            Save(true);
            ReloadAudit.Invalidate();
        }
    }
}
