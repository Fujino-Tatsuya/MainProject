using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>TRAIN_02 스폰 한 마리 — 구성 지점 1개 × Count.</summary>
public struct LobbyCombatSpawn
{
    public string PointName;
    public GameObject Prefab;

    /// <summary>기준점 + 구성 오프셋(회전·스케일 없음).</summary>
    public Vector3 Candidate;

    /// <summary>실제 스폰 위치 — 후보가 NavMesh 위면 그 점, 아니면 가장 가까운 NavMesh 점(<see cref="Snapped"/>).</summary>
    public Vector3 Position;

    public Quaternion Rotation;

    /// <summary>후보가 NavMesh 밖이라 가까운 NavMesh 점으로 옮겼다(훈련실 폭 8 m 가 존2 오프셋 ±7.2 m 보다 좁다).</summary>
    public bool Snapped;

    /// <summary>반경 안에 NavMesh 가 없었다 — 기준점에 세운다(종류·수는 바꾸지 않는다).</summary>
    public bool NoNavMesh;
}

/// <summary>
/// TRAIN_02 스폰 기준점(할 일 86 T4) — 3.LobbyScene 훈련실 중간. 이 Transform 의 <b>위치만</b> 기준점으로 쓴다(회전·스케일 무시).
/// 구성 = 로비 전용 <see cref="LobbyCombatSpawnSet"/>(6마리, <see cref="LobbyRules.CombatSpawnSet"/> — 할 일 157 ⑥). 그 칸이 비었을 때만
/// 옛 방식 — 존2(ZoneS_typeA.prefab)의 <see cref="ZoneMonsterSpawnSet"/> 를 직접 참조해 그대로 읽는다(지점·종류·Count·분산, 9마리).
/// 훈련실(x 8 m)은 존2 동·서 오프셋(±7.2 m)보다 좁다 → NavMesh 밖 후보는 <see cref="NavMesh.SamplePosition"/> 로 가장 가까운 NavMesh 점에 붙인다.
/// 좌표는 임시 — 최종 위치는 사용자 배치. Gizmo: 기준점(노랑)·후보(빨강 = NavMesh 밖)·실제 위치(초록).
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTrainingCombatSpawns : MonoBehaviour
{
    [Tooltip("존2 일반 전투 구성 — ZoneS_typeA.prefab 루트의 ZoneMonsterSpawnSet(프리팹 에셋 참조, 인스턴스 아님).")]
    [SerializeField] private ZoneMonsterSpawnSet source;

    [Tooltip("후보가 NavMesh 밖일 때 가장 가까운 NavMesh 점을 찾을 반경(m).")]
    [SerializeField] private float navMeshSnapRadius = 5f;

    // 이보다 가까우면 '후보가 이미 NavMesh 위'로 본다(수평).
    private const float OnMeshTolerance = 0.1f;

    public ZoneMonsterSpawnSet Source => source;

    /// <summary>
    /// 스폰 목록을 만든다. <paramref name="result"/> 는 비우고 채운다. 구성이 비었으면 false.
    /// <paramref name="set"/>(로비 전용 6마리, 할 일 157 ⑥)에 행이 있으면 그것을, 없으면 옛 존2 구성(<see cref="source"/>)을 쓴다.
    /// </summary>
    public bool Resolve(List<LobbyCombatSpawn> result, LobbyCombatSpawnSet set = null)
    {
        result.Clear();
        var origin = transform.position;

        if (set != null && set.Count > 0)
        {
            foreach (var entry in set.Entries)
            {
                if (entry == null)
                    continue;

                result.Add(Snap(origin, new LobbyCombatSpawn
                {
                    PointName = entry.pointName,
                    Prefab = entry.prefab,
                    Candidate = origin + entry.offset,
                    Rotation = Quaternion.Euler(0f, entry.yawDegrees, 0f),
                }));
            }

            return result.Count > 0;
        }

        if (source == null)
            return false;

        var root = source.transform.position;
        foreach (var point in source.ResolveSpawnPoints())
        {
            if (point == null)
                continue;

            var prefab = point.MonsterPrefabOverride != null ? point.MonsterPrefabOverride : source.DefaultMonsterPrefab;
            for (var i = 0; i < point.Count; i++)
            {
                result.Add(Snap(origin, new LobbyCombatSpawn
                {
                    PointName = point.name,
                    Prefab = prefab,
                    Candidate = LobbyCombatRules.CandidatePosition(origin, root, point.GetSpawnPosition(i)),
                    Rotation = point.transform.rotation,
                }));
            }
        }

        return result.Count > 0;
    }

    private LobbyCombatSpawn Snap(Vector3 origin, LobbyCombatSpawn spawn)
    {
        if (NavMesh.SamplePosition(spawn.Candidate, out var hit, Mathf.Max(0.1f, navMeshSnapRadius), NavMesh.AllAreas))
        {
            spawn.Position = hit.position;
            spawn.Snapped = LobbyCombatStuckTracker.HorizontalDistance(hit.position, spawn.Candidate) > OnMeshTolerance;
        }
        else
        {
            spawn.Position = origin;
            spawn.NoNavMesh = true;
        }

        return spawn;
    }

#if UNITY_EDITOR
    private readonly List<LobbyCombatSpawn> _gizmoBuffer = new List<LobbyCombatSpawn>();
    private LobbyTrainingDirector _gizmoDirector;

    private void OnDrawGizmos()
    {
        var origin = transform.position;
        Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(origin + Vector3.up * 0.1f, 0.4f);
        Gizmos.DrawLine(origin + Vector3.left, origin + Vector3.right);
        Gizmos.DrawLine(origin + Vector3.back, origin + Vector3.forward);

        if (_gizmoDirector == null)
            _gizmoDirector = FindAnyObjectByType<LobbyTrainingDirector>();
        if (!Resolve(_gizmoBuffer, _gizmoDirector != null ? _gizmoDirector.CombatSpawnSet : null))
            return;

        foreach (var spawn in _gizmoBuffer)
        {
            if (spawn.Snapped || spawn.NoNavMesh)
            {
                Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.8f);
                Gizmos.DrawWireCube(spawn.Candidate + Vector3.up * 0.5f, Vector3.one * 0.6f);
                Gizmos.DrawLine(spawn.Candidate + Vector3.up * 0.5f, spawn.Position + Vector3.up * 0.5f);
            }

            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireCube(spawn.Position + Vector3.up, new Vector3(0.8f, 2f, 0.8f));
            Gizmos.DrawLine(spawn.Position + Vector3.up, spawn.Position + Vector3.up + spawn.Rotation * Vector3.forward);
        }
    }
#endif
}
