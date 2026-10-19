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
/// - Q 돌진 궤적 · 변신 E 원 · E 시전: 스킬의 OnClientPlay/OnEnd(전 피어)가 로컬로 부른다 — RPC 없음.
///   🔹 변신 E 원 = 정식 VFX <c>EffectCatalog.Assassin_E_MultiSlashes</c> 루프(2026-10-15 민경) — 확정 시 원 중심에서
///   배율 = 원 반경으로 켜고, 스킬 OnEnd(5타 완료·중단 공통)에서 Release 한다. 확정 순간 1회 <c>circleStrikePrefab</c> 도 그대로 낸다.
///   🔹 변신 Q 돌진 = 정식 VFX <c>EffectCatalog.Assassin_Q_StormSlash</c> 루프(2026-10-15 민경) — 플레이어 루트를 따라가며
///   지나간 자리에 자국을 남긴다(입자 World + Rate over Distance).
///   🔹 일반 Q 돌진 = 정식 VFX <c>Assassin_Q_PierceLine</c>·<c>Assassin_Q_PierceBurst</c>(2026-10-16 민경) — 시작점을 기억했다가
///   스킬 OnEnd 에서 지나온 경로 위로 일섬(Z 배율 = 실제 이동 거리)을 긋고 끝점에 버스트. 임시 돌진 궤적은 제거.
/// - 평타 베기: 클립 Hit 애니 이벤트(전 피어에서 재생)마다 <see cref="AssassinBasicAttack"/> 가 부른다 — RPC 없음.
///   🔹 정식 VFX(2026-10-19 민경) — 소켓은 <c>Player_Assassin/VFX/*</c> 의 <see cref="EffectSocketPlayer"/>(엔트리·위치는 프리팹에서):
///   일반 1~4타·변신 묶음은 클립의 <see cref="EffectAnimEvents"/> 이벤트(<c>PlayEffect</c> / <c>StartEffect</c>·<c>StopEffect</c>)가
///   직접 재생한다 — 여기서 하지 않는다. 여기 남은 것: 적중 성공 = [서버] <see cref="ServerNormalSlashHit"/> → 전 피어
///   <c>normalSlashSuccess[타]</c> 원샷(일반 베기와 겹쳐 재생 — 서버 판정이라 애니 이벤트로 못 낸다),
///   변신 루프 안전 정지 <see cref="EndTransformedSlash"/>(묶음이 StopEffect 프레임 전에 끊길 때). 강타는 아직 임시 프리팹 경로.
/// - 우클릭 간파: 🔹 정식 VFX(2026-10-19 민경) — 기 모으기·튀기기·올려치기는 <c>Attack_Up_01</c> 클립의 <see cref="EffectAnimEvents"/>
///   이벤트가 <c>Player_Assassin/VFX/RightClick/*</c> 소켓에서 직접 재생한다. 여기 남은 것: 간파 성공 = [서버]
///   <see cref="PlayerInterruptSkillBase.ServerInterruptSucceeded"/> → 전 피어 "성공 대기" → 올려치기 소켓 id 가 재생되는 순간
///   <c>insightSuccess</c> 원샷(알림이 더 늦으면 받는 즉시), 기 모으기 안전 정지 <see cref="EndInterrupt"/>(스킬 OnEnd).
/// - 백어택 적중: [서버] <see cref="ServerBackAttackHit"/> → 전 피어. 판정은 A11 이 붙인다(그 전까지 호출자 없음).
/// 프리팹이 비어 있으면 해당 연출만 빠진다.
/// </summary>
[RequireComponent(typeof(AssassinState))]
public sealed class AssassinSkillView : BaseNetworkBehaviour
{
    [Header("변신 루프 — EffectCatalog.Assassin_R_BladeVortex")]
    [Tooltip("플레이어 루트 기준 월드 오프셋. 배율에 곱해지지 않는다 — 프리팹 피벗이 발밑이라 기본 0.")]
    [SerializeField] private Vector3 transformEffectOffset;
    [Tooltip("프리팹 저작 크기에 곱하는 배율.")]
    [SerializeField, Min(0.01f)] private float transformEffectScale = 1f;

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

