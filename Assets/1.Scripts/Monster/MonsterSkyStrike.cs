using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 수직 낙하 포격(코그모 궁 방식 — PLAN-monster-tells M4~M8). 피어마다 하나, 첫 사용 때 스스로 생긴다(씬 배치 없음).
///
/// 흐름: 서버(<see cref="MonsterBase"/>)가 발사 순간 착탄점·반경·**서버 착탄 시각**을 정해 RPC 한 번으로 알린다
///   → 각 피어가 그 시각을 기준으로 원(윤곽 + 차오르는 채움)을 그리고, 마지막 구간에 폭탄을 떨어뜨리고, 착탄 시각에 폭발을 재생한다.
///   → 서버는 같은 시각에 **한 번** 판정한다.
///
/// 🔴 포격은 쏜 몹과 <b>분리된 수명</b>이다 — 몹이 죽거나 맞거나 자리를 옮겨도 이미 뜬 포격은 떨어진다(M7).
///    그래서 몹 오브젝트가 아니라 이 관리자가 들고 있고, 착탄 RPC 도 없다(죽은 몹은 RPC 를 못 보낸다).
///    클라는 서버 시각(<see cref="NetworkManager.ServerTime"/>)으로 스스로 끝을 안다 — 지연만큼 늦게 받아도 끝은 맞는다.
/// 🔴 피해 = <b>같은 바닥만</b>(M8): 원 반경은 평면(XZ) 거리, 높이는 착탄 바닥 ± <c>floorTolerance</c>. 위층·다리 아래는 안 맞는다.
/// ⚠️ 늦게 접속한 클라는 진행 중인 포격(최대 예고 시간 1.5초)을 못 본다 — 받아들인 한계.
/// </summary>
public sealed class MonsterSkyStrike : MonoBehaviour
{
    static MonsterSkyStrike _instance;

    public static MonsterSkyStrike Instance
    {
        get
        {
            if (_instance != null) return _instance;
            var go = new GameObject(nameof(MonsterSkyStrike));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MonsterSkyStrike>();
            return _instance;
        }
    }

    // ── 서버 판정 ─────────────────────────────────────────────────

    struct PendingHit
    {
        public Vector3 point;          // 착탄점(바닥)
        public float radius;
        public float floorTolerance;
        public double impactTime;      // 서버 시각
        public int damage;
        public LayerMask targetMask;
        public Unit owner;             // 쏜 몹 — 죽어서 null 이 될 수 있다(자기 피해 제외용일 뿐)
    }

    readonly List<PendingHit> _pending = new();
    static readonly Collider[] HitBuffer = new Collider[64];   // 16칸은 다중 콜라이더로 포화될 수 있다(Codex 10-13)
    static readonly HashSet<Unit> HitUnits = new();

    /// <summary>서버 전용 — 착탄 시각에 판정을 예약한다.</summary>
    public void ServerSchedule(Vector3 point, float radius, float floorTolerance, double impactTime,
                               int damage, LayerMask targetMask, Unit owner)
    {
        _pending.Add(new PendingHit
        {
            point = point, radius = radius, floorTolerance = floorTolerance, impactTime = impactTime,
            damage = damage, targetMask = targetMask, owner = owner,
        });
    }

    void ApplyHit(in PendingHit h)
    {
        HitUnits.Clear();
        // 구(球)는 넉넉히 — 실제 범위는 아래에서 XZ 거리·높이로 다시 거른다.
        float probe = Mathf.Sqrt(h.radius * h.radius + h.floorTolerance * h.floorTolerance) + 1f;
        int n = Physics.OverlapSphereNonAlloc(h.point, probe, HitBuffer, h.targetMask, QueryTriggerInteraction.Collide);
        var info = new AttackInfo(h.damage, AttackType.Default, false);

        for (int i = 0; i < n; i++)
        {
            Collider c = HitBuffer[i];
            if (c == null) continue;

            Hurtbox hurtbox = c.GetComponentInParent<Hurtbox>();
            Unit unit = null;
            if (hurtbox != null) hurtbox.TryGetOwner(out unit);
            if (unit == null) unit = c.GetComponentInParent<Unit>();
            if (unit == null || unit == h.owner || HitUnits.Contains(unit)) continue;

            if (!InFootprint(h, c, unit)) continue;

            // 🔴 쏜 몹이 이미 죽어 파괴됐으면 "가짜 null" 이다 — 그대로 넘기면 받는 쪽이 접근하다 터진다. 진짜 null 로.
            Unit source = h.owner != null ? h.owner : null;
            var ctx = new AttackHitContext(h.point, transform, c, source);
            bool hit = hurtbox != null ? hurtbox.ReceiveAttack(info, ctx) : unit.ReceiveAttack(info, ctx);
            if (hit) HitUnits.Add(unit);
        }
    }

