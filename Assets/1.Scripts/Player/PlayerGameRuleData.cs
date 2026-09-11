using UnityEngine;

/// <summary>
/// 플레이어 공용 이동/게임 규칙(ScriptableObject). 대시·일반 이동·접지 판정이 함께 참조하는 단일 소스.
/// (PLAN §5 "등판각은 일반 이동 규칙 공유", "낮은 턱·지면 Snap·등판각은 Player GameRule 직렬화 값")
///
/// Soul soulSpeed, 기본 LifeCount 등은 각 기능이 병합·안정화된 뒤 이 규칙으로 이관한다.
/// </summary>
[CreateAssetMenu(fileName = "PlayerGameRuleData", menuName = "BeaverLobby/Player/Game Rule Data", order = 0)]
public sealed class PlayerGameRuleData : ScriptableObject
{
    private const int DefaultObstacleMask = (1 << 0) | (1 << 3) | (1 << 7) | (1 << 11);
    private const int DefaultGroundMask = (1 << 0) | (1 << 3) | (1 << 11);

    [Header("이동")]
    [Tooltip("걸어 올라갈 수 있는 최대 경사각(도). 이 값을 넘는 경사는 벽으로 취급한다. 접지 판정과 대시 등판이 이 값을 공유한다.")]
    [SerializeField, Range(1f, 89f)] private float maxWalkableSlopeAngle = 60f;

    [Tooltip("접지 이동 중 올라갈 수 있는 낮은 턱의 최대 높이(m). 맵 계단 에셋이 들어오면 실제 치수에 맞춰 조정한다.")]
    [SerializeField, Min(0f)] private float stepOffset = 0.3f;

    [Tooltip("플레이어 이동을 막는 정적 지오메트리 레이어(Default | Ground | Wall | Env).")]
    [SerializeField] private LayerMask obstacleMask = DefaultObstacleMask;
    [Tooltip("생존 상태에서 접지로 인정하는 레이어(Default | Ground | Env). Wall은 제외한다.")]
    [SerializeField] private LayerMask groundMask = DefaultGroundMask;
    [Tooltip("Soul 상태에서 접지로 인정하는 레이어. Player/Soul/전투 판정 레이어는 넣지 않는다.")]
    [SerializeField] private LayerMask soulGroundMask = DefaultGroundMask;
    [Tooltip("켜면 생존 플레이어끼리 이동을 막는다. Soul은 이 값과 무관하게 Player를 통과한다.")]
    [SerializeField] private bool blockOtherPlayers;

    [Header("중력")]
    [Tooltip("Motor 수동 중력의 최대 낙하 속도(m/s).")]
    [SerializeField, Min(0f)] private float maxFallSpeed = 30f;

    [Header("넉백")]
    [SerializeField, Min(0f)] private float minKnockbackTime = 0.15f;
    [SerializeField, Min(0f)] private float maxKnockbackTime = 1.5f;
    [SerializeField, Min(0f)] private float knockbackStopSpeed = 0.15f;
    [Tooltip("넉백 속도의 선형 감속(m/s²). 6은 기존 PhysX 마찰 0.6 × 중력 9.81과 비슷한 감속이다.")]
    [SerializeField, Min(0f)] private float knockbackDeceleration = 6f;

    public float MaxWalkableSlopeAngle => maxWalkableSlopeAngle;
    public float StepOffset => Mathf.Max(0f, stepOffset);
    public LayerMask ObstacleMask => obstacleMask;
    public LayerMask GroundMask => groundMask;
    public LayerMask SoulGroundMask => soulGroundMask;
    public bool BlockOtherPlayers => blockOtherPlayers;
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
