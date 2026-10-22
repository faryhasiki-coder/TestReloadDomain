using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Faryhasiki.PlaySessions;

namespace PlaySessions.UniTaskIntegration
{
    public static class SessionUniTaskExtensions
    {
        // UniTask 2.5.11 AsTask turns cancellation into a fault. The async Task
        // method builder preserves OperationCanceledException as Task.Canceled.
        public static Task TrackUniTask(this SessionScope scope, UniTask operation)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            return scope.Track(PreserveCancellation(operation));
        }

        static async Task PreserveCancellation(UniTask operation) => await operation;
    }
}
