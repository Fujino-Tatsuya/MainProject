using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 결과 화면 플레이어별 통계(PLAN-result-stats 2단계) — 행 프리팹 <c>ResultPlayerRow.prefab</c> 을 만들고
/// <c>5.ResultScene</c> 에 열 제목 행·<c>PlayerRows</c> 컨테이너를 만든 뒤 <see cref="ResultStatsView"/> 를 배선한다.
///
/// 🔴 <b>멱등하다</b> — 없는 것만 기본값으로 만들고, 이미 있는 오브젝트의 위치·크기·글자·프리팹은 건드리지 않는다
/// (10-08 은희 수동 레이아웃 보호). 다시 돌려도 바뀌는 건 ResultStatsView 배선과 행 미리보기(RowPreview_P*)뿐이다.
/// 계층·이름 = Docs/tech/result-stats-ui-setup.md (코드가 이름 폴백으로도 찾는다).
/// </summary>
public static class ResultStatsRowsAuthoring
{
    const string Tag = "[ResultStatsRows]";
    const string PrefabDir = "Assets/2.Prefabs/UI/Result";
    const string PrefabPath = PrefabDir + "/ResultPlayerRow.prefab";
    const string ScenePath = "Assets/0.Scenes/MainFlow/5.ResultScene.unity";
    const string RosterPath = "Assets/9.ScriptableObject/Player/CharacterRoster.asset";

    // 열 정의 — 행 프리팹과 열 제목 행이 같은 폭을 쓴다. HeaderTitle 이 null 이면 텍스트 없는 빈 칸(초상화).
    struct Column
    {
        public string RowName;
        public string HeaderTitle;
        public float Width;
        public TextAlignmentOptions Align;
        public bool IsImage;
    }

    static readonly Column[] Columns =
    {
        new Column { RowName = "Text_Slot",      HeaderTitle = "",       Width = 80f,  Align = TextAlignmentOptions.Center },
        new Column { RowName = "Image_Portrait", HeaderTitle = null,     Width = 64f,  IsImage = true },
        new Column { RowName = "Text_Character", HeaderTitle = "캐릭터", Width = 220f, Align = TextAlignmentOptions.Left },
        new Column { RowName = "Text_Damage",    HeaderTitle = "데미지", Width = 200f, Align = TextAlignmentOptions.Center },
        new Column { RowName = "Text_Kills",     HeaderTitle = "처치",   Width = 140f, Align = TextAlignmentOptions.Center },
        new Column { RowName = "Text_Counters",  HeaderTitle = "간파",   Width = 140f, Align = TextAlignmentOptions.Center },
        new Column { RowName = "Text_Deaths",    HeaderTitle = "사망",   Width = 140f, Align = TextAlignmentOptions.Center },
    };

    const float RowWidth = 1100f;
    const float RowHeight = 72f;
    const float HeaderHeight = 44f;
    const float ColumnSpacing = 12f;
    const float RowSpacing = 8f;
    const float RowFontSize = 32f;
    const float HeaderFontSize = 26f;

    // ResultStats(600×260, 중앙 y=+120) 기준 로컬 좌표. 구 Text_Kills(y=-50)는 런타임에 숨겨지므로 그 자리부터 쓴다.
    static readonly Vector2 HeaderPos = new Vector2(0f, -60f);
    static readonly Vector2 RowsTopPos = new Vector2(0f, -90f);

    static readonly Color RowBackground = new Color(0f, 0f, 0f, 0.35f);
    static readonly Color LocalMarkerColor = new Color(0.96f, 0.72f, 0.28f, 0.35f);
    static readonly Color HeaderColor = new Color(0.75f, 0.78f, 0.84f, 1f);

    [MenuItem("Tools/UI/Authoring/Result Stats Rows")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Play 중에는 실행하지 않는다 — 먼저 정지할 것.");
            return;
        }

        // 기존 결과 텍스트와 같은 TMP 기본 폰트(LiberationSans SDF). 한글은 TMP Settings 전역 폴백(NotoSansKR)이 처리한다.
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        ResultPlayerRowView rowPrefab = BuildRowPrefab(font);
        if (rowPrefab == null)
            return;