    // 원 = 평면 거리(몸 두께 포함 — 콜라이더의 가장 가까운 점), 높이 = 발이 착탄 바닥 ± 허용치 안.
    public static bool InFootprint(Vector3 point, float radius, float floorTolerance, Vector3 closestOnBody, float feetY)
    {
        float dx = closestOnBody.x - point.x, dz = closestOnBody.z - point.z;
        if (dx * dx + dz * dz > radius * radius) return false;
        return Mathf.Abs(feetY - point.y) <= floorTolerance;
    }

    static bool InFootprint(in PendingHit h, Collider c, Unit unit) =>
        InFootprint(h.point, h.radius, h.floorTolerance, c.ClosestPoint(h.point), unit.transform.position.y);

    // ── 표시(모든 피어) ───────────────────────────────────────────

    sealed class Visual
    {
        public MeshRenderer outer, fill;
        public GameObject bomb;
        public Vector3 point;
        public float radius, duration, fallTime, fallHeight;
        public double impactTime;
        public EffectEntry explode;
        public Vector3 explodeOffset;
        public float explodeScale;
        public TelegraphPlateStyle style;
        public int paintedVersion = -1;
    }

    readonly List<Visual> _visuals = new();
    readonly Stack<Visual> _pool = new();

    const float FallHeight = 9f;   // 폭탄이 나타나는 높이(m) — 탑다운 카메라 위쪽 화면 밖에서 들어오게

    /// <summary>모든 피어 — 원형 예고를 띄우고 착탄 시각에 폭발을 재생한다.</summary>
    public void Show(Vector3 point, float radius, double impactTime, float duration, float fallTime,
                     TelegraphPlateStyle style, GameObject projectilePrefab)
    {
        Visual v = _pool.Count > 0 ? _pool.Pop() : CreateVisual();
        v.point = point;
        v.radius = Mathf.Max(0.05f, radius);
        v.impactTime = impactTime;
        v.duration = Mathf.Max(0.05f, duration);
        v.fallTime = Mathf.Clamp(fallTime, 0.01f, v.duration);
        v.fallHeight = FallHeight;
        v.style = style;
        v.paintedVersion = -1;

        // 폭발·폭탄 모양 = 투사체 프리팹에서 빌린다(투사체 자체는 쓰지 않는다 — 네트워크 객체라서).
        v.explode = null; v.explodeOffset = Vector3.zero; v.explodeScale = 1f;
        if (projectilePrefab != null && projectilePrefab.TryGetComponent(out MonsterProjectileEffects fx))
        {
            v.explode = fx.Explode; v.explodeOffset = fx.ExplodeOffset; v.explodeScale = fx.Scale;
        }
        EnsureBomb(v, projectilePrefab);

        PlaceDisc(v.outer, v.point, v.radius, 0f);
        PlaceDisc(v.fill, v.point, 0.01f, 0.01f);
        Paint(v);
        v.outer.gameObject.SetActive(true);
        v.fill.gameObject.SetActive(true);
        if (v.bomb != null) v.bomb.SetActive(false);
        _visuals.Add(v);
    }

    void Update()
    {
        double now = Now;

        // 서버 판정 — 착탄 시각이 지난 것부터. 리스트를 뒤에서 돈다(제거 안전).
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (now < _pending[i].impactTime) continue;
            PendingHit h = _pending[i];
            _pending.RemoveAt(i);
            ApplyHit(h);
        }

