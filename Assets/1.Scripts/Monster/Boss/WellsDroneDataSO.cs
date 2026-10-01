using UnityEngine;

/// <summary>
/// 웰즈 자폭 드론 수치. 기획: Docs/design/boss/wells-suicide-drone.md §4 · §13.
/// <see cref="BossDataSO.wellsDrone"/> 가 비어 있으면 이 기본값으로 돈다.
/// </summary>
[CreateAssetMenu(menuName = "Monster/Boss/Wells Drone Data", fileName = "WellsDroneData")]
public class WellsDroneDataSO : ScriptableObject
{
    [Header("시간 (§4)")]
    [Min(0f)] public float firstDelay = 5f;
    [Tooltip("크로스헤어 추적(확정 3초).")]
    [Min(0.1f)] public float trackTime = 3f;
    [Tooltip("위치 고정 → 충돌까지.")]
    [Min(0.1f)] public float lockTime = 1f;
    [Tooltip("공격(또는 취소) 종료 후 다음 대상 선정까지.")]
    [Min(0f)] public float cooldown = 7f;
    [Tooltip("제압·충전 기믹 종료 직후 보장 대기(남은 시간이 이보다 짧으면 이 값).")]
    [Min(0f)] public float resumeMinDelay = 2f;

    [Header("범위 · 피해 (§7 · §9)")]
    [Tooltip("원 지름 = 보스방 타일 몇 칸(기획 0.5).")]
    [Min(0.05f)] public float diameterInTiles = 0.5f;
    [Min(0)] public int playerDamage = 25;
    [Tooltip("23호에게 주는 피해(일반 보스 피해 — 간파·취약 판정 안 탐).")]
    [Min(0)] public int bossDamage = 300;
    [Tooltip("멈추는 그로기 종류(체크형). 기본 = 제압 + 송전기 전멸 그로기.")]
    public BossPauseCondition pauseOn = BossPauseCondition.Suppress | BossPauseCondition.PylonGroggy;

    [Header("연출 (임시 — 민경 교체 훅)")]
    [Tooltip("드론 모델(연출 전용 — NetworkObject 아님, 충돌·판정 없음). 비우면 원·폭발만.")]
    public GameObject droneModel;
    [Tooltip("낙하 시작 높이(m).")]
    [Min(1f)] public float dropHeight = 12f;
    [Tooltip("lockTime 중 마지막 몇 초 동안 내려오는가.")]
    [Min(0.05f)] public float fallTime = 0.6f;
    [Tooltip("충돌 순간 생성할 폭발 VFX(선택). 비우면 임시 원 확산.")]
    public GameObject explosionVfxPrefab;
    [Tooltip("폭발 연출 유지(초) — 피해와 무관한 연출 값. 이게 끝난 뒤 쿨다운을 센다.")]
    [Min(0f)] public float explosionDuration = 0.5f;
    [Tooltip("크로스헤어 지름(m, 최소). 대상보다 약간 크게.")]
    [Min(0.5f)] public float crosshairSize = 2.4f;
    public Color crosshairColor = new Color(1f, 0.2f, 0.15f, 0.95f);
    public Color circleOuterColor = new Color(1f, 0.15f, 0.1f, 0.3f);
    public Color circleFillColor = new Color(1f, 0.05f, 0.02f, 0.6f);
}
