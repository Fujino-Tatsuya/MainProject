using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 전멸 감지 → Result 전환. 서버(또는 오프라인) 전용.
///
/// 판정은 <see cref="PartyLivesRules.IsPartyWiped"/> — 전원이 <see cref="PlayerLifeState.PermanentDead"/>이거나
/// <b>파티 공용 목숨이 0이라 부활할 수 없는 Soul</b>이면 전멸이다(할 일 30 — 공용 풀이면 목숨이 남을 때 Soul 이 된 사람이
/// 다른 사람의 부활로 풀이 비어 Soul 에 갇힐 수 있다). 공용 목숨이 남은 Soul·차감 없는 부활 대기 Soul(튜토리얼 G11 전)은 전멸이 아니다.
/// MultiGameRule 이 없으면 예전 규칙(전원 PermanentDead).
///
/// 전멸 상태가 유예시간 동안 유지되면 먼저 <see cref="PartyWipeRespawner.TryRespawnServer"/>(할 일 30 (2) — 보스 재도전 리스폰)를 부르고,
/// 리스폰 대상이 아니면 <see cref="MapSceneManager.GoToResult"/>를 한 번 호출한다. 보스 클리어·나가기는 GoToResult 를 직접 부르므로 여기를 거치지 않는다. 클라 브로드캐스트와 씬 전환은 MapSceneManager가 이미 소유하므로 여기서는
/// 판정만 한다.
/// </summary>
public sealed class PartyWipeWatcher : MonoBehaviour
{
    [SerializeField] private MapSceneManager mapSceneManager;

    [Tooltip("전멸 판정 주기(초).")]
    [SerializeField, Min(0.05f)] private float checkInterval = 0.25f;

    [Tooltip("전원 PermanentDead가 이 시간 동안 유지되면 Result로 넘어간다. 사망 연출 여유분.")]
    [SerializeField, Min(0f)] private float wipeGraceSeconds = 2f;

    private float _checkTimer;
    private float _wipeElapsed;
    private bool _fired;
    private readonly System.Collections.Generic.List<PartyMemberLife> _members =
        new System.Collections.Generic.List<PartyMemberLife>();

    /// <summary>[서버] 전멸 유예 중(전멸 판정이 났고 리스폰·결과 판정을 기다린다) — 판 도중 재합류 스폰을 미룬다(C14). 읽기 전용.</summary>
    public bool IsWipeGracePending => !_fired && _wipeElapsed > 0f;

    private void Awake()
    {
        if (mapSceneManager == null)
            mapSceneManager = FindFirstObjectByType<MapSceneManager>();
    }

    private void Update()
    {
        if (_fired || mapSceneManager == null)
            return;

        // 온라인이면 서버만 판정한다. 오프라인(단독 실행)은 그대로 진행.
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening && !networkManager.IsServer)
            return;

        _checkTimer -= Time.deltaTime;
        if (_checkTimer > 0f)
            return;

        float elapsed = Mathf.Max(checkInterval, Time.deltaTime);
        _checkTimer = checkInterval;

        if (!IsPartyWiped())
        {
            _wipeElapsed = 0f;
            return;
        }

        _wipeElapsed += elapsed;
        if (_wipeElapsed < wipeGraceSeconds)
            return;

        _fired = true;

        // 할 일 30 (2) — 리스폰할 수 있는 판이면 결과 화면 대신 복도4(절차 맵 = 보스 진입 패드 밖)로 전원 리스폰.
        // 성공하면 래치를 풀어 다음 전멸도 다시 판정한다. 실패(대상 아님·지점 없음)면 아무것도 안 바뀌었으니 기존대로 결과 화면.
        if (PartyWipeRespawner.TryRespawnServer())
        {
            _fired = false;
            _wipeElapsed = 0f;
            return;
        }

        Debug.Log("[SceneFlow] PartyWipeWatcher — 전멸 확인(전원 PermanentDead 또는 공용 목숨 0 의 Soul), Result로 전환한다.");

        // 결과 화면이 읽을 값을 씬 전환 전에 확정한다(전멸이므로 클리어 아님).
        SessionStatsTracker.Active?.Capture(cleared: false);

        mapSceneManager.GoToResult();
    }

    /// <summary>
    /// 한 명이라도 다시 싸울 수 있으면 게임이 계속된다(Alive·DeadPresentation·공용 목숨이 남은 Soul·차감 없는 부활 대기 Soul).
    /// 플레이어가 아직 스폰되지 않은 구간을 전멸로 오판하지 않도록 0명은 전멸이 아니다.
    /// </summary>
    private bool IsPartyWiped()
    {
        // 씬 탐색 대신 등록식 목록(할 일 69 — 보스전 중 FindObjectsByType 한 번 8 ms 실측).
        System.Collections.Generic.IReadOnlyList<PlayerLifeCycleController> players = PlayerLifeCycleController.Active;

        // 규칙도 등록식(MultiGameRule.Current) — 규칙 없는 씬에서 주기마다 FindFirstObjectByType 하지 않는다.
        // 규칙이 없으면 Soul 을 '부활 가능'으로 본다 = 예전 규칙(전원 PermanentDead 만 전멸).
        MultiGameRule gameRule = MultiGameRule.Current;
        int partyLives = gameRule != null && gameRule.IsSpawned ? gameRule.PartyLives : 1;

        _members.Clear();
        for (int i = 0; i < players.Count; i++)
        {
            PlayerLifeCycleController player = players[i];
            if (player == null)
                continue;

            bool free = player.State == PlayerLifeState.Soul &&
                PlayerLifeCycleController.ServerDeathKeepsSoul != null &&
                PlayerLifeCycleController.ServerDeathKeepsSoul(player.OwnerClientId);
            _members.Add(new PartyMemberLife(player.State, free));
        }

        return PartyLivesRules.IsPartyWiped(partyLives, _members);
    }
}
