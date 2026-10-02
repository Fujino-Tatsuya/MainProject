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
    private const string AttackTexturePath = GeneratedFolder + "/SkillTooltipAttackIcon.asset";
    private const string AttackSpritePath = GeneratedFolder + "/SkillTooltipAttackSprite.asset";
    private const string PaladinPrefabPath = "Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab";

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

    [MenuItem("Tools/UI/스킬 툴팁 구성")]
    public static void Configure()
    {
        EnsureFolder(GeneratedFolder);
        TMP_StyleSheet styleSheet = EnsureStyleSheet();
        TMP_SpriteAsset attackSprite = EnsureAttackSpriteAsset();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Dictionary<PlayerSkillSlot, Sprite> paladinIcons = ConfigureHud(styleSheet, attackSprite, font, out Sprite passiveIcon);
        SeedTooltipSources(paladinIcons, passiveIcon);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SkillTooltip] 구성 완료 — {HudPath}. GameData.xlsx Export/병합은 별도로 실행할 것.");
    }

    private static Dictionary<PlayerSkillSlot, Sprite> ConfigureHud(
        TMP_StyleSheet styleSheet, TMP_SpriteAsset attackSprite, TMP_FontAsset font, out Sprite passiveIcon)
    {
        var icons = new Dictionary<PlayerSkillSlot, Sprite>();
        passiveIcon = null;
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
                Image icon = FindIcon(slotRoot, fill);
                if (icon != null)
                    icon.raycastTarget = true;
                widget.FindPropertyRelative("icon").objectReferenceValue = icon;
                widget.FindPropertyRelative("hover").objectReferenceValue = hover;
                if (icon != null)
                    icons[slot] = icon.sprite;
            }
            skillSo.ApplyModifiedPropertiesWithoutUndo();

            var passiveSo = new SerializedObject(passiveHud);
            Image passiveImage = passiveSo.FindProperty("icon").objectReferenceValue as Image;
            Transform passiveRoot = FindDeep(root.transform, "Slot_P")
                ?? (passiveImage != null ? passiveImage.transform.parent : null);
            if (passiveRoot != null)
            {
                passiveRoot.gameObject.SetActive(true);
                passiveImage ??= passiveRoot.GetComponent<Image>();
                if (passiveImage != null)
                {
                    passiveImage.raycastTarget = true;
                    passiveSo.FindProperty("icon").objectReferenceValue = passiveImage;
                }
                SkillSlotHover hover = EnsureComponent<SkillSlotHover>(passiveRoot.gameObject);
                SetReference(hover, "tooltipView", view);
                passiveSo.FindProperty("hover").objectReferenceValue = hover;
                passiveIcon = passiveImage != null ? passiveImage.sprite : null;
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
            return icons;
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
        GameObject host = found != null ? found.gameObject : NewUi("SkillTooltipHost", canvas.transform).gameObject;
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

        Image icon = EnsureImage(panel, "Icon", new Vector2(16f, -16f), new Vector2(64f, 64f));
        TMP_Text key = EnsureText(panel, "KeyBadge", font, 18f, FontStyles.Bold, TextAlignmentOptions.Center,
            new Vector2(88f, -18f), new Vector2(54f, 28f));
        TMP_Text title = EnsureText(panel, "DisplayName", font, 25f, FontStyles.Bold, TextAlignmentOptions.TopLeft,
            new Vector2(88f, -48f), new Vector2(240f, 34f));
        TMP_Text subtitle = EnsureText(panel, "Subtitle", font, 16f, FontStyles.Italic, TextAlignmentOptions.TopLeft,
            new Vector2(88f, -80f), new Vector2(240f, 24f));
        TMP_Text cooldown = EnsureText(panel, "Cooldown", font, 17f, FontStyles.Normal, TextAlignmentOptions.TopRight,
            new Vector2(330f, -20f), new Vector2(84f, 28f));
        TMP_Text body = EnsureText(panel, "Description", font, 18f, FontStyles.Normal, TextAlignmentOptions.TopLeft,
            new Vector2(16f, -104f), new Vector2(398f, 112f));
        TMP_Text hint = EnsureText(panel, "ShiftHint", font, 14f, FontStyles.Italic, TextAlignmentOptions.Center,
            new Vector2(16f, -230f), new Vector2(398f, 24f));
        body.styleSheet = styleSheet;
        body.spriteAsset = attackSprite;
        body.textWrappingMode = TextWrappingModes.Normal;
        hint.color = new Color(0.72f, 0.74f, 0.78f, 1f);
        key.color = new Color(0.95f, 0.82f, 0.42f, 1f);

        var so = new SerializedObject(view);
        so.FindProperty("panel").objectReferenceValue = panel.gameObject;
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("keyBadge").objectReferenceValue = key;
        so.FindProperty("displayName").objectReferenceValue = title;
        so.FindProperty("subtitle").objectReferenceValue = subtitle;
        so.FindProperty("cooldownText").objectReferenceValue = cooldown;
        so.FindProperty("description").objectReferenceValue = body;
        so.FindProperty("shiftHint").objectReferenceValue = hint;
        so.ApplyModifiedPropertiesWithoutUndo();
        panel.gameObject.SetActive(false);
        return view;
    }

    private static void SeedTooltipSources(Dictionary<PlayerSkillSlot, Sprite> paladinIcons, Sprite passiveIcon)
    {
        SeedSkills(PaladinData, paladinIcons, new Dictionary<PlayerSkillSlot, (string, string, string)>
        {
            { PlayerSkillSlot.Main, ("진격의 방패", "Q 스킬", "TODO 전진하며 적에게 {dmg} 피해를 줍니다.") },
            { PlayerSkillSlot.Sub, ("수호자의 의지", "E 스킬", "TODO {shieldAmount}의 보호막을 {shieldDuration:0.0}초 동안 얻습니다.") },
            { PlayerSkillSlot.Interrupt, ("단죄의 방패", "우클릭 스킬", "TODO 적을 간파해 {dmg} 피해를 줍니다.") },
            { PlayerSkillSlot.Ultimate, ("최후의 심판", "궁극기", "TODO 대상을 심판해 {dmg} 피해를 줍니다.") },
        });

        var gunnerIcons = new Dictionary<PlayerSkillSlot, Sprite>();
        foreach (PlayerSkillSlot slot in Enum.GetValues(typeof(PlayerSkillSlot)))
            gunnerIcons[slot] = EnsureTemporaryIcon("Gunner_" + slot, SlotColor(slot));
        SeedSkills(GunnerData, gunnerIcons, new Dictionary<PlayerSkillSlot, (string, string, string)>
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
                    "TODO 준비되면 다음 공격에 {dmg} 추가 피해를 주고 최대 체력의 {healPercent}%를 회복합니다.", passiveIcon);
            PrefabUtility.SaveAsPrefabAsset(paladin, PaladinPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(paladin);
        }

        GunnerHeatData heat = AssetDatabase.LoadAssetAtPath<GunnerHeatData>(
            "Assets/9.ScriptableObject/Player/Gunner/GunnerHeatData.asset");
        SeedTooltip(heat, "과열", string.Empty,
            "TODO 과열 단계에 따라 기본 공격 피해가 {dmg} 범위로 강화됩니다.",
            EnsureTemporaryIcon("Gunner_Passive", new Color(1f, 0.35f, 0.18f, 1f)));
    }

    private static void SeedSkills(
        Dictionary<PlayerSkillSlot, string> paths,
        Dictionary<PlayerSkillSlot, Sprite> icons,
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
            icons.TryGetValue(pair.Key, out Sprite icon);
            SeedTooltip(data, text.name, text.subtitle, text.description, icon);
        }
    }

    private static void SeedTooltip(Object target, string name, string subtitle, string description, Sprite icon)
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
        if (iconProperty.objectReferenceValue == null && icon != null)
            iconProperty.objectReferenceValue = icon;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
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

    private static TMP_SpriteAsset EnsureAttackSpriteAsset()
    {
        TMP_SpriteAsset existing = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(AttackSpritePath);
        if (existing != null)
            return existing;
        EnsureTextureWithSprite(AttackTexturePath, "atk", new Color(0.96f, 0.69f, 0.25f, 1f), true);
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AttackTexturePath);
        Sprite sprite = null;
        foreach (Object child in AssetDatabase.LoadAllAssetsAtPath(AttackTexturePath))
            if (child is Sprite found)
                sprite = found;
        if (texture == null || sprite == null)
            throw new InvalidOperationException("임시 공격력 스프라이트 생성에 실패했다.");

        var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        asset.name = "SkillTooltipAttackSprite";
        asset.spriteSheet = texture;
        Rect rect = sprite.rect;
        var metrics = new GlyphMetrics(rect.width, rect.height, 0f, rect.height, rect.width);
        var glyphRect = new GlyphRect(
            Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y),
            Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height));
        var glyph = new TMP_SpriteGlyph(0, metrics, glyphRect, 1f, 0, sprite);
        var character = new TMP_SpriteCharacter(0xFFFE, glyph) { name = "atk" };
        asset.spriteGlyphTable.Add(glyph);
        asset.spriteCharacterTable.Add(character);
        Shader shader = Shader.Find("TextMeshPro/Sprite");
        if (shader == null)
            throw new InvalidOperationException("TextMeshPro/Sprite 셰이더를 찾지 못했다.");
        var material = new Material(shader) { name = "SkillTooltipAttackSprite Material" };
        material.SetTexture("_MainTex", texture);
        asset.material = material;
        asset.UpdateLookupTables();
        AssetDatabase.CreateAsset(asset, AttackSpritePath);
        AssetDatabase.AddObjectToAsset(material, asset);
        return asset;
    }

    private static Sprite EnsureTemporaryIcon(string name, Color color)
    {
        string path = GeneratedFolder + "/" + name + ".asset";
        EnsureTextureWithSprite(path, name, color, false);
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Sprite sprite)
                return sprite;
        return null;
    }

    private static void EnsureTextureWithSprite(string path, string name, Color color, bool swordShape)
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
            return;
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name + "Texture" };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool filled = swordShape
                ? (Mathf.Abs(x - y) <= 2 && x > 5 && x < 27) || (x >= 5 && x <= 12 && y >= 4 && y <= 11)
                : Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) <= 12f;
            pixels[y * size + x] = filled ? color : Color.clear;
        }
        texture.SetPixels(pixels);
        texture.Apply();
        AssetDatabase.CreateAsset(texture, path);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        sprite.name = name;
        AssetDatabase.AddObjectToAsset(sprite, texture);
        EditorUtility.SetDirty(texture);
    }

    private static Image EnsureImage(RectTransform parent, string name, Vector2 position, Vector2 size)
    {
        RectTransform rect = FindDeep(parent, name) as RectTransform ?? NewUi(name, parent);
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
        RectTransform rect = FindDeep(parent, name) as RectTransform ?? NewUi(name, parent);
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

    private static Image FindIcon(Transform slotRoot, Image cooldownFill)
    {
        Image fallback = null;
        foreach (Image candidate in slotRoot.GetComponentsInChildren<Image>(true))
        {
            if (candidate == cooldownFill)
                continue;
            if (candidate.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                return candidate;
            if (fallback == null && candidate.sprite != null)
                fallback = candidate;
        }
        return fallback;
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

    private static Color SlotColor(PlayerSkillSlot slot) => slot switch
    {
        PlayerSkillSlot.Main => new Color(0.2f, 0.75f, 1f, 1f),
        PlayerSkillSlot.Sub => new Color(0.25f, 0.95f, 0.72f, 1f),
        PlayerSkillSlot.Interrupt => new Color(1f, 0.65f, 0.25f, 1f),
        PlayerSkillSlot.Ultimate => new Color(0.75f, 0.35f, 1f, 1f),
        _ => Color.white,
    };

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
