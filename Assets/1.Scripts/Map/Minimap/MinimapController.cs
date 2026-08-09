using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VeyTrace.Rendering.Occlusion;

// 우측 하단 미니맵 (라벤스워치 룩 + 스타크래프트식 탐사) — PLAN 2026-07-03.
//  - 지형: 맵 생성 완료(MapGenerator.OnGenerated) 시 오버헤드 카메라로 1회 베이크(높낮이 음영 포함).
//  - 시야 3단계: 미탐사=윤곽 실루엣 / 탐사됨=0.5 디밍 / 현재 시야(팀 합산 반경 SightRadius)=풀 컬러.
//  - 상시 마커: 퀘스트/스폰/보스입구(슬롯 역할) + 티어 노드(NodeMarker). 탐사와 무관하게 항상 표시.
//  - 동적 마커: 내 위치(강조)/팀원(PlayerMovement), 몬스터(MinimapMarker.Monster, 팀 시야 내만).
//  - 네트워크 추가 없음: 팀원/몬스터는 NGO 복제본을 읽고, 탐사 마스크는 클라 로컬 계산(전 클라 동일).
public class MinimapController : MonoBehaviour
{
    [Header("=== 참조 ===")]
    public MapGenerator Generator;

    [Header("=== UI ===")]
    [Tooltip("미니맵 한 변 픽셀 크기")] public float PanelSize = 350f;
    [Tooltip("화면 우하단 여백")] public Vector2 Margin = new Vector2(1f, 1f);
    public float RoleIconSize = 22f;
    public float NodeDotSize = 9f;
    public float UnitDotSize = 11f;

    [Header("=== 시야/탐사 ===")]
    [Tooltip("현재 시야 반경(m) — 팀원 전원 합산")] public float SightRadius = 15f;
    [Tooltip("탐사 마스크 해상도")] public int MaskResolution = 128;
    [Tooltip("마스크 갱신 주기(초)")] public float MaskTick = 0.35f;

    [Header("=== 베이크 ===")]
    [Tooltip("지형 베이크 해상도")] public int BakeResolution = 1024;
    [Tooltip("맵 경계 여유(m)")] public float BoundsMargin = 8f;
    [Tooltip("베이크 카메라 높이(m). 이 높이에서 아래를 내려다본다.")]
    public float BakeCameraHeight = 120f;
    [Tooltip("베이크에 포함할 최저 월드 Y. 이보다 아래 지오메트리는 잘라낸다 — " +
             "어비스 물 Plane(y≈-19, 3300m)이 미니맵을 통째로 덮는 것을 막는다.")]
    public float BakeMinWorldY = -5f;
    [Header("=== 색 ===")]
    public Color LocalPlayerColor = Color.white;
    public Color AllyColor = new Color(0.3f, 0.85f, 1f);
    public Color MonsterColor = new Color(1f, 0.25f, 0.2f);
    public Color NodeT1Color = new Color(1f, 0.45f, 0.15f);
    public Color NodeT2Color = new Color(1f, 0.85f, 0.25f);
    public Color NodeT3Color = new Color(0.4f, 0.95f, 0.4f);

    // 베이크/마스크
    private RenderTexture _bakeRT;
    private Texture2D _maskTex;
    private Color32[] _maskPixels;
    private byte[] _explored;          // 누적 탐사 (마스크 R)
    private Rect _worldRect;           // 맵 월드 XZ 경계 (베이크/마스크 공통 좌표계)
    private bool _baked;
    private float _maskTimer;

    // UI
    private Canvas _canvas;
    private RectTransform _mapRect;    // 마커 부모 (RawImage 위)
    private Material _mapMat;
    private Sprite _dotSprite;

    // 마커
    private readonly List<(RectTransform rt, Vector3 world)> _staticMarkers = new List<(RectTransform, Vector3)>();
    private readonly Dictionary<Component, Image> _dynMarkers = new Dictionary<Component, Image>();
    private readonly List<Component> _dynRemove = new List<Component>();
    private readonly List<Transform> _players = new List<Transform>(); // TODO: NetworkManager.ConnectedClients 기반으로 주기적 캐싱 구현 필요
    private float _playerScanTimer;
    private int _lastPlayerCount = -1;
    private readonly List<Renderer> _v3MapRenderers = new List<Renderer>();
    private readonly Dictionary<int, Vector3> _v3ZoneCenters = new Dictionary<int, Vector3>();

