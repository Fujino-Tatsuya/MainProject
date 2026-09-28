using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 타이틀 연출 리그 저작 도구. 계획서 = PLAN-title-flow.md
//
// 왜 스크립트인가 — MCP 브리지로는 enum(Canvas.renderMode)과 오브젝트 참조를 못 쓴다.
// Director 하나에만 참조가 15개 넘게 붙으므로 손으로 꽂으면 빠뜨린 것을 나중에 못 찾는다.
// 여기 있으면 git 에 남고, 아트를 이식한 뒤 다시 돌려도 같은 결과가 나온다.
//
// 🔴 멱등하다. 이미 있으면 찾아 쓰고 없으면 만든다. 여러 번 돌려도 중복이 생기지 않는다.
public static class TitleRigAuthoring
{
    // 카메라 좌표는 경석이 아트 씬에서 잡아 준 구도를 그대로 쓴다.
    // FOV 는 아트 씬 카메라 값(26.99). 기존 타이틀 카메라는 60 이라 좌표만 베끼면 구도가 달라진다.
    const float Fov = 26.99f;

    static readonly Vector3 FarPos = new(0f, 4.26f, 13.8f);
    static readonly Vector3 NearPos = new(0f, 3.22f, 3.47f);
    static readonly Vector3 CamEuler = new(4.9f, 180f, 0f);

    // CRT 화면 중심. monitor_screen 의 트랜스폼 피벗은 (0, 1.11, -3.16) 이지만
    // FBX 피벗이 메시 중심이 아니어서, Near 카메라의 전방축 7m 지점으로 역산했다.
    // 🔴 아트를 이식한 뒤 CRT_Anchor 하나만 실제 화면에 맞추면 캔버스 둘이 함께 따라온다.
    const float AnchorDistance = 7f;

    // Near 에서 CRT 가 화면 세로의 몇 할을 차지하게 할 것인가. 스샷 실측 약 0.55.
    const float MenuScreenFill = 0.55f;

    // 설정 화면에서의 목표 점유율. 18pt 글자가 읽히려면 거의 꽉 채워야 한다.
    const float SettingsScreenFill = 0.95f;

    const float CanvasLogicalHeight = 1080f;
    const float CanvasLogicalWidth = 1920f;

