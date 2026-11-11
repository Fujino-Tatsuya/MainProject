using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 🔸 어쌔신 임시 VFX 창구(PLAN-assassin A12) — 민경 정식 VFX 가 오면 프리팹 필드만 교체한다(거너 GunnerBeamView 선례).
/// 스킬(PlayerSkillBase)은 RPC 를 가질 수 없으므로 서버 판정 결과를 퍼뜨리는 창구도 여기 둔다.
///
/// - 변신 중 루프: <see cref="AssassinState"/> 의 전 피어 이벤트로 켜고 끈다(늦은 접속은 스폰 시 현재 상태로 맞춤).
///   🔹 정식 VFX — <c>EffectCatalog.Assassin_R_BladeVortex</c> 를 EffectManager 로 재생한다(2026-10-14 민경).
///   변신 시작 = PlayLooping(0.5초에 차오름), 종료(만료·수동 해제·쓰러짐 공통) = Release(0.5초에 흩어짐).
/// - Q 돌진 궤적 · 변신 E 원: 스킬의 OnClientPlay/OnEnd(전 피어)가 로컬로 부른다 — RPC 없음.
///   (일반 E 시전 임시 연출 Flash_star 는 2026-10-20 삭제 — 차지·오라·날 발광은 AssassinEnhanceReadyView.)
///   🔹 변신 E 원 = 정식 VFX <c>EffectCatalog.Assassin_E_MultiSlashes</c> 루프(2026-10-15 민경) — 확정 시 원 중심에서
///   배율 = 원 반경으로 켜고, 스킬 OnEnd(5타 완료·중단 공통)에서 Release 한다. (확정 순간 임시 1회 Burst_rings 는 2026-10-30 삭제.)
///   🔹 변신 Q 돌진 = 정식 VFX <c>EffectCatalog.Assassin_Q_StormSlash</c> 루프(2026-10-15 민경) — 플레이어 루트를 따라가며
///   지나간 자리에 자국을 남긴다(입자 World + Rate over Distance).
///   🔹 일반 Q 돌진 = 정식 VFX <c>Assassin_Q_PierceLine</c>·<c>Assassin_Q_PierceBurst</c>(2026-10-16 민경) — 시작점을 기억했다가
///   스킬 OnEnd 에서 지나온 경로 위로 일섬(Z 배율 = 실제 이동 거리)을 긋고 끝점에 버스트. 임시 돌진 궤적은 제거.
/// - 평타 베기: 클립 Hit 애니 이벤트(전 피어에서 재생)마다 <see cref="AssassinBasicAttack"/> 가 부른다 — RPC 없음.
///   🔹 정식 VFX(2026-10-19 민경) — 소켓은 <c>Player_Assassin/VFX/*</c> 의 <see cref="EffectSocketPlayer"/>(엔트리·위치는 프리팹에서):
///   일반 1~4타·변신 묶음은 클립의 <see cref="EffectAnimEvents"/> 이벤트(<c>PlayEffect</c> / <c>StartEffect</c>·<c>StopEffect</c>)가
///   직접 재생한다 — 여기서 하지 않는다. 여기 남은 것: 적중 성공 = [서버] <see cref="ServerNormalSlashHit"/> → 전 피어
///   <c>normalSlashSuccess[타]</c> 원샷(일반 베기와 겹쳐 재생 — 서버 판정이라 애니 이벤트로 못 낸다),
///   변신 루프 안전 정지 <see cref="EndTransformedSlash"/>(묶음이 StopEffect 프레임 전에 끊길 때).
///   강타 슬래쉬도 <c>Combo_Attack_02_01</c> 의 <c>PlayEffect("Assassin_EnhancedSlash")</c> 이벤트가 낸다(2026-10-20, 임시 프리팹 경로 삭제).
///   일반 E 버프(날 발광·오라·차지)는 <see cref="AssassinEnhanceReadyView"/>(Assassin_Armature) 담당.
/// - 우클릭 간파: 🔹 정식 VFX(2026-10-19 민경) — 기 모으기·튀기기·올려치기는 <c>Attack_Up_01</c> 클립의 <see cref="EffectAnimEvents"/>
///   이벤트가 <c>Player_Assassin/VFX/RightClick/*</c> 소켓에서 직접 재생한다. 여기 남은 것: 간파 성공 = [서버]
///   간파 연출 디렉터 성공 단계의 relay(<see cref="IInterruptSuccessRelay"/>, 할 일 118) → 전 피어 "성공 대기" → 올려치기 소켓 id 가
///   재생되는 순간 디렉터에 C2 칸을 묻고 <c>insightSuccess</c> 원샷(알림이 더 늦으면 받는 즉시), 기 모으기 안전 정지 <see cref="EndInterrupt"/>(스킬 OnEnd).
/// - 백어택 적중 연출 없음 — 임시 Burst_sharp 와 그 RPC 는 2026-10-30 삭제(민경). 정식 연출을 붙일 땐 판정(<see cref="AssassinHitTargets.IsBackAttack"/>) 쪽에서 다시 연결한다.
/// </summary>
[RequireComponent(typeof(AssassinState))]
public sealed class AssassinSkillView : BaseNetworkBehaviour, IInterruptSuccessRelay, IInterruptCueSink
{
    [Header("변신 루프 — EffectCatalog.Assassin_R_BladeVortex")]
    [Tooltip("플레이어 루트 기준 월드 오프셋. 배율에 곱해지지 않는다 — 프리팹 피벗이 발밑이라 기본 0.")]
    [SerializeField] private Vector3 transformEffectOffset;
    [Tooltip("프리팹 저작 크기에 곱하는 배율.")]
    [SerializeField, Min(0.01f)] private float transformEffectScale = 1f;

