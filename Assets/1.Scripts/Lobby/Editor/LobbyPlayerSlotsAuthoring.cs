using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 상단 플레이어 슬롯 3칸을 아트 A안(할 일 276)으로 다시 조립한다 — 3.LobbyScene 의 UI/Canvas/Panel_PlayerSlots.
/// 원본 = SVN <c>Assets/50.Art/UI/Lobby/Unity/Lobby_A/assembly.json</c>(초상화 중심 (0,0) 기준 디자인 단위, 부품 피벗 가운데).
/// 부품은 디자인 단위 그대로 놓고 부모 Panel_PlayerSlots 만 한 번 줄인다(README: 1920×1080 = 0.6788173913) — 다른 해상도는 ScreenFitScale(283 후속).
/// 슬롯 자식은 지우고 새로 만든다(슬롯 GameObject·LobbyPlayerSlotView·LobbyUIController.slots 연결은 유지).
/// 문서: Docs/tech/lobby-player-slots.md
/// </summary>
public static class LobbyPlayerSlotsAuthoring
{
    const string ScenePath = "Assets/0.Scenes/MainFlow/3.LobbyScene.unity";
    const string ArtDir = "Assets/50.Art/UI/Lobby/Unity/Lobby_A/";
    const string HeavyFontPath = "Assets/50.Art/UI/Fonts/SeoulAlrimTTF-Heavy SDF.asset";
    // 이름 줄 = 캐릭터 이름 — Heavy 정적 에셋엔 '팔·딘·쌔' 가 없어 기본 UI 글꼴(Medium 정적, 한글 2350자)로 쓴다.
    const string NameFontPath = "Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset";

    public const float RootScale = 0.6788173913f;     // README — 1920×1080 기준 부모 스케일
    public const float SlotSpacing = 245f;            // 초상화 중심 간격(디자인 단위)
    // 1380×780 목업: 전체 UI 중심 y=74.575(위에서) → 1920 폭 비율(×1920/1380) = 103.76, 초상화 중심 = 그 위 36.5×RootScale
    public const float PortraitCenterFromTop = 103.76f - 36.5f * RootScale;

    [MenuItem("Tools/UI/Authoring/Lobby Player Slots (A안)")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            EditorUtility.DisplayDialog("Lobby Player Slots", $"{ScenePath} 를 열고 다시 실행하세요.", "확인");
            return;
        }
        if (scene.isDirty)
        {
            EditorUtility.DisplayDialog("Lobby Player Slots", "로비 씬에 저장 안 한 변경이 있습니다. 저장하거나 되돌린 뒤 다시 실행하세요(이 메뉴는 끝에 씬을 저장합니다).", "확인");
            return;
        }

        var controller = Object.FindFirstObjectByType<LobbyUIController>(FindObjectsInactive.Include);
        if (controller == null) { Debug.LogError("[LobbySlotsA] LobbyUIController 없음"); return; }

        var art = new Art();
        if (!art.Load()) return;

        // 부모: 화면 위 가운데, 초상화 중심 줄에 피벗, 한 번만 축소
        var panel = (RectTransform)controller.transform;
        panel.gameObject.SetActive(true); // 158 C8 에서 꺼 두었던 상단 슬롯을 A안으로 다시 켠다
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = new Vector2(0f, -PortraitCenterFromTop);
        panel.sizeDelta = new Vector2(SlotSpacing * 2f + 160f, 230f);
        panel.localScale = Vector3.one * RootScale;
        // 283 후속: 위 크기·위치 = 1920×1080 값. 다른 해상도에선 Play 중 ScreenFitScale 이 min(가로/1920, 세로/1080) 배로 맞춘다
        // (로비 Canvas 는 ConstantPixelSize 그대로 — 같은 캔버스의 다른 UI 를 흔들지 않음).
        if (!panel.TryGetComponent<ScreenFitScale>(out _))
            panel.gameObject.AddComponent<ScreenFitScale>();

        var slots = new SerializedObject(controller).FindProperty("slots");
        for (int i = 0; i < slots.arraySize; i++)
        {
            var view = slots.GetArrayElementAtIndex(i).objectReferenceValue as LobbyPlayerSlotView;
            if (view == null) continue;
            BuildSlot(view, (i - 1) * SlotSpacing, art);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[LobbySlotsA] 로비 플레이어 슬롯 A안 조립·저장 완료");
    }

