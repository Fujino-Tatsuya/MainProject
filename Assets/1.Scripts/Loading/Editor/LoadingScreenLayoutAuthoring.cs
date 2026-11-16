using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로딩 화면 v02 배치(할 일 290) — 시안 <c>ReC_Loading_UI_Images_20261113_v02/unity_preview_1920.png</c> 를 2.LoadingScene 에 옮긴다.
/// 왼쪽 위 브랜드 · 아래 그늘 · Tip 캡슐(무전기)+팁 문구 · 둥근 진행 막대(초록 채움+하프톤) · 회전하는 필름 릴+큰 % 숫자.
/// 기존 Text_Tooltip/Text_Status/Text_Percentage 는 참조를 살린 채 위치·모양만 바꾸고, 옛 "progress fill" 은 지운다.
/// 몇 번 돌려도 같은 결과(이름으로 찾아 다시 맞춘다). 위치는 이후 씬에서 직접 옮겨도 된다(다시 돌리면 이 값으로 돌아감).
/// </summary>
public static class LoadingScreenLayoutAuthoring
{
    private const string ScenePath = "Assets/0.Scenes/MainFlow/2.LoadingScene.unity";
    private const string ArtDir = "Assets/50.Art/UI/Loading/v02/";
    private const string HeavyFontPath = "Assets/50.Art/UI/Fonts/SeoulAlrimTTF-Heavy SDF.asset";
    private const string MediumFontPath = "Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset";

    private static readonly Color TextColor = new Color(0.945f, 0.937f, 0.910f, 1f);

    // 시안 기준(1920×1080) 치수
    private const float SideMargin = 48f;
    private const float TrackCenterY = 60f;
    private const float TrackHeight = 32f;
    private static readonly Vector2 FillInset = new Vector2(6f, 8f);   // 막대 안쪽 여백(가로, 세로)
    private const float IconRowY = 144f;                               // 필름 릴·% 숫자 줄 중심 높이
    private const float TipCapsuleY = 166f;
    private const float TipTextY = 109f;