    /// <summary>변신 오라 오프셋·배율 — 캐릭터 선택 미리보기(할 일 199)가 같은 값으로 켠다.</summary>
    public Vector3 TransformEffectOffset => transformEffectOffset;
    public float TransformEffectScale => transformEffectScale;

    [Header("변신 E 원형 5타 — EffectCatalog.Assassin_E_MultiSlashes")]
    [Tooltip("배율 = 원 반경(m) × 이 값. 1 = 프리팹의 입자 생성 원(반경 1m 저작)이 바닥 원 표시와 같은 크기.\n" +
             "베기·바닥 자국 자체 크기 때문에 실제로 보이는 범위는 원보다 넓다 — 외곽을 원에 맞추려면 0.4~0.5.")]
    [SerializeField, Min(0.01f)] private float circleStrikeScaleFactor = 1f;

    [Header("변신 Q 돌진 — EffectCatalog.Assassin_Q_StormSlash")]
    [Tooltip("플레이어 루트 기준 월드 오프셋. 배율에 곱해지지 않는다.")]
    [SerializeField] private Vector3 stormSlashOffset;
    [Tooltip("프리팹 저작 크기에 곱하는 배율.")]
    [SerializeField, Min(0.01f)] private float stormSlashScale = 1f;

    [Header("일반 Q 돌진 — EffectCatalog.Assassin_Q_PierceLine / PierceBurst")]
    [Tooltip("일섬 두께·높이와 버스트 크기에 곱하는 배율(길이는 실제 이동 거리). 프리팹은 허리 높이(1m)로 저작돼 있다.")]
    [SerializeField, Min(0.01f)] private float pierceScale = 1f;
    [Tooltip("이 거리(m)보다 짧게 끝나면(돌진 직후 벽 등) 일섬은 생략하고 버스트만 낸다.")]
    [SerializeField, Min(0f)] private float pierceMinLength = 0.3f;

