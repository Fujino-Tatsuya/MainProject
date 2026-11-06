using UnityEngine;

/// <summary>간파 스킬 캐릭터 VFX 하나(할 일 238). 목록·출처 = Docs/tech/interrupt-vfx-list.md. 순서 = 랩 화면 순서.</summary>
public enum InterruptVfx
{
    PaladinShieldMaterialGlow, // 방패 문양 발광(MaterialFadeEffect) — 시전 시작
    PaladinShieldGlowLoop,     // FX_ShieldGlow 루프 — 시전 내내
    PaladinShieldCharge,       // FX_ShieldCharge 기 모으기 — Garen_Parry 클립 이벤트
    PaladinShieldWave,         // FX_ShieldWave 충격파 — 간파 성공
    GunnerMuzzleBlast,         // FX_Arrow_hail 총구 폭발 — 판정 직후(빗나가도)
    AssassinWindup,            // FX_Assassin_RClick_Windup 기 모으기 — Attack_Up_01 클립 이벤트
    AssassinPop,               // FX_Assassin_RClick_Pop 튀기기 — 클립 이벤트
    AssassinSlash,             // FX_Assassin_RClick_Slash 올려치기 — 클립 이벤트
    AssassinSuccess,           // FX_Assassin_RClick_Success — 간파 성공
}

/// <summary>VFX 의 캐릭터 그룹(랩 화면 묶음).</summary>
public enum InterruptVfxCharacter
{
    Paladin,
    Gunner,
    Assassin,
}

/// <summary>
/// 간파 스킬 캐릭터 VFX 개별 켜고 끄기(할 일 238) — DebugInterruptLab 튜닝 패널이 바꾸고, 각 스폰 지점이 <see cref="IsOn"/> 한 곳만 묻는다.
///
/// <list type="bullet">
/// <item>기본 전부 켬. 끄면 그 VFX 재생만 건너뛴다 — 판정·슬로우·공용 간파 연출(디렉터)·다른 VFX 는 그대로.
///       끄는 쪽(Stop·FadeOut·회수)은 막지 않는다 — 켜진 채 남는 경로를 만들지 않는다.</item>
/// <item>로컬 상태다(복제 안 함) — 이 피어 화면만. 정적이라 저장하지 않는다(PlayerPrefs 없음) — Play 를 시작할 때마다 전부 켬으로 돌아간다.</item>
/// <item>에디터·개발 빌드에서만 동작한다(<see cref="GateActive"/>). 릴리스 빌드에선 <see cref="IsOn"/> 이 늘 true.</item>
/// <item>디렉터 칸(B1·B2·B3·C1·C2)과는 별개다 — 둘 다 켜져 있어야 나온다.</item>
/// </list>
/// </summary>
public static class InterruptVfxToggles
{
    /// <summary>게이트가 실제로 동작하는 빌드인가 — 아니면 끈 값도 무시(늘 켬).</summary>
    // 속성으로 둔다 — const 면 쓰는 쪽 분기가 '도달할 수 없는 코드' 경고(CS0162)가 된다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool GateActive => true;
#else
    public static bool GateActive => false;
#endif

    /// <summary>전체 목록(랩 화면 순서).</summary>
    public static readonly InterruptVfx[] All = (InterruptVfx[])System.Enum.GetValues(typeof(InterruptVfx));

    static readonly bool[] Off = new bool[All.Length]; // 기본 false = 켬

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => ResetAll();

    /// <summary>이 피어에서 그 VFX 를 낼까. 릴리스 빌드는 늘 true.</summary>
    public static bool IsOn(InterruptVfx vfx) => !GateActive || !IsOff(vfx);

    // 저장된 값(빌드 무관) — 랩 화면·테스트용.
    static bool IsOff(InterruptVfx vfx)
    {
        int i = (int)vfx;
        return i >= 0 && i < Off.Length && Off[i];
    }

    /// <summary>저장된 켬/끔 값(빌드 무관 — 랩 화면 표시용).</summary>
    public static bool Get(InterruptVfx vfx) => !IsOff(vfx);

    /// <summary>하나를 켜거나 끈다.</summary>
    public static void Set(InterruptVfx vfx, bool on)
    {
        int i = (int)vfx;
        if (i >= 0 && i < Off.Length)
            Off[i] = !on;
    }

    /// <summary>전부 켜거나 끈다.</summary>
    public static void SetAll(bool on)
    {
        for (int i = 0; i < Off.Length; i++)
            Off[i] = !on;
    }

    /// <summary>한 캐릭터 묶음을 켜거나 끈다.</summary>
    public static void SetCharacter(InterruptVfxCharacter character, bool on)
    {
        foreach (InterruptVfx vfx in All)
        {
            if (CharacterOf(vfx) == character)
                Set(vfx, on);
        }
    }

    /// <summary>기본값(전부 켬)으로.</summary>
    public static void ResetAll() => SetAll(true);

