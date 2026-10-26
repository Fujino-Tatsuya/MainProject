using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로비 무전(자막) 재생기(할 일 86 T6b, PLAN §3.6, 명세 v0.2 §4·§14) — <see cref="LobbyTrainingDirector"/> 와 같은 오브젝트(씬 상주 NetworkObject)에 붙는다.
/// 규칙은 <see cref="LobbyRadioModel"/>(서버)·<see cref="LobbyRadioReceiver"/>(클라)·<see cref="LobbyRadioSchedule"/>(단계 → 대사 표), 여기는 구독·RPC·UI 호출만 한다.
/// <list type="bullet">
/// <item>서버만 재생을 정한다: <see cref="LobbyTrainingDirector.ServerFlowChanged"/> 구독 → 단계 진입 대사를 ClientRpc 로 전 클라에 id 1회 →
/// 각 클라 <see cref="TutorialGuideUI.ShowSubtitle"/>. 파티 공통 1회, 클라 로컬 판정 없음.</item>
/// <item>큐: 일반 무전은 <see cref="TutorialSubtitleQueue"/> 로 하나씩 순서대로. 단계가 바뀌면 서버가 단계 번호를 올려 보내고 클라는 대기 줄만 버린다
/// (지금 뜬 줄은 끝까지). 대사는 진행을 막지 않는다 — 진행기는 대사 종료를 기다리지 않는다.</item>
/// <item>늦게 붙은 클라·재접속은 지난 대사를 받지 않는다(복제 상태 없이 RPC 만).</item>
/// <item>단계 안 사건(간파·벽 첫 성공 등)은 <see cref="ServerPlay"/>, 브리핑·강제 연출 시작 때 기존 무전 종료는 <see cref="ServerClear"/>(T6c).</item>
/// </list>
/// 상세: Docs/tech/lobby-training.md "로비 무전".
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyRadio : NetworkBehaviour
{
    private const string Tag = "[LobbyTraining/Radio]";

    // 서버
    private LobbyTrainingDirector _director;
    private LobbyRadioModel _model;
    private readonly List<string> _lines = new List<string>();

    // 클라
    private readonly LobbyRadioReceiver _receiver = new LobbyRadioReceiver();
    private bool _warnedNoUi;

    /// <summary>스폰된 무전(없으면 null).</summary>
    public static LobbyRadio Active { get; private set; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Active = this;

        if (!IsServer)
            return;

        _model = new LobbyRadioModel();
        _director = GetComponent<LobbyTrainingDirector>();
        if (_director == null)
        {
            Debug.LogError($"{Tag} 같은 오브젝트에 LobbyTrainingDirector 가 없다 — 단계 진입 무전이 나오지 않는다(ServerPlay 는 동작).", this);
            return;
        }

        _director.ServerFlowChanged += HandleFlowChangedServer;
        // 지금 상태를 기준으로만 잡는다(대사 없음) — 진행기가 먼저 스폰돼 첫 알림을 놓쳤어도 지난 대사를 틀지 않는다.
        HandleFlowChangedServer();
    }

    public override void OnNetworkDespawn()
    {
        if (_director != null)
            _director.ServerFlowChanged -= HandleFlowChangedServer;
        _director = null;
        _model = null;

        if (Active == this)
            Active = null;
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Active == this)
            Active = null;
        base.OnDestroy();
    }

    /// <summary>
    /// [서버] 대사 한 줄을 전 클라에 1회 보낸다(일반 무전 큐 — 지금 뜬 줄 뒤에 이어서). 단계 진입 대사와 같은 단계 번호를 달아
    /// 다음 단계가 시작되면 아직 안 뜬 줄은 버려진다. <paramref name="oncePerRound"/> 면 이번 회차에 이미 튼 id 는 무시하고 false.
    /// 서버가 아니거나 스폰 전·빈 id 도 false.
    /// </summary>
    public bool ServerPlay(string id, bool oncePerRound = true)
    {
        if (!IsServer || _model == null)
        {
            Debug.LogWarning($"{Tag} {id} 무시 — 서버에서 스폰된 뒤에만 쓸 수 있다(서버:{IsServer} 스폰:{IsSpawned}).", this);
            return false;
        }

        if (!_model.TryPlay(id, oncePerRound))
            return false;

        SendLine(id);
        return true;
    }

    /// <summary>[서버] 이번 회차에 이 id 를 이미 틀었는가(단계 진입 대사 포함).</summary>
    public bool ServerHasPlayed(string id) => _model != null && _model.HasPlayed(id);

    /// <summary>
    /// [서버] 지금 뜬 무전과 대기 줄을 전 클라에서 즉시 끈다 — 브리핑·강제 연출 시작 때(명세 §4, T6c). 목표 패널·대기 줄·강조는 그대로.
    /// </summary>
    public void ServerClear()
    {
        if (!IsServer || !IsSpawned)
        {
            Debug.LogWarning($"{Tag} 무전 종료 무시 — 서버에서 스폰된 뒤에만 쓸 수 있다.", this);
            return;
        }

        Debug.Log($"[SceneFlow] {Tag} 무전 전체 종료(현재 줄 + 대기 줄)");
        ClearClientRpc();
    }

    private void HandleFlowChangedServer()
    {
        if (_model == null || _director == null)
            return;

        if (_model.OnFlowChanged(_director.Stage, _director.Phase, _lines))
            DiscardPendingClientRpc(_model.StageSerial);

        foreach (var id in _lines)
            SendLine(id);
    }

    private void SendLine(string id)
    {
        Debug.Log($"[SceneFlow] {Tag} 무전 {id} (단계 번호 {_model.StageSerial})");
        PlayClientRpc(id, _model.StageSerial);
    }

    [ClientRpc]
    private void PlayClientRpc(string id, int stageSerial)
    {
        if (!_receiver.AcceptLine(stageSerial, out var discardPending))
            return;

        var ui = ResolveUi(id);
        if (ui == null)
            return;

        if (discardPending)
            ui.ClearPendingSubtitles();
        ui.ShowSubtitle(id);
    }

    [ClientRpc]
    private void DiscardPendingClientRpc(int stageSerial)
    {
        if (!_receiver.OnStageAdvanced(stageSerial))
            return;

        TutorialGuideUI.Instance?.ClearPendingSubtitles();
    }

    [ClientRpc]
    private void ClearClientRpc()
    {
        TutorialGuideUI.Instance?.ClearSubtitles();
    }

    private TutorialGuideUI ResolveUi(string id)
    {
        var ui = TutorialGuideUI.Instance;
        if (ui == null && !_warnedNoUi)
        {
            _warnedNoUi = true;
            Debug.LogWarning($"{Tag} 무전 {id} — 씬에 TutorialGuideUI 가 없어 띄우지 않는다(3.LobbyScene 배치 확인).", this);
        }

        return ui;
    }
}
