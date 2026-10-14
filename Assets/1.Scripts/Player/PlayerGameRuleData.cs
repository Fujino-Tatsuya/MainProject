using UnityEngine;

/// <summary>
/// 플레이어 공용 이동/게임 규칙(ScriptableObject). 대시·일반 이동·접지 판정이 함께 참조하는 단일 소스.
/// (PLAN §5 "등판각은 일반 이동 규칙 공유", "낮은 턱·지면 Snap·등판각은 Player GameRule 직렬화 값")
///
/// Soul soulSpeed 등은 각 기능이 병합·안정화된 뒤 이 규칙으로 이관한다.
/// 파티 공용 목숨 수(할 일 30)는 씬 단위 규칙이라 GameRule.prefab 의 <see cref="MultiGameRule"/>(같은 Player 시트, partyLifeCount)에 둔다.
/// </summary>
[CreateAssetMenu(fileName = "PlayerGameRuleData", menuName = "BeaverLobby/Player/Game Rule Data", order = 0)]
[DataTableSheet("Player", Order = 6)]
public sealed class PlayerGameRuleData : ScriptableObject
{
    private const int DefaultObstacleMask = (1 << 0) | (1 << 3) | (1 << 7) | (1 << 11);
    private const int DefaultGroundMask = (1 << 0) | (1 << 3) | (1 << 11);

    [Header("이동")]
    [Tooltip("걸어 올라갈 수 있는 최대 경사각(도). 이 값을 넘는 경사는 벽으로 취급한다. 접지 판정과 대시 등판이 이 값을 공유한다.")]
    [DataTableIgnore] [SerializeField, Range(1f, 89f)] private float maxWalkableSlopeAngle = 60f;

    [Tooltip("접지 이동 중 올라갈 수 있는 낮은 턱의 최대 높이(m). 맵 계단 에셋이 들어오면 실제 치수에 맞춰 조정한다.")]
    [DataTableIgnore] [SerializeField, Min(0f)] private float stepOffset = 0.3f;

    [Tooltip("플레이어 이동을 막는 정적 지오메트리 레이어(Default | Ground | Wall | Env).")]
    [SerializeField] private LayerMask obstacleMask = DefaultObstacleMask;
    [Tooltip("생존 상태에서 접지로 인정하는 레이어(Default | Ground | Env). Wall은 제외한다.")]
    [SerializeField] private LayerMask groundMask = DefaultGroundMask;
    [Tooltip("Soul 상태에서 접지로 인정하는 레이어. Player/Soul/전투 판정 레이어는 넣지 않는다.")]
    [SerializeField] private LayerMask soulGroundMask = DefaultGroundMask;
    [Tooltip("켜면 생존 플레이어끼리 이동을 막는다. Soul은 이 값과 무관하게 Player를 통과한다. (기획 10-11: 켬)")]
    [SerializeField] private bool blockOtherPlayers;
    [Tooltip("몬스터·다른 플레이어와 겹쳤을 때 플레이어를 빈 자리로 밀어내는 최대 속도(m/s). 몬스터는 움직이지 않는다. 0이면 밀어내지 않는다.")]
    [DataTableIgnore] [SerializeField, Min(0f)] private float unitSeparationSpeed = 5f;

    [Header("중력")]
    [Tooltip("Motor 수동 중력의 최대 낙하 속도(m/s).")]
    [DataTableIgnore] [SerializeField, Min(0f)] private float maxFallSpeed = 30f;

    [Header("추락")]
    [Tooltip("추락 피해 = ceil(FinalMaxHp * 이 비율). 방어력·쉴드·일반 무적 무시. (2026-10-02 씬별 FallBoundarySettings 에서 이리로 통합 — 네 씬 모두 0.25 였다)")]
    [SerializeField, Range(0f, 1f)] private float fallDamageRatio = 0.25f;

    [Header("넉백")]
    [DataTableIgnore] [SerializeField, Min(0f)] private float minKnockbackTime = 0.15f;
    [DataTableIgnore] [SerializeField, Min(0f)] private float maxKnockbackTime = 1.5f;
    [DataTableIgnore] [SerializeField, Min(0f)] private float knockbackStopSpeed = 0.15f;
    [Tooltip("넉백 속도의 선형 감속(m/s²). 6은 기존 PhysX 마찰 0.6 × 중력 9.81과 비슷한 감속이다.")]
    [DataTableIgnore] [SerializeField, Min(0f)] private float knockbackDeceleration = 6f;

    [Header("간파")]
    [Tooltip("간파(우클릭) 성공 시 그 간파 스킬의 남은 쿨에서 '전체 쿨타임 × 이 비율' 만큼 뺀다(0 미만은 0). " +
             "성공 = 열린 카운터 창(23호·중간보스·허수아비)을 맞혀 소비한 경우만. 한 번의 시전에서 여러 대상이 성공해도 1회.")]
    [SerializeField, Range(0f, 1f)] private float interruptSuccessCooldownReduction = InterruptSuccessCooldownModel.DefaultReductionRatio;

    [Header("체력 재생")]
    [Tooltip("마지막 피격 후 이 시간(초)이 지나야 체력 재생이 시작된다. 피격 = 체력이나 보호막이 실제로 깎인 피해(추락 포함). 다시 맞으면 처음부터 센다.")]
    [SerializeField, Min(0f)] private float regenDelaySeconds = PlayerHealthRegenModel.DefaultDelaySeconds;
    [Tooltip("재생 중 초당 회복량 = 최종 최대 체력 × 이 비율. 최대 체력에서 멈춘다. 0 이면 재생 없음.")]
    [SerializeField, Range(0f, 1f)] private float regenPercentPerSecond = PlayerHealthRegenModel.DefaultPercentPerSecond;

    public float FallDamageRatio => fallDamageRatio;
    public float HealthRegenDelaySeconds => Mathf.Max(0f, regenDelaySeconds);
    public float HealthRegenPercentPerSecond => Mathf.Clamp01(regenPercentPerSecond);
    public float InterruptSuccessCooldownReduction => Mathf.Clamp01(interruptSuccessCooldownReduction);
    public float MaxWalkableSlopeAngle => maxWalkableSlopeAngle;
    public float StepOffset => Mathf.Max(0f, stepOffset);
    public LayerMask ObstacleMask => obstacleMask;
    public LayerMask GroundMask => groundMask;
    public LayerMask SoulGroundMask => soulGroundMask;
    public bool BlockOtherPlayers => blockOtherPlayers;
    public float UnitSeparationSpeed => Mathf.Max(0f, unitSeparationSpeed);
    public float MaxFallSpeed => Mathf.Max(0f, maxFallSpeed);
    public float MinKnockbackTime => Mathf.Max(0f, minKnockbackTime);
    public float MaxKnockbackTime => Mathf.Max(MinKnockbackTime, maxKnockbackTime);
    public float KnockbackStopSpeed => Mathf.Max(0f, knockbackStopSpeed);
    public float KnockbackDeceleration => Mathf.Max(0f, knockbackDeceleration);

    /// <summary>등판각을 접지 판정용 up-dot 임계값으로 변환한다. dot(normal, up) &gt;= 이 값이면 지면.</summary>
    public float WalkableGroundUpDot => Mathf.Cos(maxWalkableSlopeAngle * Mathf.Deg2Rad);

    public LayerMask GetGroundMask(bool isSoul)
    {
        return isSoul ? soulGroundMask : groundMask;
    }
}
