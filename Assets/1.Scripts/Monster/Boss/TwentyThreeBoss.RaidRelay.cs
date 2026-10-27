using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 23호 레이드 새 페이즈 2종 — <b>간파 릴레이</b>(<see cref="BossPhaseSequence.RaidInterruptRelay"/>) ·
/// <b>연쇄 간파</b>(<see cref="BossPhaseSequence.RaidChainCounter"/>). 계획: PLAN-boss-raid-relay.md(경석 10-20).
///
/// 송전기 흐름을 그대로 탄다 — 예약 소비(SelectAttackSlot) → ChargeSequence 점프 → 착지 → <c>StartChargingInPlace</c> 에서
/// 이 파일로 넘어오고, 이후 <c>ChargeWait</c> 단계 안에서 자체 단계(<see cref="GimmickStep"/>)로 돈다(④ 자폭과 같은 방식).
/// 서버 권한. 클라에는 기존 RPC(애니·예고 띠·간파 창·자막·장판)만 나간다.
///
/// 🔴 끝나는 길은 셋 — 성공·실패(<see cref="EndRaidGimmick"/>) · 중단(AbortAttackChain → 같은 함수). 어느 길이든
///    돌진 이동·히트 윈도우·간파 창·예고 띠·릴레이 잠금을 여기서 걷는다.
/// </summary>
public partial class TwentyThreeBoss
{
    #region 레이드 새 페이즈 — 간파 릴레이 · 연쇄 간파
    // ─── 은희 연동(handoff-raid-relay-lock.md) — 전 피어에서 발생한다(서버 확정 후 RPC) ───
    /// <summary>이 사람의 간파가 릴레이 성공으로 인정됐다(돌진 시도당 1명) — 우클릭 남은 쿨 반환용.</summary>
    public static event Action<ulong> RelayCounterAccepted;
    /// <summary>이 사람의 우클릭이 이번 라운드 잠김(true) / 풀림(false).</summary>
    public static event Action<ulong, bool> RelayLockChanged;
    /// <summary>릴레이 종료(성공·실패·중단 공통) — 남은 잠금 전부 해제.</summary>
    public static event Action RelayEnded;

    enum RaidGimmick { None, Relay, Chain, PushNorth }

    enum GimmickStep
    {
        None,
        // 릴레이
        Gather, Ready, Prepare, Push, Reposition, FailDash,
        // 연쇄 간파
        ChainIntro, ChainMark, ChainWindow, ChainDash, ChainStun, ChainInterval,
        // 북진 압박
        PnCharge, PnStun, PnPush, PnAfterFail,
    }

    RaidGimmick _raidGimmick;
    GimmickStep _gStep;
    float _gStepTimer;
    bool _gDashing;
    Vector3 _gDashDir;
    ulong _raidCounterClientId = ulong.MaxValue;   // ReceiveAttack 이 남기고 EnterCounterSuccess 가 읽는다(같은 호출 안)

    // ── 릴레이 ──
    readonly BossRelayRoster _relayRoster = new BossRelayRoster();
    readonly HashSet<ulong> _relayLocksSent = new HashSet<ulong>();
    readonly List<ulong> _relayScratch = new List<ulong>();
    Vector3 _relaySouth;        // 6시 = 화면 아래(수평 단위 벡터). 방 축과 무관 — 대각선 카메라에서 방은 마름모다
    Vector3 _relayCenter;       // 방 중앙(그리드 중심 칸) — 송전탑 4개 안쪽, 보스 착지점
    Vector3 _relayGoalPoint;    // 12시 목표선 위의 한 점(마름모 위 꼭짓점 쪽)
    float _relaySouthReach;     // 중앙에서 6시 끝까지(m)
    ulong _relaySafeMask, _relaySideMask;
    float _relayHalfWidth;
    float _relayDeadline;
    float _relayNextSide;
    bool _relayTimedOut;
    bool _relayAttemptTaken;    // 이번 돌진 시도에 이미 성공자가 있다(1명만)
    bool _relayRoundsStarted;
    int _relayPushes;
    Vector3 _relayPushFrom, _relayPushTo;

    // ── 연쇄 간파 ──
    List<ulong> _chainOrder = new List<ulong>();
    int _chainPos, _chainRound, _chainTotal, _chainSuccesses;
    Player _chainTarget;

    bool RaidGimmickActive => _raidGimmick != RaidGimmick.None;

    /// <summary>릴레이 중 보스는 피해를 받지 않는다(기획 기믹1 §3-1) — 간파만 처리.</summary>
    bool RaidGimmickDamageImmune => _raidGimmick == RaidGimmick.Relay || _raidGimmick == RaidGimmick.PushNorth;

    /// <summary>[서버] 예약 소비 — 해당 시퀀스면 기믹을 걸고 true.</summary>
    bool TryArmRaidGimmick(BossPhaseSequence pending)
    {
        if (pending == BossPhaseSequence.RaidInterruptRelay) _raidGimmick = RaidGimmick.Relay;
        else if (pending == BossPhaseSequence.RaidChainCounter) _raidGimmick = RaidGimmick.Chain;
        else if (pending == BossPhaseSequence.RaidPushNorth) _raidGimmick = RaidGimmick.PushNorth;
        else return false;
        _gStep = GimmickStep.None;
        return true;
    }

    // ─── 공통 ──────────────────────────────────────────────────────────
    void StartRaidGimmickInPlace()
    {
        BossAttackEntry e = _currentEntry;
        if (e != null && !string.IsNullOrEmpty(e.animatorStateName))
            CrossFadeStateClientRpc(e.animatorStateName);   // 차징 자세(기존 클립)

        EnterPhase(BossAttackPhase.ChargeWait, 9999f);       // 끝은 자체 단계가 정한다
        if (_raidGimmick == RaidGimmick.Relay) StartRelay();
        else if (_raidGimmick == RaidGimmick.PushNorth) StartPushNorth();
        else StartChain();
    }

    void TickRaidGimmick(float dt)
    {
        // 데드락 안전망(_stateTimer)은 ChargeSequence 행 기준이라 60초 릴레이보다 짧다 — 기믹이 도는 동안 붙잡는다.
        _stateTimer = Mathf.Max(_stateTimer, 10f);
        _attackPhaseTimer = 9999f;
        _gStepTimer -= dt;

        if (_raidGimmick == RaidGimmick.Relay) TickRelay();
        else if (_raidGimmick == RaidGimmick.Chain) TickChain();
        else if (_raidGimmick == RaidGimmick.PushNorth) TickPushNorth();
    }

