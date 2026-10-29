using UnityEngine;

/// <summary>23호 간파 창 종류(할 일 139-6 단계 3) — 지금 열린 창이 어느 패턴의 창인가.</summary>
public enum TwentyThreeWindowKind
{
    Unknown,
    Dash,          // A  일반 돌진(단타) — 준비 클립(prepStateName)
    ChainedDash,   // A' 즉시 연결 후속 돌진 — 돌진 클립(animatorStateName)에서 자세 정지
    RaidRelay,     // D  레이드 간파 릴레이 — 준비 클립, 준비 도중 열림
    RaidChain,     // E  레이드 연쇄 간파 — 준비 클립, 지목 뒤 열림
    PushNorth,     // F  레이드 북진 무력화 — 그로기 진입 클립(GroggyStart)에서 정지
    Grab,          // B  잡기(붙잡은 뒤) — 붙잡기·지짐이·내려치기 클립
    JumpLanding,   // C  점프×3 착지 — 착지 클립(또는 착지 뒤 이동 자세)
}

/// <summary>
/// 23호 창 종류 구별 재료 — 전부 이미 공개된 값이다(보스 코드 무수정·새 전송 없음).
/// 애니메이터는 각 피어 로컬이지만 서버가 같은 ClientRpc(PlayPrepPose·CrossFadeState·PlayAttackAnim·PlayPushNorthDownPose)로 구동한다.
/// </summary>
public struct TwentyThreeWindowObservation
{
    /// <summary>복제 창 bool(<see cref="TwentyThreeBoss.CounterWindowOpen"/>).</summary>
    public bool WindowOpen;
    /// <summary>0층 지금 상태(전이 중이면 다음 상태)의 shortNameHash. 0 = 모름.</summary>
    public int StateHash;
    /// <summary>그 상태에 들어간 뒤 게임초(정규화 시각 × 길이 ÷ 애니메이터 속도). 음수 = 모름(정지 중 등).</summary>
    public float StateElapsed;
    /// <summary>창이 열린 뒤 게임초(창 기록 — 이 피어가 bool 열림을 받은 시각 기준). 음수 = 모름.</summary>
    public float OpenElapsed;
    /// <summary>서버 공개 <see cref="TwentyThreeBoss.IsChargeGimmickActive"/>(레이드 기믹 = 차징 대기 안) 1/0. -1 = 모름(클라 — 서버 전용 값).</summary>
    public int ServerRaidGimmick;
}

/// <summary>
/// 23호 간파 창 길이 어댑터(할 일 139-6 단계 3) — <see cref="InterruptWindowLengthResolver"/> 에 붙는다.
/// <list type="bullet">
/// <item>종류 구별 = 애니메이터 상태(준비 클립 / 돌진 클립 / GroggyStart) + 보스 SO 페이즈 구성(이 데이터가 쓰는 레이드 기믹).
///       준비 클립을 같이 쓰는 A·D·E 는 서버면 공개 <see cref="TwentyThreeBoss.IsChargeGimmickActive"/>,
///       클라면 '창이 열릴 때 준비 클립이 이미 흐른 시간'(A ≈ 0 — 같은 틱 / D ≈ relayPrepare − relayCounterWindow / E ≈ chainMarkTime)으로 가른다.</item>
/// <item>길이 = 클라에도 있는 보스 SO 값(서버가 창을 여닫는 값과 같은 출처). 텔레그래프 growTime·경과는 private 이라 읽지 않는다.</item>
/// <item>점프 착지(C, 단계 4) = 콤보 점프 타의 <c>landingCounterWindow</c> 중 0 이 아닌 값이 한 가지면 그 값. 서로 다른 값이 둘 이상이면
///       어느 콤보인지 공개 상태로 모른다(서버의 남은 시각도 private) — 모름.</item>
/// <item>잡기(B, 단계 4) = 열린 뒤 경과 + 지금 클립에서 닫힘(마지막 내려치기 시작)까지 남은 시간 — <see cref="GrabLength"/>.</item>
/// </list>
/// 출처 표: Docs/tech/dash-interrupt.md §5 '창 길이 출처'.
/// </summary>
public static class TwentyThreeInterruptWindowLength
{
    /// <summary>클라 구별의 최소 간격(초) — 준비 시작과 창 열림 간격이 이보다 짧은 레이드 기믹은 일반 돌진(간격 0)과 못 가른다(모름).</summary>
    public const float MinDiscriminableOffset = 0.3f;

    const string GroggyStartState = "GroggyStart";   // TwentyThreeBoss.GroggyStartState(private const)와 같은 이름

