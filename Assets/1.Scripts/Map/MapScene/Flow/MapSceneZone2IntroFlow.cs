using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 2구역 진입 전 적 소개 연출 — <b>서버 진행</b>(PLAN-main-radio-zone1-4 §4.2 · 명세 §4.2). 표시는 모든 피어의 <see cref="MapSceneZone2IntroPresentation"/>.
/// 4구역 소개(<see cref="MapSceneZone4IntroFlow"/>)와 같은 틀 — 잠금·스킵 표·집결 판정은 <see cref="MapSceneAreaIntroServer"/> 공용.
///
/// 흐름:
///  · 맵씬 활성 순간 G2(존2 입구)를 보류한다 — 복도1 특성 선택이 끝나도 바로 열리지 않는다.
///  · 열 조건이 찼고(전원 완료·60초 교착 방지) <b>살아 있는</b> 접속자 전원이 복도1 안이면 연출 시작 —
///    열린 특성 창 자동 획득 · 연출 잠금(입력·무적) · 존2 적 무리 중심·반경을 재서 복제 상태에 넣는다 · 복귀 시각 = 샷 합.
///  · 스킵 = 접속자 전원(관전자 포함) 동의 → 지금 자리에서 바로 복귀(<see cref="MapSceneZone2IntroData.SkipReturnSeconds"/>).
///  · 복귀 블렌드가 끝나는 순간 잠금 해제 → G2 보류 해제(열림) → 다음 Update 에 존2 <b>전체 기상</b>(첫 전투 — 이전의 첫 진입 4초 지연 없음).
///    G2 가 어떤 길로 열리든(QA 구간 스킵·디버그 강제) 열린 순간 기상한다 — 연출을 켠 판에서는 <see cref="MapSceneZoneFlow"/> 가 존2 를 깨우지 않는다.
/// 붙는 곳: 서버에서 디렉터가 맵씬을 켠 순간 디렉터 GameObject 에 런타임으로 붙는다(씬 무수정).
/// </summary>
[DisallowMultipleComponent]
public sealed class MapSceneZone2IntroFlow : MonoBehaviour
{
    private MapSceneDirector _director;
    private MapSceneZone2IntroData _data;
    private bool _subscribed;
    private bool _held;                         // 이 흐름이 G2 를 보류했다(연출 켜짐)
    private bool _wakePending;
    private bool _woken;
    private int _sequence;
    private readonly MapSceneAreaIntroServer _intro = new MapSceneAreaIntroServer();
    private readonly List<MonsterBase> _monsters = new List<MonsterBase>();
    private readonly List<Vector3> _positions = new List<Vector3>();
    private string _lastWaitLog;

    // ─── 부착(서버) ──────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallBootstrap()
    {
        MapSceneDirector.StateChanged -= EnsureOnServer;
        MapSceneDirector.StateChanged += EnsureOnServer;
    }

    private static void EnsureOnServer()
    {
        MapSceneDirector d = MapSceneDirector.Instance;
        if (d == null || !d.IsSpawned || !d.IsServer || !d.IsTutorialActive) return;
        if (d.GetComponent<MapSceneZone2IntroFlow>() != null) return;
        d.gameObject.AddComponent<MapSceneZone2IntroFlow>();
    }

    /// <summary>[서버] 이 판에 2구역 소개 연출이 켜져 있다 — 존2 기상은 이 흐름 몫(<see cref="MapSceneZoneFlow"/> 는 깨우지 않는다).</summary>
    public static bool OwnsZone2Wake => MapSceneZone2IntroData.Load().IntroEnabled;

    // ─── 생명주기 ────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _director = GetComponent<MapSceneDirector>();
        NetworkManager nm = NetworkManager.Singleton;
        if (_director == null || nm == null || !nm.IsServer)
        {
            enabled = false;
            return;
        }

        _data = MapSceneZone2IntroData.Load();
        _sequence = _director.Zone2Intro.Sequence;
        if (!_subscribed)
        {
            MapSceneDirector.Zone2IntroSkipVoted += HandleSkipVote;
            MapSceneDirector.GateOpened += HandleGateOpened;
            _subscribed = true;
        }

