using NUnit.Framework;
using UnityEngine;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// <see cref="InterruptTargetGeometry"/> 검증(할 일 132 정정1): 간파 사거리·대상 정면 허용각·공격 방향 — 밖이면 '대상 없음'.
    /// 근접 = 판정 위치 지금·공격 방향 = 시전 방향, 대쉬 = 판정 위치 예상 대시 끝·공격 방향 = 대시 방향.
    /// </summary>
    public class InterruptTargetGeometryTests
    {
        private const float Reach = 2f;
        // 대상은 원점, 플레이어 쪽(-Z)을 본다. 정면 허용각 90(23호 데이터값 = 총 180도).
        private static readonly Vector3 TargetPos = Vector3.zero;
        private static readonly Vector3 TargetFacingPlayer = Vector3.back;

        private static bool Melee(Vector3 playerPos, Vector3 castDir, float distance, float frontAngle = 90f, Vector3? targetForward = null) =>
            InterruptTargetGeometry.Accepts(playerPos, castDir, Reach, distance, TargetPos, targetForward ?? TargetFacingPlayer, frontAngle);

        // ─── 근접 ───

        [Test]
        public void 근접_사거리_안_정면_대상을_향하면_대상_있음()
        {
            Assert.IsTrue(Melee(new Vector3(0f, 0f, -2f), Vector3.forward, 1.5f));
        }

        [Test]
        public void 근접_사거리_밖이면_대상_없음()
        {
            Assert.IsFalse(Melee(new Vector3(0f, 0f, -5f), Vector3.forward, 4.5f));
            Assert.IsTrue(Melee(new Vector3(0f, 0f, -2.5f), Vector3.forward, Reach), "경계 = 사거리 포함");
        }

        [Test]
        public void 근접_시전_방향이_대상과_반대면_대상_없음()
        {
            Assert.IsFalse(Melee(new Vector3(0f, 0f, -2f), Vector3.back, 1.5f));
            Vector3 dir100 = Quaternion.Euler(0f, 100f, 0f) * Vector3.forward;
            Assert.IsFalse(Melee(new Vector3(0f, 0f, -2f), dir100, 1.5f, InterruptTargetGeometry.NoFrontRule), "반평면 밖");
        }

        [Test]
        public void 근접_시전_방향_허용각은_대상_정면_허용각_이하_반평면()
        {
            Vector3 pos = new Vector3(0f, 0f, -2f);
            Vector3 dir45 = Quaternion.Euler(0f, 45f, 0f) * Vector3.forward;
            Vector3 dir70 = Quaternion.Euler(0f, 70f, 0f) * Vector3.forward;
            Assert.IsTrue(Melee(pos, dir45, 1.5f, 60f));
            Assert.IsFalse(Melee(pos, dir70, 1.5f, 60f), "23호 counterFrontAngle 60 이면 시전 방향도 ±60");
            Assert.IsTrue(Melee(pos, dir70, 1.5f, InterruptTargetGeometry.NoFrontRule), "정면 판정 없는 대상 = 반평면(±90)");
            Assert.AreEqual(90f, InterruptTargetGeometry.AttackerHalfAngle(180f));
            Assert.AreEqual(60f, InterruptTargetGeometry.AttackerHalfAngle(60f));
        }

        [Test]
        public void 근접_대상_정면_허용각_밖_뒤에서면_대상_없음()
        {
            // 대상이 플레이어 반대(+Z)를 본다 — 플레이어는 대상 뒤.
            Assert.IsFalse(Melee(new Vector3(0f, 0f, -2f), Vector3.forward, 1.5f, 90f, Vector3.forward));
            Assert.IsTrue(Melee(new Vector3(0f, 0f, -2f), Vector3.forward, 1.5f, InterruptTargetGeometry.NoFrontRule, Vector3.forward),
                "정면 판정 없는 대상(중간보스·허수아비)은 뒤에서도");
        }

        [Test]
        public void 겹쳐_방향을_못_구하면_안으로_본다()
        {
            Assert.IsTrue(InterruptTargetGeometry.IsInArc(Vector3.forward, Vector3.zero, 10f));
            Assert.IsTrue(InterruptTargetGeometry.IsInArc(Vector3.zero, Vector3.back, 10f));
        }

        // ─── 대쉬(예상 대시 끝 기준) ───

        private static bool Dash(Vector3 castPos, Vector3 dashDir, float speed, float remaining, float distanceAtEnd)
        {
            Vector3 end = castPos + DashInterruptRules.GetPredictedEndOffset(dashDir, speed, remaining);
            return InterruptTargetGeometry.Accepts(end, dashDir, Reach, distanceAtEnd, TargetPos, TargetFacingPlayer, 90f);
        }

        [Test]
        public void 대쉬_대상_쪽으로_끝이_사거리_안이면_대상_있음()
        {
            Assert.IsTrue(Dash(new Vector3(0f, 0f, -5f), Vector3.forward, 10f, 0.2f, 2.5f - 1f)); // 끝 z=-3
        }

        [Test]
        public void 대쉬_방향이_대상_반대면_대상_없음()
        {
            Assert.IsFalse(Dash(new Vector3(0f, 0f, -1.5f), Vector3.back, 10f, 0.05f, 1.5f)); // 끝 z=-2, 사거리 안이지만 등짐
        }

        [Test]
        public void 대쉬_끝_위치가_사거리_밖이면_대상_없음()
        {
            Assert.IsFalse(Dash(new Vector3(0f, 0f, -10f), Vector3.forward, 10f, 0.2f, 7.5f)); // 끝 z=-8
        }

        [Test]
        public void 대쉬로_대상을_지나치면_대상_없음()
        {
            // 끝이 대상 뒤(+Z 쪽) — 대상을 등지고 끝나고, 대상 정면(-Z)에서도 벗어난다.
            Assert.IsFalse(Dash(new Vector3(0f, 0f, -1f), Vector3.forward, 10f, 0.25f, 1f)); // 끝 z=1.5
        }
    }
}