    /// <summary>[어댑터] 23호면 맡는다. 창이 닫혀 있거나 종류·길이를 모르면 <see cref="InterruptWindowLengthResolver.Unknown"/>.</summary>
    public static bool TryResolve(GameObject owner, bool isServer, float openElapsed, out float length)
    {
        length = InterruptWindowLengthResolver.Unknown;
        if (!owner.TryGetComponent(out TwentyThreeBoss boss))
            return false;

        BossDataSO data = boss.Data;
        if (data == null || !boss.CounterWindowOpen)
            return true;

        var obs = new TwentyThreeWindowObservation
        {
            WindowOpen = true,
            OpenElapsed = openElapsed,
            ServerRaidGimmick = isServer ? (boss.IsChargeGimmickActive ? 1 : 0) : -1,
        };
        ReadAnimatorState(owner.GetComponentInChildren<Animator>(), out obs.StateHash, out obs.StateElapsed);

        length = Resolve(obs, data);
        return true;
    }

    /// <summary>관측 하나로 이번 열림 길이(게임초). 잡기는 애니 상태 경과가 필요해 따로 계산한다.</summary>
    public static float Resolve(in TwentyThreeWindowObservation obs, BossDataSO data)
    {
        TwentyThreeWindowKind kind = Classify(obs, data);
        return kind == TwentyThreeWindowKind.Grab ? GrabLength(obs, data) : LengthOf(kind, data);
    }

    /// <summary>0층 지금 상태(전이 중이면 다음 상태)와 그 상태 경과(게임초). 못 읽으면 0 / -1.</summary>
    public static void ReadAnimatorState(Animator animator, out int stateHash, out float stateElapsed)
    {
        stateHash = 0;
        stateElapsed = -1f;
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            return;

        AnimatorStateInfo st = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
        stateHash = st.shortNameHash;
        // 정규화 시각은 비반복 클립도 1 을 넘어 계속 자란다. 애니메이터 속도 0(자세 정지)이면 경과를 모른다.
        if (animator.speed > 0.01f)
            stateElapsed = st.normalizedTime * st.length / animator.speed;
    }

    /// <summary>지금 열린 23호 창의 종류. 창이 닫혀 있거나 공개 상태로 못 가르면 <see cref="TwentyThreeWindowKind.Unknown"/>.</summary>
    public static TwentyThreeWindowKind Classify(in TwentyThreeWindowObservation obs, BossDataSO data)
    {
        if (!obs.WindowOpen || data == null || obs.StateHash == 0)
            return TwentyThreeWindowKind.Unknown;

        int h = obs.StateHash;
        BossAttackEntry dash = DashEntry(data);

        // F — 북진 무력화: 그로기 진입 클립 끝에서 얼린 채 창이 열려 있다. 일반 그로기는 Attack 이탈이라 창이 닫힌다.
        if (h == Hash(GroggyStartState) && HasSequence(data, BossPhaseSequence.RaidPushNorth))
            return TwentyThreeWindowKind.PushNorth;

        if (dash != null && !string.IsNullOrEmpty(dash.prepStateName) && h == Hash(dash.prepStateName))
            return ClassifyPrep(obs, data);

        // A' — 후속 돌진은 준비 클립 없이 돌진 클립에서 자세를 멈춘다. 일반 돌진은 같은 틱에 준비 클립으로 덮는다.
        // 준비 클립이 없는 데이터면 일반 돌진도 돌진 클립이라 못 가른다.
        if (dash != null && !string.IsNullOrEmpty(dash.prepStateName) && h == Hash(dash.animatorStateName))
            return TwentyThreeWindowKind.ChainedDash;

        if (h == Hash(data.grabCatchState) || h == Hash(data.grabHoldState) || h == Hash(data.grabThrowState))
            return TwentyThreeWindowKind.Grab;

        // C — 착지 피해 직후 열리고, 착지가 끝나면 이동 자세로 이어진 채 창이 남는다.
        if (h == Hash(data.jumpLandingState) || h == Hash(data.locomotionState))
            return TwentyThreeWindowKind.JumpLanding;

        return TwentyThreeWindowKind.Unknown;
    }

