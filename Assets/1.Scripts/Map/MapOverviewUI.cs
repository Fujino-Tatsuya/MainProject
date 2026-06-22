using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 디버그/오버뷰 맵 UI (M 키 토글).
//  - 존 슬롯 사각형(역할별 색, Footprint 크기) + 역할 아이콘(보스/스폰/퀘스트).
//  - 열 때마다 MapGenerator의 슬롯/배치 상태에서 다시 그림 → 서버/클라 동일 시드라 전원 동일.
//  - 클리어 시 실시간 갱신은 클리어 시스템 붙을 때 RefreshOverview() 호출로 연결 (TODO).
public class MapOverviewUI : MonoBehaviour
{
    [Header("=== 참조 ===")]
    public MapGenerator Generator;

    [Header("=== 표시 설정 ===")]
    [Range(0.4f, 0.95f)] public float PanelScreenRatio = 0.8f;
    public Color BackgroundColor = new Color(0f, 0f, 0f, 0.95f);
    public Color CombatZoneColor = new Color(0.45f, 0.55f, 0.65f, 0.85f);
    public Color BossZoneColor = new Color(0.75f, 0.25f, 0.25f, 0.9f);
    public Color SpawnZoneColor = new Color(0.25f, 0.7f, 0.35f, 0.9f);
    public Color QuestZoneColor = new Color(0.85f, 0.75f, 0.25f, 0.9f);
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

    public void Toggle() { if (_canvas != null) Hide(); else Show(); }
    public void Show() { RefreshOverview(); if (_canvas != null) _canvas.gameObject.SetActive(true); }
    public void Hide() { DestroyCanvas(); }
    private void OnDestroy() { DestroyCanvas(); }

    private void DestroyCanvas()
    {
        if (_canvas == null) return;
        if (Application.isPlaying) Destroy(_canvas.gameObject);
        else DestroyImmediate(_canvas.gameObject);
        _canvas = null;
        _mapArea = null;
    }

    // 현재 생성 상태로 오버뷰를 다시 그린다.
    public void RefreshOverview()
    {
        EnsureCanvas();

        for (int i = _mapArea.childCount - 1; i >= 0; i--)
        {
            var child = _mapArea.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }

        if (Generator == null) return;
        var slots = Generator.Slots;
        if (slots == null || slots.Count == 0) return;

        // 맵 전체 월드 바운즈 (슬롯 위치 + Footprint)
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var s in slots)
        {
            if (s == null) continue;
            Vector3 p = s.transform.position;
            float hx = s.Footprint.x * 0.5f, hz = s.Footprint.y * 0.5f;
            minX = Mathf.Min(minX, p.x - hx); maxX = Mathf.Max(maxX, p.x + hx);
            minZ = Mathf.Min(minZ, p.z - hz); maxZ = Mathf.Max(maxZ, p.z + hz);
        }
        if (maxX <= minX || maxZ <= minZ) return;

        Rect panel = _mapArea.rect;
        float scale = Mathf.Min(panel.width / (maxX - minX), panel.height / (maxZ - minZ));
        Vector2 WorldToMap(float wx, float wz) =>
            new Vector2((wx - (minX + maxX) * 0.5f) * scale, (wz - (minZ + maxZ) * 0.5f) * scale);

        // 존 슬롯 사각형 (역할별 색)
        foreach (var s in slots)
        {
            if (s == null) continue;
            Color c = s.AssignedRole switch
            {
                ZoneRole.BossRoom => BossZoneColor,
                ZoneRole.PlayerSpawn => SpawnZoneColor,
                ZoneRole.Quest => QuestZoneColor,
                _ => CombatZoneColor
            };
            var img = MakeImage($"Zone_{s.SlotID}", c);
            Vector3 p = s.transform.position;
            img.rectTransform.anchoredPosition = WorldToMap(p.x, p.z);
            img.rectTransform.sizeDelta = new Vector2(s.Footprint.x * scale, s.Footprint.y * scale);
        }

        // 역할 아이콘 (카탈로그 직렬화 참조 — 빌드에서도 동작)
        var cat = Generator.Catalog;
        PlaceRoleIcon(ZoneRole.BossRoom, cat != null ? cat.BossIcon : null, WorldToMap);
        PlaceRoleIcon(ZoneRole.PlayerSpawn, cat != null ? cat.SpawnIcon : null, WorldToMap);
        PlaceRoleIcon(ZoneRole.Quest, cat != null ? cat.QuestIcon : null, WorldToMap);
    }

    private void PlaceRoleIcon(ZoneRole role, Texture2D tex, System.Func<float, float, Vector2> worldToMap)
    {
        if (Generator == null || tex == null) return;
        var slot = Generator.GetRoleSlot(role);
        if (slot == null) return;

        var img = MakeImage($"Icon_{role}", Color.white);
        img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
        Vector3 p = slot.transform.position;
        img.rectTransform.anchoredPosition = worldToMap(p.x, p.z);
        img.rectTransform.sizeDelta = Vector2.one * RoleIconSize;
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
        UnityEditor.SceneVisibilityManager.instance.Hide(canvasGo, true);
#endif

        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 999;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvasGo.transform, false);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
        var bgImg = bg.GetComponent<Image>();
        bgImg.color = BackgroundColor;
        bgImg.raycastTarget = false;

        var area = new GameObject("MapArea", typeof(RectTransform));
        area.transform.SetParent(canvasGo.transform, false);
        _mapArea = area.GetComponent<RectTransform>();
        float side = 1080f * PanelScreenRatio;
        _mapArea.sizeDelta = new Vector2(side * 1.3f, side);
        _mapArea.anchoredPosition = Vector2.zero;

        _canvas.gameObject.SetActive(false);
    }
}
