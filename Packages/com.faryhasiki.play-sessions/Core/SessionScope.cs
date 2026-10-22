using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Faryhasiki.PlaySessions
{
    public sealed class SessionScope
    {
        readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly List<Action> cleanup = new List<Action>();
        readonly List<Task> tasks = new List<Task>();
        volatile bool active = true;
        internal bool ObservedTasks;
        bool retired;
        public Guid SessionId { get; }
        public string ModuleId { get; }
        public bool IsCurrent => active;
        public CancellationToken Token { get; }

        internal SessionScope(Guid sessionId, string moduleId)
        {
            SessionId = sessionId; ModuleId = moduleId; Token = cancellation.Token;
        }

        public void OnStop(Action action)
        {
            RequireActive();
            cleanup.Add(action ?? throw new ArgumentNullException(nameof(action)));
        }

        public T Own<T>(T resource) where T : IDisposable
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            OnStop(resource.Dispose);
            return resource;
        }

        // Register work on the owning thread; workers may only compute isolated data.
        public Task Track(Task task)
        {
            RequireActive();
            tasks.Add(task ?? throw new ArgumentNullException(nameof(task)));
            return task;
        }

        // Invoke on the main thread (e.g. after await in a Unity callback).
        // A captured old scope can never publish into a newer session.
        public bool TryCommit(Action publish)
        {
            RequireThread();
            if (!active) return false;
            if (publish == null) throw new ArgumentNullException(nameof(publish));
            publish();
            return true;
        }

        internal void Invalidate() { active = false; }
        internal void Retire(List<string> errors)
        {
            if (retired) return;
            retired = true;
            try { cancellation.Cancel(); }
            catch (Exception ex) { errors.Add(ModuleId + " cancellation: " + ex.GetBaseException().Message); }
            for (int i = cleanup.Count - 1; i >= 0; --i)
            {
                try { cleanup[i](); }
                catch (Exception ex) { errors.Add(ModuleId + " cleanup: " + ex.GetBaseException().Message); }
            }
            cleanup.Clear();
        }

        internal bool PollTasks(List<string> errors)
        {
            if (ObservedTasks) return true;
            if (tasks.Any(task => !task.IsCompleted)) return false;
            foreach (var task in tasks)
                if (task.IsFaulted) errors.Add(ModuleId + " task: " + task.Exception.GetBaseException().Message);
            ObservedTasks = true;
            cancellation.Dispose();
            return true;
        }

        void RequireActive()
        {
            RequireThread();
            if (!active) throw new InvalidOperationException("Session has retired: " + ModuleId);
        }

        void RequireThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Session API must run on its owning thread.");
        }
    }
}