    [MenuItem("Tools/Loading/로딩 화면 v02 배치 (290)")]
    public static void ApplyFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Apply();
    }

    /// <summary>배치 모드 진입점 — <c>-executeMethod LoadingScreenLayoutAuthoring.ApplyBatch</c>.</summary>
    public static void ApplyBatch()
    {
        try
        {
            Apply();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    public static void Apply()
    {
        ConfigureTextures();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        NetworkLoadingScreenView view = UnityEngine.Object.FindFirstObjectByType<NetworkLoadingScreenView>(FindObjectsInactive.Include);
        if (view == null) throw new InvalidOperationException("2.LoadingScene 에 NetworkLoadingScreenView 가 없다.");
        var canvas = (RectTransform)view.transform;

        var heavy = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HeavyFontPath);
        var medium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumFontPath);
        if (heavy == null || medium == null) throw new InvalidOperationException("서울알림 Heavy/Medium SDF 를 찾지 못했다.");

        // 아래 그늘 — 글자·막대가 그림 위에서 읽히게.
        Image shade = ImageChild(canvas, "BottomShade", Sprite("Loading_bottom_shade"));
        Place(shade.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 410f));

        // 왼쪽 위 브랜드(2x 이미지를 약 0.53배로).
        Image brand = ImageChild(canvas, "Brand", Sprite("Loading_loading_brand_2x"));
        Place(brand.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(SideMargin - 2f, -40f), new Vector2(160f, 145f));

        // Tip 캡슐 + 무전기 + "Tip".
        Image capsule = ImageChild(canvas, "TipCapsule", Sprite("Loading_tip_capsule"));
        capsule.type = Image.Type.Sliced;
        capsule.pixelsPerUnitMultiplier = 100f / 48f;
        Place(capsule.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(62f, TipCapsuleY), new Vector2(108f, 48f));
        Image radio = ImageChild(capsule.rectTransform, "Radio", Sprite("Loading_radio"));
        radio.preserveAspect = true;
        Place(radio.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(24f, -1f), new Vector2(22f, 33f));
        TMP_Text tipLabel = TextChild(capsule.rectTransform, "Label", heavy, 20f, TextAlignmentOptions.Center);
        Place(tipLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        tipLabel.rectTransform.offsetMin = new Vector2(40f, 0f);
        tipLabel.rectTransform.offsetMax = new Vector2(-10f, 2f);
        tipLabel.text = "Tip";

        // 팁 문구(기존 Text_Tooltip — 참조 유지).
        TMP_Text tooltip = Existing<TMP_Text>(canvas, "Text_Tooltip");
        StyleText(tooltip, medium, 28f, TextAlignmentOptions.MidlineLeft, TextColor);
        tooltip.fontStyle = FontStyles.Bold;
        Place(tooltip.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(64f, TipTextY), new Vector2(1500f, 44f));

        // 진행 막대 — 테두리 트랙 + 안쪽 영역 + 초록 채움(Sliced: 진행률만큼 오른쪽 끝을 늘림) + 하프톤(막대 전체 폭 고정, 채운 만큼만 보임).
        Image track = ImageChild(canvas, "ProgressTrack", Sprite("Loading_progress_track"));
        track.type = Image.Type.Sliced;
        track.pixelsPerUnitMultiplier = 2f;
        Place(track.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, TrackCenterY), new Vector2(-SideMargin * 2f, TrackHeight));

        RectTransform fillArea = RectChild(track.rectTransform, "FillArea");
        Place(fillArea, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        fillArea.offsetMin = FillInset;
        fillArea.offsetMax = -FillInset;

        Image fill = ImageChild(fillArea, "ProgressFill", Sprite("Loading_progress_fill"));
        fill.type = Image.Type.Sliced;
        fill.pixelsPerUnitMultiplier = 24f / (TrackHeight - FillInset.y * 2f);
        Place(fill.rectTransform, Vector2.zero, new Vector2(0.72f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        if (fill.GetComponent<RectMask2D>() == null) fill.gameObject.AddComponent<RectMask2D>();

        float fillWidth = 1920f - SideMargin * 2f - FillInset.x * 2f;
        Image halftone = ImageChild(fill.rectTransform, "Halftone", Sprite("Loading_progress_fill_halftone"));
        Place(halftone.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(fillWidth, 0f));

        // 필름 릴(로딩 중 회전) + % 숫자(기존 Text_Percentage) + 상태 문구(기존 Text_Status, 릴 왼쪽 작게).
        Image reel = ImageChild(canvas, "Reel", Sprite("Loading_film_canister"));
        reel.preserveAspect = true;
        Place(reel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-194f, IconRowY), new Vector2(56f, 56f));
        if (reel.GetComponent<LoadingReelSpin>() == null) reel.gameObject.AddComponent<LoadingReelSpin>();

        TMP_Text percent = Existing<TMP_Text>(canvas, "Text_Percentage");
        StyleText(percent, heavy, 46f, TextAlignmentOptions.MidlineLeft, TextColor);
        Place(percent.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0.5f), new Vector2(-150f, IconRowY), new Vector2(140f, 60f));
        percent.text = "72<size=50%> %</size>";

        TMP_Text status = Existing<TMP_Text>(canvas, "Text_Status");
        StyleText(status, medium, 22f, TextAlignmentOptions.MidlineRight, new Color(TextColor.r, TextColor.g, TextColor.b, 0.7f));
        Place(status.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0.5f), new Vector2(-236f, IconRowY), new Vector2(800f, 40f));

        // 옛 진행 막대 제거 + 뷰 참조.
        Transform oldFill = canvas.Find("progress fill");
        if (oldFill != null) UnityEngine.Object.DestroyImmediate(oldFill.gameObject);

        var so = new SerializedObject(view);
        so.FindProperty("progressFill").objectReferenceValue = fill;
        so.FindProperty("percentSuffix").stringValue = "<size=50%> %</size>";
        so.ApplyModifiedPropertiesWithoutUndo();

        // 그리는 순서(뒤 → 앞).
        string[] order = { "Background", "BottomShade", "Brand", "TipCapsule", "Text_Tooltip", "Text_Status", "ProgressTrack", "Reel", "Text_Percentage" };
        for (int i = 0; i < order.Length; i++)
        {
            Transform t = canvas.Find(order[i]);
            if (t != null) t.SetSiblingIndex(i);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[290] 로딩 화면 v02 배치 완료 — " + ScenePath);
    }

    private static void ConfigureTextures()
    {
        Configure("Loading_bottom_shade", Vector4.zero);
        Configure("Loading_loading_brand_2x", Vector4.zero);
        Configure("Loading_tip_capsule", new Vector4(50f, 50f, 50f, 50f));
        Configure("Loading_radio", Vector4.zero);
        Configure("Loading_progress_track", new Vector4(32f, 32f, 32f, 32f));
        Configure("Loading_progress_fill", new Vector4(12f, 12f, 12f, 12f));
        Configure("Loading_progress_fill_halftone", Vector4.zero);
        Configure("Loading_film_canister", Vector4.zero);
    }

    private static void Configure(string name, Vector4 border)
    {
        string path = ArtDir + name + ".png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("텍스처 없음: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spriteBorder = border;
        importer.SaveAndReimport();
    }

    private static Sprite Sprite(string name)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + name + ".png");
        if (sprite == null) throw new InvalidOperationException("스프라이트 없음: " + name);
        return sprite;
    }

    private static RectTransform RectChild(Transform parent, string name)
    {
        Transform found = parent.Find(name);
        if (found != null) return (RectTransform)found;
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image ImageChild(Transform parent, string name, Sprite sprite)
    {
        RectTransform rect = RectChild(parent, name);
        Image image = rect.GetComponent<Image>();
        if (image == null) image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.type = Image.Type.Simple;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text TextChild(Transform parent, string name, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
    {
        RectTransform rect = RectChild(parent, name);
        TMP_Text text = rect.GetComponent<TMP_Text>();
        if (text == null) text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        StyleText(text, font, size, alignment, TextColor);
        return text;
    }

    private static T Existing<T>(Transform parent, string name) where T : Component
    {
        Transform found = parent.Find(name);
        T component = found != null ? found.GetComponent<T>() : null;
        if (component == null) throw new InvalidOperationException("기존 오브젝트 없음: " + name);
        return component;
    }

    private static void StyleText(TMP_Text text, TMP_FontAsset font, float size, TextAlignmentOptions alignment, Color color)
    {
        text.font = font;
        text.fontSharedMaterial = font.material;
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.alignment = alignment;
        text.color = color;
        text.fontStyle = FontStyles.Normal;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }
}
