using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
    [Tooltip("미니맵 한 변 픽셀 크기")] public float PanelSize = 260f;
    [Tooltip("화면 우하단 여백")] public Vector2 Margin = new Vector2(16f, 16f);
    public float RoleIconSize = 22f;
    public float NodeDotSize = 9f;
    public float UnitDotSize = 11f;

    [Header("=== 시야/탐사 ===")]
    [Tooltip("현재 시야 반경(m) — 팀원 전원 합산")] public float SightRadius = 15f;
    [Tooltip("탐사 마스크 해상도")] public int MaskResolution = 128;
    [Tooltip("마스크 갱신 주기(초)")] public float MaskTick = 0.15f;

    [Header("=== 베이크 ===")]
    [Tooltip("지형 베이크 해상도")] public int BakeResolution = 1024;
    [Tooltip("맵 경계 여유(m)")] public float BoundsMargin = 8f;

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
    private readonly List<Transform> _players = new List<Transform>();
    private float _playerScanTimer;
    private int _lastPlayerCount = -1;

    private void OnEnable() => MapGenerator.OnGenerated += HandleGenerated;
    private void OnDisable() => MapGenerator.OnGenerated -= HandleGenerated;

    private void HandleGenerated(MapGenerator gen)
    {
        Generator = gen;
        ComputeWorldRect(gen);
        BakeTerrain();
        EnsureUI();
        BuildSilhouette(gen);
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

    private void ComputeWorldRect(MapGenerator gen)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var s in gen.Slots)
        {
            // 회전(90°) 시 풋프린트 스왑
            int steps = Mathf.RoundToInt(s.transform.eulerAngles.y / 90f) & 3;
            Vector2 half = (steps & 1) == 1
                ? new Vector2(s.Footprint.y, s.Footprint.x) * 0.5f
                : s.Footprint * 0.5f;
            Vector3 p = s.transform.position;
            minX = Mathf.Min(minX, p.x - half.x); maxX = Mathf.Max(maxX, p.x + half.x);
            minZ = Mathf.Min(minZ, p.z - half.y); maxZ = Mathf.Max(maxZ, p.z + half.y);
        }
        minX -= BoundsMargin; maxX += BoundsMargin; minZ -= BoundsMargin; maxZ += BoundsMargin;
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
        cam.transform.position = new Vector3(_worldRect.center.x, 120f, _worldRect.center.y);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.nearClipPlane = 1f;
        cam.farClipPlane = 300f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // 알파 0 = 맵 실루엣 마스크
        cam.targetTexture = _bakeRT;
        // 유닛/UI 제외 — 지형만 굽는다 (레이어가 없으면 무시됨)
        int mask = ~0;
        foreach (string ln in new[] { "UI", "Player", "Monster", "Unit" })
        {
            int l = LayerMask.NameToLayer(ln);
            if (l >= 0) mask &= ~(1 << l);
        }
        cam.cullingMask = mask;
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

    // ---------------- 맵 실루엣 (존+다리 모양, CPU 생성) ----------------
    // 베이크 알파는 URP 설정에 따라 불안정 → 슬롯 풋프린트 + 다리(Corridors 렌더러 AABB)로
    // 결정적으로 그린다. 미탐사 상태에서도 "존이 어떻게 연결됐는지"가 보이는 큰 틀.
    private Texture2D _silTex;

    private void BuildSilhouette(MapGenerator gen)
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

        // 존 풋프린트 (회전 반영)
        foreach (var s in gen.Slots)
        {
            int steps = Mathf.RoundToInt(s.transform.eulerAngles.y / 90f) & 3;
            Vector2 half = (steps & 1) == 1
                ? new Vector2(s.Footprint.y, s.Footprint.x) * 0.5f
                : s.Footprint * 0.5f;
            Vector3 p = s.transform.position;
            FillWorldRect(p.x - half.x, p.z - half.y, p.x + half.x, p.z + half.y);
        }

        // 다리 — MapGeometryV2/Corridors 하위 각 Cor_* 그룹의 렌더러 AABB (축 정렬 통로)
        var geo = GameObject.Find("MapGeometryV2");
        var corridors = geo != null ? geo.transform.Find("Corridors") : null;
        if (corridors != null)
        {
            foreach (Transform cor in corridors)
            {
                var rends = cor.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) continue;
                Bounds b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                FillWorldRect(b.min.x, b.min.z, b.max.x, b.max.z);
            }
        }
        else Debug.LogWarning("[Minimap] MapGeometryV2/Corridors 못 찾음 — 실루엣에 다리 미포함");

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
        var shader = Shader.Find("UI/MinimapComposite");
        _mapMat = new Material(shader);
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
            img.rectTransform.anchoredPosition = WorldToMap(s.transform.position);
            _staticMarkers.Add((img.rectTransform, s.transform.position));
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

    private static readonly float[] SizePresets = { 220f, 300f, 400f };

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
        }

        _maskTimer -= Time.deltaTime;
        if (_maskTimer <= 0f)
        {
            _maskTimer = MaskTick;
            UpdateMask();
        }

        UpdateDynamicMarkers();
    }

    // 플레이어 수집 — NGO 플레이어 오브젝트(IsPlayerObject, 전 클라에서 보임) 우선,
    // 없으면 PlayerMovement(에디터 단독 테스트) 폴백.
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