    /// <summary>그 VFX 의 캐릭터.</summary>
    public static InterruptVfxCharacter CharacterOf(InterruptVfx vfx) => vfx switch
    {
        InterruptVfx.GunnerMuzzleBlast => InterruptVfxCharacter.Gunner,
        InterruptVfx.AssassinWindup or InterruptVfx.AssassinPop or InterruptVfx.AssassinSlash or InterruptVfx.AssassinSuccess
            => InterruptVfxCharacter.Assassin,
        _ => InterruptVfxCharacter.Paladin,
    };

    /// <summary>
    /// 그 VFX 를 함께 막는 디렉터 칸(B1·B2·B3·C1·C2). 거너는 없음(false).
    /// 이 칸들은 11-06(할 일 238 사용자 결정)부터 NetworkManager.prefab 기본 켬(104~238 사이엔 꺼짐). 꺼져 있으면 이 토글이 켬이어도 안 나온다 — 랩은 켤 때 이 칸도 런타임으로 켠다.
    /// </summary>
    public static bool TryGetDirectorCue(InterruptVfx vfx, out InterruptProductionCue cue)
    {
        switch (vfx)
        {
            case InterruptVfx.PaladinShieldMaterialGlow:
            case InterruptVfx.PaladinShieldGlowLoop: cue = InterruptProductionCue.PaladinShieldGlow; return true;
            case InterruptVfx.PaladinShieldCharge: cue = InterruptProductionCue.PaladinShieldCharge; return true;
            case InterruptVfx.PaladinShieldWave: cue = InterruptProductionCue.PaladinWave; return true;
            case InterruptVfx.AssassinWindup:
            case InterruptVfx.AssassinPop:
            case InterruptVfx.AssassinSlash: cue = InterruptProductionCue.AssassinInsightAttempt; return true;
            case InterruptVfx.AssassinSuccess: cue = InterruptProductionCue.AssassinInsightSuccess; return true;
            default: cue = default; return false;
        }
    }

    /// <summary>이 피어에서 실제로 나오는가 — 이 토글 ∧ 디렉터 칸(있으면). 디렉터 없음 = 칸 꺼짐.</summary>
    public static bool IsEffective(InterruptVfx vfx, InterruptProductionDirector director) =>
        IsOn(vfx) && (!TryGetDirectorCue(vfx, out InterruptProductionCue cue) || InterruptProductionRules.IsOn(director, cue));

    /// <summary>
    /// [랩] 실제로 켜거나 끈다 — 켜면 이 토글과 디렉터 칸(런타임 값, 저장 안 함)을 함께 켠다. 끄면 이 토글만 끈다
    /// (디렉터 칸은 같은 칸의 다른 VFX 도 막으므로 건드리지 않는다).
    /// </summary>
    public static void SetEffective(InterruptVfx vfx, bool on, InterruptProductionDirector director)
    {
        Set(vfx, on);
        if (on && director != null && TryGetDirectorCue(vfx, out InterruptProductionCue cue))
            director.Set(cue, true);
    }

    /// <summary>클립 이벤트 이펙트 id → VFX(<see cref="EffectAnimEvents"/>). 간파 VFX id 가 아니면 false.</summary>
    public static bool TryGetAnimEffect(string id, out InterruptVfx vfx)
    {
        switch (id)
        {
            case InterruptProductionRules.PaladinShieldChargeId: vfx = InterruptVfx.PaladinShieldCharge; return true;
            case InterruptProductionRules.AssassinInsightWindupId: vfx = InterruptVfx.AssassinWindup; return true;
            case InterruptProductionRules.AssassinInsightPopId: vfx = InterruptVfx.AssassinPop; return true;
            case InterruptProductionRules.AssassinInsightSlashId: vfx = InterruptVfx.AssassinSlash; return true;
            default: vfx = default; return false;
        }
    }

    /// <summary>클립 이벤트 이펙트 재생 허용 — 간파 VFX id 가 아니면 늘 true.</summary>
    public static bool AllowsAnimEffect(string id) => !TryGetAnimEffect(id, out InterruptVfx vfx) || IsOn(vfx);

    /// <summary>캐릭터 묶음 제목.</summary>
    public static string CharacterLabel(InterruptVfxCharacter character) => character switch
    {
        InterruptVfxCharacter.Gunner => "거너",
        InterruptVfxCharacter.Assassin => "어쌔신",
        _ => "팔라딘",
    };

    /// <summary>랩 토글 글자.</summary>
    public static string Label(InterruptVfx vfx) => vfx switch
    {
        InterruptVfx.PaladinShieldMaterialGlow => "방패 문양 발광 (머티리얼)",
        InterruptVfx.PaladinShieldGlowLoop => "방패 발광 루프 FX_ShieldGlow",
        InterruptVfx.PaladinShieldCharge => "기 모으기 FX_ShieldCharge",
        InterruptVfx.PaladinShieldWave => "성공 충격파 FX_ShieldWave",
        InterruptVfx.GunnerMuzzleBlast => "총구 폭발 FX_Arrow_hail",
        InterruptVfx.AssassinWindup => "기 모으기 RClick_Windup",
        InterruptVfx.AssassinPop => "튀기기 RClick_Pop",
        InterruptVfx.AssassinSlash => "올려치기 RClick_Slash",
        InterruptVfx.AssassinSuccess => "성공 RClick_Success",
        _ => vfx.ToString(),
    };
}