    [SerializeField] private Material MinimapComsite;

    private void Awake()
    {
        _explored = new byte[MaskResolution * MaskResolution];
    }

    private void OnEnable() => MapGenerator.OnGenerated += HandleGenerated;
    private void OnDisable() => MapGenerator.OnGenerated -= HandleGenerated;

    private void HandleGenerated(MapGenerator gen)
    {
        Generator = gen;
        if (!CollectV3MapGeometry())
        {
            _baked = false;
            if (_canvas != null) _canvas.enabled = false;
            return;
        }

        ComputeWorldRect();
        BakeTerrain();
        EnsureUI();
        BuildSilhouette();
        ResetMask();
        BuildStaticMarkers(gen);
        _baked = true;

        // 클라에서 씬 로드 직후 렌더 요청이 빈 결과를 줄 수 있음 — 밝기 0이면 재시도
        if (_lastBakeLuminance < 0.01f) StartCoroutine(RetryBake());
    }

    private float _lastBakeLuminance;

    private System.Collections.IEnumerator RetryBake()
    {
        for (int i = 0; i < 4 && _lastBakeLuminance < 0.01f; i++)
        {
            yield return new WaitForSeconds(0.5f);
            Debug.LogWarning($"[Minimap] 베이크 밝기 0 — 재시도 {i + 1}/4");
            BakeTerrain();
        }
    }

    // ---------------- 베이크 ----------------

    private bool CollectV3MapGeometry()
    {
        _v3MapRenderers.Clear();
        _v3ZoneCenters.Clear();

        int cullingMask = BuildTerrainCullingMask();
        var seen = new HashSet<Renderer>();
        var zoneBounds = new Dictionary<int, Bounds>();
        int levelCount = 0;

        foreach (ElevationLevel level in FindObjectsByType<ElevationLevel>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (level == null || !level.isActiveAndEnabled || !level.gameObject.activeInHierarchy)
                continue;

            levelCount++;
            GeneratedZoneIdentity zone = level.GetComponentInParent<GeneratedZoneIdentity>();
            IReadOnlyList<Renderer> renderers = level.ContentRenderers;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (!IsV3MapRenderer(renderer, cullingMask))
                    continue;

                if (seen.Add(renderer))
                    _v3MapRenderers.Add(renderer);

                if (zone == null)
                    continue;

                Bounds bounds = renderer.bounds;
                if (zoneBounds.TryGetValue(zone.SlotID, out Bounds existing))
                {
                    existing.Encapsulate(bounds);
                    zoneBounds[zone.SlotID] = existing;
                }
                else
                {
                    zoneBounds.Add(zone.SlotID, bounds);
                }
            }
        }

        foreach (KeyValuePair<int, Bounds> pair in zoneBounds)
            _v3ZoneCenters[pair.Key] = pair.Value.center;

        if (_v3MapRenderers.Count == 0)
        {
            Debug.LogError(
                "[Minimap] V3 지형 Renderer가 없습니다 — Legacy Footprint로 폴백하지 않습니다. " +
                "생성된 Stage/Zone의 ElevationLevel Content 등록을 확인하세요.");
            return false;
        }

