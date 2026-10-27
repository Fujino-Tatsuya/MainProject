using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 타이틀 대기 화면의 Press 캡슐(경석 10-26 — PLAN-title-start-settings-tabs). <b>멱등</b>.
/// ESC 메뉴에서 뺀 "Press Any Button" 캡슐(알약 + 글자)을 옛 'PRESS ANY KEY' 글자 대신 <c>Press_UI_Canvas</c> 에 둔다.
/// </summary>
/// <remarks>
/// 렌더 경로는 그대로다 — <c>TitlePressCam</c> 이 Press 캔버스(1024×256)를 소형 RT 에 찍고,
/// <c>PressAnyKey_Screen</c> 의 <see cref="TitleOverlayCrtImage"/> 가 <c>Title/CRTUI</c> 로 화면 앞 하단에 보여 준다.
/// 지직거림 = <see cref="TitleCrtFx"/>(찢김·글리치 버스트) · 깜빡임 = 머티리얼 <c>_Pulse</c>(밝기 sin, 약 2초 주기).
/// </remarks>
public static class TitlePressCapsuleAuthoring
{
    const string ScenePath = "Assets/0.Scenes/MainFlow/1.TitleScene.unity";
    const string PillPath = "Assets/50.Art/UI/UI_Panel/Sprites/Buttons/button_pill_neutral.png";
    const string PressMaterialPath = "Assets/3.Materials/title/MA_TitlePressCRT.mat";

    // ESC 메뉴 목업(03_PauseMenu)의 캡슐 높이·글자 크기로 만들고 통째로 키운다 — 9-slice 둥근 끝 비율이 같게.
    // 가로는 목업(473)보다 넓혀 글자 칸을 확보한다(경석 10-26 — 1차에 크기를 안 넣어 100×100 원 + "Pres…" 로 잘렸다).
    // 🔴 SettingsArtSkin.SetSliced 는 sizeDelta 를 정하지 않는다 — 여기서 직접 넣는다.
    static readonly Vector2 CapsuleSize = new(530f, 133f);
    const float CapsuleScale = 1.75f;         // 928×233 — Press 캔버스(1024×256) 안, 찢김 밀림 여백을 남긴다
    const float LabelSize = 50f;
    // 화면에 찍히는 크기(오버레이 캔버스 단위). 소스 RT 1024×256 과 같은 4:1. 1차 760×190 → 키움(경석 10-26).
    static readonly Vector2 ScreenSize = new(920f, 230f);
    // 깜빡임 세기 — 밝기가 (1 − Pulse) 까지 내려갔다 돌아온다. 예전 글자 0.18 은 거의 안 보였다(경석 10-26 "깜빡임 잘 나오게").
    const float Pulse = 0.55f;

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

        var mat = AssetDatabase.LoadAssetAtPath<Material>(PressMaterialPath);
        if (mat != null && !Mathf.Approximately(mat.GetFloat("_Pulse"), Pulse))
        {
            mat.SetFloat("_Pulse", Pulse);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TitlePress] {PressMaterialPath} _Pulse → {Pulse}");
        }
    }

    static bool Apply(Scene scene)
    {
        Transform canvas = FindRoot(scene, "Press_UI_Canvas");
        if (canvas == null) { Debug.LogError("[TitlePress] Press_UI_Canvas 가 없다."); return false; }

        var pill = AssetDatabase.LoadAssetAtPath<Sprite>(PillPath);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SettingsArtSkin.FontAssetPath);
        if (pill == null) { Debug.LogError($"[TitlePress] 알약 그림이 없다({PillPath}) — SVN 아트를 받을 것."); return false; }

        Transform oldText = canvas.Find("PressAnyKey_Text");
        if (oldText != null) Object.DestroyImmediate(oldText.gameObject);

        Transform capsuleT = canvas.Find("PressAnyKey_Capsule");
        GameObject capsule = capsuleT != null ? capsuleT.gameObject : new GameObject("PressAnyKey_Capsule", typeof(RectTransform), typeof(Image));
        capsule.layer = canvas.gameObject.layer;   // TitlePressCam 은 UI 레이어만 찍는다
        var r = (RectTransform)capsule.transform;
        r.SetParent(canvas, false);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
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

        // 화면에 찍히는 RawImage(TitleOverlayCrtImage) 크기.
        GameObject screen = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == "PressAnyKey_Screen") screen = t.gameObject;
        if (screen != null) ((RectTransform)screen.transform).sizeDelta = ScreenSize;
        else Debug.LogWarning("[TitlePress] PressAnyKey_Screen 이 없다 — 화면 크기는 그대로.");
        return true;
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

    static Transform FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == name) return root.transform;
        return null;
    }
}
