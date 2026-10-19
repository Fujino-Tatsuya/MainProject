using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로비 결과 패널(할 일 48) — <c>LobbyResultPanel.prefab</c> 과 행 프리팹 <c>LobbyResultPlayerRow.prefab</c> 을 만든다.
/// 배치 = UI_Panel 아트 05_BattleResults(1920×1080 좌표, Sprite 2배 해상도 PPU 200 → RectTransform = PNG 픽셀/2).
/// 목업 7열(순위/플레이어/가한피해/회복량/간파 성공/입은 피해/사망) 대신 기존 데이터 6열을 가운데 정렬로 둔다.
///
/// 🔴 멱등 — 기본 메뉴는 프리팹이 이미 있으면 손대지 않는다(손으로 맞춘 레이아웃 보호).
/// 처음부터 다시 만들려면 "(재생성)" 메뉴. 씬에는 배치하지 않는다(로비 배치는 Work3 레인).
/// 미리보기 메뉴는 프리뷰 씬에서 가짜 SessionResult 로 1920×1080 PNG 를 찍고 결과를 비운다(열린 씬 무수정).
/// 사용법 = Docs/tech/lobby-result-panel.md.
/// </summary>
public static class LobbyResultPanelAuthoring
{
    const string Tag = "[LobbyResultPanel]";
    const string PrefabDir = "Assets/2.Prefabs/UI/Result";
    public const string PanelPath = PrefabDir + "/LobbyResultPanel.prefab";
    public const string RowPath = PrefabDir + "/LobbyResultPlayerRow.prefab";
    const string RosterPath = "Assets/9.ScriptableObject/Player/CharacterRoster.asset";
    // 원본 폰트(SeoulAlrimTTF-Heavy)가 없어 로비 씬이 쓰는 한글 TMP 폰트를 쓴다.
    const string FontPath = "Assets/Resources/NotoSansKR-VariableFont_wght SDF.asset";
    const string ArtDir = "Assets/50.Art/UI/UI_Panel/Sprites/";
    const string MockupRelative = "50.Art/UI/UI_Panel/Mockups~/05_BattleResults_with_text.png";
    const string CaptureDir = "C:/UnityProject/_lane-artifacts/MainProject-WorkTree/48-result-panel";

    const int SortingOrder = 200; // 로비 HUD(0)·튜토리얼(50~60) 위, 로딩 화면(1000) 아래

    // ── 표 기하(1920×1080 좌상단 기준, manifest rect = 투명 여백 제외 디자인 영역) ──
    struct Column
    {
        public string Name;   // Header_<Name> · Cell_<Name> · Text_<Name>
        public string Header; // 열 제목(직렬화 기본값)
        public bool Narrow;
    }

    static readonly Column[] Columns =
    {
        new Column { Name = "Slot",     Header = "번호",      Narrow = true },
        new Column { Name = "Player",   Header = "플레이어" },
        new Column { Name = "Damage",   Header = "가한 피해" },
        new Column { Name = "Kills",    Header = "처치" },
        new Column { Name = "Counters", Header = "간파 성공" },
        new Column { Name = "Deaths",   Header = "사망",      Narrow = true },
    };

    const float NarrowW = 150f;
    const float WideW = 235.3786f;
    const float ColumnGap = 10f;
    const float HeaderY = 275.2126f;
    const float HeaderH = 73.0693f;
    const float CellH = 109.2404f;
    const float RowPadX = 5.0744f;   // result_row_backing ↔ 첫 칸
    const float RowPadTop = 4.4367f;
    const float RowH = 118.1138f;
    const float RowsTopY = 364.9398f;
    const float RowSpacing = 6f;
    const int VisibleRows = 3;       // 실사용 3인
    const float Padding = 2f;        // Sprite 바깥 투명 여백(논리 px)

    static float ColumnWidth(Column c) => c.Narrow ? NarrowW : WideW;

    static float TableWidth()
    {
        float w = 0f;
        foreach (Column c in Columns) w += ColumnWidth(c);
        return w + ColumnGap * (Columns.Length - 1);
    }

