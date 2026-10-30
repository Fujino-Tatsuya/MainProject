using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 타이틀 대기 화면의 Press 캡슐(경석 10-26 — PLAN-title-start-settings-tabs). <b>멱등</b>.
/// "Press Any Button" 캡슐(알약 그림 + 글자)을 오버레이 <c>Canvas/PressAnyKey_Root</c> 에 평범한 UI 로 둔다.
/// </summary>
/// <remarks>
/// 10-30 경석: 깜빡임(알파)·CRT 지직거림을 뺐다 — 그림 한 장이 그냥 떠 있는다.
/// 옛 렌더 경로(<c>TitlePressCam</c> → RT → <c>PressAnyKey_Screen</c> 의 <see cref="TitleOverlayCrtImage"/>·<c>Title/CRTUI</c>)와
/// 소스 캔버스 <c>Press_UI_Canvas</c> 는 이 메뉴가 지운다. 켜고 끄기는 그대로 <see cref="TitleFlowDirector"/> 가 <c>PressAnyKey_Root</c> 로 한다.
/// </remarks>
public static class TitlePressCapsuleAuthoring
{
    const string ScenePath = "Assets/0.Scenes/MainFlow/1.TitleScene.unity";
    const string PillPath = "Assets/50.Art/UI/UI_Panel/Sprites/Buttons/button_pill_neutral.png";

    // ESC 메뉴 목업(03_PauseMenu)의 캡슐 높이·글자 크기로 만들고 통째로 키운다 — 9-slice 둥근 끝 비율이 같게.
    // 가로는 목업(473)보다 넓혀 글자 칸을 확보한다(경석 10-26 — 1차에 크기를 안 넣어 100×100 원 + "Pres…" 로 잘렸다).
    // 🔴 SettingsArtSkin.SetSliced 는 sizeDelta 를 정하지 않는다 — 여기서 직접 넣는다.
    static readonly Vector2 CapsuleSize = new(530f, 133f);
    // 옛 화면 크기와 같게 — RT 1024 안 1.75배(928) 를 화면 920 으로 줄여 찍던 것 = 1.75 × 920/1024 ≈ 1.57.
    const float CapsuleScale = 1.57f;
    const float LabelSize = 50f;
    // 옛 PressAnyKey_Screen 자리 — 화면 아래 16%.
    static readonly Vector2 ScreenAnchor = new(0.5f, 0.16f);

    [MenuItem("Tools/Title/Authoring/Press 캡슐 배치 (멱등)")]
    static void Run()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = false;
        if (!scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            opened = true;
        }

        try
        {
            if (Apply(scene))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[TitlePress] Press 캡슐 배치 완료 — 1.TitleScene 저장.");
            }
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static bool Apply(Scene scene)
    {
        Transform root = FindDeep(scene, "PressAnyKey_Root");
        if (root == null) { Debug.LogError("[TitlePress] Canvas/PressAnyKey_Root 가 없다."); return false; }

        var pill = AssetDatabase.LoadAssetAtPath<Sprite>(PillPath);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SettingsArtSkin.FontAssetPath);
        if (pill == null) { Debug.LogError($"[TitlePress] 알약 그림이 없다({PillPath}) — SVN 아트를 받을 것."); return false; }

        // 옛 캡슐을 살려 옮긴다(글자·LocalizedText 그대로). 없으면 새로.
        Transform capsuleT = FindDeep(scene, "PressAnyKey_Capsule");
        GameObject capsule = capsuleT != null ? capsuleT.gameObject : new GameObject("PressAnyKey_Capsule", typeof(RectTransform), typeof(Image));
        capsule.layer = root.gameObject.layer;
        var r = (RectTransform)capsule.transform;
        r.SetParent(root, false);
        r.anchorMin = r.anchorMax = r.pivot = ScreenAnchor;
        r.anchoredPosition = Vector2.zero;
        r.localScale = new Vector3(CapsuleScale, CapsuleScale, 1f);
        r.sizeDelta = CapsuleSize;
        var img = capsule.GetComponent<Image>();
        SettingsArtSkin.SetSliced(img, pill, CapsuleSize);
        img.raycastTarget = false;

        Transform labelT = r.Find("Text");
        GameObject labelGo = labelT != null ? labelT.gameObject : new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.layer = capsule.layer;
        labelGo.transform.SetParent(r, false);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = UiStrings.PausePressAnyButton.Ko;
        if (font != null) { label.font = font; label.fontSharedMaterial = font.material; }
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        SettingsArtSkin.AutoSize(label, 24f, LabelSize);
        label.overflowMode = TextOverflowModes.Overflow;   // "…" 로 줄이지 않는다 — 자동 크기가 칸에 맞춘다
        SettingsArtSkin.Stretch(label.rectTransform, new Vector2(30f, 0f), new Vector2(-30f, 0f));
        var loc = labelGo.GetComponent<LocalizedText>();
        if (loc == null) loc = labelGo.AddComponent<LocalizedText>();   // ?? 금지 — 에디터의 가짜 null 을 못 거른다
        loc.Key = UiStrings.PausePressAnyButton.Key;

        // 옛 RT·CRT 경로 정리 — 캡슐을 옮긴 뒤에 지운다(Press_UI_Canvas 가 캡슐의 옛 부모).
        DestroyIfFound(scene, "PressAnyKey_Screen");
        DestroyIfFound(scene, "Press_UI_Canvas");
        DestroyIfFound(scene, "TitlePressCam");
        return true;
    }

    static void DestroyIfFound(Scene scene, string name)
    {
        Transform t = FindDeep(scene, name);
        if (t == null) return;
        Object.DestroyImmediate(t.gameObject);
        Debug.Log($"[TitlePress] {name} 삭제");
    }

    static Transform FindDeep(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    const string SkipFontPath = "Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset";   // 보스 스킵 위젯과 같은 글꼴

    /// <summary>접근 연출 "Esc 건너뛰기" 안내를 보스 스킵 위젯 모양(<see cref="TitleSkipHint"/>)으로. 옛 "ESC SKIP" 글자는 지운다. 멱등.</summary>
    [MenuItem("Tools/Title/Authoring/Esc 건너뛰기 안내 배치 (멱등)")]
    static void RunSkipHint()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = false;
        if (!scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            opened = true;
        }

        try
        {
            GameObject root = null;
            foreach (GameObject r in scene.GetRootGameObjects())
            foreach (Transform t in r.GetComponentsInChildren<Transform>(true))
                if (t.name == "SkipHint_Root") root = t.gameObject;
            if (root == null) { Debug.LogError("[TitleSkipHint] SkipHint_Root 가 없다."); return; }

            Transform oldText = root.transform.Find("SkipHint_Text");
            if (oldText != null) Object.DestroyImmediate(oldText.gameObject);

            var hint = root.GetComponent<TitleSkipHint>();
            if (hint == null) hint = root.AddComponent<TitleSkipHint>();
            var so = new SerializedObject(hint);
            so.FindProperty("pillSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(PillPath);
            so.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SkipFontPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[TitleSkipHint] Esc 건너뛰기 안내 배치 완료 — 1.TitleScene 저장.");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
