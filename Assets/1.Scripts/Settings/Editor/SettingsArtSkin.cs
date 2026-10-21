using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정창 새 아트 스킨(PLAN-settings-art) — SVN r466 <c>UI/UI_Panel</c>(이지원 납품)을 <c>SettingsBody</c> 에 입힌다. <b>멱등</b>.
/// <see cref="SettingsPanelAuthoring"/>(항목 배치 메뉴)이 행을 만든 뒤 부른다 — 예전 <c>SettingsDarkTheme</c> 자리.
///
/// 좌표 원본 = 납품 <c>asset_manifest.json</c> 의 06_Settings 배치(1920×1080, 왼쪽 위 원점, y 아래로).
/// 여기서는 패널 중심(<see cref="PanelCenter"/>)을 원점으로 바꿔 쓴다. 매니페스트 rect 는 투명 여백(논리 2px)을 뺀 값이라
/// 이미지 크기는 rect + 4 로 잡는다(<see cref="Pad"/>).
/// 스프라이트 PPU 200 · 캔버스 기준 PPU 100 → 원본 크기 = 픽셀 / 2.
/// </summary>
internal static class SettingsArtSkin
{
    // ─── 매니페스트 guid (UI_Panel/asset_manifest.json, r466) ─────────────
    const string PanelSettings = "87bcdbe4deea4823ae03aecc83dd56fe";
    const string CloseBase = "2a3a9fadf51c4f47b9b232e450f3b071";
    const string CrossRed = "a65910e5aa9e4da58df47bb89e41bc21";
    const string CheckGreen = "0efa0febe77f46ac98aa65c171b6ecd9";
    const string PillNeutral = "28aaba80a0054613b29a08d4926849c1";
    const string PillGreen = "19cc3a31295a455aa749eb5fc3fe90e5";
    const string PillRed = "ba9c4ad2c9534c66b0b003202a73d7dd";
    const string LabelCapsuleId = "8f19b3aa0c464003b2c60a266aad5f2c";
    const string DropdownCapsule = "a7b5c59394564c348f73adee7cc7e5aa";
    const string DropdownPanelOpen = "0a9f69a9c0e6463e9c85b21b958dec13";
    const string DropdownButton = "0ba5414bdba04a2295f882c684ef4b1e";
    const string ChevronDown = "905fd4549a1940d8a9a45cb1041851a3";
    const string ScrollTrack = "6c2fab76d5ad49fe8795d8b7fe2145d4";
    const string ScrollHandle = "8aacace753f14d5e85e513b73cf0ab4e";
    const string SliderTrack = "7ada160901374fabb08d3f3c45b4259c";
    const string SliderFill = "00cd3c3a81274ec28389587428369b95";
    const string SliderHandle = "08a1645d39084fa2ae43672bb457bd26";

    static readonly string[] AllIds =
    {
        PanelSettings, CloseBase, CrossRed, CheckGreen, PillNeutral, PillGreen, PillRed, LabelCapsuleId, DropdownCapsule,
        DropdownPanelOpen, DropdownButton, ChevronDown, ScrollTrack, ScrollHandle, SliderTrack, SliderFill, SliderHandle,
    };

    /// <summary>아트 원본 글꼴(서울알림체 Heavy) TMP 에셋 — <see cref="SettingsFontAssetAuthoring"/> 이 만든다.</summary>
    internal const string FontAssetPath = "Assets/50.Art/UI/Fonts/SeoulAlrimTTF-Heavy SDF.asset";

    internal const float Pad = 2f;                                    // 스프라이트 바깥 투명 여백(논리)
    const float DropdownListPad = 10f;                                // 드롭다운 목록 위아래 여백(경석 10-21)
    static readonly Vector2 PanelCenter = new Vector2(971.39f, 528.11f);   // panel_settings rect 중심(목업 좌표)

    // 내용 카드(탭 오른쪽 줄무늬 영역) — 목업 좌표.
    static readonly Rect ContentCard = new Rect(656f, 254f, 949f, 661f);
    static readonly Rect ViewportRect = new Rect(676f, 370f, 909f, 525f);    // 밝은 회색 내용 영역