    void SetStep(GimmickStep step, float duration)
    {
        _gStep = step;
        _gStepTimer = Mathf.Max(0f, duration);
    }

    void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    /// <summary>[서버] ReceiveAttack 의 간파 조건 — 기믹 중이면 기믹 규칙으로 거른다(기믹 밖이면 true).</summary>
    bool RaidGimmickAcceptsCounter(ulong clientId)
    {
        if (!RaidGimmickActive) return true;

        if (_raidGimmick == RaidGimmick.Relay)
        {
            if (_gStep != GimmickStep.Prepare || _relayAttemptTaken) return false;
            BossRelayRoster.Accept a = _relayRoster.CanAccept(clientId);
            if (a != BossRelayRoster.Accept.Accepted)
                Debug.Log($"[23호/릴레이] client {clientId} 간파 불인정 — {(a == BossRelayRoster.Accept.Locked ? "이번 라운드 잠김(동료 차례)" : "명단 밖")}", this);
            return a == BossRelayRoster.Accept.Accepted;
        }

        if (_raidGimmick == RaidGimmick.PushNorth)
            return _gStep == GimmickStep.PnStun && !_pnCounterTaken;   // 무력화 중 1회만

        bool ok = _gStep == GimmickStep.ChainWindow && _chainTarget != null && _chainTarget.OwnerClientId == clientId;
        if (!ok && _gStep == GimmickStep.ChainWindow)
            Debug.Log($"[23호/연쇄 간파] client {clientId} 간파 불인정 — 지목 대상 아님", this);
        return ok;
    }

    /// <summary>[서버] 간파 성공(EnterCounterSuccess 가 넘긴다). 일반 간파 그로기 대신 기믹 처리.</summary>
    void OnRaidGimmickCounter()
    {
        ulong id = _raidCounterClientId;
        SetCounterWindow(false);
        HideAttackConeClientRpc();
        StopCutClipLoopsClientRpc();   // 돌진 준비 클립을 끊는다 — 짝 StopEffect 가 안 와 루프(레이지 같은 이펙트)가 남지 않게
        if (_raidGimmick == RaidGimmick.PushNorth) ReleasePushNorthDownPose();   // 얼린 채면 피격 리액션이 안 넘어간다
        if (_boss != null && !string.IsNullOrEmpty(_boss.hitReactionState))
            CrossFadeStateClientRpc(_boss.hitReactionState);

        if (_raidGimmick == RaidGimmick.Relay) OnRelayCounter(id);
        else if (_raidGimmick == RaidGimmick.PushNorth) OnPushNorthCounter();
        else OnChainCounter();
    }

    /// <summary>
    /// [서버] 기믹 종료 — 성공·실패·중단 공통 정리. <paramref name="resume"/> = 보상 없이 일반 전투로(재개 지연 뒤).
    /// 멱등이다(AbortAttackChain 이 다시 불러도 아무 일 없음).
    /// </summary>
    void EndRaidGimmick(string why, bool resume)
    {
        if (!RaidGimmickActive) return;
        RaidGimmick g = _raidGimmick;
        _raidGimmick = RaidGimmick.None;
        _gStep = GimmickStep.None;

        StopGimmickDash();
        SetCounterWindow(false);
        if (IsSpawned) HideAttackConeClientRpc();
        if (IsSpawned) StopCutClipLoopsClientRpc();   // 돌진 클립을 도중에 끝내는 경우가 있다 — 루프 정리

        if (g == RaidGimmick.Relay)
        {
            _relayRoster.Clear();
            SyncRelayLocks();
            _relayRoundsStarted = false;
            if (IsSpawned) NotifyRelayEndedRpc();
        }
        _chainTarget = null;
        if (g == RaidGimmick.PushNorth)
        {
            CleanupPushNorthRound();
            ReleasePushNorthDownPose();
        }

        string gName = g == RaidGimmick.Relay ? "간파 릴레이" : g == RaidGimmick.PushNorth ? "북진 압박" : "연쇄 간파";
        FightLog($"{gName} 종료 — {why}");
        Debug.Log($"[23호/레이드] {gName} 종료 — {why}", this);

        if (!resume) return;
        if (data != null && !string.IsNullOrEmpty(data.locomotionState))
            CrossFadeStateClientRpc(data.locomotionState);
        // Recovery → FinishChain → 일반 전투. 재개 지연 = 문서 "2초 정비".
        EnterPhase(BossAttackPhase.Recovery, _boss != null ? _boss.relayResumeDelay : 2f);
    }

    // ─── 기믹 돌진(실패 돌진 · 지목 돌진) — 레이지처럼 고정 방향, 판정은 근접 히트 윈도우 ───
    void BeginGimmickDash(Vector3 dir, int damage, float maxDistance, GimmickStep step)
    {
        dir.y = 0f;
        _gDashDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
        FaceDirection(_gDashDir);

        BossAttackEntry dash = AttackEntryOf(BossAttackId.Dash);
        if (dash != null)
        {
            ApplyAttackProfile(dash);   // 돌진 히트박스 앵커
            int slot = FindSlot(BossAttackId.Dash);
            float resume = dash.hitEventFallbackNormalized > 0f ? dash.hitEventFallbackNormalized : dash.telegraphPoseNormalized;
            if (slot != NoAttack) ResumeAttackFromPoseClientRpc(slot, resume);   // 돌진 클립의 실제 돌진 구간부터
        }
        meleeAttack?.SetDamageSnapshot(Mathf.Max(0, Mathf.RoundToInt(damage * PhaseDamageMultiplier)));
        meleeAttack?.BeginHitWindow();   // 1인 1회
        StartDashTrailClientRpc();
        StartDashMove(_gDashDir, DashSpeedMul, maxDistance);
        _gDashing = true;

        float speed = Mathf.Max(0.1f, MoveSpeed * DashSpeedMul);
        SetStep(step, maxDistance / speed + 0.5f);   // 도착이 먼저 끝낸다 — 이건 안전망
    }

    /// <summary>돌진 한 틱. 끝났으면 정리하고 true.</summary>
    bool TickGimmickDash()
    {
        if (!_gDashing) return true;
        meleeAttack?.Hit();
        bool arrived = DashDestinationReached(_gDashDir);
        if (_gStepTimer > 0f && !arrived) return false;
        StopGimmickDash();
        return true;
    }

    void StopGimmickDash()
    {
        if (!_gDashing) return;
        _gDashing = false;
        meleeAttack?.EndHitWindow();
        if (IsSpawned) StopDashTrailClientRpc();
        EndDashMove();
    }

