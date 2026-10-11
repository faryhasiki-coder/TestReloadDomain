using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Faryhasiki.PlaySessions
{
    public sealed class SessionCatalog
    {
        public SessionPlan Plan { get; } = new SessionPlan();
        public List<Assembly> ManagedAssemblies { get; } = new List<Assembly>();
        public List<SessionIssue> Issues { get; } = new List<SessionIssue>();
    }

    public static class CompositionDiscovery
    {
        public static SessionCatalog Discover()
        {
            var catalog = new SessionCatalog();
            // Only assembly-level opt-ins are discovered. Unknown static fields are never read or mutated.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).OrderBy(a => a.FullName, StringComparer.Ordinal))
            {
                var declaration = assembly.GetCustomAttribute<PlaySessionAssemblyAttribute>();
                if (declaration == null) continue;
                catalog.ManagedAssemblies.Add(assembly);
                var type = declaration.CompositionType;
                if (type == null || type.IsAbstract || type.Assembly != assembly ||
                    !typeof(ISessionComposition).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    catalog.Issues.Add(new SessionIssue("PLAY105", assembly.GetName().Name,
                        "Composition must be a concrete ISessionComposition in this assembly with a public parameterless constructor."));
                    continue;
                }
                try { ((ISessionComposition)Activator.CreateInstance(type)).Configure(catalog.Plan); }
                catch (Exception exception) { catalog.Issues.Add(new SessionIssue("PLAY106", type.FullName, exception.ToString())); }
            }
            return catalog;
        }
    }
}
