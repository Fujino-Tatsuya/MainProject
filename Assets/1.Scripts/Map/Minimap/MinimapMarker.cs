using System.Collections.Generic;
using UnityEngine;

// 미니맵 마커 타입 — 상시(퀘스트/스폰/보스입구/티어 노드) vs 동적(몬스터/플레이어).
public enum MinimapMarkerType
{
    Quest, Spawn, BossGate,
    NodeTier1, NodeTier2, NodeTier3,
    Monster, Player, Ally,
}

// 붙이면 미니맵에 자동 등록되는 마커 (활성/비활성 = 등록/해제).
// 몬스터 프리팹에는 이 컴포넌트를 Monster 타입으로 붙여두면 NGO 복제본에도 따라와
// 모든 클라의 미니맵에 표시된다. (몬스터 에셋 확정 시 부착 — MinimapController 주석 참조)
public class MinimapMarker : MonoBehaviour
{
    public MinimapMarkerType Type = MinimapMarkerType.Monster;

    private static readonly List<MinimapMarker> _all = new List<MinimapMarker>();
    public static IReadOnlyList<MinimapMarker> All => _all;

    private void OnEnable() => _all.Add(this);
    private void OnDisable() => _all.Remove(this);
}
