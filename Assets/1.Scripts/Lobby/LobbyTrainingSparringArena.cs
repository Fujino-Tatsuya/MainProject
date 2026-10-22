using System;
using UnityEngine;

/// <summary>
/// TRAIN_03 훈련 상대 배치(할 일 86 T5c) — 3.LobbyScene 훈련실. 좌표는 임시, 최종 위치는 사용자 배치.
/// <list type="bullet">
/// <item><see cref="SpawnPoint"/> = A/B 위치(벽에서 떨어진 곳, 명세 §9.2) — Train03A 진입 때 상대가 여기 스폰.</item>
/// <item><see cref="ResetPoint"/> = C 위치 — B→C 초기화 때 상대가 여기로 옮겨진다(간파 넉백으로 지정 벽에 닿을 수 있는 거리, 명세 §9.5).</item>
/// <item><see cref="DesignatedWalls"/> = 유효 벽 — Wall 레이어 콜라이더. 여기 든 콜라이더에 부딪힐 때만 벽 목표·칸 감소를 인정한다(장식 벽 불인정).</item>
/// </list>
/// 상대는 스폰 프리팹이라 씬 콜라이더를 직렬화로 못 가리킨다 — <see cref="LobbyTrainingSparring"/> 가 스폰 직후 <see cref="TrainingSparringBot.ServerConfigureArena"/> 로 넣는다.
/// Gizmo(선택 안 해도 보임): A/B 노랑, C 하늘색, 지정 벽 주황 + 라벨.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTrainingSparringArena : MonoBehaviour
{
    [Tooltip("A/B 위치·방향 — 벽에서 떨어진 곳. Train03A 진입 때 훈련 상대가 여기 스폰된다(임시 좌표).")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("C 위치·방향 — B→C 초기화 때 상대를 옮긴다. 간파 넉백으로 지정 벽에 닿을 수 있게(임시 좌표).")]
    [SerializeField] private Transform resetPoint;

    [Tooltip("유효 벽(Wall 레이어 콜라이더). 비면 어떤 벽도 인정하지 않는다 — C 가 끝나지 않는다.")]
    [SerializeField] private Collider[] designatedWalls = Array.Empty<Collider>();

    public Transform SpawnPoint => spawnPoint;
    public Transform ResetPoint => resetPoint;
    public Collider[] DesignatedWalls => designatedWalls;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (spawnPoint != null)
            DrawPoint(spawnPoint, new Color(1f, 0.85f, 0.1f, 0.9f), "TRAIN_03 상대 A/B");
        if (resetPoint != null)
            DrawPoint(resetPoint, new Color(0.3f, 0.8f, 1f, 0.9f), "TRAIN_03 상대 C");

        if (designatedWalls == null)
            return;

        foreach (var wall in designatedWalls)
        {
            if (wall == null)
                continue;

            var bounds = wall.bounds;
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.35f);
            Gizmos.DrawCube(bounds.center, bounds.size);
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
            UnityEditor.Handles.Label(bounds.center + Vector3.up * (bounds.extents.y + 0.3f), $"TRAIN_03 지정 벽 {wall.name}");
        }
    }

    private static void DrawPoint(Transform point, Color color, string label)
    {
        var p = point.position;
        Gizmos.color = color;
        Gizmos.DrawWireCube(p + Vector3.up, new Vector3(1f, 2f, 1f));
        Gizmos.DrawLine(p + Vector3.up, p + Vector3.up + point.forward * 1.5f);
        UnityEditor.Handles.Label(p + Vector3.up * 2.4f, label);
    }
#endif
}
