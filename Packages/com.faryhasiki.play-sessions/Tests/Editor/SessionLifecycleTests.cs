using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Faryhasiki.PlaySessions.Editor;
using NUnit.Framework;
using PlaySessions.ExternalFixture;

namespace Faryhasiki.PlaySessions.Tests
{
    public sealed class SessionLifecycleTests
    {
        [Test]
        public void AllResetsPrecedeStartsAndCleanupReversesDependencies()
        {
            var calls = new List<string>();
            var plan = new SessionPlan();
            plan.Add("child", s => { calls.Add("reset-child"); s.OnStop(() => calls.Add("stop-child")); }, _ => calls.Add("start-child"), requires: new[] { "parent" });
            plan.Add("parent", s => { calls.Add("reset-parent"); s.OnStop(() => calls.Add("stop-parent")); }, _ => calls.Add("start-parent"));
            var host = new SessionHost();
            Assert.IsTrue(host.Begin(plan)); host.Stop(); host.Stop();
            CollectionAssert.AreEqual(new[] { "reset-parent", "reset-child", "start-parent", "start-child", "stop-child", "stop-parent" }, calls);
            Assert.AreEqual(SessionState.Idle, host.State);
        }

        [Test]
        public void InvalidPlanDoesNotMutateAnyState()
        {
            int changes = 0;
            var plan = new SessionPlan();
            plan.Add("first", _ => changes++);
            plan.Add("later", _ => changes++, validate: () => "Unsupported version");
            var host = new SessionHost();
            Assert.IsFalse(host.Begin(plan)); Assert.AreEqual(0, changes);
            Assert.AreEqual(SessionState.Faulted, host.State);
        }

        [Test]
        public void DuplicateMissingAndCyclicModulesAreRejected()
        {
            var plan = new SessionPlan(); plan.Add("a", _ => { }, requires: new[] { "b" });
            Assert.Throws<InvalidOperationException>(() => plan.Add("a", _ => { }));
            Assert.Throws<InvalidOperationException>(() => plan.Resolve());
            plan.Add("b", _ => { }, requires: new[] { "a" });
            Assert.Throws<InvalidOperationException>(() => plan.Resolve());
        }

        [Test]
        public void PartialResetFailureCleansTheFailingModuleAndBlocksReuse()
        {
            var cleaned = new List<string>(); var plan = new SessionPlan();
            plan.Add("a", s => s.OnStop(() => cleaned.Add("a")));
            plan.Add("b", s => { s.OnStop(() => cleaned.Add("b")); throw new Exception("reset failure"); });
            var host = new SessionHost();
            Assert.IsFalse(host.Begin(plan)); CollectionAssert.AreEqual(new[] { "b", "a" }, cleaned);
            Assert.AreEqual(SessionState.Faulted, host.State);
            Assert.Throws<InvalidOperationException>(() => host.Begin(plan));
        }

        [Test]
        public void StartFailureRollsBackAllPreparedModules()
        {
            int cleanup = 0; var plan = new SessionPlan();
            plan.Add("a", s => s.OnStop(() => cleanup++), _ => throw new Exception("start failure"));
            plan.Add("b", s => s.OnStop(() => cleanup++));
            var host = new SessionHost(); Assert.IsFalse(host.Begin(plan));
            Assert.AreEqual(2, cleanup); Assert.AreEqual(SessionState.Faulted, host.State);
            StringAssert.Contains("a start", host.Errors[0]);
        }

        [Test]
        public void CleanupFailuresDoNotPreventOtherCleanup()
        {
            int cleaned = 0; var plan = new SessionPlan();
            plan.Add("a", s => { s.OnStop(() => cleaned++); s.OnStop(() => throw new Exception("dispose failure")); });
            var host = new SessionHost(); host.Begin(plan); host.Stop();
            Assert.AreEqual(1, cleaned); Assert.AreEqual(SessionState.Faulted, host.State);
        }

        [Test]
        public void ScopesInvalidateBeforeCancellationOrCleanupCanPublish()
        {
            SessionScope a = null; bool committed = true; var plan = new SessionPlan();
            plan.Add("a", s => a = s);
            plan.Add("b", s => s.OnStop(() => committed = a.TryCommit(() => { })));
            var host = new SessionHost(); host.Begin(plan); host.Stop();
            Assert.IsFalse(committed); Assert.IsTrue(a.Token.IsCancellationRequested);
        }

        [Test]
        public void OldScopeCannotPublishIntoNewSession()
        {
            SessionScope scope = null; var plan = new SessionPlan(); plan.Add("a", s => scope = s);
            var host = new SessionHost(); host.Begin(plan); var old = scope; host.Stop(); host.Begin(plan);
            Assert.AreNotEqual(old.SessionId, scope.SessionId);
            Assert.IsFalse(old.TryCommit(() => Assert.Fail("stale callback")));
            Assert.IsTrue(scope.TryCommit(() => { })); host.Stop();
        }

