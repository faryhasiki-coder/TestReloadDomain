using System;
using UnityEngine;

namespace Faryhasiki.PlaySessions
{
    // Only the Editor invokes the automatic bridge. Player behavior is deliberately unchanged.
    public static class PlaySession
    {
        public static SessionHost Host { get; } = new SessionHost();
        public static string BridgeError { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Prepare()
        {
#if UNITY_EDITOR
            try
            {
                if (BridgeError != null) throw new InvalidOperationException(BridgeError);
                if (!Host.Prepare(SessionCatalog.Build(AppDomain.CurrentDomain.GetAssemblies()))) ReportFailure();
                Application.quitting -= Stop;
                Application.quitting += Stop;
            }
            catch (Exception ex) { BridgeError = ex.GetBaseException().Message; Debug.LogError("[Play Sessions] " + BridgeError); }
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Start()
        {
#if UNITY_EDITOR
            if (Host.State == SessionState.Prepared && !Host.Start()) ReportFailure();
#endif
        }

        public static void Stop() => Host.Stop();
        static void ReportFailure() => Debug.LogError("[Play Sessions] " + string.Join("\n", Host.Errors));
    }
}