    [MenuItem("Tools/Title/Authoring/타이틀 리그 조립")]
    public static void BuildTitleRig()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.path.EndsWith("1.TitleScene.unity"))
        {
            EditorUtility.DisplayDialog("타이틀 리그 조립",
                $"1.TitleScene 에서 실행할 것. 현재 씬: {scene.name}", "확인");
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("[TitleRig] Main Camera 를 찾지 못했다.");
            return;
        }

        // ── 카메라 ────────────────────────────────────────────────────────
        EnsureComponent<CinemachineBrain>(mainCamera.gameObject);

        Vector3 camForward = Quaternion.Euler(CamEuler) * Vector3.forward;
        Vector3 anchorPos = NearPos + camForward * AnchorDistance;
        Vector3 closeupPos = anchorPos - camForward * (AnchorDistance * MenuScreenFill / SettingsScreenFill);

        CinemachineCamera far = EnsureVcam("VCam_Far", FarPos, 30);
        CinemachineCamera near = EnsureVcam("VCam_Near", NearPos, 10);
        CinemachineCamera closeup = EnsureVcam("VCam_Closeup", closeupPos, 10);

        // ── CRT 앵커 + 월드 캔버스 ────────────────────────────────────────
        GameObject anchor = EnsureRoot("CRT_Anchor");
        // 🔴 Y 180°. vcam 들이 캔버스 +Z 쪽에서 보므로 identity 면 UI 뒷면이 보여 글자가 좌우 반전된다
        // (09-23 be6e69ee 에서 씬만 고쳤던 것을 도구에 반영 — 안 그러면 재실행 때 다시 뒤집힌다).
        anchor.transform.SetPositionAndRotation(anchorPos, Quaternion.Euler(0f, 180f, 0f));

        // 논리 크기는 1920×1080 을 유지하고 루트만 균일 축소한다.
        // 이래야 Option_Panel 내부의 고정 좌표가 전부 보존된다(계획서 §3.4).
        float visibleHeight = 2f * AnchorDistance * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
        float menuScale = visibleHeight * MenuScreenFill / CanvasLogicalHeight;

        Canvas menuCanvas = EnsureWorldCanvas(anchor, "World_Menu_Canvas", mainCamera, menuScale, 10);
        Canvas settingsCanvas = EnsureWorldCanvas(anchor, "World_Settings_Canvas", mainCamera, menuScale, 20);

        // ── 기존 위젯 이사 ────────────────────────────────────────────────
        Canvas overlayCanvas = FindRootCanvas();
        Button start = FindButton("Start_Button");
        Button option = FindButton("Option_Button");
        Button exit = FindButton("Exit_Button");
        Button close = FindButton("Close_Button");

        GameObject menuRoot = EnsureChild(menuCanvas.gameObject, "Menu_Root");
        StretchFull(menuRoot);
        Reparent(start, menuRoot);
        Reparent(option, menuRoot);
        Reparent(exit, menuRoot);

        GameObject optionPanel = FindInScene("Option_Panel");
        if (optionPanel != null)
        {
            optionPanel.transform.SetParent(settingsCanvas.transform, false);
            StretchFull(optionPanel);
            // 🔴 지금까지 마스크가 없어서 모니터 경계 밖으로 삐져나가도 안 잘렸다.
            EnsureComponent<RectMask2D>(optionPanel);

            // 🔴 Option_Panel 은 켜 둔다. 표시 여부는 월드 캔버스(_settingsRoot) 를 켜고 끄는 것으로 정한다.
            // 여기서 꺼 두면 Director 가 캔버스를 켜도 안쪽이 꺼진 채라 설정창이 안 보인다.
            optionPanel.SetActive(true);
        }

        // ── PRESS ANY KEY / 스킵 안내 ─────────────────────────────────────
        // 🔴 PRESS ANY KEY 는 모니터 안이 아니라 **화면 앞 오버레이**(계획서 §0.2-1, 09-23 팀장).
        // 월드 캔버스에 두면 오피스 아트의 모니터 메시에 가려 안 보인다. 기존 인스턴스가 있으면 옮긴다.
        GameObject pressParent = overlayCanvas != null ? overlayCanvas.gameObject : menuCanvas.gameObject;
        GameObject pressAnyKey = FindInScene("PressAnyKey_Root");
        if (pressAnyKey == null)
            pressAnyKey = EnsureChild(pressParent, "PressAnyKey_Root");
        else
            pressAnyKey.transform.SetParent(pressParent.transform, false);
        StretchFull(pressAnyKey);

        // 페이드 이미지보다 아래 — 암전이 글자를 덮어야 한다.
        Transform fade = pressParent.transform.Find("Fade_Image");
        if (fade != null)
            pressAnyKey.transform.SetSiblingIndex(fade.GetSiblingIndex());

        TextMeshProUGUI pressText = EnsureLabel(pressAnyKey, "PressAnyKey_Text", "PRESS ANY KEY",
            56, TextAlignmentOptions.Center);
        var pressRt = (RectTransform)pressText.transform;
        pressRt.anchorMin = new Vector2(0f, 0.10f); // 화면 하단 1/4 띠 — 중앙 CRT 를 가리지 않는다
        pressRt.anchorMax = new Vector2(1f, 0.22f);
        pressRt.offsetMin = Vector2.zero;
        pressRt.offsetMax = Vector2.zero;
        EnsureComponent<BlinkingText>(pressText.gameObject);

        GameObject skipHint = null;
        if (overlayCanvas != null)
        {
            skipHint = EnsureChild(overlayCanvas.gameObject, "SkipHint_Root");
            StretchFull(skipHint);
            TextMeshProUGUI skipText = EnsureLabel(skipHint, "SkipHint_Text", "ESC  SKIP",
                28, TextAlignmentOptions.BottomRight);
            var rt = (RectTransform)skipText.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-48f, 40f);
            rt.sizeDelta = new Vector2(420f, 48f);
            skipHint.SetActive(false);

            // 3D 배경이 생겼으므로 옛 평면 타이틀 이미지는 끈다.
            SetActiveIfFound("background", false);
            SetActiveIfFound("title", false);
        }

        // ── CRT FX ────────────────────────────────────────────────────────
        GameObject fxRoot = EnsureRoot("TitleFX");
        RetroCRTController crt = EnsureComponent<RetroCRTController>(fxRoot);
        CrtFxDriver burstFx = EnsureFxDriver(fxRoot, "CrtFx_Burst", crt, BuildGlitchBurst());
        CrtFxDriver sustainFx = EnsureFxDriver(fxRoot, "CrtFx_Sustain", crt, BuildApproachSustain());

        // ── Director ──────────────────────────────────────────────────────
        GameObject directorGo = EnsureRoot("TitleFlowDirector");
        TitleFlowDirector director = EnsureComponent<TitleFlowDirector>(directorGo);
        var manager = Object.FindAnyObjectByType<TitleSceneManager>(FindObjectsInactive.Include);

        var so = new SerializedObject(director);
        Set(so, "_brain", mainCamera.GetComponent<CinemachineBrain>());
        Set(so, "_vcamFar", far);
        Set(so, "_vcamNear", near);
        Set(so, "_vcamCloseup", closeup);
        Set(so, "_pressAnyKeyRoot", pressAnyKey);
        Set(so, "_menuRoot", menuRoot);
        Set(so, "_settingsRoot", settingsCanvas.gameObject);
        Set(so, "_skipHintRoot", skipHint);
        Set(so, "_startButton", start);
        Set(so, "_optionButton", option);
        Set(so, "_exitButton", exit);
        Set(so, "_settingsCloseButton", close);
        Set(so, "_sceneManager", manager);
        Set(so, "_burstFx", burstFx);
        Set(so, "_sustainFx", sustainFx);
        so.FindProperty("_approachDuration").floatValue = 2f;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (manager != null)
        {
            var mso = new SerializedObject(manager);
            Set(mso, "flowDirector", director);
            mso.ApplyModifiedPropertiesWithoutUndo();
        }

        // 🔴 씬에 박힌 영구 콜백을 지운다. 남겨 두면 Director 의 상태 검사를 우회해
        // 연출 중에도 StartGame/OpenOption 이 그대로 실행된다(계획서 §3.1).
        int cleared = 0;
        cleared += ClearPersistentCalls(start);
        cleared += ClearPersistentCalls(option);
        cleared += ClearPersistentCalls(exit);
        cleared += ClearPersistentCalls(close);

        // 아트 씬은 스카이박스가 없다(ambient 가 달라진다). 맞춰 준다.
        RenderSettings.skybox = null;

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[TitleRig] 조립 완료. 앵커={anchorPos} 클로즈업={closeupPos} " +
                  $"캔버스배율={menuScale:F6} 영구콜백제거={cleared}건");
    }

    // ── 버스트 정의 ───────────────────────────────────────────────────────

    // 키 입력 순간. 레퍼런스 영상의 '가로 찢김 + RGB 분리' 를 가진 것만으로 흉내 낸 1차 버전.
    static List<CrtFxDriver.Channel> BuildGlitchBurst() => new()
    {
        Channel(CrtParam.RgbStripeStrength, 0.15f, 0.9f, Spike()),
        Channel(CrtParam.ScanlineStrength, 0.08f, 0.35f, Spike()),
        // 🔴 머티리얼의 STATIC 키워드가 꺼져 있으면 이 값은 화면에 안 나온다.
        // 타이틀용 머티리얼 복제본에 키워드를 켜고 RetroCRTController 에 꽂을 것.
        Channel(CrtParam.StaticStrength, 0f, 0.6f, Spike()),
    };

    // 접근하는 동안 진행도로 왜곡을 끌어올린다.
    static List<CrtFxDriver.Channel> BuildApproachSustain() => new()
    {
        Channel(CrtParam.WarpStrength, 0.035f, 0.075f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f)),
        Channel(CrtParam.ScanlineStrength, 0.08f, 0.16f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f)),
    };

    static AnimationCurve Spike() => new(new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f));

    static CrtFxDriver.Channel Channel(CrtParam p, float from, float to, AnimationCurve curve) =>
        new() { param = p, from = from, to = to, curve = curve };

    // ── 유틸 ──────────────────────────────────────────────────────────────

    static CinemachineCamera EnsureVcam(string name, Vector3 pos, int priority)
    {
        GameObject go = EnsureRoot(name);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(CamEuler));
        CinemachineCamera cam = EnsureComponent<CinemachineCamera>(go);

        LensSettings lens = cam.Lens;
        lens.FieldOfView = Fov;
        cam.Lens = lens;
        cam.Priority = priority;
        return cam;
    }

    static Canvas EnsureWorldCanvas(GameObject parent, string name, Camera cam, float scale, int sortingOrder)
    {
        GameObject go = EnsureChild(parent, name);
        Canvas canvas = EnsureComponent<Canvas>(go);
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        canvas.sortingOrder = sortingOrder;
        EnsureComponent<GraphicRaycaster>(go);

        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(CanvasLogicalWidth, CanvasLogicalHeight);
        rt.localPosition = Vector3.zero;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one * scale;
        return canvas;
    }

    static TextMeshProUGUI EnsureLabel(GameObject parent, string name, string text, float size,
        TextAlignmentOptions align)
    {
        GameObject go = EnsureChild(parent, name);
        var label = EnsureComponent<TextMeshProUGUI>(go);
        // 이미 누가 문구를 고쳤으면 존중한다. 처음 만들 때만 채운다.
        if (string.IsNullOrEmpty(label.text))
            label.text = text;

        label.fontSize = size;
        label.alignment = align;
        StretchFull(go);
        return label;
    }

    static void StretchFull(GameObject go)
    {
        if (go.transform is not RectTransform rt)
            return;

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    static void Reparent(Component c, GameObject parent)
    {
        if (c != null)
            c.transform.SetParent(parent.transform, false);
    }

    static int ClearPersistentCalls(Button button)
    {
        if (button == null)
            return 0;

        var so = new SerializedObject(button);
        SerializedProperty calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
        if (calls == null || calls.arraySize == 0)
            return 0;

        int n = calls.arraySize;
        calls.ClearArray();
        so.ApplyModifiedPropertiesWithoutUndo();
        return n;
    }

    static void Set(SerializedObject so, string field, Object value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogWarning($"[TitleRig] 필드 '{field}' 를 찾지 못했다. 이름이 바뀌었는지 확인할 것.");
            return;
        }

        p.objectReferenceValue = value;
    }

    static GameObject EnsureRoot(string name)
    {
        GameObject found = FindInScene(name);
        return found != null ? found : new GameObject(name);
    }

    static GameObject EnsureChild(GameObject parent, string name)
    {
        Transform t = parent.transform.Find(name);
        if (t != null)
            return t.gameObject;

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static CrtFxDriver EnsureFxDriver(GameObject parent, string name, RetroCRTController controller,
        List<CrtFxDriver.Channel> channels)
    {
        GameObject go = parent.transform.Find(name)?.gameObject;
        if (go == null)
        {
            go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
        }

        CrtFxDriver driver = EnsureComponent<CrtFxDriver>(go);
        string id = name == "CrtFx_Burst" ? "glitch" : "approach";

        var so = new SerializedObject(driver);
        Set(so, "_controller", controller);

        SerializedProperty bursts = so.FindProperty("_bursts");
        bursts.arraySize = 1;
        SerializedProperty burst = bursts.GetArrayElementAtIndex(0);
        burst.FindPropertyRelative("id").stringValue = id;
        burst.FindPropertyRelative("duration").floatValue = id == "glitch" ? 0.35f : 1f;
        burst.FindPropertyRelative("unscaledTime").boolValue = true;

        SerializedProperty ch = burst.FindPropertyRelative("channels");
        ch.arraySize = channels.Count;
        for (var i = 0; i < channels.Count; i++)
        {
            SerializedProperty e = ch.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("param").enumValueIndex = (int)channels[i].param;
            e.FindPropertyRelative("from").floatValue = channels[i].from;
            e.FindPropertyRelative("to").floatValue = channels[i].to;
            e.FindPropertyRelative("curve").animationCurveValue = channels[i].curve;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        return driver;
    }

    static Canvas FindRootCanvas()
    {
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c.name == "Canvas")
                return c;
        }

        return null;
    }

    static Button FindButton(string name)
    {
        GameObject go = FindInScene(name);
        return go != null ? go.GetComponent<Button>() : null;
    }

    static void SetActiveIfFound(string name, bool active)
    {
        GameObject go = FindInScene(name);
        if (go != null)
            go.SetActive(active);
    }

    static GameObject FindInScene(string name)
    {
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            Transform t = FindRecursive(root.transform, name);
            if (t != null)
                return t.gameObject;
        }

        return null;
    }

    static Transform FindRecursive(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;

        for (var i = 0; i < parent.childCount; i++)
        {
            Transform found = FindRecursive(parent.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
