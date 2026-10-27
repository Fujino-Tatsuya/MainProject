using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>CombatHUD와 툴팁 데이터 저작을 재현하는 멱등 메뉴.</summary>
public static class SkillTooltipAuthoring
{
    private const string HudPath = "Assets/2.Prefabs/UI/CombatHUD.prefab";
    private const string GeneratedFolder = "Assets/2.Prefabs/UI/SkillTooltipGenerated";
    private const string FontPath = "Assets/Resources/NotoSansKR-VariableFont_wght SDF.asset";
    private const string StylePath = GeneratedFolder + "/SkillTooltipStyles.asset";
    // 이름은 옛 공격력 임시 아이콘 그대로(guid 유지) — 지금은 인라인 아이콘 전부를 담은 아틀라스다.
    private const string AttackTexturePath = GeneratedFolder + "/SkillTooltipAttackIcon.asset";
    private const string AttackSpritePath = GeneratedFolder + "/SkillTooltipAttackSprite.asset";
    private const int InlineIconSize = 64;

    // 설명 문구의 <sprite name=…> 이름 ↔ 원본 PNG. atk 는 SkillTooltipFormatter 가 쓴다.
    private static readonly (string name, string png)[] InlineIcons =
    {
        ("atk", GeneratedFolder + "/tooltipIcon_Sword.png"),
        ("shield", GeneratedFolder + "/tooltipIcon_Shield.png"),
        ("cooldown", GeneratedFolder + "/tooltipIcon_CoolDown.png"),
    };
    private const string PaladinPrefabPath = "Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab";
    private const string SlotFramePath = "Assets/50.Art/UI/HUD/slot_skill.png";
    private const string PassiveIconPath = "Icon_mask/Icon_Passive";

    private static readonly Dictionary<PlayerSkillSlot, string> PaladinData = new Dictionary<PlayerSkillSlot, string>
    {
        { PlayerSkillSlot.Main, "Assets/9.ScriptableObject/Player/Garen/FirstMeleeMainSkillData.asset" },
        { PlayerSkillSlot.Sub, "Assets/9.ScriptableObject/Player/Garen/FirstMeleeSubSkillData.asset" },
        { PlayerSkillSlot.Interrupt, "Assets/9.ScriptableObject/Player/Garen/FirstMeleeInterruptSkillData.asset" },
        { PlayerSkillSlot.Ultimate, "Assets/9.ScriptableObject/Player/Garen/FirstMeleeUltimateSkillData.asset" },
    };

    private static readonly Dictionary<PlayerSkillSlot, string> GunnerData = new Dictionary<PlayerSkillSlot, string>
    {
        { PlayerSkillSlot.Main, "Assets/9.ScriptableObject/Player/Gunner/GunnerChargeLaserData.asset" },
        { PlayerSkillSlot.Sub, "Assets/9.ScriptableObject/Player/Gunner/GunnerCoolBackstepData.asset" },
        { PlayerSkillSlot.Interrupt, "Assets/9.ScriptableObject/Player/Gunner/GunnerInterruptData.asset" },
        { PlayerSkillSlot.Ultimate, "Assets/9.ScriptableObject/Player/Gunner/GunnerTrackingLaserData.asset" },
    };

    private const string AssassinDataFolder = "Assets/9.ScriptableObject/Player/Assassin/";

    // 어쌔신 = 일반 4슬롯 + 변신 대체 Q/E + 패시브(AssassinStateData). 문구는 TODO 임시.
    private static readonly (string path, string name, string subtitle, string description)[] AssassinTooltips =
    {
        (AssassinDataFolder + "AssassinDashStrikeSkillData.asset", "관통 돌진", "Q 스킬",
            "TODO 바라보는 방향으로 돌진하며 경로의 적에게 {dmg} 피해를 줍니다."),
        (AssassinDataFolder + "AssassinTransformedDashStrikeSkillData.asset", "관통 돌진 (변신)", "Q 스킬 · 변신",
            "TODO 돌진하며 경로의 적에게 {dmg} 피해를 줍니다. 적중 시 쿨타임이 줄어듭니다."),
        (AssassinDataFolder + "AssassinEnhanceSkillData.asset", "단검 강화", "E 스킬",
            "TODO 다음 기본 공격이 강타로 바뀝니다. 강타가 적중하면 분노 게이지가 크게 찹니다."),
        (AssassinDataFolder + "AssassinCircleStrikeSkillData.asset", "원형 난격", "E 스킬 · 변신",
            "TODO 지정한 원 범위에 {dmg} 피해를 5번 줍니다. 공격 중 무적입니다."),
        (AssassinDataFolder + "AssassinTransformSkillData.asset", "변신", "궁극기",
            "TODO 분노 게이지 전부를 연료로 변신합니다. 게이지가 0이 될 때까지 유지되며, 2초 뒤 다시 누르면 해제하고 남은 게이지를 보존합니다."),
        (AssassinDataFolder + "AssassinInterruptSkillData.asset", "간파", "우클릭 스킬",
            "TODO 무기를 올려쳐 적을 간파하고 {dmg} 피해를 줍니다."),
        (AssassinDataFolder + "AssassinStateData.asset", "백어택 · 분노 게이지", string.Empty,
            "TODO 보스 뒤에서 공격하면 피해가 증가합니다. 기본 공격·Q·강타가 적에게 적중하면 분노 게이지가 찹니다(최대 {maxRage}). " +
            "게이지가 {minTransformRage} 이상이면 R로 변신하고, 변신 중에는 초당 {transformRageDecayPerSecond}씩 줄어듭니다. 변신 중에는 모든 공격이 백어택입니다."),
    };

