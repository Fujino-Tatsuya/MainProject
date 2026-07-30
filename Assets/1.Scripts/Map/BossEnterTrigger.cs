using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// BossEnter 존 점유 감지 (PLAN §6 개정). 존 프리팹은 비네트워크 규약이라 일반 MonoBehaviour로 두고,
// MapContentSpawner가 BossRoom 역할 존 스폰 시 "서버에서만" 동적 부착한다(판정 서버 권한).
//
// 존 안의 생존 플레이어 유무를 추적해 BossTeleportManager에 알린다:
//  - 1명 이상 진입 → 카운트다운 시작 / 전원 이탈(또는 전멸) → 카운트다운 취소 (팀장 확정, 로아식)
[RequireComponent(typeof(BoxCollider))]
public class BossEnterTrigger : MonoBehaviour
{
    private readonly HashSet<Player> _inside = new HashSet<Player>();
    private BoxCollider _box;
    private float _nextPruneTime;

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
    }

    private void OnTriggerEnter(Collider other) => Track(other, true);
    private void OnTriggerExit(Collider other) => Track(other, false);

    private void Track(Collider other, bool entered)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        Player player = other.GetComponentInParent<Player>();
        if (player == null) return;

        if (entered)
        {
            if (player.CurrentHealth > 0) _inside.Add(player);
        }
        else
        {
            // 플레이어는 콜라이더가 여럿(캡슐+헛박스) — 하나만 나가도 Exit가 오므로
            // 본체가 아직 박스 안이면 이탈로 치지 않는다(경계 깜빡임 방지).
            if (_box.bounds.Contains(player.transform.position)) return;
            _inside.Remove(player);
        }

        Push();
    }

    // 존 안에서 사망/디스폰한 플레이어 정리 — Exit 이벤트가 안 오는 경우 대비.
    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || _inside.Count == 0) return;
        if (Time.time < _nextPruneTime) return;
        _nextPruneTime = Time.time + 0.5f;

        if (_inside.RemoveWhere(p => p == null || p.CurrentHealth <= 0) > 0)
            Push();
    }

    private void Push()
    {
        if (BossTeleportManager.Instance == null)
        {
            Debug.LogError("[BossEnterTrigger] BossTeleportManager가 씬에 없습니다.", this);
            return;
        }
        BossTeleportManager.Instance.SetOccupied(_inside.Count > 0);
    }
}
