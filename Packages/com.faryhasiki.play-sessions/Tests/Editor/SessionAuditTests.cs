using System;
using System.Collections.Generic;
using System.Linq;
using Faryhasiki.PlaySessions.Editor;
using NUnit.Framework;

namespace Faryhasiki.PlaySessions.Tests
{
    public sealed class SessionAuditTests
    {
        [TestCase(typeof(UnownedField), "PLAY113")]
        [TestCase(typeof(UnownedProperty), "PLAY113")]
        [TestCase(typeof(ReadonlyCollection), "PLAY113")]
        [TestCase(typeof(MissingOwner), "PLAY110")]
        [TestCase(typeof(EmptyReason), "PLAY112")]
        [TestCase(typeof(ConflictingLifetime), "PLAY109")]
        [TestCase(typeof(GenericState<>), "PLAY111")]
        public void AuditReportsConcreteDeclarationFailures(Type type, string code)
        {
            var issues = Inspect(type);
            Assert.That(issues.Any(issue => issue.Code == code), Is.True, string.Join("\n", issues));
        }

        [Test]
        public void AuditAcceptsRegisteredOwnerAndDocumentedPersistentState()
            => Assert.That(Inspect(typeof(DeclaredState)), Is.Empty);

        private static List<SessionIssue> Inspect(Type type)
        {
            var issues = new List<SessionIssue>();
            SessionAudit.InspectType(type, new HashSet<string> { "owner" }, issues);
            return issues;
        }

        private sealed class UnownedField { public static int Value = 0; }
        private sealed class UnownedProperty { public static int Value { get; set; } }
        private sealed class ReadonlyCollection { public static readonly List<int> Values = new List<int>(); }
        private sealed class MissingOwner { [SessionOwned("missing")] public static int Value = 0; }
        private sealed class EmptyReason { [SessionPersistent("")] public static int Value = 0; }
        private sealed class ConflictingLifetime { [SessionOwned("owner"), SessionPersistent("both")] public static int Value = 0; }
        private sealed class GenericState<T> { [SessionOwned("owner")] public static int Value = 0; }
        private sealed class DeclaredState
        {
            [SessionOwned("owner")] public static int Counter = 0;
            [SessionPersistent("Editor-wide cache with explicit external invalidation")] public static readonly List<int> Cache = new List<int>();
            public static readonly string Name = "immutable";
        }
    }
}
