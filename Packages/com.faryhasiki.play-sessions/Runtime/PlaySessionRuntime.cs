using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Scripting;

[assembly: AlwaysLinkAssembly]

namespace Faryhasiki.PlaySessions
{
    public static class PlaySessionRuntime
    {
        private static Func<SessionCatalog, IEnumerable<SessionIssue>> environmentValidation;
        private static bool mayStart;
        private static int reportedIssues;
        private static readonly List<SessionIssue> bootIssues = new List<SessionIssue>();

        public static SessionHost Current { get; private set; }
        public static IReadOnlyList<SessionIssue> BootIssues => bootIssues.AsReadOnly();
        public static bool IsReady => Current != null && Current.Status == SessionStatus.Ready;

        // The Editor assembly supplies policy. The runtime assembly has no UnityEditor reference.
        public static void SetEnvironmentValidation(Func<SessionCatalog, IEnumerable<SessionIssue>> validator)
            => environmentValidation = validator;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void BeginRun()
        {
            bootIssues.Clear();
            Shutdown();
            Pump();
            mayStart = Current == null || Current.CanStartNext;
            if (!mayStart)
            {
                ReportBoot(new SessionIssue("PLAY305", "previous session", "Previous cleanup is incomplete or failed. A new managed session cannot start."));
                return;
            }
            Current = null;
            reportedIssues = 0;
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartRun()
        {
            if (!mayStart) return;
            var catalog = CompositionDiscovery.Discover();
            var issues = new List<SessionIssue>(catalog.Issues);
            if (environmentValidation != null)
            {
                try { issues.AddRange(environmentValidation(catalog)); }
                catch (Exception exception) { issues.Add(new SessionIssue("PLAY107", "environment", exception.ToString())); }
            }
            if (issues.Any(issue => issue.Severity == IssueSeverity.Error))
            {
                foreach (var issue in issues) ReportBoot(issue);
                return;
            }
            // Installing the package alone does not inject a running service into an unconfigured project.
            if (catalog.Plan.Modules.Count == 0) return;
            Current = new SessionHost();
            Current.Start(catalog.Plan);
            var driver = new GameObject("Play Sessions").AddComponent<SessionDriver>();
            driver.Attach(Current);
            driver.gameObject.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(driver.gameObject);
            Pump();
        }

        public static bool TryExecute(Guid sessionId, Action action)
            => Current != null && Current.TryExecute(sessionId, action);

        public static void Shutdown() => Current?.Stop();

        public static void Pump()
        {
            if (Current == null) return;
            Current.Pump();
            while (reportedIssues < Current.Issues.Count)
            {
                var issue = Current.Issues[reportedIssues++];
                if (issue.Severity == IssueSeverity.Error) Debug.LogError(issue.ToString());
                else Debug.LogWarning(issue.ToString());
            }
        }

        private static void ReportBoot(SessionIssue issue)
        {
            bootIssues.Add(issue);
            Debug.LogError(issue.ToString());
        }
    }
}
