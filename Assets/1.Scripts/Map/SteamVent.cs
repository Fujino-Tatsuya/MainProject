using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 보스방 **증기 벤트** — 주기적으로 분사해 범위 안의 플레이어·보스에게 피해를 준다(팀 기획 `Re_C_취약_및_제압_시스템.md` §6.3).
/// 취약 상태의 보스가 맞으면 간파 게이지가 깎인다(보스가 판정 — <see cref="TwentyThreeBoss.OnSteamVentHit"/>).
/// </summary>
/// <remarks>
/// <b>붙이기만 하면 된다.</b> 벤트 오브젝트에 이 컴포넌트를 붙이면 켜질 때 스스로 <see cref="Active"/> 에 등록된다
/// — 레이어·이름 규칙·존 사전 등록·보스 스폰 순서가 필요 없다(팀장 09-28).
///
/// 🔴 <b>MonoBehaviour 다(NetworkBehaviour 아님).</b> 존은 네트워크 스폰이 아니라 각 피어가 로컬로 만든다
///    (<c>MapContentSpawner</c>) — 비스폰 NetworkBehaviour 는 <c>IsServer</c> 가 false 로 나온다(레포에 기록된 함정).
///    그래서 판정은 <c>NetworkManager.Singleton.IsServer</c> 로 가르고, 분사 주기는 <b>서버 시각</b>(ServerTime)으로
///    계산해 모든 피어가 같은 순간에 <see cref="onVentStart"/>/<see cref="onVentStop"/> 을 받는다(RPC 없음, 늦은 합류도 맞다).
///
/// 연출(VFX·사운드)은 민경이 <see cref="onVentStart"/>/<see cref="onVentStop"/> 에 꽂는다 — 이 코드는 판정·타이밍만.
/// 주기 값은 아직 미정(팀장 09-28) — 인스펙터 임시값.
/// </remarks>
[DisallowMultipleComponent]
public sealed class SteamVent : MonoBehaviour
{
    static readonly List<SteamVent> s_active = new List<SteamVent>();

    /// <summary>켜져 있는 벤트 전부(로컬 목록 — 피어마다 자기 존의 벤트).</summary>
    public static IReadOnlyList<SteamVent> Active => s_active;

    [Header("분사 주기 (임시값 — 미정)")]
    [Tooltip("분사 한 주기의 길이(초). 이 간격마다 activeDuration 동안 분사한다.")]
    [SerializeField, Min(0.1f)] float interval = 6f;

    [Tooltip("한 주기 안에서 분사하는 시간(초). interval 보다 짧아야 한다.")]
    [SerializeField, Min(0.05f)] float activeDuration = 1.5f;

    [Tooltip("주기 시작을 미는 시간(초). 벤트마다 다르게 주면 동시에 터지지 않는다.")]
    [SerializeField, Min(0f)] float phaseOffset = 0f;

    [Header("피해 (플레이어 · 보스 둘 다)")]
    [Tooltip("분사 중 틱마다 주는 피해. 0 이면 피해 없이 판정만(보스 취약 게이지는 그래도 깎인다).")]
    [SerializeField, Min(0)] int damage = 10;

    [Tooltip("분사 중 같은 대상이 다시 맞기까지의 간격(초).")]
    [SerializeField, Min(0.05f)] float tickInterval = 0.5f;

    [Header("범위 (이 오브젝트 로컬 기준 박스)")]
    [SerializeField] Vector3 boxCenter = new Vector3(0f, 1f, 0f);
    [SerializeField] Vector3 boxSize = new Vector3(2f, 2f, 2f);

    [Tooltip("판정할 레이어 — 플레이어·몬스터 허트박스(트리거). 기본 PlayerHurtbox | EnemyHurtBox.")]
    [SerializeField] LayerMask targetMask = (1 << 13) | (1 << 14);

    [Header("연출 훅 (민경 — VFX · 사운드)")]
    public UnityEvent onVentStart = new UnityEvent();
    public UnityEvent onVentStop = new UnityEvent();

    [Tooltip("분사하는 동안 판정 범위 크기의 주황 큐브를 띄운다(임시 확인용 — 전 피어). 콜라이더 없음.\n" +
             "VFX 가 들어오면 끈다.")]
    [SerializeField] bool showDebugCube = true;
    GameObject _debugCube;

    readonly Collider[] _hits = new Collider[16];
    readonly HashSet<Unit> _tickUnits = new HashSet<Unit>();
    bool _venting;
    double _nextTickAt;

