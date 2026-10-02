using UnityEngine;

/// <summary>
/// 23호 전기 장판 수치. 기획: Docs/design/boss/boss-electric-floor.md §10(임시 수치).
/// <see cref="BossDataSO.electricFloor"/> 가 비어 있으면 이 기본값으로 돈다.
/// </summary>
[CreateAssetMenu(menuName = "Monster/Boss/Electric Floor Data", fileName = "BossElectricFloorData")]
public class BossElectricFloorDataSO : ScriptableObject
{
    [Header("일반 전투 (§4.2)")]
    [Tooltip("전투 시작(착지) 뒤 첫 예고까지(초). 제압·충전 기믹이 끝난 뒤 재개 대기도 이 값.")]
    [Min(0f)] public float firstDelay = 4f;
    [Tooltip("일반 장판 예고 시간(초).")]
    [Min(0.05f)] public float normalWarnTime = 1.2f;
    [Tooltip("전기 VFX 가 끝난 뒤 다음 예고까지(초).")]
    [Min(0f)] public float normalGapAfterVfx = 5f;

    [Header("송전기 충전 기믹 (§5)")]
    [Min(0.05f)] public float chargeWarnTime = 2f;
    [Tooltip("A/B 한 그룹이 모두 끝난 뒤 다음 그룹까지(초).")]
    [Min(0f)] public float chargeGroupGap = 1f;

    [Header("공통")]
    [Tooltip("전기 VFX 유지(초). 이 동안 추가 피해 없음.")]
    [Min(0.05f)] public float vfxDuration = 0.4f;
    [Tooltip("1회 피해(고정). 방어·감소·보호막 적용되는 일반 피해.")]
    [Min(0)] public int damage = 20;
    [Tooltip("멈추는 그로기 종류(체크형). 기본 = 제압 + 송전기 전멸 그로기.")]
    public BossPauseCondition pauseOn = BossPauseCondition.Suppress | BossPauseCondition.PylonGroggy;

    [Header("연출 (임시 — 민경 교체 훅)")]
    [Tooltip("예고 사각형 바깥 색(연하게).")]
    public Color warnOuterColor = new Color(1f, 0.15f, 0.1f, 0.28f);
    [Tooltip("안쪽에서 차오르는 진한 색.")]
    public Color warnFillColor = new Color(1f, 0.05f, 0.02f, 0.55f);
    [Tooltip("임시 전기 표시 색.")]
    public Color electricColor = new Color(0.45f, 0.85f, 1f, 0.8f);
    [Tooltip("임시 전기 깜빡임 속도(라디안/초). 0 = 깜빡임 없이 고정.")]
    [Min(0f)] public float electricFlickerSpeed = 60f;
    [Tooltip("타일마다 발동 순간 생성할 전기 VFX(선택). 비우면 임시 색 타일만. vfxDuration 뒤 파괴.")]
    public GameObject electricVfxPrefab;
    [Tooltip("바닥 위 띄우는 높이(m) — Z 파이팅 방지.")]
    [Min(0f)] public float surfaceOffset = 0.06f;
}
