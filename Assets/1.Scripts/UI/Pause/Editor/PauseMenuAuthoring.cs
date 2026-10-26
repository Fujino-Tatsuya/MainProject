using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 인게임 ESC 일시정지 메뉴 프리팹(PLAN-pause-menu) — SVN r466 UI_Panel 의 03_PauseMenu · 02_ExitConfirm 배치를 그대로 옮긴다. <b>멱등</b>.
/// 1) <c>Assets/2.Prefabs/UI/PauseMenu.prefab</c> 을 새로 만든다(매번 처음부터 — 손으로 고칠 곳 없음, 고칠 건 이 파일).
/// 2) (10-22 폐지) 맵 OptionPanel 배치 — PauseMenu 는 이제 GameMenu.prefab(DDOL) 안에 중첩된다(PLAN-settings-ddol, GameMenuAuthoring).
/// 좌표 = 매니페스트 rect(1920×1080, 왼쪽 위 원점) → 화면 중심(960, 540) 원점. 이미지는 rect + 투명 여백 4.
/// </summary>
public static class PauseMenuAuthoring
{
    const string PrefabPath = "Assets/2.Prefabs/UI/PauseMenu.prefab";
    const string MapScenePath = "Assets/0.Scenes/MainFlow/4.MapScene.unity";
    static readonly Vector2 Origin = new Vector2(960f, 540f);
    const float Pad = SettingsArtSkin.Pad;

    // 매니페스트 guid(r466)
    const string PanelPause = "30c30c41de704512bf80d49e8d7cedf8";
    const string PanelExit = "33b07217189d4660b9e0d4f16cb46dd8";
    const string PillNeutral = "28aaba80a0054613b29a08d4926849c1";
    const string PillRed = "ba9c4ad2c9534c66b0b003202a73d7dd";
    const string PillGreen = "19cc3a31295a455aa749eb5fc3fe90e5";
    const string CrossRed = "a65910e5aa9e4da58df47bb89e41bc21";
    const string CheckGreen = "0efa0febe77f46ac98aa65c171b6ecd9";

    /// <summary>ESC 동안 게임 화면을 덮는 반투명 검정(경석 10-20 — "검은 화면에 알파 절반").</summary>
    internal static readonly Color DimColor = new Color(0f, 0f, 0f, 0.5f);
    /// <summary>ESC 패널 캔버스 정렬 — CombatHUD 0 · 튜토리얼 50~60 · 맵 전체 보기 999 위, 드롭다운 목록 30000·성능 표시 아래.</summary>
    const int OptionPanelSortingOrder = 1000;

    // 누를 수 있는 버튼 글자 = 순백 — 설정창 탭(SettingsBodyView)과 같은 규칙(경석 10-26). 목업 원래 색은 연회색(C25 M20 Y20).
    static readonly Color ButtonText = Color.white;
    static TMP_FontAsset _font;
    static int _uiLayer;