        for (int i = _visuals.Count - 1; i >= 0; i--)
        {
            Visual v = _visuals[i];
            double left = v.impactTime - now;
            if (left <= 0.0)
            {
                if (v.explode != null && EffectManager.Instance != null)
                    EffectManager.Instance.Play(v.explode, v.point + v.explodeOffset, Quaternion.identity, v.explodeScale);
                Release(v);
                _visuals.RemoveAt(i);
                continue;
            }

            if (v.paintedVersion != TelegraphStyleSO.Version) Paint(v);   // Play 중 스타일 조절 — 진행은 그대로

            float k = Mathf.Clamp01(1f - (float)(left / v.duration));
            PlaceDisc(v.fill, v.point, Mathf.Max(0.01f, v.radius * k), 0.01f);

            if (v.bomb != null)
            {
                bool falling = left <= v.fallTime;
                if (v.bomb.activeSelf != falling) v.bomb.SetActive(falling);
                if (falling)
                {
                    float f = 1f - (float)(left / v.fallTime);       // 0 → 1
                    v.bomb.transform.position = v.point + Vector3.up * Mathf.Lerp(v.fallHeight, 0f, f * f);   // 가속 낙하
                }
            }
        }
    }

    static double Now
    {
        get
        {
            NetworkManager nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening ? nm.ServerTime.Time : Time.timeAsDouble;
        }
    }

    Visual CreateVisual()
    {
        var v = new Visual
        {
            outer = BossPatternVisuals.CreateFlat("SkyStrikeOuter", transform),
            fill = BossPatternVisuals.CreateFlat("SkyStrikeFill", transform),
        };
        return v;
    }

    void Paint(Visual v)
    {
        TelegraphPlateStyle s = v.style ?? TelegraphStyleSO.Default.circle;
        Texture2D disc = BossPatternVisuals.DiscTextureFor(s.rimRatio);
        var extent = new Vector2(v.radius * 2f, v.radius * 2f);
        BossPatternVisuals.PaintStyled(v.outer, s.outline, disc, v.point, Vector3.right, Vector3.forward, extent);
        BossPatternVisuals.PaintStyled(v.fill, s.fill, disc, v.point, Vector3.right, Vector3.forward, extent);
        v.paintedVersion = TelegraphStyleSO.Version;
    }

    static void PlaceDisc(MeshRenderer m, Vector3 point, float radius, float lift)
    {
        Transform t = m.transform;
        t.SetPositionAndRotation(point + Vector3.up * (0.06f + lift), Quaternion.identity);   // 바닥 위 살짝 — Z 파이팅 방지
        t.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
    }

    // 폭탄 모양 = 투사체 프리팹의 메시·재질·크기만 복사한 로컬 표시 객체(콜라이더·스크립트 없음).
    readonly Dictionary<GameObject, (Mesh mesh, Material[] mats, Vector3 scale)> _bombLook = new();

    void EnsureBomb(Visual v, GameObject prefab)
    {
        if (prefab == null)
        {
            if (v.bomb != null) v.bomb.SetActive(false);
            return;
        }
        if (!_bombLook.TryGetValue(prefab, out var look))
        {
            MeshFilter mf = prefab.GetComponentInChildren<MeshFilter>(true);
            MeshRenderer mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
            look = (mf != null ? mf.sharedMesh : null, mr != null ? mr.sharedMaterials : null,
                    mf != null ? mf.transform.lossyScale : Vector3.one);
            _bombLook[prefab] = look;
        }
        if (look.mesh == null) return;

        if (v.bomb == null)
        {
            v.bomb = new GameObject("SkyStrikeBomb");
            v.bomb.transform.SetParent(transform, false);
            v.bomb.AddComponent<MeshFilter>();
            var r = v.bomb.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        v.bomb.GetComponent<MeshFilter>().sharedMesh = look.mesh;
        v.bomb.GetComponent<MeshRenderer>().sharedMaterials = look.mats;
        v.bomb.transform.localScale = look.scale;
    }

    void Release(Visual v)
    {
        v.outer.gameObject.SetActive(false);
        v.fill.gameObject.SetActive(false);
        if (v.bomb != null) v.bomb.SetActive(false);
        _pool.Push(v);
    }

    // 씬이 바뀌면(로비 복귀 등) 남은 포격을 버린다 — 다음 판에 뜬금없이 터지지 않게.
    void OnEnable() => UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
    void OnDisable() => UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnSceneChanged;

    void OnSceneChanged(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b)
    {
        _pending.Clear();
        for (int i = _visuals.Count - 1; i >= 0; i--) Release(_visuals[i]);
        _visuals.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() => _instance = null;
}