    // 준비 클립을 같이 쓰는 A(일반 돌진)·D(릴레이)·E(연쇄).
    static TwentyThreeWindowKind ClassifyPrep(in TwentyThreeWindowObservation obs, BossDataSO data)
    {
        bool relay = HasSequence(data, BossPhaseSequence.RaidInterruptRelay);
        bool chain = HasSequence(data, BossPhaseSequence.RaidChainCounter);
        if (!relay && !chain)
            return TwentyThreeWindowKind.Dash;   // 이 데이터엔 준비 클립을 쓰는 레이드 기믹이 없다

        // 둘 다 쓰는 데이터면 레이드 기믹인 걸 알아도 어느 쪽인지 모른다.
        TwentyThreeWindowKind raid = relay && chain ? TwentyThreeWindowKind.Unknown
            : relay ? TwentyThreeWindowKind.RaidRelay : TwentyThreeWindowKind.RaidChain;

        if (obs.ServerRaidGimmick >= 0)
            return obs.ServerRaidGimmick == 1 ? raid : TwentyThreeWindowKind.Dash;

        // 클라 — 창이 열릴 때 준비 클립이 이미 흐른 시간. 일반 돌진은 같은 틱(≈0), 레이드 기믹은 준비 도중에 열린다.
        if (obs.StateElapsed < 0f || obs.OpenElapsed < 0f)
            return TwentyThreeWindowKind.Unknown;

        float offset = float.MaxValue;
        if (relay) offset = Mathf.Min(offset, RelayOpenOffset(data));
        if (chain) offset = Mathf.Min(offset, data.chainMarkTime);
        if (offset < MinDiscriminableOffset)
            return TwentyThreeWindowKind.Unknown;

        float prepAtOpen = obs.StateElapsed - obs.OpenElapsed;
        return prepAtOpen < offset * 0.5f ? TwentyThreeWindowKind.Dash : raid;
    }

    /// <summary>종류별 이번 열림 길이(게임초) — 서버가 창을 여닫는 데 쓰는 SO 값. 모르면 <see cref="InterruptWindowLengthResolver.Unknown"/>.</summary>
    public static float LengthOf(TwentyThreeWindowKind kind, BossDataSO data)
    {
        if (data == null)
            return InterruptWindowLengthResolver.Unknown;

        float length;
        switch (kind)
        {
            case TwentyThreeWindowKind.Dash:
            {
                // TwentyThreeBoss.CounterWindowDuration 과 같은 식. 준비 클립 경로는 애니 준비가 즉시라 창 타이머 단독으로 닫힌다.
                BossAttackEntry dash = DashEntry(data);
                length = dash != null && dash.opensCounterWindow ? Mathf.Clamp(dash.counterWindowDuration, 0f, 2f) : 0f;
                break;
            }
            // 공칭 — 돌진 클립 Hit 가 창보다 늦으면 그만큼 늦게 닫힌다(실제는 같거나 길다).
            case TwentyThreeWindowKind.ChainedDash: length = Mathf.Clamp(data.comboDashWindup, 0f, 2f); break;
            // 준비(relayPrepare)의 마지막 relayCounterWindow 초. 준비가 더 짧으면 준비 시작부터 끝까지.
            case TwentyThreeWindowKind.RaidRelay: length = Mathf.Min(data.relayCounterWindow, data.relayPrepare); break;
            case TwentyThreeWindowKind.RaidChain: length = data.chainCounterWindow; break;
            case TwentyThreeWindowKind.PushNorth: length = data.pushNorthStunTime; break;
            case TwentyThreeWindowKind.JumpLanding: length = LandingWindowLength(data); break;
            default: return InterruptWindowLengthResolver.Unknown;   // 잡기(애니 경과 필요 — GrabLength)·모름
        }

        return length > 0f ? length : InterruptWindowLengthResolver.Unknown;
    }

    /// <summary>
    /// C 착지 창 길이 — 서버는 이번 콤보 점프 타의 <c>landingCounterWindow</c> 만큼 착지 피해 직후 열고 그 시간(Time.time)에 닫는다.
    /// 어느 콤보·어느 타인지는 공개 상태로 모르므로(서버의 닫힘 시각도 private) 데이터의 점프 타 중 0 이 아닌 값이
    /// <b>한 가지뿐일 때만</b> 그 값, 서로 다른 값이 둘 이상이거나 하나도 없으면 모름. 가중치 0 콤보도 센다(스크립트 선택이 가중치를 안 본다).
    /// </summary>
    public static float LandingWindowLength(BossDataSO data)
    {
        if (data == null || data.combos == null)
            return InterruptWindowLengthResolver.Unknown;

        float found = InterruptWindowLengthResolver.Unknown;
        foreach (BossComboEntry combo in data.combos)
        {
            if (combo?.steps == null) continue;
            foreach (BossComboStep step in combo.steps)
            {
                // 보스는 점프 타의 값만 쓴다(TwentyThreeBoss.ConsumeCombo).
                if (step.attack != BossAttackId.Jump || step.landingCounterWindow <= 0f) continue;
                if (found < 0f) found = step.landingCounterWindow;
                else if (Mathf.Abs(found - step.landingCounterWindow) > 1e-4f) return InterruptWindowLengthResolver.Unknown;
            }
        }
        return found;
    }

