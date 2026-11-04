#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 209 대시 관통 확인 씬(DebugDashPassLab — 빌드 목록에 없다)의 몬스터 배치기.
/// 서버(호스트)가 세션이 열리면 레인별 몬스터를 정식 경로(Instantiate + <see cref="NetworkObject.Spawn"/>, <c>MonsterSpawner</c> 와 같다)로 세우고,
/// 바로 <see cref="MonsterBase.SetServerLogicSuspended"/>(true) 로 FSM 을 멈춰 인지·추격·공격하지 않게 한다(피격은 그대로 — 보스 연출 정지와 같은 게이트).
/// 몬스터를 씬에 직접 두지 않는 이유: 씬 배치 NetworkObject 는 해시가 씬 저장 때 정해져 저작 메뉴로 다시 만들 때 어긋나기 쉽다.
/// 패널 버튼 = 다시 놓기(죽였거나 밀린 몬스터를 처음 자리로). 호스트 1인 기준, 새 RPC 없음.
/// 씬은 저작 메뉴 <c>Tools/Debug/209 대시 관통 테스트 씬 만들기</c> 로 다시 만든다(<c>DashPassLabSceneBuilder</c>).
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugDashPassLab : MonoBehaviour
{
    private const string Tag = "[DashPassLab]";

    [Serializable]
    public struct Entry
    {
        [Tooltip("로그·패널 표시용 레인 이름.")]
        public string lane;
        [Tooltip("NetworkObject 가 있는 몬스터 프리팹(DefaultNetworkPrefabs 에 등록된 것).")]
        public GameObject prefab;
        public Vector3 position;
        [Tooltip("Y 회전(도). 180 = 플레이어 쪽(-Z)을 본다.")]
        public float yaw;
        [Tooltip("끄면 이 줄은 놓지 않는다(23호처럼 단독 스폰이 무거운 것).")]
        public bool enabled;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();
    [Tooltip("패널 위치(화면 픽셀).")]
    [SerializeField] private Vector2 panelPosition = new Vector2(10f, 10f);

    private readonly List<NetworkObject> spawned = new List<NetworkObject>();
    private bool spawnedOnce;
    private string status = string.Empty;

    public IReadOnlyList<Entry> Entries => entries;

    /// <summary>[저작 메뉴] 배치 목록을 통째로 바꾼다.</summary>
    public void SetEntries(IEnumerable<Entry> newEntries)
    {
        entries = new List<Entry>(newEntries);
    }

    private void Update()
    {
        if (spawnedOnce)
            return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return;

        spawnedOnce = true;
        if (networkManager.IsServer)
            SpawnAll();
    }

    private void OnDisable()
    {
        spawned.Clear();
    }

    private void SpawnAll()
    {
        int count = 0;
        foreach (Entry entry in entries)
        {
            if (!entry.enabled || entry.prefab == null)
                continue;

            GameObject go = Instantiate(entry.prefab, entry.position, Quaternion.Euler(0f, entry.yaw, 0f));
            if (!go.TryGetComponent(out NetworkObject netObj))
            {
                Debug.LogError($"{Tag} '{entry.prefab.name}' 에 NetworkObject 가 없다 — 놓지 않는다({entry.lane}).", this);
                Destroy(go);
                continue;
            }

            go.name = $"{entry.lane} · {entry.prefab.name}";
            netObj.Spawn(true);
            spawned.Add(netObj);
            count++;

            if (go.TryGetComponent(out MonsterBase monster))
                monster.SetServerLogicSuspended(true);
        }

        status = $"몬스터 {count}마리 배치(FSM 정지)";
        Edit.Log($"{Tag} {status}", this);
    }

    private void DespawnAll()
    {
        foreach (NetworkObject netObj in spawned)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(true);
        }

        spawned.Clear();
    }

    private void OnGUI()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        GUILayout.BeginArea(new Rect(panelPosition.x, panelPosition.y, 360f, 170f), GUI.skin.box);
        GUILayout.Label("209 대시 관통 테스트 — 각 레인 START 칸에서 +Z(W) 방향으로 대시");
        GUILayout.Label("A: 일반 3 일렬 → 2마리 관통·3번째 앞 정지 / B: 중간보스 / C: 일반 1 + 벽 / D: 일반 2 붙임(겹친 채 끝) / E: 23호");
        bool server = networkManager != null && networkManager.IsListening && networkManager.IsServer;
        GUI.enabled = server;
        if (GUILayout.Button("몬스터 다시 놓기"))
        {
            DespawnAll();
            SpawnAll();
        }
        GUI.enabled = true;
        if (!string.IsNullOrEmpty(status))
            GUILayout.Label(status);
        GUILayout.EndArea();
    }
}
#endif
