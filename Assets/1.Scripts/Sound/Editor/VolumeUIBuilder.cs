#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Ami.BroAudio;

/// <summary>
/// 볼륨 조절 UI(Canvas + Master/BGM/SFX 슬라이더 + TMP 라벨 + VolumeSlider 배선)를 현재 씬에 생성하는 에디터 도구.
/// uGUI 생성 메뉴가 외부 자동화로는 실행되지 않아, DefaultControls 정식 API로 슬라이더를 구성한다.
/// 재실행 시 기존 VolumeCanvas를 지우고 새로 만들어 중복을 막는다(멱등).
/// 메뉴: Tools > Sound > Build Volume UI
/// </summary>
public static class VolumeUIBuilder
{
    // 프로젝트 한글 대응 폰트(없으면 TMP 기본 폰트로 폴백)
    private const string KrFontPath = "Assets/Resources/NotoSansKR-VariableFont_wght SDF.asset";

    [MenuItem("Tools/Sound/Build Volume UI")]
    public static void Build()
    {
        // 재실행 대비: 기존 VolumeCanvas 제거(중복 방지)
        var existing = GameObject.Find("VolumeCanvas");
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        // 1) Canvas
        var canvasGO = new GameObject("VolumeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // 2) EventSystem (Input System 패키지 사용 프로젝트 → 해당 모듈 우선, 없으면 레거시)
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem", typeof(EventSystem));
            var moduleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null)
            {
                esGO.AddComponent(moduleType);
            }
            else
            {
                esGO.AddComponent<StandaloneInputModule>();
            }
        }

        // 라벨 폰트 준비 (한글 대응 → 없으면 TMP 기본)
        var labelFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KrFontPath);
        if (labelFont == null)
        {
            labelFont = TMP_Settings.defaultFontAsset;
        }

        // 3) 슬라이더 3종 (마스터 / BGM(Music) / SFX)
        CreateVolumeSlider(canvasGO.transform, "MasterVolume", "Master", new Vector2(0f, 120f), true, BroAudioType.All, labelFont);
        CreateVolumeSlider(canvasGO.transform, "BGMVolume", "BGM", new Vector2(0f, 40f), false, BroAudioType.Music, labelFont);
        CreateVolumeSlider(canvasGO.transform, "SFXVolume", "SFX", new Vector2(0f, -40f), false, BroAudioType.SFX, labelFont);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[VolumeUIBuilder] Volume UI 생성 완료 (VolumeCanvas + Master/BGM/SFX + TMP 라벨).");
    }

    private static void CreateVolumeSlider(Transform parent, string name, string labelText, Vector2 anchoredPos, bool isMaster, BroAudioType type, TMP_FontAsset labelFont)
    {
        var res = new DefaultControls.Resources();
        var go = DefaultControls.CreateSlider(res);
        go.name = name;
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;

        var slider = go.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;

        // TMP 라벨 — 슬라이더 위쪽에 배치
        var labelGO = new GameObject(name + "Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, false);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = labelText;
        if (labelFont != null)
        {
            tmp.font = labelFont;
        }
        tmp.fontSize = 18f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.BottomLeft;
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchoredPosition = new Vector2(0f, 20f);
        lrt.sizeDelta = new Vector2(160f, 24f);

        var vs = go.AddComponent<VolumeSlider>();
        var so = new SerializedObject(vs);
        so.FindProperty("isMaster").boolValue = isMaster;
        so.FindProperty("audioType").intValue = (int)type;
        so.FindProperty("applyOnEnable").boolValue = true;
        so.FindProperty("persist").boolValue = true;
        so.ApplyModifiedProperties();
    }
}
#endif
