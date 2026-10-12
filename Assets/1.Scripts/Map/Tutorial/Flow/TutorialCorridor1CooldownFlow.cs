using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 복도1 첫 진입 쿨타임 초기화(v8 C1-0) — 존1 클리어(G1) 뒤 <b>각 플레이어가 복도1 에 처음 들어갈 때</b> 그 사람의
/// Q·E·R·우클릭(기본·대체 스킬 전부)·대시 쿨을 0 으로 + 자막 <c>TUT_C1_COOLDOWN_RESET</c>(그 사람). 플레이어별 1회, 왕복·부활 재진입 반복 없음.
/// 진행 중 행동(실행 중 스킬·어쌔신 변신·E 강화 대기)이 있으면 끝날 때 한 번 더(초기화 예약). 자원·체력·패시브 주기는 건드리지 않는다.
/// 원본: Docs/design/tutorial-scenario.md §3.2 · PLAN-tutorial.md V2 · Docs/tech/tutorial-corridors-zone2.md.
/// 규칙은 순수 <see cref="TutorialCooldownResetModel"/>, 실행은 <see cref="TutorialCooldownReset"/>(쿨 장부 공개 API 만).
///
/// 붙는 곳: 다른 존 흐름과 같다(<see cref="TutorialDirector.StateChanged"/> → 디렉터 GameObject 런타임 부착, 모든 피어).
/// 서버: 구간 판정(<see cref="TutorialDirector.GetPlayerAreaServer"/>) → 초기화 → 개인 체크 <see cref="TutorialCheck.Corridor1CooldownReset"/>(복제 기록).
/// 오너: 그 복제 기록이 켜지는 것을 보면 대시 예측 충전을 가득 채우고 자막을 띄운다(서버 대시 장부와 짝 — 추락 복구의 ResetChargeToOne 과 같은 방식).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialCorridor1CooldownFlow : MonoBehaviour
{
    public const string CooldownResetId = "TUT_C1_COOLDOWN_RESET";
    private const float PollInterval = 0.1f;

    private TutorialDirector _director;
    private NetworkManager _nm;
    private bool _isServer;
    private float _pollTimer;
    private readonly TutorialCooldownResetModel _model = new TutorialCooldownResetModel();

    // 오너 — 복제 기록을 처음 볼 때 이미 켜져 있었으면(늦게 그린 클라) 자막·예측 초기화를 하지 않는다.
    private bool _localObserved;
    private bool _localHad;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallBootstrap()
    {
        TutorialDirector.StateChanged -= Ensure;
        TutorialDirector.StateChanged += Ensure;
    }

    private static void Ensure()
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsTutorialActive) return;
        if (d.GetComponent<TutorialCorridor1CooldownFlow>() != null) return;
        d.gameObject.AddComponent<TutorialCorridor1CooldownFlow>();
    }

    private void OnEnable()
    {
        _director = GetComponent<TutorialDirector>();
        _nm = NetworkManager.Singleton;
        if (_director == null || _nm == null || !_nm.IsListening)
        {
            enabled = false;
            return;
        }

        _isServer = _nm.IsServer;
        TutorialDirector.StateChanged += ObserveLocal;
        if (_isServer) _nm.OnClientDisconnectCallback += _model.Forget;
        ObserveLocal();
    }

    private void OnDisable()
    {
        TutorialDirector.StateChanged -= ObserveLocal;
        if (_nm != null) _nm.OnClientDisconnectCallback -= _model.Forget;
    }

    private void Update()
    {
        if (!_isServer || _director == null || !_director.IsTutorialActive || _director.IsFinished) return;
        _pollTimer -= Time.unscaledDeltaTime;
        if (_pollTimer > 0f) return;
        _pollTimer = PollInterval;

        bool g1Open = _director.IsGateOpen(TutorialGate.G1_Corridor1Entry);
        foreach (NetworkClient client in _nm.ConnectedClientsList)
        {
            Player player = client.PlayerObject != null ? client.PlayerObject.GetComponent<Player>() : null;
            if (player == null) continue;

            ulong id = client.ClientId;
            bool busy = TutorialCooldownReset.IsBusy(player);
            bool recorded = _director.TryGetPlayerProgress(id, out TutorialPlayerProgress p) && p.Has(TutorialCheck.Corridor1CooldownReset);
            bool inCorridor1 = _director.GetPlayerAreaServer(id) == TutorialArea.Corridor1;

            if (_model.TryBegin(id, g1Open, inCorridor1, busy, recorded))
            {
                int n = TutorialCooldownReset.ResetSkillsServer(player, TutorialCooldownResetScope.Corridor1Includes);
                player.GetComponent<PlayerDashController>()?.ServerResetChargeToFull();
                _director.ReportCheckServer(id, TutorialCheck.Corridor1CooldownReset);
                Edit.Log($"[Tutorial] 복도1 첫 진입 쿨타임 초기화 — client {id} 스킬 {n}개 + 대시" +
                         (busy ? " · 진행 중 행동이 끝나면 한 번 더(예약)" : "") + ".", this);
            }
            else if (_model.TickReservation(id, busy))
            {
                int n = TutorialCooldownReset.ResetSkillsServer(player, TutorialCooldownResetScope.Corridor1Includes);
                Edit.Log($"[Tutorial] 복도1 쿨타임 초기화 예약 실행 — client {id} 진행 중 행동 종료, 스킬 {n}개.", this);
            }
        }
    }

    // 오너 — 내 복제 기록(Corridor1CooldownReset)이 꺼짐 → 켜짐으로 바뀌는 것을 본 순간 한 번.
    private void ObserveLocal()
    {
        if (_director == null || _nm == null || !_director.IsTutorialActive) return;
        if (!_director.TryGetPlayerProgress(_nm.LocalClientId, out TutorialPlayerProgress p)) return;

        bool has = p.Has(TutorialCheck.Corridor1CooldownReset);
        bool fresh = _localObserved && !_localHad && has;
        _localObserved = true;
        _localHad = has;
        if (!fresh) return;

        Player local = Player.LocalPlayer;
        if (local != null) local.GetComponent<PlayerDashController>()?.OwnerResetChargeToFull();
        TutorialGuideUI.Instance?.ShowSubtitle(CooldownResetId);
    }
}