    // 그림자 회귀(ASN_2_RETURN) 보유 시 Q 돌진 연출 색(2026-11-09 민경). 색 지정 재생(EffectTint) — 색상(hue)만 이 색으로 바꾸고
    // 밝기·알파·흐름은 원래 이펙트 그대로. 특성 목록은 복제되므로 모든 피어가 같은 색으로 본다.
    [Header("그림자 회귀 Q 색 — 일반 Q 일섬·버스트, 변신 Q Storm Slash (복귀 돌진 포함)")]
    [Tooltip("그림자 회귀를 찍은 어쌔신의 Q 돌진 연출 색. 색상(hue)만 쓴다 — 밝기·알파는 원래 이펙트 것.")]
    [SerializeField] private Color shadowReturnDashColor = new Color32(0x4B, 0x00, 0x82, 0xFF);
    [Tooltip("원래 채도에 곱하는 값(1 = 그대로). 흰 심은 이 값과 무관하게 흰 채로 남는다.")]
    [SerializeField, Min(0f)] private float shadowReturnDashSaturation = 1f;

    [Header("평타 베기 — EffectSocketPlayer(Player_Assassin/VFX/*, 엔트리·소켓은 각 컴포넌트에서)")]
    [Tooltip("일반 1~4타 적중 성공 — 서버 판정 → RPC 로 같은 타 일반 베기와 겹쳐 원샷. 인덱스 = 타 순서(0~3). 엔트리 = Assassin_LClick_Success.\n" +
             "(일반 베기 자체는 클립의 PlayEffect 이벤트가 낸다.)")]
    [SerializeField] private EffectSocketPlayer[] normalSlashSuccess = new EffectSocketPlayer[4];
    [Tooltip("변신 평타 묶음 루프 — 켜고 끄는 건 클립의 StartEffect/StopEffect 이벤트. 여기는 묶음이 끊길 때의 안전 정지에만 쓴다.")]
    [SerializeField] private EffectSocketPlayer transformedSlash;
    [Tooltip("일반 E 강타 베기(id Assassin_EnhancedSlash) — 칼날의 영역(ASN_3_E_AREA) 판정 배율만큼 키운다(저작 배율 × 사거리 배율, 균일).")]
    [SerializeField] private EffectSocketPlayer enhancedSlash;

    [Header("우클릭 간파 — EffectSocketPlayer(Player_Assassin/VFX/RightClick/*)")]
    [Tooltip("기 모으기 루프 — 켜고 끄는 건 Attack_Up_01 의 StartEffect/StopEffect 이벤트. 여기는 시전이 그 프레임 전에 끊길 때의 안전 정지에만 쓴다.")]
    [SerializeField] private EffectSocketPlayer insightWindup;
    [Tooltip("올려치기 — 클립의 PlayEffect 이벤트가 낸다. 여기서는 이 소켓의 Id 가 재생되는 순간을 성공 연출의 기준 프레임으로만 쓴다.")]
    [SerializeField] private EffectSocketPlayer insightSlash;
    [Tooltip("간파 성공 — 서버 판정 → RPC → 올려치기와 함께 원샷. id 비워 둠(코드 전용). 엔트리 = Assassin_RClick_Success.")]
    [SerializeField] private EffectSocketPlayer insightSuccess;

    private AssassinState state;
    private float enhancedSlashBaseScale = -1f;   // 강타 소켓 저작 배율(처음 한 번 읽음)
    private EffectHandle transformLoop;
    private bool presentationVortex;   // 보스 처치 연출 오라(267 후속) — 연출 끝까지 실제 변신 해제와 무관하게 유지
    private EffectHandle circleStrikeLoop;
    private float circleStrikeDeadline;
    private EffectHandle stormSlashLoop;
    private float stormSlashDeadline;
    private Vector3 pierceStart;
    private Vector3 pierceDirection;
    private bool pierceArmed;
    private Player player;
    private EffectAnimEvents animEvents;
    private bool insightSuccessPending;
    private bool insightSlashPlayed;