    [Header("평타 베기 — EffectSocketPlayer(Player_Assassin/VFX/*, 엔트리·소켓은 각 컴포넌트에서)")]
    [Tooltip("일반 1~4타 적중 성공 — 서버 판정 → RPC 로 같은 타 일반 베기와 겹쳐 원샷. 인덱스 = 타 순서(0~3). 엔트리 = Assassin_LClick_Success.\n" +
             "(일반 베기 자체는 클립의 PlayEffect 이벤트가 낸다.)")]
    [SerializeField] private EffectSocketPlayer[] normalSlashSuccess = new EffectSocketPlayer[4];
    [Tooltip("변신 평타 묶음 루프 — 켜고 끄는 건 클립의 StartEffect/StopEffect 이벤트. 여기는 묶음이 끊길 때의 안전 정지에만 쓴다.")]
    [SerializeField] private EffectSocketPlayer transformedSlash;
    [Tooltip("강타 — 임시 프리팹(Hit 이벤트에 소켓에 붙여 1회).")]
    [SerializeField] private GameObject enhancedSlashPrefab;

    [Header("우클릭 간파 — EffectSocketPlayer(Player_Assassin/VFX/RightClick/*)")]
    [Tooltip("기 모으기 루프 — 켜고 끄는 건 Attack_Up_01 의 StartEffect/StopEffect 이벤트. 여기는 시전이 그 프레임 전에 끊길 때의 안전 정지에만 쓴다.")]
    [SerializeField] private EffectSocketPlayer insightWindup;
    [Tooltip("올려치기 — 클립의 PlayEffect 이벤트가 낸다. 여기서는 이 소켓의 Id 가 재생되는 순간을 성공 연출의 기준 프레임으로만 쓴다.")]
    [SerializeField] private EffectSocketPlayer insightSlash;
    [Tooltip("간파 성공 — 서버 판정 → RPC → 올려치기와 함께 원샷. id 비워 둠(코드 전용). 엔트리 = Assassin_RClick_Success.")]
    [SerializeField] private EffectSocketPlayer insightSuccess;

    [Header("시전 1회 — 스킬 OnClientPlay(전 피어)")]
    [SerializeField] private GameObject enhanceCastPrefab;

    [Header("1회·스킬 동안")]
    [Tooltip("변신 E 확정 순간 원 중심에 1회(Multi Slashes 루프와 함께).")]
    [SerializeField] private GameObject circleStrikePrefab;
    [SerializeField] private GameObject backAttackHitPrefab;
    [Tooltip("1회 연출·꺼진 루프가 남은 입자를 흘리고 사라지기까지(초).")]
    [SerializeField, Min(0.1f)] private float releaseLifetime = 2f;

    [Header("소켓 (비우면 플레이어 루트)")]
    [Tooltip("강타 베기 — Armature/VFX/EnhancedAttack.")]
    [SerializeField] private Transform enhancedSlashSocket;

    private AssassinState state;
    private EffectHandle transformLoop;
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

    // 서버에서만 난다(정적 이벤트) — 시전자가 나인 것만 거른다.
    private void OnEnable() => PlayerInterruptSkillBase.ServerInterruptSucceeded += HandleServerInterruptSucceeded;

    private void OnDisable() => PlayerInterruptSkillBase.ServerInterruptSucceeded -= HandleServerInterruptSucceeded;

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
    /// [전 피어] 평타 Hit 애니 이벤트 — 강타만 임시 프리팹을 소켓에 붙여 1회. 일반 1~4타·변신 묶음은 클립의
    /// EffectAnimEvents 이벤트가 직접 내므로 여기서는 아무것도 하지 않는다.
    /// </summary>
    public void PlayAttackSlash(AssassinBasicAttackMode mode, int stepIndex)
    {
        if (mode == AssassinBasicAttackMode.Enhanced)
            PlayAttached(enhancedSlashPrefab, enhancedSlashSocket);
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

    /// <summary>[전 피어] 일반 E 버프 시전.</summary>
    public void PlayEnhanceCast() => PlayAttached(enhanceCastPrefab);

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

        if (line != null && length >= pierceMinLength)
            effects.Play(line, pierceStart, rotation, new Vector3(pierceScale, pierceScale, length));
        if (burst != null)
            effects.Play(burst, endPoint, rotation, pierceScale);
    }