        Debug.Log(
            $"[Minimap] V3 지형 수집 완료 — ElevationLevel {levelCount}, " +
            $"Renderer {_v3MapRenderers.Count}, Zone 중심 {_v3ZoneCenters.Count}.");
        return true;
    }

    private static bool IsV3MapRenderer(Renderer renderer, int cullingMask)
    {
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            return false;
        if ((cullingMask & (1 << renderer.gameObject.layer)) == 0)
            return false;

        Bounds bounds = renderer.bounds;
        Vector3 size = bounds.size;
        return IsFinite(bounds.center) && IsFinite(size) && size.x > 0.001f && size.z > 0.001f;
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static int BuildTerrainCullingMask()
    {
        int mask = ~0;
        foreach (string layerName in new[]
                 {
                     "UI", "Player", "Monster", "Unit", "Water", "Soul", "Corpse", "Projectile"
                 })
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) mask &= ~(1 << layer);
        }

        return mask;
    }

    private void ComputeWorldRect()
    {
        Bounds mapBounds = _v3MapRenderers[0].bounds;
        for (int i = 1; i < _v3MapRenderers.Count; i++)
            mapBounds.Encapsulate(_v3MapRenderers[i].bounds);

        float minX = mapBounds.min.x - BoundsMargin;
        float maxX = mapBounds.max.x + BoundsMargin;
        float minZ = mapBounds.min.z - BoundsMargin;
        float maxZ = mapBounds.max.z + BoundsMargin;
        // 정사각 유지(미니맵 비율 왜곡 방지) — 짧은 축을 중앙 기준으로 확장
        float w = maxX - minX, h = maxZ - minZ, side = Mathf.Max(w, h);
        float cx = (minX + maxX) * 0.5f, cz = (minZ + maxZ) * 0.5f;
        _worldRect = new Rect(cx - side * 0.5f, cz - side * 0.5f, side, side);
    }

    private void BakeTerrain()
    {
        if (_bakeRT == null)
        {
            _bakeRT = new RenderTexture(BakeResolution, BakeResolution, 16, RenderTextureFormat.ARGB32);
            _bakeRT.name = "MinimapBake";
        }

        var camGo = new GameObject("MinimapBakeCam");
        camGo.transform.SetParent(transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = _worldRect.width * 0.5f;
        cam.transform.position = new Vector3(_worldRect.center.x, BakeCameraHeight, _worldRect.center.y);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.nearClipPlane = 1f;
        // ⚠️ far를 300으로 두면 y=-19의 어비스 물 Plane(스케일 330 = 3300m)까지 구워져
        // 미니맵 전체가 큰 사각형으로 채워진다(배경 알파 0 실루엣이 무의미해짐).
        // 지형이 있는 높이까지만 본다.
        cam.farClipPlane = Mathf.Max(10f, BakeCameraHeight - BakeMinWorldY);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // 알파 0 = 맵 실루엣 마스크
        cam.targetTexture = _bakeRT;
        // 유닛/UI 제외 — 지형만 굽는다 (레이어가 없으면 무시됨)
        cam.cullingMask = BuildTerrainCullingMask();
        cam.enabled = false;

        // URP에서 Camera.Render()는 동작하지 않음 — 렌더 요청 API 사용 (Unity 6)
        var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = _bakeRT };
        if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
        else
            cam.Render(); // 빌트인 파이프라인 폴백

        cam.targetTexture = null;
        Destroy(camGo);
        LogBakeCoverage();
    }

    // 베이크 진단 — 중앙 64px 샘플의 평균 밝기 로그 (0에 가까우면 베이크 실패 의심)
    private void LogBakeCoverage()
    {
        var prev = RenderTexture.active;
        RenderTexture.active = _bakeRT;
        var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(_bakeRT.width / 2 - 32, _bakeRT.height / 2 - 32, 64, 64), 0, 0);
        t.Apply(false);
        RenderTexture.active = prev;
        float lum = 0f;
        var px = t.GetPixels32();
        foreach (var c in px) lum += (c.r + c.g + c.b) / (3f * 255f);
        _lastBakeLuminance = lum / px.Length;
        Debug.Log($"[Minimap] 베이크 완료 — 중앙 샘플 평균 밝기 {_lastBakeLuminance:F3} (0이면 렌더 실패 의심)");
        Destroy(t);
    }

    // ---------------- 맵 실루엣 (V3 등록 Renderer XZ 합집합, CPU 생성) ----------------
    // 베이크 알파는 URP 설정에 따라 불안정하므로, V3 ElevationLevel에 등록된 실제 Renderer Bounds를
    // 같은 월드 좌표계에 합성한다. Legacy Slot Footprint나 Stage1 경로는 사용하지 않는다.
    private Texture2D _silTex;

    private void BuildSilhouette()
    {
        const int res = 256;
        if (_silTex == null)
        {
            _silTex = new Texture2D(res, res, TextureFormat.R8, false);
            _silTex.name = "MinimapSilhouette";
            _silTex.wrapMode = TextureWrapMode.Clamp;
        }
        var px = new byte[res * res];

        void FillWorldRect(float minX, float minZ, float maxX, float maxZ)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - _worldRect.xMin) / _worldRect.width * res), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - _worldRect.xMin) / _worldRect.width * res), 0, res - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((minZ - _worldRect.yMin) / _worldRect.height * res), 0, res - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - _worldRect.yMin) / _worldRect.height * res), 0, res - 1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                px[y * res + x] = 255;
        }

        // Stage와 생성 Zone의 모든 고도를 XZ 합집합으로 표시한다.
        for (int i = 0; i < _v3MapRenderers.Count; i++)
        {
            Bounds bounds = _v3MapRenderers[i].bounds;
            FillWorldRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
        }

        _silTex.SetPixelData(px, 0);
        _silTex.Apply(false);
        _mapMat.SetTexture("_SilTex", _silTex);
    }

    // ---------------- 탐사 마스크 (CPU 스탬프) ----------------

    private void ResetMask()
    {
        int n = MaskResolution * MaskResolution;
        if (_maskTex == null || _maskTex.width != MaskResolution)
        {
            _maskTex = new Texture2D(MaskResolution, MaskResolution, TextureFormat.RGBA32, false);
            _maskTex.name = "MinimapMask";
            _maskTex.wrapMode = TextureWrapMode.Clamp;
            _maskPixels = new Color32[n];
            if (_mapMat != null) _mapMat.SetTexture("_MaskTex", _maskTex);
        }
        _explored = new byte[n];
        for (int i = 0; i < n; i++) _maskPixels[i] = new Color32(0, 0, 0, 255);
        _maskTex.SetPixels32(_maskPixels);
        _maskTex.Apply(false);
    }

    private void UpdateMask()
    {
        if (_explored == null) return;
        int res = MaskResolution;
        float pxPerMeter = res / _worldRect.width;
        int rPx = Mathf.Max(1, Mathf.RoundToInt(SightRadius * pxPerMeter));
        int rSq = rPx * rPx;

        // 현재 시야(G)는 매 틱 다시 계산, 탐사(R)는 누적
        for (int i = 0; i < _maskPixels.Length; i++) { _maskPixels[i].g = 0; _maskPixels[i].r = _explored[i]; }

        foreach (var p in _players)
        {
            if (p == null) continue;
            Vector3 wp = p.position;
            int cx = Mathf.RoundToInt((wp.x - _worldRect.xMin) * pxPerMeter);
            int cy = Mathf.RoundToInt((wp.z - _worldRect.yMin) * pxPerMeter);
            int x0 = Mathf.Max(0, cx - rPx), x1 = Mathf.Min(res - 1, cx + rPx);
            int y0 = Mathf.Max(0, cy - rPx), y1 = Mathf.Min(res - 1, cy + rPx);
            for (int y = y0; y <= y1; y++)
            {
                int dy = y - cy;
                int row = y * res;
                for (int x = x0; x <= x1; x++)
                {
                    int dx = x - cx;
                    int dSq = dx * dx + dy * dy;
                    if (dSq > rSq) continue;
                    // 가장자리 소프트 (바깥 30% 페이드)
                    float t = 1f - Mathf.Sqrt((float)dSq / rSq);
                    byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(1f, t / 0.3f) * 255f), 0, 255);
                    int idx = row + x;
                    if (v > _maskPixels[idx].g) _maskPixels[idx].g = v;
                    if (v > _explored[idx]) { _explored[idx] = v; _maskPixels[idx].r = v; }
                }
            }
        }
        _maskTex.SetPixels32(_maskPixels);
        _maskTex.Apply(false);
    }

    // ---------------- UI ----------------

    private void EnsureUI()
    {
        if (_canvas != null)
        {
            _mapMat.SetTexture("_MainTex", _bakeRT);
            return;
        }

        if (MinimapComsite == null)
        {
            // 미배정이면 미니맵 UI 자체가 안 만들어진다 — 조용히 사라지면 원인 추적이 오래 걸리므로 알린다.
            // 인스펙터 참조가 정본인 이유: 이 참조가 없으면 UI/MinimapComposite 셰이더를 아무 에셋도
            // 참조하지 않게 되어 빌드에서 스트립된다(Shader.Find 로는 빌드에서 못 찾는다).
            Debug.LogError("[Minimap] MinimapComsite 머티리얼 미할당 — 미니맵을 생성하지 않는다. " +
                           "4.MapScene 의 Minimap 오브젝트에 3.Materials/MinimapComposite/MinimapComposite.mat 을 배정할 것.");
            return;
        }

        var canvasGo = new GameObject("MinimapCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 40;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);

        var mapGo = new GameObject("Minimap", typeof(RawImage));
        mapGo.transform.SetParent(canvasGo.transform, false);
        var raw = mapGo.GetComponent<RawImage>();
        //var shader = Shader.Find("UI/MinimapComposite");
        //_mapMat = new Material(shader);

        _mapMat = new Material(MinimapComsite); //##경슥아 이거 수정했음 26.7.31

        _mapMat.SetTexture("_MainTex", _bakeRT);
        raw.texture = _bakeRT;
        raw.material = _mapMat;

        _mapRect = mapGo.GetComponent<RectTransform>();
        _mapRect.anchorMin = _mapRect.anchorMax = new Vector2(1f, 0f); // 우하단
        _mapRect.pivot = new Vector2(1f, 0f);
        _mapRect.anchoredPosition = new Vector2(-Margin.x, Margin.y);
        _mapRect.sizeDelta = Vector2.one * PanelSize;

        _dotSprite = MakeCircleSprite(32);
        if (_maskTex != null) _mapMat.SetTexture("_MaskTex", _maskTex);

        _mapRect.sizeDelta = Vector2.one * PanelSize; // 인스펙터/기본값 반영
        // 네트워크 세션 중이면 플레이어 스폰 전(로딩 화면)엔 숨김 — Update가 상태 갱신
        var nm = Unity.Netcode.NetworkManager.Singleton;
        _canvas.enabled = !(nm != null && nm.IsListening);
    }

    private static Sprite MakeCircleSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f, r = size * 0.5f - 1f;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float a = Mathf.Clamp01(r - d); // 1px 안티에일리어싱
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
    }

    private Vector2 WorldToMap(Vector3 world)
    {
        float u = (world.x - _worldRect.xMin) / _worldRect.width;
        float v = (world.z - _worldRect.yMin) / _worldRect.height;
        return new Vector2(u * PanelSize - PanelSize, v * PanelSize); // pivot(1,0) 기준
    }

    private Image MakeMarkerImage(string name, Sprite sprite, Color color, float size)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(_mapRect, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(1f, 0f);
        img.rectTransform.sizeDelta = Vector2.one * size;
        return img;
    }

    // ---------------- 상시 마커 (생성 시 1회) ----------------

    private void BuildStaticMarkers(MapGenerator gen)
    {
        foreach (var (rt, _) in _staticMarkers) if (rt != null) Destroy(rt.gameObject);
        _staticMarkers.Clear();

        var cat = gen.Catalog;
        foreach (var s in gen.Slots)
        {
            (Texture2D tex, string label) = s.AssignedRole switch
            {
                ZoneRole.Quest       => (cat != null ? cat.QuestIcon : null, "Quest"),
                ZoneRole.PlayerSpawn => (cat != null ? cat.SpawnIcon : null, "Spawn"),
                ZoneRole.BossRoom    => (cat != null ? cat.BossIcon : null, "Boss"),
                _ => (null, null),
            };
            if (label == null) continue;
            var img = MakeMarkerImage($"Role_{label}", null, Color.white, RoleIconSize);
            if (tex != null)
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
            else { img.sprite = _dotSprite; img.color = label == "Boss" ? MonsterColor : Color.yellow; }
            if (!_v3ZoneCenters.TryGetValue(s.SlotID, out Vector3 markerWorld))
            {
                Debug.LogWarning(
                    $"[Minimap] Slot {s.SlotID} 역할 마커의 V3 Zone Renderer 중심을 찾지 못해 " +
                    $"{label} 아이콘을 생략합니다.");
                Destroy(img.gameObject);
                continue;
            }

            img.rectTransform.anchoredPosition = WorldToMap(markerWorld);
            _staticMarkers.Add((img.rectTransform, markerWorld));
        }

        // 티어 노드 — 스폰된 존 안의 NodeMarker (보상 오브젝트 시스템 확정 전 임시 소스)
        foreach (var node in FindObjectsByType<NodeMarker>(FindObjectsSortMode.None))
        {
            Color c = node.Tier switch
            {
                NodeTier.Tier1_Large => NodeT1Color,
                NodeTier.Tier2_Medium => NodeT2Color,
                _ => NodeT3Color,
            };
            var img = MakeMarkerImage($"Node_{node.Tier}", _dotSprite, c, NodeDotSize);
            img.rectTransform.anchoredPosition = WorldToMap(node.transform.position);
            _staticMarkers.Add((img.rectTransform, node.transform.position));
        }
    }

    // ---------------- 크기 조절 ([ = 축소, ] = 확대) ----------------

    private static readonly float[] SizePresets = { 300f, 400f, 520f };

    private void ApplyPanelSize(float size)
    {
        PanelSize = size;
        if (_mapRect == null) return;
        _mapRect.sizeDelta = Vector2.one * PanelSize;
        foreach (var (rt, world) in _staticMarkers)
            if (rt != null) rt.anchoredPosition = WorldToMap(world);
        // 동적 마커는 매 프레임 재배치되므로 별도 처리 불필요
    }

    private void HandleSizeInput()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        int dir = kb.rightBracketKey.wasPressedThisFrame ? 1 : kb.leftBracketKey.wasPressedThisFrame ? -1 : 0;
