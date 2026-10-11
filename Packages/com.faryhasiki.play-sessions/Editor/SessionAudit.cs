using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Faryhasiki.PlaySessions.Editor
{
    /// <summary>Read-only declaration audit. It does not prove Reset or Dispose implementations correct.</summary>
    public static class SessionAudit
    {
        public static List<SessionIssue> Inspect(SessionCatalog catalog)
        {
            var issues = new List<SessionIssue>(catalog.Issues);
            SessionHost.ValidatePlan(catalog.Plan, issues);
            var moduleIds = new HashSet<string>(catalog.Plan.Modules.Select(module => module.Id), StringComparer.Ordinal);
            foreach (var assembly in catalog.ManagedAssemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException exception)
                {
                    issues.Add(new SessionIssue("PLAY108", assembly.GetName().Name, exception.ToString()));
                    continue;
                }
                foreach (var type in types) InspectType(type, moduleIds, issues);
            }
            return issues;
        }

        public static void InspectType(Type type, ISet<string> moduleIds, ICollection<SessionIssue> issues)
        {
            // Compiler-generated noncapturing delegate caches are implementation details. User type
            // backing fields are NOT skipped: static auto-properties and events still need ownership.
            if (type.IsDefined(typeof(CompilerGeneratedAttribute), false)) return;
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsLiteral || (field.IsInitOnly && IsKnownImmutable(field.FieldType))) continue;
                var owned = field.GetCustomAttribute<SessionOwnedAttribute>();
                var persistent = field.GetCustomAttribute<SessionPersistentAttribute>();
                var owner = $"{type.FullName}.{field.Name}";
                if (owned != null && persistent != null)
                    issues.Add(new SessionIssue("PLAY109", owner, "Choose exactly one lifetime: SessionOwned or SessionPersistent."));
                else if (owned != null)
                {
                    if (string.IsNullOrWhiteSpace(owned.ModuleId) || !moduleIds.Contains(owned.ModuleId))
                        issues.Add(new SessionIssue("PLAY110", owner, $"Owner module '{owned.ModuleId}' is not registered."));
                    if (type.ContainsGenericParameters)
                        issues.Add(new SessionIssue("PLAY111", owner, "Session-owned generic statics require an explicit closed-type adapter; move state into a session instance."));
                }
                else if (persistent != null)
                {
                    if (string.IsNullOrWhiteSpace(persistent.Reason))
                        issues.Add(new SessionIssue("PLAY112", owner, "A persistent field requires a concrete lifetime reason."));
                }
                else issues.Add(new SessionIssue("PLAY113", owner,
                    "Mutable static state has no owner. Move it into a session instance, or declare SessionOwned(moduleId) / SessionPersistent(reason)."));
            }
        }

        private static bool IsKnownImmutable(Type type)
            => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
               type == typeof(Guid) || type == typeof(DateTime) || type == typeof(TimeSpan);
    }
}
