using UnityEngine;

namespace Faryhasiki.PlaySessions
{
    internal sealed class SessionDriver : MonoBehaviour
    {
        private SessionHost host;
        internal void Attach(SessionHost session) => host = session;
        private void Update() => PlaySessionRuntime.Pump();
        private void OnDestroy()
        {
            if (ReferenceEquals(host, PlaySessionRuntime.Current)) PlaySessionRuntime.Shutdown();
        }
    }
}
