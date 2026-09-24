using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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

    // 설정창(1920 폭)을 모니터 RT(1536 폭) 안에 넣는 배율. 3단계에서 가독성 기준(≥14px)으로 레이아웃을 다시 잡는다.
    const float SettingsFitScale = 0.78f;

    const int UILayer = 5; // 내장 "UI" 레이어 — TagManager 를 건드리지 않으려고 전용 레이어 대신 쓴다
    const string ScreenRendererPath = "TitleOffice/monitor/monitor_screen"; // 🔴 중앙 1대. 벽 모니터도 이름이 monitor_screen
    const string UIMaterialPath = "Assets/3.Materials/title/MA_TitleMonitorUI.mat";
    const string PressMaterialPath = "Assets/3.Materials/title/MA_TitlePressCRT.mat";
    const string OffMaterialPath = "Assets/3.Materials/title/MA_TitleCRTOff.mat";
    const string WallMaterialPath = "Assets/3.Materials/title/MA_TitleWallFeed.mat";
    const string LogoTexturePath = "Assets/50.Art/Environment/Textures/Props/Office/monitor_screen.png"; // Re:C (SVN)
    static readonly string[] WallSourceMaterials =
    {
        "Assets/3.Materials/Environment/NotUsedInMap/Props/Office/MA_monitor_screen glitch.mat",
        "Assets/3.Materials/Environment/NotUsedInMap/Props/Office/MA_monitor_screen non.mat",
    };

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

        // ── 모니터 UI = 렌더텍스처 (계획서 §0.4-A, 2026-09-23 3판) ──────────
        // 🔴 예전엔 CRT_Anchor 밑 월드 캔버스 2개였다. 아트 오피스가 들어오자 모니터 메시 뒤에 가려 안 보였고,
        //    GraphicRaycaster 는 3D 가림을 몰라 "모니터 아무 데나 누르면 Start" 가 됐다. 이제 UI 카메라가 RT 에 그리고
        //    TitleScreenRaycaster 가 화면 UV 로만 클릭을 받는다. CRT_Anchor 는 클로즈업 위치 계산용으로만 남는다.
        Camera uiCam = EnsureUICamera(mainCamera);
        Canvas monitorCanvas = EnsureMonitorCanvas(uiCam);

        // 메인 카메라는 UI 레이어를 안 본다 — UI 는 RT 로만 보인다(오버레이 캔버스는 레이어와 무관).
        mainCamera.cullingMask &= ~(1 << UILayer);
        EditorUtility.SetDirty(mainCamera);

        // ── 기존 위젯 이사 ────────────────────────────────────────────────
        Canvas overlayCanvas = FindRootCanvas();
        Button start = FindButton("Start_Button");
        Button option = FindButton("Option_Button");
        Button exit = FindButton("Exit_Button");
        Button close = FindButton("Close_Button");

        GameObject menuRoot = FindInScene("Menu_Root") ?? EnsureChild(monitorCanvas.gameObject, "Menu_Root");
        menuRoot.transform.SetParent(monitorCanvas.transform, false);
        StretchFull(menuRoot);
        Reparent(start, menuRoot);
        Reparent(option, menuRoot);
        Reparent(exit, menuRoot);
        LayoutMonitorMenu(start, option, exit);

        GameObject settingsRoot = EnsureChild(monitorCanvas.gameObject, "Settings_Root");
        StretchFull(settingsRoot);
        GameObject optionPanel = FindInScene("Option_Panel");
        if (optionPanel != null)
        {
            optionPanel.transform.SetParent(settingsRoot.transform, false);
            // 설정창 내부는 1920×1080 고정 좌표. 모니터 RT 는 세로 1080 기준 폭 1536(화면 비율 1.42)이라
            // 늘리면(Stretch) 좌우가 잘린다 → 논리 크기를 유지한 채 가운데 기준으로 폭에 맞춰 축소한다.
            var panelRt = (RectTransform)optionPanel.transform;
            panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(CanvasLogicalWidth, CanvasLogicalHeight);
            panelRt.localScale = Vector3.one * SettingsFitScale;
            EnsureComponent<RectMask2D>(optionPanel);
            // 표시 여부는 Settings_Root 로 정한다. 안쪽을 꺼 두면 루트를 켜도 안 보인다.
            optionPanel.SetActive(true);
            int swapped = SwapSlidersToScreenSlider(optionPanel);
            if (swapped > 0) Debug.Log($"[TitleRig] 슬라이더 {swapped}개 → TitleScreenSlider");
        }

        SetLayerRecursive(monitorCanvas.gameObject, UILayer);

        TitleMonitorDisplay monitorDisplay = EnsureMonitorDisplay(uiCam);

        // ── PRESS ANY KEY (화면 앞 오버레이, CRT 룩) ─────────────────────────
        // 🔴 모니터 안이 아니라 화면 앞(계획서 §0.2-1). 단순 깜빡임이 아니라 "모니터에 그려지는" 룩(팀장 09-23):
        //    전용 카메라가 소형 RT 에 글자를 그리고 → 오버레이 RawImage 가 Title/CRTUI 로 보여 준다(TitleOverlayCrtImage).
        GameObject pressParent = overlayCanvas != null ? overlayCanvas.gameObject : monitorCanvas.gameObject;
        GameObject pressAnyKey = FindInScene("PressAnyKey_Root");
        if (pressAnyKey == null)
            pressAnyKey = EnsureChild(pressParent, "PressAnyKey_Root");
        else
            pressAnyKey.transform.SetParent(pressParent.transform, false);
        StretchFull(pressAnyKey);

        Transform fade = pressParent.transform.Find("Fade_Image");
        if (fade != null)
            pressAnyKey.transform.SetSiblingIndex(fade.GetSiblingIndex()); // 암전이 글자를 덮는다

        Camera pressCam = EnsurePressCamera(mainCamera);
        Canvas pressCanvas = EnsurePressCanvas(pressCam);

        GameObject pressTextGo = FindInScene("PressAnyKey_Text");
        TextMeshProUGUI pressText = pressTextGo != null ? pressTextGo.GetComponent<TextMeshProUGUI>() : null;
        if (pressText == null)
            pressText = EnsureLabel(pressCanvas.gameObject, "PressAnyKey_Text", "PRESS ANY KEY", 120, TextAlignmentOptions.Center);
        pressText.transform.SetParent(pressCanvas.transform, false);
        pressText.fontSize = 120;
        pressText.alignment = TextAlignmentOptions.Center;
        pressText.textWrappingMode = TextWrappingModes.NoWrap;
        pressText.color = Color.white;
        StretchFull(pressText.gameObject);
        var blink = pressText.GetComponent<BlinkingText>();
        if (blink != null) Object.DestroyImmediate(blink); // 알파 0.15↔1 구형파는 요구와 다르다 — 셰이더 밝기 변조로 대체
        var pressScramble = EnsureComponent<TextScramble>(pressText.gameObject);
        SetFloat(pressScramble, "_duration", 1.0f);
        SetLayerRecursive(pressCanvas.gameObject, UILayer);

        GameObject pressScreen = EnsureChild(pressAnyKey, "PressAnyKey_Screen");
        var pressImage = EnsureComponent<RawImage>(pressScreen);
        var pressRt = (RectTransform)pressScreen.transform;
        pressRt.anchorMin = pressRt.anchorMax = pressRt.pivot = new Vector2(0.5f, 0.16f); // 화면 하단 — 중앙 CRT 를 가리지 않는다
        pressRt.anchoredPosition = Vector2.zero;
        pressRt.sizeDelta = new Vector2(760f, 190f); // 소스 RT 1024×256 과 같은 4:1
        var overlayImg = EnsureComponent<TitleOverlayCrtImage>(pressScreen);
        var oso = new SerializedObject(overlayImg);
        Set(oso, "_sourceCamera", pressCam);
        Set(oso, "_sourceCanvas", pressCanvas.gameObject);
        Set(oso, "_materialSource", EnsureMaterial(PressMaterialPath, "Title/CRTUI"));
        oso.ApplyModifiedPropertiesWithoutUndo();
        pressImage.raycastTarget = false;

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

        // 비게 된(PRESS ANY KEY 까지 옮긴 뒤) 옛 월드 캔버스 정리.
        DestroyIfFound("World_Menu_Canvas");
        DestroyIfFound("World_Settings_Canvas");

        // ── CRT FX ────────────────────────────────────────────────────────
        GameObject fxRoot = EnsureRoot("TitleFX");
        RetroCRTController crt = EnsureComponent<RetroCRTController>(fxRoot);
        CrtFxDriver burstFx = EnsureFxDriver(fxRoot, "CrtFx_Burst", crt, BuildGlitchBurst());
        CrtFxDriver sustainFx = EnsureFxDriver(fxRoot, "CrtFx_Sustain", crt, BuildApproachSustain());

        // ── 타이틀 CRT 연출 (상시·Flow·꺼짐) ────────────────────────────────
        GameObject titleFx = EnsureRoot("TitleCrtFx");
        TitleCrtFx crtFx = EnsureComponent<TitleCrtFx>(titleFx);
        // Flow(TitleFlowFx) 는 넣었다가 뺐다(팀장 09-23) — 스크립트를 지워 남은 missing script 를 정리한다.
        int missing = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(titleFx);
        if (missing > 0) Debug.Log($"[TitleRig] TitleCrtFx 의 누락 스크립트 {missing}개 제거");

        TitlePowerOff powerOff = EnsureComponent<TitlePowerOff>(titleFx);
        GameObject coverGo = overlayCanvas != null ? EnsureChild(overlayCanvas.gameObject, "PowerOff_Cover") : null;
        if (coverGo != null)
        {
            StretchFull(coverGo);
            var cover = EnsureComponent<RawImage>(coverGo);
            cover.color = Color.white;
            cover.raycastTarget = true; // 꺼지는 동안 뒤 UI 클릭 차단
            coverGo.transform.SetAsLastSibling();
            coverGo.SetActive(false);
            var pso = new SerializedObject(powerOff);
            Set(pso, "_cover", cover);
            Set(pso, "_offMaterialSource", EnsureMaterial(OffMaterialPath, "Title/CRTOff"));
            var flip = pso.FindProperty("_flipY");
            if (flip != null) flip.boolValue = true;
            pso.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── 벽 모니터 13대 = 지금 화면 반복(계획서 §0.5) ────────────────────
        GameObject feedGo = EnsureRoot("TitleScreenFeed");
        var feed = EnsureComponent<TitleScreenFeed>(feedGo);
        var feso = new SerializedObject(feed);
        Set(feso, "_targetCamera", mainCamera);
        Set(feso, "_wallMaterialSource", EnsureWallMaterial());
        Renderer[] walls = FindWallScreens();
        SerializedProperty wallsProp = feso.FindProperty("_wallScreens");
        wallsProp.arraySize = walls.Length;
        for (int i = 0; i < walls.Length; i++) wallsProp.GetArrayElementAtIndex(i).objectReferenceValue = walls[i];
        feso.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"[TitleRig] 벽 모니터 피드 대상 {walls.Length}대");

        // 메뉴 버튼 라벨도 등장할 때 스크램블 디코드
        foreach (Button b in new[] { start, option, exit })
        {
            var label = b != null ? b.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (label == null) continue;
            var sc = EnsureComponent<TextScramble>(label.gameObject);
            SetFloat(sc, "_duration", 0.45f);
        }

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
        Set(so, "_settingsRoot", settingsRoot);
        Set(so, "_monitorDisplay", monitorDisplay);
        Set(so, "_crtFx", crtFx);
        Set(so, "_powerOff", powerOff);
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
                  $"모니터표시={(monitorDisplay != null)} 영구콜백제거={cleared}건");
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

    /// <summary>
    /// 모니터 안 메뉴 = 가운데 세로 3단 텍스트 버튼(START / SETTING / EXIT, 계획서 §0.2-2).
    /// 옛 평면 UI 는 Start 가 늘어나는 앵커(폭 −1620), Setting·Exit 가 모서리 60×60 아이콘이라 RT 안에서 깨졌다.
    /// </summary>
    static void LayoutMonitorMenu(Button start, Button option, Button exit)
    {
        TMP_FontAsset font = start != null ? start.GetComponentInChildren<TextMeshProUGUI>(true)?.font : null;
        (Button b, string label, float y)[] rows =
        {
            (start, "START", 170f),
            (option, "SETTING", 0f),
            (exit, "EXIT", -170f),
        };

        foreach (var (b, label, y) in rows)
        {
            if (b == null) continue;
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y - 60f); // 화면 중심보다 조금 아래 — 곡면 위쪽 가장자리를 피한다
            rt.sizeDelta = new Vector2(600f, 120f);
            rt.localScale = Vector3.one;

            // 배경은 투명하게 두되 클릭은 받는다(alpha 0 이어도 raycastTarget 이면 판정된다).
            var img = b.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = null;
                img.color = new Color(1f, 1f, 1f, 0f);
                img.raycastTarget = true;
            }

            TextMeshProUGUI text = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text == null)
            {
                var go = new GameObject("Text (TMP)", typeof(RectTransform));
                go.transform.SetParent(b.transform, false);
                text = go.AddComponent<TextMeshProUGUI>();
            }
            text.text = label;
            if (font != null) text.font = font;
            text.fontSize = 72f;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            StretchFull(text.gameObject);

            // 호버·선택 색은 글자에 건다(배경이 투명이라 배경 틴트는 안 보인다).
            b.targetGraphic = text;
            ColorBlock cb = b.colors;
            cb.normalColor = new Color(0.78f, 0.9f, 0.85f, 1f);
            cb.highlightedColor = Color.white;
            cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.55f, 1f, 0.8f, 1f);
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            EditorUtility.SetDirty(b);
        }
    }

    // ── 모니터 RT 리그 ────────────────────────────────────────────────────

    static Camera EnsureUICamera(Camera main)
    {
        GameObject go = EnsureRoot("TitleUICam");
        go.layer = UILayer;
        Camera cam = EnsureComponent<Camera>(go);
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.cullingMask = 1 << UILayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 10f;
        cam.depth = main.depth - 1f; // 메인보다 먼저 — 같은 프레임에 RT 가 채워진 뒤 모니터가 그린다
        cam.targetTexture = null;    // 런타임에 TitleMonitorDisplay 가 RT 를 꽂는다(애셋 RT 를 만들지 않는다)
        go.transform.SetPositionAndRotation(new Vector3(0f, -50f, 0f), Quaternion.identity); // 오피스와 겹치지 않게 멀리

        var data = EnsureComponent<UniversalAdditionalCameraData>(go);
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;
        EditorUtility.SetDirty(cam);
        EditorUtility.SetDirty(data);

        // AudioListener·CinemachineBrain 은 붙이지 않는다.
        var listener = go.GetComponent<AudioListener>();
        if (listener != null) Object.DestroyImmediate(listener);
        return cam;
    }

    static Canvas EnsureMonitorCanvas(Camera uiCam)
    {
        GameObject go = EnsureRoot("Monitor_UI_Canvas");
        go.layer = UILayer;
        Canvas canvas = EnsureComponent<Canvas>(go);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCam;
        canvas.planeDistance = 1f;

        var scaler = EnsureComponent<CanvasScaler>(go);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(CanvasLogicalWidth, CanvasLogicalHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f; // 세로 1080 기준 — RT 가 4:3 이어도 글자 크기가 유지된다

        // 기본 GraphicRaycaster 는 실제 화면 좌표로 판정해 모니터 밖에서도 버튼이 눌린다 → 교체.
        var plain = go.GetComponent<GraphicRaycaster>();
        if (plain != null && plain is not TitleScreenRaycaster) Object.DestroyImmediate(plain);
        EnsureComponent<TitleScreenRaycaster>(go);
        return canvas;
    }

    static TitleMonitorDisplay EnsureMonitorDisplay(Camera uiCam)
    {
        GameObject go = EnsureRoot("TitleMonitorDisplay");
        var display = EnsureComponent<TitleMonitorDisplay>(go);

        GameObject screenGo = GameObject.Find(ScreenRendererPath);
        Renderer screen = screenGo != null ? screenGo.GetComponent<Renderer>() : null;
        if (screen == null)
            Debug.LogError($"[TitleRig] 중앙 화면 '{ScreenRendererPath}' 를 못 찾았다 — TitleOffice 이식 여부 확인.");

        var so = new SerializedObject(display);
        Set(so, "_screenRenderer", screen);
        Set(so, "_uiCamera", uiCam);
        Set(so, "_uiMaterialSource", EnsureUIMaterial());
        // Idle 로고를 CRT 셰이더로 — 원본 화면 머티리얼은 유리 반사에 스카이박스가 비쳤다(팀장 09-23).
        Set(so, "_logoTexture", AssetDatabase.LoadAssetAtPath<Texture>(LogoTexturePath));
        so.ApplyModifiedPropertiesWithoutUndo();
        return display;
    }

    static Material EnsureUIMaterial() => EnsureMaterial(UIMaterialPath, "Title/CRTScreen");

    /// <summary>애셋 머티리얼을 찾거나 만들고, 셰이더가 다르면 맞춘다(URP Unlit → CRTScreen 교체 포함).</summary>
    static Material EnsureMaterial(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[TitleRig] 셰이더 '{shaderName}' 를 못 찾았다 — 컴파일 에러 확인.");
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
            EditorUtility.SetDirty(mat);
        }
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>원본 머티리얼(glitch/non)을 쓰는 화면 렌더러 = 벽 모니터. 중앙 화면은 이름이 같아도 머티리얼이 다르다.</summary>
    static Renderer[] FindWallScreens()
    {
        var sources = new HashSet<Material>();
        foreach (string path in WallSourceMaterials)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) sources.Add(m);
        }

        var result = new List<Renderer>();
        GameObject office = GameObject.Find("TitleOffice");
        if (office == null) return result.ToArray();
        foreach (Renderer r in office.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material m in r.sharedMaterials)
                if (m != null && sources.Contains(m)) { result.Add(r); break; }
        }
        return result.ToArray();
    }

    static Material EnsureWallMaterial()
    {
        Material mat = EnsureMaterial(WallMaterialPath, "Title/CRTScreen");
        if (mat == null) return null;
        // 🔴 재귀마다 곱해진다 — (1 + 글로우) × 밝기 가 1 을 넘으면 몇 프레임 만에 하얗게 탄다.
        mat.SetFloat("_Brightness", 0.8f);
        mat.SetFloat("_Glow", 0.12f);
        mat.SetFloat("_ChromaPx", 1.5f);
        mat.SetFloat("_ScanStrength", 0.35f);
        mat.SetFloat("_ScanCount", 220f);
        mat.SetFloat("_Vignette", 0.9f);
        mat.SetColor("_Tint", new Color(0.88f, 1f, 0.95f, 1f));
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static Camera EnsurePressCamera(Camera main)
    {
        GameObject go = EnsureRoot("TitlePressCam");
        go.layer = UILayer;
        Camera cam = EnsureComponent<Camera>(go);
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.cullingMask = 1 << UILayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.clear;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 10f;
        cam.depth = main.depth - 2f;
        cam.targetTexture = null; // 런타임에 TitleOverlayCrtImage 가 RT 를 꽂는다
        // 🔴 모니터 UI 카메라(y −50)와 떨어뜨린다 — Screen Space-Camera 캔버스가 서로의 카메라에 잡히지 않게.
        go.transform.SetPositionAndRotation(new Vector3(0f, -100f, 0f), Quaternion.identity);

        var data = EnsureComponent<UniversalAdditionalCameraData>(go);
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;
        EditorUtility.SetDirty(cam);
        EditorUtility.SetDirty(data);
        var listener = go.GetComponent<AudioListener>();
        if (listener != null) Object.DestroyImmediate(listener);
        return cam;
    }

    static Canvas EnsurePressCanvas(Camera pressCam)
    {
        GameObject go = EnsureRoot("Press_UI_Canvas");
        go.layer = UILayer;
        Canvas canvas = EnsureComponent<Canvas>(go);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = pressCam;
        canvas.planeDistance = 1f;
        var scaler = EnsureComponent<CanvasScaler>(go);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1024f, 256f);
        scaler.matchWidthOrHeight = 1f;
        var ray = go.GetComponent<GraphicRaycaster>();
        if (ray != null) Object.DestroyImmediate(ray); // 클릭 받을 것이 없다
        return canvas;
    }

    static void SetFloat(Object target, string field, float value)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"[TitleRig] 필드 '{field}' 없음 ({target.GetType().Name})"); return; }
        p.floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Slider 의 m_Script 만 TitleScreenSlider 로 바꾼다 — 필드가 같아 값·참조가 보존된다.</summary>
    static int SwapSlidersToScreenSlider(GameObject root)
    {
        MonoScript target = null;
        foreach (string guid in AssetDatabase.FindAssets("TitleScreenSlider t:MonoScript"))
        {
            var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (ms != null && ms.GetClass() == typeof(TitleScreenSlider)) { target = ms; break; }
        }
        if (target == null) { Debug.LogError("[TitleRig] TitleScreenSlider 스크립트를 못 찾았다."); return 0; }

        int n = 0;
        foreach (Slider slider in root.GetComponentsInChildren<Slider>(true))
        {
            if (slider is TitleScreenSlider) continue;
            var so = new SerializedObject(slider);
            so.FindProperty("m_Script").objectReferenceValue = target;
            so.ApplyModifiedPropertiesWithoutUndo();
            n++;
        }
        return n;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    static void DestroyIfFound(string name)
    {
        GameObject go = FindInScene(name);
        if (go == null) return;
        if (go.GetComponentsInChildren<Button>(true).Length > 0 || go.GetComponentsInChildren<Slider>(true).Length > 0)
        {
            Debug.LogWarning($"[TitleRig] '{name}' 에 아직 버튼/슬라이더가 남아 지우지 않는다 — 이사 누락 확인.");
            return;
        }
        Object.DestroyImmediate(go);
    }

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
