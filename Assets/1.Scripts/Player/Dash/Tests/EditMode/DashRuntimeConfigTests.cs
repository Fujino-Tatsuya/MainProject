using NUnit.Framework;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// <see cref="DashRuntimeConfig"/> 검증: 정상값은 DashEnabled=true로 복사,
    /// 비정상값은 DashEnabled=false + 안전 보정. (PLAN §6)
    /// </summary>
    public class DashRuntimeConfigTests
    {
        [Test]
        public void ValidValues_Enabled_AndCopied()
        {
            var cfg = DashRuntimeConfig.Create(
                dashSpeed: 20.0, dashDuration: 0.25, maxCharge: 1,
                rechargeDuration: 2.0, snapshotCapacity: 32, snapshotFreshnessTolerance: 0.1);

            Assert.IsTrue(cfg.DashEnabled);
            Assert.AreEqual(20.0, cfg.DashSpeed, 1e-9);
            Assert.AreEqual(0.25, cfg.DashDuration, 1e-9);
            Assert.AreEqual(1, cfg.MaxCharge);
            Assert.AreEqual(2.0, cfg.RechargeDuration, 1e-9);
            Assert.AreEqual(32, cfg.SnapshotCapacity);
            Assert.AreEqual(0.1, cfg.SnapshotFreshnessTolerance, 1e-9);
        }

        [Test]
        public void ZeroDashDuration_Disabled()
        {
            var cfg = DashRuntimeConfig.Create(20.0, 0.0, 1, 2.0, 32, 0.1);
            Assert.IsFalse(cfg.DashEnabled);
        }

        [Test]
        public void ZeroMaxCharge_Disabled_AndClampedToOne()
        {
            var cfg = DashRuntimeConfig.Create(20.0, 0.25, 0, 2.0, 32, 0.1);
            Assert.IsFalse(cfg.DashEnabled);
            Assert.AreEqual(1, cfg.MaxCharge, "소비자 안전을 위해 1로 보정");
        }

        [Test]
        public void NegativeRecharge_Disabled()
        {
            var cfg = DashRuntimeConfig.Create(20.0, 0.25, 1, -1.0, 32, 0.1);
            Assert.IsFalse(cfg.DashEnabled);
        }

        [Test]
        public void ZeroSnapshotCapacity_Disabled_AndClamped()
        {
            var cfg = DashRuntimeConfig.Create(20.0, 0.25, 1, 2.0, 0, 0.1);
            Assert.IsFalse(cfg.DashEnabled);
            Assert.AreEqual(1, cfg.SnapshotCapacity);
        }

        [Test]
        public void NaNDashSpeed_Disabled()
        {
            var cfg = DashRuntimeConfig.Create(double.NaN, 0.25, 1, 2.0, 32, 0.1);
            Assert.IsFalse(cfg.DashEnabled);
        }

        [Test]
        public void ZeroFreshnessTolerance_IsValid()
        {
            // 0 허용값은 "정확히 요청시각 이하만" 의미로 유효하다.
            var cfg = DashRuntimeConfig.Create(20.0, 0.25, 1, 2.0, 32, 0.0);
            Assert.IsTrue(cfg.DashEnabled);
        }
    }
}