    internal static readonly Color TextLight = Color.white;
    internal static readonly Color TextDark = new Color(0.17f, 0.17f, 0.17f, 1f);   // "100%" (CMYK 79/75/73/49)

    static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

    // ─── 스프라이트 ───────────────────────────────────────────────────

    /// <summary>필요한 스프라이트 17개를 guid 로 읽는다. 하나라도 없으면 false — SVN r466 이상을 받았는지 확인할 것.</summary>
    internal static bool LoadSprites()
    {
        _sprites.Clear();
        var missing = new List<string>();
        foreach (string id in AllIds)
        {
            string path = AssetDatabase.GUIDToAssetPath(id);
            Sprite s = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null) missing.Add(id);
            else _sprites[id] = s;
        }
        if (missing.Count > 0)
        {
            Debug.LogError($"[SettingsArtSkin] UI_Panel 스프라이트 {missing.Count}개가 없다 — SVN r466 이상(UI/UI_Panel)을 받았는지 확인. guid: {string.Join(", ", missing)}");
            return false;
        }
        return true;
    }

    internal static Sprite LabelCapsule => _sprites[LabelCapsuleId];

    // ─── 틀 · 탭 · 내용 카드 ──────────────────────────────────────────

    static readonly string[] TabNames = { "Gameplay_Tab", "Graphics_Tab", "Controls_Tab", "Audio_Tab" };
    static readonly string[] TabEnglish = { "Gameplay", "Graphics", "Controls", "Audio" };
    static readonly float[] TabCenterY = { 337.47f, 502.47f, 667.47f, 832.47f };   // 그림 속 탭 칸 중심(165 간격)

    /// <summary>패널 그림·제목·닫기 버튼·탭·내용 카드 배치. contents = 게임플레이·그래픽·조작·오디오 순.</summary>
    internal static void ApplyFrame(Transform root, GameObject[] contents)
    {
        // 패널 그림 — 맨 뒤(첫 자식). 뒤 클릭도 막는다(ESC 에서 게임 화면 클릭 차단).
        Image frame = EnsureChild<Image>(root, "Frame");
        frame.transform.SetSiblingIndex(0);
        frame.sprite = _sprites[PanelSettings];
        frame.type = Image.Type.Simple;
        frame.color = Color.white;
        frame.raycastTarget = true;
        Place(frame.rectTransform, Vector2.zero, NativeSize(frame.sprite));

        // 제목
        TMP_Text title = EnsureText(root, "Frame_Title", SettingsUiStrings.AuthoredEnglish[SettingsUiStrings.PanelTitle.Key]);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.fontSize = 64f;
        title.color = TextLight;
        RectTransform tr = title.rectTransform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
        tr.pivot = new Vector2(0f, 0.5f);
        tr.anchoredPosition = new Vector2(364f - PanelCenter.x, PanelCenter.y - (131.34f + 83.71f * 0.5f));
        tr.sizeDelta = new Vector2(560f, 90f);

        // 닫기 버튼(오른쪽 위 탭 모양) + 빨간 X
        Image close = EnsureChild<Image>(root, "Frame_Close");
        close.sprite = _sprites[CloseBase];
        close.type = Image.Type.Simple;
        close.color = Color.white;
        Place(close.rectTransform, FromMock(new Rect(1496.20f, 74.22f, 182.07f, 119.43f)), NativeSize(close.sprite));
        Button closeButton = GetOrAdd<Button>(close.gameObject);
        closeButton.targetGraphic = close;
        Tint(closeButton);
        Image cross = EnsureChild<Image>(close.transform, "Icon");
        cross.sprite = _sprites[CrossRed];
        cross.raycastTarget = false;
        Place(cross.rectTransform, new Vector2(3.75f, 0f), NativeSize(cross.sprite));

        // 탭 4개 — 그림 위 칸 자리에 버튼을 맞추고, 선택된 탭만 알약을 보인다(SettingsBodyView).
        var tabBgs = new Image[4];
        var tabLabels = new TMP_Text[4];
        for (int i = 0; i < TabNames.Length; i++)
        {
            Transform tab = root.Find(TabNames[i]);
            if (tab == null) { Debug.LogWarning($"[SettingsArtSkin] {TabNames[i]} 이 없다 — 건너뜀."); continue; }
            var rt = (RectTransform)tab;
            Place(rt, FromMock(new Rect(345.34f, TabCenterY[i] - 66.5f, 274.83f, 132.99f)), new Vector2(274.83f + Pad * 2, 132.99f + Pad * 2));
            Image bg = tab.GetComponent<Image>();
            if (bg != null)
            {
                SetSliced(bg, _sprites[PillNeutral], rt.sizeDelta);
                bg.raycastTarget = true;
                tabBgs[i] = bg;
            }
            Button b = tab.GetComponent<Button>();
            if (b != null) Tint(b);
            TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                Stretch(label.rectTransform, new Vector2(20f, 12f), new Vector2(-20f, -12f));
                label.alignment = TextAlignmentOptions.Center;
                AutoSize(label, 18f, 40f);
                label.color = TextLight;
                tabLabels[i] = label;
            }
        }

        // 내용 카드 4개 — 같은 자리. 카드 배경은 그림에 있으므로 기존 Image 는 투명.
        for (int i = 0; i < contents.Length; i++)
        {
            GameObject content = contents[i];
            var rt = (RectTransform)content.transform;
            Place(rt, FromMock(ContentCard), ContentCard.size);
            Image bg = content.GetComponent<Image>();
            if (bg != null) { bg.color = Color.clear; bg.raycastTarget = false; }

            // 🔴 Audio_Content 에는 원래 VerticalLayoutGroup 이 있어 새로 넣은 자식(머리글·스크롤)을 끌고 간다 — 끈다.
            VerticalLayoutGroup legacyLayout = content.GetComponent<VerticalLayoutGroup>();
            if (legacyLayout != null) legacyLayout.enabled = false;

            TMP_Text header = FindHeader(content.transform) ?? EnsureText(content.transform, TabNames[i].Replace("_Tab", "_Label"), TabEnglish[i]);
            header.alignment = TextAlignmentOptions.MidlineLeft;
            header.fontSize = 56f;
            header.color = TextLight;
            header.raycastTarget = false;
            RectTransform hr = header.rectTransform;
            hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.5f);
            hr.pivot = new Vector2(0f, 0.5f);
            hr.anchoredPosition = new Vector2(745.07f - ContentCard.center.x, ContentCard.center.y - 319.45f);
            hr.sizeDelta = new Vector2(560f, 80f);
        }

        SettingsBodyView view = GetOrAdd<SettingsBodyView>(root.gameObject);
        var so = new SerializedObject(view);
        so.FindProperty("closeButton").objectReferenceValue = closeButton;
        SetArray(so.FindProperty("tabBackgrounds"), tabBgs);
        SetArray(so.FindProperty("tabLabels"), tabLabels);
        SetArray(so.FindProperty("contents"), contents);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static TMP_Text FindHeader(Transform content)
    {
        for (int i = 0; i < content.childCount; i++)
        {
            Transform ch = content.GetChild(i);
            if (ch.name.EndsWith("_Label")) { TMP_Text t = ch.GetComponent<TMP_Text>(); if (t != null) return t; }
        }
        return null;
    }

    // ─── 스크롤(A2) ────────────────────────────────────────────────────

    /// <summary>
    /// 내용 카드에 스크롤 영역(Viewport + ScrollRect + 새 스크롤바)을 보장하고 그 안의 <c>Rows</c> 를 돌려준다.
    /// 예전 구조(content/Rows)면 Rows 를 Viewport 아래로 옮긴다 — 안의 행(기존 볼륨·토글 포함)은 그대로 따라간다.
    /// </summary>
    internal static RectTransform EnsureScroll(Transform content, float rowSpacing, RectOffset padding)
    {
        Image viewportImage = EnsureChild<Image>(content, "Viewport");
        viewportImage.sprite = null;
        viewportImage.color = Color.clear;    // 빈 곳에서도 휠·드래그가 먹게(레이캐스트 대상)
        viewportImage.raycastTarget = true;
        RectTransform viewport = viewportImage.rectTransform;
        Place(viewport, ViewportRect.center - ContentCard.center, ViewportRect.size, flipY: true);
        GetOrAdd<RectMask2D>(viewport.gameObject);

        Transform rowsT = content.Find("Rows") ?? viewport.Find("Rows");
        RectTransform rows;
        if (rowsT == null)
        {
            var go = new GameObject("Rows", typeof(RectTransform));
            rows = (RectTransform)go.transform;
        }
        else rows = (RectTransform)rowsT;
        rows.SetParent(viewport, false);
        rows.anchorMin = new Vector2(0f, 1f);
        rows.anchorMax = new Vector2(1f, 1f);
        rows.pivot = new Vector2(0.5f, 1f);
        rows.anchoredPosition = Vector2.zero;
        rows.sizeDelta = new Vector2(0f, rows.sizeDelta.y);

        VerticalLayoutGroup v = GetOrAdd<VerticalLayoutGroup>(rows.gameObject);
        v.childAlignment = TextAnchor.UpperLeft;
        v.padding = new RectOffset(padding.left, padding.right, padding.top, padding.bottom);
        v.spacing = rowSpacing;
        v.childControlWidth = false;
        v.childControlHeight = false;
        v.childForceExpandWidth = false;
        v.childForceExpandHeight = false;
        ContentSizeFitter fit = GetOrAdd<ContentSizeFitter>(rows.gameObject);
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        GetOrAdd<LayoutElement>(rows.gameObject).ignoreLayout = true;

        // 스크롤바 — 내용 영역 오른쪽 안쪽(목업 드롭다운 목록의 스크롤바와 같은 그림).
        Scrollbar bar = EnsureScrollbar(content, "Scrollbar");
        var barRt = (RectTransform)bar.transform;
        Place(barRt, new Vector2(1570.5f, 632.5f) - ContentCard.center, new Vector2(15f + Pad * 2, 505f + Pad * 2), flipY: true);
        bar.transform.SetAsLastSibling();

        ScrollRect scroll = GetOrAdd<ScrollRect>(viewport.gameObject);
        scroll.content = rows;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.scrollSensitivity = 40f;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        return rows;
    }

    static Scrollbar EnsureScrollbar(Transform parent, string name)
    {
        Image track = EnsureChild<Image>(parent, name);
        SetSlicedSprite(track, _sprites[ScrollTrack]);
        track.raycastTarget = true;
        Scrollbar bar = GetOrAdd<Scrollbar>(track.gameObject);
        bar.direction = Scrollbar.Direction.BottomToTop;

        RectTransform area = EnsureRect(track.transform, "Sliding Area");
        Stretch(area, new Vector2(Pad, Pad), new Vector2(-Pad, -Pad));
        Image handle = EnsureChild<Image>(area, "Handle");
        handle.sprite = _sprites[ScrollHandle];
        handle.type = Image.Type.Simple;
        handle.color = Color.white;
        Stretch(handle.rectTransform, Vector2.zero, Vector2.zero);
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        Tint(bar);
        return bar;
    }

    // ─── 컨트롤 스킨 ───────────────────────────────────────────────────

    /// <summary>탭 내용 안의 드롭다운·슬라이더·토글·버튼에 새 아트를 입힌다. 배치(크기·위치)는 항목 배치 메뉴가 먼저 잡는다.</summary>
    internal static void SkinControls(Transform content)
    {
        foreach (TMP_Dropdown dd in content.GetComponentsInChildren<TMP_Dropdown>(true)) SkinDropdown(dd);
        foreach (Slider s in content.GetComponentsInChildren<Slider>(true)) SkinSlider(s);
        foreach (Toggle t in content.GetComponentsInChildren<Toggle>(true))
        {
            if (t.GetComponentInParent<TMP_Dropdown>(true) != null) continue;   // 드롭다운 목록 항목은 SkinDropdown 이 한다
            SkinToggle(t);
        }
        foreach (Button b in content.GetComponentsInChildren<Button>(true))
        {
            string holder = b.transform.parent != null ? b.transform.parent.name : "";
            string pill = holder == "Keep" ? PillGreen : holder == "Revert" ? PillRed : PillNeutral;
            SkinButton(b, _sprites[pill]);
        }
    }

    static void SkinDropdown(TMP_Dropdown dd)
    {
        var rt = (RectTransform)dd.transform;
        Image body = dd.targetGraphic as Image;
        if (body == null) body = dd.GetComponent<Image>();
        if (body != null) SetSliced(body, _sprites[DropdownCapsule], rt.rect.size == Vector2.zero ? new Vector2(557f, 61f) : rt.rect.size);
        Tint(dd);

        if (dd.captionText != null)
        {
            Stretch(dd.captionText.rectTransform, new Vector2(26f, 0f), new Vector2(-88f, 0f));
            dd.captionText.alignment = TextAlignmentOptions.MidlineLeft;
            dd.captionText.color = TextLight;
            AutoSize(dd.captionText, 14f, 24f);
        }

        // 닫힘 화살표 = 둥근 버튼 그림 + 아래 화살표(목업 오른쪽 끝, 세로 가운데).
        Transform arrowT = dd.transform.Find("Arrow");
        if (arrowT != null)
        {
            Image arrow = arrowT.GetComponent<Image>();
            DropdownArrowButton(arrow, 0f);
        }

        RectTransform template = dd.template;
        if (template == null) return;

        // 펼친 목록 = 캡슐 자리부터 덮는 어두운 판(dropdown_panel_open). 위쪽 캡슐 높이만큼은 머리(위 화살표).
        template.anchorMin = new Vector2(0f, 1f);
        template.anchorMax = new Vector2(1f, 1f);
        template.pivot = new Vector2(0.5f, 1f);
        template.anchoredPosition = Vector2.zero;
        template.sizeDelta = new Vector2(0f, 296.71f + Pad * 2);
        Image panel = template.GetComponent<Image>();
        if (panel != null) SetSliced(panel, _sprites[DropdownPanelOpen], new Vector2(557f, template.sizeDelta.y));

        Image header = EnsureChild<Image>(template, "Header");
        header.transform.SetSiblingIndex(0);
        DropdownArrowButton(header, 180f);
        var headerRt = header.rectTransform;
        headerRt.anchorMin = headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.anchoredPosition = new Vector2(-6f, -30.5f);

        ScrollRect scroll = template.GetComponent<ScrollRect>();
        if (scroll != null && scroll.viewport != null)
        {
            Stretch(scroll.viewport, new Vector2(20f, 14f), new Vector2(-58f, -63f));
            scroll.scrollSensitivity = 30f;
        }
        if (scroll != null && scroll.verticalScrollbar != null)
        {
            Scrollbar bar = scroll.verticalScrollbar;
            var barRt = (RectTransform)bar.transform;
            barRt.anchorMin = new Vector2(1f, 0f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(1f, 0.5f);
            barRt.offsetMin = new Vector2(-35f - (15f + Pad * 2), 21f);
            barRt.offsetMax = new Vector2(-35f, -63f);
            Image track = bar.GetComponent<Image>();
            if (track != null) SetSlicedSprite(track, _sprites[ScrollTrack]);
            if (bar.handleRect != null)
            {
                if (bar.handleRect.parent is RectTransform area) Stretch(area, new Vector2(Pad, Pad), new Vector2(-Pad, -Pad));
                Image h = bar.handleRect.GetComponent<Image>();
                if (h != null) { h.sprite = _sprites[ScrollHandle]; h.type = Image.Type.Simple; h.color = Color.white; }
            }
            Tint(bar);
        }

        // 항목 — 배경은 투명, 마우스를 올리면 살짝 밝게. 체크 = 초록 체크 아이콘.
        Toggle item = template.GetComponentInChildren<Toggle>(true);
        if (item != null)
        {
            var itemRt = (RectTransform)item.transform;
            itemRt.sizeDelta = new Vector2(itemRt.sizeDelta.x, 44f);
            // 목록 위아래 여백(경석 10-21: 맨 위 항목이 테두리에 붙었다). TMP 드롭다운은 Content 높이와 항목 높이의 차이를
            // 위아래 여백으로 쓴다 — Content 를 기본 28 로 두면 44 짜리 항목이 위로 8px 삐져나가 맨 위 글자가 붙는다.
            if (itemRt.parent is RectTransform listContent)
                listContent.sizeDelta = new Vector2(listContent.sizeDelta.x, 44f + DropdownListPad * 2f);
            if (item.targetGraphic is Image itemBg)
            {
                itemBg.sprite = null;
                itemBg.color = Color.white;
                ColorBlock c = item.colors;
                c.normalColor = new Color(1f, 1f, 1f, 0f);
                c.highlightedColor = new Color(1f, 1f, 1f, 0.14f);
                c.selectedColor = new Color(1f, 1f, 1f, 0.08f);
                c.pressedColor = new Color(1f, 1f, 1f, 0.24f);
                c.disabledColor = new Color(1f, 1f, 1f, 0f);
                c.colorMultiplier = 1f;
                item.colors = c;
            }
            if (item.graphic is Image check)
            {
                check.sprite = _sprites[CheckGreen];
                check.preserveAspect = true;
                check.color = Color.white;
                RectTransform cr = check.rectTransform;
                cr.anchorMin = cr.anchorMax = new Vector2(0f, 0.5f);
                cr.pivot = new Vector2(0f, 0.5f);
                cr.anchoredPosition = new Vector2(10f, 0f);
                cr.sizeDelta = new Vector2(30f, 22f);
            }
        }
        if (dd.itemText != null)
        {
            Stretch(dd.itemText.rectTransform, new Vector2(52f, 0f), new Vector2(-10f, 0f));
            dd.itemText.alignment = TextAlignmentOptions.MidlineLeft;
            dd.itemText.color = TextLight;
            AutoSize(dd.itemText, 14f, 22f);
        }
    }

    // 둥근 버튼 그림(dropdown_button_base) + 화살표. 닫힘 0°, 펼침 180°(납품 README).
    static void DropdownArrowButton(Image button, float chevronZ)
    {
        if (button == null) return;
        button.sprite = _sprites[DropdownButton];
        button.type = Image.Type.Simple;
        button.color = Color.white;
        button.raycastTarget = false;
        RectTransform r = button.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
        r.pivot = new Vector2(1f, 0.5f);
        r.anchoredPosition = new Vector2(-6f, 0f);
        r.sizeDelta = NativeSize(button.sprite);

        Image chevron = EnsureChild<Image>(button.transform, "Chevron");
        chevron.sprite = _sprites[ChevronDown];
        chevron.type = Image.Type.Simple;
        chevron.color = Color.white;
        chevron.raycastTarget = false;
        Place(chevron.rectTransform, Vector2.zero, NativeSize(chevron.sprite));
        chevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, chevronZ);
    }

    static void SkinSlider(Slider s)
    {
        if (s is null) return;
        Transform track = s.transform.Find("Background");
        if (track != null && track.GetComponent<Image>() is Image trackImage)
        {
            SetSlicedSprite(trackImage, _sprites[SliderTrack]);
            var r = (RectTransform)track;
            r.anchorMin = new Vector2(0f, 0.5f);
            r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(0f, 22.5f + Pad * 2);
        }

        // 채움 = Horizontal Filled(납품 README) — Slider 가 fillAmount 를 쓰고 크기는 그대로 둔다.
        if (s.fillRect != null)
        {
            if (s.fillRect.parent is RectTransform area)
            {
                area.anchorMin = new Vector2(0f, 0.5f);
                area.anchorMax = new Vector2(1f, 0.5f);
                area.pivot = new Vector2(0.5f, 0.5f);
                area.offsetMin = new Vector2(6f, -(8.75f + Pad * 2) * 0.5f);
                area.offsetMax = new Vector2(-6f, (8.75f + Pad * 2) * 0.5f);
            }
            Image fill = s.fillRect.GetComponent<Image>();
            if (fill != null)
            {
                fill.sprite = _sprites[SliderFill];
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)Image.OriginHorizontal.Left;
                fill.color = Color.white;
            }
            Stretch(s.fillRect, Vector2.zero, Vector2.zero);
        }

        if (s.handleRect != null)
        {
            if (s.handleRect.parent is RectTransform area) Stretch(area, new Vector2(8f, 0f), new Vector2(-8f, 0f));
            Image handle = s.handleRect.GetComponent<Image>();
            if (handle != null)
            {
                handle.sprite = _sprites[SliderHandle];
                handle.type = Image.Type.Simple;
                handle.color = Color.white;
                s.targetGraphic = handle;
            }
            s.handleRect.sizeDelta = new Vector2(29f + Pad * 2, 0f);
        }
        Tint(s);
    }

    static void SkinToggle(Toggle t)
    {
        Image bg = t.targetGraphic as Image;
        if (bg == null) return;
        bg.sprite = _sprites[DropdownButton];
        bg.type = Image.Type.Simple;
        bg.color = Color.white;
        Tint(t);

        // 아이콘은 SettingsToggleIcon 이 켬/끔에 따라 바꾼다 — Toggle.graphic 은 꺼짐에서 숨겨지므로 비운다.
        Image icon = t.graphic as Image;
        if (icon == null)
        {
            Transform existing = bg.transform.Find("Checkmark") ?? bg.transform.Find("Icon");
            icon = existing != null ? existing.GetComponent<Image>() : EnsureChild<Image>(bg.transform, "Icon");
        }
        t.graphic = null;
        icon.color = Color.white;
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        icon.sprite = t.isOn ? _sprites[CheckGreen] : _sprites[CrossRed];
        RectTransform ir = icon.rectTransform;
        ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 0.5f);
        ir.pivot = new Vector2(0.5f, 0.5f);
        ir.anchoredPosition = Vector2.zero;

        SettingsToggleIcon swap = GetOrAdd<SettingsToggleIcon>(t.gameObject);
        var so = new SerializedObject(swap);
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("onSprite").objectReferenceValue = _sprites[CheckGreen];
        so.FindProperty("offSprite").objectReferenceValue = _sprites[CrossRed];
        so.ApplyModifiedPropertiesWithoutUndo();
        ir.sizeDelta = t.isOn ? so.FindProperty("onSize").vector2Value : so.FindProperty("offSize").vector2Value;
    }

    static void SkinButton(Button b, Sprite pill)
    {
        if (b.targetGraphic is Image img)
        {
            var rt = (RectTransform)b.transform;
            SetSliced(img, pill, rt.rect.size == Vector2.zero ? new Vector2(278f, 61f) : rt.rect.size);
        }
        Tint(b);
        foreach (TMP_Text t in b.GetComponentsInChildren<TMP_Text>(true))
        {
            t.color = TextLight;
            t.alignment = TextAlignmentOptions.Center;
            AutoSize(t, 12f, 24f);
            Stretch(t.rectTransform, new Vector2(16f, 0f), new Vector2(-16f, 0f));
        }
    }

    /// <summary>해상도 확인 창 배경 — 펼친 드롭다운과 같은 어두운 판.</summary>
    internal static void SkinDialog(Image background)
    {
        var rt = background.rectTransform;
        SetSliced(background, _sprites[DropdownPanelOpen], rt.sizeDelta);
    }

    // ─── 글꼴(A4) ─────────────────────────────────────────────────────

    /// <summary>패널 안 TMP 전부를 아트 원본 글꼴로. 에셋이 아직 없으면 지금 글꼴을 그대로 두고 false.</summary>
    internal static bool ApplyFont(Transform root)
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (font == null)
        {
            Debug.LogWarning($"[SettingsArtSkin] 글꼴 에셋이 없다({FontAssetPath}) — 지금 글꼴 유지. 'Tools/UI/Settings/설정창 글꼴 에셋 만들기 (서울알림체)' 먼저.");
            return false;
        }
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            t.font = font;
            t.fontSharedMaterial = font.material;
            t.fontStyle = FontStyles.Normal;   // Heavy 굵기가 글꼴 자체에 있다
        }
        return true;
    }

    // ─── 공용 ─────────────────────────────────────────────────────────

    /// <summary>목업 rect(왼쪽 위 원점) → 패널 중심 원점 anchoredPosition.</summary>
    static Vector2 FromMock(Rect r) => new Vector2(r.center.x - PanelCenter.x, PanelCenter.y - r.center.y);

    static void Place(RectTransform r, Vector2 pos, Vector2 size, bool flipY = false)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = flipY ? new Vector2(pos.x, -pos.y) : pos;
        r.sizeDelta = size;
        r.localScale = Vector3.one;
        r.localEulerAngles = Vector3.zero;
    }

    internal static Vector2 NativeSize(Sprite s) => s.rect.size * (100f / s.pixelsPerUnit);

    /// <summary>
    /// 9-slice. 🔴 테두리 합이 칸보다 크면 Unity 가 축마다 따로 줄여 알약 끝이 찌그러진다 —
    /// 가장 모자란 축 기준으로 pixelsPerUnitMultiplier 를 올려 모서리를 고르게 줄인다.
    /// </summary>
    internal static void SetSliced(Image img, Sprite sprite, Vector2 size)
    {
        SetSlicedSprite(img, sprite);
        float unit = 100f / sprite.pixelsPerUnit;
        float needX = (sprite.border.x + sprite.border.z) * unit / Mathf.Max(1f, size.x);
        float needY = (sprite.border.y + sprite.border.w) * unit / Mathf.Max(1f, size.y);
        img.pixelsPerUnitMultiplier = Mathf.Max(1f, needX, needY);
    }

    static void SetSlicedSprite(Image img, Sprite sprite)
    {
        img.sprite = sprite;
        img.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        img.fillCenter = true;
        img.color = Color.white;
    }

    // 그림 자체가 색을 가진다 — 기본 흰색, 올리면 살짝 밝게(그림은 1 이 최대라 대신 누르면 어둡게), 비활성 반투명.
    static void Tint(Selectable s)
    {
        s.transition = Selectable.Transition.ColorTint;
        if (s.targetGraphic != null) s.targetGraphic.color = s.targetGraphic.color.a <= 0f ? s.targetGraphic.color : Color.white;
        ColorBlock c = s.colors;
        c.normalColor = Color.white;
        c.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        c.selectedColor = Color.white;
        c.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        c.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        c.colorMultiplier = 1f;
        c.fadeDuration = 0.08f;
        s.colors = c;
    }

    internal static void AutoSize(TMP_Text t, float min, float max)
    {
        t.enableAutoSizing = true;
        t.fontSizeMin = min;
        t.fontSizeMax = max;
        t.fontSize = max;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
    }

    internal static void Stretch(RectTransform r, Vector2 offsetMin, Vector2 offsetMax)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = offsetMin;
        r.offsetMax = offsetMax;
    }

    static T EnsureChild<T>(Transform parent, string name) where T : Component
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            t = go.transform;
            t.SetParent(parent, false);
        }
        return GetOrAdd<T>(t.gameObject);
    }

    static RectTransform EnsureRect(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) return (RectTransform)t;
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TMP_Text EnsureText(Transform parent, string name, string text)
    {
        Transform t = parent.Find(name);
        if (t != null && t.GetComponent<TMP_Text>() is TMP_Text existing) return existing;
        var tmp = EnsureChild<TextMeshProUGUI>(parent, name);
        tmp.text = text;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }

    // 🔴 `GetComponent<T>() ?? Add` 금지 — 에디터의 없는 컴포넌트는 "가짜 null" 이라 ?? 가 통과시킨다.
    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static void SetArray<T>(SerializedProperty p, T[] values) where T : Object
    {
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
