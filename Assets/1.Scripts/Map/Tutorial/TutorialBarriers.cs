using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 튜토리얼 배리어 8개(G1~G4·G6·G7·G9·G10)를 <b>모든 피어에서</b> 만들고 디렉터 게이트 상태대로 연다.
/// 원본: Docs/design/tutorial-scenario.md §2.2·§4 · PLAN-tutorial.md S3 · Docs/tech/tutorial-director.md §7.
///
/// · 비네트워크. <see cref="TutorialStageMarker"/> 가 Play 중에 자기 루트(StageTutorial)에 붙인다 — 공용 존 프리팹에는 없다.
/// · 만드는 시점 = <see cref="TutorialDirector.IsTutorialActive"/> 가 처음 true 로 보일 때(<see cref="TutorialDirector.StateChanged"/>).
///   튜토리얼이 아니면(디렉터 비활성) 만들지 않는다. 늦은 클라는 스폰 직후 StateChanged 에서 지금 열린 게이트를 그대로 반영한다.
/// · 모양 = S2 구간(<see cref="TutorialAreaLayout"/>)의 복도 사각형 양 끝(<see cref="TutorialBarrierRules.BuildSpecs"/>).
/// · 막기: 플레이어 = <b>Wall 레이어</b> 비트리거 BoxCollider — 플레이어 모터 스윕 마스크(PlayerGameRuleData.obstacleMask)에
///   Wall 이 있다. 대시·이동 스킬·넉백도 같은 스윕을 탄다. 이동이 오너 권한이라 각 피어에 콜라이더가 있어야 한다.
///   몬스터 = NavMeshObstacle 카브(ZoneBridgeGate 와 같은 설정). Wall 은 NavMesh 베이크 수집 레이어(Default·Ground)가 아니라
///   재베이크(다리)에 섞이지 않는다.
/// · 열기: 콜라이더·카브를 즉시 끄고 짧게 페이드(실제 시간 — 슬로우모션 무관) 뒤 비활성.
/// · 추락 복구: <see cref="PlayerSafePointTracker.ReturnPointFilter"/> 에 닫힌 배리어 너머 구간 제외를 건다(서버가 부른다).
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialBarriers : MonoBehaviour
{
    public const string RootName = "TutorialBarriers";
    public const float DefaultHeight = 3f;
    const float CarveVerticalPadding = 2f;
    const float FadeSeconds = 0.35f;

    public static readonly Color DefaultColor = new Color(0.25f, 0.85f, 1f, 0.35f);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    sealed class Entry
    {
        public TutorialBarrierSpec Spec;
        public GameObject Go;
        public bool Opened;
    }

    readonly List<Entry> _entries = new List<Entry>();
    TutorialAreaTracker _areas;
    Material _material;
    bool _built;

    public static TutorialBarriers Active { get; private set; }

    public bool IsBuilt => _built;

    /// <summary>배리어가 지금 닫혀 있는가(만들어지지 않았으면 false).</summary>
    public bool IsBarrierClosed(TutorialGate gate)
    {
        foreach (Entry e in _entries)
            if (e.Spec.Gate == gate) return !e.Opened;
        return false;
    }

    public void SetMaterial(Material material) => _material = material;

    void OnEnable()
    {
        Active = this;
        TutorialDirector.StateChanged += Refresh;
        TutorialDirector.GateOpened += HandleGateOpened;
    }

    void Start() => Refresh();   // 디렉터가 이미 스폰돼 StateChanged 를 놓쳤을 때

    void OnDisable()
    {
        TutorialDirector.StateChanged -= Refresh;
        TutorialDirector.GateOpened -= HandleGateOpened;
        if (PlayerSafePointTracker.ReturnPointFilter == (System.Func<Vector3, bool>)IsReturnPointAllowed)
            PlayerSafePointTracker.ReturnPointFilter = null;
        if (Active == this) Active = null;
    }

    void HandleGateOpened(TutorialGate _, TutorialGateOpenReason __) => Refresh();

    void Refresh()
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (d == null || !d.IsTutorialActive) return;   // 튜토리얼이 아니면 만들지 않는다
        if (!_built) Build(d);

        foreach (Entry e in _entries)
            if (!e.Opened && d.IsGateOpen(e.Spec.Gate)) Open(e, fade: true);
    }

    void Build(TutorialDirector d)
    {
        _built = true;
        _areas = new TutorialAreaTracker();
        if (!TutorialAreaLayout.TryBuild(transform, _areas, out string layoutError))
            Edit.LogWarning($"[Tutorial] 배리어 — 구간 계산 실패: {layoutError}. 계산된 복도만 배리어를 둔다.", this);

        List<TutorialBarrierSpec> specs = TutorialBarrierRules.BuildSpecs(
            _areas, TutorialBarrierRules.DefaultThickness, TutorialBarrierRules.DefaultSideOverlap, out string specError);
        if (specError != null) Edit.LogWarning($"[Tutorial] 배리어 — {specError}.", this);
        if (_material == null)
            Edit.LogWarning("[Tutorial] 배리어 머티리얼이 없다(TutorialStageMarker.barrierMaterial) — 기본 머티리얼로 보인다.", this);

        var root = new GameObject(RootName).transform;
        root.SetParent(transform, false);

        int openedAtBuild = 0;
        foreach (TutorialBarrierSpec s in specs)
        {
            var e = new Entry { Spec = s, Go = CreateBarrierObject(s, root, transform.position.y, DefaultHeight, _material, DefaultColor) };
            _entries.Add(e);
            // 이미 열린 게이트(늦은 클라·재빌드) — 페이드 없이 바로 끈다
            if (d.IsGateOpen(s.Gate)) { Open(e, fade: false); openedAtBuild++; }
        }

        PlayerSafePointTracker.ReturnPointFilter = IsReturnPointAllowed;
        Edit.Log($"[Tutorial] 배리어 {specs.Count}개 생성(이미 열림 {openedAtBuild}).", this);
    }

    void Open(Entry e, bool fade)
    {
        e.Opened = true;
        if (e.Go == null) return;

        // 막기는 즉시 푼다 — 페이드 중에 걸리면 '열렸는데 막힘'으로 읽힌다.
        foreach (Collider c in e.Go.GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (NavMeshObstacle o in e.Go.GetComponentsInChildren<NavMeshObstacle>()) o.enabled = false;

        if (fade && isActiveAndEnabled) StartCoroutine(FadeOut(e.Go));
        else e.Go.SetActive(false);
    }

    static IEnumerator FadeOut(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        var block = new MaterialPropertyBlock();
        Vector3 scale = go.transform.localScale;
        for (float t = 0f; t < FadeSeconds && go != null; t += Time.unscaledDeltaTime)
        {
            float k = 1f - t / FadeSeconds;
            go.transform.localScale = new Vector3(scale.x, scale.y * k, scale.z);
            if (r != null) SetColor(r, block, new Color(DefaultColor.r, DefaultColor.g, DefaultColor.b, DefaultColor.a * k));
            yield return null;
        }
        if (go != null) go.SetActive(false);
    }

    /// <summary>
    /// [서버, 추락 복구] 닫힌 배리어 너머(아직 못 들어가는 구간)의 점이면 false. 튜토리얼 구간 밖은 true.
    /// </summary>
    public bool IsReturnPointAllowed(Vector3 p)
    {
        TutorialDirector d = TutorialDirector.Instance;
        if (!_built || d == null || !d.IsTutorialActive) return true;

        int mask = 0;
        for (int g = 0; g < TutorialFlowModel.GateCount; g++)
            if (d.IsGateOpen((TutorialGate)g)) mask |= 1 << g;
        return TutorialBarrierRules.IsPointReachable(_areas, p.x, p.z, mask);
    }

    // ─── 생성(런타임·에디터 미리보기 공용) ─────────────────────────────────

    /// <summary>
    /// 배리어 오브젝트 하나 — Wall 레이어 큐브(BoxCollider 비트리거) + 카브 NavMeshObstacle + 반투명 머티리얼(색은 PropertyBlock).
    /// 바닥 높이 <paramref name="baseY"/> 에서 <paramref name="height"/> 만큼.
    /// </summary>
    public static GameObject CreateBarrierObject(TutorialBarrierSpec spec, Transform parent, float baseY, float height,
                                                 Material material, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"Barrier_{spec.Gate}";
        go.layer = LayerMask.NameToLayer("Wall");
        go.transform.SetParent(parent, false);

        spec.Rect.GetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ);
        go.transform.position = new Vector3((minX + maxX) * 0.5f, baseY + height * 0.5f, (minZ + maxZ) * 0.5f);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = new Vector3(maxX - minX, height, maxZ - minZ);

        var r = go.GetComponent<MeshRenderer>();
        if (material != null) r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        SetColor(r, new MaterialPropertyBlock(), color);

        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = Vector3.zero;
        // 로컬 크기(스케일 곱) — 위아래로 넉넉히 부풀려 NavMesh 표면을 확실히 파낸다(ZoneBridgeGate 와 같은 이유).
        obstacle.size = new Vector3(1f, 1f + CarveVerticalPadding / Mathf.Max(0.01f, height), 1f);
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        return go;
    }

    static void SetColor(Renderer r, MaterialPropertyBlock block, Color c)
    {
        r.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c);
        block.SetColor(ColorId, c);
        r.SetPropertyBlock(block);
    }
}