        WireScene(rowPrefab, font);
    }

    // ── 행 프리팹 ─────────────────────────────────────────────

    static ResultPlayerRowView BuildRowPrefab(TMP_FontAsset font)
    {
        if (!AssetDatabase.IsValidFolder(PrefabDir))
        {
            AssetDatabase.CreateFolder("Assets/2.Prefabs/UI", "Result");
            Debug.Log($"{Tag} 폴더 생성: {PrefabDir}");
        }

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null && existing.GetComponent<ResultPlayerRowView>() != null)
        {
            // 🔴 이미 있으면 손대지 않는다 — 프리팹에서 손으로 맞춘 글꼴·폭·색을 덮어쓰지 않기 위해서다.
            //    처음부터 다시 만들려면 프리팹을 지우고 메뉴를 돌린다.
            Debug.Log($"{Tag} 행 프리팹 재사용(손대지 않음): {PrefabPath}");
            return existing.GetComponent<ResultPlayerRowView>();
        }

        bool exists = existing != null;
        GameObject root = exists
            ? PrefabUtility.LoadPrefabContents(PrefabPath)
            : new GameObject("ResultPlayerRow", typeof(RectTransform));

        try
        {
            root.layer = LayerMask.NameToLayer("UI");
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(RowWidth, RowHeight);

            Image background = GetOrAdd<Image>(root);
            background.color = RowBackground;
            background.raycastTarget = false;

            HorizontalLayoutGroup layout = GetOrAdd<HorizontalLayoutGroup>(root);
            ConfigureRowLayout(layout);

            LayoutElement element = GetOrAdd<LayoutElement>(root);
            element.minHeight = RowHeight;
            element.preferredHeight = RowHeight;
            element.preferredWidth = RowWidth;

            // 강조 배경 — 레이아웃 무시, 행 전체를 덮고 맨 뒤(첫 자식)에 둔다.
            RectTransform marker = GetOrCreateChild(root.transform, "Marker_Local");
            marker.SetSiblingIndex(0);
            Stretch(marker);
            Image markerImage = GetOrAdd<Image>(marker.gameObject);
            markerImage.color = LocalMarkerColor;
            markerImage.raycastTarget = false;
            GetOrAdd<LayoutElement>(marker.gameObject).ignoreLayout = true;
            marker.gameObject.SetActive(false);

            var refs = new Dictionary<string, Component>();
            for (int i = 0; i < Columns.Length; i++)
            {
                Column column = Columns[i];
                RectTransform cell = GetOrCreateChild(root.transform, column.RowName, out bool cellCreated);
                if (cellCreated)
                {
                    cell.SetSiblingIndex(i + 1);
                    SetColumnWidth(cell.gameObject, column.Width, RowHeight);
                }

                if (column.IsImage)
                {
                    Image image = GetOrAdd<Image>(cell.gameObject);
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                    image.enabled = false; // 초상화가 있을 때만 Bind 가 켠다
                    refs[column.RowName] = image;
                }
                else
                {
                    TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(cell.gameObject);
                    if (cellCreated) // 이미 있는 칸의 글꼴·크기·정렬은 손으로 맞춘 값을 유지
                        ConfigureText(text, font, RowFontSize, column.Align, Color.white, "-");
                    refs[column.RowName] = text;
                }
            }

            ResultPlayerRowView view = GetOrAdd<ResultPlayerRowView>(root);
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

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"{Tag} 행 프리팹 {(exists ? "갱신" : "생성")}: {PrefabPath}");
        }
        finally
        {
            if (exists)
                PrefabUtility.UnloadPrefabContents(root);
            else
                Object.DestroyImmediate(root);
        }

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        return saved != null ? saved.GetComponent<ResultPlayerRowView>() : null;
    }

    // ── 씬 ───────────────────────────────────────────────────

    static void WireScene(ResultPlayerRowView rowPrefab, TMP_FontAsset font)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        try
        {
            var views = new List<ResultStatsView>();
            foreach (GameObject root in scene.GetRootGameObjects())
                views.AddRange(root.GetComponentsInChildren<ResultStatsView>(true));

            if (views.Count != 1)
            {
                Debug.LogError($"{Tag} {ScenePath} 의 ResultStatsView 가 {views.Count} 개다 — 1 개여야 한다. 중단.");
                return;
            }

            ResultStatsView view = views[0];
            Transform parent = view.transform;

            // 🔴 이미 있는 오브젝트의 위치·크기·글자는 건드리지 않는다 — 은희가 씬에서 손으로 맞춘 레이아웃
            //    (10-08 9b4bad7a)을 다시 돌릴 때 덮어쓰지 않기 위해서다. 새로 만들 때만 기본값을 넣는다.

            // 열 제목 행
            RectTransform header = GetOrCreateChild(parent, "PlayerRowsHeader", out bool headerCreated);
            if (headerCreated)
                PlaceTopCenter(header, HeaderPos, new Vector2(RowWidth, HeaderHeight));
            ConfigureRowLayout(GetOrAdd<HorizontalLayoutGroup>(header.gameObject));
            for (int i = 0; i < Columns.Length; i++)
            {
                Column column = Columns[i];
                string name = "Header_" + column.RowName.Substring(column.RowName.IndexOf('_') + 1);
                RectTransform cell = GetOrCreateChild(header, name, out bool cellCreated);
                if (!cellCreated)
                    continue;
                cell.SetSiblingIndex(i);
                SetColumnWidth(cell.gameObject, column.Width, HeaderHeight);
                if (column.HeaderTitle != null)
                    ConfigureText(GetOrAdd<TextMeshProUGUI>(cell.gameObject), font, HeaderFontSize, column.Align,
                        HeaderColor, column.HeaderTitle);
            }
            Debug.Log($"{Tag} 열 제목 행 {(headerCreated ? "생성" : "재사용(위치·글자 유지)")}: PlayerRowsHeader");

            // 행 컨테이너 — 실제 행은 런타임에 복제된다. 자식은 배치 미리보기(RowPreview_P*, 런타임에 꺼짐)만 둔다.
            RectTransform rows = GetOrCreateChild(parent, "PlayerRows", out bool rowsCreated);
            if (rowsCreated)
                PlaceTopCenter(rows, RowsTopPos, new Vector2(RowWidth, RowHeight));
            VerticalLayoutGroup vertical = GetOrAdd<VerticalLayoutGroup>(rows.gameObject);
            vertical.spacing = RowSpacing;
            vertical.childAlignment = TextAnchor.UpperCenter;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
            ContentSizeFitter fitter = GetOrAdd<ContentSizeFitter>(rows.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            EnsureRowPreviews(rows, rowPrefab);
            int stray = 0;
            foreach (Transform child in rows)
                if (child.GetComponent<EditorPreviewOnly>() == null)
                    stray++;
            if (stray > 0)
                Debug.LogWarning($"{Tag} PlayerRows 아래에 미리보기가 아닌 자식 {stray} 개가 있다 — 런타임에 남으니 지울 것.");
            Debug.Log($"{Tag} 행 컨테이너 {(rowsCreated ? "생성" : "재사용(위치 유지)")}: PlayerRows");

            CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            if (roster == null)
                Debug.LogError($"{Tag} 로스터를 찾지 못했다: {RosterPath}");

            var so = new SerializedObject(view);
            so.FindProperty("playerRowContainer").objectReferenceValue = rows;
            so.FindProperty("playerRowPrefab").objectReferenceValue = rowPrefab;
            so.FindProperty("characterRoster").objectReferenceValue = roster;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"{Tag} ResultStatsView 배선: playerRowContainer · playerRowPrefab · characterRoster");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"{Tag} 씬 저장: {ScenePath}");
        }
        finally
        {
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    // ── 배치 미리보기 ─────────────────────────────────────────

    const int PreviewRowCount = 3; // 실사용 3인
    static readonly Color PreviewColor = new Color(0.2f, 0.75f, 1f, 0.35f);

    /// <summary>
    /// 결과 화면 씬의 PlayerRows 아래에 행 미리보기만 만들거나 갱신한다(위치·배선은 안 건드림).
    /// PlayerRows 를 옮기면 런타임 행이 미리보기 자리 그대로 뜬다.
    /// </summary>
    [MenuItem("Tools/UI/Authoring/Result Stats Row Preview")]
    public static void BuildPreviewOnly()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Play 중에는 실행하지 않는다 — 먼저 정지할 것.");
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        try
        {
            var views = new List<ResultStatsView>();
            foreach (GameObject root in scene.GetRootGameObjects())
                views.AddRange(root.GetComponentsInChildren<ResultStatsView>(true));
            Transform rows = null;
            if (views.Count == 1)
                foreach (Transform t in views[0].GetComponentsInChildren<Transform>(true))
                    if (t.name == "PlayerRows") { rows = t; break; }
            if (rows == null)
            {
                Debug.LogError($"{Tag} ResultStatsView(1개)/PlayerRows 를 찾지 못했다 — 먼저 'Result Stats Rows' 를 돌릴 것.");
                return;
            }

            EnsureRowPreviews((RectTransform)rows,
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)?.GetComponent<ResultPlayerRowView>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"{Tag} 씬 저장: {ScenePath}");
        }
        finally
        {
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// RowPreview_P1..P3 — Image 만 있는 자리 표시. 태그 EditorOnly(빌드 제외) + EditorPreviewOnly(Play 시 꺼짐).
    /// 높이는 행 프리팹 LayoutElement 를 따라가고, 폭·간격은 PlayerRows 의 VerticalLayoutGroup 이 정한다.
    /// </summary>
    static void EnsureRowPreviews(RectTransform rows, ResultPlayerRowView rowPrefab)
    {
        float height = RowHeight;
        LayoutElement prefabElement = rowPrefab != null ? rowPrefab.GetComponent<LayoutElement>() : null;
        if (prefabElement != null && prefabElement.preferredHeight > 0f)
            height = prefabElement.preferredHeight;

        for (int i = 0; i < PreviewRowCount; i++)
        {
            RectTransform preview = GetOrCreateChild(rows, $"RowPreview_P{i + 1}", out bool created);
            preview.gameObject.tag = "EditorOnly";
            GetOrAdd<EditorPreviewOnly>(preview.gameObject);

            LayoutElement element = GetOrAdd<LayoutElement>(preview.gameObject);
            element.minHeight = height;
            element.preferredHeight = height;

            Image image = GetOrAdd<Image>(preview.gameObject);
            image.raycastTarget = false;
            if (created)
                image.color = PreviewColor;

            Debug.Log($"{Tag} 행 미리보기 {(created ? "생성" : "갱신")}: {preview.name} (높이 {height})");
        }
    }

    // ── 공통 ─────────────────────────────────────────────────

    static void ConfigureRowLayout(HorizontalLayoutGroup layout)
    {
        layout.spacing = ColumnSpacing;
        layout.padding = new RectOffset(16, 16, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
    }

    static void SetColumnWidth(GameObject cell, float width, float height)
    {
        LayoutElement element = GetOrAdd<LayoutElement>(cell);
        element.minWidth = width;
        element.preferredWidth = width;
        element.flexibleWidth = 0f;
        element.preferredHeight = height;
    }

    static void ConfigureText(TextMeshProUGUI text, TMP_FontAsset font, float size, TextAlignmentOptions align,
        Color color, string value)
    {
        if (font != null)
            text.font = font;
        text.fontSize = size;
        text.alignment = align;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        text.text = value;
    }

    static void PlaceTopCenter(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static RectTransform GetOrCreateChild(Transform parent, string name)
    {
        return GetOrCreateChild(parent, name, out _);
    }

    static RectTransform GetOrCreateChild(Transform parent, string name, out bool created)
    {
        Transform found = parent.Find(name);
        created = found == null;
        if (found == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            found = go.transform;
        }

        return (RectTransform)found;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }
}
