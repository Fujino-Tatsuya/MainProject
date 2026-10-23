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
    [DataTableSheet("Player", Order = 2)]
    public sealed class PlayerDashData : ScriptableObject
    {
        [Header("이동")]
        [SerializeField, Min(0f)] private float dashSpeed = 20f;      // 기본 이동속도 10m/s 대비 2배
        [SerializeField, Min(0f)] private float dashDuration = 0.25f; // 오너 로컬 대시 지속시간 (기본 거리 = 속도 × 지속)

        [Header("충전")]
        [SerializeField, Min(1)] private int maxCharge = 1;             // 2 이상부터 HUD 숫자 표시
        [SerializeField, Min(0f)] private float rechargeDuration = 2f;  // 소비 순간부터 순차 회복

        [Header("서버 검증")]
        [SerializeField, Min(1), DataTableIgnore] private int snapshotCapacity = 32;                // 서버 과거 상태 Ring Buffer
        [SerializeField, Min(0f), DataTableIgnore] private float snapshotFreshnessTolerance = 0.1f; // 요청시각과 Snapshot 최대 간격(초)

        // 연출 정렬 값이라 테이블에 올리지 않는다. 재생 속도는 컨트롤러 상태의 Speed 로 조절한다(코드에서 건드리지 않음).
        [Header("애니메이션 (컨트롤러 Base 레이어에 이 상태가 있을 때만 — 어쌔신)")]
        [Tooltip("대시 시작 때 Base 레이어에서 CrossFade 할 상태 이름. 컨트롤러에 없으면(팔라딘·거너) 대시 애니를 틀지 않는다.")]
        [SerializeField, DataTableIgnore] private string dodgeStateName = "Dodge";
        [Tooltip("대시 시작 → Dodge 블렌드 시간(초).")]
        [SerializeField, Min(0f), DataTableIgnore] private float dodgeStartBlend = 0.05f;
        [Tooltip("대시 끝 → Run_Loop(이동 입력 중) / Idle 블렌드 시간(초).")]
        [SerializeField, Min(0f), DataTableIgnore] private float dodgeEndBlend = 0.1f;

        public float DashSpeed => dashSpeed;
        public float DashDuration => dashDuration;
        public int MaxCharge => maxCharge;
        public float RechargeDuration => rechargeDuration;
        public int SnapshotCapacity => snapshotCapacity;
        public float SnapshotFreshnessTolerance => snapshotFreshnessTolerance;
        public string DodgeStateName => dodgeStateName;
        public float DodgeStartBlend => dodgeStartBlend;
        public float DodgeEndBlend => dodgeEndBlend;

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
