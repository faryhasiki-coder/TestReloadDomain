using System;
using System.Collections.Generic;
using System.Linq;

namespace Faryhasiki.PlaySessions
{
    public sealed class SessionModule
    {
        public string Id { get; }
        public Action<SessionScope> Reset { get; }
        public Action<SessionScope> Start { get; }
        public Func<string> Validate { get; }
        public IReadOnlyList<string> Requires { get; }

        internal SessionModule(string id, Action<SessionScope> reset, Action<SessionScope> start,
            Func<string> validate, string[] requires)
        {
            Id = id; Reset = reset; Start = start; Validate = validate;
            Requires = Array.AsReadOnly((string[])requires.Clone());
        }
    }

    // Configuration and validation must not modify runtime state.
    public sealed class SessionPlan
    {
        readonly Dictionary<string, SessionModule> modules = new Dictionary<string, SessionModule>(StringComparer.Ordinal);
        readonly Dictionary<Type, string> stateOwners = new Dictionary<Type, string>();
        public bool Contains(string id) => id != null && modules.ContainsKey(id);

        public void CoverStatics(Type type, string moduleId, string evidence)
        {
            if (type == null || type.ContainsGenericParameters) throw new ArgumentException("A closed state type is required.");
            if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("State coverage needs a module ID and review evidence.");
            if (stateOwners.ContainsKey(type)) throw new InvalidOperationException("Duplicate static state owner: " + type.FullName);
            stateOwners.Add(type, moduleId);
        }

        public string GetStaticOwner(Type type) => stateOwners.TryGetValue(type, out var owner) ? owner : null;

        public void Add(string id, Action<SessionScope> reset, Action<SessionScope> start = null,
            Func<string> validate = null, params string[] requires)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Module ID is required.");
            if (reset == null) throw new ArgumentNullException(nameof(reset));
            if (modules.ContainsKey(id)) throw new InvalidOperationException("Duplicate module: " + id);
            modules.Add(id, new SessionModule(id, reset, start, validate, requires ?? Array.Empty<string>()));
        }

        public IReadOnlyList<SessionModule> Resolve()
        {
            var result = new List<SessionModule>();
            var marks = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in modules.Keys.OrderBy(x => x, StringComparer.Ordinal)) Visit(id, marks, result);
            return result.AsReadOnly();
        }

        void Visit(string id, Dictionary<string, int> marks, List<SessionModule> result)
        {
            if (id == null || !modules.TryGetValue(id, out var module))
                throw new InvalidOperationException("Missing dependency: " + (id ?? "<null>"));
            if (marks.TryGetValue(id, out var mark))
            {
                if (mark == 1) throw new InvalidOperationException("Dependency cycle at: " + id);
                return;
            }
            marks[id] = 1;
            foreach (var dependency in module.Requires) Visit(dependency, marks, result);
            marks[id] = 2;
            result.Add(module);
        }

        public void Validate()
        {
            foreach (var owner in stateOwners)
                if (!Contains(owner.Value)) throw new InvalidOperationException("State owner missing: " + owner.Key.FullName + " -> " + owner.Value);
            foreach (var module in Resolve())
            {
                var error = module.Validate?.Invoke();
                if (!string.IsNullOrWhiteSpace(error))
                    throw new InvalidOperationException(module.Id + ": " + error);
            }
        }
    }
}
