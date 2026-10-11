using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Faryhasiki.PlaySessions
{
    /// <summary>A single session, with deterministic dependency order and reverse-order rollback.</summary>
    public sealed class SessionHost
    {
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly TimeSpan shutdownTimeout;
        private readonly List<SessionIssue> issues = new List<SessionIssue>();
        private readonly List<PlayModule> ordered = new List<PlayModule>();
        private readonly List<SessionScope> scopes = new List<SessionScope>();
        private readonly Stopwatch shutdown = new Stopwatch();
        private bool stopping;
        private bool finalized;
        private bool timeoutReported;
        private bool cleanupFailed;

        public Guid Id { get; } = Guid.NewGuid();
        public SessionStatus Status { get; private set; } = SessionStatus.Idle;
        public IReadOnlyList<SessionIssue> Issues => issues.AsReadOnly();
        public IReadOnlyList<SessionScope> Scopes => scopes.AsReadOnly();
        public int PendingTasks => scopes.Sum(scope => scope.PendingTasks);
        public int ResourceCount => scopes.Sum(scope => scope.ResourceCount);
        public int RejectedCommits => scopes.Sum(scope => scope.RejectedCommits);
        public bool CanStartNext => finalized && !cleanupFailed && PendingTasks == 0;

        public SessionHost(TimeSpan? shutdownTimeout = null)
        {
            this.shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5);
            if (this.shutdownTimeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
        }

        public bool Start(SessionPlan plan)
        {
            EnsureOwner();
            if (Status != SessionStatus.Idle) throw new InvalidOperationException("A SessionHost represents exactly one Play session.");
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            Status = SessionStatus.Starting;
            ordered.AddRange(ValidatePlan(plan, issues));
            if (HasErrors()) { Status = SessionStatus.Faulted; finalized = true; return false; }

            foreach (var module in ordered) scopes.Add(new SessionScope(Id, module.Id));
            var owner = "reset";
            try
            {
                foreach (var module in ordered) { owner = module.Id; module.Reset(); }
                for (var i = 0; i < ordered.Count; ++i) { owner = ordered[i].Id; ordered[i].Start(scopes[i]); }
                Status = SessionStatus.Ready;
                return true;
            }
            catch (Exception exception)
            {
                issues.Add(new SessionIssue("PLAY202", owner, exception.ToString()));
                Stop();
                return false;
            }
        }

        public bool TryExecute(Guid sessionId, Action action)
        {
            EnsureOwner();
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (Status != SessionStatus.Ready || sessionId != Id) return false;
            action();
            return true;
        }

        public void Stop()
        {
            EnsureOwner();
            if (stopping || finalized) return;
            stopping = true;
            Status = SessionStatus.Stopping;
            shutdown.Start();
            foreach (var scope in scopes) scope.Retire();
            for (var i = scopes.Count - 1; i >= 0; --i) scopes[i].Cancel();
            Pump();
        }

        public void Pump()
        {
            EnsureOwner();
            if (Status == SessionStatus.Ready)
                foreach (var scope in scopes) scope.PumpCommits();
            CollectIssues();
            if (Status == SessionStatus.Ready && HasErrors()) { Stop(); return; }
            if (!stopping || finalized) return;

            // Keep dependencies alive until every dependent scope has drained and released.
            for (var i = scopes.Count - 1; i >= 0; --i)
            {
                if (scopes[i].PendingTasks != 0) break;
                scopes[i].Release();
            }
            CollectIssues();
            if (scopes.All(scope => scope.IsStopped))
            {
                for (var i = ordered.Count - 1; i >= 0; --i)
                {
                    var count = issues.Count;
                    try { ordered[i].VerifyStopped(issues); }
                    catch (Exception exception) { issues.Add(new SessionIssue("PLAY303", ordered[i].Id, exception.ToString())); }
                    if (issues.Skip(count).Any(issue => issue.Severity == IssueSeverity.Error)) cleanupFailed = true;
                }
                finalized = true;
                shutdown.Stop();
                Status = HasErrors() ? SessionStatus.Faulted : SessionStatus.Stopped;
            }
            else if (!timeoutReported && shutdown.Elapsed >= shutdownTimeout)
            {
                timeoutReported = true;
                cleanupFailed = true;
                var pending = string.Join(", ", scopes.Where(scope => scope.PendingTasks > 0).Select(scope => scope.ModuleId));
                issues.Add(new SessionIssue("PLAY304", pending, "Shutdown timed out. Resources remain owned; drain the work and reload the Editor domain before another session."));
                Status = SessionStatus.Faulted;
            }
        }

        public static IReadOnlyList<PlayModule> ValidatePlan(SessionPlan plan, ICollection<SessionIssue> issues)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (issues == null) throw new ArgumentNullException(nameof(issues));
            var byId = new Dictionary<string, PlayModule>(StringComparer.Ordinal);
            foreach (var module in plan.Modules)
            {
                if (string.IsNullOrWhiteSpace(module.Id) || byId.ContainsKey(module.Id))
                    issues.Add(new SessionIssue("PLAY101", module.Id, "Module IDs must be non-empty and unique."));
                else byId.Add(module.Id, module);
            }
            var result = new List<PlayModule>();
            var marks = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var module in plan.Modules)
                if (!string.IsNullOrWhiteSpace(module.Id) && byId.TryGetValue(module.Id, out var registered) && ReferenceEquals(module, registered))
                    Visit(module, byId, marks, result, issues);
            foreach (var module in result)
            {
                try { module.Validate(issues); }
                catch (Exception exception) { issues.Add(new SessionIssue("PLAY102", module.Id, exception.ToString())); }
            }
            return result;
        }

        private static void Visit(PlayModule module, Dictionary<string, PlayModule> byId,
            Dictionary<string, int> marks, List<PlayModule> result, ICollection<SessionIssue> issues)
        {
            if (marks.TryGetValue(module.Id, out var mark))
            {
                if (mark == 1) issues.Add(new SessionIssue("PLAY103", module.Id, "Cyclic module dependency."));
                return;
            }
            marks[module.Id] = 1;
            foreach (var dependency in module.Dependencies ?? Array.Empty<string>())
            {
                if (dependency != null && byId.TryGetValue(dependency, out var target)) Visit(target, byId, marks, result, issues);
                else issues.Add(new SessionIssue("PLAY104", module.Id, $"Missing dependency: {dependency}"));
            }
            marks[module.Id] = 2;
            result.Add(module);
        }

        private bool HasErrors() => issues.Any(issue => issue.Severity == IssueSeverity.Error);

        private void CollectIssues()
        {
            var count = issues.Count;
            foreach (var scope in scopes) scope.DrainIssues(issues);
            if (issues.Skip(count).Any(issue => issue.Code.StartsWith("PLAY3", StringComparison.Ordinal))) cleanupFailed = true;
        }

        private void EnsureOwner()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("SessionHost must be called on its creating thread.");
        }
    }
}
