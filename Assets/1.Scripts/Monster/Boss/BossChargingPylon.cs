using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 송전탑(차징 기둥). 서버 권한으로 숨김↔활성 위치를 왕복하고, 활성 중에만 피격된다.
//
// 아레나(`bossroom.prefab`)의 `Env_Mv_bosscharger_upper` 4개에 붙는다.
// 평상시엔 바닥 아래에 숨어 통행을 방해하지 않고, 차징 시퀀스가 시작되면 **올라온다**.
// 부서지면 **다시 내려간다**(팀장 확정 2026-08-07).
//
// ─── 레거시 `Enemy/Boss/ChargingObject` 에서 승격한 것 ──────────────────────
// 그 코드가 요구사항을 이미 정확히 구현하고 있었으므로 로직을 그대로 가져왔다.
// 바뀐 것은 두 가지뿐이다:
//   ① **정적 레지스트리**(<see cref="Active"/>) — 기둥은 아레나에, 매니저는 보스에 붙으므로
//      부모-자식 탐색으로는 서로를 못 찾는다. `AreaZone.Active` 와 같은 패턴이다.
//   ② `Unit.Initialize` 호출을 서버에서만 하도록 유지(원본과 동일).
//
// 🔴 위치는 **로컬 좌표 기준**이다. 이전 구현은 Awake 시점의 월드 Y 를 최저점으로 잡아서
//    보스룸이 맵 밖 좌표(x≈+500)로 옮겨지거나 같은 세션에서 패턴을 두 번 돌리면 목표 높이가 어긋났다.
//    위치 복제는 루트의 NetworkTransform 이 담당한다 — 서버만 위치를 쓴다.
//
// ⚠️ `Hurtbox` 를 붙이지 않아도 된다. `BaseAttack.TryResolveHit` 은 Hurtbox 가 없으면
//    `GetComponentInParent<Unit>()` 로 폴백하고, 이 클래스가 `Unit` 파생이라 그대로 잡힌다.
public class BossChargingPylon : Unit
{
    /// <summary>서버 전용 활성 기둥 목록. 차징 매니저가 여기서 필요한 개수만 고른다.</summary>
    public static readonly List<BossChargingPylon> Active = new List<BossChargingPylon>();

    public enum PylonState
    {
        Hidden,
        Rising,
        Live,       // 상승 완료 — 이때만 피격된다
        Lowering,
    }

    [Header("체력")]
    [SerializeField, Min(1)] int maxHp = 200;
    [SerializeField, Min(0)] int defense = 0;

    [Header("이동")]
    [SerializeField, Min(0.01f)]
    [Tooltip("숨김 위치에서 얼마나 올라오는지(m).")]
    float riseHeight = 1f;
    [SerializeField, Min(0.01f)]
    [Tooltip("상승·하강 속도(m/s).")]
    float moveSpeed = 1f;

    // ─── 공격 가능 표시 — 노랑 아웃라인 (PLAN-boss-1012.md §A, 팀장 10-12) ─────────────
    // 기믹으로 안 보인다는 피드백 → **Live(피격 가능)일 때만** 테두리를 켠다.
    // 🔴 본체 재질은 맵 공용 아틀라스(`Generic_01_A`, 프리팹 107개 공유)라 거기 아웃라인을 켜면 맵 전체가
    //    테두리를 갖는다. 그래서 **같은 메시의 자식 렌더러**에 아웃라인 전용 재질(`Project/Outline Only`)을
    //    씌우고 enabled 만 토글한다. 본체 렌더러의 재질 배열은 건드리지 않는다(HitFlash 가 재질을 캐시한다).
    [Header("공격 가능 표시")]
    [SerializeField]
    [Tooltip("Live 동안 켜는 아웃라인 재질(MA_PylonOutline, 셰이더 Project/Outline Only). 비우면 아웃라인 없이 동작.")]
    Material outlineMaterial;

    // 상태(_state)는 서버 전용이라 클라는 Live 를 모른다 → 표시 여부만 복제한다.
    readonly NetworkVariable<bool> _outlined = new NetworkVariable<bool>(false);
    readonly List<Renderer> _outlineRenderers = new List<Renderer>();

    Collider _collider;
    Vector3 _hiddenLocal;
    Vector3 _liveLocal;
    PylonState _state = PylonState.Hidden;

    /// <summary>상승을 완료해 피격 가능한 상태인가.</summary>
    public bool IsLive => _state == PylonState.Live;

    /// <summary>이 시퀀스에서 부서졌는가(매니저가 완료 판정에 쓴다).</summary>
    public bool WasDestroyed { get; private set; }

    /// <summary>이번 시퀀스에 참여 중인가(매니저가 활성시킨 기둥만 true).</summary>
    public bool IsEngaged { get; private set; }

    public PylonState State => _state;

    void Awake()
    {
        _collider = GetComponent<Collider>();
        _hiddenLocal = transform.localPosition;
        _liveLocal = _hiddenLocal + Vector3.up * riseHeight;

        // 🔴 Awake 에서 만든다 — HitFlash 는 스폰 때 붙어 렌더러를 긁으므로 그 전에 NoHitFlash 가 있어야 한다.
        BuildOutlineRenderers();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        SetColliderEnabled(false);

        _outlined.OnValueChanged += OnOutlinedChanged;
        ApplyOutline(_outlined.Value);   // 늦게 합류한 클라도 지금 값으로

        if (!IsServer) return;

        _outlined.Value = false;

        Initialize(0, 0, 0, maxHp, defense);
        Active.Add(this);

        // 시작은 항상 숨김 — 평상시 통행을 방해하지 않는다.
        _state = PylonState.Hidden;
        transform.localPosition = _hiddenLocal;
    }