#else
        int dir = Input.GetKeyDown(KeyCode.RightBracket) ? 1 : Input.GetKeyDown(KeyCode.LeftBracket) ? -1 : 0;
#endif
        if (dir == 0) return;
        int cur = 0;
        for (int i = 0; i < SizePresets.Length; i++)
            if (Mathf.Abs(SizePresets[i] - PanelSize) < Mathf.Abs(SizePresets[cur] - PanelSize)) cur = i;
        ApplyPanelSize(SizePresets[Mathf.Clamp(cur + dir, 0, SizePresets.Length - 1)]);
    }

    // ---------------- 동적 마커 (플레이어/몬스터) ----------------

    private void Update()
    {
        if (!_baked) return;

        HandleSizeInput();

        _playerScanTimer -= Time.deltaTime;
        if (_playerScanTimer <= 0f)
        {
            _playerScanTimer = 1f;
            ScanPlayers();
            UpdateCanvasVisibility();
        }

        _maskTimer -= Time.deltaTime;
        if (_maskTimer <= 0f)
        {
            _maskTimer = MaskTick;
            UpdateMask();
        }

        UpdateDynamicMarkers();
    }

    // 로딩 씬 등 게임플레이 전엔 미니맵 숨김 (팀장 지시 2026-07-03) —
    // 네트워크 세션 중엔 플레이어가 스폰된 뒤에만 표시. 오프라인(에디터 단독 테스트)은 항상 표시.
    private void UpdateCanvasVisibility()
    {
        if (_canvas == null) return;
        var nm = Unity.Netcode.NetworkManager.Singleton;
        bool online = nm != null && nm.IsListening;
        _canvas.enabled = !online || _players.Count > 0;
    }

    // [TODO] 플레이어 캐싱 최적화 및 영혼(Ghost) 부활 시스템 대응 예정
    // - 내일 플레이어 머지 이후, Player.cs 측에 정적 리스트(AllPlayers)를 두어 스폰 시 자동 캐싱하도록 설계.
    // - 이후 본 메서드의 FindObjectsByType 주기적 폴링을 제거하고, 그 리스트를 직접 순회하도록 수정.
    // - 기획된 다중 목숨 시스템에 따라, 영혼 상태일 때의 마커 처리(색상 변경 등) 로직을 통합.
    private void ScanPlayers()
    {
        _players.Clear();
        foreach (var no in FindObjectsByType<Unity.Netcode.NetworkObject>(FindObjectsSortMode.None))
            if (no.IsPlayerObject) _players.Add(no.transform);
        if (_players.Count == 0)
            foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
                _players.Add(pm.transform);
        if (_players.Count != _lastPlayerCount)
        {
            _lastPlayerCount = _players.Count;
            Debug.Log($"[Minimap] 플레이어 {_players.Count}명 추적 중 (시야/탐사 스탬프 대상)");
        }
    }

    private void UpdateDynamicMarkers()
    {
        // 플레이어
        foreach (var p in _players)
        {
            if (p == null) continue;
            var img = GetOrCreateDyn(p, IsLocal(p) ? LocalPlayerColor : AllyColor, UnitDotSize);
            img.rectTransform.anchoredPosition = WorldToMap(p.position);
            img.enabled = true;
        }

        // 몬스터 (MinimapMarker) — 팀 시야 내에서만
        foreach (var m in MinimapMarker.All)
        {
            if (m == null || m.Type != MinimapMarkerType.Monster) continue;
            var img = GetOrCreateDyn(m, MonsterColor, UnitDotSize * 0.8f);
            bool inSight = false;
            foreach (var p in _players)
            {
                if (p == null) continue;
                Vector3 d = m.transform.position - p.position;
                d.y = 0f;
                if (d.sqrMagnitude <= SightRadius * SightRadius) { inSight = true; break; }
            }
            img.enabled = inSight;
            if (inSight) img.rectTransform.anchoredPosition = WorldToMap(m.transform.position);
        }

        // 파괴된 대상 정리
        _dynRemove.Clear();
        foreach (var kv in _dynMarkers)
            if (kv.Key == null) { if (kv.Value != null) Destroy(kv.Value.gameObject); _dynRemove.Add(kv.Key); }
        foreach (var k in _dynRemove) _dynMarkers.Remove(k);
    }

    private Image GetOrCreateDyn(Component key, Color color, float size)
    {
        if (_dynMarkers.TryGetValue(key, out var img) && img != null) return img;
        img = MakeMarkerImage($"Dyn_{key.name}", _dotSprite, color, size);
        _dynMarkers[key] = img;
        return img;
    }

    private static bool IsLocal(Component p)
    {
        var no = p.GetComponentInParent<Unity.Netcode.NetworkObject>();
        if (no != null) return no.IsOwner;
        return true; // 비네트워크(에디터 단독 테스트) — 전부 내 것으로 간주
    }

    // ---------------- 탐사 상태 네트워크 공유 API (MinimapNetworkSync가 사용) ----------------
    // 서버가 자기 탐사 그리드를 비트팩으로 뽑아 브로드캐스트 → 클라는 병합(OR).
    // 클라 로컬 스탬프는 즉각 반응용으로 유지되고, 서버 브로드캐스트가 최종 일치를 보장한다.

    public bool IsReady => _baked && _explored != null;

    // 탐사 그리드를 1비트/셀로 팩킹 (임계 128). 반환 길이 = res*res/8.
    public byte[] GetExploredBits()
    {
        if (_explored == null) return null;
        var bits = new byte[_explored.Length / 8];
        for (int i = 0; i < _explored.Length; i++)
            if (_explored[i] >= 128) bits[i >> 3] |= (byte)(1 << (i & 7));
        return bits;
    }

    // 서버 탐사 비트를 로컬 그리드에 병합
    public void MergeExploredBits(byte[] bits)
    {
        if (_explored == null || bits == null || bits.Length != _explored.Length / 8) return;
        for (int i = 0; i < _explored.Length; i++)
            if ((bits[i >> 3] & (1 << (i & 7))) != 0 && _explored[i] < 255)
                _explored[i] = 255;
    }

    private void OnDestroy()
    {
        if (_bakeRT != null) _bakeRT.Release();
    }
}
