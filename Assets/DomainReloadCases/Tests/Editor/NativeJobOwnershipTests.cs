using System;
using System.Collections;
using System.Threading;
using EasyReloadGuard;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    [Category("Case.NativeResources")]
    public sealed class NativeJobOwnershipTests : ReloadCaseTestBase
    {
        [UnityTest]
        [Description("RES-01: Real scheduled Job completes before its owned NativeArray is disposed on exit; three Fast Plays.")]
        public IEnumerator SessionExitCompletesJobAndReleasesNativeArray()
        {
            for (int cycle = 1; cycle <= 3; cycle++)
            {
                yield return EnterFastPlay();
                using (var lease = new JobLease(PlaySession.Token))
                {
                    Assert.IsTrue(lease.IsCreated);
                    yield return LeavePlay();
                    Assert.IsTrue(lease.Completed);
                    Assert.IsFalse(lease.IsCreated);
                    Assert.AreEqual(42, lease.ResultBeforeDispose);
                    Assert.AreEqual(1, lease.ReleaseCount);
                    lease.Dispose();
                    Assert.AreEqual(1, lease.ReleaseCount, "重复清理不能重复 Dispose NativeArray。");
                }
            }
        }

        sealed class JobLease : IDisposable
        {
            NativeArray<int> values;
            JobHandle handle;
            CancellationTokenRegistration registration;
            public bool IsCreated => values.IsCreated;
            public bool Completed { get; private set; }
            public int ResultBeforeDispose { get; private set; }
            public int ReleaseCount { get; private set; }

            public JobLease(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                values = new NativeArray<int>(1, Allocator.Persistent);
                try
                {
                    handle = new WriteValue { Values = values }.Schedule();
                    registration = token.Register(Release);
                }
                catch
                {
                    Release();
                    throw;
                }
            }

            void Release()
            {
                if (!values.IsCreated) return;
                handle.Complete();
                ResultBeforeDispose = values[0];
                Completed = true;
                values.Dispose();
                ReleaseCount++;
            }

            public void Dispose()
            {
                registration.Dispose();
                Release();
            }
        }

        struct WriteValue : IJob
        {
            public NativeArray<int> Values;
            public void Execute() => Values[0] = 42;
        }
    }
}
