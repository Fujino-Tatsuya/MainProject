using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 디버그/오버뷰 맵 UI (레이븐스워치 'Act Spawn Locations' 류).
//  - M 키(토글)로 전체 맵 오버뷰 표시: 존 사각형(역할별 색) + 역할 아이콘(보스/스폰/퀘스트) + 노드 점(티어/내용별)
//  - 열 때마다 현재 생성 데이터(MapGenerator.Results + 씬 SpawnPoint)에서 다시 그림
//    → 서버/클라 모두 같은 시드로 생성하므로 전원 동일 화면.
//  - 노드 클리어 시 실시간 갱신은 클리어 시스템(서버 권한) 붙을 때 RefreshOverview() 호출로 연결 (TODO).
public class MapOverviewUI : MonoBehaviour
{
    [Header("=== 참조 ===")]
    public MapGenerator Generator;

    [Header("=== 표시 설정 ===")]
    [Tooltip("화면 높이 대비 맵 패널 비율")] [Range(0.4f, 0.95f)] public float PanelScreenRatio = 0.8f;
    [Tooltip("알파를 낮추면 뒤의 3D 월드가 비쳐 보임(밝은 벽/바닥이 줄처럼 비침) — 거의 불투명 권장")]
    public Color BackgroundColor = new Color(0f, 0f, 0f, 0.95f);
    public Color CombatZoneColor = new Color(0.45f, 0.55f, 0.65f, 0.85f);
    public Color BossZoneColor = new Color(0.75f, 0.25f, 0.25f, 0.9f);
    public Color SpawnZoneColor = new Color(0.25f, 0.7f, 0.35f, 0.9f);
    public Color QuestZoneColor = new Color(0.85f, 0.75f, 0.25f, 0.9f);
    public Color CorridorColor = new Color(0.55f, 0.62f, 0.7f, 0.9f);
    [Tooltip("역할 아이콘 픽셀 크기")] public float RoleIconSize = 48f;

    private Canvas _canvas;
    private RectTransform _mapArea;

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) Toggle();
#else
        if (Input.GetKeyDown(KeyCode.M)) Toggle();
