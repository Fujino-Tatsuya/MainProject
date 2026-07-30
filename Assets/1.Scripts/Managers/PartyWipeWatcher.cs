using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 전멸 감지 → Result 전환. 서버(또는 오프라인) 전용.
///
/// 판정 기준은 <see cref="PlayerLifeState.PermanentDead"/>다. 즉 <b>목숨을 다 쓴 뒤 죽은 경우</b>만
/// 전멸로 센다. Soul은 아직 부활 여지가 있는 상태이므로 전멸이 아니다
/// (목숨이 남아 있으면 Temp_MultiGameRule이 Soul로 보내고, 0이면 PermanentDead로 보낸다).
///
/// 모두가 PermanentDead인 상태가 유예시간 동안 유지되면 <see cref="MapSceneManager.GoToResult"/>를
/// 한 번 호출한다. 클라 브로드캐스트와 씬 전환은 MapSceneManager가 이미 소유하므로 여기서는
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

        if (HasAnyoneNotPermanentlyDead())
        {
            _wipeElapsed = 0f;
            return;
        }

        _wipeElapsed += elapsed;
        if (_wipeElapsed < wipeGraceSeconds)
            return;

        _fired = true;
        Debug.Log("[SceneFlow] PartyWipeWatcher — 전원 PermanentDead 확인, Result로 전환한다.");

        // 결과 화면이 읽을 값을 씬 전환 전에 확정한다(전멸이므로 클리어 아님).
        SessionStatsTracker.Active?.Capture(cleared: false);

        mapSceneManager.GoToResult();
    }

    /// <summary>
    /// 한 명이라도 PermanentDead가 아니면 게임이 계속된다(Alive·DeadPresentation·Soul 포함).
    /// 플레이어가 아직 스폰되지 않은 구간을 전멸로 오판하지 않도록 0명도 "남아 있음"으로 취급한다.
    /// </summary>
    private bool HasAnyoneNotPermanentlyDead()
    {
        PlayerLifeCycleController[] players = FindObjectsByType<PlayerLifeCycleController>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        if (players.Length == 0)
            return true;

        foreach (PlayerLifeCycleController player in players)
        {
            if (player != null && player.State != PlayerLifeState.PermanentDead)
                return true;
        }

        return false;
    }
}