    static Player FindPlayerByClient(ulong clientId)
    {
        foreach (Player p in BossPatternTargets.AllPlayers())
            if (p != null && p.IsSpawned && p.OwnerClientId == clientId) return p;
        return null;
    }

    static bool IsClientAlive(ulong clientId) => BossPatternTargets.IsAlive(FindPlayerByClient(clientId));

    // ─── 간파 릴레이 ───────────────────────────────────────────────────
    /// <summary>
    /// 6시·12시 = **화면** 아래·위(경석 Play 10-20). 카메라가 대각선(요 −45°)이라 방은 화면에서 마름모로 보인다 —
    /// 그래서 방의 행·열 축을 고르지 않고(두 축이 동점이라 엉뚱한 벽이 골라졌다) 타일 중심을 화면 아래 방향으로 **투영**해 나눈다.
    /// · 시작 = 방 중앙(송전탑 4개 안쪽, 송전기 착지점 그대로)
    /// · 6시 안전 구역 = 화면 아래로 가장 먼 줄 relaySafeLines 개(마름모 아래 꼭짓점 쪽 삼각형)
    /// · 통로 = 중앙을 지나는 화면 세로선에서 relayCorridorHalfWidth 안쪽 띠 · 측면 장판 = 그 밖(안전 구역 제외)
    /// · 12시 목표 = 마름모 위 꼭짓점 쪽 끝에서 relayGoalInset 만큼 안쪽
    /// </summary>
    bool ResolveRelayGeometry()
    {
        if (!TryGetTileGrid(out BossTileGrid g)) return false;

        int n = BossTileGrid.Size, mid = n / 2;
        Camera cam = Camera.main;
        _relaySouth = cam != null
            ? BossChargeFacingPolicy.ScreenSouth(cam.transform.forward, cam.transform.up, BossChargeFacingPolicy.DefaultFallback)
            : BossChargeFacingPolicy.DefaultFallback;
        Vector3 right = Vector3.Cross(Vector3.up, _relaySouth);   // 화면 가로(부호 무관 — 통로는 대칭)
        _relayCenter = g.TileCenter(mid, mid);

        // 한 칸의 반이 화면 세로로 차지하는 길이 — 방 끝(꼭짓점)을 칸 중심 + 이만큼으로 잡는다.
        Vector3 rowDir = g.TileCenter(n - 1, mid) - g.TileCenter(0, mid); rowDir.y = 0f; rowDir.Normalize();
        Vector3 colDir = g.TileCenter(mid, n - 1) - g.TileCenter(mid, 0); colDir.y = 0f; colDir.Normalize();
        Vector2 ext = g.TileExtent;   // x = 열 방향 폭 · y = 행 방향 폭
        float halfAlong = 0.5f * (ext.x * Mathf.Abs(Vector3.Dot(colDir, _relaySouth)) + ext.y * Mathf.Abs(Vector3.Dot(rowDir, _relaySouth)));

        // 칸마다 화면 세로(along)·가로(lat) 좌표.
        float maxAlong = float.MinValue, minAlong = float.MaxValue;
        var along = new float[BossTileGrid.TileCount];
        var lat = new float[BossTileGrid.TileCount];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                Vector3 d = g.TileCenter(r, c) - _relayCenter; d.y = 0f;
                int i = BossTileGrid.Bit(r, c);
                along[i] = Vector3.Dot(d, _relaySouth);
                lat[i] = Vector3.Dot(d, right);
                maxAlong = Mathf.Max(maxAlong, along[i]);
                minAlong = Mathf.Min(minAlong, along[i]);
            }

        // 안전 구역 = 화면 아래로 가장 먼 '줄'(같은 along 값) relaySafeLines 개. 대각선 카메라면 꼭짓점 삼각형, 정면 카메라면 맨 아래 행들.
        var levels = new List<float>();
        float tol = 0.25f * g.TileSize;
        foreach (float a in along)
        {
            bool known = false;
            foreach (float l in levels) if (Mathf.Abs(l - a) <= tol) { known = true; break; }
            if (!known) levels.Add(a);
        }
        levels.Sort((x, y) => y.CompareTo(x));   // 화면 아래(큰 값)부터
        int safeLines = Mathf.Clamp(_boss != null ? _boss.relaySafeLines : 3, 1, Mathf.Max(1, levels.Count - 2));
        float safeFrom = levels[safeLines - 1] - tol;

        float halfWidth = _boss != null ? _boss.relayCorridorHalfWidth : 3.5f;
        _relaySafeMask = 0UL;
        _relaySideMask = 0UL;
        for (int i = 0; i < BossTileGrid.TileCount; i++)
        {
            bool safe = along[i] >= safeFrom;
            if (safe) _relaySafeMask |= 1UL << i;
            else if (Mathf.Abs(lat[i]) > halfWidth + 0.01f) _relaySideMask |= 1UL << i;
        }

        _relayHalfWidth = Mathf.Max(0.5f, halfWidth);

