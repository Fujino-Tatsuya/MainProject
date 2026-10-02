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
    [Tooltip("원 지름 = 보스방 타일 몇 칸. 기획서 원안 0.5 → 팀장 10-02 Play 확인 후 2.5배(1.25).\n" +
             "피해 판정과 바닥 원이 이 값 하나를 같이 쓴다.")]
    [Min(0.05f)] public float diameterInTiles = 1.25f;
    [Min(0)] public int playerDamage = 25;
    [Tooltip("23호에게 주는 피해(일반 보스 피해 — 간파·취약 판정 안 탐). 300 → 120(0.4배, 팀장 10-02 — 범위 2.5배로 맞히기 쉬워짐).")]
    [Min(0)] public int bossDamage = 120;
    [Tooltip("멈추는 그로기 종류(체크형). 기본 = 제압 + 송전기 전멸 그로기.")]
    public BossPauseCondition pauseOn = BossPauseCondition.Suppress | BossPauseCondition.PylonGroggy;

    [Header("드론 모델")]
    [Tooltip("드론 모델(연출 전용 — NetworkObject 아님, 충돌·판정 없음). 비우면 원·폭발만.")]
    public GameObject droneModel;
    [Tooltip("드론 모델 크기 배율(모델 원본 = 1).")]
    [Min(0.1f)] public float droneScale = 2f;
    [Tooltip("낙하 시작 높이(m).")]
    [Min(1f)] public float dropHeight = 12f;
    [Tooltip("lockTime 중 마지막 몇 초 동안 내려오는가.")]
    [Min(0.05f)] public float fallTime = 0.6f;
    [Tooltip("충돌 순간 생성할 폭발 VFX(선택). 비우면 임시 원 확산.")]
    public GameObject explosionVfxPrefab;
    [Tooltip("폭발 연출 유지(초) — 피해와 무관한 연출 값. 이게 끝난 뒤 쿨다운을 센다.")]
    [Min(0f)] public float explosionDuration = 0.5f;
    [Header("크로스헤어 (추적 단계)")]
    [Tooltip("크로스헤어 기준 지름(m). 아래 시작/끝 배율이 여기에 곱해진다.")]
    [Min(0.5f)] public float crosshairSize = 2.4f;
    [Tooltip("추적 시작 때 크기 배율.")]
    [Min(0.1f)] public float crosshairStartScale = 1.5f;
    [Tooltip("다 줄어든 뒤 크기 배율 — 이 크기로 멈춰 있다가 위치 고정 순간 사라진다.")]
    [Min(0.1f)] public float crosshairEndScale = 0.8f;
    [Tooltip("추적 시간 중 줄어드는 구간 비율(0~1). 0.8 = 앞 80% 동안 줄고 남은 20% 는 작은 크기로 정지.")]
    [Range(0.05f, 1f)] public float crosshairShrinkPortion = 0.8f;
    [Tooltip("카메라가 이 거리(m)보다 멀면 그만큼 키운다 — 최소 표시 크기 보장(§6.1).")]
    [Min(1f)] public float crosshairReferenceDistance = 20f;
    [Tooltip("캐릭터 모델에 가리지 않게 카메라 쪽으로 당겨 그리는 거리(m).")]
    [Min(0f)] public float crosshairTowardCamera = 1.5f;
    public Color crosshairColor = new Color(1f, 0.2f, 0.15f, 0.95f);

    [Header("바닥 원 · 폭발 색")]
    public Color circleOuterColor = new Color(1f, 0.15f, 0.1f, 0.3f);
    public Color circleFillColor = new Color(1f, 0.05f, 0.02f, 0.6f);
    [Tooltip("임시 폭발 원 색(폭발 VFX 프리팹이 비었을 때).")]
    public Color explosionColor = new Color(1f, 0.55f, 0.1f, 0.85f);
    [Tooltip("임시 폭발 원이 범위 반경의 몇 배까지 퍼지는가.")]
    [Min(1f)] public float explosionGrowScale = 2.5f;
}
