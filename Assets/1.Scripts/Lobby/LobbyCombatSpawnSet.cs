using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>로비 전투 스폰 한 마리 — 기준점(<see cref="LobbyTrainingCombatSpawns"/> 위치)에서의 오프셋.</summary>
[Serializable]
public sealed class LobbyCombatSpawnEntry
{
    [Tooltip("로그·이름용(옛 존2 지점 이름).")]
    public string pointName;
    [Tooltip("NetworkObject + MonsterBase 필수, NetworkPrefabs 등록.")]
    public GameObject prefab;
    [Tooltip("기준점에서의 오프셋(m, 회전·스케일 없음).")]
    public Vector3 offset;
    [Tooltip("스폰 회전(Y, 도).")]
    public float yawDegrees;
}

/// <summary>
/// [데이터] T22 전투 훈련 상대 구성 — 할 일 157 ⑥(명세 §5 T22, Q9 = 6마리). 로비 전용이라 존2(ZoneS_typeA)를 고쳐도 따라가지 않는다.
/// 연결 = <see cref="LobbyRules.CombatSpawnSet"/>(씬 무수정). 비어 있으면 <see cref="LobbyTrainingCombatSpawns"/> 가 옛 존2 구성을 쓴다.
/// 처음 값 = 존2 9개 중 중앙 박격포 1 + 남·북 2쌍 + 동쪽 깨물이 1(오프셋·회전 그대로) — 훈련실 폭(x 8 m)에 맞는 남·북 위주.
/// </summary>
[CreateAssetMenu(fileName = "LobbyCombatSpawnSet", menuName = "Lobby/Lobby Combat Spawn Set")]
public sealed class LobbyCombatSpawnSet : ScriptableObject
{
    [SerializeField] private LobbyCombatSpawnEntry[] entries = Array.Empty<LobbyCombatSpawnEntry>();

    public IReadOnlyList<LobbyCombatSpawnEntry> Entries => entries;
    public int Count => entries != null ? entries.Length : 0;

#if UNITY_EDITOR
    /// <summary>에디터 전용 — 행 통째 교체(에셋 생성 도구·테스트).</summary>
    public void EditorSetEntries(LobbyCombatSpawnEntry[] newEntries) => entries = newEntries ?? Array.Empty<LobbyCombatSpawnEntry>();
#endif
}
