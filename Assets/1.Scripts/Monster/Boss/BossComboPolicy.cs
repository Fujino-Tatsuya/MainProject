/// <summary>
/// 23호 콤보 판정(PLAN-boss-combo §3.2~3.3) — 순수 규칙. EditMode 로 경계를 고정한다.
///
/// 🔴 기존 각본 소비(<c>TryScripted</c>)를 쓰지 않는 이유(Codex 교차검증 10-18): 그쪽은 "간격 대기"와 "거리 이탈"을
///    둘 다 Wait 로 돌려 큐를 들고 추격한다. 콤보는 간격이면 기다리고, 거리 이탈이면 짧은 추격 유예 뒤 **버린다**(팀장 10-18·19).
///    단, 개전 콤보의 첫 타는 아직 아무것도 안 나갔으므로 예전 ① 각본처럼 기다리며 다가간다.
/// </summary>
public static class BossComboPolicy
{
    public enum StepGate { Wait, Fire, Drop }

    /// <summary>룰렛 후보가 되는가 — 첫 타의 게이트(거리창·행 쿨다운)는 호출측이 단타 규칙으로 계산해 넘긴다.</summary>
    public static bool IsCandidate(float weight, int currentPhase, int allowedFromPhase,
                                   float secondsSinceComboUsed, float comboCooldown, bool firstStepGatePassed)
    {
        if (weight <= 0f) return false;
        if (currentPhase < allowedFromPhase) return false;
        if (secondsSinceComboUsed < comboCooldown) return false;
        return firstStepGatePassed;
    }

    /// <summary>다음 타를 낼까.</summary>
    /// <param name="started">이미 한 타 이상 나갔는가. 개전 콤보 첫 타만 false — 이 경우 거리 이탈은 버림이 아니라 대기(추격).</param>
    /// <param name="lockValid">고정 대상이 살아 있고 지금 어그로 대상과 같은가(시작 전이면 true 로 넘긴다).</param>
    /// <param name="sinceLastAttackEnd">직전 공격 종료 후 경과(초).</param>
    /// <param name="interval">타 사이 간격(초).</param>
    /// <param name="inWindow">이 타의 거리창 안인가(거리창 무시 행이면 true).</param>
    /// <param name="forceFire">사거리 무시하고 무조건 시전하는 타인가(돌진 → 잡기, 팀장 10-19).</param>
    /// <param name="chaseElapsed">이 타에서 사거리 밖이 된 뒤 경과(초) — 사거리 안이었으면 0.</param>
    /// <param name="chaseGrace">사거리 밖일 때 다가가며 기다려 주는 시간(초, 팀장 10-19: 1초). 넘기면 버린다.</param>
    public static StepGate NextStep(bool started, bool lockValid, float sinceLastAttackEnd, float interval,
                                    bool inWindow, bool forceFire = false,
                                    float chaseElapsed = 0f, float chaseGrace = 0f)
    {
        if (started && !lockValid) return StepGate.Drop;
        if (sinceLastAttackEnd < interval) return StepGate.Wait;
        if (forceFire || inWindow) return StepGate.Fire;
        if (!started) return StepGate.Wait;                                     // 개전 첫 타 — 다가가서 쓴다
        return chaseElapsed < chaseGrace ? StepGate.Wait : StepGate.Drop;       // 짧은 추격 유예 뒤 버림
    }

    // ─── 즉시 연결(PLAN-boss-combo-chain, 팀장 10-19) ─────────────────────────

    /// <summary>다음 타까지 기다릴 간격(초). 즉시 연결이면 0 — 앞 타가 맞히는 순간 넘어간다.</summary>
    public static float StepInterval(bool chainOnHit, float normalInterval) => chainOnHit ? 0f : normalInterval;

    /// <summary>다음 타를 사거리 검사 없이 낼까. 즉시 연결이면 회피해도 친다(첫 타 방향 그대로).</summary>
    public static bool IgnoresRange(bool chainOnHit, bool forceFire) => chainOnHit || forceFire;

    /// <summary>
    /// 지금 공격이 끝나는 순간 **다음 타로 바로 넘길** 콤보 타인가.
    /// <paramref name="nextIndex"/> = 아직 안 나간 다음 타의 인덱스(진행 중 콤보의 _comboIndex).
    /// </summary>
    public static bool ShouldChain(bool comboActive, bool chainOnHit, int nextIndex, int stepCount) =>
        comboActive && chainOnHit && nextIndex > 0 && nextIndex < stepCount;

    /// <summary>훅·어퍼인가 — 즉시 연결에서 예고 없이 첫 타 방향으로 바로 치는 타이자, 바닥 예고 토글 대상.</summary>
    public static bool IsHookOrUpper(BossAttackId id) =>
        id == BossAttackId.LeftHook || id == BossAttackId.RightHook || id == BossAttackId.Upper;
}
