using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로비 임무 브리핑 잠금·카메라(할 일 86 T6c → 할 일 157 ⑨, 명세 §5 L30–L34·Q7 A) — <see cref="LobbyTrainingDirector"/> 와 같은 오브젝트(씬 상주 NetworkObject)에 붙는다.
/// 규칙은 <see cref="LobbyBriefingLockSession"/>·<see cref="LobbyBriefingLockLedger"/>·<see cref="LobbyBriefingRules"/>, 여기는 구독·잠금 호출·카메라만 한다.
/// <list type="bullet">
/// <item>대사 4행(L31 평가 → L32 생산 시설 → L33 반란 → L34 출격 안내)은 진행기 공용 대사 스케줄러가 서버 시각으로 한 행씩 넘긴다(여기는 시간을 재지 않는다).
/// 옛 무전 BRIEF_01~04 는 더 재생하지 않는다(키 보존). 목표 패널 숨김·대사·배경 ID 는 파사드(<see cref="LobbyTutorialState"/>)가 단계표로 낸다.</item>
/// <item>잠금(서버) = 묶음 첫 행(L31) 진입 때 <see cref="LobbyRadio.ServerClear"/>(기존 무전 종료) → 접속자 전원(늦은 참가자 포함) 잠금, 묶음 안 행 이동은 그대로,
/// 묶음 밖(L34 끝 → L40·회차 초기화)으로 나가면 해제. 브리핑 중 새로 생긴 플레이어 오브젝트도 0.25초 주기로 잠근다. 이미 다른 계통이 잠근 사람은 건드리지 않는다(장부).</item>
/// <item>조작 제한 = 보스 등장 연출 잠금 재사용(<see cref="PlayerEncounterLock.BeginCinematicServer"/> — FSM Cinematic·대시·입력 정책·무적).</item>
/// <item>복구: 정상 완료·단계 이탈(회차 초기화)·디스폰(판 끝·씬 언로드·호스트 셧다운) 모두 장부의 잠금을 푼다. 접속 끊긴 사람은 장부에서만 뺀다.</item>
/// <item>카메라(전 피어, 복제된 진행기 단계로): 묶음 동안 <see cref="CameraTargetSwitcher.SetTemporaryFollow"/>(보스 엘리베이터 연출 API)로 <see cref="cameraFocus"/> 를 따라가고,
/// 묶음을 떠나거나 이 컴포넌트가 사라지면 <see cref="CameraTargetSwitcher.ClearTemporaryFollow"/>.</item>
/// </list>
/// 실전 초기화(명세 §10)는 하지 않는다 — 로비 플레이어는 메인 진입 때 디스폰되고 MapScene 이 새로 스폰한다. 상세: Docs/tech/lobby-training.md "브리핑".
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyBriefing : NetworkBehaviour
{
    private const string Tag = "[LobbyTraining/Brief]";
    private const float LockTickInterval = 0.25f;

    [Tooltip("브리핑 동안 카메라가 따라갈 지점(브리핑 위치). 비우면 카메라는 건드리지 않는다.")]
    [SerializeField] private Transform cameraFocus;

    // 서버
    private LobbyTrainingDirector _director;
    private readonly LobbyBriefingLockSession _session = new LobbyBriefingLockSession();
    private readonly LobbyBriefingLockLedger _locks = new LobbyBriefingLockLedger();
    private readonly List<ulong> _idBuffer = new List<ulong>();
    private float _lockTimer;

    // 전 피어(카메라)
    private bool _wantCamera;
    private CameraTargetSwitcher _heldCamera;

    /// <summary>[서버] 브리핑 묶음(L31~L34) 안이라 잠금을 걸고 있다.</summary>
    public bool ServerIsRunning => IsServer && _session.IsHolding;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        LobbyTrainingDirector.StepChanged += HandleStepChanged;

        var director = GetComponent<LobbyTrainingDirector>();
        if (director != null && director.IsSpawned)
            HandleStepChanged();

        if (!IsServer)
            return;

        _director = director;
        if (_director == null)
        {
            Debug.LogError($"{Tag} 같은 오브젝트에 LobbyTrainingDirector 가 없다 — 브리핑 잠금이 걸리지 않는다.", this);
            return;
        }

        _director.ServerFlowChanged += HandleFlowChangedServer;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        // 진행기가 먼저 스폰돼 첫 알림을 놓쳤을 수 있다 — 지금 상태로 한 번 맞춘다.
        HandleFlowChangedServer();
    }

    public override void OnNetworkDespawn()
    {
        LobbyTrainingDirector.StepChanged -= HandleStepChanged;

        if (IsServer)
        {
            if (_director != null)
                _director.ServerFlowChanged -= HandleFlowChangedServer;
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

            // 판 끝·씬 언로드·호스트 셧다운 — 남은 잠금을 푼다(이미 디스폰된 플레이어는 잠금도 함께 사라졌다).
            if (_session.End())
                Debug.Log($"[SceneFlow] {Tag} 브리핑 중단 — 진행기 디스폰");
            ReleaseLocks("진행기 디스폰");
            _director = null;
        }

        ClearPresentation();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        LobbyTrainingDirector.StepChanged -= HandleStepChanged;
        ClearPresentation();
        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        // 카메라 리그·로컬 플레이어가 늦게 붙을 수 있다 — 원하는데 못 잡았으면 다시 시도.
        if (_wantCamera && _heldCamera == null)
            HoldCamera();

        if (!IsServer || !_session.IsHolding)
            return;

        _lockTimer -= Time.unscaledDeltaTime;
        if (_lockTimer <= 0f)
        {
            _lockTimer = LockTickInterval;
            // 브리핑 중 접속·재스폰한 플레이어도 잠근다(이미 잠근 사람은 그대로).
            LockConnected();
        }
    }

    // ---------------- 서버 ----------------

    private void HandleFlowChangedServer()
    {
        if (_director == null)
            return;

        switch (_session.Update(LobbyBriefingRules.IsBriefing(_director.CurrentView)))
        {
            case LobbyBriefingLockAction.Begin:
                BeginServer();
                break;
            case LobbyBriefingLockAction.Release:
                ReleaseLocks($"브리핑 묶음 이탈 → {_director.DescribeStep()}");
                break;
        }
    }

    private void BeginServer()
    {
        // 명세 §4 — 브리핑이 시작되면 재생 중·대기 중인 기존 무전(TRAIN_END 등)을 끈다. 브리핑 대사는 무전이 아니라 대사 행(파사드)이다.
        var radio = LobbyRadio.Active;
        if (radio != null)
            radio.ServerClear();

        _lockTimer = LockTickInterval;
        LockConnected();
        Debug.Log($"[SceneFlow] {Tag} 브리핑 시작 — {_director.DescribeStep()}, 잠금 {_locks.Count}명");
    }

    // 접속자 전원(훈련 명단 밖 늦은 참가자 포함) — 플레이어 오브젝트가 있는 사람만, 이미 잠근 사람·다른 계통이 잠근 사람은 건너뛴다.
    private void LockConnected()
    {
        if (NetworkManager == null)
            return;

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            var encounterLock = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerEncounterLock>() : null;
            if (encounterLock == null || !encounterLock.IsSpawned)
                continue;

            if (!_locks.ShouldLock(client.ClientId, encounterLock.IsCinematicLocked))
                continue;

            if (!encounterLock.BeginCinematicServer())
                continue;

            _locks.Record(client.ClientId);
            Debug.Log($"[SceneFlow] {Tag} 조작 제한 client {client.ClientId}");
        }
    }

    // 이 컴포넌트가 건 잠금만 푼다(장부).
    private void ReleaseLocks(string reason)
    {
        _locks.TakeAll(_idBuffer);
        if (_idBuffer.Count == 0)
            return;

        foreach (var clientId in _idBuffer)
        {
            var encounterLock = FindLock(clientId);
            if (encounterLock == null || !encounterLock.IsSpawned)
                continue;

            encounterLock.EndCinematicServer();
        }

        Debug.Log($"[SceneFlow] {Tag} 조작 제한 해제({reason}) — [{string.Join(",", _idBuffer)}]");
    }

    private PlayerEncounterLock FindLock(ulong clientId)
    {
        if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
            return null;

        return client.PlayerObject.GetComponent<PlayerEncounterLock>();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        // 플레이어 오브젝트와 함께 잠금도 사라진다 — 장부에서만 뺀다(남은 사람으로 계속).
        if (_locks.Forget(clientId))
            Debug.Log($"[SceneFlow] {Tag} 브리핑 중 퇴장 client {clientId} — 잠금 장부에서 뺌, 남은 {_locks.Count}명으로 계속");
    }

    // ---------------- 전 피어(카메라) ----------------

    private void HandleStepChanged()
    {
        var director = LobbyTrainingDirector.Active;
        var step = director != null ? director.CurrentView : default;

        _wantCamera = cameraFocus != null && LobbyBriefingRules.IsBriefing(step);
        if (_wantCamera)
            HoldCamera();
        else
            ReleaseCamera();
    }

    private void HoldCamera()
    {
        if (_heldCamera != null || cameraFocus == null)
            return;

        var cameraSwitcher = CameraTargetSwitcher.Active;
        if (cameraSwitcher == null)
            return;

        cameraSwitcher.SetTemporaryFollow(cameraFocus);
        _heldCamera = cameraSwitcher;
    }

    private void ReleaseCamera()
    {
        if (_heldCamera == null)
            return;

        _heldCamera.ClearTemporaryFollow();
        _heldCamera = null;
    }

    private void ClearPresentation()
    {
        _wantCamera = false;
        ReleaseCamera();
    }
}