    /// <summary>지금 분사 중인가(모든 피어에서 같은 값 — 서버 시각 기준).</summary>
    public bool IsVenting => _venting;

    void OnEnable()
    {
        if (!s_active.Contains(this)) s_active.Add(this);
    }

    void OnDisable()
    {
        s_active.Remove(this);
        if (_venting)
        {
            _venting = false;
            onVentStop?.Invoke();
        }
        SetDebugCube(false);
    }

    void OnDestroy()
    {
        if (_debugCube != null) Destroy(_debugCube);
    }

    // 임시 확인용 큐브 — 판정 박스와 같은 자리·크기. 머티리얼은 인스턴스(에셋 오염 없음).
    void SetDebugCube(bool on)
    {
        if (!showDebugCube)
        {
            if (_debugCube != null) _debugCube.SetActive(false);
            return;
        }

        if (on && _debugCube == null)
        {
            _debugCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _debugCube.name = "SteamVent_DebugCube";
            Destroy(_debugCube.GetComponent<Collider>());   // 판정·이동에 끼면 안 된다
            _debugCube.transform.SetParent(transform, false);
            _debugCube.transform.localPosition = boxCenter;
            _debugCube.transform.localRotation = Quaternion.identity;
            _debugCube.transform.localScale = boxSize;
            if (_debugCube.TryGetComponent(out Renderer r))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.material.color = new Color(1f, 0.55f, 0.1f, 1f);
            }
        }

        if (_debugCube != null) _debugCube.SetActive(on);
    }

    void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;   // 세션 밖(로비·에디트)에서는 조용히

        double now = nm.ServerTime.Time;
        bool shouldVent = IsActiveAt(now);

        if (shouldVent != _venting)
        {
            _venting = shouldVent;
            if (_venting)
            {
                _nextTickAt = now;   // 분사 시작 틱은 바로 친다
                onVentStart?.Invoke();
            }
            else
            {
                onVentStop?.Invoke();
            }
            SetDebugCube(_venting);
        }

        // 판정은 서버에서만. 연출 이벤트는 위에서 모든 피어가 받았다.
        if (!_venting || !nm.IsServer || now < _nextTickAt) return;
        _nextTickAt = now + tickInterval;
        ApplyTick();
    }

    // 서버 시각 → 주기 안 위상. 모든 피어가 같은 시각을 보므로 같은 결과가 나온다.
    bool IsActiveAt(double serverTime)
    {
        double period = Mathf.Max(0.1f, interval);
        double t = (serverTime + phaseOffset) % period;
        if (t < 0) t += period;
        return t < Mathf.Min(activeDuration, interval);
    }

    void ApplyTick()
    {
        Transform tr = transform;
        Vector3 center = tr.TransformPoint(boxCenter);
        Vector3 half = Vector3.Scale(boxSize * 0.5f, new Vector3(
            Mathf.Abs(tr.lossyScale.x), Mathf.Abs(tr.lossyScale.y), Mathf.Abs(tr.lossyScale.z)));

        int n = Physics.OverlapBoxNonAlloc(center, half, _hits, tr.rotation, targetMask, QueryTriggerInteraction.Collide);

        _tickUnits.Clear();
        for (int i = 0; i < n; i++)
        {
            Collider c = _hits[i];
            if (c == null) continue;

            Hurtbox hurtbox = c.GetComponentInParent<Hurtbox>();
            Unit unit = hurtbox != null ? hurtbox.OwnerUnit : c.GetComponentInParent<Unit>();
            if (unit == null || unit.CurrentHealth <= 0) continue;
            if (!_tickUnits.Add(unit)) continue;   // 콜라이더가 여러 개인 유닛도 틱당 1회

            // 출처는 벤트 — 플레이어가 아니므로 제압 피해 배율(플레이어 전용)이 붙지 않는다.
            if (damage > 0)
            {
                var info = new AttackInfo(damage, AttackType.Default);
                var ctx = new AttackHitContext(center, tr, c);
                if (hurtbox != null) hurtbox.ReceiveAttack(info, ctx);
                else unit.ReceiveAttack(info, ctx);
            }

            // 보스 쪽 판정(취약이면 게이지 −20). 벤트 피해로 죽었으면 처리하지 않는다(사망 우선).
            if (unit is TwentyThreeBoss boss && boss.CurrentHealth > 0)
                boss.OnSteamVentHit(this);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.35f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(boxCenter, boxSize);
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(boxCenter, boxSize);
    }
}