        _relaySouthReach = maxAlong + halfAlong;
        float northReach = -minAlong + halfAlong;
        _relayGoalPoint = _relayCenter - _relaySouth * Mathf.Max(0f, northReach - (_boss != null ? _boss.relayGoalInset : 4f));
        return true;
    }

    ulong RelaySafeMask() => _relaySafeMask;
    ulong RelaySideMask() => _relaySideMask;

    /// <summary>보스에서 6시 끝까지(보행면에서 자른) 거리.</summary>
    float RelayReachSouth()
    {
        Vector3 edge = _relayCenter + _relaySouth * _relaySouthReach;
        float reach = Mathf.Max(0.5f, Vector3.Dot(edge - transform.position, _relaySouth));
        Vector3 from = transform.position;
        if (NavMesh.Raycast(from, from + _relaySouth * reach, out NavMeshHit hit, NavMesh.AllAreas))
            reach = Mathf.Max(0.5f, Vector3.Distance(from, hit.position));
        return reach;
    }

    void StartRelay()
    {
        if (!ResolveRelayGeometry())
        {
            Debug.LogWarning($"{name}: 간파 릴레이 — 타일 그리드(보스방 범위)를 못 찾아 건너뛴다.", this);
            EndRaidGimmick("타일 그리드 없음(기술적 중단)", resume: true);
            return;
        }

        _relayPushes = 0;
        _relayTimedOut = false;
        _relayRoundsStarted = false;
        FaceDirection(_relaySouth);

        // 집결 — 6시 안전 구역 밖 전 타일 방전(예고가 곧 "어디로 가라"다). 기존 레이드 방전 경로.
        ulong safe = RelaySafeMask();
        _electricFloor?.ServerQueueDischarge(BossTileGrid.AllMask & ~safe, _boss.relayGatherWarn, _boss.relayOpeningDamage);
        ShowRaidSubtitleServer("BOSS_RAID_RelayGather");
        FightLog("간파 릴레이 시작 — 6시 집결");
        Debug.Log($"[23호/릴레이] 시작 — 6시(화면 아래) = {_relaySouth} · 안전 칸 {BossTileGrid.Count(_relaySafeMask)} · " +
                  $"측면 장판 칸 {BossTileGrid.Count(_relaySideMask)} · 통로 반폭 {_relayHalfWidth:0.0}m · " +
                  $"중앙에서 보스까지 {Vector3.Distance(_relayCenter, transform.position):0.0}m · 12시 목표까지 " +
                  $"{Vector3.Dot(_relayGoalPoint - transform.position, -_relaySouth):0.0}m", this);
        SetStep(GimmickStep.Gather, _boss.relayGatherWarn + 0.2f);
    }

    void StartRelayRounds()
    {
        // 첫 라운드 명단 = 지금 살아서 행동 가능한 사람. (문서의 미도착자 위치 보정은 서버 순간이동 API 가 없어 뺐다.)
        _relayScratch.Clear();
        foreach (Player p in BossPatternTargets.AllPlayers())
            if (p != null && p.IsSpawned && BossPatternTargets.IsAlive(p)) _relayScratch.Add(p.OwnerClientId);

        if (_relayScratch.Count == 0)
        {
            EndRaidGimmick("전원 행동 불가", resume: true);
            return;
        }

        _relayRoster.Begin(_relayScratch);
        _relayRoundsStarted = true;
        _relayDeadline = Time.time + _boss.relayReady + _boss.relayTimeout;
        _relayNextSide = Time.time + _boss.relayReady;
        ShowRaidSubtitleServer("BOSS_RAID_RelayGuide");
        Debug.Log($"[23호/릴레이] 라운드 1 — 명단 {_relayScratch.Count}명", this);
        SetStep(GimmickStep.Ready, _boss.relayReady);
    }

    void TickRelay()
    {
        if (_relayRoundsStarted && _gStep != GimmickStep.FailDash)
        {
            TickRelayRoster();
            if (!RaidGimmickActive) return;

            // 측면 장판 — 통로 좌우 열 주기 방전(장판이 이전 방전을 끝낸 뒤 다음 요청을 낸다).
            if (Time.time >= _relayNextSide)
            {
                _electricFloor?.ServerQueueDischarge(RelaySideMask(), _boss.relaySideWarn, _boss.relaySideDamage);
                _relayNextSide = Time.time + Mathf.Max(_boss.relaySideInterval, _boss.relaySideWarn + 0.5f);
            }
        }

        switch (_gStep)
        {
            case GimmickStep.Gather:
                if (_gStepTimer <= 0f) StartRelayRounds();
                break;

            case GimmickStep.Ready:
            case GimmickStep.Reposition:
                if (_gStepTimer <= 0f) BeginRelayPrepare();
                break;

            case GimmickStep.Prepare:
                // 준비의 마지막 relayCounterWindow 초만 간파 창. 제한 시간을 넘긴 준비는 창 없이 돌진(문서 4-8).
                if (!_relayTimedOut && !_relayAttemptTaken && !_counterWindow.Value
                    && _gStepTimer > 0f && _gStepTimer <= _boss.relayCounterWindow)
                    SetCounterWindow(true, _gStepTimer);
                if (_gStepTimer > 0f) break;
                SetCounterWindow(false);
                HideAttackConeClientRpc();
                BeginRelayFailDash();
                break;

            case GimmickStep.Push:
            {
                float t = _boss.relayPushTime > 0f ? 1f - Mathf.Clamp01(_gStepTimer / _boss.relayPushTime) : 1f;
                WarpTo(Vector3.Lerp(_relayPushFrom, _relayPushTo, t));
                if (_gStepTimer <= 0f) EndRelayPush();
                break;
            }

            case GimmickStep.FailDash:
                if (TickGimmickDash()) EndRaidGimmick("실패 — 돌진 실행", resume: true);
                break;
        }
    }

    void BeginRelayPrepare()
    {
        if (!_relayTimedOut && Time.time >= _relayDeadline)
        {
            _relayTimedOut = true;
            FightLog("간파 릴레이 — 제한 시간 초과");
        }
        _relayAttemptTaken = false;
        FaceDirection(_relaySouth);

        BossAttackEntry dash = AttackEntryOf(BossAttackId.Dash);
        if (dash != null && !string.IsNullOrEmpty(dash.prepStateName))
            CrossFadeStateClientRpc(dash.prepStateName);

        float reach = RelayReachSouth();
        if (_relayTimedOut)
        {
            ShowRaidSubtitleServer("BOSS_RAID_RelayFail");
            ShowRageDashTelegraphClientRpc(reach, _relayHalfWidth);       // 빨강 — 간파 창 없음
        }
        else
        {
            ShowDashTelegraphClientRpc(reach, _relayHalfWidth, _boss.relayPrepare);   // 파랑 — 간파 가능
        }
        SetStep(GimmickStep.Prepare, _boss.relayPrepare);
    }

    void OnRelayCounter(ulong clientId)
    {
        _relayAttemptTaken = true;
        bool advanced = _relayRoster.RecordSuccess(clientId);
        NotifyRelayAcceptedRpc(clientId);
        SyncRelayLocks();
        if (advanced && _relayRoster.ParticipantCount > 1)
            ShowRaidSubtitleServer("BOSS_RAID_RelayRoundReset");

        _relayPushes++;
        Vector3 north = -_relaySouth;
        Vector3 from = transform.position;
        float remain = Mathf.Max(0f, Vector3.Dot(_relayGoalPoint - from, north));
        float push = _boss.relayPushDistance * (_relayRoster.ParticipantCount <= 1 ? _boss.relaySoloPushMultiplier : 1f);
        Vector3 to = from + north * Mathf.Min(push, remain);
        if (NavMesh.Raycast(from, to, out NavMeshHit hit, NavMesh.AllAreas)) to = hit.position;
        _relayPushFrom = from;
        _relayPushTo = to;

        Debug.Log($"[23호/릴레이] {_relayPushes}회 성공 — client {clientId} · 라운드 {_relayRoster.Round} " +
                  $"({_relayRoster.SucceededCount}/{_relayRoster.ParticipantCount}) · 남은 거리 {remain:0.0}m", this);
        SetStep(GimmickStep.Push, _boss.relayPushTime);
    }

    void EndRelayPush()
    {
        WarpTo(_relayPushTo);
        FaceDirection(_relaySouth);
        float remain = Vector3.Dot(_relayGoalPoint - transform.position, -_relaySouth);
        if (remain <= 0.1f)
        {
            ShowRaidSubtitleServer("BOSS_RAID_RelaySuccess");
            FightLog($"간파 릴레이 성공 — {_relayPushes}회");
            EndRaidGimmick("성공 — 12시 도달", resume: false);
            _groggyKind = BossPauseCondition.CounterGroggy;   // 장판·드론 정지 조건(그로기 종류)
            StunForCounter(_boss.relaySuccessExhaust);        // 탈진 = 그로기 클립 재사용. 게이지·취약은 건드리지 않는다
            return;
        }

        // 보행면에 막혀 한 발도 못 갔다 — 정상 플레이로 풀 수 없는 상태(문서 4-7 기술적 중단).
        if ((_relayPushTo - _relayPushFrom).sqrMagnitude < 0.01f)
        {
            Debug.LogWarning($"[23호/릴레이] 밀림이 막혔다(남은 {remain:0.0}m) — 기술적 중단. relayGoalInset·NavMesh 를 볼 것.", this);
            EndRaidGimmick("밀림 막힘(기술적 중단)", resume: true);
            return;
        }

        BossAttackEntry e = _currentEntry;
        if (e != null && !string.IsNullOrEmpty(e.animatorStateName))
            CrossFadeStateClientRpc(e.animatorStateName);
        SetStep(GimmickStep.Reposition, _boss.relayReposition);
    }

    void BeginRelayFailDash()
    {
        ShowRaidSubtitleServer("BOSS_RAID_RelayFail");
        FightLog("간파 릴레이 실패 — 돌진");
        BeginGimmickDash(_relaySouth, _boss.relayFailDashDamage, RelayReachSouth(), GimmickStep.FailDash);
    }

    /// <summary>[서버] 쓰러짐·이탈 = 명단에서 빼고 재계산, 부활·복귀 = 다음 라운드 대기(문서 4-4·4-5).</summary>
    void TickRelayRoster()
    {
        _relayScratch.Clear();
        foreach (ulong id in _relayRoster.Participants) _relayScratch.Add(id);
        foreach (ulong id in _relayScratch)
            if (!IsClientAlive(id)) _relayRoster.Remove(id);

        foreach (Player p in BossPatternTargets.AllPlayers())
        {
            if (p == null || !p.IsSpawned || !BossPatternTargets.IsAlive(p)) continue;
            ulong id = p.OwnerClientId;
            if (!_relayRoster.IsParticipant(id) && !_relayRoster.IsPending(id)) _relayRoster.AddPending(id);
        }

        SyncRelayLocks();
        if (_relayRoster.ParticipantCount == 0)
            EndRaidGimmick("전원 행동 불가", resume: true);
    }

    /// <summary>[서버] 명단의 잠금 상태와 보낸 상태의 차이만 RPC 로 보낸다.</summary>
    void SyncRelayLocks()
    {
        _relayScratch.Clear();
        foreach (ulong id in _relayLocksSent)
            if (!_relayRoster.IsLocked(id)) _relayScratch.Add(id);
        foreach (ulong id in _relayScratch)
        {
            _relayLocksSent.Remove(id);
            if (IsSpawned) NotifyRelayLockRpc(id, false);
        }

        foreach (ulong id in _relayRoster.Participants)
            if (_relayRoster.IsLocked(id) && _relayLocksSent.Add(id) && IsSpawned)
                NotifyRelayLockRpc(id, true);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void NotifyRelayAcceptedRpc(ulong clientId) => RelayCounterAccepted?.Invoke(clientId);

    [Rpc(SendTo.ClientsAndHost)]
    void NotifyRelayLockRpc(ulong clientId, bool locked) => RelayLockChanged?.Invoke(clientId, locked);

    [Rpc(SendTo.ClientsAndHost)]
    void NotifyRelayEndedRpc() => RelayEnded?.Invoke();

    // ─── 연쇄 간파 ─────────────────────────────────────────────────────
    void StartChain()
    {
        var threat = new List<KeyValuePair<ulong, float>>();
        foreach (Player p in BossPatternTargets.AllPlayers())
            if (p != null && p.IsSpawned && BossPatternTargets.IsAlive(p))
                threat.Add(new KeyValuePair<ulong, float>(p.OwnerClientId, _threat.Score(p.transform.GetInstanceID(), Time.time)));

        if (threat.Count == 0)
        {
            EndRaidGimmick("전원 행동 불가", resume: true);
            return;
        }

        _chainOrder = BossChainCounterRules.Order(threat);
        _chainPos = 0;
        _chainRound = 0;
        _chainSuccesses = 0;
        _chainTotal = BossChainCounterRules.RoundsFor(threat.Count);
        FaceScreenSouthForCharge();
        ShowRaidSubtitleServer("BOSS_RAID_ChainGuide");
        FightLog($"연쇄 간파 시작 — {threat.Count}명 · {_chainTotal}회");
        Debug.Log($"[23호/연쇄 간파] 시작 — 순번 [{string.Join(", ", _chainOrder)}] · {_chainTotal}회", this);
        SetStep(GimmickStep.ChainIntro, 1.5f);
    }

    void TickChain()
    {
        switch (_gStep)
        {
            case GimmickStep.ChainIntro:
            case GimmickStep.ChainInterval:
                if (_gStepTimer <= 0f) BeginChainMark();
                break;

            case GimmickStep.ChainMark:
                if (_chainTarget == null || !BossPatternTargets.IsAlive(_chainTarget))
                {
                    HideAttackConeClientRpc();
                    BeginChainMark();   // 지목 중 쓰러짐 — 회차를 쓰지 않고 다음 사람
                    break;
                }
                FaceDirection(_chainTarget.transform.position - transform.position);
                if (_gStepTimer > 0f) break;
                SetCounterWindow(true, _boss.chainCounterWindow);
                SetStep(GimmickStep.ChainWindow, _boss.chainCounterWindow);
                break;

            case GimmickStep.ChainWindow:
                if (_chainTarget == null || !BossPatternTargets.IsAlive(_chainTarget))
                {
                    SetCounterWindow(false);
                    HideAttackConeClientRpc();
                    BeginChainMark();   // 창 도중 쓰러짐 — 지목 중과 같이 회차를 쓰지 않고 다음 사람(빈 자리에 방전 금지)
                    break;
                }
                if (_gStepTimer > 0f) break;
                SetCounterWindow(false);
                HideAttackConeClientRpc();
                float reach = _chainTarget != null
                    ? Vector3.Distance(transform.position, _chainTarget.transform.position) + 4f
                    : DashMaxDistance;
                BeginGimmickDash(transform.forward, _boss.chainDashDamage, Mathf.Min(reach, DashMaxDistance * 2f), GimmickStep.ChainDash);
                break;

            case GimmickStep.ChainDash:
                if (!TickGimmickDash()) break;
                _chainRound++;
                // 실패 = 돌진 끝 + 방 전체 방전(기존 레이드 방전 경로).
                _electricFloor?.ServerQueueDischarge(BossTileGrid.AllMask, _boss.chainFailWarn, _boss.chainFailDamage);
                FightLog($"연쇄 간파 {_chainRound}회 실패 → 방전");
                Debug.Log($"[23호/연쇄 간파] {_chainRound}/{_chainTotal} 실패 — 방전", this);
                BossAttackEntry e = _currentEntry;
                if (e != null && !string.IsNullOrEmpty(e.animatorStateName)) CrossFadeStateClientRpc(e.animatorStateName);
                SetStep(GimmickStep.ChainInterval, _boss.chainFailWarn + _boss.chainInterval);
                break;

            case GimmickStep.ChainStun:
                if (_gStepTimer > 0f) break;
                BossAttackEntry ce = _currentEntry;
                if (ce != null && !string.IsNullOrEmpty(ce.animatorStateName)) CrossFadeStateClientRpc(ce.animatorStateName);
                SetStep(GimmickStep.ChainInterval, _boss.chainInterval);
                break;
        }
    }

    void BeginChainMark()
    {
        if (_chainRound >= _chainTotal) { ResolveChain(); return; }

        int i = BossChainCounterRules.NextIndex(_chainOrder, _chainPos, IsClientAlive);
        if (i < 0) { ResolveChain(); return; }

        _chainPos = i + 1;
        _chainTarget = FindPlayerByClient(_chainOrder[i]);
        if (_chainTarget == null) { ResolveChain(); return; }

        AdoptAggro(_chainTarget.transform, "연쇄 간파 지목");
        FaceDirection(_chainTarget.transform.position - transform.position);

        BossAttackEntry dash = AttackEntryOf(BossAttackId.Dash);
        if (dash != null && !string.IsNullOrEmpty(dash.prepStateName))
            CrossFadeStateClientRpc(dash.prepStateName);

        // 지목 + 간파 창 동안 띠가 차오른다(파랑). 방향은 예고가 매 프레임 보스 정면에서 다시 잰다 — 지목 중 보스가 대상을 따라 돈다.
        float reach = Vector3.Distance(transform.position, _chainTarget.transform.position) + 4f;
        float halfWidth = TryGetDashFootprint(dash, transform.forward, out float w, out _) ? w : 1.7f;
        ShowDashTelegraphClientRpc(reach, halfWidth, _boss.chainMarkTime + _boss.chainCounterWindow);

        Debug.Log($"[23호/연쇄 간파] {_chainRound + 1}/{_chainTotal} 지목 — client {_chainOrder[i]}", this);
        SetStep(GimmickStep.ChainMark, _boss.chainMarkTime);
    }

    void OnChainCounter()
    {
        _chainSuccesses++;
        _chainRound++;
        FightLog($"연쇄 간파 {_chainRound}회 성공");
        Debug.Log($"[23호/연쇄 간파] {_chainRound}/{_chainTotal} 성공 (누적 {_chainSuccesses})", this);
        SetStep(GimmickStep.ChainStun, _boss.chainHitStun);
    }

    void ResolveChain()
    {
        int total = Mathf.Max(_chainRound, 1);
        BossChainCounterRules.Outcome o = BossChainCounterRules.Resolve(_chainSuccesses, total);
        FightLog($"연쇄 간파 결과 {_chainSuccesses}/{total} → {o}");

        if (o == BossChainCounterRules.Outcome.None)
        {
            EndRaidGimmick($"보상 없음({_chainSuccesses}/{total})", resume: true);
            return;
        }

        EndRaidGimmick($"{o}({_chainSuccesses}/{total})", resume: false);
        _attackPhase = BossAttackPhase.None;

        if (o == BossChainCounterRules.Outcome.Full)
        {
            EnterSuppress("연쇄 간파 전부 성공", fromReservation: true);
            return;
        }

        // 절반 이상 = 그로기 → 취약(문서 §8.5). 취약 길이만 이 기믹 값으로 덮는다.
        _groggyKind = BossPauseCondition.CounterGroggy;
        StunForCounter(_boss.chainPartialGroggy);
        StartVulnerable();
        if (_vulnerableUntil > 0f)
            _vulnerableUntil = Time.time + _boss.chainPartialGroggy + _boss.chainPartialVulnerable;
    }

    // ─── 북진 압박 (RaidPushNorth — PLAN-boss-ab-test, 은희 제안 61) ───────────────
    // 중앙 점프(송전기 흐름) → [차징: 송전탑 인원 비례 + 바닥 즉사 예고] → 탑 전부 파괴 = 예고 취소 + 무력화(간파 1회 = 화면 위로 1칸)
    // → 그 자리에서 다음 차징 … → 위 꼭짓점 도달 = 제압(8초 · ×1.3). 탑이 남은 채 차징이 끝나면 예고 칸 즉사 → 다음 차징.
    // 🔴 BeginCharge 를 다시 부르지 않는다 — 부르면 중앙으로 돌아간다(Codex 10-20). 탑은 _charge.Begin 을 회차마다 다시 부른다(연속 Begin 허용).
    int _pnRound;
    bool _pnCounterTaken;
    Vector3 _pnGoalPoint;   // 위 꼭짓점 칸 중심
    float _pnStepLen;       // 간파 1회 밀림(m) — 화면 위로 대각 한 칸

    void StartPushNorth()
    {
        if (!ResolveRelayGeometry() || !TryGetTileGrid(out BossTileGrid g))
        {
            Debug.LogWarning($"{name}: 북진 압박 — 타일 그리드를 못 찾아 건너뛴다.", this);
            EndRaidGimmick("타일 그리드 없음(기술적 중단)", resume: true);
            return;
        }

        // 목표 = 화면 위로 가장 먼 칸(같으면 가운데). 한 칸 = 중앙 칸에서 화면 위쪽 이웃 칸까지의 화면 세로 거리 최대값
        // (대각선 카메라 = 타일 대각선, 정면 카메라 = 타일 한 변).
        int n = BossTileGrid.Size, mid = n / 2;
        Vector3 north = -_relaySouth;
        Vector3 right = Vector3.Cross(Vector3.up, _relaySouth);
        float best = float.MinValue, bestLat = float.MaxValue;
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                Vector3 d = g.TileCenter(r, c) - _relayCenter; d.y = 0f;
                float a = Vector3.Dot(d, north), l = Mathf.Abs(Vector3.Dot(d, right));
                if (a > best + 0.01f || (Mathf.Abs(a - best) <= 0.01f && l < bestLat)) { best = a; bestLat = l; _pnGoalPoint = g.TileCenter(r, c); }
            }
        float step = 0f;
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                Vector3 d = g.TileCenter(mid + dr, mid + dc) - g.TileCenter(mid, mid); d.y = 0f;
                step = Mathf.Max(step, Vector3.Dot(d, north));
            }
        _pnStepLen = Mathf.Max(0.5f, step * Mathf.Max(0.25f, _boss.pushNorthStepTiles));

        _pnRound = 0;
        FaceDirection(_relaySouth);
        ShowRaidSubtitleServer("BOSS_RAID_PushNorthGuide");
        FightLog("북진 압박 시작");
        Debug.Log($"[23호/북진] 시작 — 1칸 {_pnStepLen:0.0}m · 위 끝까지 {Vector3.Dot(_pnGoalPoint - transform.position, north):0.0}m", this);
        BeginPushNorthCharge();
    }

    float PnRiseGrace => _charge is BossChargeSequence seq ? seq.RiseGrace : 1.5f;
    const float PushNorthPylonAvoidRadius = 3f;   // 보스 몸(반경 ~0.85) + 탑 몸통 + 여유

    void BeginPushNorthCharge()
    {
        _pnRound++;
        _pnCounterTaken = false;
        FaceDirection(_relaySouth);
        EndDashMove();

        BossAttackEntry e = _currentEntry;
        if (e != null && !string.IsNullOrEmpty(e.animatorStateName))
            CrossFadeStateClientRpc(e.animatorStateName);   // 차징 자세

        float chargeTime = _pnRound == 1 ? _boss.pushNorthFirstChargeTime : _boss.pushNorthChargeTime;
        // 🔴 예고 길이 = 탑 제한시간과 **같은 종료 시각**(제한 + 상승 여유) — 따로 돌면 탑을 부숴도 방전이 먼저 터진다(Codex 10-20).
        float total = chargeTime + PnRiseGrace;
        int pylons = PylonCountFor(CountAlivePlayers());
        if (_charge == null) _charge = GetComponentInChildren<IBossChargeSequence>(true);
        // 밀린 보스가 탑 자리(중앙 둘레 대각선)에 서 있으면 그 탑이 몸 안에서 솟아 안 보인다(경석 Play 10-20) → 발밑 탑은 뺀다.
        //    3인(탑 4개)이면 그 회차는 3개 — 제안서 답 "3인 기준 3~4개" 범위.
        if (_charge is BossChargeSequence seq) seq.AvoidNextSelection(transform.position, PushNorthPylonAvoidRadius);
        _charge?.Begin(pylons, chargeTime, _boss.raidPylonHpMultiplier);

        ulong mask = _pnRound == 1 ? (BossTileGrid.AllMask & ~RelaySafeMask()) : BossTileGrid.AllMask;
        // 탑이 남으면 즉사 방전 — 데이터 칸 pushNorthDischargeIgnoreInvuln(0/1, 할 일 105 기본 1)이 1 이면 무적으로 못 피한다:
        // 전체 범위 회차(2회차~)는 전멸기 = 살아 있는 참가자 전원 사망 처리, 1회차(안전 구역 있음)는 칸 판정 + 무적 무시 즉사 피해(BossWipeDischargeRules).
        _electricFloor?.ServerQueueDischarge(mask, total, 0, lethal: true,
                                             ignoreInvulnerability: _boss.pushNorthDischargeIgnoreInvuln != 0);
        StartChargeBallClientRpc(transform.position, ChargeZoneRadius);

        Debug.Log($"[23호/북진] {_pnRound}회차 차징 {chargeTime:0.#}초(+상승 {PnRiseGrace:0.#}) · 탑 {pylons}개 · " +
                  $"예고 {BossTileGrid.Count(mask)}칸{(_pnRound == 1 ? "(안전 구역 제외)" : "(전체)")}", this);
        SetStep(GimmickStep.PnCharge, total);
    }

    /// <summary>[서버] 회차 정리 — 바닥 예고 취소 · 구슬 · 탑. 다음 회차·종료·중단 공통.</summary>
    void CleanupPushNorthRound(bool cancelFloor = true)
    {
        if (cancelFloor) _electricFloor?.ServerCancelDischarge();
        if (IsSpawned) EndChargeBallClientRpc(broken: true);
        _charge?.Cancel();
    }

    void TickPushNorth()
    {
        switch (_gStep)
        {
            case GimmickStep.PnCharge:
            {
                // 🔴 보스 Update 가 장판(DefaultExecutionOrder 100)보다 먼저 돈다 — 마지막 틱의 파괴 성공이 발동보다 먼저 확정된다.
                BossChargeResult res = _charge != null ? _charge.Poll() : BossChargeResult.InProgress;
                if (res == BossChargeResult.AllPylonsDestroyed)
                {
                    CleanupPushNorthRound();
                    SetCounterWindow(true, _boss.pushNorthStunTime);   // 보라색 = 판정 창(같이 꺼진다)
                    // 무력화 = 그로기 클립(Boss_23_.groggy) 끝의 **쓰러진 자세로 멈춰** 간파를 기다린다(경석 10-23).
                    PlayPushNorthDownPoseClientRpc();
                    FightLog($"북진 {_pnRound}회차 — 탑 파괴 → 무력화");
                    Debug.Log($"[23호/북진] {_pnRound}회차 탑 전부 파괴 — 무력화 {_boss.pushNorthStunTime:0.#}초(간파 1회 = 1칸)", this);
                    SetStep(GimmickStep.PnStun, _boss.pushNorthStunTime);
                    break;
                }
                if (_gStepTimer > 0f) break;
                // 실패 — 예고 칸 즉사 방전은 장판이 그대로 낸다. 탑·구슬만 걷고 잠깐 뒤 다음 차징.
                CleanupPushNorthRound(cancelFloor: false);
                FightLog($"북진 {_pnRound}회차 실패 — 즉사 방전");
                Debug.Log($"[23호/북진] {_pnRound}회차 실패 — 탑이 남았다 · 즉사 방전", this);
                SetStep(GimmickStep.PnAfterFail, 2f);
                break;
            }

            case GimmickStep.PnStun:
                if (_gStepTimer > 0f) break;
                SetCounterWindow(false);
                ReleasePushNorthDownPose();
                BeginPushNorthCharge();   // 간파 없이 끝 — 그 자리에서 다음 차징
                break;

            case GimmickStep.PnPush:
            {
                float t = _boss.pushNorthPushTime > 0f ? 1f - Mathf.Clamp01(_gStepTimer / _boss.pushNorthPushTime) : 1f;
                WarpTo(Vector3.Lerp(_relayPushFrom, _relayPushTo, t));
                if (_gStepTimer <= 0f) EndPushNorthPush();
                break;
            }

            case GimmickStep.PnAfterFail:
                if (_gStepTimer <= 0f) BeginPushNorthCharge();
                break;
        }
    }

    // ─── 북진 무력화 자세 — 그로기 클립 끝(쓰러진 자세)에서 멈춰 둔다(경석 10-23) ───
    // 기믹 중 상태는 Attack 이라 Groggy bool 이 꺼져 있다 → 그냥 두면 GroggyStart 끝(exitTime 0.95)에서 Groggy 를 거쳐
    // GroggyEnd(일어남) → Locomotion 으로 **다시 선다**. 그래서 exitTime 직전에서 자세 홀드 래치(animator.speed 0)로 얼린다.
    // 🔴 래치는 카운터 선딜 홀드와 **공용**이다 — 사망·중단은 AbortAttackChain → ResetCounterWindup 이 같이 푼다.
    //    간파 성공·3초 경과·기믹 종료는 ReleasePushNorthDownPose 가 푼다(안 풀면 보스가 쓰러진 채 굳는다).
    // ⚠️ 0.94 이후의 클립 이벤트는 안 온다(얼렸으므로). 그로기 클립 끝에 짝 StopEffect 를 넣으면 루프 이펙트가 남는다.
    const float PushNorthDownPoseAt = 0.94f;   // No23Controller GroggyStart→Groggy exitTime 0.95 바로 앞
    Coroutine _pnDownPoseRoutine;

    [ClientRpc]
    void PlayPushNorthDownPoseClientRpc()
    {
        if (animator == null) return;
        if (_pnDownPoseRoutine != null) StopCoroutine(_pnDownPoseRoutine);
        SafeCrossFade(GroggyStartState);
        _pnDownPoseRoutine = StartCoroutine(HoldPushNorthDownPose());
    }

    System.Collections.IEnumerator HoldPushNorthDownPose()
    {
        int hash = Animator.StringToHash(GroggyStartState);
        while (animator != null)
        {
            yield return null;   // Update 뒤·애니 평가 앞 — 이번 프레임에 exitTime 을 넘길지 미리 본다
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.shortNameHash != hash)
            {
                // 아직 GroggyStart 로 넘어가는 중이면 기다리고, 다른 상태가 가져갔으면(간파 리액션·차징·사망) 그만둔다.
                if (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == hash) continue;
                break;
            }
            if (animator.IsInTransition(0)) continue;

            float step = st.length > 0.0001f ? Time.deltaTime * animator.speed / st.length : 1f;
            if (st.normalizedTime + step < PushNorthDownPoseAt) continue;

            animator.Play(hash, 0, PushNorthDownPoseAt);
            animator.Update(0f);   // 즉시 평가 — HoldAttackPoseClientRpc 와 같은 이유
            HoldCounterPoseLocal();
            break;
        }
        _pnDownPoseRoutine = null;
    }

    [ClientRpc]
    void ReleasePushNorthDownPoseClientRpc()
    {
        if (_pnDownPoseRoutine != null)
        {
            StopCoroutine(_pnDownPoseRoutine);
            _pnDownPoseRoutine = null;
        }
        RestoreCounterPose();
    }

    /// <summary>[서버] 무력화 자세 홀드를 푼다(멱등).</summary>
    void ReleasePushNorthDownPose()
    {
        if (IsSpawned) ReleasePushNorthDownPoseClientRpc();
    }

    void OnPushNorthCounter()
    {
        _pnCounterTaken = true;
        Vector3 north = -_relaySouth;
        Vector3 from = transform.position;
        float remain = Mathf.Max(0f, Vector3.Dot(_pnGoalPoint - from, north));
        _relayPushFrom = from;
        // NavMesh.Raycast 로 자르지 않는다 — 내려간 송전탑 자리가 메시 구멍이면 첫 칸에서 막힌다. WarpTo 가 가까운 메시로 붙인다.
        _relayPushTo = from + north * Mathf.Min(_pnStepLen, remain);
        FightLog($"북진 {_pnRound}회차 — 간파 → 1칸");
        Debug.Log($"[23호/북진] 간파 — 위로 {Mathf.Min(_pnStepLen, remain):0.0}m (남은 {remain:0.0}m)", this);
        SetStep(GimmickStep.PnPush, _boss.pushNorthPushTime);
    }

    void EndPushNorthPush()
    {
        WarpTo(_relayPushTo);
        Vector3 north = -_relaySouth;
        float before = Vector3.Dot(_pnGoalPoint - _relayPushFrom, north);
        float remain = Vector3.Dot(_pnGoalPoint - transform.position, north);

        // 도달 — 또는 한 칸 밀었는데 거의 못 갔다(지형 끝) = 벽에 닿은 것으로 본다(무한 반복 방지, Codex 10-20).
        bool stuck = before - remain < 0.1f;
        // 🔴 첫 칸부터 막혔으면 벽이 아니라 출발점이 막힌 것(송전탑 자리 NavMesh 구멍 등) — 보상 없이 기술적 중단(릴레이와 같은 처리).
        if (stuck && _pnRound <= 1 && remain > 0.3f)
        {
            Debug.LogWarning($"[23호/북진] 첫 칸부터 밀림이 막혔다(남은 {remain:0.0}m) — 기술적 중단. 출발점 NavMesh·북진 목표 지점을 볼 것.", this);
            EndRaidGimmick("밀림 막힘(기술적 중단)", resume: true);
            return;
        }
        if (remain <= 0.3f || stuck)
        {
            if (stuck) Debug.LogWarning($"[23호/북진] 밀림이 막혔다(남은 {remain:0.0}m) — 도달로 처리한다.", this);
            ShowRaidSubtitleServer("BOSS_RAID_PushNorthSuccess");
            EndRaidGimmick($"성공 — {_pnRound}회차", resume: false);
            _attackPhase = BossAttackPhase.None;
            EnterSuppressWith("북진 압박 파훼", _boss.pushNorthRewardDuration, _boss.pushNorthRewardDamageMultiplier);
            return;
        }
        BeginPushNorthCharge();
    }
    #endregion
}