    /// <summary>
    /// [전 피어] 변신 E 공격 시작 — 원 중심에서 루프를 켠다. 배율 = <paramref name="radius"/> × circleStrikeScaleFactor.
    /// <paramref name="maxDuration"/>(스킬 최대 지속) + 1초 안에 <see cref="EndCircleStrike"/> 가 안 오면 스스로 해제한다 —
    /// 클라의 종료 RPC 가 이미 정리된 스킬에 막혀 OnEnd 가 안 불리는 경우의 안전망.
    /// </summary>
    public void BeginCircleStrike(Vector3 center, float radius, float maxDuration)
    {
        EndCircleStrike();   // 재진입 — 남은 루프는 먼저 걷는다
        PlayOneShot(circleStrikePrefab, center);
        if (!EffectManager.TryGet(out EffectManager effects, this))
            return;

        EffectEntry entry = effects.Catalog.Assassin_E_MultiSlashes;
        if (entry == null)
        {
            Edit.LogWarning("[AssassinSkillView] EffectCatalog.Assassin_E_MultiSlashes 가 비어 변신 E 연출을 건너뛴다.", this);
            return;
        }

        circleStrikeLoop = effects.PlayLooping(entry, center, Quaternion.identity, Mathf.Max(0.01f, radius * circleStrikeScaleFactor));
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
        stormSlashDeadline = Time.time + Mathf.Max(0f, maxDuration) + 1f;
    }

    /// <summary>[전 피어] 변신 Q 종료(사유 무관) — 방출만 멈추고 남은 자국은 엔트리 outroDuration 동안 사라지게 둔다.</summary>
    public void EndStormSlash()
    {
        if (stormSlashLoop.IsSet && EffectManager.Instance != null)
            EffectManager.Instance.Release(stormSlashLoop);
        stormSlashLoop = EffectHandle.None;
    }

    private void Update()
    {
        if (circleStrikeLoop.IsSet && Time.time >= circleStrikeDeadline)
            EndCircleStrike();
        if (stormSlashLoop.IsSet && Time.time >= stormSlashDeadline)
            EndStormSlash();
    }

    // ── 서버 판정 → 전 피어 ──

    /// <summary>[서버] 백어택 적중 지점 — 전 피어에 1회 연출(A11 판정이 호출).</summary>
    public void ServerBackAttackHit(Vector3 position)
    {
        if (IsNetworkActive)
            BackAttackHitRpc(position);
        else
            PlayBackAttackHit(position);
    }

    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void BackAttackHitRpc(Vector3 position) => PlayBackAttackHit(position);

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

    // [서버] 간파 성공(쿨 감소와 같은 기준) — 전 피어에 성공 대기를 알린다. 1회성 판정 결과라 Reliable.
    private void HandleServerInterruptSucceeded(Player caster, PlayerInterruptSkillBase skill)
    {
        if (caster == null || caster != player)
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
        if (insightSuccess != null)
            insightSuccess.PlayOnce();
    }

    private void PlayBackAttackHit(Vector3 position) => PlayOneShot(backAttackHitPrefab, position);

    // 1회 연출은 방출을 끊지 않는다(버스트가 첫 업데이트 전에 잘린다) — 수명 뒤 파괴만.
    private void PlayOneShot(GameObject prefab, Vector3 position)
    {
        GameObject instance = Spawn(prefab, null);
        if (instance == null)
            return;
        instance.transform.position = position;
        Destroy(instance, releaseLifetime);
    }

    // 1회 연출을 소켓(없으면 플레이어 루트)에 붙인다 — 시전·베기가 캐릭터를 따라가고, 소켓 위치·회전으로 맞춘다.
    private void PlayAttached(GameObject prefab, Transform socket = null)
    {
        GameObject instance = Spawn(prefab, socket != null ? socket : transform);
        if (instance != null)
            Destroy(instance, releaseLifetime);
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

    private static GameObject Spawn(GameObject prefab, Transform parent)
    {
        if (prefab == null)
            return null;

        GameObject instance = parent != null ? Instantiate(prefab, parent, false) : Instantiate(prefab);
        instance.name = prefab.name + "(임시)";

        // 빌려 쓰는 프리팹 중 루트 위치가 0 이 아닌 것이 있다(CharacterCirclePurple —씬 배치값이 저장됨).
        // 붙일 때 그 값이 소켓 기준 오프셋이 되어 원이 몇 m 떨어져 생긴다 — 위치는 소켓이 정한다.
        if (parent != null)
            instance.transform.localPosition = Vector3.zero;

        // 빌려 쓰는 프리팹 중 playOnAwake 가 꺼진 것이 있다(가붕이 FX_SingleSlash_* — 원래 쓰는 쪽이 Play() 를 직접 부름).
        foreach (ParticleSystem particle in instance.GetComponentsInChildren<ParticleSystem>())
        {
            if (!particle.isPlaying)
                particle.Play(false);
        }
        return instance;
    }
}
