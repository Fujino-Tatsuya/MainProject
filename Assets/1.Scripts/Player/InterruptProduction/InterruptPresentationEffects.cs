/// <summary>
/// 간파 연출 효과 하나(할 일 132 §17 튜닝 도구) — 디렉터의 효과별 켜기/끄기 칸 하나씩.
/// 끈 효과는 '그리기만 안 함' — 오너 상태 기계·적중 섬광 시간표·섬광 순간 사건(암전 걷힘 등)은 그대로 돈다.
/// 팀원 암순응은 여기 없다 — 기존 A2 칸(<see cref="InterruptProductionCue.DarkAdaptation"/>)이 그 켜기/끄기다(한 칸으로 정리).
/// 슬로우·줌·히트스톱은 연출 그림이 아니라 여기 없다(A1·A3 칸 그대로).
/// </summary>
public enum InterruptPresentationEffect
{
    /// <summary>오너 암전(마스크 암전 덮개). 기본 켬 — 나머지는 기본 끔.</summary>
    OwnerDim,
    /// <summary>빛 모임 VFX + 소켓 라이트(본인·팀원 화면).</summary>
    LightGather,
    /// <summary>② 스타버스트(렌즈 플레어 + 푸른 고리).</summary>
    Starburst,
    /// <summary>③ 화이트아웃(회청색 덮개 + 방사형 블러).</summary>
    Whiteout,
    /// <summary>④ 스미어(흰 덩어리 한 장).</summary>
    Smear,
    /// <summary>⑤ 색수차 블러(방사형 블러 + Volume 색수차 + 블룸).</summary>
    Aberration,
    /// <summary>⑥ 가라앉음(블룸·블러·색수차 감소).</summary>
    Settle,
    /// <summary>월드 섬광 파티클(모든 피어).</summary>
    WorldFlash,
    /// <summary>적중 지점 Point Light(모든 피어).</summary>
    HitLight,
}

/// <summary>효과별 켜기/끄기 칸 — 디렉터가 구현하고, 그릴 때마다 읽는다(캐싱 없음).</summary>
public interface IInterruptPresentationEffectSettings
{
    bool IsEffectOn(InterruptPresentationEffect effect);
}

/// <summary>효과 칸 순수 규칙(할 일 132 §17).</summary>
public static class InterruptPresentationEffectRules
{
    /// <summary>모든 효과 — 디버그 패널·테스트가 같은 목록을 쓴다.</summary>
    public static readonly InterruptPresentationEffect[] All =
        (InterruptPresentationEffect[])System.Enum.GetValues(typeof(InterruptPresentationEffect));

    /// <summary>기본값 — 오너 암전만 켬(§17-1).</summary>
    public static bool DefaultOn(InterruptPresentationEffect effect) => effect == InterruptPresentationEffect.OwnerDim;

    /// <summary>적중 섬광 단계 → 그 단계의 효과 칸. None 이면 false.</summary>
    public static bool TryGetStageEffect(InterruptHitFlashStage stage, out InterruptPresentationEffect effect)
    {
        switch (stage)
        {
            case InterruptHitFlashStage.Starburst: effect = InterruptPresentationEffect.Starburst; return true;
            case InterruptHitFlashStage.Whiteout: effect = InterruptPresentationEffect.Whiteout; return true;
            case InterruptHitFlashStage.Smear: effect = InterruptPresentationEffect.Smear; return true;
            case InterruptHitFlashStage.Aberration: effect = InterruptPresentationEffect.Aberration; return true;
            case InterruptHitFlashStage.Settle: effect = InterruptPresentationEffect.Settle; return true;
            default: effect = default; return false;
        }
    }

    /// <summary>그 단계를 그리는가 — 칸이 없으면(설정 없음) 그린다.</summary>
    public static bool StageVisible(IInterruptPresentationEffectSettings settings, InterruptHitFlashStage stage) =>
        settings == null || !TryGetStageEffect(stage, out InterruptPresentationEffect effect) || settings.IsEffectOn(effect);

    public static bool IsOn(IInterruptPresentationEffectSettings settings, InterruptPresentationEffect effect) =>
        settings == null || settings.IsEffectOn(effect);
}

/// <summary>
/// 오너 출력 문(할 일 132 §17) — 오너 상태 기계와 실제 출력(암전·빛 모임) 사이. 상태 기계는 그대로 값·사건을 넘기고,
/// 칸이 꺼져 있으면 문이 그리기만 막는다(암전 = 0 으로, 빛 모임 = 시작 안 함).
/// <list type="bullet">
/// <item>암전: 상태 기계가 넘긴 값을 들고 있다가, 칸이 Play 중에 바뀌면 <see cref="Sync"/>(디렉터 매 프레임)가 바로 맞춘다.</item>
/// <item>빛 모임: 칸은 시작 순간 읽는다(돌던 빛 모임은 끝까지 — 다음 시도부터). 정지는 늘 넘긴다.</item>
/// </list>
/// </summary>
public sealed class InterruptOwnerOutputGate : IInterruptOwnerDimOutput, IInterruptLightGatherOutput
{
    private readonly IInterruptPresentationEffectSettings _settings;
    private readonly IInterruptOwnerDimOutput _dim;
    private readonly IInterruptLightGatherOutput _gather;
    private float _sent;

    public InterruptOwnerOutputGate(IInterruptPresentationEffectSettings settings, IInterruptOwnerDimOutput dim,
        IInterruptLightGatherOutput gather)
    {
        _settings = settings;
        _dim = dim;
        _gather = gather;
    }

    /// <summary>상태 기계가 마지막으로 넘긴 암전 강도(칸과 무관).</summary>
    public float RequestedDim { get; private set; }

    public void SetOwnerDim(float intensity)
    {
        RequestedDim = intensity;
        Send(Visible(intensity));
    }

    /// <summary>칸 변경을 지금 반영(암전) — 바뀐 경우만 출력에 넘긴다.</summary>
    public void Sync()
    {
        float value = Visible(RequestedDim);
        if (value != _sent)
            Send(value);
    }

    public void StartLightGather(PlayerInterruptSkillBase skill)
    {
        if (InterruptPresentationEffectRules.IsOn(_settings, InterruptPresentationEffect.LightGather))
            _gather?.StartLightGather(skill);
    }

    public void StopLightGather() => _gather?.StopLightGather();

    private float Visible(float intensity) =>
        InterruptPresentationEffectRules.IsOn(_settings, InterruptPresentationEffect.OwnerDim) ? intensity : 0f;

    private void Send(float value)
    {
        _sent = value;
        _dim?.SetOwnerDim(value);
    }
}
