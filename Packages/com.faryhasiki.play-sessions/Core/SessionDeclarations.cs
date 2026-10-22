using System;

namespace Faryhasiki.PlaySessions
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class PlaySessionAssemblyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ResetOnPlayAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SessionResetAttribute : Attribute
    {
        public string Id { get; }
        public string[] Requires { get; }
        public SessionResetAttribute(string id, params string[] requires) { Id = id; Requires = requires; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class SessionOwnedAttribute : Attribute
    {
        public string ModuleId { get; }
        public SessionOwnedAttribute(string moduleId) { ModuleId = moduleId; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class SessionPersistentAttribute : Attribute
    {
        public string Reason { get; }
        public SessionPersistentAttribute(string reason) { Reason = reason; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class PlaySessionAdapterAttribute : Attribute
    {
        public Type AdapterType { get; }
        public PlaySessionAdapterAttribute(Type adapterType) { AdapterType = adapterType; }
    }

    public interface IPlaySessionAdapter { void Configure(SessionPlan plan); }

    public enum DependencyKind { Package, ManagedAssembly }
    public enum DependencyHandling { Adapter, SelfManaged, FullReloadRequired, EditorRestartRequired }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class PlaySessionCompatibilityAttribute : Attribute
    {
        public DependencyKind Kind { get; }
        public string Name { get; }
        public string Version { get; }
        public DependencyHandling Handling { get; }
        public string Evidence { get; }
        public string ModuleId { get; set; }
        public string Sha256 { get; set; }
        public bool Optional { get; set; }

        public PlaySessionCompatibilityAttribute(DependencyKind kind, string name, string version,
            DependencyHandling handling, string evidence)
        {
            Kind = kind; Name = name; Version = version; Handling = handling; Evidence = evidence;
        }
    }
}
