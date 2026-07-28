using UnityEngine;

/// <summary>
/// 플레이어 공용 이동/게임 규칙(ScriptableObject). 대시·일반 이동·접지 판정이 함께 참조하는 단일 소스.
/// (PLAN §5 "등판각은 일반 이동 규칙 공유", "낮은 턱·지면 Snap·등판각은 Player GameRule 직렬화 값")
///
/// v1은 걸을 수 있는 최대 경사각만 담는다. Soul soulSpeed, 기본 LifeCount, 지면 Snap 거리,
/// 낮은 턱 높이 등은 각 기능(Soul/W3c)이 병합·안정화된 뒤 이 규칙으로 이관한다.
/// </summary>
[CreateAssetMenu(fileName = "PlayerGameRuleData", menuName = "BeaverLobby/Player/Game Rule Data", order = 0)]
public sealed class PlayerGameRuleData : ScriptableObject
{
    [Header("이동")]
    [Tooltip("걸어 올라갈 수 있는 최대 경사각(도). 이 값을 넘는 경사는 벽으로 취급한다. 접지 판정과 대시 등판이 이 값을 공유한다.")]
    [SerializeField, Range(1f, 89f)] private float maxWalkableSlopeAngle = 60f;

    public float MaxWalkableSlopeAngle => maxWalkableSlopeAngle;

    /// <summary>등판각을 접지 판정용 up-dot 임계값으로 변환한다. dot(normal, up) &gt;= 이 값이면 지면.</summary>
    public float WalkableGroundUpDot => Mathf.Cos(maxWalkableSlopeAngle * Mathf.Deg2Rad);
}