    [MenuItem("Tools/UI/Pause/일시정지 메뉴 프리팹 만들기 (멱등)")]
    static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[PauseMenu] Play 중에는 실행하지 않는다."); return; }
        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SettingsArtSkin.FontAssetPath);
        if (_font == null) Debug.LogWarning("[PauseMenu] 서울알림체 에셋이 없다 — 기본 글꼴로 만든다(SVN r476 확인).");
        _uiLayer = LayerMask.NameToLayer("UI");

        GameObject root = Build();
        if (root == null) return;
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"[PauseMenu] {PrefabPath} 저장");
        // 맵 OptionPanel 배치(ApplyToMap)는 하지 않는다 — PauseMenu 는 GameMenu.prefab(DDOL) 안에 중첩된다(PLAN-settings-ddol).
    }

    // ─── 프리팹 ─────────────────────────────────────────────────────

    static GameObject Build()
    {
        Sprite panelPause = Load(PanelPause), panelExit = Load(PanelExit), pill = Load(PillNeutral),
               red = Load(PillRed), green = Load(PillGreen), cross = Load(CrossRed), check = Load(CheckGreen);
        if (panelPause == null || panelExit == null || pill == null || red == null || green == null || cross == null || check == null)
        {
            Debug.LogError("[PauseMenu] UI_Panel 스프라이트가 없다 — SVN r466 이상을 받았는지 확인.");
            return null;
        }

        var root = new GameObject("PauseMenu", typeof(RectTransform));
        root.layer = _uiLayer;
        var rr = (RectTransform)root.transform;
        rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one; rr.offsetMin = rr.offsetMax = Vector2.zero;
        var view = root.AddComponent<PauseMenuView>();

        // 메뉴(03_PauseMenu)
        RectTransform menu = Group(rr, "Menu");
        Img(menu, "Panel", panelPause, new Rect(783.1f, 161.8f, 364.4f, 696.3f), simple: true).raycastTarget = true;
        Label(menu, "Title", UiStrings.PauseTitle, new Rect(860f, 209.5f, 210f, 74.5f), 70f, Color.white);
        Button cont = PillButton(menu, "Continue", pill, new Rect(827.9f, 336.7f, 274.8f, 133f), UiStrings.PauseContinue, 54f, ButtonText);
        Button settings = PillButton(menu, "Settings", pill, new Rect(827.9f, 501.5f, 274.8f, 133f), UiStrings.PauseSettings, 54f, ButtonText);
        Button quit = PillButton(menu, "Quit", pill, new Rect(827.9f, 666.2f, 274.8f, 133f), UiStrings.PauseQuit, 54f, ButtonText);
        // 목업 아래 "Press Any Button" 캡슐은 ESC 메뉴에서 빼고 타이틀로 옮겼다(경석 10-26 — PLAN-title-start-settings-tabs).

        // 종료 확인(02_ExitConfirm)
        RectTransform exit = Group(rr, "ExitConfirm");
        Img(exit, "Panel", panelExit, new Rect(544.2f, 223.0f, 842.3f, 574.4f), simple: true).raycastTarget = true;
        TMP_Text title = Label(exit, "Title", UiStrings.ExitTitle, new Rect(632f, 265.7f, 560f, 68.9f), 65f, Color.white);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        Label(exit, "Message", UiStrings.ExitMessage, new Rect(615.4f, 422.6f, 700f, 76.3f), 60f, Color.black);
        Sprite ring = EnsureRingSprite();
        Button desktop = PillButton(exit, "Desktop", pill, new Rect(604.2f, 628.7f, 274.8f, 133f), UiStrings.ExitDesktop, 50f, ButtonText);
        GameObject desktopRing = Ring(desktop, ring);
        Button lobby = PillButton(exit, "Lobby", pill, new Rect(1060.2f, 628.7f, 274.8f, 133f), UiStrings.ExitLobby, 54f, ButtonText);
        GameObject lobbyRing = Ring(lobby, ring);
        Button cancel = IconButton(exit, "Cancel", red, new Rect(547.3f, 850f, 274.8f, 133f), cross, new Rect(655.7f, 887.6f, 57.9f, 57.9f));
        Button confirm = IconButton(exit, "Confirm", green, new Rect(1099.9f, 860f, 274.8f, 133f), check, new Rect(1199.4f, 897.6f, 83.9f, 57.9f));
        exit.gameObject.SetActive(false);

        var so = new SerializedObject(view);
        so.FindProperty("menuRoot").objectReferenceValue = menu.gameObject;
        so.FindProperty("continueButton").objectReferenceValue = cont;
        so.FindProperty("settingsButton").objectReferenceValue = settings;
        so.FindProperty("quitButton").objectReferenceValue = quit;
        so.FindProperty("exitRoot").objectReferenceValue = exit.gameObject;
        so.FindProperty("desktopButton").objectReferenceValue = desktop;
        so.FindProperty("lobbyButton").objectReferenceValue = lobby;
        so.FindProperty("cancelButton").objectReferenceValue = cancel;
        so.FindProperty("confirmButton").objectReferenceValue = confirm;
        so.FindProperty("desktopSelectedRing").objectReferenceValue = desktopRing;
        so.FindProperty("lobbySelectedRing").objectReferenceValue = lobbyRing;
        so.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    static RectTransform Group(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = _uiLayer;
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(1920f, 1080f);
        r.anchoredPosition = Vector2.zero;
        return r;
    }

    static Image Img(RectTransform parent, string name, Sprite sprite, Rect mock, bool simple)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = _uiLayer;
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        Place(r, mock);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        if (simple)
        {
            img.sprite = sprite;
            r.sizeDelta = SettingsArtSkin.NativeSize(sprite);
        }
        else SettingsArtSkin.SetSliced(img, sprite, r.sizeDelta);
        return img;
    }

    static Button PillButton(RectTransform parent, string name, Sprite pill, Rect mock, UiString text, float size, Color color)
    {
        Image img = Img(parent, name, pill, mock, simple: false);
        img.raycastTarget = true;
        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        Tint(b);
        TMP_Text label = Label(img.rectTransform, "Text", text, Rect.zero, size, color);
        SettingsArtSkin.Stretch(label.rectTransform, new Vector2(24f, 0f), new Vector2(-24f, 0f));
        var state = img.gameObject.AddComponent<ButtonLabelStateColor>();
        var sso = new SerializedObject(state);
        sso.FindProperty("label").objectReferenceValue = label;
        sso.ApplyModifiedPropertiesWithoutUndo();
        return b;
    }

    static Button IconButton(RectTransform parent, string name, Sprite pill, Rect mock, Sprite icon, Rect iconMock)
    {
        Image img = Img(parent, name, pill, mock, simple: false);
        img.raycastTarget = true;
        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        Tint(b);
        Image ic = Img(img.rectTransform, "Icon", icon, iconMock, simple: true);
        // 부모 기준으로 다시 — Place 는 화면 원점 기준이라 부모 중심을 뺀다.
        ic.rectTransform.anchoredPosition = Center(iconMock) - Center(mock);
        return b;
    }

    // 고른 쪽 테두리(경석 10-20 "흰 글자 + 테두리") — 알약 바깥 둘레를 따라 흰 선을 버튼 위에 얹는다.
    // 🔴 알약 그림을 흰색으로 "칠해" 뒤에 까는 방식은 안 된다 — UI 색은 곱하기라 어두운 그림이 흰색이 되지 않는다(10-20 캡처로 확인).
    static GameObject Ring(Button button, Sprite ring)
    {
        var go = new GameObject("SelectRing", typeof(RectTransform), typeof(Image));
        go.layer = _uiLayer;
        var r = (RectTransform)go.transform;
        r.SetParent(button.transform, false);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = ring;
        img.raycastTarget = false;
        go.SetActive(false);
        return go;
    }

    const string RingPath = "Assets/2.Prefabs/UI/Textures/PauseSelectRing.png";

    /// <summary>
    /// 흰 캡슐 테두리 PNG(생성물 · git). 크기 = 알약 버튼 이미지(274.8×133 + 여백 4)의 2배 해상도 — 아트와 같은 PPU 200.
    /// 바깥 둘레에서 안쪽으로 선 폭 5(논리) 만큼, 가장자리 1px 부드럽게.
    /// </summary>
    static Sprite EnsureRingSprite()
    {
        const int W = 558, H = 274;            // (274.8+4)×2, (133+4)×2
        const float PadPx = Pad * 2f, Stroke = 10f;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        float radius = (H - PadPx * 2f) * 0.5f;
        float cy = H * 0.5f, x0 = PadPx + radius, x1 = W - PadPx - radius;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            float dx = fx < x0 ? fx - x0 : fx > x1 ? fx - x1 : 0f;
            float d = Mathf.Sqrt(dx * dx + (fy - cy) * (fy - cy));      // 캡슐 중심선까지
            float outer = Mathf.Clamp01(radius - d + 0.5f);            // 바깥 경계 안쪽
            float inner = Mathf.Clamp01(d - (radius - Stroke) + 0.5f); // 안쪽 경계 바깥
            byte a = (byte)Mathf.RoundToInt(255f * Mathf.Min(outer, inner));
            px[y * W + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px);
        tex.Apply();
        System.IO.File.WriteAllBytes(RingPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(RingPath, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(RingPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 200f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(RingPath);
    }

    static TMP_Text Label(RectTransform parent, string name, UiString text, Rect mock, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = _uiLayer;
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        if (mock != Rect.zero) Place(r, mock);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text.Ko;
        if (_font != null) { t.font = _font; t.fontSharedMaterial = _font.material; }
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        SettingsArtSkin.AutoSize(t, 16f, size);
        go.AddComponent<LocalizedText>().Key = text.Key;
        return t;
    }

    internal static readonly Color ChosenTint = ButtonLabelStateColor.ChosenTint;   // 공통 반응 규칙(UiFeedbackStyle)과 같은 값

    static void Tint(Selectable s)
    {
        ColorBlock c = s.colors;
        c.normalColor = Color.white;
        // 고름(마우스 올림·키보드 선택) = 설정 탭 선택과 같은 0.55 로 어둡게 — 글자는 ButtonLabelStateColor(경석 10-26).
        c.highlightedColor = ChosenTint;
        c.selectedColor = ChosenTint;
        c.pressedColor = new Color(0.45f, 0.45f, 0.45f, 1f);
        c.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        c.colorMultiplier = 1f;
        c.fadeDuration = 0.08f;
        s.colors = c;
    }

    static Vector2 Center(Rect mock) => new Vector2(mock.center.x - Origin.x, Origin.y - mock.center.y);

    static void Place(RectTransform r, Rect mock)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = Center(mock);
        r.sizeDelta = new Vector2(mock.width + Pad * 2, mock.height + Pad * 2);
    }

    static Sprite Load(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ─── 맵 씬 ──────────────────────────────────────────────────────

    static void ApplyToMap()
    {
        Scene scene = SceneManager.GetSceneByPath(MapScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);
        try
        {
            Transform panel = null;
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                panel = SettingsPanelAuthoring.FindDeep(r.transform, "OptionPanel");
                if (panel != null) break;
            }
            if (panel == null) { Debug.LogError("[PauseMenu] 맵 OptionPanel 이 없다."); return; }

            // 🔴 OptionPanel 을 화면 전체로 펴고 반투명 검정으로 게임을 덮는다. 자체 캔버스(정렬 1000)로 HUD 위에 —
            //    기본 캔버스(정렬 0) 안에 있어 CombatHUD(별도 캔버스, 정렬 0)와 순서가 엉켜 스킬 아이콘이 메뉴를 가렸다(경석 10-20 Play).
            var pr = (RectTransform)panel;
            pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one;
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.offsetMin = pr.offsetMax = Vector2.zero;
            Image dim = panel.GetComponent<Image>();
            if (dim != null) { dim.color = DimColor; dim.raycastTarget = true; }
            Canvas canvas = panel.GetComponent<Canvas>();
            if (canvas == null) canvas = panel.gameObject.AddComponent<Canvas>();
            // 🔴 Canvas.overrideSorting setter 는 비활성 오브젝트(OptionPanel 은 평소 꺼져 있다)에서 저장되지 않았다(10-20 확인) — 직렬화 필드로 넣는다.
            var cso = new SerializedObject(canvas);
            cso.FindProperty("m_OverrideSorting").boolValue = true;
            cso.FindProperty("m_SortingOrder").intValue = OptionPanelSortingOrder;
            cso.ApplyModifiedPropertiesWithoutUndo();
            if (panel.GetComponent<GraphicRaycaster>() == null) panel.gameObject.AddComponent<GraphicRaycaster>();   // 하위 캔버스는 자기 레이캐스터가 있어야 클릭된다

            Transform existing = panel.Find("PauseMenu");
            if (existing == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                inst.transform.SetParent(panel, false);
                existing = inst.transform;
            }
            // 설정창(SettingsBody) 위, 클라 경고창 아래.
            existing.SetAsLastSibling();
            Transform warning = panel.Find("WarnningMessage_Panel");
            if (warning != null) warning.SetAsLastSibling();

            Transform exitButton = panel.Find("ExitButton");
            if (exitButton != null) exitButton.gameObject.SetActive(false);   // 그만두기가 대신(PLAN-pause-menu §4-3)

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[PauseMenu] 맵 OptionPanel 적용 · 저장");
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