    private void Awake()
    {
        state = GetComponent<AssassinState>();
        if (state != null)
            state.TransformChanged += HandleTransformChanged;
        player = GetComponent<Player>();
        animEvents = GetComponent<EffectAnimEvents>();
        if (animEvents != null)
            animEvents.Played += HandleAnimEffectPlayed;
    }

    private void Start() => SyncLoops();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // 늦은 접속 — 초기 동기화는 변경 이벤트를 내지 않으므로 현재 스냅샷으로 맞춘다.
        SyncLoops();
    }

    public override void OnDestroy()
    {
        if (state != null)
            state.TransformChanged -= HandleTransformChanged;
        if (animEvents != null)
            animEvents.Played -= HandleAnimEffectPlayed;
        // 디스폰·씬 언로드 — 아웃트로 없이 바로 반납(추종 대상이 사라지므로).
        if (EffectManager.Instance != null)
        {
            if (transformLoop.IsSet)
                EffectManager.Instance.ReleaseImmediate(transformLoop);
            if (circleStrikeLoop.IsSet)
                EffectManager.Instance.ReleaseImmediate(circleStrikeLoop);
            if (stormSlashLoop.IsSet)
                EffectManager.Instance.ReleaseImmediate(stormSlashLoop);
        }
        transformLoop = EffectHandle.None;
        circleStrikeLoop = EffectHandle.None;
        stormSlashLoop = EffectHandle.None;
        base.OnDestroy();
    }

    // ── 스킬이 부르는 로컬 연출(전 피어) ──

    /// <summary>스킬(PlayerSkillBase)이 시전자에게 붙은 창구를 찾아 연출을 부른다. 창구가 없으면 아무것도 안 한다.</summary>
    public static void Play(Component owner, System.Action<AssassinSkillView> play)
    {
        if (owner == null)
            return;
        AssassinSkillView view = owner.GetComponent<AssassinSkillView>();
        if (view != null)
            play(view);
    }

    /// <summary>
    /// [전 피어] 변신 묶음 끝·취소·일반 모드 타 시작 — 안전 정지(엔트리 outroDuration 동안 남은 입자가 사라진다). 꺼져 있으면 무시.
    /// 평소엔 클립의 StopEffect 이벤트가 끄지만, 피격·대시·변신 해제로 묶음이 그 프레임 전에 끊기면 이게 끈다.
    /// </summary>
    public void EndTransformedSlash()
    {
        if (transformedSlash != null && transformedSlash.IsPlaying)
            transformedSlash.Stop();
    }

    private static T At<T>(T[] array, int index) where T : Object =>
        array != null && index >= 0 && index < array.Length ? array[index] : null;

    /// <summary>
    /// [전 피어] 우클릭 간파 시전 — 지난 시전의 성공 대기·올려치기 표시를 지운다. 연출 재생은 클립 이벤트가 한다.
    /// </summary>
    public void BeginInterrupt()
    {
        insightSuccessPending = false;
        insightSlashPlayed = false;
    }

    /// <summary>
    /// [전 피어] 간파 스킬 종료(완료·피격·사망 공통) — 기 모으기 안전 정지. 꺼져 있으면 무시.
    /// 성공 대기는 지우지 않는다 — 스킬 종료(0.6초)가 올려치기 프레임과 겹치거나 앞설 수 있어서, 다음 시전에서 지운다.
    /// </summary>
    public void EndInterrupt()
    {
        if (insightWindup != null && insightWindup.IsPlaying)
            insightWindup.Stop();
    }

    /// <summary>[전 피어] 일반 Q 돌진 시작 — 일섬을 그을 시작점·방향만 기억한다(이 시점엔 아무것도 재생하지 않는다).</summary>
    public void BeginPierce(Vector3 start, Vector3 direction)
    {
        direction.y = 0f;
        pierceStart = start;
        pierceDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        pierceArmed = true;
    }

    /// <summary>
    /// [전 피어] 일반 Q 종료(사유 무관) — 시작점에서 지금 위치까지 지나온 경로 위로 일섬을 긋고 끝점에 버스트.
    /// 길이 = 돌진 방향으로 실제 나아간 거리(벽 조기 종료면 그만큼 짧다). 서버 종료가 돌진 끝 + 0.1초라
    /// "베인 자리가 한 박자 늦게 드러나는" 타이밍이 된다.
    /// </summary>
    public void EndPierce()
    {
        if (!pierceArmed)
            return;
        pierceArmed = false;
        if (!EffectManager.TryGet(out EffectManager effects, this))
            return;

        Vector3 end = transform.position;
        float length = Mathf.Max(0f, Vector3.Dot(end - pierceStart, pierceDirection));
        Quaternion rotation = Quaternion.LookRotation(pierceDirection, Vector3.up);
        Vector3 endPoint = pierceStart + pierceDirection * length;

        EffectEntry line = effects.Catalog.Assassin_Q_PierceLine;
        EffectEntry burst = effects.Catalog.Assassin_Q_PierceBurst;
        if (line == null || burst == null)
            Edit.LogWarning("[AssassinSkillView] EffectCatalog.Assassin_Q_PierceLine/PierceBurst 가 비어 일반 Q 연출 일부를 건너뛴다.", this);

        EffectTint tint = DashTint;
        if (line != null && length >= pierceMinLength)
        {
            Vector3 lineScale = new Vector3(pierceScale, pierceScale, length);
            if (tint.IsSet)
                effects.SetTint(effects.PlayRecallable(line, pierceStart, rotation, lineScale), tint);
            else
                effects.Play(line, pierceStart, rotation, lineScale);
        }
        if (burst != null)
        {
            if (tint.IsSet)
                effects.SetTint(effects.PlayRecallable(burst, endPoint, rotation, pierceScale), tint);
            else
                effects.Play(burst, endPoint, rotation, pierceScale);
        }
    }

    /// <summary>Q 돌진 연출 색 — 그림자 회귀 보유면 <see cref="shadowReturnDashColor"/>, 아니면 원래 색.</summary>
    private EffectTint DashTint
    {
        get
        {
            PlayerTraitModifiers traits = player != null ? PlayerTraitModifiers.For(player) : null;
            return traits != null && traits.HasTrait(TraitModeIds.AssassinShadowReturn)
                ? EffectTint.FromColor(shadowReturnDashColor, shadowReturnDashSaturation)
                : EffectTint.None;
        }
    }

    /// <summary>
    /// [전 피어] 변신 E 공격 시작 — 원 중심에서 루프를 켠다. 배율 = <paramref name="radius"/> × 칼날의 영역 반경 배율 × circleStrikeScaleFactor.
    /// <paramref name="maxDuration"/>(스킬 최대 지속) + 1초 안에 <see cref="EndCircleStrike"/> 가 안 오면 스스로 해제한다 —
    /// 클라의 종료 RPC 가 이미 정리된 스킬에 막혀 OnEnd 가 안 불리는 경우의 안전망.
    /// </summary>
    public void BeginCircleStrike(Vector3 center, float radius, float maxDuration)
    {
        EndCircleStrike();   // 재진입 — 남은 루프는 먼저 걷는다
        if (!EffectManager.TryGet(out EffectManager effects, this))
            return;

        EffectEntry entry = effects.Catalog.Assassin_E_MultiSlashes;
        if (entry == null)
        {
            Edit.LogWarning("[AssassinSkillView] EffectCatalog.Assassin_E_MultiSlashes 가 비어 변신 E 연출을 건너뛴다.", this);
            return;
        }

        // 칼날의 영역 — 판정과 같은 반경 배율(미보유 = 1). 판정 원과 VFX 원을 맞춘다.
        float areaMultiplier = AssassinTraitAreaRules.ScaleRadius(player != null ? player.TraitModifiers : null, AssassinTraitAreaRules.CircleStrike, 1f);
        circleStrikeLoop = effects.PlayLooping(entry, center, Quaternion.identity, Mathf.Max(0.01f, radius * areaMultiplier * circleStrikeScaleFactor));
        circleStrikeDeadline = Time.time + Mathf.Max(0f, maxDuration) + 1f;
    }

    /// <summary>[전 피어] 변신 E 종료(사유 무관) — 방출만 멈추고 남은 입자는 엔트리 outroDuration 동안 사라지게 둔다.</summary>
    public void EndCircleStrike()
    {
        if (circleStrikeLoop.IsSet && EffectManager.Instance != null)
            EffectManager.Instance.Release(circleStrikeLoop);
        circleStrikeLoop = EffectHandle.None;
    }

    /// <summary>
    /// [전 피어] 변신 Q 돌진 시작 — 플레이어 루트를 따라가는 루프를 켠다. 입자는 World + Rate over Distance 라 움직인 만큼
    /// 지나간 자리에 남는다. <paramref name="maxDuration"/>(스킬 최대 지속) + 1초 안에 <see cref="EndStormSlash"/> 가 안 오면 스스로 해제한다.
    /// </summary>
    public void BeginStormSlash(float maxDuration)
    {
        EndStormSlash();   // 재진입 — 남은 루프는 먼저 걷는다
        if (!EffectManager.TryGet(out EffectManager effects, this))
            return;

        EffectEntry entry = effects.Catalog.Assassin_Q_StormSlash;
        if (entry == null)
        {
            Edit.LogWarning("[AssassinSkillView] EffectCatalog.Assassin_Q_StormSlash 가 비어 변신 Q 연출을 건너뛴다.", this);
            return;
        }

        stormSlashLoop = effects.PlayLooping(entry, transform, stormSlashOffset, stormSlashScale);
        effects.SetTint(stormSlashLoop, DashTint);   // 그림자 회귀 없으면 None = 원래 색
        stormSlashDeadline = Time.time + Mathf.Max(0f, maxDuration) + 1f;
    }

    /// <summary>[전 피어] 변신 Q 종료(사유 무관) — 방출만 멈추고 남은 자국은 엔트리 outroDuration 동안 사라지게 둔다.</summary>
    public void EndStormSlash()
    {
        if (stormSlashLoop.IsSet && EffectManager.Instance != null)
            EffectManager.Instance.Release(stormSlashLoop);
        stormSlashLoop = EffectHandle.None;
    }

    /// <summary>
    /// [이 피어] 보스 처치 연출 — 변신 오라(Assassin_R_BladeVortex)를 실제 변신 없이 켠다(267 후속, 이미 변신 중이면 그 루프 그대로).
    /// 처치 연출이 끝나거나 취소되면(<see cref="BossVictoryGlitchPresentation.IsRunning"/> false) Update 가 실제 변신 상태로 되돌린다 — 아니면 outro 로 흩어짐.
    /// </summary>
    public void PlayPresentationVortex()
    {
        presentationVortex = true;
        HandleTransformChanged(true);
    }

    /// <summary>처치 연출 오라를 접을 때인가 — 오라를 켰고 연출이 끝났다(순수 판단, EditMode).</summary>
    public static bool ShouldEndPresentationVortex(bool presentationVortex, bool victoryRunning) => presentationVortex && !victoryRunning;

    private void Update()
    {
        if (ShouldEndPresentationVortex(presentationVortex, BossVictoryGlitchPresentation.IsRunning))
        {
            presentationVortex = false;
            HandleTransformChanged(state != null && state.IsTransformed);
        }
        SyncEnhancedSlashScale();
        if (circleStrikeLoop.IsSet && Time.time >= circleStrikeDeadline)
            EndCircleStrike();
        if (stormSlashLoop.IsSet && Time.time >= stormSlashDeadline)
            EndStormSlash();
    }

    // ── 추격의 궤적(ASN_4_Q_TRAIL) ──

    private void OnEnable() => TraitSkillFx.Raised += HandleTraitFx;

    private void OnDisable() => TraitSkillFx.Raised -= HandleTraitFx;

    // [전 피어] 추격의 궤적 추가 피해 판정 순간(서버가 Q 종료 + 지연 ÷ 공격속도 뒤 판정 → 공용 RPC, 빗나가도 옴) — 지나온 선분에
    // FX_PursuitTrail(바닥 그림자 자국 + 경로 전체가 팡 터지는 베기 줄 + 파편) 한 번. +Z 1m 저작 → Z 배율 = 선분 길이(일반 Q 일섬과 같은 규칙).
    // 일반·변신 Q, 그림자 회귀 왕복 각각 같은 연출. 대상별(Hit) 연출은 없다(피격 반응·피해 숫자).
    private void HandleTraitFx(TraitFxEvent e)
    {
        if (e.Trait != TraitFxTrait.AssassinPursuitTrail || e.Kind != TraitFxKind.Activated)
            return;
        if (e.Caster == null || e.Caster != player)   // player = Awake 의 GetComponent<Player>()
            return;
        if (!EffectManager.TryGet(out EffectManager effects, this))
            return;
        EffectEntry entry = effects.Catalog.Assassin_Q_PursuitTrail;
        if (entry == null)
        {
            Edit.LogWarning("[AssassinSkillView] EffectCatalog.Assassin_Q_PursuitTrail 이 비어 추격의 궤적 연출을 건너뛴다.", this);
            return;
        }

        Vector3 span = e.EndPosition - e.Position;
        span.y = 0f;
        float length = span.magnitude;
        if (length < AssassinTrailRules.MinLength)
            return;
        effects.Play(entry, e.Position, Quaternion.LookRotation(span / length, Vector3.up), new Vector3(1f, 1f, length));
    }

    // 칼날의 영역(ASN_3_E_AREA) — 일반 E 강타 베기를 판정 사거리 배율만큼 균일하게 키운다(사거리·각도 배율이 같은 1.25 라 수평 범위가 판정과 맞는다).
    // 배율은 재생(대출) 시점에 확정되므로 클립 이벤트 전에 매 프레임 밀어 둔다. 특성 수치는 전 피어 같은 값.
    private void SyncEnhancedSlashScale()
    {
        if (enhancedSlash == null)
            return;
        if (enhancedSlashBaseScale < 0f)
            enhancedSlashBaseScale = enhancedSlash.Scale;
        AssassinTraitAreaRules.AreaScale(player != null ? player.TraitModifiers : null, AssassinTraitAreaRules.EnhancedStrike, out float length, out _);
        float target = enhancedSlashBaseScale * length;
        if (!Mathf.Approximately(enhancedSlash.Scale, target))
            enhancedSlash.SetScale(target);
    }

    // ── 서버 판정 → 전 피어 ──

    /// <summary>
    /// [서버] 일반 평타 <paramref name="stepIndex"/> 타가 무언가를 맞혔다 — 전 피어에서 그 타의 성공 연출을 일반 베기 위에 겹쳐 낸다.
    /// 팔라딘 <c>DefaultAttackController.PlaySlashVfxRpc</c> 와 같은 방식(호스트는 같은 프레임, 원격은 RPC 지연만큼 늦게 겹친다).
    /// </summary>
    public void ServerNormalSlashHit(int stepIndex)
    {
        if (IsNetworkActive)
            NormalSlashHitRpc(stepIndex);
        else
            PlayNormalSlashSuccess(stepIndex);
    }

    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void NormalSlashHitRpc(int stepIndex) => PlayNormalSlashSuccess(stepIndex);

    private void PlayNormalSlashSuccess(int stepIndex)
    {
        EffectSocketPlayer success = At(normalSlashSuccess, stepIndex);
        if (success != null)
            success.PlayOnce();
    }

    // [서버] 간파 성공 — 디렉터 성공 단계가 시전자 아래 relay 를 부른다(할 일 118). 전 피어에 성공 대기를 알린다. 1회성 판정 결과라 Reliable.
    // 꺼진 컴포넌트는 옛 OnEnable 구독처럼 빠진다.
    void IInterruptSuccessRelay.ServerRelaySuccess(in InterruptProductionEvent e)
    {
        if (!isActiveAndEnabled || e.Caster == null || e.Caster != player)
            return;
        if (IsNetworkActive)
            InterruptSucceededRpc();
        else
            MarkInsightSuccess();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void InterruptSucceededRpc() => MarkInsightSuccess();

    private void MarkInsightSuccess()
    {
        insightSuccessPending = true;
        TryPlayInsightSuccess();
    }

    // [전 피어] 클립이 재생한 이펙트 — 올려치기 소켓이면 성공 연출의 기준 프레임.
    private void HandleAnimEffectPlayed(string id)
    {
        if (insightSlash == null || string.IsNullOrEmpty(id) || id != insightSlash.Id)
            return;
        insightSlashPlayed = true;
        TryPlayInsightSuccess();
    }

    // 성공 알림과 올려치기 프레임이 둘 다 왔을 때 한 번 — 순서는 상관없다(알림이 늦으면 받는 즉시).
    private void TryPlayInsightSuccess()
    {
        if (!insightSuccessPending || !insightSlashPlayed)
            return;
        insightSuccessPending = false;
        insightSlashPlayed = false;
        // C2 칸은 디렉터가 본다(피어마다 자기 값) — 켜져 있으면 PlayCue 로 돌려 부른다.
        if (insightSuccess != null)
            InterruptProductionDirector.PlayCue(InterruptProductionCue.AssassinInsightSuccess,
                InterruptProductionEvent.LocalCue(InterruptProductionPhase.Success, IsSpawned ? OwnerClientId : 0, player), this);
    }

    // [각 피어] C2 간파 성공 원샷.
    void IInterruptCueSink.PlayCue(InterruptProductionCue cue, in InterruptProductionEvent e)
    {
        if (cue == InterruptProductionCue.AssassinInsightSuccess && insightSuccess != null &&
            InterruptVfxToggles.IsOn(InterruptVfx.AssassinSuccess)) // 238 랩 토글
            insightSuccess.PlayOnce();
    }

    // ── 루프 ──

    // 변신 루프 — 켜면 차오르고(프리팹이 0.5초 안에 스스로 채운다), 끄면 엔트리의 outro 로 흩어진다
    // (End 원샷 + 남은 입자 ×outroPlayRate). 쓰러짐·사망도 같은 이벤트로 온다.
    private void HandleTransformChanged(bool on)
    {
        if (on)
        {
            if (transformLoop.IsSet || !EffectManager.TryGet(out EffectManager effects, this))
                return;
            EffectEntry entry = effects.Catalog.Assassin_R_BladeVortex;
            if (entry == null)
            {
                Edit.LogWarning("[AssassinSkillView] EffectCatalog.Assassin_R_BladeVortex 가 비어 변신 연출을 건너뛴다.", this);
                return;
            }
            transformLoop = effects.PlayLooping(entry, transform, transformEffectOffset, transformEffectScale);
            return;
        }

        if (presentationVortex)
            return;   // 처치 연출 동안 실제 변신이 풀려도 오라 유지(끝나면 Update 가 접는다)

        if (transformLoop.IsSet && EffectManager.Instance != null)
            EffectManager.Instance.Release(transformLoop);
        transformLoop = EffectHandle.None;
    }

    private void SyncLoops()
    {
        if (state == null)
            return;
        HandleTransformChanged(state.IsTransformed);
    }
}