    [MenuItem("Tools/UI/스킬 툴팁 구성")]
    public static void Configure()
    {
        EnsureFolder(GeneratedFolder);
        TMP_StyleSheet styleSheet = EnsureStyleSheet();
        TMP_SpriteAsset attackSprite = EnsureAttackSpriteAsset();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        ConfigureHud(styleSheet, attackSprite, font);
        SeedTooltipSources();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SkillTooltip] 구성 완료 — {HudPath}. GameData.xlsx Export/병합은 별도로 실행할 것.");
    }

    /// <summary>CombatHUD 툴팁·슬롯 연결만 다시 한다 — 스킬 데이터 문구 시드·인라인 아이콘 아틀라스 재생성은 건드리지 않는다(할 일 114 배치 갱신용).</summary>
    [MenuItem("Tools/UI/스킬 툴팁 레이아웃만 갱신")]
    public static void ConfigureLayoutOnly()
    {
        TMP_StyleSheet styleSheet = EnsureStyleSheet();
        TMP_SpriteAsset attackSprite = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(AttackSpritePath);
        if (attackSprite == null)
            attackSprite = EnsureAttackSpriteAsset();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        ConfigureHud(styleSheet, attackSprite, font);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SkillTooltip] 레이아웃 갱신 완료 — {HudPath}");
    }

    private static void ConfigureHud(TMP_StyleSheet styleSheet, TMP_SpriteAsset attackSprite, TMP_FontAsset font)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            Canvas canvas = root.GetComponentInChildren<Canvas>(true);
            if (canvas == null)
                throw new InvalidOperationException("CombatHUD.prefab에서 Canvas를 찾지 못했다.");

            SkillTooltipView view = EnsureTooltipView(canvas, styleSheet, attackSprite, font);
            SkillCooldownHUD skillHud = root.GetComponentInChildren<SkillCooldownHUD>(true);
            PassiveHUD passiveHud = root.GetComponentInChildren<PassiveHUD>(true);
            if (skillHud == null || passiveHud == null)
                throw new InvalidOperationException("CombatHUD.prefab에서 SkillCooldownHUD/PassiveHUD를 찾지 못했다.");

            var skillSo = new SerializedObject(skillHud);
            SerializedProperty slots = skillSo.FindProperty("slots");
            for (int i = 0; slots != null && i < slots.arraySize; i++)
            {
                SerializedProperty widget = slots.GetArrayElementAtIndex(i);
                PlayerSkillSlot slot = (PlayerSkillSlot)widget.FindPropertyRelative("slot").enumValueIndex;
                Image fill = widget.FindPropertyRelative("cooldownFill").objectReferenceValue as Image;
                Transform slotRoot = fill != null ? fill.transform.parent : FindDeep(root.transform, SlotName(slot));
                if (slotRoot == null)
                    continue;

                SkillSlotHover hover = EnsureComponent<SkillSlotHover>(slotRoot.gameObject);
                SetReference(hover, "tooltipView", view);
                EnableFrameRaycast(slotRoot);
                widget.FindPropertyRelative("icon").objectReferenceValue = FindSlotIcon(slotRoot, SlotIconPath(slot));
                widget.FindPropertyRelative("hover").objectReferenceValue = hover;
            }
            skillSo.ApplyModifiedPropertiesWithoutUndo();

            var passiveSo = new SerializedObject(passiveHud);
            Transform passiveRoot = FindDeep(root.transform, "Slot_Passive");
            if (passiveRoot != null)
            {
                passiveRoot.gameObject.SetActive(true);
                EnableFrameRaycast(passiveRoot);
                passiveSo.FindProperty("icon").objectReferenceValue = FindSlotIcon(passiveRoot, PassiveIconPath);
                SkillSlotHover hover = EnsureComponent<SkillSlotHover>(passiveRoot.gameObject);
                SetReference(hover, "tooltipView", view);
                passiveSo.FindProperty("hover").objectReferenceValue = hover;
            }
            passiveSo.ApplyModifiedPropertiesWithoutUndo();

            // 대시 칸은 툴팁·가상 입력 없이 좌클릭만 소비한다(D18-C6).
            Transform dashRoot = FindDeep(root.transform, "Slot_Dash");
            if (dashRoot != null)
            {
                Image dashHitTarget = dashRoot.GetComponent<Image>();
                if (dashHitTarget != null)
                    dashHitTarget.raycastTarget = true;
                SkillSlotHover dashBlocker = EnsureComponent<SkillSlotHover>(dashRoot.gameObject);
                SetReference(dashBlocker, "tooltipView", view);
            }

            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static SkillTooltipView EnsureTooltipView(
        Canvas canvas, TMP_StyleSheet styleSheet, TMP_SpriteAsset attackSprite, TMP_FontAsset font)
    {
        Transform found = FindDeep(canvas.transform, "SkillTooltipHost");
        // 없으면 Combat 상태 레이어의 마지막 자식(가장 위에 그림)으로 만든다 — 레이어가 없던 옛 프리팹이면 루트.
        Transform hostParent = FindDeep(canvas.transform, PlayerCombatUiLifecyclePolicy.CombatLayerName) ?? canvas.transform;
        GameObject host = found != null ? found.gameObject : NewUi("SkillTooltipHost", hostParent).gameObject;
        Stretch((RectTransform)host.transform);
        SkillTooltipView view = EnsureComponent<SkillTooltipView>(host);

        RectTransform panel = FindDeep(host.transform, "Panel") as RectTransform;
        if (panel == null)
        {
            panel = NewUi("Panel", host.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.sizeDelta = new Vector2(430f, 270f);
            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.035f, 0.045f, 0.065f, 0.97f);
            background.raycastTarget = false;
            Outline outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.48f, 0.42f, 0.25f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        bool firstBuild = FindDeep(panel, "Description") == null;
        Image icon = EnsureImage(panel, "Icon", new Vector2(16f, -16f), new Vector2(80f, 80f));
        TMP_Text title = EnsureText(panel, "DisplayName", font, 28f, FontStyles.Bold, TextAlignmentOptions.TopLeft,
            new Vector2(108f, -14f), new Vector2(206f, 36f));
        TMP_Text subtitle = EnsureText(panel, "Subtitle", font, 17f, FontStyles.Normal, TextAlignmentOptions.TopLeft,
            new Vector2(108f, -52f), new Vector2(306f, 24f));
        TMP_Text cooldown = EnsureText(panel, "Cooldown", font, 18f, FontStyles.Normal, TextAlignmentOptions.MidlineRight,
            new Vector2(330f, -16f), new Vector2(84f, 28f));
        TMP_Text body = EnsureText(panel, "Description", font, 18f, FontStyles.Normal, TextAlignmentOptions.TopLeft,
            new Vector2(16f, -116f), new Vector2(398f, 112f));
        TMP_Text hint = EnsureText(panel, "ShiftHint", font, 14f, FontStyles.Italic, TextAlignmentOptions.Right,
            new Vector2(16f, -240f), new Vector2(398f, 24f));
        body.styleSheet = styleSheet;
        body.spriteAsset = attackSprite;
        if (firstBuild)
        {
            body.textWrappingMode = TextWrappingModes.Normal;
            hint.color = new Color(0.72f, 0.74f, 0.78f, 1f);
        }
        EnsureFlexibleHeight(panel, body, hint, icon.rectTransform, title.rectTransform,
            subtitle.rectTransform, cooldown.rectTransform);
        TooltipLayout layout = EnsureLayout114(panel, font, icon, title, subtitle, cooldown, body, hint);
        StyleTagChip(layout.TagChip);

        var so = new SerializedObject(view);
        so.FindProperty("panel").objectReferenceValue = panel.gameObject;
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("displayName").objectReferenceValue = title;
        so.FindProperty("subtitle").objectReferenceValue = subtitle;
        so.FindProperty("cooldownGroup").objectReferenceValue = layout.CooldownGroup;
        so.FindProperty("cooldownText").objectReferenceValue = cooldown;
        so.FindProperty("tagRow").objectReferenceValue = layout.TagRow;
        so.FindProperty("tagChipTemplate").objectReferenceValue = layout.TagChip;
        so.FindProperty("tagStyles").objectReferenceValue = EnsureTagStyles();
        so.FindProperty("headerLayout").objectReferenceValue = layout.HeaderLayout;
        so.FindProperty("description").objectReferenceValue = body;
        so.FindProperty("shiftHint").objectReferenceValue = hint;
        so.ApplyModifiedPropertiesWithoutUndo();
        panel.gameObject.SetActive(false);
        return view;
    }

    private readonly struct TooltipLayout
    {
        public TooltipLayout(GameObject cooldownGroup, RectTransform tagRow, RectTransform tagChip, LayoutElement headerLayout)
        {
            CooldownGroup = cooldownGroup;
            TagRow = tagRow;
            TagChip = tagChip;
            HeaderLayout = headerLayout;
        }

        public GameObject CooldownGroup { get; }
        public RectTransform TagRow { get; }
        public RectTransform TagChip { get; }
        public LayoutElement HeaderLayout { get; }
    }

    // 할 일 114 시안 배치. 쿨다운 시계 아이콘은 기획 이미지가 나올 때까지 임시 흰 사각형(SVN 원본은 참조만). 칩 배경은 StyleTagChip.
    private const string TempWhiteSpritePath = "Assets/50.Art/TestAssets/Temp_Images/Images/white_512.png";
    private const float HeaderMinHeight = 116f;
    private static readonly Color AccentColor = new Color(0.95f, 0.82f, 0.42f, 1f);
    private static readonly Color MutedColor = new Color(0.72f, 0.74f, 0.78f, 1f);

    /// <summary>
    /// 할 일 114 배치(좌상단 아이콘 / 이름·슬롯 줄·태그 칩 / 우상단 시계+초 / 구분선 / 설명 / 우하단 Shift 안내).
    /// <c>Header/TagRow</c> 가 없을 때 한 번만 머리 영역을 다시 놓는다 — 이후 위치·색·크기는 프리팹 인스펙터에서 고친다(재실행은 참조만 다시 잇는다).
    /// 옛 키 배지(<c>KeyBadge</c>)는 슬롯 줄로 합쳐 지운다.
    /// </summary>
    private static TooltipLayout EnsureLayout114(
        RectTransform panel, TMP_FontAsset font, Image icon, TMP_Text title, TMP_Text subtitle, TMP_Text cooldown,
        TMP_Text body, TMP_Text hint)
    {
        var header = (RectTransform)panel.Find("Header");
        LayoutElement headerLayout = EnsureComponent<LayoutElement>(header.gameObject);
        if (header.Find("TagRow") is RectTransform existingRow)
        {
            Transform existingGroup = header.Find("CooldownGroup");
            Transform existingChip = existingRow.Find("TagChip");
            return new TooltipLayout(existingGroup != null ? existingGroup.gameObject : cooldown.gameObject, existingRow,
                existingChip as RectTransform, headerLayout);
        }

        Sprite white = AssetDatabase.LoadAssetAtPath<Sprite>(TempWhiteSpritePath);
        if (white == null)
            Debug.LogWarning($"[SkillTooltip] 임시 스프라이트 없음: {TempWhiteSpritePath} — 시계를 스프라이트 없이 만든다(SVN 업데이트 확인).");

        Transform oldBadge = FindDeep(panel, "KeyBadge");
        if (oldBadge != null)
            Object.DestroyImmediate(oldBadge.gameObject);

        headerLayout.minHeight = HeaderMinHeight;
        headerLayout.preferredHeight = HeaderMinHeight;
        float width = panel.sizeDelta.x;
        const float pad = 16f;
        const float textLeft = pad + 80f + 12f;

        Place(icon.rectTransform, new Vector2(pad, -pad), new Vector2(80f, 80f));
        icon.preserveAspect = true;

        Place(title.rectTransform, new Vector2(textLeft, -12f), new Vector2(width - textLeft - pad - 116f, 38f)); // 오른쪽 116 = 쿨다운 묶음 자리
        title.fontSize = 28f;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.TopLeft;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;

        Place(subtitle.rectTransform, new Vector2(textLeft, -50f), new Vector2(width - textLeft - pad, 24f));
        subtitle.fontSize = 17f;
        subtitle.fontStyle = FontStyles.Normal;
        subtitle.alignment = TextAlignmentOptions.TopLeft;
        subtitle.color = MutedColor;

        // 우상단 쿨다운 = 시계 아이콘 + 초. 숫자 폭이 바뀌어도 오른쪽 끝에 붙게 가로 레이아웃(오른쪽 정렬).
        RectTransform group = NewUi("CooldownGroup", header);
        group.anchorMin = group.anchorMax = new Vector2(0f, 1f); // Header 는 왼쪽 위 기준 고정 폭
        group.pivot = new Vector2(1f, 1f);
        group.anchoredPosition = new Vector2(width - pad, -16f);
        group.sizeDelta = new Vector2(110f, 30f);
        HorizontalLayoutGroup row = group.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleRight;
        row.spacing = 6f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        RectTransform clock = NewUi("CooldownIcon", group);
        Image clockImage = clock.gameObject.AddComponent<Image>();
        clockImage.sprite = white;
        clockImage.color = MutedColor;
        clockImage.raycastTarget = false;
        clockImage.preserveAspect = true;
        LayoutElement clockSize = clock.gameObject.AddComponent<LayoutElement>();
        clockSize.preferredWidth = clockSize.minWidth = 22f;
        clockSize.preferredHeight = clockSize.minHeight = 22f;

        cooldown.rectTransform.SetParent(group, false);
        cooldown.fontSize = 22f;
        cooldown.fontStyle = FontStyles.Bold;
        cooldown.alignment = TextAlignmentOptions.MidlineRight;
        cooldown.textWrappingMode = TextWrappingModes.NoWrap;
        cooldown.spriteAsset = null; // 시계는 옆 Image — 옛 <sprite name=cooldown> 은 쓰지 않는다

        // 태그 칩 줄 — 이름·슬롯 줄 아래. 칩 위치·줄바꿈은 SkillTooltipView 가 매 프레임 놓는다.
        RectTransform tagRow = NewUi("TagRow", header);
        Place(tagRow, new Vector2(textLeft, -80f), new Vector2(width - textLeft - pad, 26f));

        RectTransform chip = NewUi("TagChip", tagRow);
        Place(chip, Vector2.zero, new Vector2(60f, 26f));
        chip.gameObject.AddComponent<Image>(); // 둥근 스프라이트·색은 StyleTagChip
        RectTransform chipLabelRect = NewUi("Label", chip);
        Stretch(chipLabelRect);
        TextMeshProUGUI chipLabel = chipLabelRect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            chipLabel.font = font;
        chipLabel.fontSize = 15f;
        chipLabel.fontStyle = FontStyles.Bold;
        chipLabel.alignment = TextAlignmentOptions.Center;
        chipLabel.textWrappingMode = TextWrappingModes.NoWrap;
        chipLabel.overflowMode = TextOverflowModes.Ellipsis;
        chipLabel.color = AccentColor;
        chipLabel.raycastTarget = false;
        chip.gameObject.SetActive(false);

        // 머리 영역 아래 가로 구분선 — Header 바닥에 붙어 태그가 여러 줄이면 같이 내려간다.
        RectTransform divider = NewUi("Divider", header);
        divider.anchorMin = new Vector2(0f, 0f);
        divider.anchorMax = new Vector2(1f, 0f);
        divider.pivot = new Vector2(0.5f, 0f);
        divider.offsetMin = new Vector2(pad, 0f);
        divider.offsetMax = new Vector2(-pad, 1.5f);
        Image dividerImage = divider.gameObject.AddComponent<Image>();
        dividerImage.color = new Color(0.48f, 0.42f, 0.25f, 0.7f);
        dividerImage.raycastTarget = false;

        // 설명은 구분선 아래 여백, Shift 안내는 오른쪽 아래.
        body.margin = new Vector4(pad, 12f, pad, 0f);
        hint.margin = new Vector4(pad, 8f, pad, 0f);
        hint.alignment = TextAlignmentOptions.Right;
        VerticalLayoutGroup panelGroup = panel.GetComponent<VerticalLayoutGroup>();
        if (panelGroup != null)
            panelGroup.padding = new RectOffset(0, 0, 0, 12);

        return new TooltipLayout(group.gameObject, tagRow, chip, headerLayout);
    }

    // 할 일 114 후속 — 태그 칩 = 둥근 사각형 9-slice(생성물 · git) + 태그별 색 표.
    private const string TagChipSpritePath = "Assets/2.Prefabs/UI/Textures/SkillTooltipTagChip.png";
    private const string TagStylesPath = GeneratedFolder + "/SkillTooltipTagStyles.asset";
    private const int TagChipTextureSize = 64;
    private const int TagChipBorder = 24;        // 9-slice 보더(px) — 모서리 반지름 + AA 여유
    private const float TagChipRadius = 22f;     // px, PPU 200 → 11 UI 단위(칩 높이 26 의 거의 반 = 알약에 가까운 둥근 사각형)
    private const float TagChipPixelsPerUnit = 200f;
    // 칩 글자만 서울알림체 Medium — CombatHUD 등 HUD 가 쓰는 정적 에셋(git, 태그 글자 전부 있음). 다른 툴팁 글자는 FontPath 그대로.
    // Heavy(SVN 50.Art)는 설정창 전용 · 아틀라스에 넉·백·막·저가 없다.
    private const string TagChipFontPath = "Assets/Fonts/SeoulAlrim/SeoulAlrimTTF-Medium SDF.asset";

    // 칩 테두리 = 같은 반지름·보더의 흰 둥근 링 9-slice(생성물 · git). 두께(px)는 링 PNG 임포터 userData 에 적어 두고, 바꾸면 그 자리에서 다시 굽는다(guid 유지).
    private const string TagChipRingSpritePath = "Assets/2.Prefabs/UI/Textures/SkillTooltipTagChipRing.png";
    private const string RingThicknessKey = "ringThicknessPx=";
    /// <summary>링 두께 기본값(px) — PPU 200 → 1.75 UI 단위 = 칩 높이 26 의 약 6.7%(참고 이미지 비율).</summary>
    public const float DefaultTagChipRingThickness = 3.5f;
    public const float MinTagChipRingThickness = 1f;
    public const float MaxTagChipRingThickness = 12f;

    /// <summary>
    /// 칩 원본을 둥근 9-slice 로 — 배경 Image = <see cref="TagChipSpritePath"/>·Sliced(글자 길이로 늘어나도 모서리 유지),
    /// 그 위에 자식 <see cref="SkillTooltipView.TagChipRingName"/> = 링 <see cref="TagChipRingSpritePath"/>·Sliced(글자색 테두리, 글자 아래).
    /// 칩 글자 글꼴 = <see cref="TagChipFontPath"/>(서울알림체 Medium).
    /// 옛 금색 <see cref="Outline"/> 은 지운다(사각 오프셋 복제라 둥근 모서리와 어긋난다).
    /// 색은 런타임에 <see cref="SkillTooltipTagStyles"/> 가 덮는다 — 여기 색은 표가 비었을 때의 기본색.
    /// </summary>
    private static void StyleTagChip(RectTransform chip)
    {
        if (chip == null)
            return;

        Image image = EnsureComponent<Image>(chip.gameObject);
        image.sprite = EnsureTagChipSprite();
        image.type = Image.Type.Sliced;
        image.fillCenter = true;
        image.pixelsPerUnitMultiplier = 1f;
        image.color = SkillTooltipTagStyles.FallbackBackground;
        image.raycastTarget = false;
        if (chip.TryGetComponent(out Outline outline))
            Object.DestroyImmediate(outline, true);

        var ring = chip.Find(SkillTooltipView.TagChipRingName) as RectTransform;
        if (ring == null)
            ring = NewUi(SkillTooltipView.TagChipRingName, chip);
        Stretch(ring);
        ring.SetSiblingIndex(0); // 배경 위 · 글자 아래
        Image ringImage = EnsureComponent<Image>(ring.gameObject);
        ringImage.sprite = EnsureTagChipRingSprite(null);
        ringImage.type = Image.Type.Sliced;
        ringImage.fillCenter = true;
        ringImage.pixelsPerUnitMultiplier = 1f;
        ringImage.color = SkillTooltipTagStyles.FallbackText;
        ringImage.raycastTarget = false;

        TMP_Text label = chip.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.color = SkillTooltipTagStyles.FallbackText;
            TMP_FontAsset chipFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TagChipFontPath);
            if (chipFont != null)
                label.font = chipFont; // 크기 15·Bold·가운데 정렬은 그대로(세로 어긋남 0.5 UI 단위 이하라 보정 없음)
            else
                Debug.LogWarning($"[SkillTooltip] 칩 글꼴 없음: {TagChipFontPath} — 칩 글자 글꼴을 바꾸지 않는다.");
        }
    }

    /// <summary>
    /// 흰 둥근 사각형 PNG 를 한 번 굽는다(있으면 그대로 — 임포트 설정만 맞춘다). 64×64, 반지름 22px, 가장자리 1px 부드럽게,
    /// 9-slice 보더 24px 사방. 색은 Image.color 로 입힌다.
    /// </summary>
    private static Sprite EnsureTagChipSprite()
    {
        if (!System.IO.File.Exists(TagChipSpritePath))
            BakeRoundedRect(TagChipSpritePath, 0f);
        return EnsureTagChipImport(TagChipSpritePath, null);
    }

    /// <summary>
    /// 흰 둥근 링 PNG(칩과 같은 64px·반지름 22px·보더 24px, 바깥 가장자리 = 칩 가장자리). <paramref name="thickness"/> = null 이면
    /// 지금 구운 두께(없으면 <see cref="DefaultTagChipRingThickness"/>)를 쓴다. 파일이 없거나 두께가 다르면 그 자리에 다시 굽는다.
    /// </summary>
    public static Sprite EnsureTagChipRingSprite(float? thickness)
    {
        float? baked = BakedRingThickness();
        float want = Mathf.Clamp(thickness ?? baked ?? DefaultTagChipRingThickness, MinTagChipRingThickness, MaxTagChipRingThickness);
        if (!System.IO.File.Exists(TagChipRingSpritePath) || baked == null || !Mathf.Approximately(baked.Value, want))
            BakeRoundedRect(TagChipRingSpritePath, want);
        return EnsureTagChipImport(TagChipRingSpritePath, RingThicknessKey + want.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>지금 링 PNG 에 구운 두께(px). 파일·기록이 없으면 null.</summary>
    public static float? BakedRingThickness()
    {
        if (!(AssetImporter.GetAtPath(TagChipRingSpritePath) is TextureImporter importer) ||
            importer.userData == null || !importer.userData.StartsWith(RingThicknessKey, StringComparison.Ordinal))
            return null;
        return float.TryParse(importer.userData.Substring(RingThicknessKey.Length), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : (float?)null;
    }

    // 흰 둥근 사각형(ring = 0) 또는 두께 ring px 의 둥근 링. 링 안쪽 = 같은 모서리 중심·반지름 (22 − ring) — 두께가 둘레 전체에서 같다.
    private static void BakeRoundedRect(string path, float ring)
    {
        const int size = TagChipTextureSize;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        float lo = TagChipRadius, hi = size - TagChipRadius;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            float dx = fx < lo ? lo - fx : fx > hi ? fx - hi : 0f;
            float dy = fy < lo ? lo - fy : fy > hi ? fy - hi : 0f;
            float d = Mathf.Sqrt(dx * dx + dy * dy); // 안쪽 사각형까지 거리
            float coverage = Mathf.Clamp01(TagChipRadius - d + 0.5f);
            if (ring > 0f)
                coverage -= Mathf.Clamp01(TagChipRadius - ring - d + 0.5f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(coverage)));
        }

        tex.SetPixels32(px);
        tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    // Sprite·Single·PPU 200·보더 24px 사방. userData 가 null 이 아니면 그 값도 맞춘다(링 두께 기록).
    private static Sprite EnsureTagChipImport(string path, string userData)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var border = new Vector4(TagChipBorder, TagChipBorder, TagChipBorder, TagChipBorder);
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != border ||
            !Mathf.Approximately(importer.spritePixelsPerUnit, TagChipPixelsPerUnit) ||
            (userData != null && importer.userData != userData))
        {
            if (userData != null)
                importer.userData = userData;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = TagChipPixelsPerUnit;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>태그 칩 색 표 에셋 — 없을 때만 기본 색 초안으로 만든다(이후 값은 xlsx `UI` 시트·인스펙터가 원본).</summary>
    private static SkillTooltipTagStyles EnsureTagStyles()
    {
        var styles = AssetDatabase.LoadAssetAtPath<SkillTooltipTagStyles>(TagStylesPath);
        if (styles != null)
            return styles;

        EnsureFolder(GeneratedFolder);
        styles = ScriptableObject.CreateInstance<SkillTooltipTagStyles>();
        styles.SetEntries(SkillTooltipTagStyles.DefaultBackgroundHex, SkillTooltipTagStyles.DefaultTextHex,
            SkillTooltipTagStyles.DefaultStyles());
        AssetDatabase.CreateAsset(styles, TagStylesPath);
        return styles;
    }

    /// <summary>
    /// 패널 세로 크기 = 내용 높이(가로 고정). 머리줄은 <c>Header</c> 로 묶어 배치를 그대로 두고, 설명·Shift 안내만 세로로 흐른다.
    /// <c>Header</c> 가 없을 때 한 번만 지금 절대 배치를 Header 높이·TMP margin·아래 여백으로 옮긴다 — 이후 간격은 프리팹 인스펙터에서 고친다.
    /// </summary>
    private static void EnsureFlexibleHeight(RectTransform panel, TMP_Text body, TMP_Text hint, params RectTransform[] headerItems)
    {
        if (panel.Find("Header") != null)
            return;

        float width = panel.sizeDelta.x;
        RectTransform bodyRect = body.rectTransform;
        RectTransform hintRect = hint.rectTransform;
        float bodyTop = -bodyRect.anchoredPosition.y;
        float bodyBottom = bodyTop + bodyRect.sizeDelta.y;
        float hintTop = -hintRect.anchoredPosition.y;
        float hintBottom = hintTop + hintRect.sizeDelta.y;

        RectTransform header = NewUi("Header", panel);
        Place(header, Vector2.zero, new Vector2(width, bodyTop));
        foreach (RectTransform item in headerItems)
            item.SetParent(header, false); // Header 왼쪽 위 = Panel 왼쪽 위 → 머리줄 배치 그대로
        LayoutElement headerLayout = header.gameObject.AddComponent<LayoutElement>();
        headerLayout.minHeight = bodyTop;
        headerLayout.preferredHeight = bodyTop;

        // 레이아웃이 폭을 패널 전체로 잡으므로 지금 좌우 위치·간격은 margin(왼, 위, 오른, 아래)으로 옮긴다.
        body.margin += new Vector4(bodyRect.anchoredPosition.x, 0f, width - bodyRect.anchoredPosition.x - bodyRect.sizeDelta.x, 0f);
        hint.margin += new Vector4(hintRect.anchoredPosition.x, hintTop - bodyBottom, width - hintRect.anchoredPosition.x - hintRect.sizeDelta.x, 0f);
        header.SetSiblingIndex(0);
        bodyRect.SetSiblingIndex(1);
        hintRect.SetSiblingIndex(2);

        VerticalLayoutGroup group = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        group.padding = new RectOffset(0, 0, 0, Mathf.Max(0, Mathf.RoundToInt(panel.sizeDelta.y - hintBottom)));
        group.spacing = 0f;
        group.childAlignment = TextAnchor.UpperLeft;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;

        ContentSizeFitter fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private static void SeedTooltipSources()
    {
        SeedSkills(PaladinData, new Dictionary<PlayerSkillSlot, (string, string, string)>
        {
            { PlayerSkillSlot.Main, ("진격의 방패", "Q 스킬", "TODO 전진하며 적에게 {dmg} 피해를 줍니다.") },
            { PlayerSkillSlot.Sub, ("수호자의 의지", "E 스킬", "전방의 공격을 막으면 반격하여 {dmg}의 피해를 입힙니다. 반격 중에는 피해를 무시합니다.") },
            { PlayerSkillSlot.Interrupt, ("단죄의 방패", "우클릭 스킬", "TODO 적을 간파해 {dmg} 피해를 줍니다.") },
            { PlayerSkillSlot.Ultimate, ("최후의 심판", "궁극기", "TODO 대상을 심판해 {dmg} 피해를 줍니다.") },
        });

        SeedSkills(GunnerData, new Dictionary<PlayerSkillSlot, (string, string, string)>
        {
            { PlayerSkillSlot.Main, ("충전 레이저", "Q 스킬", "TODO 집중도와 과열 단계에 따라 {dmg} 피해를 줍니다.") },
            { PlayerSkillSlot.Sub, ("냉각 백스텝", "E 스킬", "TODO 뒤로 {distance}m 이동하고 과열도를 초기화합니다.") },
            { PlayerSkillSlot.Interrupt, ("근접 간파", "우클릭 스킬", "TODO 근접 공격을 간파해 {dmg} 피해를 줍니다.") },
            { PlayerSkillSlot.Ultimate, ("추적 레이저", "궁극기", "TODO 대상을 추적하며 틱마다 {dmg} 피해를 줍니다.") },
        });

        GameObject paladin = PrefabUtility.LoadPrefabContents(PaladinPrefabPath);
        try
        {
            FirstMeleePassive passive = paladin.GetComponent<FirstMeleePassive>();
            if (passive != null)
                SeedTooltip(passive, "불굴의 의지", string.Empty,
                    "TODO 준비되면 다음 공격에 {dmg} 추가 피해를 주고 최대 체력의 {healPercent}%를 회복합니다.");
            PrefabUtility.SaveAsPrefabAsset(paladin, PaladinPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(paladin);
        }

        GunnerHeatData heat = AssetDatabase.LoadAssetAtPath<GunnerHeatData>(
            "Assets/9.ScriptableObject/Player/Gunner/GunnerHeatData.asset");
        SeedTooltip(heat, "과열", string.Empty,
            "TODO 과열 단계에 따라 기본 공격 피해가 {dmg} 범위로 강화됩니다.");

        SeedAssassinTooltips();
    }

    /// <summary>
    /// 어쌔신 스킬·패시브 툴팁 임시 문구(PLAN-assassin A12, 기획서 표현을 짧게). 변신 Q/E 는 대체 세트라 슬롯 사전 대신 경로로 직접 채운다.
    /// 비어 있을 때만 채우므로 어쌔신 저작 메뉴(8번)도 CombatHUD 를 건드리지 않고 이것만 부른다.
    /// </summary>
    public static void SeedAssassinTooltips()
    {
        foreach ((string path, string name, string subtitle, string description) in AssassinTooltips)
        {
            Object data = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (data == null)
            {
                Debug.LogWarning($"[SkillTooltip] 데이터 에셋 없음: {path}");
                continue;
            }
            SeedTooltip(data, name, subtitle, description);
        }
    }

    private static void SeedSkills(
        Dictionary<PlayerSkillSlot, string> paths,
        Dictionary<PlayerSkillSlot, (string name, string subtitle, string description)> texts)
    {
        foreach (KeyValuePair<PlayerSkillSlot, string> pair in paths)
        {
            PlayerSkillData data = AssetDatabase.LoadAssetAtPath<PlayerSkillData>(pair.Value);
            if (data == null)
            {
                Debug.LogWarning($"[SkillTooltip] 데이터 에셋 없음: {pair.Value}");
                continue;
            }
            texts.TryGetValue(pair.Key, out (string name, string subtitle, string description) text);
            SeedTooltip(data, text.name, text.subtitle, text.description);
        }
    }

    /// <summary>문구는 비어 있을 때만 채운다. 아이콘은 지정하지 않는다(스킬 아이콘 아트가 나오면 인스펙터에서).</summary>
    private static void SeedTooltip(Object target, string name, string subtitle, string description)
    {
        if (target == null)
            return;
        var so = new SerializedObject(target);
        SerializedProperty tooltip = so.FindProperty("tooltip");
        if (tooltip == null)
            return;
        SetIfEmpty(tooltip.FindPropertyRelative("displayName"), name);
        SetIfEmpty(tooltip.FindPropertyRelative("subtitle"), subtitle);
        SetIfEmpty(tooltip.FindPropertyRelative("description"), description);
        SerializedProperty iconProperty = tooltip.FindPropertyRelative("icon");
        if (IsNotSkillIcon(iconProperty.objectReferenceValue as Sprite))
            iconProperty.objectReferenceValue = null;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    // 이전 저작이 잘못 넣은 값 정리: 슬롯 프레임(slot_skill.png) · 임시 생성 아이콘은 스킬 아이콘이 아니다.
    private static bool IsNotSkillIcon(Sprite sprite)
    {
        if (sprite == null)
            return false;
        string path = AssetDatabase.GetAssetPath(sprite);
        return path == SlotFramePath || path.StartsWith(GeneratedFolder + "/", StringComparison.Ordinal);
    }

    /// <summary>
    /// 슬롯 안 마스크 밑 아이콘 Image(프리팹에서 직접 만든 것 — <c>Slot_Q/Icon_mask/Icon_Q</c>, <c>Slot_Passive/Icon_mask/Icon_Passive</c>)를 찾아 쓴다.
    /// 위치·크기·스프라이트는 건드리지 않는다. 슬롯 루트(프레임 slot_skill)·마스크 Image 는 아이콘으로 쓰지 않는다.
    /// </summary>
    private static Image FindSlotIcon(Transform slotRoot, string iconPath)
    {
        Image image = slotRoot.Find(iconPath)?.GetComponent<Image>();
        if (image == null)
            Debug.LogWarning($"[SkillTooltip] {slotRoot.name}/{iconPath} Image 가 없다 — 프리팹에 만들어 둘 것(아이콘 없이 진행).");
        return image;
    }

    private static string SlotIconPath(PlayerSkillSlot slot) => slot switch
    {
        PlayerSkillSlot.Main => "Icon_mask/Icon_Q",
        PlayerSkillSlot.Sub => "Icon_mask/Icon_E",
        PlayerSkillSlot.Interrupt => "Icon_mask/Icon_RMB",
        PlayerSkillSlot.Ultimate => "Icon_mask/Icon_Ult",
        _ => string.Empty,
    };

    private static void EnableFrameRaycast(Transform slotRoot)
    {
        Image frame = slotRoot.GetComponent<Image>();
        if (frame != null)
            frame.raycastTarget = true;
    }

    private static TMP_StyleSheet EnsureStyleSheet()
    {
        TMP_StyleSheet sheet = AssetDatabase.LoadAssetAtPath<TMP_StyleSheet>(StylePath);
        if (sheet != null)
            return sheet;
        sheet = ScriptableObject.CreateInstance<TMP_StyleSheet>();
        var definitions = new (string name, string opening, string closing)[]
        {
            ("stun", "<color=#F2C14E>", "</color>"),
            ("shield", "<color=#74C7EC>", "</color>"),
            ("heal", "<color=#72D572>", "</color>"),
            ("overheat", "<color=#FF6B4A>", "</color>"),
            ("slow", "<color=#A9B8FF>", "</color>"),
        };
        var serialized = new SerializedObject(sheet);
        SerializedProperty styles = serialized.FindProperty("m_StyleList");
        styles.arraySize = definitions.Length;
        for (int i = 0; i < definitions.Length; i++)
        {
            SerializedProperty style = styles.GetArrayElementAtIndex(i);
            style.FindPropertyRelative("m_Name").stringValue = definitions[i].name;
            style.FindPropertyRelative("m_OpeningDefinition").stringValue = definitions[i].opening;
            style.FindPropertyRelative("m_ClosingDefinition").stringValue = definitions[i].closing;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        sheet.RefreshStyles();
        AssetDatabase.CreateAsset(sheet, StylePath);
        return sheet;
    }

    [MenuItem("Tools/UI/스킬 툴팁 인라인 아이콘 갱신")]
    public static void RebuildInlineIcons()
    {
        EnsureFolder(GeneratedFolder);
        EnsureAttackSpriteAsset();
        AssetDatabase.SaveAssets();
        Debug.Log($"[SkillTooltip] 인라인 아이콘 갱신 — {string.Join(", ", Array.ConvertAll(InlineIcons, i => i.name))}");
    }

    private static TMP_SpriteAsset EnsureAttackSpriteAsset()
    {
        TMP_SpriteAsset existing = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(AttackSpritePath);
        if (existing != null)
        {
            FillInlineIcons(existing);
            return existing;
        }
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(AttackTexturePath) == null)
            AssetDatabase.CreateAsset(new Texture2D(1, 1, TextureFormat.RGBA32, false), AttackTexturePath);
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AttackTexturePath);

        var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        asset.name = "SkillTooltipAttackSprite";
        asset.spriteSheet = texture;
        Shader shader = Shader.Find("TextMeshPro/Sprite");
        if (shader == null)
            throw new InvalidOperationException("TextMeshPro/Sprite 셰이더를 찾지 못했다.");
        var material = new Material(shader) { name = "SkillTooltipAttackSprite Material" };
        material.SetTexture("_MainTex", texture);
        asset.material = material;
        AssetDatabase.CreateAsset(asset, AttackSpritePath);
        AssetDatabase.AddObjectToAsset(material, asset);
        // 새 인스턴스는 m_Version 이 비어 있어 UpdateLookupTables 가 레거시 업그레이드(spriteInfoList null → NRE)로 빠진다.
        var serialized = new SerializedObject(asset);
        serialized.FindProperty("m_Version").stringValue = "1.1.0";
        serialized.ApplyModifiedPropertiesWithoutUndo();
        FillInlineIcons(asset);
        return asset;
    }

    /// <summary>
    /// <see cref="InlineIcons"/> PNG 를 한 줄 아틀라스로 줄여 텍스처 에셋을 제자리에서 다시 쓰고(guid 유지),
    /// TMP 스프라이트 표를 그 칸들로 다시 만든다. 원본 PNG 는 임포트 설정과 무관하게 파일 바이트로 읽는다.
    /// </summary>
    private static void FillInlineIcons(TMP_SpriteAsset asset)
    {
        Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AttackTexturePath);
        if (atlas == null)
            throw new InvalidOperationException($"{AttackTexturePath} 텍스처가 없다.");

        const int size = InlineIconSize;
        atlas.Reinitialize(size * InlineIcons.Length, size, TextureFormat.RGBA32, true);
        atlas.filterMode = FilterMode.Bilinear;
        atlas.wrapMode = TextureWrapMode.Clamp;
        for (int i = 0; i < InlineIcons.Length; i++)
            atlas.SetPixels32(i * size, 0, size, size, LoadDownscaled(InlineIcons[i].png, size));
        atlas.Apply(true);

        foreach (Object child in AssetDatabase.LoadAllAssetsAtPath(AttackTexturePath))
            if (child is Sprite)
                Object.DestroyImmediate(child, true);

        asset.spriteSheet = atlas;
        asset.material.SetTexture("_MainTex", atlas);
        asset.spriteGlyphTable.Clear();
        asset.spriteCharacterTable.Clear();
        for (int i = 0; i < InlineIcons.Length; i++)
        {
            var rect = new Rect(i * size, 0, size, size);
            Sprite sprite = Sprite.Create(atlas, rect, new Vector2(0.5f, 0.5f), size);
            sprite.name = InlineIcons[i].name;
            AssetDatabase.AddObjectToAsset(sprite, atlas);

            var metrics = new GlyphMetrics(size, size, 0f, size, size);
            var glyph = new TMP_SpriteGlyph((uint)i, metrics, new GlyphRect(i * size, 0, size, size), 1f, 0, sprite);
            asset.spriteGlyphTable.Add(glyph);
            asset.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xE000u + (uint)i, glyph) { name = InlineIcons[i].name });
        }
        asset.UpdateLookupTables();
        EditorUtility.SetDirty(atlas);
        EditorUtility.SetDirty(asset.material);
        EditorUtility.SetDirty(asset);
    }

    // 원본(1254px)을 칸 크기로 상자 평균 축소. 투명 가장자리 색이 번지지 않게 알파 가중 평균.
    private static Color32[] LoadDownscaled(string pngPath, int size)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(System.IO.File.ReadAllBytes(pngPath)))
                throw new InvalidOperationException($"PNG 를 읽지 못했다: {pngPath}");
            Color32[] src = source.GetPixels32();
            int sw = source.width, sh = source.height;
            var result = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int x0 = x * sw / size, x1 = Mathf.Max(x0 + 1, (x + 1) * sw / size);
                int y0 = y * sh / size, y1 = Mathf.Max(y0 + 1, (y + 1) * sh / size);
                double r = 0, g = 0, b = 0, a = 0;
                for (int sy = y0; sy < y1; sy++)
                for (int sx = x0; sx < x1; sx++)
                {
                    Color32 c = src[sy * sw + sx];
                    r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                }
                int count = (x1 - x0) * (y1 - y0);
                result[y * size + x] = a <= 0
                    ? new Color32(0, 0, 0, 0)
                    : new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)Math.Round(a / count));
            }
            return result;
        }
        finally
        {
            Object.DestroyImmediate(source);
        }
    }

    // 이미 있는 오브젝트는 배치·서식을 건드리지 않는다 — 프리팹에서 손본 값이 재실행에 덮이지 않게(새로 만들 때만 기본값).
    private static Image EnsureImage(RectTransform parent, string name, Vector2 position, Vector2 size)
    {
        if (FindDeep(parent, name) is RectTransform existing && existing.TryGetComponent(out Image found))
            return found;
        RectTransform rect = NewUi(name, parent);
        Place(rect, position, size);
        Image image = EnsureComponent<Image>(rect.gameObject);
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    private static TMP_Text EnsureText(
        RectTransform parent, string name, TMP_FontAsset font, float size, FontStyles style,
        TextAlignmentOptions alignment, Vector2 position, Vector2 dimensions)
    {
        if (FindDeep(parent, name) is RectTransform existing && existing.TryGetComponent(out TMP_Text found))
            return found;
        RectTransform rect = NewUi(name, parent);
        Place(rect, position, dimensions);
        TextMeshProUGUI text = EnsureComponent<TextMeshProUGUI>(rect.gameObject);
        if (font != null)
            text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.color = Color.white;
        return text;
    }

    private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeft;
        rect.sizeDelta = size;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name)
                return child;
        return null;
    }

    private static string SlotName(PlayerSkillSlot slot) => slot switch
    {
        PlayerSkillSlot.Main => "Slot_Q",
        PlayerSkillSlot.Sub => "Slot_E",
        PlayerSkillSlot.Interrupt => "Slot_RMB",
        PlayerSkillSlot.Ultimate => "Slot_R",
        _ => string.Empty,
    };

    private static RectTransform NewUi(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static T EnsureComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }

    private static void SetReference(Object target, string property, Object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty reference = so.FindProperty(property);
        if (reference != null)
        {
            reference.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetIfEmpty(SerializedProperty property, string value)
    {
        if (property != null && string.IsNullOrEmpty(property.stringValue))
            property.stringValue = value ?? string.Empty;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
