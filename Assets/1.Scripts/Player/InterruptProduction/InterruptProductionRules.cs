/// <summary>
/// 간파 연출 디렉터의 판단만 모은 순수 규칙(할 일 118) — 칸 on/off, 히트스톱 길이, 클립 이벤트 id → cue, 보는 사람.
/// 상태가 없고 디렉터 인스턴스를 인자로 받는다(null = 인스턴스 없음 = 전부 꺼짐). EditMode 테스트 대상.
/// </summary>
public static class InterruptProductionRules
{
    // 클립 이벤트 id — Player_Paladin.prefab · Player_Assassin.prefab(VFX/RightClick/*) 의 EffectSocketPlayer.id
    public const string PaladinShieldChargeId = "ShieldCharge";
    public const string AssassinInsightWindupId = "Assassin_InsightWindup";
    public const string AssassinInsightPopId = "Assassin_InsightPop";
    public const string AssassinInsightSlashId = "Assassin_InsightSlash";

    /// <summary>그 디렉터의 칸. 디렉터가 없으면 꺼짐.</summary>
    public static bool IsOn(InterruptProductionDirector director, InterruptProductionCue cue) =>
        director != null && director.Get(cue);

    /// <summary>A1 — 서버가 이벤트에 실을 히트스톱 길이. 꺼지면 0(배율·나머지 단계는 그대로).</summary>
    public static float SelectHitStopDuration(bool hitStopOn, float hitStopDuration) => hitStopOn ? hitStopDuration : 0f;

    /// <summary>클립 이벤트 id 가 간파 연출이면 그 cue. 다른 id 는 false.</summary>
    public static bool TryGetAnimEffectCue(string id, out InterruptProductionCue cue)
    {
        switch (id)
        {
            case PaladinShieldChargeId:
                cue = InterruptProductionCue.PaladinShieldCharge;
                return true;
            case AssassinInsightWindupId:
            case AssassinInsightPopId:
            case AssassinInsightSlashId:
                cue = InterruptProductionCue.AssassinInsightAttempt;
                return true;
            default:
                cue = default;
                return false;
        }
    }

    /// <summary>시전 시작(OnClientPlay) 시도 cue — 팔라딘 방패 발광(B1)만. 어쌔신·거너는 없다(어쌔신 시도 연출은 클립 이벤트 C1).</summary>
    public static bool TryGetAttemptCue(PlayerInterruptSkillBase skill, out InterruptProductionCue cue)
    {
        if (skill is FirstMeleeInterruptSkill)
        {
            cue = InterruptProductionCue.PaladinShieldGlow;
            return true;
        }

        cue = default;
        return false;
    }

    /// <summary>B2·C1 — 클립 이벤트 재생 허용. 간파 id 가 아니면 늘 true.</summary>
    public static bool AllowsAnimEffect(InterruptProductionDirector director, string id) =>
        !TryGetAnimEffectCue(id, out InterruptProductionCue cue) || IsOn(director, cue);

    /// <summary>A3 — 오너 줌 배율. 칸이 꺼져 있으면 1(줌 없음).</summary>
    public static float OwnerZoomFactor(InterruptProductionDirector director, float zoomFactor) =>
        IsOn(director, InterruptProductionCue.OwnerZoom) ? zoomFactor : 1f;

    /// <summary>
    /// 시도 슬로우 강·약(할 일 132 정정1) — 성공 예측 = 강(<see cref="SlowMotionPurpose.Attempt"/>), 실패 예측 = 약(<see cref="SlowMotionPurpose.AttemptWeak"/>).
    /// 대상 없음이면 false — 시도 슬로우를 걸지 않는다(단계1 이전과 같다).
    /// </summary>
    public static bool TrySelectAttemptPurpose(BeaverLobby.Player.Dash.InterruptAttemptForecast forecast, out SlowMotionPurpose purpose)
    {
        switch (forecast)
        {
            case BeaverLobby.Player.Dash.InterruptAttemptForecast.Strong:
                purpose = SlowMotionPurpose.Attempt;
                return true;
            case BeaverLobby.Player.Dash.InterruptAttemptForecast.Weak:
                purpose = SlowMotionPurpose.AttemptWeak;
                return true;
            default:
                purpose = SlowMotionPurpose.Attempt;
                return false;
        }
    }

    /// <summary>시도 목적에 맞는 슬로우 프로필 — 약이면 약 프로필, 그 밖은 강(옛 시도) 프로필.</summary>
    public static SlowMotionProfile SelectAttemptProfile(SlowMotionPurpose purpose, IInterruptProductionSettings settings) =>
        purpose == SlowMotionPurpose.AttemptWeak ? settings.InterruptWeakProfile : settings.InterruptProfile;

    /// <summary>
    /// 지금 재생 중인 슬로우 목적별 오너 줌 배율(할 일 132) — 강 시도 / 약 시도 / 성공(카메라 <c>slowMotionZoomFactor</c>).
    /// A3 칸 on/off 는 따로(<see cref="OwnerZoomFactor"/>) 본다.
    /// </summary>
    public static float SelectOwnerZoomFactor(SlowMotionPurpose purpose, float strongZoomFactor, float weakZoomFactor, float successZoomFactor)
    {
        switch (purpose)
        {
            case SlowMotionPurpose.Attempt: return strongZoomFactor;
            case SlowMotionPurpose.AttemptWeak: return weakZoomFactor;
            default: return successZoomFactor;
        }
    }

    /// <summary>이 화면이 간파한 본인인가 — 개인 채널 기준.</summary>
    public static bool IsCasterView(ulong casterClientId, ulong localClientId) => casterClientId == localClientId;

    /// <summary>A4 — 목적·발동자·그 플레이어의 OwnerClientId 로 성공 이펙트 칸 재생 여부를 정한다(옛 InterruptSuccessFx.ShouldPlay).</summary>
    public static bool ShouldPlaySuccessFx(SlowMotionPurpose purpose, ulong triggerClientId, bool isSpawned, ulong ownerClientId) =>
        purpose == SlowMotionPurpose.Success && isSpawned && triggerClientId == ownerClientId;

    /// <summary>A4 — 재생해야 하면 연결된 칸만 고른다. 둘 다 비면 None(옛 InterruptSuccessFx.SelectSlots).</summary>
    public static InterruptSuccessFx.Slots SelectSuccessFxSlots(bool shouldPlay, bool hasCasterFx, bool hasTargetFx)
    {
        if (!shouldPlay)
            return InterruptSuccessFx.Slots.None;

        InterruptSuccessFx.Slots slots = InterruptSuccessFx.Slots.None;
        if (hasCasterFx)
            slots |= InterruptSuccessFx.Slots.Caster;
        if (hasTargetFx)
            slots |= InterruptSuccessFx.Slots.Target;
        return slots;
    }
}
