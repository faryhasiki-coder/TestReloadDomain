using System;
using System.Collections.Generic;
using System.Linq;

namespace Faryhasiki.PlaySessions.Editor
{
    public sealed class InstalledDependency
    {
        public DependencyKind Kind;
        public string Name;
        public string Version;
        public string Sha256;
    }

    public static class CompatibilityCheck
    {
        public static List<SessionDiagnostic> Evaluate(IEnumerable<PlaySessionCompatibilityAttribute> declarations,
            IEnumerable<InstalledDependency> installed, SessionPlan plan, bool fastPlay, bool requireCoverage)
        {
            var result = new List<SessionDiagnostic>();
            var policies = declarations.ToArray();
            var dependencies = installed.ToArray();
            var seen = new HashSet<string>();
            foreach (var policy in policies)
            {
                var key = policy.Kind + ":" + policy.Name;
                if (!seen.Add(key)) { Error("PS204", key, "Duplicate compatibility declarations."); continue; }
                var matches = dependencies.Where(d => d.Kind == policy.Kind && d.Name == policy.Name).ToArray();
                if (matches.Length == 0 && policy.Optional) continue;
                if (matches.Length != 1) { Error("PS201", key, "Dependency missing or ambiguous."); continue; }
                var item = matches[0];
                if (string.IsNullOrWhiteSpace(policy.Evidence) || string.IsNullOrWhiteSpace(policy.Version))
                    Error("PS202", key, "Exact version and review evidence are required.");
                if (policy.Version != item.Version) Error("PS203", key, "Version changed: reviewed " + policy.Version + ", installed " + item.Version);
                if (policy.Kind == DependencyKind.ManagedAssembly &&
                    (string.IsNullOrWhiteSpace(policy.Sha256) || !string.Equals(policy.Sha256, item.Sha256, StringComparison.OrdinalIgnoreCase)))
                    Error("PS205", key, "DLL content hash is missing or changed. Review the adapter before accepting the new binary.");
                if (!Enum.IsDefined(typeof(DependencyHandling), policy.Handling)) Error("PS208", key, "Unknown dependency handling mode.");
                if (policy.Handling == DependencyHandling.Adapter && !plan.Contains(policy.ModuleId))
                    Error("PS206", key, "Adapter module not registered: " + policy.ModuleId);
                if (fastPlay && (policy.Handling == DependencyHandling.FullReloadRequired || policy.Handling == DependencyHandling.EditorRestartRequired))
                    Error("PS207", key, policy.Handling == DependencyHandling.EditorRestartRequired
                        ? "Native/process state requires an Editor restart. Fast Play is blocked."
                        : "This dependency requires Domain Reload. Enable Reload Domain to play.");
            }
            foreach (var item in dependencies)
                if (!seen.Contains(item.Kind + ":" + item.Name))
                    result.Add(new SessionDiagnostic(requireCoverage ? "Error" : "Info", "PS209", item.Kind + ":" + item.Name,
                        "Not reviewed; no compatibility claim. Version " + item.Version));
            return result;

            void Error(string code, string target, string message) => result.Add(new SessionDiagnostic("Error", code, target, message));
        }
    }
}