    /// <summary>
    /// B 잡기 창 길이(게임초) — 창 = 붙잡은 순간(Catch 클립의 OnAttackHit ≈ 붙잡기 구간 끝) ~ 마지막 내려치기 시작.
    /// 서버 단계 길이는 전부 SO 값 ÷ <c>grabCycleSpeed</c> 이고 애니도 같은 배속이라, 지금 클립과 그 경과로 닫힘까지 남은 시간을 안다.
    /// 반환 = 열린 뒤 경과 + 남은 시간(해석기 계약 '이번 열림 전체 길이' — Catch 중이면 = 합 − 열릴 때의 Catch 경과).
    /// <list type="bullet">
    /// <item>Catch(붙잡기) — 남은 = catch + hold + 닫힘 전 타 수 × throw − Catch 경과. 정확.</item>
    /// <item>Shock(지짐이) — 남은 = hold + 닫힘 전 타 수 × throw − Shock 경과. 정확.</item>
    /// <item>Throw(내려치기) — 타마다 같은 클립을 처음부터 다시 틀어 몇 번째 타인지 애니로 모른다. 열림 ≈ Shock 시작(붙잡기 끝)으로 보고
    ///       (열린 뒤 경과 − hold − 이번 타 경과) ÷ throw 를 반올림해 지난 타 수를 추정한다 — 근사(열림·Shock 시작 차이 + 도착 흔들림이 throw/2 미만이면 맞다).</item>
    /// </list>
    /// 닫힘 전 타 수 = max(1, slamCount − 1) — 서버는 각 타가 끝날 때 남은 타가 1 이하면 닫으므로 1타 데이터는 그 1타 끝에 닫힌다.
    /// 성공·쓰러짐·중단 같은 조기 닫힘은 복제 bool 로만 안다. 경과를 모르거나(자세 정지·창 기록 없음) 클립을 못 가르면 모름.
    /// </summary>
    public static float GrabLength(in TwentyThreeWindowObservation obs, BossDataSO data)
    {
        if (data == null || obs.StateHash == 0 || obs.StateElapsed < 0f || obs.OpenElapsed < 0f)
            return InterruptWindowLengthResolver.Unknown;

        int catchHash = Hash(data.grabCatchState), holdHash = Hash(data.grabHoldState), throwHash = Hash(data.grabThrowState);
        // 단계 클립이 비었거나 겹치면(비우면 앞 클립 유지) 어느 단계인지 모른다.
        if (catchHash == 0 || holdHash == 0 || throwHash == 0 || catchHash == holdHash || holdHash == throwHash || catchHash == throwHash)
            return InterruptWindowLengthResolver.Unknown;

        // TwentyThreeBoss.ScaledGrab·GrabSlamCount 와 같은 식.
        float speed = Mathf.Clamp(data.grabCycleSpeed, 0.25f, 3f);
        float catchT = data.grabCatchDuration / speed;
        float holdT = data.grabHoldDuration / speed;
        float throwT = data.grabThrowDuration / speed;
        int throwsBeforeClose = Mathf.Max(1, Mathf.Max(1, data.grabSlamCount) - 1);

        int h = obs.StateHash;
        float remaining;
        if (h == catchHash)
            remaining = catchT + holdT + throwsBeforeClose * throwT - obs.StateElapsed;
        else if (h == holdHash)
            remaining = holdT + throwsBeforeClose * throwT - obs.StateElapsed;
        else if (h == throwHash)
        {
            if (throwT <= 0f) return InterruptWindowLengthResolver.Unknown;
            int done = Mathf.RoundToInt((obs.OpenElapsed - holdT - obs.StateElapsed) / throwT);
            done = Mathf.Clamp(done, 0, throwsBeforeClose - 1);
            remaining = (throwsBeforeClose - done) * throwT - obs.StateElapsed;
        }
        else
            return InterruptWindowLengthResolver.Unknown;

        float length = obs.OpenElapsed + Mathf.Max(0f, remaining);
        return length > 0f ? length : InterruptWindowLengthResolver.Unknown;
    }

    /// <summary>릴레이 준비 시작 → 창 열림 간격(초).</summary>
    public static float RelayOpenOffset(BossDataSO data) => Mathf.Max(0f, data.relayPrepare - data.relayCounterWindow);

    static BossAttackEntry DashEntry(BossDataSO data)
    {
        if (data.attacks == null) return null;
        foreach (BossAttackEntry e in data.attacks)
            if (e != null && e.attackId == BossAttackId.Dash) return e;
        return null;
    }

    static bool HasSequence(BossDataSO data, BossPhaseSequence sequence)
    {
        if (data.phases == null) return false;
        foreach (BossPhaseEntry p in data.phases)
            if (p != null && p.sequence == sequence) return true;
        return false;
    }

    static int Hash(string stateName) => string.IsNullOrEmpty(stateName) ? 0 : Animator.StringToHash(stateName);
}
