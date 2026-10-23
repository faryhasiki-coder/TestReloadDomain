using System;
using UnityEngine;

namespace EasyReloadGuard.Editor
{
    internal static class Log
    {
        const string k_Prefix = "[Easy Reload Guard] ";

        /// <summary>仅在设置开启详细日志时输出。</summary>
        public static void Verbose(string message)
        {
            if (ReloadGuardSettings.instance.verboseLog) Debug.Log(k_Prefix + message);
        }

        public static void Notice(string message) => Debug.Log(k_Prefix + message);

        public static void Warning(string message) => Debug.LogWarning(k_Prefix + message);

        public static void Error(string message) => Debug.LogError(k_Prefix + message);

        public static void Exception(string context, Exception exception)
        {
            Debug.LogError(k_Prefix + context);
            Debug.LogException(exception);
        }
    }
}
