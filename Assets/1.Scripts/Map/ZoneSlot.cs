using UnityEngine;
using System.Collections.Generic;

// Stage1 스켈레톤에 배치되는 존 슬롯 앵커 (v11 단일 골격).
// 생성기가 역할을 배정하고, 이 transform.position(손으로 맞춘 baseline)에 선택된 프리팹을 Instantiate 한다.
// 회전은 프리팹마다 다르므로 슬롯이 프리팹별 YawSteps(90° 4택)를 직접 들고 있는다(Save Placements가 저작).
// (기존 ZoneVolume/ZoneDefinitionSO + SpawnPoint 절차배치 모델을 대체)
public class ZoneSlot : MonoBehaviour
{
    // (슬롯 × 프리팹) → 90° 회전 스텝. Save Placements 툴이 채운다.
    [System.Serializable]
    public struct RotationEntry
    {
        public GameObject Prefab;          // 카탈로그 원본 프리팹 에셋
        [Range(0, 3)] public int YawSteps; // 0=0° 1=90° 2=180° 3=270°
        public bool HasPosition;           // true면 Position을 스폰 위치로 사용(조합별 오버라이드). false면 슬롯 baseline.
        public Vector3 Position;           // 조합별 저장 위치(월드). 문 위치가 프리팹마다 달라 baseline만으론 안 맞는 경우 대응.
    }

    [Header("=== 슬롯 정의 ===")]
    public int SlotID;
    public ZoneSize Size;
    [Tooltip("오버뷰 UI/검증용 풋프린트(가로 x, 세로 z). 실제 비주얼은 배치되는 프리팹이 가짐.")]
    public Vector2 Footprint = new Vector2(20f, 20f);

    [Header("=== 역할 후보 플래그 (배정 가능 역할) ===")]
    public bool IsQuestCandidate;
    public bool IsBossCandidate;
    public bool IsSpawnCandidate;

    [Header("=== 프리팹 고정 (선택) ===")]
    [Tooltip("설정 시 이 슬롯은 셔플·역할과 무관하게 항상 이 프리팹. 전투 셔플 풀에서도 제외됨(다른 슬롯 중복 방지).")]
    public GameObject FixedPrefab;
    [Tooltip("설정 시 이 슬롯이 퀘스트로 배정되면 랜덤 대신 이 프리팹을 쓴다(슬롯↔퀘스트 고정 페어링).")]
    public GameObject QuestPrefab;

    [Header("=== 프리팹별 회전 (Save Placements가 저작) ===")]
    [Tooltip("이 슬롯에 들어올 수 있는 각 프리팹의 90° 회전 스텝. 셔플로 뽑힌 프리팹을 이 표로 조회해 스폰 회전을 결정.")]
    public List<RotationEntry> Rotations = new List<RotationEntry>();

    [Header("=== 런타임 (생성기가 채움) ===")]
    public ZoneRole AssignedRole = ZoneRole.Combat;
    public bool IsFilled;

    public void ResetRuntime()
    {
        AssignedRole = ZoneRole.Combat;
        IsFilled = false;
    }

    // 뽑힌 프리팹의 저작된 회전 스텝 조회. 미저작이면 false.
    public bool TryGetYaw(GameObject prefab, out int yawSteps)
    {
        if (prefab != null && Rotations != null)
            foreach (var r in Rotations)
                if (r.Prefab == prefab) { yawSteps = r.YawSteps; return true; }
        yawSteps = 0;
        return false;
    }

    // 조합별 저장 위치 조회. HasPosition=true면 그 위치, 아니면 슬롯 baseline(transform) 반환 + false.
    public bool TryGetPosition(GameObject prefab, out Vector3 position)
    {
        if (prefab != null && Rotations != null)
            foreach (var r in Rotations)
                if (r.Prefab == prefab) { position = r.HasPosition ? r.Position : transform.position; return r.HasPosition; }
        position = transform.position;
        return false;
    }

    // 저작 툴 전용: 프리팹의 회전 스텝 + 위치를 갱신(있으면 덮어쓰기, 없으면 추가). yaw 0~3 정규화, 위치는 오버라이드 저장.
    public void SetPlacement(GameObject prefab, int yawSteps, Vector3 position)
    {
        if (prefab == null) return;
        yawSteps = ((yawSteps % 4) + 4) % 4;
        for (int i = 0; i < Rotations.Count; i++)
            if (Rotations[i].Prefab == prefab)
            { var e = Rotations[i]; e.YawSteps = yawSteps; e.Position = position; e.HasPosition = true; Rotations[i] = e; return; }
        Rotations.Add(new RotationEntry { Prefab = prefab, YawSteps = yawSteps, Position = position, HasPosition = true });
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Size switch
        {
            ZoneSize.Large  => new Color(1f, 0.3f, 0.3f),
            ZoneSize.Medium => new Color(1f, 0.9f, 0.3f),
            ZoneSize.Small  => new Color(0.3f, 1f, 0.4f),
            _ => Color.white
        };
        Gizmos.DrawWireCube(transform.position, Vector3.one * 2f);
        // 방향 표시 (앵커 forward = 프리팹 배치 방향)
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2f);
    }
}
