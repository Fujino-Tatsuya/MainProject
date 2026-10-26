using UnityEngine;

/// <summary>간파 연출 스위치 종류 — <see cref="InterruptPresentationToggles"/> 의 칸 하나씩(할 일 104).</summary>
public enum InterruptPresentationKind
{
    HitStop,                  // A1
    DarkAdaptation,           // A2
    OwnerZoom,                // A3
    SuccessFxSlot,            // A4
    PaladinShieldGlow,        // B1
    PaladinShieldCharge,      // B2
    PaladinWave,              // B3
    AssassinInsightAttempt,   // C1
    AssassinInsightSuccess,   // C2
    TargetFlash,              // E1
}

/// <summary>
/// 간파 연출 켜고 끄기(할 일 104). 슬로우 자체는 늘 켜져 있고, 그 위에 얹은 연출만 칸마다 끈다 — 기본 전부 꺼짐.
/// NetworkManager.prefab 의 <see cref="InterruptDarkAdaptation"/> 옆에 붙는다.
///
/// <list type="bullet">
/// <item>피어마다 자기 인스턴스 값을 읽는다(동기화 없음). 단 히트스톱은 서버가 이벤트를 만들 때만 보므로 서버 값이 전 피어에 간다.</item>
/// <item>인스턴스가 없으면(EditMode·NetworkManager 없는 씬) 전부 꺼짐.</item>
/// <item>재생 지점은 <see cref="IsOn(InterruptPresentationKind)"/> 를 확인만 한다 — 연출 로직·판정은 그대로.</item>
/// <item>클립 이벤트 이펙트(<see cref="EffectAnimEvents"/>)는 id 로 가린다 — 간파 id 는 이 클래스에만 둔다.</item>
/// </list>
/// 문서: Docs/tech/interrupt-presentation.md §7.
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptPresentationToggles : MonoBehaviour
{
    // 클립 이벤트 id — Player_Paladin.prefab · Player_Assassin.prefab(VFX/RightClick/*) 의 EffectSocketPlayer.id
    public const string PaladinShieldChargeId = "ShieldCharge";
    public const string AssassinInsightWindupId = "Assassin_InsightWindup";
    public const string AssassinInsightPopId = "Assassin_InsightPop";
    public const string AssassinInsightSlashId = "Assassin_InsightSlash";

    public static InterruptPresentationToggles Instance { get; private set; }

    [Header("공용 (A)")]
    [Tooltip("A1 히트스톱 — 간파 성공 슬로우 맨 앞의 짧은 정지. 서버 값만 쓴다(끄면 hitStopDuration 0 으로 전 피어에 실린다)")]
    [SerializeField] private bool hitStop;
    [Tooltip("A2 암순응 — 간파 성공 때 화면의 어두운 픽셀을 잠깐 낮춘다(InterruptDarkAdaptation)")]
    [SerializeField] private bool darkAdaptation;
    [Tooltip("A3 오너 줌 — 내가 일으킨 슬로우 동안 카메라가 다가간다(CameraTargetSwitcher). 끄면 배율 1")]
    [SerializeField] private bool ownerZoom;
    [Tooltip("A4 성공 이펙트 칸 — 간파 성공 때 Player 의 InterruptSuccessFx casterFx·targetFx 원샷")]
    [SerializeField] private bool successFxSlot;

    [Header("팔라딘 (B)")]
    [Tooltip("B1 방패 발광·루프 — 간파 시도(우클릭) 시작에 shieldGlow FadeIn·glowLoop Play. 끄는 쪽은 늘 돈다")]
    [SerializeField] private bool paladinShieldGlow;
    [Tooltip("B2 방패 기 모으기 — 간파 시도 클립(Garen_Parry) 이벤트의 ShieldCharge 이펙트")]
    [SerializeField] private bool paladinShieldCharge;
    [Tooltip("B3 충격파 — 간파 성공(방패 적중) 때 PlayerShieldVfx.interruptWave 원샷")]
    [SerializeField] private bool paladinWave;

    [Header("어쌔신 (C)")]
    [Tooltip("C1 기 모으기·튀기기·올려치기 — 간파 시도 클립(Attack_Up_01) 이벤트의 Assassin_InsightWindup/Pop/Slash 이펙트")]
    [SerializeField] private bool assassinInsightAttempt;
    [Tooltip("C2 간파 성공 이펙트 — 간파 성공 때 올려치기 프레임에 AssassinSkillView.insightSuccess 원샷(C1 을 꺼도 같은 프레임에 나온다)")]
    [SerializeField] private bool assassinInsightSuccess;

    [Header("맞은 쪽 (E)")]
    [Tooltip("E1 섬광 Interrupt_Flash — 간파당한 보스·엘리트·허수아비·스파링 봇 몸에서 터지는 섬광(판정·그로기는 그대로)")]
    [SerializeField] private bool targetFlash;

    private void Awake()
    {
        if (Instance != null && Instance != this)
            return;

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool Get(InterruptPresentationKind kind)
    {
        switch (kind)
        {
            case InterruptPresentationKind.HitStop: return hitStop;
            case InterruptPresentationKind.DarkAdaptation: return darkAdaptation;
            case InterruptPresentationKind.OwnerZoom: return ownerZoom;
            case InterruptPresentationKind.SuccessFxSlot: return successFxSlot;
            case InterruptPresentationKind.PaladinShieldGlow: return paladinShieldGlow;
            case InterruptPresentationKind.PaladinShieldCharge: return paladinShieldCharge;
            case InterruptPresentationKind.PaladinWave: return paladinWave;
            case InterruptPresentationKind.AssassinInsightAttempt: return assassinInsightAttempt;
            case InterruptPresentationKind.AssassinInsightSuccess: return assassinInsightSuccess;
            case InterruptPresentationKind.TargetFlash: return targetFlash;
            default: return false;
        }
    }

    /// <summary>이 피어의 스위치. 인스턴스가 없으면 꺼짐.</summary>
    public static bool IsOn(InterruptPresentationKind kind) => IsOn(Instance, kind);

    public static bool IsOn(InterruptPresentationToggles toggles, InterruptPresentationKind kind) =>
        toggles != null && toggles.Get(kind);

    /// <summary>A1 — 서버가 이벤트에 실을 히트스톱 길이. 꺼지면 0(배율·나머지 단계는 그대로).</summary>
    public static float SelectHitStopDuration(bool hitStopOn, float hitStopDuration) => hitStopOn ? hitStopDuration : 0f;

    /// <summary>클립 이벤트 id 가 간파 연출이면 그 스위치 종류. 다른 id 는 false.</summary>
    public static bool TryGetAnimEffectKind(string id, out InterruptPresentationKind kind)
    {
        switch (id)
        {
            case PaladinShieldChargeId:
                kind = InterruptPresentationKind.PaladinShieldCharge;
                return true;
            case AssassinInsightWindupId:
            case AssassinInsightPopId:
            case AssassinInsightSlashId:
                kind = InterruptPresentationKind.AssassinInsightAttempt;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>B2·C1 — 클립 이벤트 재생 허용. 간파 id 가 아니면 늘 true.</summary>
    public static bool AllowsAnimEffect(string id) => AllowsAnimEffect(Instance, id);

    public static bool AllowsAnimEffect(InterruptPresentationToggles toggles, string id) =>
        !TryGetAnimEffectKind(id, out InterruptPresentationKind kind) || IsOn(toggles, kind);
}
