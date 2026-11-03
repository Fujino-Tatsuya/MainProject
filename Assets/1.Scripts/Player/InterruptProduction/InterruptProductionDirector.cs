using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

/// <summary>
/// 간파 연출 디렉터(할 일 118) — 간파 연출의 단일 진입점. NetworkManager.prefab 의 <see cref="GlobalTimeScale"/> 옆에 붙는다.
/// 104 스위치 10칸(<see cref="InterruptProductionCue"/>) + A5 전투 표시 숨김(154·179 후속 2)을 들고, 재생 지점은 정적 API 로 묻는다.
///
/// <list type="bullet">
/// <item>사건이 올 때마다 필드를 읽는다(캐싱 없음) — Play 중 인스펙터에서 바꾸면 다음 사건부터 바로 쓰인다.</item>
/// <item>피어마다 자기 인스턴스 값을 읽는다(동기화 없음). 단 히트스톱은 서버가 이벤트를 만들 때만 보므로 서버 값이 전 피어에 간다.</item>
/// <item>인스턴스가 없으면(EditMode·NetworkManager 없는 씬) 전부 꺼짐.</item>
/// <item>판정·피해·카운터 창에는 손대지 않는다 — 연출 재생 여부만 정한다.</item>
/// <item>시도·성공 슬로우 프로필·시도 줌 배율은 SO <see cref="InterruptSlowMotionSettings"/> 가 든다(194 후속 2 — 필드 이름 그대로, 테이블 대상 열 = 그 에셋).
///       디렉터는 참조만 들고 사건마다 읽는다. 서버 인스턴스 값만 쓰인다(서버가 이벤트에 실어 보낸다). 스위치 bool 은 테이블에 내지 않는다(프리팹이 원본).</item>
/// </list>
/// 문서: Docs/tech/interrupt-presentation.md.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class InterruptProductionDirector : MonoBehaviour, IInterruptProductionSettings, IInterruptOwnerPresentationSettings,
    IInterruptOwnerDimLook, IInterruptHitFlashSettings, IInterruptHitFlashScreenSettings, IInterruptHitLightScene
{
    public static InterruptProductionDirector Instance { get; private set; }

    /// <summary>
    /// [각 피어] 간파 성공 사건 — 성공 슬로우가 이 피어에서 시작됐다. 칸(스위치)과 무관하게 매번 낸다.
    /// 연출 밖 구독자(디버그 씬 <c>DebugTimeScale</c> 의 성공 후 1배속)용이다 — 새 연출은 여기 말고 칸(cue)으로 붙인다.
    /// </summary>
    public static event System.Action<InterruptProductionEvent> LocalSucceeded;

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 구독자가 남지 않게 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticEvents()
    {
        LocalSucceeded = null;
        DebugSuppressForecastOverride = null;
    }

    /// <summary>
    /// [디버그 — DebugInterruptLab 전용, 할 일 192] 대상의 제압 예측을 덮어쓴다(허수아비엔 제압 칸이 없어 '허수아비 간파 = 제압 간파' 재현 때 오너 암전 예측도 맞춘다).
    /// true 를 돌려주면 제압으로 예측한다. null = 없음(런타임 기본). 랩 패널만 쓴다.
    /// </summary>
    public static System.Func<GameObject, bool> DebugSuppressForecastOverride;

    // 슬로우 3단계 수치(194 후속 2) — ① 대쉬 간파 시도 · ② 간파 준비 · ③ 간파 적중. 옛 이 자리 칸을 이름 그대로 옮겼다(테이블 대상 열 = SO 에셋).
    // 비면 코드 기본값(InterruptSlowMotionSettings.OrDefault).
    [Header("슬로우 3단계 수치(194 후속 2)")]
    [Tooltip("슬로우 SO(InterruptSlowMotionSettings) — 대쉬 간파 시도·간파 준비 강/약 프로필·시도 줌·간파 적중 프로필. 사건마다 읽는다. 비면 코드 기본값")]
    [DataTableIgnore, SerializeField] private InterruptSlowMotionSettings slowMotionSettings;

    [Header("시도 — 공용")]
    [Tooltip("B1 방패 발광·루프 — 간파 시도(우클릭) 시작에 shieldGlow FadeIn·glowLoop Play. 끄는 쪽은 늘 돈다")]
    [DataTableIgnore, SerializeField] private bool paladinShieldGlow;
    [Tooltip("B2 방패 기 모으기 — 간파 시도 클립(Garen_Parry) 이벤트의 ShieldCharge 이펙트")]
    [DataTableIgnore, SerializeField] private bool paladinShieldCharge;
    [Tooltip("C1 기 모으기·튀기기·올려치기 — 간파 시도 클립(Attack_Up_01) 이벤트의 Assassin_InsightWindup/Pop/Slash 이펙트")]
    [DataTableIgnore, SerializeField] private bool assassinInsightAttempt;

    [Header("성공 — 공용")]
    [Tooltip("A1 히트스톱 — 간파 성공 슬로우 맨 앞의 짧은 정지. 서버 값만 쓴다(끄면 hitStopDuration 0 으로 전 피어에 실린다)")]
    [DataTableIgnore, SerializeField] private bool hitStop;
    [Tooltip("A4 성공 이펙트 칸 — 간파 성공 때 Player 의 InterruptSuccessFx casterFx·targetFx 원샷")]
    [DataTableIgnore, SerializeField] private bool successFxSlot;
    [Tooltip("B3 충격파 — 간파 성공(방패 적중) 때 PaladinSkillView.interruptWave 원샷")]
    [DataTableIgnore, SerializeField] private bool paladinWave;
    [Tooltip("C2 간파 성공 이펙트 — 간파 성공 때 올려치기 프레임에 AssassinSkillView.insightSuccess 원샷(C1 을 꺼도 같은 프레임에 나온다)")]
    [DataTableIgnore, SerializeField] private bool assassinInsightSuccess;
    [Tooltip("E1 섬광 Interrupt_Flash — 간파당한 보스·엘리트·허수아비·스파링 봇 몸에서 터지는 섬광(판정·그로기는 그대로)")]
    [DataTableIgnore, SerializeField] private bool targetFlash;
    [Tooltip("A2 암순응(= 팀원 암순응 켜기/끄기, 할 일 132 §17) — 섬광 순간 화면의 어두운 픽셀을 잠깐 낮춘다(InterruptDarkAdaptation). " +
             "다른 화면 = 약하게, 본인 화면 = 오너 화면 섬광이 안 돈 성공(약 예측)만 강하게")]
    [DataTableIgnore, SerializeField] private bool darkAdaptation;
    [Tooltip("A6 제압 간파 섬광 시퀀스(할 일 179) — 켜면 제압 간파 성공 때 일반 성공 섬광 ②~⑥ 대신 애니메이션 섬광 시퀀스(모든 피어). " +
             "끄면 제압 간파도 일반 간파와 같은 연출(②~⑥·오너 암전 규칙 그대로). 성공 시작 순간 읽는다 — 도는 시퀀스는 끝까지. 섬광 줄이기와 독립")]
    [DataTableIgnore, SerializeField] private bool suppressFlash = true;
    [Tooltip("A7 섬광 = 대상 내부만(179/192 후속) — 켜면 제압 간파 섬광 시퀀스를 간파 대상(보스·허수아비) 실루엣 안에만 그리고 바깥은 원래 화면(오너 암전 중이면 암전 그대로). " +
             "끄면 전체 화면. 재생 시작 순간 읽는다 — 도는 시퀀스는 끝까지. 이 화면에서 대상을 못 찾으면 전체 화면")]
    [DataTableIgnore, SerializeField] private bool suppressFlashTargetOnly;

    [Header("성공 — 개인")]
    [Tooltip("A3 오너 줌 — 내가 일으킨 슬로우 동안 카메라가 다가간다(CameraTargetSwitcher). 끄면 배율 1")]
    [DataTableIgnore, SerializeField] private bool ownerZoom;
    [Tooltip("A5 전투 표시 숨김(할 일 154 → 179 후속 2·4) — 내 강 시도 암전 시작 ~ 성공 섬광 ⑥ 끝(실패면 암전 걷기 끝) + 제압 간파 섬광 시퀀스(팀원 화면 포함) 동안 " +
             "이 화면의 피해 숫자(보스 포함)·플레이어·몬스터·보스 머리 위 체력바·오너 조준 표시를 숨긴다(화면 고정 보스 HP 바는 그대로). 생성·수명·게이지 값은 그대로(가리기만), 끝나면 그대로 보인다")]
    [DataTableIgnore, SerializeField, FormerlySerializedAs("hideDamageNumbers")] private bool hideCombatUi = true;

    // 연출 켜기/끄기(할 일 132 §17) — 끈 효과는 그리기만 안 한다. 오너 상태 기계·섬광 시간표·섬광 순간 사건(암전 걷힘 등)은 그대로 돈다.
    // 기본 = 오너 암전만 켬. 팀원 암순응 = 위 A2 칸, 슬로우·줌·히트스톱 = A1·A3·프로필 그대로. 이 피어 값을 쓴다(그릴 때마다 읽음).
    // DebugInterruptLab 의 튜닝 패널(DebugInterruptTuningPanel)이 Play 중 런타임 값만 바꾼다.
    [Header("연출 켜기/끄기 — 그리기만(할 일 132 §17)")]
    [Tooltip("오너 암전(마스크 암전 덮개) 그림. 끄면 암전 상태 기계는 그대로 돌고 덮개만 0")]
    [DataTableIgnore, SerializeField] private bool showOwnerDim = true;
    [Tooltip("빛 모임 VFX + 소켓 라이트(본인·팀원 화면). 시작 순간 읽는다 — 돌던 빛 모임은 끝까지")]
    [DataTableIgnore, SerializeField] private bool showLightGather;
    [Tooltip("② 스타버스트(렌즈 플레어 + 푸른 고리). 끄면 그 프레임 동안 아무것도 안 그린다(시간표 그대로)")]
    [DataTableIgnore, SerializeField] private bool showStarburst;
    [Tooltip("③ 화이트아웃(회청색 덮개 + 방사형 블러)")]
    [DataTableIgnore, SerializeField] private bool showWhiteout;
    [Tooltip("④ 스미어(흰 덩어리 한 장)")]
    [DataTableIgnore, SerializeField] private bool showSmear;
    [Tooltip("⑤ 색수차 블러(방사형 블러 + Volume 색수차 + 블룸)")]
    [DataTableIgnore, SerializeField] private bool showAberration;
    [Tooltip("⑥ 가라앉음(블룸·블러·색수차 감소)")]
    [DataTableIgnore, SerializeField] private bool showSettle;
    [Tooltip("월드 섬광 파티클(모든 피어, 섬광 순간)")]
    [DataTableIgnore, SerializeField] private bool showWorldFlash;
    [Tooltip("적중 지점 Point Light(모든 피어, 섬광 순간)")]
    [DataTableIgnore, SerializeField] private bool showHitLight;

    // 오너 암전·빛 모임(할 일 132) — 오너 클라가 입력 순간 예측해 바로 켜고, 서버 결과로 걷거나 성공에 넘긴다. 이 피어 값을 쓴다.
    // 데이터 테이블 열 후보지만 아직 xlsx 칸이 없어 [DataTableIgnore] — 프리팹이 원본.
    [Header("시도·결론 — 개인(오너 암전·빛 모임, 할 일 132)")]
    [Tooltip("강 시도(성공 예측) 입력 순간 오너 암전이 0 → 1 로 들어오는 시간(초, 실시간)")]
    [DataTableIgnore, SerializeField, Min(0f)] private float ownerDimInDuration = 0.1f;
    [Tooltip("예측 빗나감(서버 목적이 다름)·서버 실패·판정 전 종료 때 오너 암전을 걷는 시간(초, 실시간). 빛 모임은 그 순간 멈춘다")]
    [DataTableIgnore, SerializeField, Min(0f)] private float ownerDimOutDuration = 0.2f;
    [Tooltip("오너 암전 덮개 색(RGB만 쓴다) — 남길 것(오너·간파 대상·잔상·빛 모임·오너 무기 이펙트) 밖의 월드·HUD 를 덮는다. 예: 검정, 남색")]
    [DataTableIgnore, SerializeField] private Color ownerDimColor = Color.black;
    [Tooltip("오너 암전 덮개 불투명도 [0, 1] — 1 = 100%(완전히 덮음), 0.9 = 90%")]
    [DataTableIgnore, SerializeField, Range(0f, 1f)] private float ownerDimOpacity = 1f;

    // 적중 섬광(할 일 132 §15·§17) — 성공 Started 순간(히트스톱 안) ② 시작. ②③④ = 렌더 프레임 수(§17-3), 그 끝 프레임 = 섬광 순간,
    // ⑤⑥ = 시간 기준 칸의 시계(기본 실시간, §14) 초. 이 피어 값을 쓴다. 연출 길이라 애니 이벤트로 대체할 스킬 타이밍이 아니다(계획서 확정).
    // xlsx 칸이 아직 없어 [DataTableIgnore] — 프리팹이 원본. 화면 효과(②~⑥)는 오너만, 팀원은 섬광 순간에 월드 섬광 + 암순응.
    [Header("성공 — 적중 섬광(할 일 132 §15·§17)")]
    [Tooltip("⑤⑥ 시계 — 실시간(기본): 슬로우·히트스톱과 무관 / 스케일: 성공 슬로우를 따라 느려진다. ②③④·섬광 순간은 프레임 수라 이 칸을 타지 않는다. 진행 중에 바꾸면 다음 성공부터")]
    [DataTableIgnore, SerializeField] private InterruptEffectTimeBase hitFlashTimeBase = InterruptEffectTimeBase.Unscaled;
    [Tooltip("② 스타버스트 — 오너 암전 위 베이지 렌즈 플레어(부드러운 코어 + 제각각 빛살) + 얇은 푸른 고리(오너 화면, 렌더 프레임 수). 0 = 건너뜀")]
    [DataTableIgnore, SerializeField, Min(0)] private int hitFlashStarburstFrames = 2;
    [Tooltip("③ 화이트아웃 — 화면 전체 밝은 회청색(오너·대상 블러 실루엣이 비침) + 강한 방사형 블러(오너 화면, 렌더 프레임 수). 불투명도·실루엣은 오버레이 재질 칸. 0 = 건너뜀")]
    [DataTableIgnore, SerializeField, Min(0)] private int hitFlashWhiteoutFrames = 2;
    [Tooltip("④ 스미어 — 검정 위 오너·대상·빔이 커진 흰 덩어리로 공격 방향(시전/대시 방향 화면 투영)으로 늘어난 한 장(오너 화면, 렌더 프레임 수). 끝 프레임 = 섬광 순간: 오너 암전 끔·월드 섬광·라이트·팀원 암순응. 0 = 건너뜀")]
    [DataTableIgnore, SerializeField, Min(0)] private int hitFlashSmearFrames = 2;
    [Tooltip("⑤ 색수차 블러 — 실제 화면 위 방사형 블러 + Volume 색수차(가장자리) + 블룸 최대(오너 화면, 초, 진행기 시계). 0 = 건너뜀")]
    [DataTableIgnore, SerializeField, Min(0f)] private float hitFlashAberrationDuration = 0.1f;
    [Tooltip("⑥ 가라앉음 — 블룸 덩어리가 가라앉고 블러·색수차 0 으로(오너 화면, 초, 진행기 시계). 0 = 바로 끊김")]
    [DataTableIgnore, SerializeField, Min(0f)] private float hitFlashSettleDuration = 0.2f;
    [Tooltip("오너 화면 섬광 오버레이 재질(BeaverLobby/UI/InterruptHitFlashOverlay) — 색·단계별 덮어쓸 텍스처 칸은 재질에. 비면 오버레이 그림 없음")]
    [DataTableIgnore, SerializeField] private Material hitFlashOverlayMaterial;
    [Tooltip("② 렌즈 플레어 모양(URP Lens Flare (SRP) 에셋) — 아나모픽 가로 빛살 최장·베이지. 비면 플레어 없음")]
    [DataTableIgnore, SerializeField] private LensFlareDataSRP hitFlashLensFlare;
    [Tooltip("② 렌즈 플레어 세기(LensFlareComponentSRP.intensity)")]
    [DataTableIgnore, SerializeField, Min(0f)] private float hitFlashFlareIntensity = 1f;
    [Tooltip("② 렌즈 플레어 크기(LensFlareComponentSRP.scale)")]
    [DataTableIgnore, SerializeField, Min(0f)] private float hitFlashFlareScale = 1f;
    [Tooltip("방사형 블러 최대 세기 [0, 1] — ③ 에서 이 값, ⑤ 0.8→0.6배, ⑥ 0.6배→0. 픽셀에서 적중 지점까지 거리 중 번지는 비율")]
    [DataTableIgnore, SerializeField, Range(0f, 1f)] private float hitFlashBlurStrength = 0.6f;
    [Tooltip("⑤⑥ 색수차 Volume 프로필 — 이 피어 전용 전역 Volume(우선순위 1000)의 가중치만 움직인다(에셋 값은 그대로). 비면 색수차 없음")]
    [DataTableIgnore, SerializeField] private VolumeProfile hitFlashAberrationProfile;
    [Tooltip("⑤⑥ 블룸 Volume 프로필 — 가중치만큼 씬 블룸을 이 값 쪽으로 올린다(섬광 순간 가중치 1 — 세기는 프로필 intensity). 비면 블룸 상승 없음")]
    [DataTableIgnore, SerializeField] private VolumeProfile hitFlashBloomProfile;
    [Tooltip("섬광 순간(⑤ 시작) 적중 지점 월드 섬광(부드러운 파랑·흰 빛 덩어리 + 블룸 + 적중 지점 Point Light) 프리팹 — 모든 피어가 튼다. 첫 재생 때 한 번 만들어 재사용. 실시간 상한·라이트 칸은 프리팹에. 비면 월드 섬광 없음")]
    [DataTableIgnore, SerializeField] private InterruptHitFlashVfx hitFlashWorldVfx;
    [Tooltip("월드 섬광 크기 배율(루트 스케일)")]
    [DataTableIgnore, SerializeField, Min(0.01f)] private float hitFlashWorldScale = 1f;
    [Tooltip("적중 지점(대상 발밑 위치)에서 섬광 중심까지 높이(m) — 월드 섬광·오버레이 중심 둘 다")]
    [DataTableIgnore, SerializeField] private float hitFlashHeightOffset = 1f;

    // 제압 간파 섬광(할 일 179) — 제압 칸 마지막 1칸을 깎은 간파 성공이면 위 적중 섬광 ②~⑥ 대신 이 시퀀스를 모든 피어가 각자 화면에서 튼다.
    // 오너 암전(132)은 그대로 오너만(시퀀스 시작 순간 걷힌다). 이 피어 값을 쓴다(사건마다·프레임마다 읽음). 문서: interrupt-presentation.md §7.
    [Header("성공 — 제압 간파 섬광(할 일 179)")]
    // 켜기/끄기 = 위 "성공 — 공용" 의 A6 칸(suppressFlash).
    [Tooltip("컷 데이터(SuppressFlashSequence) — 컷마다 길이·명암·십자·별·집중선·색수차·빗금·3단 색. Play 중 바꾸면 다음 렌더 프레임부터. 비면 일반 섬광")]
    [DataTableIgnore, SerializeField] private SuppressFlashSequence suppressFlashSequence;
    [Tooltip("전체 화면 패스 재질(Hidden/Rendering/SuppressFlash). 비면 그리지 않는다(시간표는 돈다)")]
    [DataTableIgnore, SerializeField] private Material suppressFlashMaterial;
    [Tooltip("섬광 줄이기 기본값 — 켜면 반전 끔 + 대비 낮춤. PlayerPrefs '" + ReduceFlashPrefsKey + "' 가 있으면 그 값이 이긴다")]
    [DataTableIgnore, SerializeField] private bool reduceFlash;
    // 제압 암순응(할 일 192 Q7) = 섬광 시퀀스 복귀 뒤 컷 데이터의 '암순응 컷'(어두움 → 중간 → 원래 색)을 끊어서. 모든 피어 공용(Q8). 부드럽게 방식은 11-03 삭제.

    // 적중 라이트 연출(할 일 194) — 단계 설정이 V1·V2 면 ②~⑥ 대신 성공 순간 바로 튼다(모든 피어, 실시간). 둘 중 하나라도 비면 V0 로 돌아간다.
    // 이 피어 값을 쓴다(사건마다·프레임마다 읽음). 문서: interrupt-presentation.md §9.
    [Header("성공 — 적중 라이트(할 일 194)")]
    [Tooltip("수치 SO(InterruptHitLightLook) — 라이트·블룸·암순응·화이트아웃. 비면 V1·V2 대신 V0")]
    [DataTableIgnore, SerializeField] private InterruptHitLightLook hitLightLook;
    [Tooltip("월드 이펙트 프리팹(FX_InterruptHitLight — 라이트·불꽃·파편·빛줄기). 첫 재생 때 한 번 만들어 재사용. 비면 V1·V2 대신 V0")]
    [DataTableIgnore, SerializeField] private InterruptHitLightFx hitLightVfx;
    // V3 레퍼런스 맞춤(194 후속) — V1·V2 값을 건드리지 않게 SO·프리팹을 따로 든다. 저작 메뉴 'Tools/Interrupt/적중 라이트 V3 레퍼런스 에셋 만들기 (194)'.
    [Tooltip("V3 수치 SO(InterruptHitLightLook_V3) — 어두운 암순응 나·채도 낮춤 색 보정·약한 라이트·중심만 번지는 블룸. 비면 V3 대신 V0")]
    [DataTableIgnore, SerializeField] private InterruptHitLightLook hitLightLookReference;
    [Tooltip("V3 월드 이펙트 프리팹(FX_InterruptHitLight_V3 — 중심 점·가로 빛줄·가는 불꽃·작은 빛줄기). 비면 V3 대신 V0")]
    [DataTableIgnore, SerializeField] private InterruptHitLightFx hitLightVfxReference;

    // 간파 연출 4단계(할 일 192) — 실패 / 간파 / 대쉬 간파 / 제압 간파. 단계는 성공 사건(슬로우 Start 이벤트의 제압·대쉬 칸)으로 고르고,
    // 오너 시도 암전은 입력 순간 예측한 단계로 고른다. 이 피어 값을 쓴다(사건마다 읽음). 문서: interrupt-presentation.md §8.
    // 실패 = 단계 설정 없음(약 예측 = 약한 줌만, 강 예측의 판정 실패 = 그 순간 연출 종료 — 191 D4).
    [Header("간파 연출 단계(할 일 192)")]
    [Tooltip("간파(근접) 성공 — Q1 암전 없음, 섬광 기본 = V3 레퍼런스 맞춤·암순응 나(194 후속, 11-03 — V0~V2 는 랩 버튼으로)")]
    [DataTableIgnore, SerializeField] private InterruptTierProfile normalTier =
        new InterruptTierProfile(false, InterruptTierFlash.HitLightReference, InterruptDarkAdaptMode.SceneLight);
    [Tooltip("대쉬 간파 성공 — Q1 암전 없음, 섬광 기본 = V3 레퍼런스 맞춤·암순응 나(194 후속, 11-03 — V0~V2 는 랩 버튼으로)")]
    [DataTableIgnore, SerializeField] private InterruptTierProfile dashTier =
        new InterruptTierProfile(false, InterruptTierFlash.HitLightReference, InterruptDarkAdaptMode.SceneLight);
    [Tooltip("제압 간파 성공(근접·대쉬 무관, Q6) — 오너 암전 + 섬광 시퀀스 + 강한 줌 + 제압 암순응, 시퀀스에 V3 적중 라이트를 더한다(194 후속, 11-03)")]
    [DataTableIgnore, SerializeField] private InterruptTierProfile suppressTier = new InterruptTierProfile(true, InterruptTierFlash.SuppressSequence,
        InterruptDarkAdaptMode.ScreenPass, InterruptTierFlash.HitLightReference);

    /// <summary>섬광 줄이기 저장 키(PlayerPrefs, 0/1). 새 설정 UI 는 없다 — 나중에 설정 화면이 이 키(<see cref="ReduceFlash"/>)를 쓰면 된다.</summary>
    public const string ReduceFlashPrefsKey = "Interrupt.ReduceFlash";

    /// <summary>슬로우 3단계 수치 SO(194 후속 2) — 비면 코드 기본값 인스턴스. 사건마다 읽는다.</summary>
    public InterruptSlowMotionSettings SlowMotion => InterruptSlowMotionSettings.OrDefault(slowMotionSettings);

    /// <summary>시도 슬로우 프로필(강) — 사건 순간 이 객체를 넘기면 이벤트가 그 순간 값을 복사한다(캐싱 아님).</summary>
    public SlowMotionProfile InterruptProfile => SlowMotion.InterruptProfile;
    /// <summary>시도 슬로우 프로필(약).</summary>
    public SlowMotionProfile InterruptWeakProfile => SlowMotion.InterruptWeakProfile;
    /// <summary>대쉬 간파 시도 슬로우 프로필(강, 194 후속 2).</summary>
    public SlowMotionProfile InterruptDashProfile => SlowMotion.InterruptDashProfile;
    /// <summary>대쉬 간파 시도 슬로우 프로필(약, 194 후속 2).</summary>
    public SlowMotionProfile InterruptDashWeakProfile => SlowMotion.InterruptDashWeakProfile;
    /// <summary>성공 슬로우 프로필.</summary>
    public SlowMotionProfile InterruptSuccessProfile => SlowMotion.InterruptSuccessProfile;

    public float OwnerDimInDuration => ownerDimInDuration;
    public float OwnerDimOutDuration => ownerDimOutDuration;
    public Color OwnerDimColor => ownerDimColor;
    public float OwnerDimOpacity => ownerDimOpacity;

    public int HitFlashStarburstFrames => hitFlashStarburstFrames;
    public int HitFlashWhiteoutFrames => hitFlashWhiteoutFrames;
    public int HitFlashSmearFrames => hitFlashSmearFrames;
    public float HitFlashAberrationDuration => hitFlashAberrationDuration;
    public float HitFlashSettleDuration => hitFlashSettleDuration;
    public InterruptEffectTimeBase HitFlashTimeBase => hitFlashTimeBase;

    public Material HitFlashOverlayMaterial => hitFlashOverlayMaterial;
    public LensFlareDataSRP HitFlashLensFlare => hitFlashLensFlare;
    public float HitFlashFlareIntensity => hitFlashFlareIntensity;
    public float HitFlashFlareScale => hitFlashFlareScale;
    public float HitFlashBlurStrength => hitFlashBlurStrength;
    public VolumeProfile HitFlashAberrationProfile => hitFlashAberrationProfile;
    public VolumeProfile HitFlashBloomProfile => hitFlashBloomProfile;

    /// <summary>A6 제압 간파 섬광 시퀀스 칸(할 일 179) — 런타임 값(저장하지 않음, 디버그·테스트). Play 중 바꾸면 다음 성공부터.</summary>
    public bool SuppressFlashEnabled
    {
        get => suppressFlash;
        set => suppressFlash = value;
    }

    /// <summary>A7 섬광 = 대상 내부만(179/192 후속) — 런타임 값(저장하지 않음, 디버그·테스트). Play 중 바꾸면 다음 재생부터.</summary>
    public bool SuppressFlashTargetOnly
    {
        get => suppressFlashTargetOnly;
        set => suppressFlashTargetOnly = value;
    }

    /// <summary>
    /// 이번 성공이 제압 간파 섬광 시퀀스인가(아니면 일반 ②~⑥). 성공 시작 순간 A6 칸·컷 데이터를 읽는다 — 섬광 줄이기와 무관.
    /// </summary>
    public bool PlaysSuppressFlash(SlowMotionPurpose purpose, bool suppressInterrupt) =>
        PlaysSuppressFlash(purpose, InterruptPresentationTierRules.Select(true, InterruptVariant.Normal, suppressInterrupt));

    /// <summary>이 단계의 성공이 제압 간파 섬광 시퀀스인가(할 일 192) — 단계 설정·A6 칸·컷 데이터.</summary>
    public bool PlaysSuppressFlash(SlowMotionPurpose purpose, InterruptPresentationTier tier) =>
        InterruptPresentationTierRules.PlaysSuppressSequence(purpose, TierProfile(tier), suppressFlash, suppressFlashSequence != null);

    /// <summary>이 단계의 성공에서 트는 섬광(할 일 194) — 단계 설정·A6 칸·컷 데이터·적중 라이트 연출 유무. 사건마다 읽는다.</summary>
    public InterruptSuccessLook SuccessLook(InterruptPresentationTier tier) =>
        InterruptPresentationTierRules.SelectSuccessLook(TierProfile(tier), suppressFlash, suppressFlashSequence != null, HasHitLight,
            HasHitLightReference);

    /// <summary>이 단계의 섬광 시퀀스에 더하는 적중 라이트(194 후속 — 제압 간파 V3). 더하지 않으면 <see cref="InterruptSuccessLook.HitFlash"/>.</summary>
    public InterruptSuccessLook SequenceHitLight(InterruptPresentationTier tier) =>
        InterruptPresentationTierRules.SelectSequenceHitLight(SuccessLook(tier), TierProfile(tier), HasHitLight, HasHitLightReference);

    // 적중 라이트 연출(V1·V2)을 틀 수 있는가 — 수치 SO·월드 이펙트 프리팹 둘 다 있어야 한다.
    private bool HasHitLight => hitLightLook != null && hitLightVfx != null;
    // V3 — 전용 SO·프리팹 둘 다 있어야 한다.
    private bool HasHitLightReference => hitLightLookReference != null && hitLightVfxReference != null;

    public InterruptHitLightLook HitLightLook => hitLightLook;
    /// <summary>V3 수치 SO(194 후속) — 확인용.</summary>
    public InterruptHitLightLook HitLightLookReference => hitLightLookReference;

    /// <summary>이 섬광 종류가 읽는 수치 SO — V3 는 전용, 그 밖은 V1·V2 공용.</summary>
    public InterruptHitLightLook HitLightLookFor(InterruptSuccessLook look) =>
        InterruptPresentationTierRules.UsesReferenceLook(look) ? hitLightLookReference : hitLightLook;

    /// <summary>단계 설정(할 일 192). 실패는 설정이 없다(null).</summary>
    public InterruptTierProfile TierProfile(InterruptPresentationTier tier)
    {
        switch (tier)
        {
            case InterruptPresentationTier.Normal: return normalTier;
            case InterruptPresentationTier.Dash: return dashTier;
            case InterruptPresentationTier.Suppress: return suppressTier;
            default: return null;
        }
    }

    /// <summary>
    /// [오너] 이 화면의 마지막 간파 연출 단계(할 일 192 — 랩 표시용). 입력 순간 예측한 단계로 시작하고(강·약 시도),
    /// 판정 성공·서버 성공 슬로우가 오면 그 단계로, 판정 실패·종료면 실패로 바뀐다. 아직 없으면 실패.
    /// </summary>
    public InterruptPresentationTier LastOwnerTier { get; private set; }

    /// <summary>[각 피어] 이 화면이 마지막으로 받은 성공 단계(남의 성공 포함 — 랩 표시용). 아직 없으면 실패.</summary>
    public InterruptPresentationTier LastSuccessTier { get; private set; }

    // [오너] 입력 순간 예측한 성공 단계 — 판정 성공 때 섬광 종류를 고른다(서버 표식보다 먼저라서).
    private InterruptPresentationTier _ownerPredictedTier = InterruptPresentationTier.Normal;

    public SuppressFlashSequence SuppressFlashSequence => suppressFlashSequence;
    public Material SuppressFlashMaterial => suppressFlashMaterial;

    /// <summary>
    /// 섬광 줄이기(할 일 179) — PlayerPrefs 값이 있으면 그것, 없으면 디렉터 칸(기본 끔). 쓰면 칸과 PlayerPrefs 둘 다 바꾼다.
    /// 재생 중에도 프레임마다 읽는다.
    /// </summary>
    public bool ReduceFlash
    {
        get => PlayerPrefs.HasKey(ReduceFlashPrefsKey) ? PlayerPrefs.GetInt(ReduceFlashPrefsKey) != 0 : reduceFlash;
        set
        {
            reduceFlash = value;
            PlayerPrefs.SetInt(ReduceFlashPrefsKey, value ? 1 : 0);
        }
    }

    // 단계 객체는 값을 들지 않는다(설정 = this, 시간 = 포트) — 만들어 두기만 하고 사건마다 this 의 필드를 읽는다.
    private readonly ITimeScalePort _timePort = new GlobalTimeScalePort();
    private InterruptAttemptStage _attempt;
    private InterruptResolveStage _resolve;
    private InterruptSuccessStage _success;
    private InterruptFailStage _fail;
    private InterruptPersonalChannel _personal;
    private InterruptSharedChannel _shared;

    private InterruptPersonalChannel PersonalChannel => _personal ??= new InterruptPersonalChannel(this);
    private InterruptSharedChannel SharedChannel => _shared ??= new InterruptSharedChannel(this);
    private InterruptAttemptStage AttemptStage => _attempt ??= new InterruptAttemptStage(this, _timePort, SharedChannel);
    private InterruptResolveStage ResolveStage => _resolve ??= new InterruptResolveStage(_timePort);
    private InterruptSuccessStage SuccessStage => _success ??= new InterruptSuccessStage(this, _timePort, PersonalChannel, SharedChannel);
    private InterruptFailStage FailStage => _fail ??= new InterruptFailStage(_timePort);

    // 오너 연출 상태 기계(할 일 132). 암전 = 마스크 암전 렌더 출력(단계 3), 빛 모임 = 플레이어 소켓 VFX(단계 4) — 둘 다 Awake 에서 만든다(없으면 로그).
    private readonly InterruptOwnerPresentationLogOutput _ownerLogOutput = new InterruptOwnerPresentationLogOutput();
    private InterruptOwnerDimOutput _ownerDimOutput;
    private InterruptLightGatherOwnerOutput _ownerLightGatherOutput;
    private InterruptOwnerPresentation _ownerPresentation;
    // 팀원 화면의 남(발동자) 빛 모임(할 일 132 단계 4) — 서버 강 시도 슬로우로 켜고 발동자 Hit·실패·성공·리셋으로 끈다.
    private readonly InterruptLightGatherPeers _peerLightGather = new InterruptLightGatherPeers();

    private IInterruptOwnerDimOutput DefaultOwnerDimOutput =>
        _ownerDimOutput != null ? _ownerDimOutput : (IInterruptOwnerDimOutput)_ownerLogOutput;
    private IInterruptLightGatherOutput DefaultLightGatherOutput =>
        _ownerLightGatherOutput != null ? _ownerLightGatherOutput : (IInterruptLightGatherOutput)_ownerLogOutput;

    // 효과 칸 문(할 일 132 §17) — 상태 기계와 출력 사이. 끈 효과는 여기서 그리기만 막는다.
    private InterruptOwnerOutputGate _ownerGate;

    /// <summary>오너 연출 상태 기계 — 성공에 들어선 순간 암전이 켜져 있었으면(<see cref="InterruptOwnerPresentation.SuccessDimmed"/>) 적중 섬광 오버레이를 튼다.</summary>
    public InterruptOwnerPresentation OwnerPresentation
    {
        get
        {
            if (_ownerPresentation == null)
            {
                _ownerGate = new InterruptOwnerOutputGate(this, DefaultOwnerDimOutput, DefaultLightGatherOutput);
                _ownerPresentation = new InterruptOwnerPresentation(this, _ownerGate, _ownerGate);
            }

            return _ownerPresentation;
        }
    }

    // 적중 섬광(할 일 132 §15) — 오너 화면 출력(오버레이·플레어·블러·Volume)은 Awake 에서 객체만 만든다(실제 오브젝트는 첫 재생 때). 월드 섬광은 첫 재생 때 한 번 만든다.
    private InterruptHitFlashOverlay _hitFlashOverlay;
    private InterruptHitFlashSequencer _hitFlash;
    private InterruptHitFlashVfx _hitFlashWorld;

    /// <summary>적중 섬광 진행기 — 성공 Started 에 오너 오버레이·섬광 순간을 넘긴다.</summary>
    public InterruptHitFlashSequencer HitFlash =>
        _hitFlash ??= new InterruptHitFlashSequencer(this, _hitFlashOverlay, HandleHitFlashMoment);

    // 적중 라이트 연출(할 일 194) — 진행기는 처음 쓸 때, 월드 이펙트는 첫 재생 때 한 번 만든다.
    private InterruptHitLightPlayer _hitLight;
    private InterruptHitLightFx _hitLightFx;
    private InterruptHitLightFx _hitLightFxReference;

    /// <summary>적중 라이트 연출 진행기(V1·V2·V3) — 성공 순간 시작, 매 프레임 실시간으로 진행.</summary>
    public InterruptHitLightPlayer HitLight => _hitLight ??=
        new InterruptHitLightPlayer(HitLightLookFor, EnsureHitLightFx, () => _darkAdaptation, SceneLightDim.Shared,
            new InterruptHitFlashOverlay(new InterruptHitLightWhiteoutSettings(this)), FindCasterWeaponPoint, this);

    // 194 후속 — 시전자 플레이어(이 피어에 스폰된 것)의 Variant 루트 PlayerWeaponAnchor 오른손 무기 위치. 못 찾으면 null(대상으로 대체).
    private Vector3? FindCasterWeaponPoint(ulong casterClientId)
    {
        NetworkObject caster = FindCasterPlayerObject(casterClientId);
        return caster != null && caster.TryGetComponent(out PlayerWeaponAnchor anchor) && anchor.TryGetRightWeaponPoint(out Vector3 point)
            ? point
            : null;
    }

    // 시전자 플레이어 오브젝트 — 찾는 방식은 FindLightGatherSocket 과 같다(스폰 목록에서 직접 — 성공은 드물어 매번 찾는다).
    private NetworkObject FindCasterPlayerObject(ulong casterClientId)
    {
        NetworkSpawnManager spawnManager = _networkManager != null ? _networkManager.SpawnManager : null;
        if (spawnManager == null)
            return null;

        foreach (NetworkObject networkObject in spawnManager.SpawnedObjectsList)
        {
            if (networkObject != null && networkObject.IsPlayerObject && networkObject.OwnerClientId == casterClientId)
                return networkObject;
        }

        return null;
    }

    // 틈 수직(194 후속) — 무기 앵커가 없으면 시전자 중심 = 발밑 + 섬광 높이(대상 적중점과 같은 높이).
    bool IInterruptHitLightScene.TryGetCasterCenter(ulong casterClientId, out Vector3 center)
    {
        NetworkObject caster = FindCasterPlayerObject(casterClientId);
        center = caster != null ? FlashPoint(caster.transform.position) : default;
        return caster != null;
    }

    // 틈 수직 — 적중점 근처 간파 대상(제압 섬광 실루엣과 같은 찾기)의 시전자 쪽 몸 표면.
    bool IInterruptHitLightScene.TryGetTargetSurface(Vector3 targetPoint, Vector3 from, out Vector3 surface)
    {
        InterruptTargetRegistry.Entry entry = FindNearestTarget(targetPoint);
        surface = default;
        return entry != null && entry.TryGetClosestSurfacePoint(from, out surface);
    }

    bool IInterruptHitLightScene.TryGetViewForward(out Vector3 forward)
    {
        Camera camera = Camera.main;
        forward = camera != null ? camera.transform.forward : default;
        return camera != null;
    }

    // V3 는 전용 프리팹 인스턴스(194 후속), 그 밖은 V1·V2 공용 인스턴스.
    private InterruptHitLightFx EnsureHitLightFx(InterruptSuccessLook look) =>
        InterruptPresentationTierRules.UsesReferenceLook(look)
            ? EnsureFx(ref _hitLightFxReference, hitLightVfxReference)
            : EnsureFx(ref _hitLightFx, hitLightVfx);

    private InterruptHitLightFx EnsureFx(ref InterruptHitLightFx instance, InterruptHitLightFx prefab)
    {
        if (instance == null && prefab != null)
        {
            instance = Instantiate(prefab, transform);
            instance.name = prefab.name;
        }

        return instance;
    }

    // 제압 간파 섬광(할 일 179) — 출력은 Awake 에서 만든다(없으면 시간표만 돈다). 서버 표식은 같은 프레임 성공 사건이 소비한다.
    private SuppressFlashScreenOutput _suppressFlashOutput;
    private SuppressFlashPlayer _suppressFlash;
    private readonly SuppressInterruptMark _suppressMark = new SuppressInterruptMark();
    private readonly List<GameObject> _suppressSubjects = new List<GameObject>();
    private readonly List<GameObject> _suppressTargets = new List<GameObject>();
    private readonly List<GameObject> _suppressMaskRoots = new List<GameObject>();
    private readonly List<InterruptTargetRegistry.Entry> _targetBuffer = new List<InterruptTargetRegistry.Entry>();

    /// <summary>제압 간파 섬광 진행기 — 공용 성공 사건(제압 간파)에 시작, 매 프레임 실제 시간으로 진행.</summary>
    public SuppressFlashPlayer SuppressFlash =>
        _suppressFlash ??= new SuppressFlashPlayer(() => suppressFlashSequence, () => ReduceFlash, _suppressFlashOutput,
            _darkAdaptation);

    // 피어 연출 — 슬로우 이벤트(Started/Failed/ForcedReset)의 유일한 구독자. 객체 참조만 들고 값은 들지 않는다.
    private GlobalTimeScale _timeScale;
    private NetworkManager _networkManager;
    private InterruptDarkAdaptation _darkAdaptation;
    private readonly List<IInterruptCueSink> _sinkBuffer = new List<IInterruptCueSink>();

    private void Awake()
    {
        // TryGetComponent — 없으면 진짜 null(에디터의 GetComponent 가짜 null 은 인터페이스로 넘기면 null 비교를 통과한다).
        TryGetComponent(out _timeScale);
        TryGetComponent(out _networkManager);
        TryGetComponent(out _darkAdaptation);

        if (Instance != null && Instance != this)
            return;

        Instance = this;
        _ownerDimOutput = new InterruptOwnerDimOutput(this);
        _ownerLightGatherOutput = new InterruptLightGatherOwnerOutput(InterruptLightGatherOwnerOutput.FindSocket, KeepInOwnerDim);
        _hitFlashOverlay = new InterruptHitFlashOverlay(this);
        _suppressFlashOutput = new SuppressFlashScreenOutput(() => suppressFlashMaterial);
    }

    private void OnEnable()
    {
        if (_timeScale == null)
            return;

        _timeScale.Started += HandleStarted;
        _timeScale.Failed += HandleFailed;
        _timeScale.ForcedReset += HandleForcedReset;
    }

    private void Update()
    {
        // 오너 암전 램프 = 실시간(슬로우·히트스톱 중에도 0.1/0.2초 그대로). 적중 섬광 ②③④·섬광 순간 = 렌더 프레임 수, ⑤⑥ = 진행기 시계(기본 실시간) —
        // 성공 암전 끄기도 그 섬광 순간에 진행기가 알린다(HandleHitFlashMoment, 시간표 하나).
        _ownerPresentation?.Tick(Time.unscaledTime);
        _ownerGate?.Sync(); // 오너 암전 칸을 Play 중에 바꾸면 바로(§17)
        _hitFlash?.Tick(InterruptEffectTime.Now(_hitFlash.Clock), Time.frameCount);
        _suppressFlash?.Tick(Time.unscaledTime); // 제압 간파 섬광 = 실제 시간(슬로우·히트스톱 무관, 할 일 179)
        _hitLight?.Tick(Time.unscaledTime, Time.frameCount); // 적중 라이트 연출 = 실제 시간(할 일 194 Q8), V2 화이트아웃 = 렌더 프레임 수
    }

    private void OnDisable()
    {
        _ownerPresentation?.ForcedReset();
        _peerLightGather.EndAll();
        StopHitFlash();
        _suppressFlash?.Stop();

        if (ReferenceEquals(_timeScale, null))
            return;

        // 파괴된 GlobalTimeScale 도 C# 객체는 남아 있어 그대로 뗄 수 있다.
        _timeScale.Started -= HandleStarted;
        _timeScale.Failed -= HandleFailed;
        _timeScale.ForcedReset -= HandleForcedReset;
    }

    private void OnDestroy()
    {
        // 비트 원복·덮개 캔버스·RT 정리(상태 기계는 OnDisable 에서 이미 0 을 넘겼다).
        _ownerDimOutput?.Dispose();
        _ownerDimOutput = null;
        _hitFlashOverlay?.Dispose();
        _hitFlashOverlay = null;
        _suppressFlash?.Stop();
        _suppressFlashOutput?.Hide();
        _hitLight?.Dispose();
        _hitLight = null;

        if (Instance == this)
            Instance = null;
    }

    /// <summary>이 디렉터의 칸 — 호출 순간 필드를 읽는다.</summary>
    public bool Get(InterruptProductionCue cue)
    {
        switch (cue)
        {
            case InterruptProductionCue.HitStop: return hitStop;
            case InterruptProductionCue.DarkAdaptation: return darkAdaptation;
            case InterruptProductionCue.OwnerZoom: return ownerZoom;
            case InterruptProductionCue.SuccessFxSlot: return successFxSlot;
            case InterruptProductionCue.PaladinShieldGlow: return paladinShieldGlow;
            case InterruptProductionCue.PaladinShieldCharge: return paladinShieldCharge;
            case InterruptProductionCue.PaladinWave: return paladinWave;
            case InterruptProductionCue.AssassinInsightAttempt: return assassinInsightAttempt;
            case InterruptProductionCue.AssassinInsightSuccess: return assassinInsightSuccess;
            case InterruptProductionCue.TargetFlash: return targetFlash;
            default: return false;
        }
    }

    /// <summary>
    /// 칸을 런타임에 바꾼다(디버그 패널 — 할 일 132 §17). 이 인스턴스 값만 바꾸고 저장하지 않는다(Play 종료 시 원래 값).
    /// 다음 사건부터 쓰인다(사건마다 읽는 규칙 그대로).
    /// </summary>
    public void Set(InterruptProductionCue cue, bool on)
    {
        switch (cue)
        {
            case InterruptProductionCue.HitStop: hitStop = on; break;
            case InterruptProductionCue.DarkAdaptation: darkAdaptation = on; break;
            case InterruptProductionCue.OwnerZoom: ownerZoom = on; break;
            case InterruptProductionCue.SuccessFxSlot: successFxSlot = on; break;
            case InterruptProductionCue.PaladinShieldGlow: paladinShieldGlow = on; break;
            case InterruptProductionCue.PaladinShieldCharge: paladinShieldCharge = on; break;
            case InterruptProductionCue.PaladinWave: paladinWave = on; break;
            case InterruptProductionCue.AssassinInsightAttempt: assassinInsightAttempt = on; break;
            case InterruptProductionCue.AssassinInsightSuccess: assassinInsightSuccess = on; break;
            case InterruptProductionCue.TargetFlash: targetFlash = on; break;
        }
    }

    /// <summary>연출 효과 칸(할 일 132 §17) — 그릴 때마다 읽는다.</summary>
    public bool IsEffectOn(InterruptPresentationEffect effect)
    {
        switch (effect)
        {
            case InterruptPresentationEffect.OwnerDim: return showOwnerDim;
            case InterruptPresentationEffect.LightGather: return showLightGather;
            case InterruptPresentationEffect.Starburst: return showStarburst;
            case InterruptPresentationEffect.Whiteout: return showWhiteout;
            case InterruptPresentationEffect.Smear: return showSmear;
            case InterruptPresentationEffect.Aberration: return showAberration;
            case InterruptPresentationEffect.Settle: return showSettle;
            case InterruptPresentationEffect.WorldFlash: return showWorldFlash;
            case InterruptPresentationEffect.HitLight: return showHitLight;
            default: return false;
        }
    }

    /// <summary>연출 효과 칸을 런타임에 바꾼다(디버그 패널). 이 인스턴스 값만 — 저장하지 않는다.</summary>
    public void SetEffect(InterruptPresentationEffect effect, bool on)
    {
        switch (effect)
        {
            case InterruptPresentationEffect.OwnerDim: showOwnerDim = on; break;
            case InterruptPresentationEffect.LightGather: showLightGather = on; break;
            case InterruptPresentationEffect.Starburst: showStarburst = on; break;
            case InterruptPresentationEffect.Whiteout: showWhiteout = on; break;
            case InterruptPresentationEffect.Smear: showSmear = on; break;
            case InterruptPresentationEffect.Aberration: showAberration = on; break;
            case InterruptPresentationEffect.Settle: showSettle = on; break;
            case InterruptPresentationEffect.WorldFlash: showWorldFlash = on; break;
            case InterruptPresentationEffect.HitLight: showHitLight = on; break;
        }
    }

    // ---- 재생 지점이 부르는 정적 API (인스턴스 없으면 꺼짐) ----

    /// <summary>이 피어의 칸. 인스턴스가 없으면 꺼짐.</summary>
    public static bool IsOn(InterruptProductionCue cue) => InterruptProductionRules.IsOn(Instance, cue);

    /// <summary>B2·C1 — 클립 이벤트 이펙트 재생 허용(<see cref="EffectAnimEvents"/>). 간파 id 가 아니면 늘 true.</summary>
    public static bool AllowsAnimEffect(string id) => InterruptProductionRules.AllowsAnimEffect(Instance, id);

    /// <summary>
    /// A3 — 개인 채널 오너 줌 배율(카메라가 매 프레임 묻는다). 지금 슬로우 목적으로 강·약 시도(슬로우 SO 칸)·성공(<paramref name="successZoomFactor"/>)
    /// 배율을 고른다(할 일 132). 칸이 꺼져 있거나 인스턴스가 없으면 1.
    /// </summary>
    public static float OwnerZoomFactor(SlowMotionPurpose purpose, float successZoomFactor) =>
        Instance != null
            ? Instance.SuccessStage.OwnerZoomFactor(InterruptProductionRules.SelectOwnerZoomFactor(
                purpose, Instance.SlowMotion.InterruptZoomFactor, Instance.SlowMotion.InterruptWeakZoomFactor, successZoomFactor))
            : 1f;

    /// <summary>
    /// A3 — 내 강 시도가 아직 결론 전인가(오너 상태 기계 <see cref="InterruptOwnerPresentation.AwaitsStrongResult"/>).
    /// 카메라가 시도 슬로우 복귀 중에도 줌을 붙잡을지 묻는다 — 성공이 이어지면 줌인·줌아웃이 한 번씩(할 일 132). 인스턴스가 없으면 false.
    /// </summary>
    public static bool OwnerAwaitsStrongResult =>
        Instance != null && Instance._ownerPresentation != null && Instance._ownerPresentation.AwaitsStrongResult;

    /// <summary>A5 칸(할 일 154 → 179 후속 2) — 런타임 값(저장하지 않음, 디버그·테스트).</summary>
    public bool HideCombatUi
    {
        get => hideCombatUi;
        set => hideCombatUi = value;
    }

    /// <summary>
    /// A5 — 이 화면에서 지금 전투 표시를 숨기는가(<see cref="InterruptProductionRules.ShouldHideCombatUi"/>).
    /// 읽는 곳 = 피해 숫자 스포너(캔버스)·머리 위 체력바(플레이어·몬스터·보스)·오너 조준 표시, 매 프레임(디렉터 → 표시 한 방향, 디렉터는 그들을 모른다).
    /// 상태에서 바로 나오는 값이라 따로 복원할 것이 없다 — 연출 끝·실패·강제 리셋·디렉터 꺼짐(인스턴스 없음 = false)이면 다음 프레임에 보인다.
    /// 숨긴 렌더러는 그려지지 않으므로 암전 마스크(비트 4)·섬광 실루엣(비트 7)에도 남지 않는다.
    /// </summary>
    public static bool CombatUiHidden =>
        Instance != null && InterruptProductionRules.ShouldHideCombatUi(Instance.hideCombatUi,
            Instance.OwnerStateForCombatUi,
            Instance._ownerPresentation != null ? Instance._ownerPresentation.DimIntensity : 0f,
            Instance._hitFlash != null && Instance._hitFlash.OverlayActive,
            Instance._suppressFlash != null && Instance._suppressFlash.IsPlaying);

    // A5 가 보는 오너 상태 — 암전 없는 강 시도(일반·대쉬 간파, 할 일 192 Q1)는 시도 중 숨기지 않는다(숨김 구간 = 암전·섬광).
    private InterruptOwnerPresentationState OwnerStateForCombatUi
    {
        get
        {
            if (_ownerPresentation == null)
                return InterruptOwnerPresentationState.None;

            InterruptOwnerPresentationState state = _ownerPresentation.State;
            return state == InterruptOwnerPresentationState.AttemptStrong && !_ownerPresentation.AttemptDims
                ? InterruptOwnerPresentationState.None
                : state;
        }
    }

    /// <summary>
    /// [각 피어] 시도 — 시전 시작(OnClientPlay, 전 피어)에 스킬이 알린다. 그 스킬의 시도 cue(B1)를 공용 채널로 스킬 자신에 튼다.
    /// 끄는 쪽(OnEnd)은 디렉터를 거치지 않는다.
    /// </summary>
    public static void LocalAttempt(in InterruptProductionEvent e)
    {
        if (Instance != null)
            Instance.AttemptStage.Local(e, e.Skill as IInterruptCueSink);
    }

    /// <summary>
    /// [각 피어] 캐릭터·대상 RPC 수신부(B3 충격파·C2 어쌔신 성공·E1 맞은 쪽 섬광)가 부른다 — 칸이 켜져 있으면 <paramref name="sink"/> 에 튼다(공용 채널).
    /// 전송로(RPC)는 그대로다(설계 Q1-a). 인스턴스가 없으면 안 튼다(옛 스위치 꺼짐과 같다).
    /// </summary>
    public static void PlayCue(InterruptProductionCue cue, in InterruptProductionEvent e, IInterruptCueSink sink)
    {
        if (Instance != null)
            Instance.SharedChannel.PlayCue(cue, e, sink);
    }

    // ---- 오너 사건 (할 일 132 — 간파한 본인 화면만, 오너 연출 상태 기계로) ----

    /// <summary>
    /// [오너] 간파(근접·대쉬) 입력 순간의 즉시 예측 — 강(성공 예측)이고 예측한 성공 단계가 암전 켬(기본 제압 간파만, 할 일 192 Q1)이면 오너 암전·빛 모임을 바로 켠다.
    /// 일반·대쉬 간파 강 예측은 암전 없이 강 상태만(강한 줌·섬광 ②~⑥ 조건). 약은 아무것도 안 켠다(줌·슬로우는 서버 시도 슬로우가 처리, Q3),
    /// 대상 없음도 아무것도 안 켠다(정정1). 앞 시도가 결론나지 않았으면 중복 입력으로 무시한다.
    /// <paramref name="owner"/>·<paramref name="target"/> = 암전에 남길 오너 캐릭터(무기 이펙트 포함 하위 전부)·이번 시도 간파 대상(예측이 고른 캐시 대상).
    /// 제압 여부는 대상의 <see cref="IInterruptSuppressForecast"/>(23호 = 복제된 남은 제압 칸)로 예측한다.
    /// <paramref name="attackDirection"/> = 근접은 요청에 싣는 시전 방향, 대쉬는 대시 방향 — 적중 섬광 ④ 스미어 방향(할 일 132 §16).
    /// 새 시도로 받아들였으면 true(인스턴스 없음·대상 없음·중복 입력 = false).
    /// </summary>
    public static bool LocalOwnerAttempt(PlayerInterruptSkillBase skill, BeaverLobby.Player.Dash.InterruptAttemptForecast forecast,
        GameObject owner, GameObject target, Vector3 attackDirection, bool viaDash = false)
    {
        if (Instance == null)
            return false;

        // 받아들일 시도만 대상을 바꾼다 — 태깅은 다음 프레임 암전이 0 → 양수가 될 때(들어옴 램프 첫 값).
        // 대상을 예측보다 먼저 넘긴다: 예측이 같은 호출 안에서 빛 모임을 켜며 KeepInOwnerDim 으로 넣은 루트를 SetSubjects 가 비우지 않게.
        InterruptOwnerPresentation presentation = Instance.OwnerPresentation;
        if (!presentation.Accepts(forecast))
            return false;

        InterruptPresentationTier predicted = InterruptPresentationTierRules.Select(true,
            InterruptPresentationTierRules.VariantOf(viaDash), PredictsSuppress(target));
        bool dims = InterruptPresentationTierRules.AttemptDims(forecast, Instance.TierProfile(predicted));
        Instance._ownerPredictedTier = predicted;
        Instance.LastOwnerTier = forecast == BeaverLobby.Player.Dash.InterruptAttemptForecast.Strong ? predicted : InterruptPresentationTier.Fail;

        Instance._ownerDimOutput?.SetSubjects(owner, target);
        return presentation.Predict(forecast, skill, Time.unscaledTime, attackDirection, dims);
    }

    /// <summary>[오너] 이 대상을 지금 간파하면 제압에 들어갈 것으로 보이는가(<see cref="IInterruptSuppressForecast"/>). 대상 없음 = false.</summary>
    public static bool PredictsSuppress(GameObject target) =>
        target != null && ((DebugSuppressForecastOverride != null && DebugSuppressForecastOverride(target)) ||
                           (target.TryGetComponent(out IInterruptSuppressForecast forecast) && forecast.WouldSuppressOnInterrupt));

    /// <summary>
    /// [오너] 이번 오너 암전에 함께 남길 루트(빛 모임 VFX 등, 단계 4). 암전이 걷히면 빠진다 — 시도마다 다시 부른다.
    /// 렌더링 레이어 비트 4 를 그 순간 루트 아래 렌더러에 붙인다(<see cref="InterruptOwnerMaskTagger"/>).
    /// </summary>
    public static void KeepInOwnerDim(GameObject root)
    {
        if (Instance != null)
            Instance._ownerDimOutput?.KeepWhileDimmed(root);
    }

    /// <summary>[오너] 간파 스킬 애니 Hit 이벤트 — 빛 모임 끝(기존 Hit 이벤트 재사용).</summary>
    public static void LocalOwnerHit()
    {
        if (Instance != null)
            Instance._ownerPresentation?.LocalHit();
    }

    /// <summary>
    /// [각 피어] 남(<paramref name="casterClientId"/>)의 간파 스킬 애니 Hit 이벤트 — 이 화면에서 튼 그 발동자의 빛 모임 끝(할 일 132 단계 4).
    /// 각 피어가 자기 애니메이터(복제된 발동자 애니)의 Hit 을 받는 기존 경로를 쓴다 — 새 RPC 없음.
    /// </summary>
    public static void PeerCasterHit(ulong casterClientId)
    {
        if (Instance != null)
            Instance._peerLightGather.EndCaster(casterClientId);
    }

    /// <summary>[오너] 판정 전 종료·서버 거부 — 결론이 없으면 실패처럼 걷는다.</summary>
    public static void LocalOwnerEnded()
    {
        if (Instance != null)
            Instance._ownerPresentation?.LocalEnded(Time.unscaledTime);
    }

    /// <summary>
    /// 오너 암전·빛 모임 출력을 갈아 끼운다(디버그·테스트 등). null 이면 기본 — 암전 = 마스크 암전 렌더 출력, 빛 모임 = 플레이어 소켓 VFX.
    /// </summary>
    public static void SetOwnerOutputs(IInterruptOwnerDimOutput dimOutput, IInterruptLightGatherOutput gatherOutput)
    {
        if (Instance == null)
            return;

        // 갈아 끼운 출력도 효과 칸 문 뒤에 둔다(§17).
        InterruptOwnerPresentation presentation = Instance.OwnerPresentation;
        Instance._ownerGate = new InterruptOwnerOutputGate(Instance,
            dimOutput ?? Instance.DefaultOwnerDimOutput, gatherOutput ?? Instance.DefaultLightGatherOutput);
        presentation.SetOutputs(Instance._ownerGate, Instance._ownerGate);
    }

    // ---- 피어 사건 (GlobalTimeScale 이벤트 → 단계) ----

    private void HandleStarted(uint triggerId, ulong casterClientId, SlowMotionPurpose purpose, Vector3 targetPosition)
    {
        // 보는 사람을 못 정하면(NetworkManager 없음) 암순응은 건너뛴다 — 옛 InterruptDarkAdaptation 과 같다.
        bool hasViewer = _networkManager != null;
        bool viewerIsCaster = hasViewer && InterruptProductionRules.IsCasterView(casterClientId, _networkManager.LocalClientId);

        // 로컬 선착순(할 일 143-2) — 남의 강 시도·성공이면 그 대상의 이 창을 이 피어에서 닫는다. 이후 내 입력 예측은 약(오너 암전·빛 모임·섬광 없음).
        // 판정은 서버 장부(InterruptWindowClaims) 그대로 — 여기는 오너 연출만 1차로 막는다.
        if (hasViewer && !viewerIsCaster)
            ObserveLocalClaim(triggerId, casterClientId, purpose, targetPosition);

        // 오너 연출(할 일 132) — 내 슬로우만(팀원 화면은 시도 단계 무변화). 시도 목적이 예측과 다르면 걷고, 성공이면 성공 단계로.
        // 오너 판정 cue(할 일 191)가 이미 처리한 내 슬로우면 본인 화면 오너 연출·섬광 예약을 건너뛴다(OwnerJudgeCues.cs).
        bool ownerCueHandled = viewerIsCaster && OwnerJudgeCueHandles(purpose);
        if (viewerIsCaster && !ownerCueHandled)
            OwnerPresentation.ServerStarted(triggerId, purpose, true, Time.unscaledTime);

        // 빛 모임(할 일 132 단계 4) — 남의 강 시도면 이 화면에 튼다(본인 화면은 입력 순간 예측이 이미 켰다). 성공이면 그 발동자 것을 끈다.
        // 빛 모임 칸(§17)이 꺼져 있으면 틀지 않는다(그리기만 — 끄는 사건은 그대로 와도 빈 목록이라 무해).
        if (hasViewer && InterruptLightGatherRules.ShouldPlayForPeer(purpose, viewerIsCaster))
        {
            if (showLightGather)
                _peerLightGather.Begin(casterClientId, triggerId, FindLightGatherSocket(casterClientId));
        }
        else if (purpose == SlowMotionPurpose.Success)
            _peerLightGather.EndCaster(casterClientId);

        if (purpose != SlowMotionPurpose.Success)
            return;

        // 제압 간파(할 일 179)·대쉬 간파(192) — 서버가 슬로우 Start 이벤트 표식 바이트에 실어 보낸 칸(이 Started 호출 안에서 읽는다) → 연출 단계.
        bool suppressInterrupt = _timeScale != null && _timeScale.CurrentSuppressInterrupt;
        bool dashInterrupt = _timeScale != null && _timeScale.CurrentDashInterrupt;
        var e = InterruptProductionEvent.LocalSuccess(triggerId, casterClientId, targetPosition, suppressInterrupt,
            InterruptPresentationTierRules.VariantOf(dashInterrupt));
        InterruptPresentationTier tier = e.Tier;
        LastSuccessTier = tier;
        if (viewerIsCaster)
            LastOwnerTier = tier;

        // 오너 판정 cue(191)가 판정 순간 이미 섬광을 틀었다 — 예측 단계의 섬광(시퀀스 / 적중 라이트 / ②~⑥, OwnerJudgeCues.cs).
        InterruptSuccessLook look = SuccessLook(tier);
        bool playsSequence = look == InterruptSuccessLook.SuppressSequence;
        bool ownerPlayedSequence = ownerCueHandled && _ownerJudgeCues != null && _ownerJudgeCues.LocalSuccessPlayedSuppress;
        if (InterruptPresentationTierRules.StartsSuppressSequenceOnServerSuccess(playsSequence, ownerCueHandled, ownerPlayedSequence))
        {
            // 예측은 제압 아님 → 서버는 제압: 판정 순간 튼 ②~⑥·적중 라이트를 걷고 시퀀스로(두 섬광이 겹치지 않게).
            if (ownerCueHandled)
                StopHitFlash();
            BeginSuppressFlash(e, viewerIsCaster);
        }
        else if (InterruptPresentationTierRules.UsesHitLight(look))
        {
            // 적중 라이트 연출(할 일 194, V1·V2) — 성공 순간 바로(섬광 순간 예약 없음). 본인 화면은 판정 cue 가 이미 틀었으면 건너뛴다.
            if (!ownerCueHandled)
                BeginHitLight(e, viewerIsCaster, look);
        }
        else if (!playsSequence)
        {
            // 적중 섬광(할 일 132 §15·§17) — 히트스톱이 이 순간 시작하므로 ② 도 이 프레임(히트스톱 길이 0 이어도 같다).
            // 오너 화면 섬광(②~⑥) = 내 성공 ∧ 그 순간 오너 암전 상태가 켜져 있음(강 시도 — 암전 칸을 꺼도 상태는 돈다).
            // 섬광 순간(②③④ 프레임 뒤 = ⑤ 시작) = 모든 피어 월드 섬광·라이트 + 암순응.
            float now = InterruptEffectTime.Now(HitFlash.Clock);
            int frame = Time.frameCount;
            bool ownerOverlay = !ownerCueHandled && InterruptHitFlashRules.ShouldPlayOwnerOverlay(purpose, viewerIsCaster,
                viewerIsCaster && OwnerPresentation.SuccessDimmed);
            // ④ 스미어 방향 = 이번 강 시도의 공격 방향(오너 상태 — 입력 순간 예측이 받은 시전/대시 방향).
            if (ownerOverlay)
                HitFlash.BeginOwnerOverlay(FlashPoint(targetPosition), OwnerPresentation.AttackDirection, now, frame);
            if (!ownerCueHandled)
                HitFlash.ScheduleFlash(new InterruptHitFlashMoment(e, viewerIsCaster, ownerOverlay), frame);
        }

        SuccessStage.LocalStarted(purpose, e, FindSuccessFxSinks(casterClientId));
        LocalSucceeded?.Invoke(e);
    }

    // 슬로우 메시지는 대상을 싣지 않는다(할 일 143-3) — 시전자 플레이어의 간파 스킬이 이 피어의 복제 위치·방향·창으로 대상을 다시 찾는다.
    private void ObserveLocalClaim(uint triggerId, ulong casterClientId, SlowMotionPurpose purpose, Vector3 targetPosition)
    {
        if (purpose != SlowMotionPurpose.Attempt && purpose != SlowMotionPurpose.Success)
            return;

        PlayerInterruptSkillBase skill = FindInterruptSkill(casterClientId);
        ulong viewer = _networkManager.LocalClientId;
        if (purpose == SlowMotionPurpose.Attempt)
        {
            if (skill != null)
                InterruptLocalWindowClaims.Local.ObserveAttempt(skill.FindPeerAttemptTargetId(), triggerId, casterClientId, purpose, viewer);
            return;
        }

        InterruptLocalWindowClaims.Local.ObserveSuccess(skill != null ? skill.FindPeerSuccessTargetId(targetPosition) : 0,
            casterClientId, viewer);
    }

    // 발동자 플레이어의 간파 슬롯 스킬. 찾는 방식은 FindSuccessFxSinks 와 같다(스폰 목록에서 발동자 소유 플레이어 오브젝트).
    private PlayerInterruptSkillBase FindInterruptSkill(ulong casterClientId)
    {
        NetworkSpawnManager spawnManager = _networkManager != null ? _networkManager.SpawnManager : null;
        if (spawnManager == null)
            return null;

        foreach (NetworkObject networkObject in spawnManager.SpawnedObjectsList)
        {
            if (networkObject == null || !networkObject.IsPlayerObject || networkObject.OwnerClientId != casterClientId)
                continue;

            PlayerSkillController controller = networkObject.GetComponentInChildren<PlayerSkillController>();
            if (controller != null)
                return controller.GetSkill(PlayerSkillSlot.Interrupt) as PlayerInterruptSkillBase;
        }

        return null;
    }

    private void HandleFailed(uint triggerId)
    {
        // 강 시도가 빗나갔다(할 일 143-3) — 그 시도가 닫은 창은 다른 강 시도·성공이 없으면 다시 열린다.
        InterruptLocalWindowClaims.Local.AttemptEnded(triggerId);
        _ownerPresentation?.ServerFailed(triggerId, Time.unscaledTime);
        _peerLightGather.EndTrigger(triggerId);
        FailStage.Local(InterruptProductionEvent.LocalFail(triggerId));
    }

    private void HandleForcedReset(uint triggerId)
    {
        InterruptLocalWindowClaims.Local.AttemptEnded(triggerId);
        _ownerPresentation?.ForcedReset();
        _peerLightGather.EndAll();
        StopHitFlash();
        _suppressFlash?.Stop();
        SuccessStage.LocalForcedReset(_darkAdaptation);
    }

    // 섬광 순간(성공 Started + ②③④, 진행기 시계) — 오너 암전 끔(본인 화면) + 적중 지점 월드 섬광(모든 피어) + 암순응(남 = others, 오버레이 없던 본인 = self).
    private void HandleHitFlashMoment(InterruptHitFlashMoment moment)
    {
        if (moment.ViewerIsCaster)
            _ownerPresentation?.SuccessFlashMoment(moment.Event.TriggerId, Time.unscaledTime);

        PlayWorldFlash(FlashPoint(moment.Event.TargetPosition));

        // 보는 사람을 못 정하면(NetworkManager 없음) 암순응은 건너뛴다.
        SuccessStage.LocalFlash(moment, _networkManager != null ? _darkAdaptation : null);
    }

    // 제압 간파 섬광 시작(할 일 179, 모든 피어) — 시퀀스 첫 프레임이 섬광 순간을 대신한다: 오너 암전 걷힘(본인 화면)·월드 섬광(모든 피어).
    // 시퀀스 중엔 암순응을 틀지 않는다(화면을 통째로 3단 색으로 바꾼다). 시퀀스가 끝나면 제압 암순응 컷(할 일 192 Q7 — 진행기가 이어 튼다, 모든 피어).
    // 실루엣 = 마스크 대상 목록(지금은 오너 + 간파 대상). A7 대상 내부만이면 마스크 = 간파 대상만(그 안에만 그린다).
    private void BeginSuppressFlash(in InterruptProductionEvent e, bool viewerIsCaster)
    {
        Vector3 point = FlashPoint(e.TargetPosition);
        CollectSuppressSubjects(e.CasterClientId, e.TargetPosition, _suppressSubjects, _suppressTargets);
        SuppressFlashMaskMode mode = SuppressFlashRules.SelectMaskMode(suppressFlashTargetOnly, _suppressTargets.Count > 0);
        SuppressFlashRules.SelectMaskRoots(mode, _suppressSubjects, _suppressTargets, _suppressMaskRoots);
        SuppressFlash.Begin(Time.unscaledTime, point, _suppressMaskRoots, mode);

        // 오너 암전 걷기 — 지금 성공 단계의 발동 번호(서버 성공 = 슬로우 번호, 오너 판정 cue = 로컬 번호)로.
        if (viewerIsCaster && _ownerPresentation != null)
            _ownerPresentation.SuccessFlashMoment(_ownerPresentation.SuccessTriggerId, Time.unscaledTime);
        PlayWorldFlash(point);

        // 194 후속 — 시퀀스에 적중 라이트(기본 V3)를 더한다(대체 아님). 암순응은 늘 나 — 가는 시퀀스 뒤 제압 암순응 컷과 같은 화면 패스라
        // 먼저 끝나는 쪽이 다른 쪽을 지운다. 나(씬 조명 배율)·색 보정 Volume 은 시퀀스·암순응 컷과 출력이 달라 복구 순서가 엮이지 않는다.
        InterruptSuccessLook layered = SequenceHitLight(e.Tier);
        if (layered != InterruptSuccessLook.HitFlash)
            BeginHitLight(e, viewerIsCaster, layered, InterruptPresentationTierRules.SequenceHitLightDarkAdapt);
    }

    /// <summary>
    /// 제압 간파 섬광 실루엣 대상(할 일 179) — 발동자 플레이어 오브젝트 + 적중점에 가장 가까운 간파 대상(등록 캐시).
    /// 191(간파 동시 성공) 이후 동시 성공자는 이 목록에 더하면 된다 — 출력은 목록만 받는다.
    /// <paramref name="targets"/> = 그중 간파 대상만(A7 대상 내부만 마스크) — 동시 성공자는 여기에 넣지 않는다.
    /// </summary>
    private void CollectSuppressSubjects(ulong casterClientId, Vector3 targetPosition, List<GameObject> results,
        List<GameObject> targets)
    {
        results.Clear();
        targets.Clear();
        NetworkSpawnManager spawnManager = _networkManager != null ? _networkManager.SpawnManager : null;
        if (spawnManager != null)
        {
            foreach (NetworkObject networkObject in spawnManager.SpawnedObjectsList)
            {
                if (networkObject != null && networkObject.IsPlayerObject && networkObject.OwnerClientId == casterClientId)
                {
                    results.Add(networkObject.gameObject);
                    break;
                }
            }
        }

        InterruptTargetRegistry.Entry nearest = FindNearestTarget(targetPosition);
        if (nearest != null)
        {
            results.Add(nearest.Unit.gameObject);
            targets.Add(nearest.Unit.gameObject);
        }
    }

    // 성공 기준 위치에 가장 가까운 등록 간파 대상(수평 거리, 허용 반경 안). 없으면 null.
    private InterruptTargetRegistry.Entry FindNearestTarget(Vector3 targetPosition)
    {
        InterruptTargetRegistry.CopyLiveTo(_targetBuffer);
        InterruptTargetRegistry.Entry nearest = null;
        float best = SuppressFlashTargetSearchRadius * SuppressFlashTargetSearchRadius;
        for (int i = 0; i < _targetBuffer.Count; i++)
        {
            Vector3 delta = _targetBuffer[i].Unit.transform.position - targetPosition;
            delta.y = 0f;
            if (delta.sqrMagnitude <= best)
            {
                best = delta.sqrMagnitude;
                nearest = _targetBuffer[i];
            }
        }

        _targetBuffer.Clear();
        return nearest;
    }

    // 성공 기준 위치(판정 순간 대상 위치)에서 이 피어의 대상까지 허용 거리 — 복제 지연으로 조금 어긋나도 찾는다.
    private const float SuppressFlashTargetSearchRadius = 6f;

    private Vector3 FlashPoint(Vector3 targetPosition) => targetPosition + Vector3.up * hitFlashHeightOffset;

    // 월드 섬광 파티클·적중 지점 라이트 — 효과 칸(§17)이 둘 다 꺼져 있으면 만들지도 않는다.
    private void PlayWorldFlash(Vector3 position)
    {
        if (!showWorldFlash && !showHitLight)
            return;

        if (_hitFlashWorld == null)
        {
            if (hitFlashWorldVfx == null)
                return;

            _hitFlashWorld = Instantiate(hitFlashWorldVfx, transform);
            _hitFlashWorld.name = hitFlashWorldVfx.name;
        }

        _hitFlashWorld.Play(position, hitFlashWorldScale, showWorldFlash, showHitLight);
    }

    // 강제 리셋·꺼짐·서버 거부 — 오너 화면 효과 즉시 복구(오버레이·플레어·블러·Volume 가중치), 남은 섬광 순간 취소, 월드 섬광 지움.
    // 적중 라이트 연출(V1·V2, 할 일 194)도 같이 끈다 — 성공 섬광을 끄는 곳이 이 한 곳이다.
    private void StopHitFlash()
    {
        _hitFlash?.ForcedReset();
        if (_hitFlashWorld != null)
            _hitFlashWorld.Stop();
        _hitLight?.Stop();
    }

    // 적중 라이트 연출 시작(할 일 194, 모든 피어) — 성공 순간이 곧 섬광 순간이다: 오너 성공 단계를 끝내고(본인 화면, 암전 없는 단계라 덮개는 이미 0)
    // 단계 칸 hitLightOrigin(틈 수직 기본 / 무기 / 대상) 위치·방향에 라이트·불꽃·파편·빛줄기 + 블룸·암순응(+V2 화이트아웃, 본인 화면 — 적중 지점 기준).
    // darkAdapt 를 주면 단계 칸 대신 그 방식(시퀀스에 더한 적중 라이트 = 늘 나).
    private void BeginHitLight(in InterruptProductionEvent e, bool viewerIsCaster, InterruptSuccessLook look,
        InterruptDarkAdaptMode? darkAdapt = null)
    {
        if (viewerIsCaster && _ownerPresentation != null)
            _ownerPresentation.SuccessFlashMoment(_ownerPresentation.SuccessTriggerId, Time.unscaledTime);

        InterruptTierProfile profile = TierProfile(e.Tier);
        HitLight.Begin(new InterruptHitLightRequest
        {
            Point = FlashPoint(e.TargetPosition),
            CasterClientId = e.CasterClientId,
            Origin = profile != null ? profile.hitLightOrigin : InterruptHitLightOrigin.Gap,
            ViewerIsCaster = viewerIsCaster,
            Look = look,
            DarkAdapt = darkAdapt ?? (profile != null ? profile.darkAdapt : InterruptDarkAdaptMode.ScreenPass),
        }, Time.unscaledTime, Time.frameCount);
    }

    // 빛 모임 — 발동자 소유의 켜진 InterruptLightGatherSocket(Player.prefab base 루트). 찾는 방식은 FindSuccessFxSinks 와 같다.
    private IInterruptLightGatherPlayer FindLightGatherSocket(ulong casterClientId)
    {
        NetworkSpawnManager spawnManager = _networkManager != null ? _networkManager.SpawnManager : null;
        if (spawnManager == null)
            return null;

        foreach (NetworkObject networkObject in spawnManager.SpawnedObjectsList)
        {
            if (networkObject == null || networkObject.OwnerClientId != casterClientId)
                continue;

            if (networkObject.TryGetComponent(out InterruptLightGatherSocket socket) && socket.isActiveAndEnabled)
                return socket;
        }

        return null;
    }

    // A4 — 발동자 플레이어의 성공 이펙트 칸. 스폰된 오브젝트 중 발동자 소유의 켜진 InterruptSuccessFx(옛 구독 조건과 같다).
    // SpawnManager.GetPlayerNetworkObject 는 클라에서 남의 것을 찾으면 에러를 찍는다 — 스폰 목록에서 직접 찾는다.
    private List<IInterruptCueSink> FindSuccessFxSinks(ulong casterClientId)
    {
        _sinkBuffer.Clear();
        NetworkSpawnManager spawnManager = _networkManager != null ? _networkManager.SpawnManager : null;
        if (spawnManager == null)
            return _sinkBuffer;

        foreach (NetworkObject networkObject in spawnManager.SpawnedObjectsList)
        {
            if (networkObject == null || networkObject.OwnerClientId != casterClientId)
                continue;

            foreach (InterruptSuccessFx fx in networkObject.GetComponents<InterruptSuccessFx>())
            {
                if (fx.isActiveAndEnabled)
                    _sinkBuffer.Add(fx);
            }
        }

        return _sinkBuffer;
    }

    // ---- 서버 사건 (간파 스킬이 알린다 — 인스턴스 없으면 아무것도 안 한다) ----
    // 판정·피해·카운터 창은 스킬 소관. 여기는 GlobalTimeScale.ServerStart/ServerFail 과 캐릭터 성공 relay 만 부른다.

    /// <summary>[서버] 시도 — 성공 예측 = 강 / 실패 예측 = 약 시도 슬로우를 건다(할 일 132). 발동 번호(0 = 없음)를 돌려준다.</summary>
    public static uint ServerAttempt(in InterruptProductionEvent e) => Instance != null ? Instance.AttemptStage.Server(e) : 0;

    /// <summary>[서버] 판정 — 시도 슬로우 정리(빗나감이면 실패 복귀).</summary>
    public static void ServerResolve(in InterruptProductionEvent e)
    {
        if (Instance != null)
            Instance.ResolveStage.Server(e);
    }

    /// <summary>[서버] 성공 — 성공 슬로우(+A1)를 걸고 시전자의 캐릭터 성공 relay 를 부른다. 판정 사건 바로 뒤에 알린다.</summary>
    public static void ServerSuccess(in InterruptProductionEvent e)
    {
        if (Instance != null)
            Instance.SuccessStage.Server(e.WithSuppressInterrupt(Instance._suppressMark.Consume(Time.frameCount)),
                FindSuccessRelays(e.Caster));
    }

    /// <summary>
    /// [서버] 제압 간파 표식(할 일 179) — 대상(23호)이 간파 성공 수신 안에서 제압 칸 0 에 닿았을 때 부른다. 같은 프레임의 다음 <see cref="ServerSuccess"/> 가
    /// 소비해 슬로우 Start 이벤트에 실어 전 피어에 보낸다(새 RPC 없음). 판정은 그대로 — 표식만.
    /// </summary>
    public static void ServerMarkSuppressInterrupt()
    {
        if (Instance != null)
            Instance._suppressMark.Mark(Time.frameCount);
    }

    /// <summary>[서버] 실패 — 판정 전 종료·앵커 누락. 모든 피어에서 불려도 발동 번호가 0 이면 아무것도 안 한다.</summary>
    public static void ServerFail(in InterruptProductionEvent e)
    {
        if (Instance != null)
            Instance.FailStage.Server(e);
    }

    // 성공은 드물어 사건마다 찾는다(값 캐싱 없음 규칙과도 맞다).
    private static IInterruptSuccessRelay[] FindSuccessRelays(Player caster) =>
        caster != null ? caster.GetComponentsInChildren<IInterruptSuccessRelay>(true) : null;
}
