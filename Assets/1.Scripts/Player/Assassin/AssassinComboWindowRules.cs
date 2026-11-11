/// <summary>
/// 어쌔신 평타 다음 타 연결(예약 창 + 홀드)의 순수 규칙(할 일 47 연장·정정). Unity 의존 없이 EditMode 로 검증한다.
/// 창 자체는 클립 ComboWindowOpen~Close 이벤트다(가붕이와 같다, PLAN-anim-event-timing P2-1) — 여기는 End 순간 잇기 규칙만.
/// 오너 예측(할 일 256)부터 창·눌림 모두 오너 자기 것이다(서버 눌림 신호 없음).
/// </summary>
public static class AssassinComboWindowRules
{
    /// <summary>
    /// 한 타의 End 순간 다음 타로 잇는가(할 일 47 정정, 거너 기본 공격과 같은 규칙) — 창 안 클릭 예약이 있거나 지금 좌클릭이 눌려 있으면.
    /// 눌림은 "처음 누른 입력을 한 번도 떼지 않았는가"가 아니라 End 순간의 상태다 — 뗐다 다시 눌러 들고 있어도 잇는다.
    /// </summary>
    public static bool ShouldContinueAtEnd(bool queued, bool held)
    {
        return queued || held;
    }
}

/// <summary>
/// 어쌔신 평타 오너 예측(할 일 256)의 서버 확인 규칙 — 순수 함수, EditMode 대상(AssassinOwnerPredictionRulesTests).
/// 오너가 복제된 어쌔신 상태로 고른 모드(변신 &gt; 강화 &gt; 일반)를 서버가 자기 상태로 다시 골라 비교한다.
/// </summary>
public static class AssassinOwnerPredictionRules
{
    /// <summary>
    /// 서버가 오너의 모드를 받아들이는가. 서버가 시작할 수 없으면(변신 종료 대기) 거부, 같으면 수락.
    /// 오너 = 일반·서버 = 강화(서버가 막 강화 준비를 켰는데 복제가 아직 안 닿음)는 일반으로 수락 — 강화는 소모하지 않고 다음 타로 남는다.
    /// 그 밖의 어긋남(서버엔 강화 없음·변신 여부가 다름)은 거부 → 서버 끊기 경로.
    /// </summary>
    public static bool AcceptsClaimedMode(AssassinBasicAttackMode claimed, AssassinBasicAttackMode server, bool serverCanStart)
    {
        if (!serverCanStart)
            return false;

        if (claimed == server)
            return true;

        return claimed == AssassinBasicAttackMode.Normal && server == AssassinBasicAttackMode.Enhanced;
    }

    /// <summary>2차 검증의 단 수 — 일반 = 일반 타 수, 강타·변신 묶음 = 1단(같은 단을 다시 이으면 루프로 본다).</summary>
    public static int VerifyStepCount(AssassinBasicAttackMode mode, int normalStepCount) =>
        mode == AssassinBasicAttackMode.Normal ? normalStepCount : 1;

    /// <summary>
    /// 2차 검증에서 '이어지는 콤보'로 보는가 — 오너가 이었다고 보냈고, 직전 타와 모드가 같을 때만. 모드가 바뀌면(일반 → 강타 등) 새 콤보로
    /// 보아 순서는 0 부터, 첫 히트는 하한 없이 통과한다(모드마다 클립 시각표가 달라 간격을 잇지 않는다).
    /// </summary>
    public static bool ContinuesForVerify(bool ownerContinues, bool hasPreviousMode, AssassinBasicAttackMode previousMode,
        AssassinBasicAttackMode mode) =>
        ownerContinues && hasPreviousMode && previousMode == mode;
}
