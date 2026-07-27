using UnityEngine;

namespace BeaverLobby.Player.Dash
{
    /// <summary>
    /// 대시 정적 튜닝 원본(ScriptableObject). (PLAN §5, §6)
    /// 모든 캐릭터가 v1에서 같은 DashData를 사용한다.
    /// 씬 초기화 시 <see cref="CreateValidatedConfig"/>로 검증된 불변 <see cref="DashRuntimeConfig"/>를 만든다.
    ///
    /// v1에서 실제 소비되는 값만 노출한다. 이동/추락/공중 물리 값은 W3/W10에서 추가한다.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerDashData", menuName = "BeaverLobby/Player/Dash Data", order = 0)]
    public sealed class PlayerDashData : ScriptableObject
    {
        [Header("이동")]
        [SerializeField, Min(0f)] private float dashSpeed = 20f;      // 기본 이동속도 10m/s 대비 2배
        [SerializeField, Min(0f)] private float dashDuration = 0.25f; // 오너 로컬 대시 지속시간 (기본 거리 = 속도 × 지속)

        [Header("충전")]
        [SerializeField, Min(1)] private int maxCharge = 1;             // 2 이상부터 HUD 숫자 표시
        [SerializeField, Min(0f)] private float rechargeDuration = 2f;  // 소비 순간부터 순차 회복

        [Header("서버 검증")]
        [SerializeField, Min(1)] private int snapshotCapacity = 32;                // 서버 과거 상태 Ring Buffer
        [SerializeField, Min(0f)] private float snapshotFreshnessTolerance = 0.1f; // 요청시각과 Snapshot 최대 간격(초)

        [Header("충돌/경사 (W3)")]
        [SerializeField, Min(0f)] private float collisionSkin = 0.02f;             // Capsule Sweep 여유
        [SerializeField, Min(1)] private int maxSweepIterations = 3;               // 물리 Tick당 최대 충돌 해결 반복
        [SerializeField, Range(1f, 89f)] private float maxWalkableSlopeAngle = 50f;// 초과 경사는 벽으로 취급
        [Tooltip("대시 Capsule Sweep이 충돌로 취급할 레이어. 자기(Player/Hurtbox) 콜라이더는 코드에서 제외한다.")]
        [SerializeField] private LayerMask dashObstacleMask = ~0;

        public float DashSpeed => dashSpeed;
        public float DashDuration => dashDuration;
        public int MaxCharge => maxCharge;
        public float RechargeDuration => rechargeDuration;
        public int SnapshotCapacity => snapshotCapacity;
        public float SnapshotFreshnessTolerance => snapshotFreshnessTolerance;

        public float CollisionSkin => collisionSkin;
        public int MaxSweepIterations => maxSweepIterations;
        public float MaxWalkableSlopeAngle => maxWalkableSlopeAngle;
        public LayerMask DashObstacleMask => dashObstacleMask;

        /// <summary>검증된 불변 런타임 설정을 만든다. 값이 비정상이면 DashEnabled=false로 반환한다.</summary>
        public DashRuntimeConfig CreateValidatedConfig()
        {
            return DashRuntimeConfig.Create(
                dashSpeed,
                dashDuration,
                maxCharge,
                rechargeDuration,
                snapshotCapacity,
                snapshotFreshnessTolerance);
        }
    }
}