    static float TableLeft => 960f - TableWidth() * 0.5f;
    static float RowWidth => TableWidth() + RowPadX * 2f;

    static float ColumnLeft(int index)
    {
        float x = 0f;
        for (int i = 0; i < index; i++) x += ColumnWidth(Columns[i]) + ColumnGap;
        return x;
    }

    static readonly Color TextLight = new Color(0.86f, 0.86f, 0.84f, 1f);
    static readonly Color LocalMarkerColor = new Color(1f, 0.78f, 0.2f, 1f);
    static readonly Color DimColor = new Color(0f, 0f, 0f, 0.6f);

    // ── 메뉴 ────────────────────────────────────────────────

    [MenuItem("Tools/UI/Authoring/Lobby Result Panel")]
    public static void Build() => Build(false);

    [MenuItem("Tools/UI/Authoring/Lobby Result Panel (재생성)")]
    public static void Rebuild() => Build(true);

    static void Build(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Play 중에는 실행하지 않는다 — 먼저 정지할 것.");
            return;
        }

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"{Tag} 폰트를 찾지 못했다: {FontPath}");
            return;
        }

        if (!AssetDatabase.IsValidFolder(PrefabDir))
        {
            Debug.LogError($"{Tag} 폴더가 없다: {PrefabDir}");
            return;
        }

        var sprites = new Sprites();
        if (!sprites.Load())
            return;

        ResultPlayerRowView row = AssetDatabase.LoadAssetAtPath<GameObject>(RowPath)?.GetComponent<ResultPlayerRowView>();
        if (row == null || force)
            row = BuildRowPrefab(font, sprites);
        else
            Debug.Log($"{Tag} 행 프리팹 재사용(손대지 않음): {RowPath}");
        if (row == null)
            return;

        bool panelExists = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath)?.GetComponent<LobbyResultPanel>() != null;
        if (panelExists && !force)
        {
            Debug.Log($"{Tag} 패널 프리팹 재사용(손대지 않음): {PanelPath} — 다시 만들려면 '(재생성)' 메뉴");
            return;
        }

        BuildPanelPrefab(font, sprites, row);
    }

    sealed class Sprites
    {
        public Sprite Panel, HeaderCapsule, CellNarrow, CellWide, RowBacking, TimerCapsule, Pill;

        public bool Load()
        {
            Panel = LoadSprite("Panels/panel_battle_results.png");
            HeaderCapsule = LoadSprite("Results/result_header_capsule.png");
            CellNarrow = LoadSprite("Results/result_cell_narrow.png");
            CellWide = LoadSprite("Results/result_cell_wide.png");
            RowBacking = LoadSprite("Results/result_row_backing.png");
            TimerCapsule = LoadSprite("Results/result_timer_capsule.png");
            Pill = LoadSprite("Buttons/button_pill_neutral.png");
            return Panel != null && HeaderCapsule != null && CellNarrow != null && CellWide != null &&
                   RowBacking != null && TimerCapsule != null && Pill != null;
        }

        static Sprite LoadSprite(string relative)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + relative);
            if (sprite == null)
                Debug.LogError($"{Tag} Sprite 를 찾지 못했다: {ArtDir}{relative}");
            else if (!Mathf.Approximately(sprite.pixelsPerUnit, 200f))
                Debug.LogWarning($"{Tag} {relative} PPU 가 {sprite.pixelsPerUnit} 다(README 는 200) — 아트 .meta 는 고치지 않는다. 보고할 것.");
            return sprite;
        }
    }

    // ── 행 프리팹 ─────────────────────────────────────────────

    static ResultPlayerRowView BuildRowPrefab(TMP_FontAsset font, Sprites sprites)
    {
        var root = new GameObject("LobbyResultPlayerRow", typeof(RectTransform));
        try
        {
            root.layer = LayerMask.NameToLayer("UI");
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(RowWidth, RowH);

            LayoutElement element = root.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = RowWidth;
            element.minHeight = element.preferredHeight = RowH;

            // "나" 강조 — 행 배경을 금색으로 조금 크게 깐다(배경 뒤). Bind 가 로컬 행만 켠다.
            RectTransform marker = NewRect("Marker_Local", root.transform);
            StretchWithOffset(marker, Padding + 9f);
            AddImage(marker.gameObject, sprites.RowBacking, true, LocalMarkerColor);
            marker.gameObject.SetActive(false);

            RectTransform backing = NewRect("Backing", root.transform);
            StretchWithOffset(backing, Padding);
            AddImage(backing.gameObject, sprites.RowBacking, true, Color.white);

            var refs = new Dictionary<string, Component>();
            for (int i = 0; i < Columns.Length; i++)
            {
                Column column = Columns[i];
                Sprite cellSprite = column.Narrow ? sprites.CellNarrow : sprites.CellWide;
                RectTransform cell = NewRect("Cell_" + column.Name, root.transform);
                cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f);
                cell.pivot = new Vector2(0.5f, 0.5f);
                cell.anchoredPosition = new Vector2(RowPadX + ColumnLeft(i) + ColumnWidth(column) * 0.5f,
                    -(RowPadTop + CellH * 0.5f));
                cell.sizeDelta = NativeSize(cellSprite); // 결과 셀 = Simple, 원래 크기
                AddImage(cell.gameObject, cellSprite, false, Color.white);

                if (column.Name == "Player")
                {
                    RectTransform portrait = NewRect("Image_Portrait", cell);
                    portrait.anchorMin = portrait.anchorMax = new Vector2(0f, 0.5f);
                    portrait.pivot = new Vector2(0f, 0.5f);
                    portrait.anchoredPosition = new Vector2(Padding + 12f, 0f);
                    portrait.sizeDelta = new Vector2(84f, 84f);
                    Image portraitImage = AddImage(portrait.gameObject, null, false, Color.white);
                    portraitImage.preserveAspect = true;
                    portraitImage.enabled = false; // 초상화가 있을 때만 Bind 가 켠다
                    refs["Image_Portrait"] = portraitImage;

                    RectTransform name = NewRect("Text_Character", cell);
                    name.anchorMin = Vector2.zero;
                    name.anchorMax = Vector2.one;
                    name.offsetMin = new Vector2(Padding + 12f + 84f + 10f, 0f);
                    name.offsetMax = new Vector2(-(Padding + 10f), 0f);
                    refs["Text_Character"] = AddText(name, font, "-", 38f, TextAlignmentOptions.Left, Color.white, ellipsis: true);
                    continue;
                }

                RectTransform text = NewRect("Text_" + column.Name, cell);
                StretchWithOffset(text, -Padding);
                refs["Text_" + column.Name] = AddText(text, font, "-", 44f, TextAlignmentOptions.Center, Color.white);
            }

            ResultPlayerRowView view = root.AddComponent<ResultPlayerRowView>();
            var so = new SerializedObject(view);
            so.FindProperty("slotText").objectReferenceValue = refs["Text_Slot"];
            so.FindProperty("characterText").objectReferenceValue = refs["Text_Character"];
            so.FindProperty("portraitImage").objectReferenceValue = refs["Image_Portrait"];
            so.FindProperty("damageText").objectReferenceValue = refs["Text_Damage"];
            so.FindProperty("killsText").objectReferenceValue = refs["Text_Kills"];
            so.FindProperty("countersText").objectReferenceValue = refs["Text_Counters"];
            so.FindProperty("deathsText").objectReferenceValue = refs["Text_Deaths"];
            so.FindProperty("localMarker").objectReferenceValue = marker.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, RowPath, out bool success);
            Debug.Log($"{Tag} 행 프리팹 저장 {(success ? "성공" : "실패")}: {RowPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(RowPath)?.GetComponent<ResultPlayerRowView>();
    }

    // ── 패널 프리팹 ───────────────────────────────────────────

    static void BuildPanelPrefab(TMP_FontAsset font, Sprites sprites, ResultPlayerRowView rowPrefab)
    {
        var root = new GameObject("LobbyResultPanel", typeof(RectTransform));
        try
        {
            root.layer = LayerMask.NameToLayer("UI");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
            root.AddComponent<GraphicRaycaster>();

            RectTransform content = NewRect("Content", root.transform);
            StretchWithOffset(content, 0f);
            content.gameObject.AddComponent<UiModalBlocker>();

            // 뒤 화면 어둡게 + 클릭 막기(모달)
            RectTransform dim = NewRect("Dim", content);
            StretchWithOffset(dim, 0f);
            AddImage(dim.gameObject, null, false, DimColor).raycastTarget = true;

            RectTransform panel = NewRect("Panel", content);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero; // 디자인 rect 중심 = (960, 540)
            panel.sizeDelta = NativeSize(sprites.Panel);
            AddImage(panel.gameObject, sprites.Panel, false, Color.white).raycastTarget = true;

            // 탭 제목 + 판 결과
            RectTransform title = NewRect("Text_Title", panel);
            PlaceDesign(title, 160f, 128f, 360f, 100f);
            AddText(title, font, "전투결과", 84f, TextAlignmentOptions.Center, Color.white);

            RectTransform outcome = NewRect("Text_Outcome", panel);
            PlaceDesign(outcome, 540f, 160f, 600f, 76f);
            TMP_Text outcomeText = AddText(outcome, font, "CLEAR", 56f, TextAlignmentOptions.Left, Color.white);

            // 열 제목 — result_header_capsule(Sliced)
            RectTransform header = NewRect("Header", panel);
            StretchWithOffset(header, 0f);
            for (int i = 0; i < Columns.Length; i++)
            {
                Column column = Columns[i];
                RectTransform capsule = NewRect("Header_" + column.Name, header);
                PlaceDesign(capsule, TableLeft + ColumnLeft(i) - Padding, HeaderY - Padding,
                    ColumnWidth(column) + Padding * 2f, HeaderH + Padding * 2f);
                AddImage(capsule.gameObject, sprites.HeaderCapsule, true, Color.white);

                RectTransform label = NewRect("Label", capsule);
                StretchWithOffset(label, -Padding);
                AddText(label, font, column.Header, 40f, TextAlignmentOptions.Center, Color.white);
            }

            // 플레이어 행 — 런타임에 ResultStatsView 가 복제한다. 디자인용 더미 행을 두지 말 것.
            RectTransform rows = NewRect("PlayerRows", panel);
            rows.anchorMin = rows.anchorMax = new Vector2(0.5f, 0.5f);
            rows.pivot = new Vector2(0.5f, 1f);
            rows.anchoredPosition = new Vector2(0f, 540f - RowsTopY);
            rows.sizeDelta = new Vector2(RowWidth, RowH * VisibleRows + RowSpacing * (VisibleRows - 1));
            VerticalLayoutGroup vertical = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = RowSpacing;
            vertical.childAlignment = TextAnchor.UpperCenter;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = false;
            vertical.childForceExpandHeight = false;

            // 푸터 — 소요시간 캡슐(Sliced) + 닫기 버튼(button_pill_neutral, Sliced)
            RectTransform timer = NewRect("TimerCapsule", panel);
            PlaceDesign(timer, 137.4231f - Padding, 830.1236f - Padding, 824.4923f + Padding * 2f, 144.794f + Padding * 2f);
            AddImage(timer.gameObject, sprites.TimerCapsule, true, Color.white);

            RectTransform timeLabel = NewRect("Text_PlayTimeLabel", panel);
            PlaceDesign(timeLabel, 187f, 850f, 400f, 100f);
            AddText(timeLabel, font, "소요시간  :", 72f, TextAlignmentOptions.Left, TextLight);

            RectTransform timeValue = NewRect("Text_PlayTime", panel);
            PlaceDesign(timeValue, 600f, 850f, 330f, 100f);
            TMP_Text playTimeText = AddText(timeValue, font, "00:00", 72f, TextAlignmentOptions.Left, Color.white);

            RectTransform close = NewRect("Button_Close", panel);
            PlaceDesign(close, 1313.7607f - Padding, 838.0244f - Padding, 444.1768f + Padding * 2f, 128.9922f + Padding * 2f);
            Image closeImage = AddImage(close.gameObject, sprites.Pill, true, Color.white);
            closeImage.raycastTarget = true;
            Button closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = closeImage;
            RectTransform closeLabel = NewRect("Label", close);
            StretchWithOffset(closeLabel, -Padding);
            AddText(closeLabel, font, "닫기", 54f, TextAlignmentOptions.Center, TextLight);

            // 배선
            ResultStatsView stats = panel.gameObject.AddComponent<ResultStatsView>();
            var statsSo = new SerializedObject(stats);
            statsSo.FindProperty("outcomeText").objectReferenceValue = outcomeText;
            statsSo.FindProperty("playerRowContainer").objectReferenceValue = rows;
            statsSo.FindProperty("playerRowPrefab").objectReferenceValue = rowPrefab;
            CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            if (roster == null)
                Debug.LogError($"{Tag} 로스터를 찾지 못했다: {RosterPath}");
            statsSo.FindProperty("characterRoster").objectReferenceValue = roster;
            statsSo.FindProperty("applyOnStart").boolValue = false;
            statsSo.ApplyModifiedPropertiesWithoutUndo();

            LobbyResultPanel view = root.AddComponent<LobbyResultPanel>();
            var viewSo = new SerializedObject(view);
            viewSo.FindProperty("content").objectReferenceValue = content.gameObject;
            viewSo.FindProperty("statsView").objectReferenceValue = stats;
            viewSo.FindProperty("playTimeText").objectReferenceValue = playTimeText;
            viewSo.FindProperty("outcomeText").objectReferenceValue = outcomeText;
            viewSo.FindProperty("closeButton").objectReferenceValue = closeButton;
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            content.gameObject.SetActive(false); // 기본 닫힘 — Show 가 켠다

            PrefabUtility.SaveAsPrefabAsset(root, PanelPath, out bool success);
            Debug.Log($"{Tag} 패널 프리팹 저장 {(success ? "성공" : "실패")}: {PanelPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ── 미리보기 캡처 ─────────────────────────────────────────

    [MenuItem("Tools/UI/Preview/Lobby Result Panel 캡처")]
    public static void CapturePreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Play 중에는 실행하지 않는다 — 먼저 정지할 것.");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        if (prefab == null)
        {
            Debug.LogError($"{Tag} 패널 프리팹이 없다 — 먼저 'Tools/UI/Authoring/Lobby Result Panel'.");
            return;
        }

        Directory.CreateDirectory(CaptureDir);
        Scene preview = EditorSceneManager.NewPreviewScene();
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        Texture2D shot = null;
        Camera camera = null;
        try
        {
            // 가짜 결과 — 3인, 로스터 캐릭터 0·1·2, P2 가 "나", 클리어, 12:34
            SessionResult.Capture(SessionOutcome.Cleared, 754.4f, new List<PlayerSessionStats>
            {
                new PlayerSessionStats { ClientId = 0, CharacterId = 0, Damage = 48210, Kills = 37, Counters = 6, Deaths = 1 },
                new PlayerSessionStats { ClientId = 1, CharacterId = 1, Damage = 61875, Kills = 52, Counters = 2, Deaths = 0 },
                new PlayerSessionStats { ClientId = 2, CharacterId = 2, Damage = 39940, Kills = 29, Counters = 11, Deaths = 3 },
            });

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);

            var cameraGo = new GameObject("PreviewCamera");
            SceneManager.MoveGameObjectToScene(cameraGo, preview);
            camera = cameraGo.AddComponent<Camera>();
            camera.scene = preview;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.22f, 0.25f, 0.28f, 1f); // 로비 대신 단색
            camera.targetTexture = rt;
            camera.cameraType = CameraType.Game;

            Canvas canvas = instance.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;

            LobbyResultPanel panel = instance.GetComponent<LobbyResultPanel>();
            instance.transform.Find("Content").gameObject.SetActive(true);
            panel.Refresh(1UL); // P2 강조

            Canvas.ForceUpdateCanvases();
            foreach (LayoutGroup group in instance.GetComponentsInChildren<LayoutGroup>(true))
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
            foreach (TMP_Text text in instance.GetComponentsInChildren<TMP_Text>(true))
                text.ForceMeshUpdate(true, true);
            Canvas.ForceUpdateCanvases();

            camera.Render();
            shot = ReadBack(rt);
            string capturePath = Path.Combine(CaptureDir, "LobbyResultPanel_preview.png");
            File.WriteAllBytes(capturePath, shot.EncodeToPNG());
            Debug.Log($"{Tag} 캡처 저장: {capturePath}");

            SaveComparison(shot);
        }
        finally
        {
            SessionResult.Clear(); // 가짜 결과 원상복구
            if (shot != null) Object.DestroyImmediate(shot);
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    static void SaveComparison(Texture2D shot)
    {
        string mockupPath = Path.Combine(Application.dataPath, MockupRelative);
        if (!File.Exists(mockupPath))
        {
            Debug.LogWarning($"{Tag} 목업이 없어 비교 PNG 를 건너뛴다: {mockupPath}");
            return;
        }

        var mockup = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D scaled = null;
        Texture2D combined = null;
        try
        {
            mockup.LoadImage(File.ReadAllBytes(mockupPath));
            scaled = Resize(mockup, shot.width, shot.height);
            combined = new Texture2D(shot.width * 2, shot.height, TextureFormat.RGBA32, false);
            combined.SetPixels(0, 0, shot.width, shot.height, scaled.GetPixels());
            combined.SetPixels(shot.width, 0, shot.width, shot.height, shot.GetPixels());
            combined.Apply();
            string path = Path.Combine(CaptureDir, "LobbyResultPanel_vs_mockup.png");
            File.WriteAllBytes(path, combined.EncodeToPNG());
            Debug.Log($"{Tag} 비교 저장(왼쪽 목업 {mockup.width}x{mockup.height} · 오른쪽 캡처): {path}");
        }
        finally
        {
            Object.DestroyImmediate(mockup);
            if (scaled != null && scaled != mockup) Object.DestroyImmediate(scaled);
            if (combined != null) Object.DestroyImmediate(combined);
        }
    }

    static Texture2D Resize(Texture2D source, int width, int height)
    {
        if (source.width == width && source.height == height)
            return source;

        RenderTexture temp = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        try
        {
            Graphics.Blit(source, temp);
            return ReadBack(temp);
        }
        finally
        {
            RenderTexture.ReleaseTemporary(temp);
        }
    }

    static Texture2D ReadBack(RenderTexture rt)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        return texture;
    }

    // ── 공통 ─────────────────────────────────────────────────

    static Vector2 NativeSize(Sprite sprite) => sprite.rect.size * (100f / sprite.pixelsPerUnit);

    /// <summary>1920×1080 좌상단 기준 디자인 rect → Panel(중심 = 화면 중심) 로컬 배치.</summary>
    static void PlaceDesign(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x + width * 0.5f - 960f, 540f - (y + height * 0.5f));
        rect.sizeDelta = new Vector2(width, height);
    }

    /// <summary>부모를 꽉 채우고 사방으로 <paramref name="outset"/> 만큼 넓힌다(음수 = 안쪽).</summary>
    static void StretchWithOffset(RectTransform rect, float outset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(-outset, -outset);
        rect.offsetMax = new Vector2(outset, outset);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Image AddImage(GameObject go, Sprite sprite, bool sliced, Color color)
    {
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>
    /// 한 줄 TMP. 기본 Overflow — NotoSansKR 줄 높이(≈1.45em)가 상자보다 크면 Ellipsis 는 글자를 통째로 숨긴다.
    /// 폭이 정해진 이름 칸만 <paramref name="ellipsis"/> 로 말줄임.
    /// </summary>
    static TMP_Text AddText(RectTransform rect, TMP_FontAsset font, string value, float size,
        TextAlignmentOptions align, Color color, bool ellipsis = false)
    {
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.alignment = align;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = ellipsis ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }
}
