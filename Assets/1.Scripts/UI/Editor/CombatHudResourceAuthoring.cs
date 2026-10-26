using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 2026-10-26 HUD 납품본(체력바 포트레잇 v3 · 스킬 슬롯 마스크)을 <c>CombatHUD.prefab</c> 에 입힌다.
///
/// 🔴 <b>왜 저작 스크립트인가</b> — <see cref="CombatHudSlotAuthoring"/> 와 같다. 5000줄짜리 프리팹을
///    손으로 고치지 않고 유니티가 직렬화하게 둔다.
///
/// 🔴 <b>멱등하다</b> — 단계마다 이름으로 찾아 값만 덮어쓴다. 다시 돌려도 결과가 같다.
///    스프라이트를 하나라도 못 찾으면 <b>저장하지 않는다</b>(SVN 아트·.meta 누락 시 null 이 박히는 걸 막는다).
///
/// 납품 코드(<c>HealthPortraitBar</c>·<c>ProjectMT.HUD</c>)는 쓰지 않는다 — PNG 만 가져와 기존 Image Filled 구조에 연결한다.
/// </summary>
public static class CombatHudResourceAuthoring
{
    const string HudPath = "Assets/2.Prefabs/UI/CombatHUD.prefab";
    const string HealthArtDir = "Assets/50.Art/UI/HUD/HealthPortrait/";

    // ── 체력바 배치(납품 layout.json, 1x UI 픽셀 · 프레임 좌상단 기준) ─────────────
    //  프레임 462×104 / HP 채움 (14,14) 435×74 / XP 채움 (174,51) 274×38.
    //  PlayerStatus 는 중앙 피벗이라 좌상단 좌표를 중앙 기준 오프셋으로 바꿔 쓴다.
    static readonly Vector2 FrameSize = new Vector2(462f, 104f);
    static readonly Vector2 HpSize = new Vector2(435f, 74f);
    static readonly Vector2 HpTopLeft = new Vector2(14f, 14f);
    static readonly Vector2 XpSize = new Vector2(274f, 38f);
    static readonly Vector2 XpTopLeft = new Vector2(174f, 51f);

