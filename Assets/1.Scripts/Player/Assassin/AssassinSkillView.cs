using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 🔸 어쌔신 임시 VFX 창구(PLAN-assassin A12) — 민경 정식 VFX 가 오면 프리팹 필드만 교체한다(거너 GunnerBeamView 선례).
/// 스킬(PlayerSkillBase)은 RPC 를 가질 수 없으므로 서버 판정 결과를 퍼뜨리는 창구도 여기 둔다.
///
/// - 변신 중 루프 · 강화 준비 루프: <see cref="AssassinState"/> 의 전 피어 이벤트로 켜고 끈다(늦은 접속은 스폰 시 현재 상태로 맞춤).
/// - Q 돌진 궤적 · 변신 E 원: 스킬의 OnClientPlay/OnEnd(전 피어)가 로컬로 부른다 — RPC 없음.
/// - 백어택 적중: [서버] <see cref="ServerBackAttackHit"/> → 전 피어. 판정은 A11 이 붙인다(그 전까지 호출자 없음).
/// 프리팹이 비어 있으면 해당 연출만 빠진다.
/// </summary>
[RequireComponent(typeof(AssassinState))]
public sealed class AssassinSkillView : BaseNetworkBehaviour
{
    [Header("루프 — 상태가 켜진 동안")]
    [SerializeField] private GameObject transformLoopPrefab;
    [SerializeField] private GameObject enhancedReadyLoopPrefab;

    [Header("1회·스킬 동안")]
    [SerializeField] private GameObject dashTrailPrefab;
    [SerializeField] private GameObject circleStrikePrefab;
    [SerializeField] private GameObject backAttackHitPrefab;
    [Tooltip("1회 연출·꺼진 루프가 남은 입자를 흘리고 사라지기까지(초).")]
    [SerializeField, Min(0.1f)] private float releaseLifetime = 2f;

    [Header("소켓 (비우면 플레이어 루트)")]
    [SerializeField] private Transform transformSocket;
    [SerializeField] private Transform enhancedSocket;

    private AssassinState state;
    private GameObject transformLoop;
    private GameObject enhancedLoop;
    private GameObject dashTrail;

    private void Awake()
    {
        state = GetComponent<AssassinState>();
        if (state != null)
        {
            state.TransformChanged += HandleTransformChanged;
            state.EnhancedReadyChanged += HandleEnhancedReadyChanged;
        }
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
        {
            state.TransformChanged -= HandleTransformChanged;
            state.EnhancedReadyChanged -= HandleEnhancedReadyChanged;
        }
        base.OnDestroy();
    }

    // ── 스킬이 부르는 로컬 연출(전 피어) ──

    /// <summary>[전 피어] Q 돌진 시작 — 궤적을 플레이어에 붙인다.</summary>
    public void BeginDashTrail()
    {
        Release(ref dashTrail);
        dashTrail = Spawn(dashTrailPrefab, transform);
    }

    /// <summary>[전 피어] Q 종료(사유 무관) — 궤적을 떼어 남은 입자만 흘린다.</summary>
    public void EndDashTrail() => Release(ref dashTrail);

    /// <summary>[전 피어] 변신 E 공격 시작 — 원 중심에 1회.</summary>
    public void PlayCircleStrike(Vector3 center) => PlayOneShot(circleStrikePrefab, center);

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

    // ── 루프 ──

    private void HandleTransformChanged(bool on) => SetLoop(ref transformLoop, transformLoopPrefab, transformSocket, on);

    private void HandleEnhancedReadyChanged(bool on) => SetLoop(ref enhancedLoop, enhancedReadyLoopPrefab, enhancedSocket, on);

    private void SyncLoops()
    {
        if (state == null)
            return;
        HandleTransformChanged(state.IsTransformed);
        HandleEnhancedReadyChanged(state.IsEnhancedReady);
    }

    private void SetLoop(ref GameObject instance, GameObject prefab, Transform socket, bool on)
    {
        if (on)
        {
            if (instance == null)
                instance = Spawn(prefab, socket != null ? socket : transform);
        }
        else
        {
            Release(ref instance);
        }
    }

    private static GameObject Spawn(GameObject prefab, Transform parent)
    {
        if (prefab == null)
            return null;

        GameObject instance = parent != null ? Instantiate(prefab, parent, false) : Instantiate(prefab);
        instance.name = prefab.name + "(임시)";
        return instance;
    }

    // 루프·궤적용 — 방출을 멈추고 월드에 떼어 남은 입자가 사라진 뒤 파괴한다.
    private void Release(ref GameObject instance)
    {
        if (instance == null)
            return;

        foreach (ParticleSystem particle in instance.GetComponentsInChildren<ParticleSystem>())
            particle.Stop(false, ParticleSystemStopBehavior.StopEmitting);

        instance.transform.SetParent(null, true);
        Destroy(instance, releaseLifetime);
        instance = null;
    }
}
