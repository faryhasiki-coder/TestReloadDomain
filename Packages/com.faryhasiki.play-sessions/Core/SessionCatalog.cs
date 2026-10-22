using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Faryhasiki.PlaySessions
{
    public static class SessionCatalog
    {
        public const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        public static SessionPlan Build(IEnumerable<Assembly> assemblies)
        {
            var plan = new SessionPlan();
            foreach (var assembly in assemblies.Where(a => !a.IsDynamic).OrderBy(a => a.FullName, StringComparer.Ordinal))
            {
                foreach (var adapter in assembly.GetCustomAttributes<PlaySessionAdapterAttribute>())
                {
                    var type = adapter.AdapterType;
                    if (type == null || !typeof(IPlaySessionAdapter).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters)
                        throw new InvalidOperationException("Invalid session adapter in " + assembly.FullName);
                    ((IPlaySessionAdapter)Activator.CreateInstance(type)).Configure(plan);
                }
                if (!assembly.IsDefined(typeof(PlaySessionAssemblyAttribute), false)) continue;
                foreach (var type in assembly.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    foreach (var field in type.GetFields(DeclaredMembers).Where(f => f.IsDefined(typeof(ResetOnPlayAttribute), false)))
                    {
                        if (!CanReset(field)) throw new InvalidOperationException("[ResetOnPlay] requires a non-readonly static scalar/string on a closed type: " + field);
                        var initial = field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null;
                        plan.Add(FieldId(field), _ => field.SetValue(null, initial));
                    }
                    foreach (var method in type.GetMethods(DeclaredMembers))
                    {
                        var declaration = method.GetCustomAttribute<SessionResetAttribute>();
                        if (declaration == null) continue;
                        var args = method.GetParameters();
                        if (!method.IsStatic || method.ReturnType != typeof(void) || method.ContainsGenericParameters || type.ContainsGenericParameters ||
                            method.IsDefined(typeof(AsyncStateMachineAttribute), false) ||
                            !(args.Length == 0 || args.Length == 1 && args[0].ParameterType == typeof(SessionScope)))
                            throw new InvalidOperationException("Invalid [SessionReset] signature (static synchronous void, optional SessionScope): " + type.FullName + "." + method.Name);
                        Action<SessionScope> reset;
                        if (args.Length == 0)
                        {
                            var action = (Action)Delegate.CreateDelegate(typeof(Action), method);
                            reset = _ => action();
                        }
                        else reset = (Action<SessionScope>)Delegate.CreateDelegate(typeof(Action<SessionScope>), method);
                        plan.Add(declaration.Id, reset, requires: declaration.Requires);
                    }
                }
            }
            plan.Validate();
            return plan;
        }

        public static string FieldId(FieldInfo field) => "field:" + field.DeclaringType.Assembly.GetName().Name + ":" + field.DeclaringType.FullName + "." + field.Name;

        public static bool CanReset(FieldInfo field) => field.IsStatic && !field.IsInitOnly && !field.IsLiteral &&
            !field.DeclaringType.ContainsGenericParameters && !field.IsDefined(typeof(ThreadStaticAttribute), false) &&
            (field.FieldType.IsPrimitive && field.FieldType != typeof(IntPtr) && field.FieldType != typeof(UIntPtr) ||
             field.FieldType.IsEnum || field.FieldType == typeof(string) || field.FieldType == typeof(decimal));
    }

    [Serializable]
    public sealed class SessionDiagnostic
    {
        public string severity;
        public string code;
        public string target;
        public string message;
        public SessionDiagnostic(string severity, string code, string target, string message)
        { this.severity = severity; this.code = code; this.target = target; this.message = message; }
        public override string ToString() => severity + " " + code + " " + target + ": " + message;
    }

    public static class SessionAudit
    {
        // Metadata-only: no field values are read, and no unknown state is mutated.
        public static List<SessionDiagnostic> Scan(IEnumerable<Assembly> assemblies, SessionPlan plan)
        {
            var results = new List<SessionDiagnostic>();
            foreach (var assembly in assemblies.Where(a => !a.IsDynamic))
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    results.Add(new SessionDiagnostic("Error", "PS102", assembly.FullName, "Type discovery incomplete: " + ex.Message));
                    types = ex.Types.Where(t => t != null).ToArray();
                }
                foreach (var type in types)
                {
                    if (type.IsDefined(typeof(CompilerGeneratedAttribute), false)) continue;
                    foreach (var field in type.GetFields(SessionCatalog.DeclaredMembers).Where(f => f.IsStatic && !f.IsLiteral))
                    {
                        var target = type.FullName + "." + field.Name;
                        var reset = field.IsDefined(typeof(ResetOnPlayAttribute), false);
                        var owner = field.GetCustomAttribute<SessionOwnedAttribute>() ?? type.GetCustomAttribute<SessionOwnedAttribute>();
                        if (owner == null && plan.GetStaticOwner(type) != null) owner = new SessionOwnedAttribute(plan.GetStaticOwner(type));
                        var persistent = field.GetCustomAttribute<SessionPersistentAttribute>() ?? type.GetCustomAttribute<SessionPersistentAttribute>();
                        if ((reset ? 1 : 0) + (owner != null ? 1 : 0) + (persistent != null ? 1 : 0) > 1)
                            results.Add(new SessionDiagnostic("Error", "PS103", target, "Conflicting state declarations."));
                        else if (reset && (!SessionCatalog.CanReset(field) || !plan.Contains(SessionCatalog.FieldId(field))))
                            results.Add(new SessionDiagnostic("Error", "PS104", target, "Reset field is invalid or assembly is not opted in."));
                        else if (owner != null && !plan.Contains(owner.ModuleId))
                            results.Add(new SessionDiagnostic("Error", "PS105", target, "Owner module is not registered: " + owner.ModuleId));
                        else if (persistent != null && string.IsNullOrWhiteSpace(persistent.Reason))
                            results.Add(new SessionDiagnostic("Error", "PS106", target, "Persistence needs a reason."));
                        else if (!reset && owner == null && persistent == null &&
                            !(field.IsInitOnly && (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string))))
                            results.Add(new SessionDiagnostic("Warning", "PS101", target, "Unclassified static state. Declare an owner/reset/persistence reason or review through an adapter. readonly collections still contain mutable state."));
                    }
                }
            }
            return results;
        }
    }
}
