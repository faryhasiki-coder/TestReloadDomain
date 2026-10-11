using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Faryhasiki.PlaySessions.Tests
{
    public sealed class SessionHostTests
    {
        [Test]
        public void DependenciesResetBeforeStartAndReleaseInReverseOrder()
        {
            var trace = new List<string>();
            var plan = new SessionPlan()
                .Add("ui", s => { trace.Add("start ui"); s.OnDispose(() => trace.Add("stop ui")); },
                    () => trace.Add("reset ui"), dependencies: new[] { "data" })
                .Add("data", s => { trace.Add("start data"); s.OnDispose(() => trace.Add("stop data")); },
                    () => trace.Add("reset data"));
            var host = new SessionHost();
            Assert.That(host.Start(plan), Is.True);
            host.Stop();
            host.Stop();
            CollectionAssert.AreEqual(new[] { "reset data", "reset ui", "start data", "start ui", "stop ui", "stop data" }, trace);
            Assert.That(host.CanStartNext, Is.True);
        }

        [TestCase("duplicate", "PLAY101")]
        [TestCase("missing", "PLAY104")]
        [TestCase("cycle", "PLAY103")]
        public void InvalidPlanHasNoStartupSideEffects(string failure, string expectedCode)
        {
            var effects = 0;
            var plan = new SessionPlan();
            if (failure == "duplicate") plan.Add("a", s => effects++).Add("a", s => effects++);
            if (failure == "missing") plan.Add("a", s => effects++, dependencies: new[] { "absent" });
            if (failure == "cycle") plan.Add("a", s => effects++, dependencies: new[] { "b" }).Add("b", s => effects++, dependencies: new[] { "a" });
            var host = new SessionHost();
            Assert.That(host.Start(plan), Is.False);
            Assert.That(effects, Is.Zero);
            Assert.That(host.Issues.Any(issue => issue.Code == expectedCode), Is.True);
        }

        [Test]
        public void PartialStartupRollsBackIncludingFailingModule()
        {
            var released = new List<string>();
            var host = new SessionHost();
            var plan = new SessionPlan()
                .Add("first", s => s.OnDispose(() => released.Add("first")))
                .Add("second", s => { s.OnDispose(() => released.Add("second")); throw new InvalidOperationException("startup failed"); }, dependencies: new[] { "first" });
            Assert.That(host.Start(plan), Is.False);
            CollectionAssert.AreEqual(new[] { "second", "first" }, released);
            Assert.That(host.Status, Is.EqualTo(SessionStatus.Faulted));
            Assert.That(host.CanStartNext, Is.True, "Startup failure alone must not be reported as a cleanup leak.");
        }

        [Test]
        public void ReleaseFailureDoesNotSkipOtherResourcesAndBlocksNextSession()
        {
            var released = false;
            var host = new SessionHost();
            host.Start(new SessionPlan().Add("a", s =>
            {
                s.OnDispose(() => released = true);
                s.OnDispose(() => throw new InvalidOperationException("release failed"));
            }));
            host.Stop();
            Assert.That(released, Is.True);
            Assert.That(host.Issues.Any(issue => issue.Code == "PLAY302"), Is.True);
            Assert.That(host.CanStartNext, Is.False);
        }

        [Test]
        public void StopVerificationFailureIsNotReportedAsClean()
        {
            var host = new SessionHost();
            host.Start(new SessionPlan().Add("adapter", s => { }, verifyStopped: errors => errors.Add(new SessionIssue("ADAPTER001", "adapter", "Still subscribed"))));
            host.Stop();
            Assert.That(host.CanStartNext, Is.False);
            Assert.That(host.Status, Is.EqualTo(SessionStatus.Faulted));
        }

        [Test]
        public void TwentySessionsDoNotAccumulateSubscriptionsOrState()
        {
            Action signal = null;
            var value = 19;
            var leases = 0;
            var plan = new SessionPlan().Add("counter", s =>
            {
                leases++;
                s.OnDispose(() => leases--);
                Action handler = () => value++;
                signal += handler;
                s.OnRetire(() => signal -= handler);
            }, reset: () => value = 0);
            for (var i = 0; i < 20; i++)
            {
                var host = new SessionHost();
                Assert.That(host.Start(plan), Is.True);
                Assert.That(value, Is.Zero);
                Assert.That(leases, Is.EqualTo(1));
                signal();
                Assert.That(value, Is.EqualTo(1));
                host.Stop();
                Assert.That(signal, Is.Null);
                Assert.That(leases, Is.Zero);
                Assert.That(host.ResourceCount, Is.Zero);
                Assert.That(host.CanStartNext, Is.True);
            }
        }

        [Test]
        public void ExternalRequestsRequireCurrentReadySession()
        {
            var old = new SessionHost();
            old.Start(new SessionPlan());
            old.Stop();
            var current = new SessionHost();
            current.Start(new SessionPlan());
            var value = 0;
            Assert.That(current.TryExecute(old.Id, () => value++), Is.False);
            Assert.That(current.TryExecute(current.Id, () => value++), Is.True);
            current.Stop();
            Assert.That(current.TryExecute(current.Id, () => value++), Is.False);
            Assert.That(value, Is.EqualTo(1));
        }

        [Test]
        public void StopRejectsAlreadyQueuedCommit()
        {
            SessionScope scope = null;
            var host = new SessionHost();
            host.Start(new SessionPlan().Add("a", s => scope = s));
            var value = 0;
            var commit = scope.CommitAsync(() => value++);
            host.Stop();
            Assert.That(commit.IsCompleted, Is.True);
            Assert.That(commit.Result, Is.False);
            Assert.That(value, Is.Zero);
            Assert.That(host.RejectedCommits, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator LateResultIsRejectedAndResourcesWaitForWorker()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var released = false;
            var mutations = 0;
            Action externalEvent = null;
            var host = new SessionHost();
            host.Start(new SessionPlan().Add("a", s =>
            {
                s.OnDispose(() => released = true);
                Action handler = () => mutations++;
                externalEvent += handler;
                s.OnRetire(() => externalEvent -= handler);
                s.RunBackground(async _ =>
                {
                    await gate.Task.ConfigureAwait(false); // Deliberately ignore cancellation, like an external SDK.
                    await s.CommitAsync(() => mutations++).ConfigureAwait(false);
                });
            }));
            try
            {
                host.Stop();
                Assert.That(released, Is.False);
                Assert.That(externalEvent, Is.Null, "Detach event sources before waiting for background work.");
                Assert.That(host.CanStartNext, Is.False);
                gate.TrySetResult(true);
                yield return Drain(host);
                Assert.That(mutations, Is.Zero);
                Assert.That(host.RejectedCommits, Is.EqualTo(1));
                Assert.That(released, Is.True);
                Assert.That(host.CanStartNext, Is.True);
            }
            finally { gate.TrySetResult(true); host.Stop(); }
        }

        [UnityTest]
        public IEnumerator BackgroundCommitRunsOnSessionThread()
        {
            var ownerThread = Thread.CurrentThread.ManagedThreadId;
            var commitThread = -1;
            var host = new SessionHost();
            host.Start(new SessionPlan().Add("a", s => s.RunBackground(async _ =>
            {
                await s.CommitAsync(() => commitThread = Thread.CurrentThread.ManagedThreadId).ConfigureAwait(false);
            })));
            try
            {
                yield return Drain(host, stop: false);
                Assert.That(commitThread, Is.EqualTo(ownerThread));
            }
            finally { host.Stop(); }
        }

        [UnityTest]
        public IEnumerator DependentTaskKeepsDependencyAliveUntilItFinishes()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var released = new List<string>();
            var host = new SessionHost();
            host.Start(new SessionPlan()
                .Add("service", s => s.OnDispose(() => released.Add("service")))
                .Add("worker", s =>
                {
                    s.OnDispose(() => released.Add("worker"));
                    s.RunBackground(async _ => await gate.Task.ConfigureAwait(false));
                }, dependencies: new[] { "service" }));
            try
            {
                host.Stop();
                Assert.That(released, Is.Empty);
                gate.TrySetResult(true);
                yield return Drain(host);
                CollectionAssert.AreEqual(new[] { "worker", "service" }, released);
            }
            finally { gate.TrySetResult(true); host.Stop(); }
        }

        [UnityTest]
        public IEnumerator TimeoutRetainsResourcesAndDoesNotSilentlyClearFailure()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var released = false;
            var host = new SessionHost(TimeSpan.Zero);
            host.Start(new SessionPlan().Add("slow", s =>
            {
                s.OnDispose(() => released = true);
                s.RunBackground(async _ => await gate.Task.ConfigureAwait(false));
            }));
            try
            {
                host.Stop();
                Assert.That(host.Issues.Any(issue => issue.Code == "PLAY304"), Is.True);
                Assert.That(released, Is.False);
                gate.TrySetResult(true);
                yield return Drain(host);
                Assert.That(released, Is.True);
                Assert.That(host.CanStartNext, Is.False);
            }
            finally { gate.TrySetResult(true); host.Stop(); }
        }

        private static IEnumerator Drain(SessionHost host, bool stop = true)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (host.PendingTasks > 0 && DateTime.UtcNow < deadline)
            {
                host.Pump();
                yield return null;
            }
            if (stop) host.Stop();
            host.Pump();
            Assert.That(host.PendingTasks, Is.Zero, "Tracked work failed to drain before the test deadline.");
        }
    }
}