#endif
    }

    public void Toggle()
    {
        if (_canvas != null) Hide();
        else Show();
    }

    public void Show()
    {
        RefreshOverview();
        if (_canvas != null) _canvas.gameObject.SetActive(true);
    }

    // 숨김 = 캔버스 파괴.
    // Overlay 캔버스는 씬 뷰에 거대한 판으로 그려지는 데다, DontSave 오브젝트는
    // 플레이 종료 후에도 에디트 모드로 누수되므로(씬 뷰 가림/조작 방해) 아예 없앤다.
    // 열 때마다 재생성 — 요소 ~100개라 비용 무시 가능.
    public void Hide()
    {
        DestroyCanvas();
    }

    private void OnDestroy()
    {
        DestroyCanvas(); // 플레이 종료/오브젝트 파괴 시 누수 방지
    }

    private void DestroyCanvas()
    {
        if (_canvas == null) return;
        if (Application.isPlaying) Destroy(_canvas.gameObject);
        else DestroyImmediate(_canvas.gameObject);
        _canvas = null;
        _mapArea = null;
    }

    // 현재 생성 데이터로 오버뷰를 다시 그린다. (클리어 갱신 시에도 이걸 호출)
    public void RefreshOverview()
    {
        EnsureCanvas();

        // 기존 그림 제거 (에디트 모드 호출도 안전하게)
        for (int i = _mapArea.childCount - 1; i >= 0; i--)
        {
            var child = _mapArea.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }

        var volumes = FindObjectsByType<ZoneVolume>(FindObjectsSortMode.None);
        if (volumes.Length == 0) return;

        // 맵 전체 월드 바운즈
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var v in volumes)
        {
            Bounds b = v.GetBounds();
            minX = Mathf.Min(minX, b.min.x); maxX = Mathf.Max(maxX, b.max.x);
            minZ = Mathf.Min(minZ, b.min.z); maxZ = Mathf.Max(maxZ, b.max.z);
        }

        Rect panel = _mapArea.rect;
        float scale = Mathf.Min(panel.width / (maxX - minX), panel.height / (maxZ - minZ));
        Vector2 WorldToMap(float wx, float wz) =>
            new Vector2((wx - (minX + maxX) * 0.5f) * scale, (wz - (minZ + maxZ) * 0.5f) * scale);

        // 1) 존 사각형 (역할별 색)
        foreach (var v in volumes)
        {
            Bounds b = v.GetBounds();
            ZoneRole role = Generator != null ? Generator.GetZoneRole(v.Zone) : ZoneRole.Combat;
            Color c = role switch
            {
                ZoneRole.BossRoom => BossZoneColor,
                ZoneRole.PlayerSpawn => SpawnZoneColor,
                ZoneRole.Quest => QuestZoneColor,
                _ => CombatZoneColor
            };
            var img = MakeImage($"Zone_{(v.Zone != null ? v.Zone.ZoneID : 0)}", c);
            img.rectTransform.anchoredPosition = WorldToMap(b.center.x, b.center.z);
            img.rectTransform.sizeDelta = new Vector2(b.size.x * scale, b.size.z * scale);
        }

        // 2) 통로 (존 연결로 — 실제 지오메트리와 같은 MapCorridors 계산 공유)
        foreach (var c in MapCorridors.FindAll())
        {
            float half = MapCorridors.Width * 0.5f;
            float cx, cz, w, d;
            if (c.AlongX)
            {
                cx = (c.Start + c.End) * 0.5f; cz = c.Center;
                w = c.Length; d = MapCorridors.Width;
            }
            else
            {
                cx = c.Center; cz = (c.Start + c.End) * 0.5f;
                w = MapCorridors.Width; d = c.Length;
            }

            var img = MakeImage($"Corridor_{MapCorridors.ZoneId(c.A)}_{MapCorridors.ZoneId(c.B)}", CorridorColor);
            img.rectTransform.anchoredPosition = WorldToMap(cx, cz);
            // 너무 짧은 통로도 보이게 최소 픽셀 보장
            img.rectTransform.sizeDelta = new Vector2(Mathf.Max(3f, w * scale), Mathf.Max(3f, d * scale));
        }

        // 3) 노드 점 (Results의 SpawnPointID → 씬 SpawnPoint 위치)
        var pointById = new Dictionary<int, SpawnPoint>();
        foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            pointById[sp.PointID] = sp;

        if (Generator != null)
        {
            foreach (var node in Generator.Results)
            {
                if (!pointById.TryGetValue(node.SpawnPointID, out var sp)) continue;

                // 티어별 크기 확연히 차등 (1티어 >> 2티어 >> 3티어)
                float size = node.Tier switch
                {
                    NodeTier.Tier1_Large => 26f,
                    NodeTier.Tier2_Medium => 14f,
                    _ => 8f
                };

                // 색/모양 규칙:
                //  1티어 노드 = 빨강 / 2티어 노드 = 주황 / 장애물 = 검정 (2티어=사각형, 3티어=원형)
                Color c;
                bool circle = false;
                switch (node.Content)
                {
                    case NodeContentType.CombatNode:
                        c = node.Tier == NodeTier.Tier1_Large
                            ? new Color(0.95f, 0.15f, 0.15f)   // 1티어 = 빨강
                            : new Color(1f, 0.55f, 0.15f);     // 2티어 = 주황
                        break;
                    case NodeContentType.Obstacle:
                        c = new Color(0.05f, 0.05f, 0.05f);    // 장애물 = 검정
                        circle = node.Tier == NodeTier.Tier3_Small; // 3티어 장애물만 원형
                        break;
                    case NodeContentType.Recovery: c = new Color(0.3f, 1f, 0.4f); break;   // 회복 = 초록
                    case NodeContentType.Teleport: c = new Color(0.3f, 0.8f, 1f); break;   // 순간이동 = 하늘
                    case NodeContentType.Buff:     c = new Color(0.8f, 0.4f, 1f); break;   // 버프 = 보라
                    default:                       c = Color.white; break;
                }

                var dot = MakeImage($"Node_{node.SpawnPointID}", c);
                if (circle) dot.sprite = GetCircleSprite();
                dot.rectTransform.anchoredPosition = WorldToMap(sp.transform.position.x, sp.transform.position.z);
                dot.rectTransform.sizeDelta = Vector2.one * size;
            }
        }

        // 3) 역할 아이콘 (Resources/MapGen의 Boss/Spawn/Quest 텍스처)
        PlaceRoleIcon(volumes, ZoneRole.BossRoom, "MapGen/Boss", WorldToMap);
        PlaceRoleIcon(volumes, ZoneRole.PlayerSpawn, "MapGen/Spawn", WorldToMap);
        PlaceRoleIcon(volumes, ZoneRole.Quest, "MapGen/Quest", WorldToMap);
    }

    private void PlaceRoleIcon(ZoneVolume[] volumes, ZoneRole role, string resourcePath,
        System.Func<float, float, Vector2> worldToMap)
    {
        if (Generator == null) return;

        foreach (var v in volumes)
        {
            if (v.Zone == null || Generator.GetZoneRole(v.Zone) != role) continue;

            var tex = Resources.Load<Texture2D>(resourcePath);
            if (tex == null) return;

            var img = MakeImage($"Icon_{role}", Color.white);
            img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
            img.rectTransform.anchoredPosition = worldToMap(v.transform.position.x, v.transform.position.z);
            img.rectTransform.sizeDelta = Vector2.one * RoleIconSize;
            return;
        }
    }

    // 원형 점용 스프라이트 (런타임 1회 생성·캐시)
    private static Sprite _circleSprite;

    private static Sprite GetCircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;

        const int s = 32;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        float r = s * 0.5f - 0.5f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x - r, dy = y - r;
                tex.SetPixel(x, y, dx * dx + dy * dy <= r * r ? Color.white : Color.clear);
            }
        tex.Apply();
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, s, s), Vector2.one * 0.5f);
        return _circleSprite;
    }

    private Image MakeImage(string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_mapArea, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private void EnsureCanvas()
    {
        if (_canvas != null) return;

        var canvasGo = new GameObject("MapOverviewCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.hideFlags = HideFlags.DontSave; // 디버그 UI — 씬에 저장 안 함
        canvasGo.transform.SetParent(transform, false);

#if UNITY_EDITOR
        // Overlay 캔버스는 씬 뷰에 픽셀=유닛 크기의 거대한 판으로 그려져 시야를 가림
        // → 씬 뷰에서만 숨김 (게임 뷰 렌더링에는 영향 없음)
        UnityEditor.SceneVisibilityManager.instance.Hide(canvasGo, true);
#endif

        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 999;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // 반투명 배경 (전체 화면)
        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvasGo.transform, false);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
        var bgImg = bg.GetComponent<Image>();
        bgImg.color = BackgroundColor;
        bgImg.raycastTarget = false;

        // 맵 영역 (중앙 정사각 비율)
        var area = new GameObject("MapArea", typeof(RectTransform));
        area.transform.SetParent(canvasGo.transform, false);
        _mapArea = area.GetComponent<RectTransform>();
        float side = 1080f * PanelScreenRatio;
        _mapArea.sizeDelta = new Vector2(side * 1.3f, side); // 맵이 가로로 넓어서 1.3:1
        _mapArea.anchoredPosition = Vector2.zero;

        _canvas.gameObject.SetActive(false);
    }
}
