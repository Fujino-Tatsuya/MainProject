using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로비 브리핑 BRIEF_01(할 일 86 T6c, PLAN §3.6, 명세 v0.2 §4·§10) — <see cref="LobbyTrainingDirector"/> 와 같은 오브젝트(씬 상주 NetworkObject)에 붙는다.
/// 규칙은 <see cref="LobbyBriefingModel"/>·<see cref="LobbyBriefingLockLedger"/>·<see cref="LobbyBriefingRules"/>, 여기는 구독·잠금 호출·UI·카메라만 한다.
/// <list type="bullet">
/// <item>시작(서버) = 진행기가 첫 입력 잠금 대사 행(L31, <see cref="LobbyBriefingRules.IsBriefing"/> — L30 중앙 집결 확정 뒤)이 되는 순간 — 그 revision 을 기억한다:
/// <see cref="LobbyRadio.ServerClear"/>(기존 무전 종료) → 접속자 전원 잠금 → BRIEF_01~04 를 앞 줄 표시 시간이 지날 때마다 하나씩 <see cref="LobbyRadio.ServerPlay"/>.</item>
/// <item>시간은 서버가 잰다 = 시작 지연 + 대사 표시 시간 합(<c>TutorialGuideTexts</c> SO 행) + 끝 여유. 클라 자막 종료 보고에 의존하지 않는다.
/// 끝나면 잠금 해제 → <see cref="LobbyTrainingDirector.ServerReportBriefingCompleted"/>(시작 revision) 1회 → 진행기가 잠금 묶음(L31~L34) 뒤 출격 집결(L40)로.
/// 대표 상호작용·개인 건너뛰기 없음. (할 일 157 ② — 대사 행 4개 분해·감독관 대사는 커밋 ⑨.)</item>
/// <item>조작 제한 = 보스 등장 연출 잠금 재사용(<see cref="PlayerEncounterLock.BeginCinematicServer"/> — FSM Cinematic·대시·입력 정책·무적).
/// 대상 = 접속자 전원(늦은 참가자 포함), 브리핑 중 새로 생긴 플레이어 오브젝트도 0.25초 주기로 잠근다. 이미 다른 계통이 잠근 사람은 건드리지 않는다(장부).</item>
/// <item>복구: 정상 완료·단계 이탈(회차 초기화)·디스폰(판 끝·씬 언로드·호스트 셧다운) 모두 장부의 잠금을 푼다. 접속 끊긴 사람은 장부에서만 뺀다.</item>
/// <item>표시(전 피어, 복제된 진행기 단계로): 목표 패널 '작전 설명 중' → 끝나면 '출격 지점에 집결'. 카메라 = <see cref="CameraTargetSwitcher.SetTemporaryFollow"/>
/// (보스 엘리베이터 연출 API)로 <see cref="cameraFocus"/> 를 따라가고, 브리핑이 끝나거나 이 컴포넌트가 사라지면 <see cref="CameraTargetSwitcher.ClearTemporaryFollow"/>.</item>
/// </list>
/// 실전 초기화(명세 §10)는 하지 않는다 — 로비 플레이어는 메인 진입 때 디스폰되고 MapScene 이 새로 스폰한다. 상세: Docs/tech/lobby-training.md "브리핑".
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyBriefing : NetworkBehaviour
{
    private const string Tag = "[LobbyTraining/Brief]";
    private const float LockTickInterval = 0.25f;

    [Tooltip("브리핑 대사 표시 시간을 읽는다(BRIEF_01~04 행). 비우거나 행이 없으면 줄마다 6초.")]
    [SerializeField] private TutorialGuideTextTable textTable;

    [Tooltip("첫 대사 전 지연(초) — 무전 종료가 클라에 닿은 뒤 첫 줄이 뜨게.")]
    [SerializeField, Min(0f)] private float leadSeconds = 0.5f;

    [Tooltip("마지막 대사가 끝난 뒤 여유(초) — 자막 페이드·RPC 지연. 이 시간이 지나야 조작이 풀리고 출격 집결이 켜진다.")]
    [SerializeField, Min(0f)] private float tailSeconds = 1f;

    [Tooltip("브리핑 동안 카메라가 따라갈 지점(브리핑 위치). 비우면 카메라는 건드리지 않는다.")]
    [SerializeField] private Transform cameraFocus;

    // 서버
    private LobbyTrainingDirector _director;
    private LobbyBriefingModel _model;
    private readonly LobbyBriefingLockLedger _locks = new LobbyBriefingLockLedger();
    private readonly List<LobbyBriefingLine> _lineBuffer = new List<LobbyBriefingLine>();
    private readonly List<string> _dueLines = new List<string>();
    private readonly List<ulong> _idBuffer = new List<ulong>();
    private float _lockTimer;
    private int _runRevision;

    // 전 피어(표시)
    private string _shownObjective;
    private bool _wantCamera;
    private CameraTargetSwitcher _heldCamera;
    private bool _warnedNoUi;

    /// <summary>[서버] 브리핑 진행 중(잠금을 걸고 있다).</summary>
    public bool ServerIsRunning => _model != null && _model.IsRunning;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        LobbyTrainingDirector.StepChanged += HandleStepChanged;

        var director = GetComponent<LobbyTrainingDirector>();
        if (director != null && director.IsSpawned)
            HandleStepChanged();

        if (!IsServer)
            return;

        _model = new LobbyBriefingModel();
        _director = director;
        if (_director == null)
        {
            Debug.LogError($"{Tag} 같은 오브젝트에 LobbyTrainingDirector 가 없다 — 브리핑이 시작되지 않는다.", this);
            return;
        }

        if (textTable == null)
            Debug.LogWarning($"{Tag} TutorialGuideTexts 가 비었다 — 브리핑 대사마다 {LobbyBriefingRules.DefaultLineSeconds}초로 잰다.", this);

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
            if (_model != null && _model.Abort())
                Debug.Log($"[SceneFlow] {Tag} 브리핑 중단 — 진행기 디스폰");
            ReleaseLocks("진행기 디스폰");
            _director = null;
            _model = null;
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

        if (!IsServer || _model == null || !_model.IsRunning)
            return;

        var completed = _model.Tick(Time.unscaledDeltaTime, _dueLines);
        foreach (var id in _dueLines)
            PlayLine(id);

        if (completed)
        {
            Debug.Log($"[SceneFlow] {Tag} 브리핑 끝 — {_model.TotalSeconds:0.0}초, 대사 {_model.SentCount}줄");
            ReleaseLocks("브리핑 완료");
            if (_director != null)
                _director.ServerReportBriefingCompleted(_runRevision);
            return;
        }

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
        if (_model == null || _director == null)
            return;

        var briefing = LobbyBriefingRules.IsBriefing(_director.CurrentView);
        if (briefing && _model.State == LobbyBriefingState.Idle)
        {
            BeginServer();
            return;
        }

        // 진행 중에 단계가 바뀌었다(회차 초기화 등 — 정상 완료는 Update 가 먼저 모델을 끝낸다) — 잠금만 풀고 끝낸다.
        if (!briefing && _model.Abort())
        {
            Debug.LogWarning($"{Tag} 브리핑 중단 — 진행기 {_director.DescribeStep()}로 바뀜, 조작 제한 해제.", this);
            ReleaseLocks("단계 이탈");
        }
    }

    private void BeginServer()
    {
        _runRevision = _director.Revision;
        _lineBuffer.Clear();
        foreach (var id in LobbyBriefingRules.LineIds)
        {
            var seconds = textTable != null && textTable.TryGetRow(id, out var row) ? row.DurationSeconds : 0f;
            _lineBuffer.Add(new LobbyBriefingLine(id, seconds));
        }

        if (!_model.Start(_lineBuffer, leadSeconds, tailSeconds))
            return;

        // 명세 §4 — 브리핑이 시작되면 재생 중·대기 중인 기존 무전(TRAIN_END 등)을 끈다.
        var radio = LobbyRadio.Active;
        if (radio != null)
            radio.ServerClear();
        else
            Debug.LogWarning($"{Tag} LobbyRadio 가 없다 — 기존 무전 종료·브리핑 대사 없이 시간만 잰다.", this);

        _lockTimer = LockTickInterval;
        LockConnected();
        Debug.Log($"[SceneFlow] {Tag} 브리핑 시작 — {_model.TotalSeconds:0.0}초(지연 {leadSeconds}·대사 {_model.Lines.Count}줄·여유 {tailSeconds}), 잠금 {_locks.Count}명");
    }

    private void PlayLine(string id)
    {
        var radio = LobbyRadio.Active;
        if (radio != null)
            radio.ServerPlay(id);
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

    // ---------------- 전 피어(표시) ----------------

    private void HandleStepChanged()
    {
        var director = LobbyTrainingDirector.Active;
        var step = director != null ? director.CurrentView : default;
        ApplyObjective(LobbyBriefingRules.ObjectiveFor(step));

        _wantCamera = cameraFocus != null && LobbyBriefingRules.IsBriefing(step);
        if (_wantCamera)
            HoldCamera();
        else
            ReleaseCamera();
    }

    // 목표 패널은 이 컴포넌트가 띄운 것만 지운다(로비의 다른 목표가 생겨도 덮지 않게).
    private void ApplyObjective(string id)
    {
        if (id == _shownObjective)
            return;

        var ui = TutorialGuideUI.Instance;
        if (ui == null)
        {
            if (id != null && !_warnedNoUi)
            {
                _warnedNoUi = true;
                Debug.LogWarning($"{Tag} 목표 {id} — 씬에 TutorialGuideUI 가 없어 띄우지 않는다(3.LobbyScene 배치 확인).", this);
            }

            _shownObjective = null;
            return;
        }

        if (id != null)
            ui.SetObjective(id);
        else if (_shownObjective != null)
            ui.SetObjective(null);
        _shownObjective = id;
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
        if (_shownObjective != null)
        {
            TutorialGuideUI.Instance?.SetObjective(null);
            _shownObjective = null;
        }
    }
}
