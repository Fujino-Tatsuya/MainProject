using System.Collections.Generic;
using UnityEngine;

// BossEnter 발판 (PLAN §6 개정 → 할 일 89 파티 집결). 존 프리팹은 비네트워크 규약이라 일반 MonoBehaviour로 두고,
// MapContentSpawner가 BossRoom 역할 존 스폰 시 "서버에서만" 동적 부착한다(판정 서버 권한).
//
// 이 컴포넌트는 발판 박스만 내놓는다 — 판정은 BossTeleportManager 가 서버 Update 에서 참가자마다
// 본체(transform.position)가 박스 안인지(ContainsPoint) 보고 파티 집결 규칙(BossElevatorGather)으로 한다.
// 트리거 Enter/Exit 를 쓰지 않는다: 플레이어 콜라이더가 여럿이라 Exit 가 깜빡이고, 사망·디스폰엔 Exit 가 안 온다.
// 튜토리얼(S5) 발판 잠금(MapSceneBossPath.IsBossPadEnabled)도 판정 쪽에서 본다.
[RequireComponent(typeof(BoxCollider))]
public class BossEnterTrigger : MonoBehaviour
{
    // 본체 위치는 발 높이라 박스 바닥(존 로컬 0)과 거의 같다 — 바닥 아래로 살짝 내려가도 안으로 친다.
    private const float VerticalSlack = 0.5f;

    private static readonly List<BossEnterTrigger> ActiveTriggers = new List<BossEnterTrigger>();

    private BoxCollider _box;

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
    }

    private void OnEnable() => ActiveTriggers.Add(this);
    private void OnDisable() => ActiveTriggers.Remove(this);

    /// <summary>월드 좌표가 발판 박스(회전 포함, 아래로 <see cref="VerticalSlack"/> 여유) 안인가.</summary>
    public bool ContainsPoint(Vector3 worldPosition)
    {
        BoxCollider box = _box != null ? _box : GetComponent<BoxCollider>();
        if (box == null || !box.enabled) return false;

        Vector3 local = box.transform.InverseTransformPoint(worldPosition) - box.center;
        Vector3 half = box.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z
            && local.y >= -half.y - VerticalSlack && local.y <= half.y;
    }

    /// <summary>[서버] 켜진 발판 중 하나라도 이 위치를 담는가. 발판이 없으면(클라·스폰 전) false.</summary>
    public static bool AnyContains(Vector3 worldPosition)
    {
        for (int i = 0; i < ActiveTriggers.Count; i++)
        {
            if (ActiveTriggers[i] != null && ActiveTriggers[i].ContainsPoint(worldPosition))
                return true;
        }
        return false;
    }
}
