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
    [Tooltip("미니맵 한 변 픽셀 크기")] public float PanelSize = 350f;
    [Tooltip("화면 우하단 여백")] public Vector2 Margin = new Vector2(1f, 1f);
    public float RoleIconSize = 22f;
    public float NodeDotSize = 9f;
    public float UnitDotSize = 11f;

    [Header("=== 시야/탐사 ===")]
    [Tooltip("현재 시야 반경(m) — 팀원 전원 합산")] public float SightRadius = 15f;
    [Tooltip("탐사 마스크 해상도")] public int MaskResolution = 128;
    [Tooltip("마스크 갱신 주기(초)")] public float MaskTick = 0.35f;

    [Header("=== 룩 (PLAN-minimap 2026-09-20) ===")]
    [Tooltip("지형 사진을 굽는다. 끄면(기본) 균일 회색 플랫 채움 — minimap.png 룩. " +
             "베이크는 밝기 0 재시도·어비스 물 plane 같은 함정이 있어 기본은 꺼 둔다.")]
    public bool UseTerrainBake = false;

    [Tooltip("미니맵 회전(도). 카메라 요각과 맞춘다 — 레퍼런스의 마름모가 이 회전의 결과다. " +
             "월드 슬롯은 축 정렬이라 0 이면 반듯한 사각형으로 보인다.")]
    public float MapRotationDegrees = 315;

    [Tooltip("방 모서리 반경(m). 통로는 항상 각지게 둔다.")]
    public float RoomCornerMeters = 2.5f;

    public Color FlatColor = new Color(0.58f, 0.58f, 0.58f, 1f);
    public Color OutlineColor = new Color(0.13f, 0.15f, 0.17f, 1f);

    [Tooltip("미탐사 구역 밝기(0=검정, 1=탐사와 동일). 🔴 0 이면 안 보인다 — 맵 전체 모양이 " +
             "어두운 회색으로 늘 깔려 있고, 탐사하면 밝아지는 게 레퍼런스 룩이다.")]
    [Range(0f, 1f)] public float UnexploredDim = 0.34f;

    [Tooltip("탐사했지만 지금 시야 밖인 구역의 밝기.")]
    [Range(0f, 1f)] public float ExploredDim = 0.62f;

    [Tooltip("역할 아이콘(퀘스트·스폰·보스입구)을 맵과 함께 회전시킨다. " +
             "아트가 직사각형이라 정립시키면 마름모 미니맵 위에서 축이 어긋나 보인다(팀장 확정 2026-09-20). " +
             "아이콘 아트가 회전 무관한 모양으로 바뀌면 끄면 된다.")]
    public bool RotateRoleIconsWithMap = true;

    [Tooltip("CombatHUD 안의 미니맵 슬롯 이름. 찾으면 그 밑에 붙고, 못 찾으면 자체 Canvas 로 폴백한다.")]
    public string MinimapSlotName = "MinimapSlot";

    [Header("=== 베이크 ===")]
    [Tooltip("지형 베이크 해상도")] public int BakeResolution = 1024;
    [Tooltip("맵 경계 여유(m)")] public float BoundsMargin = 8f;
    [Tooltip("베이크 카메라 높이(m). 이 높이에서 아래를 내려다본다.")]
    public float BakeCameraHeight = 120f;
    [Tooltip("베이크에 포함할 최저 월드 Y. 이보다 아래 지오메트리는 잘라낸다 — " +
             "어비스 물 Plane(y≈-19, 3300m)이 미니맵을 통째로 덮는 것을 막는다.")]
    public float BakeMinWorldY = -5f;
    [Tooltip("실루엣에 통로로 그릴 최대 한 변(m). 이보다 큰 렌더러 묶음은 통로가 아니라 " +
             "배경/전체 메시로 보고 제외한다 — 실루엣이 통짜 사각형으로 채워지는 것을 막는다.")]
    public float CorridorMaxSpan = 60f;

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
    private RectTransform _mapRoot;    // 회전 없는 컨테이너 (슬롯/화면에 고정)
    private RectTransform _mapRect;    // 마커 부모 (RawImage 위). 회전은 여기에 걸린다
    private Material _mapMat;
    private Sprite _dotSprite;

    // 마커
    // upright = true 면 맵 회전을 역보정해 아이콘을 세운다. false 면 맵과 함께 돈다.
    private readonly List<(RectTransform rt, Vector3 world, bool upright)> _staticMarkers =
        new List<(RectTransform, Vector3, bool)>();
    private readonly Dictionary<Component, Image> _dynMarkers = new Dictionary<Component, Image>();
    private readonly List<Component> _dynRemove = new List<Component>();
    private readonly List<Transform> _players = new List<Transform>(); // TODO: NetworkManager.ConnectedClients 기반으로 주기적 캐싱 구현 필요
    private float _playerScanTimer;
    private int _lastPlayerCount = -1;
    private Transform _corridorsRoot;

    [SerializeField] private Material MinimapComsite;

    private void Awake()
    {
        // 맵 구조물(복도/다리 등)을 미리 캐싱하여 매 업데이트마다 찾는 비용 방지
        var mapGeom = GameObject.Find("Stage1/Level_wall_hallway");
        if (mapGeom != null) _corridorsRoot = mapGeom.transform;

        _explored = new byte[MaskResolution * MaskResolution];
    }

    private void OnEnable() => MapGenerator.OnGenerated += HandleGenerated;
    private void OnDisable() => MapGenerator.OnGenerated -= HandleGenerated;

    private void HandleGenerated(MapGenerator gen)
    {
        Generator = gen;
        ComputeWorldRect(gen);

        // 플랫 채움 룩에서는 지형 사진이 필요 없다 — 카메라 생성·렌더·재시도를 통째로 건너뛴다.
        if (UseTerrainBake) BakeTerrain();

        EnsureUI();
        BuildSilhouette(gen);
        ResetMask();
        BuildStaticMarkers(gen);
        _baked = true;

        // 클라에서 씬 로드 직후 렌더 요청이 빈 결과를 줄 수 있음 — 밝기 0이면 재시도
        if (UseTerrainBake && _lastBakeLuminance < 0.01f) StartCoroutine(RetryBake());
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
        int mask = ~0;
        foreach (string ln in new[] { "UI", "Player", "Monster", "Unit", "Water", "Soul", "Corpse", "Projectile" })
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

        // 코너를 둥글게 깎아 채운다(PLAN-minimap D3). 반경 0 이면 기존 사각형과 같다.
        void FillWorldRoundedRect(float minX, float minZ, float maxX, float maxZ, float cornerMeters)
        {
            if (cornerMeters <= 0f) { FillWorldRect(minX, minZ, maxX, maxZ); return; }

            float pxPerMeter = res / _worldRect.width;
            float r = cornerMeters * pxPerMeter;

            float fx0 = (minX - _worldRect.xMin) * pxPerMeter;
            float fx1 = (maxX - _worldRect.xMin) * pxPerMeter;
            float fy0 = (minZ - _worldRect.yMin) * pxPerMeter;
            float fy1 = (maxZ - _worldRect.yMin) * pxPerMeter;

            // 반경이 변의 절반을 넘으면 모양이 뭉개진다 — 짧은 변에 맞춰 줄인다.
            r = Mathf.Min(r, (fx1 - fx0) * 0.5f, (fy1 - fy0) * 0.5f);
            if (r <= 0.5f) { FillWorldRect(minX, minZ, maxX, maxZ); return; }

            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx0), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(fx1), 0, res - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy0), 0, res - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(fy1), 0, res - 1);

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                // 모서리 안쪽으로 r 만큼 들어간 사각형까지의 거리로 판정한다.
                float cx = Mathf.Clamp(x + 0.5f, fx0 + r, fx1 - r);
                float cy = Mathf.Clamp(y + 0.5f, fy0 + r, fy1 - r);
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy <= r * r) px[y * res + x] = 255;
            }
        }

        // 존 풋프린트 (회전 반영)
        foreach (var s in gen.Slots)
        {
            int steps = Mathf.RoundToInt(s.transform.eulerAngles.y / 90f) & 3;
            Vector2 half = (steps & 1) == 1
                ? new Vector2(s.Footprint.y, s.Footprint.x) * 0.5f
                : s.Footprint * 0.5f;
            Vector3 p = s.transform.position;
            FillWorldRoundedRect(p.x - half.x, p.z - half.y, p.x + half.x, p.z + half.y, RoomCornerMeters);
        }

        // 5) 복도/방 연결부 (보통 얇고 긴 메쉬)
        Transform corridors = _corridorsRoot;
        if (corridors != null)
        {
            foreach (Transform cor in corridors)
            {
                var rends = cor.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) continue;
                Bounds b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);

                // ⚠️ 이 루트의 자식이 항상 "통로 한 조각"은 아니다. 맵 전체를 덮는 묶음이 하나라도
                // 있으면 실루엣이 통째로 채워져 미탐사 색(_SilColor)이 미니맵 전체를 덮는다 —
                // 실제로 그래서 미니맵이 거대한 파란 사각형으로 보였다. (M키 지도도 같은 원인)
                if (b.size.x > CorridorMaxSpan || b.size.z > CorridorMaxSpan)
                    continue;

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

        // 컨테이너(회전 없음) — 화면 우하단에 고정된다.
        var rootGo = new GameObject("MinimapRoot", typeof(RectTransform));
        rootGo.transform.SetParent(canvasGo.transform, false);
        _mapRoot = rootGo.GetComponent<RectTransform>();
        _mapRoot.anchorMin = _mapRoot.anchorMax = new Vector2(1f, 0f); // 우하단
        _mapRoot.pivot = new Vector2(1f, 0f);
        _mapRoot.anchoredPosition = new Vector2(-Margin.x, Margin.y);

        // 🔴 회전은 **pivot 이 중앙인 안쪽 컨텐츠**에 건다.
        //    컨테이너(pivot 우하단)를 직접 돌리면 화면 우하단 모서리를 축으로 돌아
        //    한 변 L 에 대해 L/√2 만큼(350 이면 약 246 UI 단위) 화면 밖으로 빠진다.
        var mapGo = new GameObject("Minimap", typeof(RawImage));
        mapGo.transform.SetParent(rootGo.transform, false);
        var raw = mapGo.GetComponent<RawImage>();

        _mapMat = new Material(MinimapComsite);
        _mapMat.SetTexture("_MainTex", _bakeRT);
        raw.texture = _bakeRT != null ? (Texture)_bakeRT : Texture2D.whiteTexture;
        raw.material = _mapMat;
        raw.raycastTarget = false;

        // 🔴 머티리얼에 예전 값(_BgAlpha 0.35)이 저장돼 있어 셰이더 기본값만으로는 안 먹는다.
        //    맵 모양 바깥은 완전 투명이어야 한다 — 회전하면 패널 사각형이 그대로 드러나기 때문.
        ApplyLookToMaterial();

        _mapRect = mapGo.GetComponent<RectTransform>();
        _mapRect.anchorMin = _mapRect.anchorMax = new Vector2(0.5f, 0.5f);
        _mapRect.pivot = new Vector2(0.5f, 0.5f);
        _mapRect.anchoredPosition = Vector2.zero;

        _dotSprite = MakeCircleSprite(32);
        if (_maskTex != null) _mapMat.SetTexture("_MaskTex", _maskTex);

        ApplyPanelSize(PanelSize);

        // 플레이어 HUD 안의 슬롯에 붙을 수 있으면 붙는다(없으면 이 자체 Canvas 로 남는다).
        Player.LocalPlayerChanged += HandleLocalPlayerChanged;
        TryAttachToSlot(Player.LocalPlayer);

        // 네트워크 세션 중이면 플레이어 스폰 전(로딩 화면)엔 숨김 — Update가 상태 갱신
        var nm = Unity.Netcode.NetworkManager.Singleton;
        _canvas.enabled = !(nm != null && nm.IsListening);
    }

    /// <summary>룩 파라미터를 머티리얼 인스턴스에 적용한다. 인스펙터에서 바꾸면 바로 반영된다.</summary>
    private void ApplyLookToMaterial()
    {
        if (_mapMat == null) return;
        _mapMat.SetColor("_FlatColor", FlatColor);
        _mapMat.SetColor("_OutlineColor", OutlineColor);
        _mapMat.SetFloat("_UnexploredDim", UnexploredDim);
        _mapMat.SetFloat("_DimExplored", ExploredDim);
        _mapMat.SetFloat("_BgAlpha", 0f);
    }

    // ---------------- CombatHUD 슬롯 부착 ----------------
    //
    // 🔴 CombatHUD 는 씬이 아니라 **Player.prefab 의 자식**이다. 그래서 슬롯은 로컬 플레이어가
    //    스폰된 뒤에야 생기고, 디스폰하면 **붙여 둔 UI 가 플레이어와 함께 파괴된다.**
    //    베이크·마스크·실루엣은 이 씬 상주 컴포넌트가 계속 들고 있으므로 UI 만 다시 붙이면 된다.
    //
    // ⚠️ 전역 검색(FindObjectsByType)으로 슬롯을 찾으면 **원격 플레이어의 HUD** 를 잡을 수 있다
    //    (원격 HUD 는 Player.OnNetworkSpawn 에서 꺼지지만 그 전 한 프레임이 있다).
    //    그래서 반드시 LocalPlayerChanged 가 준 로컬 Player 밑에서만 찾는다.
    private RectTransform _slot;

    private void HandleLocalPlayerChanged(Player player) => TryAttachToSlot(player);

    private void TryAttachToSlot(Player player)
    {
        if (_mapRoot == null) return;

        RectTransform slot = FindSlot(player);

        if (slot == null)
        {
            // 슬롯이 사라졌다(디스폰 등) — 자체 Canvas 로 되돌린다.
            if (_slot != null && _canvas != null)
            {
                _mapRoot.SetParent(_canvas.transform, false);
                _mapRoot.anchorMin = _mapRoot.anchorMax = new Vector2(1f, 0f);
                _mapRoot.pivot = new Vector2(1f, 0f);
                _mapRoot.anchoredPosition = new Vector2(-Margin.x, Margin.y);
                _slot = null;

                // 🔴 부착할 때 stretch(offset 0)로 만들면서 sizeDelta 가 0 이 됐다.
                //    되돌릴 때 외접 크기를 다시 계산하지 않으면 미니맵 중심이 화면 우하단
                //    모서리에 놓여 통째로 잘린다. (_slot 을 먼저 null 로 둬야 아래가 크기를 쓴다.)
                ApplyPanelSize(PanelSize);
            }
            return;
        }

        if (_slot == slot) return;

        _slot = slot;
        _mapRoot.SetParent(slot, false);

        // 위치·크기는 슬롯이 정한다 — 디자이너가 프리팹에서 조정할 수 있어야 한다.
        _mapRoot.anchorMin = Vector2.zero;
        _mapRoot.anchorMax = Vector2.one;
        _mapRoot.pivot = new Vector2(0.5f, 0.5f);
        _mapRoot.offsetMin = Vector2.zero;
        _mapRoot.offsetMax = Vector2.zero;
        _mapRoot.anchoredPosition = Vector2.zero;

        // 🔴 표시 권한은 HUD 정책(PlayerCombatUiLifecyclePolicy)에 남긴다 — 우리 Canvas 는 비운다.
        if (_canvas != null) _canvas.enabled = false;

        // 크기의 주인은 슬롯이다 — 디자이너가 프리팹에서 슬롯을 키우면 미니맵도 따라 커진다.
        // 회전한 정사각형이 슬롯 안에 들어가야 하므로 외접 계수로 나눈다(45° → √2).
        float rad = MapRotationDegrees * Mathf.Deg2Rad;
        float ratio = Mathf.Abs(Mathf.Cos(rad)) + Mathf.Abs(Mathf.Sin(rad));
        float slotSide = Mathf.Min(slot.rect.width, slot.rect.height);
        if (slotSide > 1f && ratio > 0.01f)
            ApplyPanelSize(slotSide / ratio);

        Debug.Log($"[Minimap] CombatHUD 슬롯 '{MinimapSlotName}' 에 부착했다.");
    }

    private RectTransform FindSlot(Player player)
    {
        if (player == null || string.IsNullOrEmpty(MinimapSlotName)) return null;

        var hud = player.GetComponentInChildren<CombatHUD>(true);
        if (hud == null) return null;

        foreach (var rt in hud.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == MinimapSlotName) return rt;

        return null;
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

    private Image MakeMarkerImage(string name, Sprite sprite, Color color, float size, bool upright = true)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(_mapRect, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(1f, 0f);
        img.rectTransform.sizeDelta = Vector2.one * size;
        // 부모(_mapRect)가 회전한다. 정립시킬 아이콘만 되돌린다(D10·D14).
        img.rectTransform.localRotation = upright
            ? Quaternion.Euler(0f, 0f, -MapRotationDegrees)
            : Quaternion.identity;
        return img;
    }

    // ---------------- 상시 마커 (생성 시 1회) ----------------

    private void BuildStaticMarkers(MapGenerator gen)
    {
        foreach (var (rt, _, _) in _staticMarkers) if (rt != null) Destroy(rt.gameObject);
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
            bool upright = !RotateRoleIconsWithMap;
            var img = MakeMarkerImage($"Role_{label}", null, Color.white, RoleIconSize, upright);
            if (tex != null)
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
            else { img.sprite = _dotSprite; img.color = label == "Boss" ? MonsterColor : Color.yellow; }
            img.rectTransform.anchoredPosition = WorldToMap(s.transform.position);
            _staticMarkers.Add((img.rectTransform, s.transform.position, upright));
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
            _staticMarkers.Add((img.rectTransform, node.transform.position, true));
        }
    }

    // ---------------- 크기 조절 ([ = 축소, ] = 확대) ----------------

    private static readonly float[] SizePresets = { 300f, 400f, 520f };

    private void ApplyPanelSize(float size)
    {
        PanelSize = size;
        if (_mapRect == null) return;

        _mapRect.sizeDelta = Vector2.one * PanelSize;
        _mapRect.localRotation = Quaternion.Euler(0f, 0f, MapRotationDegrees);

        // 🔴 회전한 정사각형의 외접 영역을 컨테이너가 예약해야 화면 밖으로 안 빠진다.
        //    45° 면 한 변의 √2 배다(350 → 495).
        if (_mapRoot != null && _slot == null)
        {
            float rad = MapRotationDegrees * Mathf.Deg2Rad;
            float extent = PanelSize * (Mathf.Abs(Mathf.Cos(rad)) + Mathf.Abs(Mathf.Sin(rad)));
            _mapRoot.sizeDelta = Vector2.one * extent;
        }

        // 아이콘은 정립 유지 — 같이 기울면 해골·퀘스트 아이콘이 비뚤어져 읽힌다.
        Quaternion uprightRot = Quaternion.Euler(0f, 0f, -MapRotationDegrees);
        foreach (var (rt, world, upright) in _staticMarkers)
            if (rt != null)
            {
                rt.anchoredPosition = WorldToMap(world);
                rt.localRotation = upright ? uprightRot : Quaternion.identity;
            }
        foreach (var kv in _dynMarkers)
            if (kv.Value != null) kv.Value.rectTransform.localRotation = uprightRot;
    }

    private void HandleSizeInput()
    {
        // 🔴 슬롯에 붙어 있으면 크기의 주인은 슬롯이다. 여기서 프리셋(300/400/520)으로 바꾸면
        //    회전 외접이 슬롯을 넘어 **화면 밖으로 잘려 나간다**(400 → 외접 566).
        if (_slot != null) return;

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

            // 플레이 중에 인스펙터로 룩을 맞출 수 있게 주기적으로 다시 밀어 넣는다(초당 1회, 비용 무시).
            ApplyLookToMaterial();
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
        // 🔴 CombatHUD 슬롯에 붙었으면 표시 권한은 그쪽 정책(PlayerCombatUiLifecyclePolicy)의 것이다.
        //    여기서 매초 켜면 **사망으로 숨긴 HUD 전체를 되살린다.**
        if (_slot != null) return;

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
        Player.LocalPlayerChanged -= HandleLocalPlayerChanged;

        // 런타임에 만든 것들은 씬을 떠나도 자동으로 사라지지 않는다 — 명시적으로 버린다.
        if (_bakeRT != null) { _bakeRT.Release(); Destroy(_bakeRT); _bakeRT = null; }
        if (_mapMat != null) { Destroy(_mapMat); _mapMat = null; }
        if (_maskTex != null) { Destroy(_maskTex); _maskTex = null; }
        if (_silTex != null) { Destroy(_silTex); _silTex = null; }

        if (_dotSprite != null)
        {
            // Sprite.Create 로 만든 것은 스프라이트와 원본 텍스처가 따로 남는다.
            if (_dotSprite.texture != null) Destroy(_dotSprite.texture);
            Destroy(_dotSprite);
            _dotSprite = null;
        }
    }
}