    static void BuildSlot(LobbyPlayerSlotView view, float x, Art art)
    {
        var slot = (RectTransform)view.transform;
        for (int c = slot.childCount - 1; c >= 0; c--)
            Object.DestroyImmediate(slot.GetChild(c).gameObject);

        slot.anchorMin = slot.anchorMax = slot.pivot = new Vector2(0.5f, 0.5f);
        slot.anchoredPosition = new Vector2(x, 0f);
        slot.sizeDelta = new Vector2(160f, 230f);
        slot.localScale = Vector3.one;

        // assembly.json componentsBackToFront 순서
        var frame = NewImage("FrameBase", slot, art.FrameDisconnected, new Vector2(0.402f, -2.4225f), new Vector2(157f, 157f));
        var mask = NewImage("PortraitMask", slot, art.CircleMask, new Vector2(0.402f, -0.4225f), new Vector2(129f, 129f));
        mask.color = new Color32(31, 33, 35, 255);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var portrait = NewImage("CharacterPortrait", mask.rectTransform, null, Vector2.zero, new Vector2(129f, 129f));
        portrait.preserveAspect = true;
        portrait.enabled = false;
        var readyRing = NewImage("ReadyRing", slot, art.ReadyRing, new Vector2(-0.098f, 0.0775f), new Vector2(140f, 140f));
        var dashed = NewImage("EmptyDashedRing", slot, art.DashedRing, new Vector2(-0.098f, 0.0775f), new Vector2(128f, 128f));
        var plus = NewImage("EmptyPlus", slot, art.Plus, new Vector2(-0.098f, 0.0775f), new Vector2(32f, 32f));

        var badge = NewImage("PlayerNumberBadge", slot, art.Badge, new Vector2(-54.598f, 62.0775f), new Vector2(47f, 32f));
        var number = NewText("PlayerNumberTMP", badge.rectTransform, art.Heavy, 15f, new Vector2(-0.4f, 0.42f), new Vector2(47f, 32f), "P1");
        var name = NewText("PlayerNameTMP", slot, art.NameFont, 20f, new Vector2(0f, -92f), new Vector2(240f, 30f), "");
        name.fontStyle = FontStyles.Bold;

        var capsule = NewImage("StatusCapsule", slot, art.CapsuleDisconnected, new Vector2(0.402f, -131.4225f), new Vector2(153f, 41f));
        var icon = NewImage("StatusIcon", capsule.rectTransform, art.Dash, new Vector2(-48f, 0f), new Vector2(15f, 7f));
        var status = NewText("ReadyStatusTMP", capsule.rectTransform, art.Heavy, 18f, new Vector2(9.6f, 0.42f), new Vector2(110f, 30f), "");

        var so = new SerializedObject(view);
        so.FindProperty("frameImage").objectReferenceValue = frame;
        so.FindProperty("portraitMaskImage").objectReferenceValue = mask;
        so.FindProperty("portraitImage").objectReferenceValue = portrait;
        so.FindProperty("readyRing").objectReferenceValue = readyRing.gameObject;
        so.FindProperty("emptyDashedRing").objectReferenceValue = dashed.gameObject;
        so.FindProperty("emptyPlus").objectReferenceValue = plus.gameObject;
        so.FindProperty("playerNumberText").objectReferenceValue = number;
        so.FindProperty("playerNameText").objectReferenceValue = name;
        so.FindProperty("statusCapsuleImage").objectReferenceValue = capsule;
        so.FindProperty("statusIconImage").objectReferenceValue = icon;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("frameConnected").objectReferenceValue = art.FrameConnected;
        so.FindProperty("frameDisconnected").objectReferenceValue = art.FrameDisconnected;
        so.FindProperty("capsuleReady").objectReferenceValue = art.CapsuleReady;
        so.FindProperty("capsuleWaiting").objectReferenceValue = art.CapsuleWaiting;
        so.FindProperty("capsuleDisconnected").objectReferenceValue = art.CapsuleDisconnected;
        so.FindProperty("iconCheck").objectReferenceValue = art.Check;
        so.FindProperty("iconDot").objectReferenceValue = art.Dot;
        so.FindProperty("iconDash").objectReferenceValue = art.Dash;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 에디터 기본 모습 = 미접속(런타임엔 LobbyUIController 가 OnEnable 에서 실제 상태로 덮는다)
        int index = view.transform.GetSiblingIndex();
        view.SetState(false, false, null, $"P{index + 1}", null);
    }

    static Image NewImage(string name, Transform parent, Sprite sprite, Vector2 pos, Vector2 size)
    {
        var rt = NewRect(name, parent, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, float size, Vector2 pos, Vector2 box, string text)
    {
        var rt = NewRect(name, parent, pos, box);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.CenterGeoAligned; // 목업 좌표 = 글자 윤곽 중심
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        t.text = text;
        return t;
    }

    static RectTransform NewRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    sealed class Art
    {
        public Sprite FrameConnected, FrameDisconnected, CircleMask, ReadyRing, DashedRing, Plus, Badge,
            CapsuleReady, CapsuleWaiting, CapsuleDisconnected, Check, Dot, Dash;
        public TMP_FontAsset Heavy, NameFont;

        public bool Load()
        {
            FrameConnected = S("lobby_portrait_frame_connected");
            FrameDisconnected = S("lobby_portrait_frame_disconnected");
            CircleMask = S("lobby_portrait_circle_mask");
            ReadyRing = S("lobby_portrait_ready_ring");
            DashedRing = S("lobby_portrait_empty_dashed_ring");
            Plus = S("lobby_empty_plus");
            Badge = S("lobby_player_number_badge");
            CapsuleReady = S("lobby_status_capsule_ready");
            CapsuleWaiting = S("lobby_status_capsule_waiting");
            CapsuleDisconnected = S("lobby_status_capsule_disconnected");
            Check = S("lobby_status_check");
            Dot = S("lobby_status_dot");
            Dash = S("lobby_status_dash");
            Heavy = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HeavyFontPath);
            NameFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NameFontPath);

            bool ok = FrameConnected && FrameDisconnected && CircleMask && ReadyRing && DashedRing && Plus && Badge &&
                      CapsuleReady && CapsuleWaiting && CapsuleDisconnected && Check && Dot && Dash && Heavy && NameFont;
            if (!ok)
                Debug.LogError($"[LobbySlotsA] 아트·글꼴을 못 찾음 — SVN {ArtDir} 업데이트(r683)·Refresh 후 다시 실행");
            return ok;
        }

        static Sprite S(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + file + ".png");
    }
}