    public override void OnNetworkDespawn()
    {
        _outlined.OnValueChanged -= OnOutlinedChanged;
        Active.Remove(this);
        base.OnNetworkDespawn();
    }

    void Update()
    {
        if (!IsServer) return;

        TickMoving();

        // 활성 중 체력이 0 이면 내려간다(= 부서졌다).
        if (_state == PylonState.Live && CurrentHealth <= 0)
        {
            WasDestroyed = true;
            BeginLowering();
        }
    }

    // 🔴 활성(상승 완료) 상태에서만 피격된다 — 숨김·이동 중에는 무적이다.
    //    이 가드가 없으면 바닥 아래 숨은 기둥이 장판·광역기에 맞아 조용히 부서진다.
    public override void TakeDamage(AttackInfo attackInfo)
    {
        if (!IsServer || _state != PylonState.Live) return;
        base.TakeDamage(attackInfo);
    }

    void TickMoving()
    {
        if (_state != PylonState.Rising && _state != PylonState.Lowering) return;

        Vector3 target = _state == PylonState.Rising ? _liveLocal : _hiddenLocal;
        Vector3 next = Vector3.MoveTowards(transform.localPosition, target, moveSpeed * Time.deltaTime);
        transform.localPosition = next;

        if ((next - target).sqrMagnitude > 0.0000001f) return;

        if (_state == PylonState.Rising)
        {
            _state = PylonState.Live;
            SetColliderEnabled(true);
            _outlined.Value = true;
            return;
        }

        _state = PylonState.Hidden;
        IsEngaged = false;
    }

    /// <summary>서버 전용. 체력을 되살리고 상승을 시작한다. 반복 호출해도 안전하다.</summary>
    /// <param name="hpMultiplier">이번 시퀀스 최대 HP 배율(레이드 ② = 2). 프리팹 maxHp 는 그대로 두고 매번 다시 잡는다 —
    /// 같은 보스방 송전탑을 기존 23호와 레이드가 같이 쓰기 때문.</param>
    public void BeginCharge(float hpMultiplier = 1f)
    {
        if (!IsServer) return;

        int hp = Mathf.Max(1, Mathf.RoundToInt(maxHp * Mathf.Max(0.01f, hpMultiplier)));
        if (hp != MaxHp) Initialize(0, 0, 0, hp, defense);
        else Revive();
        WasDestroyed = false;
        IsEngaged = true;
        _state = PylonState.Rising;
        SetColliderEnabled(false);
        _outlined.Value = false;   // 올라오는 동안은 맞지 않는다 — 다 올라온 뒤에 켠다
    }

    /// <summary>서버 전용·멱등. 어느 상태에서든 숨김 위치로 되돌린다.</summary>
    public void EndCharge()
    {
        if (!IsServer) return;
        if (_state == PylonState.Hidden || _state == PylonState.Lowering) return;

        BeginLowering();
    }

    void BeginLowering()
    {
        _state = PylonState.Lowering;
        SetColliderEnabled(false);
        _outlined.Value = false;   // 부서짐·차징 종료 모두 여기로 온다
    }

    void SetColliderEnabled(bool value)
    {
        if (_collider != null) _collider.enabled = value;
    }

    // ─── 아웃라인 ─────────────────────────────────────────────────────

    void BuildOutlineRenderers()
    {
        // 비움 = 아웃라인 없이 동작(필드 Tooltip 그대로). 2026-10-22 부터 bossroom 의 송전기 4개가
        // 모두 비어 있다 — Live 표시를 몸 연출(PylonChargeVisual)이 맡고, 아웃라인 렌더러가 그 위를
        // 덮어 안 보였기 때문이다. 콜라이더 토글(ApplyOutline)은 여기와 무관하게 그대로 돈다.
        if (outlineMaterial == null) return;

        var sources = GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            MeshRenderer src = sources[i];
            var filter = src.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            var go = new GameObject("PylonOutline") { layer = src.gameObject.layer };
            go.transform.SetParent(src.transform, false);
            go.AddComponent<NoHitFlash>();   // 피격 플래시가 아웃라인 색을 덮지 않게

            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            // 서브메시마다 한 장 — 한 장만 주면 첫 서브메시에만 테두리가 생긴다.
            var mats = new Material[Mathf.Max(1, filter.sharedMesh.subMeshCount)];
            for (int m = 0; m < mats.Length; m++) mats[m] = outlineMaterial;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            _outlineRenderers.Add(r);
        }
    }

    void OnOutlinedChanged(bool previous, bool next) => ApplyOutline(next);

    void ApplyOutline(bool on)
    {
        for (int i = 0; i < _outlineRenderers.Count; i++)
            if (_outlineRenderers[i] != null) _outlineRenderers[i].enabled = on;

        // 🔴 클라는 콜라이더도 이 값을 따른다 — 서버는 콜라이더·아웃라인을 항상 같이 켜고 끄는데(TickMoving·BeginCharge·
        //    BeginLowering) 클라는 스폰 때 끈 채 영영 안 켜져, 이동 권한이 있는 원격 플레이어가 탑을 통과했다(점검 10-14).
        if (!IsServer) SetColliderEnabled(on);
    }
}
