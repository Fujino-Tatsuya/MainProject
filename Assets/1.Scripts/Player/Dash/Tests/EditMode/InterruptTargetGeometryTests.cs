using NUnit.Framework;
using UnityEngine;

namespace BeaverLobby.Player.Dash.EditModeTests
{
    /// <summary>
    /// <see cref="InterruptTargetGeometry"/> 검증(할 일 132 정정1 → 139-3): 대상 정면 허용각 — 밖이면 '대상 없음'.
    /// 사거리·공격 방향은 판정 앵커 형상 오버랩이 본다(<c>HitboxAnchorShapeTests</c>).
    /// </summary>
    public class InterruptTargetGeometryTests
    {
        // 대상은 원점, 플레이어 쪽(-Z)을 본다.
        private static readonly Vector3 TargetPos = Vector3.zero;
        private static readonly Vector3 TargetFacingPlayer = Vector3.back;

        [Test]
        public void 대상_정면_허용각_안이면_통과()
        {
            Assert.IsTrue(InterruptTargetGeometry.FacesAttacker(new Vector3(0f, 0f, -2f), TargetPos, TargetFacingPlayer, 90f));
            Vector3 at45 = Quaternion.Euler(0f, 45f, 0f) * Vector3.back * 2f;
            Assert.IsTrue(InterruptTargetGeometry.FacesAttacker(at45, TargetPos, TargetFacingPlayer, 60f));
        }

        [Test]
        public void 대상_정면_허용각_밖이면_대상_없음()
        {
            Vector3 at70 = Quaternion.Euler(0f, 70f, 0f) * Vector3.back * 2f;
            Assert.IsFalse(InterruptTargetGeometry.FacesAttacker(at70, TargetPos, TargetFacingPlayer, 60f), "23호 counterFrontAngle 60");
            Assert.IsFalse(InterruptTargetGeometry.FacesAttacker(new Vector3(0f, 0f, 2f), TargetPos, TargetFacingPlayer, 90f), "등 뒤");
        }

        [Test]
        public void 정면_판정_없는_대상은_뒤에서도()
        {
            Assert.IsTrue(InterruptTargetGeometry.FacesAttacker(new Vector3(0f, 0f, 2f), TargetPos, TargetFacingPlayer,
                InterruptTargetGeometry.NoFrontRule), "중간보스·허수아비");
        }

        [Test]
        public void 겹쳐_방향을_못_구하면_안으로_본다()
        {
            Assert.IsTrue(InterruptTargetGeometry.IsInArc(Vector3.forward, Vector3.zero, 10f));
            Assert.IsTrue(InterruptTargetGeometry.IsInArc(Vector3.zero, Vector3.back, 10f));
            Assert.IsTrue(InterruptTargetGeometry.FacesAttacker(TargetPos, TargetPos, TargetFacingPlayer, 10f));
        }
    }
}
