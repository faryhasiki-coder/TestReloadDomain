using System;
using System.Collections.Generic;

namespace Faryhasiki.PlaySessions
{
    public enum SessionStatus { Idle, Starting, Ready, Stopping, Stopped, Faulted }
    public enum IssueSeverity { Warning, Error }

    public sealed class SessionIssue
    {
        public string Code { get; }
        public string Owner { get; }
        public string Message { get; }
        public IssueSeverity Severity { get; }

        public SessionIssue(string code, string owner, string message, IssueSeverity severity = IssueSeverity.Error)
        {
            Code = code;
            Owner = owner;
            Message = message;
            Severity = severity;
        }

        public override string ToString() => $"{Code} [{Owner}] {Message}";
    }

    /// <summary>One project-level composition root. Configure only declares modules; it must not acquire resources.</summary>
    public interface ISessionComposition
    {
        void Configure(SessionPlan plan);
    }

    /// <summary>Opt an assembly into discovery AND the Editor's static-state audit.</summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class PlaySessionAssemblyAttribute : Attribute
    {
        public Type CompositionType { get; }
        public PlaySessionAssemblyAttribute(Type compositionType) => CompositionType = compositionType;
    }

    /// <summary>The named module explicitly resets this field and releases any resources behind it.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SessionOwnedAttribute : Attribute
    {
        public string ModuleId { get; }
        public SessionOwnedAttribute(string moduleId) => ModuleId = moduleId;
    }

    /// <summary>A reviewed cross-Play lifetime, not a claim that the value survives assembly reload.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SessionPersistentAttribute : Attribute
    {
        public string Reason { get; }
        public SessionPersistentAttribute(string reason) => Reason = reason;
    }

    /// <summary>Adapters implement this contract; ordinary modules can use SessionPlan.Add callbacks.</summary>
    public abstract class PlayModule
    {
        public abstract string Id { get; }
        public virtual IReadOnlyList<string> Dependencies => Array.Empty<string>();
        public virtual void Validate(ICollection<SessionIssue> issues) { }
        // Reset is for owned values only. Acquire resources in Start and give them to its scope immediately.
        public virtual void Reset() { }
        public abstract void Start(SessionScope scope);
        public virtual void VerifyStopped(ICollection<SessionIssue> issues) { }
    }

    public sealed class SessionPlan
    {
        private readonly List<PlayModule> modules = new List<PlayModule>();
        public IReadOnlyList<PlayModule> Modules => modules.AsReadOnly();

        public SessionPlan Add(PlayModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            modules.Add(module);
            return this;
        }

        public SessionPlan Add(string id, Action<SessionScope> start, Action reset = null,
            Action<ICollection<SessionIssue>> verifyStopped = null, params string[] dependencies)
        {
            return Add(new CallbackModule(id, start, reset, verifyStopped, dependencies));
        }

        private sealed class CallbackModule : PlayModule
        {
            private readonly string id;
            private readonly Action<SessionScope> start;
            private readonly Action reset;
            private readonly Action<ICollection<SessionIssue>> verify;
            private readonly string[] dependencies;
            public override string Id => id;
            public override IReadOnlyList<string> Dependencies => dependencies;

            public CallbackModule(string id, Action<SessionScope> start, Action reset,
                Action<ICollection<SessionIssue>> verify, string[] dependencies)
            {
                this.id = id;
                this.start = start ?? throw new ArgumentNullException(nameof(start));
                this.reset = reset;
                this.verify = verify;
                this.dependencies = dependencies == null ? Array.Empty<string>() : (string[])dependencies.Clone();
            }

            public override void Reset() => reset?.Invoke();
            public override void Start(SessionScope scope) => start(scope);
            public override void VerifyStopped(ICollection<SessionIssue> issues) => verify?.Invoke(issues);
        }
    }
}
