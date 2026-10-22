using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Faryhasiki.PlaySessions
{
    public enum SessionState { Idle, Preparing, Prepared, Starting, Running, Stopping, Faulted }

    public sealed class SessionHost
    {
        readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        readonly List<SessionScope> scopes = new List<SessionScope>();
        readonly List<string> errors = new List<string>();
        readonly Stopwatch stopping = new Stopwatch();
        IReadOnlyList<SessionModule> modules;
        bool retiring;
        bool timedOut;
        public SessionState State { get; private set; }
        public Guid SessionId { get; private set; }
        public IReadOnlyList<string> Errors => errors.AsReadOnly();
        public TimeSpan StopTimeout { get; }

        public SessionHost(TimeSpan? stopTimeout = null)
        {
            StopTimeout = stopTimeout ?? TimeSpan.FromSeconds(2);
            if (StopTimeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(stopTimeout));
        }

        public bool Prepare(SessionPlan plan)
        {
            RequireThread();
            if (State != SessionState.Idle) throw new InvalidOperationException("Cannot prepare in state " + State);
            errors.Clear(); scopes.Clear(); SessionId = Guid.NewGuid(); State = SessionState.Preparing;
            try
            {
                // Validate the whole plan before the first state mutation.
                plan.Validate(); modules = plan.Resolve();
                foreach (var module in modules)
                {
                    var scope = new SessionScope(SessionId, module.Id);
                    scopes.Add(scope); // A partially failed reset must still be cleaned up.
                    try { module.Reset(scope); }
                    catch (Exception ex) { throw new InvalidOperationException(module.Id + " reset", ex); }
                    if (State != SessionState.Preparing) throw new InvalidOperationException(module.Id + " changed host lifecycle during Reset.");
                }
                State = SessionState.Prepared;
                return true;
            }
            catch (Exception ex) { Fail(ex); return false; }
        }

        public bool Start()
        {
            RequireThread();
            if (State != SessionState.Prepared) throw new InvalidOperationException("Session is not prepared.");
            State = SessionState.Starting;
            try
            {
                for (int i = 0; i < modules.Count; i++)
                {
                    try { modules[i].Start?.Invoke(scopes[i]); }
                    catch (Exception ex) { throw new InvalidOperationException(modules[i].Id + " start", ex); }
                    if (State != SessionState.Starting) throw new InvalidOperationException(modules[i].Id + " changed host lifecycle during Start.");
                }
                State = SessionState.Running;
                return true;
            }
            catch (Exception ex) { Fail(ex); return false; }
        }

        public bool Begin(SessionPlan plan) => Prepare(plan) && Start();

        public void Stop()
        {
            RequireThread();
            if (retiring || State == SessionState.Idle || State == SessionState.Faulted) return;
            State = SessionState.Stopping;
            Retire();
        }

        void Fail(Exception ex)
        {
            errors.Add(ex.Message + ": " + ex.GetBaseException().Message);
            State = SessionState.Faulted;
            Retire();
        }

        void Retire()
        {
            retiring = true; timedOut = false; stopping.Restart();
            // Reentrant cancellation/cleanup cannot commit through any other module.
            foreach (var scope in scopes) scope.Invalidate();
            for (int i = scopes.Count - 1; i >= 0; --i) scopes[i].Retire(errors);
            Poll();
        }

        // Pump from EditorApplication.update; never block Unity waiting for tasks.
        public void Poll()
        {
            RequireThread();
            if (!retiring) return;
            bool complete = true;
            foreach (var scope in scopes) complete &= scope.PollTasks(errors);
            if (!complete)
            {
                if (stopping.Elapsed < StopTimeout) return;
                if (!timedOut) errors.Add("Background work did not stop within " + StopTimeout.TotalSeconds + "s. Restart the Editor if native work is involved.");
                timedOut = true;
                State = SessionState.Faulted;
                return; // Keep observing late task faults and dispose cancellation sources once they finish.
            }
            retiring = false;
            State = errors.Count == 0 ? SessionState.Idle : SessionState.Faulted;
        }

        void RequireThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Session host must run on its owning thread.");
        }
    }
}