    [MenuItem("Tools/UI/Authoring/CombatHUD — 새 HUD 리소스 적용(체력바·스킬 슬롯)")]
    public static void ApplyAll()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HudPath);
        if (root == null)
        {
            Debug.LogError($"[CombatHudResource] 프리팹을 열 수 없다: {HudPath}");
            return;
        }

        try
        {
            if (!ApplyHealthBar(root.transform) || !ApplySkillSlots(root.transform) || !ApplyTraitProgress(root.transform))
            {
                Debug.LogError("[CombatHudResource] 적용 실패 — 저장하지 않았다. 위 오류를 확인할 것.");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, HudPath);

            if (!AssignRosterHpFills())
            {
                Debug.LogError("[CombatHudResource] 로스터 체력 채움 지정 실패 — 위 오류를 확인할 것.");
                return;
            }

            Debug.Log($"[CombatHudResource] 적용 완료 — {HudPath}, {RosterPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── C1: 체력바 ──────────────────────────────────────────────────────

    /// <summary>
    /// PlayerStatus 틀 → hp_empty_frame, HP 채움 3장 → 435×74, ExBar → xp_fill.
    /// 🔴 HpBar 자체는 제자리(0,0)에 두고 채움만 옮긴다 — HpBar 를 옮기면 자식 HpText·Portrait 가 따라 움직인다.
    /// ExBar 를 켜고 값을 잇는 건 C4(<see cref="ApplyTraitProgress"/>).
    /// </summary>
    static bool ApplyHealthBar(Transform root)
    {
        Sprite frame = LoadSprite(HealthArtDir + "hp_empty_frame.png");
        Sprite hpFill = LoadSprite(HealthArtDir + "hp_fill_gunner.png");
        Sprite hpMask = LoadSprite(HealthArtDir + "hp_visible_area_mask.png");
        Sprite xpFill = LoadSprite(HealthArtDir + "xp_fill.png");
        if (frame == null || hpFill == null || hpMask == null || xpFill == null)
            return false;

        RectTransform status = Find(root, "Layer_Combat/PlayerStatus");
        RectTransform hpBar = Find(status, "HpBar");
        RectTransform fill = Find(hpBar, "HpFill");
        RectTransform delayed = Find(hpBar, "HpDelayedFill");
        RectTransform shield = Find(hpBar, "ShieldBar");
        RectTransform ex = Find(status, "ExBar");
        if (status == null || hpBar == null || fill == null || delayed == null || shield == null || ex == null)
            return false;

        status.sizeDelta = FrameSize;
        status.GetComponent<Image>().sprite = frame;

        hpBar.sizeDelta = HpSize;
        Vector2 hpCenter = CenterOffset(HpTopLeft, HpSize);

        // 채움 = 캐릭터 포트레잇 합성본(기본 거너, 실제 캐릭터는 PlayerHealthHUD.Bind 가 고른다).
        // 잔상·실드 = 공통 가시 영역 마스크에 기존 틴트(회색·파랑)를 그대로 입힌다.
        SetHpLayer(fill, hpCenter, hpFill);
        SetHpLayer(delayed, hpCenter, hpMask);
        SetHpLayer(shield, hpCenter, hpMask);

        ex.sizeDelta = XpSize;
        ex.anchoredPosition = CenterOffset(XpTopLeft, XpSize);
        ex.GetComponent<Image>().sprite = xpFill;
        return true;
    }

    // ── C2: 캐릭터별 체력 채움 ───────────────────────────────────────────

    const string RosterPath = "Assets/9.ScriptableObject/Player/CharacterRoster.asset";

    /// <summary>
    /// 로스터 칸마다 <c>hudHpFill</c> 을 넣는다. 칸 순서가 아니라 칸의 캐릭터(<see cref="TutorialCharacter"/>)로 고른다 —
    /// 칸을 옮겨도 그림이 따라간다. 값이 같으면 건드리지 않는다(에셋을 dirty 로 만들지 않는다).
    /// </summary>
    static bool AssignRosterHpFills()
    {
        var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
        if (roster == null)
        {
            Debug.LogError($"[CombatHudResource] 로스터를 못 찾았다: {RosterPath}");
            return false;
        }

        var so = new SerializedObject(roster);
        SerializedProperty entries = so.FindProperty("entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            string file = HpFillFileFor((TutorialCharacter)entry.FindPropertyRelative("tutorialCharacter").intValue);
            if (file == null)
                continue;

            Sprite sprite = LoadSprite(HealthArtDir + file);
            if (sprite == null)
                return false;

            entry.FindPropertyRelative("hudHpFill").objectReferenceValue = sprite;
        }

        if (so.ApplyModifiedPropertiesWithoutUndo())
            AssetDatabase.SaveAssetIfDirty(roster);
        return true;
    }

    static string HpFillFileFor(TutorialCharacter character)
    {
        switch (character)
        {
            case TutorialCharacter.Paladin: return "hp_fill_paladin.png";
            case TutorialCharacter.Gunner: return "hp_fill_gunner.png";
            case TutorialCharacter.Assassin: return "hp_fill_assassin.png";
            default: return null;
        }
    }

    // ── C3: 스킬 바 프레임·마스크·상단 칸 ───────────────────────────────────

    const string SlotArtDir = "Assets/50.Art/UI/HUD/Slots/";
    const string SkillBarPath = "Layer_Combat/SkillBar/";
    const string UpgradeCellsName = "UpgradeCells";

    // 기존 프레임 조각(slot_skill_N, 96×99 px)은 100×100 슬롯 루트에 늘려 그려진다. 새 그림도 같은 배율로 보이게 한다.
    // 새 그림은 99×101 캔버스(논리 1x px, 2x 파일은 PPU 200)이고 슬롯·마스크가 같은 캔버스를 쓴다.
    static readonly Vector2 SlotRootSize = new Vector2(100f, 100f);
    static readonly Vector2 OldFramePx = new Vector2(96f, 99f);
    static readonly Vector2 CanvasPx = new Vector2(99f, 101f);
    static Vector2 PxToUi => new Vector2(SlotRootSize.x / OldFramePx.x, SlotRootSize.y / OldFramePx.y);
    static Vector2 CanvasUiSize => Vector2.Scale(CanvasPx, PxToUi);

    // 실측(2026-10-26, 알파 맞춤): mask_skill_body 캔버스에서 기존 slot_skill_1 은 (1,0) 에 놓인다
    // (본체 내부 x8..87·y21..94 ↔ 마스크 x9..88·y21..94). 새 좌·우 프레임은 기존 조각 대비 (1,1) 이라
    // 슬롯 루트를 캔버스 크기로 키우고 중심은 그대로 둔다(0.5px 미만 차이).
    static readonly Vector2 SkillFrameInCanvasPx = new Vector2(1f, 0f);

    // 마스크 본체 알파 영역(캔버스 px, 좌상단 기준 [min, max)) — 아이콘 중심을 본체 중심에 맞춘다.
    static readonly Rect SkillBodyPx = Rect.MinMaxRect(9f, 21f, 89f, 95f);
    static readonly Rect PassiveBodyPx = Rect.MinMaxRect(10f, 7.5f, 90f, 95f);
    static readonly Rect UltBodyPx = Rect.MinMaxRect(8f, 7.5f, 88f, 95f);

    // slot_skill_1 상단 4칸 내부(프레임 px, 좌상단 기준 [min, max)) — 실측 폭 20·22·21·21, 높이 13.
    static readonly float[] HeaderCellXMinPx = { 3f, 25f, 49f, 72f };
    static readonly float[] HeaderCellXMaxPx = { 23f, 47f, 70f, 93f };
    static readonly Vector2 HeaderCellYPx = new Vector2(4f, 17f);
    // 칸 = 내부를 사방 1px 줄인 크기. 예전엔 22×13 고정이라 폭 20 인 첫 칸이 테두리를 1px 덮었다.
    const float HeaderCellInsetPx = 1f;
    // 🟨 임시 색 — 디자인 확정 전까지 XP 바 계열 주황.
    static readonly Color HeaderCellColor = new Color(1f, 0.6f, 0.1f, 1f);

    static bool ApplySkillSlots(Transform root)
    {
        Sprite passiveFrame = LoadSprite(SlotArtDir + "slot_passive_no_tabs_2x.png");
        Sprite ultFrame = LoadSprite(SlotArtDir + "slot_ult_no_tabs_2x.png");
        Sprite passiveMask = LoadSprite(SlotArtDir + "mask_passive_body_no_tabs_2x.png");
        Sprite ultMask = LoadSprite(SlotArtDir + "mask_ult_body_no_tabs_2x.png");
        Sprite skillMask = LoadSprite(SlotArtDir + "mask_skill_body_2x.png");
        Sprite headerCell = LoadSprite(SlotArtDir + "mask_skill_header_cell_2x.png");
        if (passiveFrame == null || ultFrame == null || passiveMask == null || ultMask == null ||
            skillMask == null || headerCell == null)
            return false;

        // 좌·우: 새 프레임 = 마스크와 같은 캔버스 → 슬롯 루트를 캔버스 크기로 키우고 마스크는 루트와 겹친다.
        if (!ApplySideSlot(root, "Slot_Passive", passiveFrame, passiveMask, PassiveBodyPx) ||
            !ApplySideSlot(root, "Slot_Dash", ultFrame, ultMask, UltBodyPx))
            return false;

        // 가운데 4칸: 프레임(slot_skill_1)은 그대로, 마스크 캔버스를 프레임 위치에 맞춰 얹고 상단 칸을 둔다.
        Vector2 px = PxToUi;
        Vector2 canvasCenter = new Vector2(
            -SlotRootSize.x * 0.5f - SkillFrameInCanvasPx.x * px.x + CanvasUiSize.x * 0.5f,
            SlotRootSize.y * 0.5f + SkillFrameInCanvasPx.y * px.y - CanvasUiSize.y * 0.5f);
        foreach (string slotName in new[] { "Slot_Q", "Slot_E", "Slot_R", "Slot_RMB" })
        {
            RectTransform slot = Find(root, SkillBarPath + slotName);
            if (slot == null || !ApplyMasks(slot, skillMask, canvasCenter, SkillBodyPx))
                return false;

            BuildUpgradeCells(slot, headerCell);
        }
        return AssignCooldownBodies(root);
    }

    /// <summary>
    /// 쿨다운 덮개 본체 구간(C5)을 슬롯마다 적는다 — 가운데 4칸 = mask_skill_body, 패시브·대시 = 좌우 마스크.
    /// 컴포넌트 기본값과 같지만, 기존 프리팹 배열 원소는 기본값이 안 들어갈 수 있어 명시한다.
    /// </summary>
    static bool AssignCooldownBodies(Transform root)
    {
        RectTransform bar = Find(root, SkillBarPath.TrimEnd('/'));
        if (bar == null)
            return false;

        var skill = bar.GetComponentInChildren<SkillCooldownHUD>(true);
        var dash = bar.GetComponentInChildren<DashCooldownHUD>(true);
        var passive = bar.GetComponentInChildren<PassiveHUD>(true);
        if (skill == null || dash == null || passive == null)
        {
            Debug.LogError("[CombatHudResource] SkillBar 에서 SkillCooldownHUD·DashCooldownHUD·PassiveHUD 를 못 찾았다.");
            return false;
        }

        var skillSo = new SerializedObject(skill);
        SerializedProperty slots = skillSo.FindProperty("slots");
        for (int i = 0; i < slots.arraySize; i++)
            WriteBody(slots.GetArrayElementAtIndex(i).FindPropertyRelative("cooldownBody"), CooldownCoverBody.SkillBody);
        skillSo.ApplyModifiedPropertiesWithoutUndo();

        foreach (Object side in new Object[] { dash, passive })
        {
            var so = new SerializedObject(side);
            WriteBody(so.FindProperty("cooldownBody"), CooldownCoverBody.SideBody);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        return true;
    }

    static void WriteBody(SerializedProperty prop, CooldownCoverBody body)
    {
        prop.FindPropertyRelative("canvasHeightPx").floatValue = body.canvasHeightPx;
        prop.FindPropertyRelative("bodyTopPx").floatValue = body.bodyTopPx;
        prop.FindPropertyRelative("bodyBottomPx").floatValue = body.bodyBottomPx;
    }

    static bool ApplySideSlot(Transform root, string slotName, Sprite frame, Sprite mask, Rect bodyPx)
    {
        RectTransform slot = Find(root, SkillBarPath + slotName);
        if (slot == null)
            return false;

        // 중심(anchoredPosition)은 그대로 — 아이콘·키 배지·쿨다운 글자는 중앙 앵커라 움직이지 않는다.
        slot.sizeDelta = CanvasUiSize;
        slot.GetComponent<Image>().sprite = frame;
        return ApplyMasks(slot, mask, Vector2.zero, bodyPx);
    }

    /// <summary>
    /// Icon_mask·Icon_mask_CooldownFill 을 마스크 캔버스 크기·위치로 맞춘다. 부모(슬롯 루트)·Filled·색은 그대로.
    /// 마스크 안 아이콘(사본 포함)은 크기는 두고 중심만 본체 중심에 맞춘다 — 마스크를 옮긴 만큼 아이콘이 따라 움직이지 않게.
    /// </summary>
    static bool ApplyMasks(RectTransform slot, Sprite mask, Vector2 canvasCenter, Rect bodyPx)
    {
        Vector2 px = PxToUi;
        Vector2 bodyCenter = canvasCenter + new Vector2(
            (bodyPx.center.x - CanvasPx.x * 0.5f) * px.x,
            -(bodyPx.center.y - CanvasPx.y * 0.5f) * px.y);

        foreach (string maskName in new[] { "Icon_mask", "Icon_mask_CooldownFill" })
        {
            RectTransform rt = Find(slot, maskName);
            if (rt == null)
                return false;

            rt.sizeDelta = CanvasUiSize;
            rt.anchoredPosition = canvasCenter;
            rt.GetComponent<Image>().sprite = mask;

            foreach (RectTransform icon in rt)
                icon.anchoredPosition = bodyCenter - canvasCenter;
        }
        return true;
    }

    /// <summary>slot_skill_1 그림의 상단 4칸 자리에 칸 Image 를 둔다. 기본은 전부 꺼짐(0단계).</summary>
    static void BuildUpgradeCells(RectTransform slot, Sprite cellSprite)
    {
        RectTransform group = EnsureChild(slot, UpgradeCellsName);
        group.anchorMin = group.anchorMax = group.pivot = new Vector2(0.5f, 0.5f);
        group.anchoredPosition = Vector2.zero;
        group.sizeDelta = SlotRootSize;
        group.localScale = Vector3.one;
        // 프레임(루트) 위, 아이콘 마스크 다음 — 키 배지·쿨다운 글자보다 아래.
        group.SetSiblingIndex(slot.Find("Icon_mask_CooldownFill").GetSiblingIndex() + 1);

        Vector2 px = PxToUi;
        var cells = new GameObject[HeaderCellXMinPx.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            RectTransform cell = EnsureChild(group, "Cell" + i);
            cell.anchorMin = cell.anchorMax = cell.pivot = new Vector2(0.5f, 0.5f);
            cell.localScale = Vector3.one;
            cell.sizeDelta = Vector2.Scale(new Vector2(
                HeaderCellXMaxPx[i] - HeaderCellXMinPx[i] - HeaderCellInsetPx * 2f,
                HeaderCellYPx.y - HeaderCellYPx.x - HeaderCellInsetPx * 2f), px);
            cell.anchoredPosition = new Vector2(
                (HeaderCellXMinPx[i] + HeaderCellXMaxPx[i]) * 0.5f * px.x - SlotRootSize.x * 0.5f,
                SlotRootSize.y * 0.5f - (HeaderCellYPx.x + HeaderCellYPx.y) * 0.5f * px.y);

            Image image = EnsureComponent<Image>(cell.gameObject);
            image.sprite = cellSprite;
            image.color = HeaderCellColor;
            image.raycastTarget = false;
            cell.gameObject.SetActive(false);
            cells[i] = cell.gameObject;
        }

        SkillUpgradeCellsView view = EnsureComponent<SkillUpgradeCellsView>(group.gameObject);
        var so = new SerializedObject(view);
        SerializedProperty list = so.FindProperty("cells");
        list.arraySize = cells.Length;
        for (int i = 0; i < cells.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = cells[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── C4: 특성 진행도(XP 바·레벨 숫자·상단 칸 연결) ──────────────────────

    const string LevelTextName = "LevelText";

    // 레벨 숫자 = XP 바 왼쪽 둥근 자리(미리보기 실측, 프레임 좌상단 기준). XP 채움 위에 겹친다.
    static readonly Vector2 LevelTopLeft = new Vector2(182f, 51f);
    static readonly Vector2 LevelSize = new Vector2(36f, 38f);
    // 레벨 숫자 색 = 미리보기 그림 기준 검정(주황 XP 바 위). 글꼴·크기는 HpText 그대로.
    static readonly Color LevelTextColor = Color.black;

    static readonly (string name, PlayerSkillSlot slot)[] SkillSlots =
    {
        ("Slot_Q", PlayerSkillSlot.Main),
        ("Slot_E", PlayerSkillSlot.Sub),
        ("Slot_R", PlayerSkillSlot.Ultimate),
        ("Slot_RMB", PlayerSkillSlot.Interrupt),
    };

    /// <summary>
    /// ExBar 를 켜고(Filled Horizontal Left) 레벨 숫자 TMP 를 둔 뒤, <see cref="TraitProgressHUD"/> 를 PlayerStatus 에 붙여
    /// XP 바·레벨·상단 칸 4개를 잇고 <see cref="CombatHUD"/> 의 필드에 꽂는다.
    /// </summary>
    static bool ApplyTraitProgress(Transform root)
    {
        RectTransform status = Find(root, "Layer_Combat/PlayerStatus");
        RectTransform ex = Find(status, "ExBar");
        RectTransform hpText = Find(status, "HpBar/HpText");
        if (status == null || ex == null || hpText == null)
            return false;

        var exImage = ex.GetComponent<Image>();
        exImage.type = Image.Type.Filled;
        exImage.fillMethod = Image.FillMethod.Horizontal;
        exImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        exImage.fillAmount = 0f;
        ex.gameObject.SetActive(true);

        TMP_Text level = BuildLevelText(status, ex, hpText.GetComponent<TMP_Text>());

        var view = EnsureComponent<TraitProgressHUD>(status.gameObject);
        var so = new SerializedObject(view);
        so.FindProperty("xpFill").objectReferenceValue = exImage;
        so.FindProperty("levelText").objectReferenceValue = level;
        SerializedProperty list = so.FindProperty("slotCells");
        list.arraySize = SkillSlots.Length;
        for (int i = 0; i < SkillSlots.Length; i++)
        {
            RectTransform cells = Find(root, SkillBarPath + SkillSlots[i].name + "/" + UpgradeCellsName);
            if (cells == null)
                return false;

            SerializedProperty element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("slot").enumValueIndex = (int)SkillSlots[i].slot;
            element.FindPropertyRelative("view").objectReferenceValue = cells.GetComponent<SkillUpgradeCellsView>();
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        var hud = root.GetComponent<CombatHUD>();
        var hudSo = new SerializedObject(hud);
        hudSo.FindProperty("traitProgressHUD").objectReferenceValue = view;
        hudSo.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    /// <summary>HpText 의 글꼴·머티리얼·크기를 그대로 쓰고 색만 <see cref="LevelTextColor"/> 인 숫자 칸. XP 바 바로 위(형제 순서)에 그린다.</summary>
    static TMP_Text BuildLevelText(RectTransform status, RectTransform ex, TMP_Text style)
    {
        RectTransform rt = EnsureChild(status, LevelTextName);
        rt.gameObject.layer = status.gameObject.layer;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
        rt.sizeDelta = LevelSize;
        rt.anchoredPosition = CenterOffset(LevelTopLeft, LevelSize);
        rt.SetSiblingIndex(ex.GetSiblingIndex() + 1);

        var text = EnsureComponent<TextMeshProUGUI>(rt.gameObject);
        text.font = style.font;
        text.fontSharedMaterial = style.fontSharedMaterial;
        text.fontSize = style.fontSize;
        text.enableAutoSizing = false;
        text.fontStyle = style.fontStyle;
        text.color = LevelTextColor;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.text = "1";

        // color 세터는 m_fontColor 만 바꾸고 m_fontColor32 는 메시를 다시 만들 때 따라간다.
        // LoadPrefabContents 안에서는 메시가 안 만들어져 흰색 32비트 값이 남으므로 직접 맞춘다.
        var so = new SerializedObject(text);
        so.FindProperty("m_fontColor32").colorValue = LevelTextColor;
        so.ApplyModifiedPropertiesWithoutUndo();
        return text;
    }

    static RectTransform EnsureChild(Transform parent, string name)
    {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null)
            return existing;

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    /// <summary>HpBar(중앙 0,0) 안에서의 위치·크기·스프라이트만 바꾼다. 색·Filled 설정은 기존 값을 유지한다.</summary>
    static void SetHpLayer(RectTransform rt, Vector2 center, Sprite sprite)
    {
        rt.sizeDelta = HpSize;
        rt.anchoredPosition = center;
        rt.GetComponent<Image>().sprite = sprite;
    }

    /// <summary>프레임 좌상단 기준 좌표(아래로 +) → 중앙 피벗 프레임 기준 중앙 오프셋(위로 +).</summary>
    static Vector2 CenterOffset(Vector2 topLeft, Vector2 size) =>
        new Vector2(
            -FrameSize.x * 0.5f + topLeft.x + size.x * 0.5f,
            FrameSize.y * 0.5f - topLeft.y - size.y * 0.5f);

    // ── 헬퍼 ────────────────────────────────────────────────────────

    static RectTransform Find(Transform parent, string path)
    {
        if (parent == null)
            return null;

        var rt = parent.Find(path) as RectTransform;
        if (rt == null)
            Debug.LogError($"[CombatHudResource] '{parent.name}/{path}' 를 못 찾았다 — 프리팹 구조가 바뀌었는지 확인할 것.");
        return rt;
    }

    static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

        // 🔴 아트는 SVN 이고 `.meta` 가 빠지면 참조가 조용히 끊긴다(2026-09-19 7건 사고).
        if (sprite == null)
            Debug.LogError($"[CombatHudResource] 스프라이트를 못 찾았다: {path} — SVN 업데이트와 .meta 를 확인할 것.");

        return sprite;
    }
}
