using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Faryhasiki.PlaySessions
{
    /// <summary>
    /// Own/RunBackground and lifecycle operations run on the creating thread. Background work must use
    /// CommitAsync for shared/Unity state. Retirement fences commits before cancellation; resources remain
    /// owned until all tracked tasks finish. No synchronous waits on Unity's main thread.
    /// </summary>
    public sealed class SessionScope
    {
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly List<IDisposable> resources = new List<IDisposable>();
        private readonly List<IDisposable> retirementActions = new List<IDisposable>();
        private readonly ConcurrentQueue<Commit> commits = new ConcurrentQueue<Commit>();
        private readonly ConcurrentQueue<SessionIssue> issues = new ConcurrentQueue<SessionIssue>();
        private int active = 1;
        private int pendingTasks;
        private int rejectedCommits;
        private bool cancellationRequested;
        private bool released;

        public Guid SessionId { get; }
        public string ModuleId { get; }
        public bool IsActive => Volatile.Read(ref active) == 1;
        public int PendingTasks => Volatile.Read(ref pendingTasks);
        public int RejectedCommits => Volatile.Read(ref rejectedCommits);
        public int ResourceCount => resources.Count + retirementActions.Count;
        public bool IsStopped => released;

        internal SessionScope(Guid sessionId, string moduleId)
        {
            SessionId = sessionId;
            ModuleId = moduleId;
        }

        public T Own<T>(T resource) where T : IDisposable
        {
            EnsureOwner();
            EnsureActive();
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            resources.Add(resource);
            return resource;
        }

        public IDisposable OnDispose(Action release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            return Own(new ReleaseAction(release));
        }

        /// <summary>Detach event sources immediately after every scope is fenced, before waiting for workers.</summary>
        public IDisposable OnRetire(Action detach)
        {
            EnsureOwner();
            EnsureActive();
            if (detach == null) throw new ArgumentNullException(nameof(detach));
            var action = new ReleaseAction(detach);
            retirementActions.Add(action);
            return action;
        }

        public void RunBackground(Func<CancellationToken, Task> work)
        {
            EnsureOwner();
            EnsureActive();
            if (work == null) throw new ArgumentNullException(nameof(work));
            var token = cancellation.Token;
            Interlocked.Increment(ref pendingTasks);
            _ = Task.Run(async () =>
            {
                try { await work(token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    issues.Enqueue(new SessionIssue("PLAY201", ModuleId, exception.ToString()));
                }
                finally { Interlocked.Decrement(ref pendingTasks); }
            });
        }

        public Task<bool> CommitAsync(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (!IsActive)
            {
                Interlocked.Increment(ref rejectedCommits);
                return Task.FromResult(false);
            }
            var commit = new Commit(action);
            commits.Enqueue(commit);
            // Close the race where retirement drained the queue before this enqueue.
            if (!IsActive) RejectQueuedCommits();
            return commit.Completion.Task;
        }

        internal void PumpCommits()
        {
            EnsureOwner();
            var budget = 64;
            while (budget-- > 0 && commits.TryDequeue(out var commit))
            {
                if (!IsActive) Reject(commit);
                else
                {
                    try { commit.Action(); commit.Completion.TrySetResult(true); }
                    catch (Exception exception)
                    {
                        issues.Enqueue(new SessionIssue("PLAY203", ModuleId, exception.ToString()));
                        commit.Completion.TrySetException(exception);
                    }
                }
            }
        }

        // All scopes must be fenced before invoking any user cancellation callback.
        internal void Retire()
        {
            EnsureOwner();
            Interlocked.Exchange(ref active, 0);
            RejectQueuedCommits();
        }

        internal void Cancel()
        {
            EnsureOwner();
            if (cancellationRequested) return;
            cancellationRequested = true;
            for (var i = retirementActions.Count - 1; i >= 0; --i)
            {
                try { retirementActions[i].Dispose(); }
                catch (Exception exception) { issues.Enqueue(new SessionIssue("PLAY307", ModuleId, exception.ToString())); }
            }
            retirementActions.Clear();
            try { cancellation.Cancel(); }
            catch (Exception exception) { issues.Enqueue(new SessionIssue("PLAY301", ModuleId, exception.ToString())); }
        }

        internal void Release()
        {
            EnsureOwner();
            if (released || IsActive || PendingTasks != 0) return;
            RejectQueuedCommits();
            for (var i = resources.Count - 1; i >= 0; --i)
            {
                try { resources[i].Dispose(); }
                catch (Exception exception) { issues.Enqueue(new SessionIssue("PLAY302", ModuleId, exception.ToString())); }
            }
            resources.Clear();
            cancellation.Dispose();
            released = true;
        }

        internal void DrainIssues(ICollection<SessionIssue> target)
        {
            while (issues.TryDequeue(out var issue)) target.Add(issue);
        }

        private void RejectQueuedCommits()
        {
            while (commits.TryDequeue(out var commit)) Reject(commit);
        }

        private void Reject(Commit commit)
        {
            if (commit.Completion.TrySetResult(false)) Interlocked.Increment(ref rejectedCommits);
        }

        private void EnsureOwner()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Lifecycle and resource ownership require the session's main thread. Use CommitAsync from background work.");
        }

        private void EnsureActive()
        {
            if (!IsActive) throw new InvalidOperationException("The Play session has retired.");
        }

        private sealed class Commit
        {
            internal readonly Action Action;
            internal readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Commit(Action action) => Action = action;
        }

        private sealed class ReleaseAction : IDisposable
        {
            private Action release;
            internal ReleaseAction(Action release) => this.release = release;
            public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
        }
    }
}
