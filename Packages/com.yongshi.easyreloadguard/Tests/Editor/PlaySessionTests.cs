using NUnit.Framework;

namespace EasyReloadGuard.Editor.Tests
{
    public class PlaySessionTests
    {
        [TearDown]
        public void TearDown() => PlaySession.End();

        [Test]
        public void Stamp_ExpiresWhenSessionEndsAndNewSessionBegins()
        {
            PlaySession.Begin();
            var stamp = PlaySession.Current;
            var token = PlaySession.Token;
            Assert.IsTrue(stamp.IsAlive);
            Assert.IsFalse(token.IsCancellationRequested);

            PlaySession.End();
            Assert.IsFalse(stamp.IsAlive);
            Assert.IsTrue(token.IsCancellationRequested);
            Assert.IsTrue(PlaySession.Token.IsCancellationRequested, "会话外的令牌应为已取消");

            PlaySession.Begin();
            Assert.IsFalse(stamp.IsAlive, "旧会话的标记不能在新会话中复活");
            Assert.AreEqual(stamp.Id + 1, PlaySession.Id);
        }

        [Test]
        public void Begin_EndsPreviousSessionDefensively()
        {
            PlaySession.Begin();
            var token = PlaySession.Token;
            PlaySession.Begin();

            Assert.IsTrue(token.IsCancellationRequested);
            Assert.IsFalse(PlaySession.Token.IsCancellationRequested);
        }

        [Test]
        public void RegisterReset_IsDrainedOnce()
        {
            PlaySession.DrainRegisteredResets();
            var calls = 0;
            PlaySession.RegisterReset(() => calls++);

            foreach (var reset in PlaySession.DrainRegisteredResets()) reset();
            Assert.AreEqual(1, calls);
            Assert.IsEmpty(PlaySession.DrainRegisteredResets());
        }

        [Test]
        public void RegisterPersistentReset_SurvivesMultipleDrains()
        {
            PlaySession.DrainRegisteredResets();
            var calls = 0;
            PlaySession.RegisterPersistentReset(() => calls++);

            foreach (var reset in PlaySession.DrainRegisteredResets()) reset();
            Assert.AreEqual(1, calls);

            foreach (var reset in PlaySession.DrainRegisteredResets()) reset();
            Assert.AreEqual(2, calls);

            foreach (var reset in PlaySession.DrainRegisteredResets()) reset();
            Assert.AreEqual(3, calls);
        }

        [Test]
        public void RegisterPersistentReset_ThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => PlaySession.RegisterPersistentReset(null));
        }
    }
}
