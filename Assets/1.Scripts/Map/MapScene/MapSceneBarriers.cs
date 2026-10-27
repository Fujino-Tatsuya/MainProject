using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 튜토리얼 배리어 8개(G1~G4·G6·G7·G9·G10)를 <b>모든 피어에서</b> 만들고 디렉터 게이트 상태대로 연다.
/// 원본: Docs/design/tutorial-scenario.md §2.2·§4 · PLAN-tutorial.md S3 · Docs/tech/tutorial-director.md §7.
///
/// · 비네트워크. <see cref="MapSceneStageMarker"/> 가 Play 중에 자기 루트(StageTutorial)에 붙인다 — 공용 존 프리팹에는 없다.
/// · 만드는 시점 = <see cref="MapSceneDirector.IsTutorialActive"/> 가 처음 true 로 보일 때(<see cref="MapSceneDirector.StateChanged"/>).
///   튜토리얼이 아니면(디렉터 비활성) 만들지 않는다. 늦은 클라는 스폰 직후 StateChanged 에서 지금 열린 게이트를 그대로 반영한다.
/// · 모양 = S2 구간(<see cref="MapSceneAreaLayout"/>)의 복도 사각형 양 끝(<see cref="MapSceneBarrierRules.BuildSpecs"/>).
/// · 막기: 플레이어 = <b>Barrier 레이어</b>(할 일 32 — 전에는 Wall) 비트리거 BoxCollider — 플레이어 모터 스윕 마스크(PlayerGameRuleData.obstacleMask)에
///   Barrier 가 있다. 대시·이동 스킬·넉백도 같은 스윕을 탄다. 이동이 오너 권한이라 각 피어에 콜라이더가 있어야 한다.
///   몬스터 = NavMeshObstacle 카브(ZoneBridgeGate 와 같은 설정). Barrier 는 NavMesh 베이크 수집 레이어(Default·Ground·Wall)가 아니라
///   재베이크(다리)에 섞이지 않는다. 벽이 아니라서 벽 충돌 이벤트(<see cref="UnitWallImpacts"/>)도 나지 않는다. Docs/tech/wall-concept.md
/// · 열기: 콜라이더·카브를 즉시 끄고 짧게 페이드(실제 시간 — 슬로우모션 무관) 뒤 비활성.
/// · 추락 복구: <see cref="PlayerSafePointTracker.ReturnPointFilter"/> 에 닫힌 배리어 너머 구간 · 그 클라이언트의 뒤 배리어 너머 제외를 건다(서버가 부른다).
/// · 개인 뒤 배리어: 서버가 판정해 복제한 '지난 배리어' 마스크(<see cref="MapSceneDirector.GetBackBarrierMask"/>)를 <b>로컬 플레이어</b> 값으로 읽어
///   같은 자리에 Barrier 콜라이더(카브 없음 — 몬스터는 안 막음)를 이 피어에서만 켠다. 이동이 오너 권한·오너 로컬 스윕이라 남의 플레이어엔 영향 없다.
///   켤 때 로컬 플레이어와 겹치면 다음 프레임까지 미룬다. 튜토리얼 Finished 면 끈다(<see cref="MapSceneBackBarrierRules"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class MapSceneBarriers : MonoBehaviour
{
    public const string RootName = "MapSceneBarriers";
    public const float DefaultHeight = 3f;
    const float CarveVerticalPadding = 2f;
    const float FadeSeconds = 0.35f;

    public static readonly Color DefaultColor = new Color(0.25f, 0.85f, 1f, 0.35f);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    sealed class Entry
    {
        public MapSceneBarrierSpec Spec;
        public GameObject Go;
        public bool Opened;
        public GameObject BackGo;   // 개인 뒤 배리어(로컬 전용, 처음 켤 때 만든다)
    }

    readonly List<Entry> _entries = new List<Entry>();
    MapSceneAreaTracker _areas;
    List<MapSceneBarrierSpec> _specs;
    Material _material;
    bool _built;
    int _backWanted;   // 켜야 할 뒤 배리어(게이트 비트)
    int _backActive;   // 실제로 켜진 뒤 배리어

    public static MapSceneBarriers Active { get; private set; }

    public bool IsBuilt => _built;

    /// <summary>배리어가 지금 닫혀 있는가(만들어지지 않았으면 false).</summary>
    public bool IsBarrierClosed(MapSceneGate gate)
    {
        foreach (Entry e in _entries)
            if (e.Spec.Gate == gate) return !e.Opened;
        return false;
    }

    public void SetMaterial(Material material) => _material = material;

    void OnEnable()
    {
        Active = this;
        MapSceneDirector.StateChanged += Refresh;
        MapSceneDirector.GateOpened += HandleGateOpened;
    }

    void Start() => Refresh();   // 디렉터가 이미 스폰돼 StateChanged 를 놓쳤을 때

    void OnDisable()
    {
        MapSceneDirector.StateChanged -= Refresh;
        MapSceneDirector.GateOpened -= HandleGateOpened;
        if (PlayerSafePointTracker.ReturnPointFilter == (System.Func<ulong, Vector3, bool>)IsReturnPointAllowed)
            PlayerSafePointTracker.ReturnPointFilter = null;
        if (Active == this) Active = null;
    }

    void HandleGateOpened(MapSceneGate _, MapSceneGateOpenReason __) => Refresh();

    void Refresh()
    {
        MapSceneDirector d = MapSceneDirector.Instance;
        if (d == null || !d.IsTutorialActive) return;   // 튜토리얼이 아니면 만들지 않는다
        if (!_built) Build(d);

        foreach (Entry e in _entries)
            if (!e.Opened && d.IsGateOpen(e.Spec.Gate)) Open(e, fade: true);

        RefreshBackBarriers(d);
    }

    void Update()
    {
        // 겹쳐서 미룬 뒤 배리어만 매 프레임 다시 시도한다.
        if (MapSceneBackBarrierRules.ToEnable(_backWanted, _backActive) != 0) ApplyBackBarriers();
    }

    // ─── 개인 뒤 배리어(로컬 플레이어 기준, 각 피어) ──────────────────────────

    void RefreshBackBarriers(MapSceneDirector d)
    {
        NetworkManager nm = NetworkManager.Singleton;
        int opened = 0;
        for (int g = 0; g < MapSceneFlowModel.GateCount; g++)
            if (d.IsGateOpen((MapSceneGate)g)) opened |= 1 << g;
        int passed = nm != null ? d.GetBackBarrierMask(nm.LocalClientId) : 0;
        _backWanted = MapSceneBackBarrierRules.LocalColliderMask(passed, opened, d.IsTutorialActive && !d.IsFinished);
        ApplyBackBarriers();
    }

    void ApplyBackBarriers()
    {
        int off = MapSceneBackBarrierRules.ToDisable(_backWanted, _backActive);
        int on = MapSceneBackBarrierRules.ToEnable(_backWanted, _backActive);
        if (off == 0 && on == 0) return;

        Transform root = transform.Find(RootName);
        foreach (Entry e in _entries)
        {
            int bit = MapSceneBackBarrierRules.Bit(e.Spec.Gate);
            if ((off & bit) != 0)
            {
                if (e.BackGo != null) e.BackGo.SetActive(false);
                _backActive &= ~bit;
            }
            else if ((on & bit) != 0)
            {
                if (OverlapsLocalPlayer(e.Spec)) continue;   // 끼임 방지 — 다음 프레임(Update)에 다시
                if (e.BackGo == null)
                {
                    e.BackGo = CreateBarrierObject(e.Spec, root != null ? root : transform, transform.position.y, DefaultHeight,
                                                   _material, DefaultColor, carve: false);
                    e.BackGo.name = $"BackBarrier_{e.Spec.Gate}";
                }
                e.BackGo.SetActive(true);
                _backActive |= bit;
            }
        }
    }

    bool OverlapsLocalPlayer(MapSceneBarrierSpec spec)
    {
        NetworkObject po = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
        if (po == null) return false;

        spec.Rect.GetWorldBounds(out float minX, out float minZ, out float maxX, out float maxZ);
        float y = transform.position.y;
        var box = new Bounds();
        box.SetMinMax(new Vector3(minX, y, minZ), new Vector3(maxX, y + DefaultHeight, maxZ));
        foreach (Collider c in po.GetComponentsInChildren<Collider>())
            if (c.enabled && !c.isTrigger && c.bounds.Intersects(box)) return true;
        return false;
    }

    void Build(MapSceneDirector d)
    {
        _built = true;
        _areas = new MapSceneAreaTracker();
        if (!MapSceneAreaLayout.TryBuild(transform, _areas, out string layoutError))
            Edit.LogWarning($"[Tutorial] 배리어 — 구간 계산 실패: {layoutError}. 계산된 복도만 배리어를 둔다.", this);

        List<MapSceneBarrierSpec> specs = MapSceneBarrierRules.BuildSpecs(
            _areas, MapSceneBarrierRules.DefaultThickness, MapSceneBarrierRules.DefaultSideOverlap, out string specError);
        if (specError != null) Edit.LogWarning($"[Tutorial] 배리어 — {specError}.", this);
        if (_material == null)
            Edit.LogWarning("[Tutorial] 배리어 머티리얼이 없다(MapSceneStageMarker.barrierMaterial) — 기본 머티리얼로 보인다.", this);

        var root = new GameObject(RootName).transform;
        root.SetParent(transform, false);

        _specs = specs;
        int openedAtBuild = 0;
        foreach (MapSceneBarrierSpec s in specs)
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
    /// [서버, 추락 복구] 닫힌 배리어 너머(아직 못 들어가는 구간)의 점이거나, <paramref name="clientId"/> 의 뒤 배리어 너머
    /// (지나온 최전방 구간보다 뒤 · 지난 배리어 앞면에서 여유 미만)면 false. 보스 진입 패드 범위 + 1m 안도 false(C3). 그 밖의 튜토리얼 구간 밖은 true.
    /// 마스크는 그 클라이언트의 서버 원본(<see cref="MapSceneDirector.GetBackBarrierMaskServer"/>) — 호스트 로컬 플레이어 값이 아니다.
    /// </summary>
    public bool IsReturnPointAllowed(ulong clientId, Vector3 p)
    {
        MapSceneDirector d = MapSceneDirector.Instance;
        if (!_built || d == null || !d.IsTutorialActive) return true;

        int mask = 0;
        for (int g = 0; g < MapSceneFlowModel.GateCount; g++)
            if (d.IsGateOpen((MapSceneGate)g)) mask |= 1 << g;
        return MapSceneBackBarrierRules.IsReturnPointAllowed(_areas, _specs, p.x, p.z, mask,
                                                             d.GetBackBarrierMaskServer(clientId), d.BackBarrierClearance,
                                                             MapSceneDeathRules.ServerPadAreas());
    }

    // ─── 생성(런타임·에디터 미리보기 공용) ─────────────────────────────────

    /// <summary>
    /// 배리어 오브젝트 하나 — Barrier 레이어 큐브(BoxCollider 비트리거) + 카브 NavMeshObstacle + 반투명 머티리얼(색은 PropertyBlock).
    /// 바닥 높이 <paramref name="baseY"/> 에서 <paramref name="height"/> 만큼. <paramref name="carve"/> = false 면 NavMeshObstacle 없음(개인 뒤 배리어).
    /// </summary>
    public static GameObject CreateBarrierObject(MapSceneBarrierSpec spec, Transform parent, float baseY, float height,
                                                 Material material, Color color, bool carve = true)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"Barrier_{spec.Gate}";
        go.layer = GameLayers.BarrierLayer;   // 할 일 32 — 벽(Wall)이 아니다: 막기는 같고 벽 충돌 이벤트·NavMesh 베이크에서 빠진다
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
        if (!carve) return go;

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