        if (!_data.IntroEnabled) return;
        if (_director.IsGateOpen(MapSceneGate.G2_Zone2Entry)) _wakePending = true;   // 붙기 전에 이미 열렸다(정상 흐름에서는 없음)
        else if (_director.Zone2Intro.Phase == MapSceneDirector.IntroPhase.None)
            _held = _director.HoldGateServer(MapSceneGate.G2_Zone2Entry);
    }

    private void OnDisable()
    {
        if (_subscribed)
        {
            MapSceneDirector.Zone2IntroSkipVoted -= HandleSkipVote;
            MapSceneDirector.GateOpened -= HandleGateOpened;
            _subscribed = false;
        }
        _intro.Unlock();
    }

    private void Update()
    {
        if (_director == null || !_director.IsSpawned || !_director.IsTutorialActive) return;

        if (_wakePending && !_woken)
        {
            _wakePending = false;
            _woken = true;
            int n = _director.WakeZoneServer(MapSceneArea.Zone2);
            Edit.Log($"[Zone2Intro] G2 열림 — 존2 기상 {n}마리(첫 전투).", this);
        }

        MapSceneDirector.Zone2IntroState state = _director.Zone2Intro;
        switch (state.Phase)
        {
            case MapSceneDirector.IntroPhase.None:
                TickWaiting();
                break;
            case MapSceneDirector.IntroPhase.Running:
                TickRunning(state);
                break;
        }
    }

    // ─── 대기 → 시작 ─────────────────────────────────────────────────────

    private void TickWaiting()
    {
        if (!_held || !_director.IsGateHeldServer(MapSceneGate.G2_Zone2Entry)) return;

        if (IsQaSkipRunning())
        {
            _held = false;
            bool opened = _director.ReleaseHeldGateServer(MapSceneGate.G2_Zone2Entry);
            Edit.Log($"[Zone2Intro] 구간 스킵이 G2 를 지난다 — 연출 없이 G2 보류 해제({(opened ? "열림" : "조건 대기")}).", this);
            return;
        }

        if (!_director.IsGateReadyHeldServer(MapSceneGate.G2_Zone2Entry)) return;

        if (!MapSceneAreaIntroServer.AllAliveIn(_director, MapSceneArea.Corridor1, out int inside, out int alive))
        {
            string wait = $"{inside}/{alive}";
            if (wait != _lastWaitLog)
            {
                _lastWaitLog = wait;
                Edit.Log($"[Zone2Intro] 복도1 특성 선택 끝 — 살아 있는 전원이 복도1 에 모이길 기다린다({wait}).", this);
            }
            return;
        }

        StartIntro();
    }

    private void StartIntro()
    {
        // 60초 교착 방지로 준비된 경우 아직 특성 창이 열린 사람이 있다 — 연출 위에 창이 남지 않게 먼저 닫는다(자동 획득).
        TraitDirector traits = TraitDirector.Instance;
        int auto = traits != null ? traits.AutoChooseAllPendingServer() : 0;

        MeasureEnemies(out Vector3 focus, out float radius, out int count);

        _intro.Lock();
        _intro.ClearVotes();
        _intro.RefreshVoters();
        _sequence++;
        double now = NetworkManager.Singleton.ServerTime.Time;
        _director.SetZone2IntroServer(new MapSceneDirector.Zone2IntroState
        {
            Phase = MapSceneDirector.IntroPhase.Running,
            Sequence = _sequence,
            StartServerTime = now,
            ReturnAt = _data.IntroSeconds,
            ReturnSeconds = _data.ReturnBlendSeconds,
            Votes = 0,
            Voters = (byte)Mathf.Min(255, _intro.Voters.Count),
            Focus = focus,
            Radius = radius,
        });
        _lastWaitLog = null;
        Edit.Log($"[Zone2Intro] 적 소개 연출 시작(회차 {_sequence}) — 샷 {_data.IntroSeconds:0.##}초 + 복귀 {_data.ReturnBlendSeconds:0.##}초 · " +
                 $"적 {count}마리 중심 {focus} 반경 {radius:0.#}m · 잠금 {_intro.LockedCount}명 · 투표자 {_intro.Voters.Count}명" +
                 $"{(auto > 0 ? $" · 특성 자동 획득 {auto}명" : "")}.", this);
    }

    // 존2 장부의 살아 있는 몬스터 위치로 구도 — 없으면 존2 구간 중심(높이는 살아 있는 플레이어 평균, 그것도 없으면 0).
    private void MeasureEnemies(out Vector3 focus, out float radius, out int count)
    {
        _director.GetZoneMonstersServer(MapSceneArea.Zone2, MapSceneMonsterFilter.All, _monsters);
        _positions.Clear();
        foreach (MonsterBase m in _monsters)
            if (m != null) _positions.Add(m.transform.position);
        count = _positions.Count;
        if (MapSceneZone2IntroRules.TryFrame(_positions, 1f, out focus, out radius)) return;

        radius = _data.FallbackRadius;
        focus = Vector3.zero;
        if (_director.TryGetAreaRectServer(MapSceneArea.Zone2, out MapSceneRect rect))
            focus = new Vector3(rect.CenterX, PlayerHeight(), rect.CenterZ);
        Edit.LogWarning($"[Zone2Intro] 존2 장부에 살아 있는 몬스터가 없다 — 존2 구간 중심 {focus} 를 비춘다.", this);
    }

    private static float PlayerHeight()
    {
        NetworkManager nm = NetworkManager.Singleton;
        float sum = 0f;
        int n = 0;
        if (nm != null)
            foreach (KeyValuePair<ulong, NetworkClient> pair in nm.ConnectedClients)
            {
                if (pair.Value.PlayerObject == null) continue;
                sum += pair.Value.PlayerObject.transform.position.y;
                n++;
            }
        return n > 0 ? sum / n : 0f;
    }

    // ─── 진행 ───────────────────────────────────────────────────────────

    private void TickRunning(MapSceneDirector.Zone2IntroState state)
    {
        double now = NetworkManager.Singleton.ServerTime.Time;
        float elapsed = state.Elapsed(now);

        if (!state.IsSkipClosed(now))
        {
            bool pruned = _intro.RefreshAndPrune();
            if (_intro.IsUnanimous)
            {
                Skip(state, elapsed);
                return;
            }
            if (pruned || state.Voters != _intro.Voters.Count)
            {
                state.Votes = (byte)_intro.VoteCount;
                state.Voters = (byte)Mathf.Min(255, _intro.Voters.Count);
                _director.SetZone2IntroServer(state);
            }
        }

        if (state.ReturnAt >= 0f && elapsed >= state.ReturnAt + state.ReturnSeconds)
            Finish(state, "복귀 완료");
    }

    private void HandleSkipVote(ulong sender, int sequence)
    {
        if (_director == null) return;
        MapSceneDirector.Zone2IntroState state = _director.Zone2Intro;
        if (state.Phase != MapSceneDirector.IntroPhase.Running || sequence != state.Sequence) return;
        if (state.IsSkipClosed(NetworkManager.Singleton.ServerTime.Time)) return;

        if (!_intro.TryVote(sender)) return;
        state.Votes = (byte)_intro.VoteCount;
        state.Voters = (byte)Mathf.Min(255, _intro.Voters.Count);
        _director.SetZone2IntroServer(state);
        Edit.Log($"[Zone2Intro] 스킵 동의 — client {sender} ({_intro.VoteCount}/{_intro.Voters.Count}).", this);
    }

    // 전원 동의 — 남은 샷·대사는 끊고 지금 자리에서 바로 복귀한다(4구역처럼 따로 보여 줄 샷이 없다).
    private void Skip(MapSceneDirector.Zone2IntroState state, float elapsed)
    {
        _intro.ClearVotes();
        state.Skipped = true;
        state.ReturnAt = Mathf.Max(0f, elapsed);
        state.ReturnSeconds = _data.SkipReturnSeconds;
        state.Votes = 0;
        _director.SetZone2IntroServer(state);
        Edit.Log($"[Zone2Intro] 적 소개 스킵 — 전원 동의, {elapsed:0.##}초 지점 → 복귀 {_data.SkipReturnSeconds:0.##}초.", this);
    }

    private void Finish(MapSceneDirector.Zone2IntroState state, string why)
    {
        _intro.Unlock();
        state.Phase = MapSceneDirector.IntroPhase.Done;
        state.Votes = 0;
        _director.SetZone2IntroServer(state);
        _held = false;

        bool opened = _director.IsGateOpen(MapSceneGate.G2_Zone2Entry) || _director.ReleaseHeldGateServer(MapSceneGate.G2_Zone2Entry);
        if (opened) Edit.Log($"[Zone2Intro] 적 소개 끝({why}) — G2 열림.", this);
        else Edit.LogWarning($"[Zone2Intro] 적 소개 끝({why})인데 G2 가 안 열렸다 — 게이트 순서·보류 상태 확인.", this);
    }

    // G2 열림 — 어떤 길이든 존2 기상(다음 Update — 디렉터 Commit 안에서 다시 손대지 않게). 연출 중 다른 길로 열렸으면 연출을 정리한다.
    private void HandleGateOpened(MapSceneGate gate, MapSceneGateOpenReason reason)
    {
        if (gate != MapSceneGate.G2_Zone2Entry || _director == null || _data == null || !_data.IntroEnabled) return;
        _held = false;
        _wakePending = true;

        MapSceneDirector.Zone2IntroState state = _director.Zone2Intro;
        if (state.Phase != MapSceneDirector.IntroPhase.Running) return;
        float elapsed = state.Elapsed(NetworkManager.Singleton.ServerTime.Time);
        state.ReturnAt = state.ReturnAt >= 0f ? Mathf.Min(state.ReturnAt, elapsed) : elapsed;
        Edit.LogWarning($"[Zone2Intro] 연출 중 G2 가 다른 길로 열렸다({reason}) — 연출을 정리한다.", this);
        Finish(state, "G2 외부 개방");
    }

#if UNITY_EDITOR
    /// <summary>[에디터 디버그] 접속자 전원 스킵 표.</summary>
    public void DebugVoteAllServer()
    {
        if (_director == null) return;
        int sequence = _director.Zone2Intro.Sequence;
        _intro.RefreshVoters();
        foreach (ulong id in new List<ulong>(_intro.Voters)) HandleSkipVote(id, sequence);
    }
#endif

    private bool IsQaSkipRunning()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        MapSceneSkipController skip = GetComponent<MapSceneSkipController>();
        return skip != null && skip.IsSkippingThrough(MapSceneGate.G2_Zone2Entry);
#else
        return false;
#endif
    }
}