        [Test]
        public void PendingWorkBlocksRestartWithoutBlockingMainThread()
        {
            var work = new TaskCompletionSource<bool>(); var plan = new SessionPlan();
            plan.Add("a", s => s.Track(work.Task)); var host = new SessionHost(); host.Begin(plan); host.Stop();
            Assert.AreEqual(SessionState.Stopping, host.State);
            Assert.Throws<InvalidOperationException>(() => host.Begin(plan));
            work.SetResult(true); host.Poll(); Assert.AreEqual(SessionState.Idle, host.State);
        }

        [Test]
        public void UncooperativeWorkTimesOutAndRequiresRecovery()
        {
            var work = new TaskCompletionSource<bool>(); var plan = new SessionPlan(); plan.Add("a", s => s.Track(work.Task));
            var host = new SessionHost(TimeSpan.Zero); host.Begin(plan); host.Stop();
            Assert.AreEqual(SessionState.Faulted, host.State); work.SetResult(true);
            host.Poll(); Assert.AreEqual(SessionState.Faulted, host.State);
        }

        [Test]
        public void BackgroundFaultIsObservedAndBlocksReuse()
        {
            var plan = new SessionPlan(); plan.Add("a", s => s.Track(Task.FromException(new Exception("worker failed"))));
            var host = new SessionHost(); host.Begin(plan); host.Stop();
            Assert.AreEqual(SessionState.Faulted, host.State);
            StringAssert.Contains("worker failed", string.Join(";", host.Errors));
        }

        [Test]
        public void WorkerCannotDirectlyUseCommitApi()
        {
            SessionScope scope = null; var plan = new SessionPlan(); plan.Add("a", s => scope = s);
            var host = new SessionHost(); host.Begin(plan);
            var error = Task.Run(() => { try { scope.TryCommit(() => { }); return false; } catch (InvalidOperationException) { return true; } }).Result;
            Assert.IsTrue(error); host.Stop();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LifecycleReentrancyCannotResurrectRetiredScopes(bool duringStart)
        {
            var host = new SessionHost(); var plan = new SessionPlan(); int cleaned = 0;
            plan.Add("a", s => { s.OnStop(() => cleaned++); if (!duringStart) host.Stop(); }, _ => { if (duringStart) host.Stop(); });
            Assert.IsFalse(host.Begin(plan)); Assert.AreEqual(SessionState.Faulted, host.State);
            Assert.AreEqual(1, cleaned);
        }

        [Test]
        public void ManagedDllResetsAndUnsubscribesAcrossTwentySessions()
        {
            Assert.IsTrue(typeof(VendorCache).Assembly.Location.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(typeof(VendorCache).Assembly.GetReferencedAssemblies().Any(a => a.Name.Contains("PlaySessions")));
            var plan = new SessionPlan();
            int notifications = 0;
            plan.Add("vendor", scope => { VendorCache.Reset(); scope.OnStop(VendorCache.Reset); }, scope =>
            {
                Action handler = () => notifications++;
                VendorCache.Changed += handler;
                scope.OnStop(() => VendorCache.Changed -= handler);
            });
            var host = new SessionHost();
            for (int i = 0; i < 20; i++)
            {
                Assert.IsTrue(host.Begin(plan)); Assert.AreEqual(0, VendorCache.Values.Count);
                Assert.AreEqual(1, VendorCache.Subscribers); VendorCache.Add(i); host.Stop();
                Assert.AreEqual(0, VendorCache.Subscribers); Assert.AreEqual(0, VendorCache.Values.Count);
            }
            Assert.AreEqual(20, notifications);
        }

        [Test]
        public void AuditFindsReadonlyCollectionsAndUnregisteredOwnership()
        {
            var diagnostics = SessionAudit.Scan(new[] { typeof(AuditFixture).Assembly }, new SessionPlan());
            Assert.IsTrue(diagnostics.Any(d => d.code == "PS101" && d.target.EndsWith("AuditFixture.Items")));
            Assert.IsTrue(diagnostics.Any(d => d.code == "PS105" && d.target.EndsWith("AuditFixture.MissingOwner")));
            Assert.IsTrue(diagnostics.Any(d => d.code == "PS104" && d.target.EndsWith("AuditFixture.ReadonlyNumber")));
        }

        [Test]
        public void FieldResetRejectsReadonlyCollectionsThreadLocalAndGenericState()
        {
            foreach (var name in new[] { "Items", "ReadonlyNumber", "ThreadLocal" })
                Assert.IsFalse(SessionCatalog.CanReset(typeof(AuditFixture).GetField(name)));
            Assert.IsFalse(SessionCatalog.CanReset(typeof(GenericFixture<>).GetField("Value")));
            Assert.IsTrue(SessionCatalog.CanReset(typeof(AuditFixture).GetField("Number")));
        }

        static class AuditFixture
        {
            public static readonly List<int> Items = new List<int>();
            [SessionOwned("not-registered")] public static int MissingOwner;
            [ResetOnPlay] public static readonly int ReadonlyNumber;
            [ThreadStatic] public static int ThreadLocal;
            public static int Number;
        }
        static class GenericFixture<T> { public static int Value; }
    }
}
