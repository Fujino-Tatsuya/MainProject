using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로비 캐릭터 선택 패널(할 일 48.2) — <c>CharacterSelectPanel.prefab</c> 과 카드 프리팹 <c>CharacterSelectCard.prefab</c> 을 만든다.
/// 배치 = char_select 목업(legacy/00_mock_up.png, 1920×1080 좌상단 기준). 아트 Sprite 는 PPU 100 → RectTransform = PNG 픽셀 그대로.
/// 화면 비율이 달라도 무리가 붙어 다니도록 왼쪽 위(뒤로가기·안내)·왼쪽 아래(이름판·잠금)·오른쪽(검은 판·카드·SELECT)에 앵커를 나눈다.
///
/// 🔴 멱등 — 기본 메뉴는 프리팹이 이미 있으면 손대지 않는다(손으로 맞춘 레이아웃 보호). 처음부터 다시 만들려면 "(재생성)".
/// 씬에는 배치하지 않는다(로비 배치는 48 C12). 미리보기 메뉴는 프리뷰 씬에서 1920×1080 PNG 를 찍는다(열린 씬 무수정).
/// 사용법 = Docs/tech/character-select-panel.md.
/// </summary>
public static class CharacterSelectPanelAuthoring
{
    const string Tag = "[CharacterSelectPanel]";
    const string PrefabParent = "Assets/2.Prefabs/UI";
    const string PrefabDir = PrefabParent + "/CharacterSelect";
    public const string PanelPath = PrefabDir + "/CharacterSelectPanel.prefab";
    public const string CardPath = PrefabDir + "/CharacterSelectCard.prefab";
    const string RosterPath = "Assets/9.ScriptableObject/Player/CharacterRoster.asset";
    // 원본 폰트가 없어 로비 씬이 쓰는 한글 TMP 폰트를 쓴다(LobbyResultPanel 과 같다).
    const string FontPath = "Assets/Resources/NotoSansKR-VariableFont_wght SDF.asset";
    const string ArtDir = "Assets/50.Art/UI/char_select/";
    const string MockupRelative = "50.Art/UI/char_select/legacy/00_mock_up.png";
    const string CaptureDir = "C:/UnityProject/_lane-artifacts/MainProject-Worktree4/48.2-char-select";
    const string MotionCaptureDir = "C:/UnityProject/_lane-artifacts/MainProject-Worktree4/60-anim";
    const string FocusRingName = "Ring_Focus"; // 구 이름 Marker_Current(할 일 60 전 — 금색 테두리 = 지금 캐릭터였다)

    const int SortingOrder = 190; // LobbyResultPanel(200) 아래 — 결과 패널이 위에 뜬다

    // 배경 타이포 — 일반판(어두운 회색 63%)과 _gamma 판(흰색 14%) 중 목업 색에 맞는 쪽. 캡처 메뉴가 둘을 비교해 로그로 남긴다.
    const string BackgroundLoopSprite = "bg_typography_loop.png";
    const string BackgroundLoopGammaSprite = "bg_typography_loop_gamma.png";
    // 목업 타이포 기울기(영문 문장 줄 기울기 ≈ 26°, 오른쪽 아래로).
    const float BackgroundAngle = 26f;
    // 흐름 방향(회전 좌표 — 줄무늬를 따라 오른쪽, 사용자 결정 10-19).
    static readonly Vector2 BackgroundDirection = new Vector2(1f, 0f);

    // 카드(가운데 기준 654×519 = card_selected). 위·아래 카드는 패널이 0.84 배(= 549 / 654)로 줄인다.
    static readonly Vector2 CardSize = new Vector2(654f, 519f);

    static readonly Color CurrentMarkerColor = new Color(1f, 0.8f, 0.25f, 1f);
    static readonly Color TagBackColor = new Color(0f, 0f, 0f, 0.78f);
    static readonly Color HintColor = new Color(0.62f, 0.62f, 0.62f, 1f);
    static readonly Color LockedColor = new Color(1f, 0.78f, 0.3f, 1f);

    // ── 메뉴 ────────────────────────────────────────────────

    [MenuItem("Tools/UI/Authoring/Character Select Panel")]
    public static void Build() => Build(false);

    [MenuItem("Tools/UI/Authoring/Character Select Panel (재생성)")]
    public static void Rebuild() => Build(true);

    static void Build(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Play 중에는 실행하지 않는다 — 먼저 정지할 것.");
            return;
        }

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"{Tag} 폰트를 찾지 못했다: {FontPath}");
            return;
        }

        if (!AssetDatabase.IsValidFolder(PrefabDir))
            AssetDatabase.CreateFolder(PrefabParent, "CharacterSelect");

        var sprites = new Sprites();
        if (!sprites.Load())
            return;

        CharacterSelectCard card = AssetDatabase.LoadAssetAtPath<GameObject>(CardPath)?.GetComponent<CharacterSelectCard>();
        if (card == null || force)
            card = BuildCardPrefab(font, sprites);
        else
            Debug.Log($"{Tag} 카드 프리팹 재사용(손대지 않음): {CardPath}");
        if (card == null)
            return;

        bool panelExists = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath)?.GetComponent<CharacterSelectPanel>() != null;
        if (panelExists && !force)
        {
            Debug.Log($"{Tag} 패널 프리팹 재사용(손대지 않음): {PanelPath} — 다시 만들려면 '(재생성)' 메뉴");
            return;
        }

        BuildPanelPrefab(font, sprites, card);
    }

    sealed class Sprites
    {
        public Sprite BgColor, BgLoop, BlackPanel, Chevron, SelectBadge, BackButton,
            CardGap, CardFill, CardRing, CardTop, CardBottom, Nameplate, PortraitSlot;

        public bool Load()
        {
            BgColor = LoadSprite("bg_color.png");
            BgLoop = LoadSprite(BackgroundLoopSprite);
            BlackPanel = LoadSprite("black_panel.png");
            Chevron = LoadSprite("chevron.png");
            SelectBadge = LoadSprite("select_badge.png");
            BackButton = LoadSprite("back_button.png");
            CardGap = LoadSprite("card_selected_gap.png");
            CardFill = LoadSprite("card_selected_fill_tint.png");
            CardRing = LoadSprite("card_selected_ring.png");
            CardTop = LoadSprite("card_unselected_top.png");
            CardBottom = LoadSprite("card_unselected_bottom.png");
            Nameplate = LoadSprite("nameplate_base.png");
            PortraitSlot = LoadSprite("nameplate_portrait_slot.png");
            return BgColor != null && BgLoop != null && BlackPanel != null && Chevron != null && SelectBadge != null &&
                   BackButton != null && CardGap != null && CardFill != null && CardRing != null && CardTop != null &&
                   CardBottom != null && Nameplate != null && PortraitSlot != null;
        }
    }

    static Sprite LoadSprite(string file)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + file);
        if (sprite == null)
            Debug.LogError($"{Tag} Sprite 를 찾지 못했다: {ArtDir}{file}");
        else if (!Mathf.Approximately(sprite.pixelsPerUnit, 100f))
            Debug.LogWarning($"{Tag} {file} PPU 가 {sprite.pixelsPerUnit} 다(배치는 100 기준) — 아트 .meta 는 고치지 않는다. 보고할 것.");
        return sprite;
    }

    // ── 카드 프리팹 ───────────────────────────────────────────

    static CharacterSelectCard BuildCardPrefab(TMP_FontAsset font, Sprites sprites)
    {
        var root = new GameObject("CharacterSelectCard", typeof(RectTransform));
        try
        {
            root.layer = LayerMask.NameToLayer("UI");
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = CardSize;
            // 클릭 영역 — 사각형(아트 텍스처가 Read/Write 꺼짐이라 알파 판정 불가). 자리끼리 겹치지 않는다.
            AddImage(root, null, new Color(0f, 0f, 0f, 0f)).raycastTarget = true;

            // 가운데 자리 강조 — 금색 테두리(카드 실루엣을 조금 크게 뒤에). 알파는 패널 이동 연출이 준다(할 일 60).
            RectTransform ring = Child(FocusRingName, root.transform, Vector2.zero, CardSize + new Vector2(40f, 32f));
            AddImage(ring.gameObject, sprites.CardRing, CurrentMarkerColor);
            ring.gameObject.SetActive(false);

            // 위·아래 카드 바탕(card_unselected_top/bottom, 패널이 0.84 배로 줄여 549×436 원래 크기가 된다)
            RectTransform side = Child("Side", root.transform, Vector2.zero, CardSize);
            Image sideImage = AddImage(side.gameObject, sprites.CardTop, Color.white);

            // 가운데(포커스) 카드 — 검은 테두리(gap) + 초록 채움(fill_tint 를 색으로)
            RectTransform focused = Child("Focused", root.transform, Vector2.zero, CardSize);
            RectTransform gap = Child("Gap", focused, Vector2.zero, NativeSize(sprites.CardGap));
            AddImage(gap.gameObject, sprites.CardGap, Color.white);
            RectTransform fill = Child("Fill", focused, Vector2.zero, NativeSize(sprites.CardFill));
            Image fillImage = AddImage(fill.gameObject, sprites.CardFill, new Color(0.478f, 0.733f, 0.333f, 1f));
            focused.gameObject.SetActive(false);

            // 초상화 — 카드 실루엣으로 자른다(Mask)
            RectTransform clip = Child("Clip", root.transform, Vector2.zero, NativeSize(sprites.CardFill));
            AddImage(clip.gameObject, sprites.CardFill, Color.white);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            RectTransform portrait = Child("Image_Portrait", clip, new Vector2(-27f, -70f), new Vector2(340f, 230f));
            Image portraitImage = AddImage(portrait.gameObject, null, Color.white);
            portraitImage.preserveAspect = true;
            portraitImage.enabled = false;

            // 글자 — 위·아래 카드는 화면에 일부만 보인다(목업). 카드 로컬 y 12~157 이 세 자리 모두에서 보이는 띠라 이름·표식을 거기 둔다.
            // 카드는 오른쪽으로 기운 평행사변형 — 높이 y 에서 가운데 x ≈ 0.447·y.
            RectTransform name = Child("Text_Name", root.transform, new Vector2(54f, 112f), new Vector2(340f, 84f)); // 높이 ≥ 52 × 1.45(말줄임이 글자를 숨기지 않게)
            TMP_Text nameText = AddText(name, font, "-", 52f, TextAlignmentOptions.Center, Color.white, ellipsis: true);

            RectTransform currentTag = AddTag(root.transform, "Tag_Current", new Vector2(-70f, 48f), new Vector2(130f, 48f),
                font, CharacterSelectUiStrings.CurrentTag.Ko, CurrentMarkerColor, out TMP_Text currentText);
            currentTag.gameObject.SetActive(false);
            RectTransform unavailableTag = AddTag(root.transform, "Tag_Unavailable", new Vector2(110f, 48f), new Vector2(190f, 48f),
                font, CharacterSelectUiStrings.Unavailable.Ko, new Color(0.85f, 0.85f, 0.85f, 1f), out TMP_Text unavailableText);
            unavailableTag.gameObject.SetActive(false);

            CharacterSelectCard view = root.AddComponent<CharacterSelectCard>();
            var so = new SerializedObject(view);
            so.FindProperty("focusedVisual").objectReferenceValue = focused.gameObject;
            so.FindProperty("focusedFill").objectReferenceValue = fillImage;
            so.FindProperty("sideBackground").objectReferenceValue = sideImage;
            so.FindProperty("sideTopSprite").objectReferenceValue = sprites.CardTop;
            so.FindProperty("sideBottomSprite").objectReferenceValue = sprites.CardBottom;
            so.FindProperty("focusRing").objectReferenceValue = ring.gameObject;
            so.FindProperty("portraitImage").objectReferenceValue = portraitImage;
            so.FindProperty("nameText").objectReferenceValue = nameText;
            so.FindProperty("currentTag").objectReferenceValue = currentTag.gameObject;
            so.FindProperty("currentText").objectReferenceValue = currentText;
            so.FindProperty("unavailableTag").objectReferenceValue = unavailableTag.gameObject;
            so.FindProperty("unavailableText").objectReferenceValue = unavailableText;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, CardPath, out bool success);
            Debug.Log($"{Tag} 카드 프리팹 저장 {(success ? "성공" : "실패")}: {CardPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(CardPath)?.GetComponent<CharacterSelectCard>();
    }

    /// <summary>어두운 사각 배경 + 글자(자식 Label). 반환 = 배경(켜고 끄는 대상).</summary>
    static RectTransform AddTag(Transform parent, string name, Vector2 position, Vector2 size, TMP_FontAsset font, string value,
        Color color, out TMP_Text text)
    {
        RectTransform tag = Child(name, parent, position, size);
        AddImage(tag.gameObject, null, TagBackColor);
        RectTransform label = Child("Label", tag, Vector2.zero, size);
        text = AddText(label, font, value, 32f, TextAlignmentOptions.Center, color);
        return tag;
    }

    // ── 패널 프리팹 ───────────────────────────────────────────

    static void BuildPanelPrefab(TMP_FontAsset font, Sprites sprites, CharacterSelectCard cardPrefab)
    {
        var root = new GameObject("CharacterSelectPanel", typeof(RectTransform));
        try
        {
            root.layer = LayerMask.NameToLayer("UI");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
            root.AddComponent<GraphicRaycaster>();

            RectTransform content = NewRect("Content", root.transform);
            Stretch(content);
            content.gameObject.AddComponent<UiModalBlocker>();

            // 배경 — 불투명 단색(뒤 클릭 막기) + 흐르는 타이포 패턴(루프 타일 2×2)
            RectTransform bg = NewRect("Bg_Color", content);
            Stretch(bg);
            AddImage(bg.gameObject, sprites.BgColor, Color.white).raycastTarget = true;

            // 루프 타일은 가로 줄무늬 — 목업처럼 비스듬히 보이게 부모를 돌리고, 그 좌표에서 줄무늬 방향으로 흐른다.
            RectTransform rotate = NewRect("Bg_Rotate", content);
            rotate.anchorMin = rotate.anchorMax = new Vector2(0.5f, 0.5f);
            rotate.pivot = new Vector2(0.5f, 0.5f);
            rotate.anchoredPosition = Vector2.zero;
            rotate.sizeDelta = Vector2.zero;
            rotate.localRotation = Quaternion.Euler(0f, 0f, -BackgroundAngle);

            Vector2 tile = NativeSize(sprites.BgLoop);
            Vector2 half = CharacterSelectBackgroundScroll.RotatedHalfExtents(new Vector2(1920f, 1080f), BackgroundAngle);
            if (!CharacterSelectBackgroundScroll.TryCoveringOrigin(tile, half, out Vector2 origin))
                Debug.LogWarning($"{Tag} 배경 타일 {tile} 2×2 가 {BackgroundAngle}° 돌린 화면을 다 덮지 못한다 — 가장자리가 비어 보일 수 있다.");
            RectTransform scroll = NewRect("Bg_Scroll", rotate);
            scroll.anchorMin = scroll.anchorMax = new Vector2(0.5f, 0.5f);
            scroll.pivot = new Vector2(0f, 1f);
            scroll.sizeDelta = tile * 2f;
            scroll.anchoredPosition = CharacterSelectBackgroundScroll.GridPosition(Vector2.zero, tile, origin);
            for (int i = 0; i < 4; i++)
            {
                RectTransform t = NewRect($"Tile_{i}", scroll);
                t.anchorMin = t.anchorMax = new Vector2(0f, 1f);
                t.pivot = new Vector2(0f, 1f);
                t.sizeDelta = tile;
                t.anchoredPosition = new Vector2((i % 2) * tile.x, -(i / 2) * tile.y);
                AddImage(t.gameObject, sprites.BgLoop, Color.white);
            }
            CharacterSelectBackgroundScroll scroller = scroll.gameObject.AddComponent<CharacterSelectBackgroundScroll>();
            var scrollSo = new SerializedObject(scroller);
            scrollSo.FindProperty("tileSize").vector2Value = tile;
            scrollSo.FindProperty("gridOrigin").vector2Value = origin;
            scrollSo.FindProperty("direction").vector2Value = BackgroundDirection;
            scrollSo.ApplyModifiedPropertiesWithoutUndo();

            // 오른쪽 검은 판(SELECT 셰브론 자리 포함)
            RectTransform black = NewRect("BlackPanel", content);
            Place(black, Right, 1920f - 928f, 0f, 928f, 1080f);
            AddImage(black.gameObject, sprites.BlackPanel, Color.white);

            // 카드 — 런타임에 가운데 카드 원본(Card_Template, 배선 뒤 EnsureCardTemplate)을 로스터 수만큼 복제. 원점 = 기준 해상도 화면 가운데, 오른쪽 앵커.
            RectTransform cards = NewRect("Cards", content);
            cards.anchorMin = cards.anchorMax = Right;
            cards.pivot = new Vector2(0.5f, 0.5f);
            cards.anchoredPosition = new Vector2(-960f, 0f);
            cards.sizeDelta = Vector2.zero;

            // SELECT 셰브론 띠 + 글자 — 셰브론 = 선택 버튼(할 일 109, 삼각형만 클릭)
            RectTransform chevron = NewRect("Chevron", content);
            Vector2 chevronSize = NativeSize(sprites.Chevron);
            Place(chevron, Right, 1920f - chevronSize.x, 660f - chevronSize.y * 0.5f, chevronSize.x, chevronSize.y);
            AddImage(chevron.gameObject, sprites.Chevron, Color.white);
            Button confirmButton = MakeChevronButton(chevron.gameObject);

            RectTransform badge = NewRect("SelectBadge", content);
            Vector2 badgeSize = NativeSize(sprites.SelectBadge);
            Place(badge, Right, 1865f - badgeSize.x * 0.5f, 660f - badgeSize.y * 0.5f, badgeSize.x, badgeSize.y);
            AddImage(badge.gameObject, sprites.SelectBadge, Color.white);

            // 왼쪽 위 — 뒤로가기 + 조작 안내
            RectTransform back = NewRect("Button_Back", content);
            Vector2 backSize = NativeSize(sprites.BackButton);
            Place(back, TopLeft, 111f - backSize.x * 0.5f, 81f - backSize.y * 0.5f, backSize.x, backSize.y);
            Image backImage = AddImage(back.gameObject, sprites.BackButton, Color.white);
            backImage.raycastTarget = true;
            Button backButton = back.gameObject.AddComponent<Button>();
            backButton.targetGraphic = backImage;
            backButton.navigation = new Navigation { mode = Navigation.Mode.None };

            RectTransform hint = NewRect("Text_Hint", content);
            Place(hint, TopLeft, 205f, 56f, 900f, 50f);
            TMP_Text hintText = AddText(hint, font, CharacterSelectUiStrings.Hint.Ko, 30f, TextAlignmentOptions.Left, HintColor);

            // 왼쪽 아래 — 이름판(포커스된 캐릭터 초상화·이름) + 잠금 안내
            RectTransform plate = NewRect("Nameplate", content);
            Vector2 plateSize = NativeSize(sprites.Nameplate);
            Place(plate, BottomLeft, 40f, 826f, plateSize.x, plateSize.y);
            AddImage(plate.gameObject, sprites.Nameplate, Color.white);

            RectTransform slot = NewRect("PortraitSlot", plate);
            slot.anchorMin = slot.anchorMax = new Vector2(0f, 1f);
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = new Vector2(103f, -109f);
            slot.sizeDelta = NativeSize(sprites.PortraitSlot);
            AddImage(slot.gameObject, sprites.PortraitSlot, new Color(0.16f, 0.16f, 0.16f, 1f));
            slot.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            RectTransform platePortrait = Child("Image_Portrait", slot, Vector2.zero, new Vector2(150f, 150f));
            Image platePortraitImage = AddImage(platePortrait.gameObject, null, Color.white);
            platePortraitImage.preserveAspect = true;
            platePortraitImage.enabled = false;

            RectTransform plateName = NewRect("Text_Name", plate);
            plateName.anchorMin = plateName.anchorMax = new Vector2(0f, 1f);
            plateName.pivot = new Vector2(0.5f, 0.5f);
            plateName.anchoredPosition = new Vector2(355f, -122f);
            plateName.sizeDelta = new Vector2(250f, 80f);
            TMP_Text plateNameText = AddText(plateName, font, "Name", 54f, TextAlignmentOptions.Center, Color.white, ellipsis: true);

            RectTransform locked = NewRect("Text_Locked", content);
            Place(locked, BottomLeft, 44f, 752f, 1000f, 60f);
            TMP_Text lockedText = AddText(locked, font, CharacterSelectUiStrings.LockedNotice.Ko, 40f, TextAlignmentOptions.Left, LockedColor);
            locked.pivot = new Vector2(0f, 0.5f); // 강조 때 왼쪽 기준으로 커진다
            locked.anchoredPosition -= new Vector2(500f, 0f);
            locked.gameObject.SetActive(false);

            // 배선
            CharacterSelectPanel view = root.AddComponent<CharacterSelectPanel>();
            var viewSo = new SerializedObject(view);
            viewSo.FindProperty("content").objectReferenceValue = content.gameObject;
            CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            if (roster == null)
                Debug.LogError($"{Tag} 로스터를 찾지 못했다: {RosterPath}");
            viewSo.FindProperty("roster").objectReferenceValue = roster;
            viewSo.FindProperty("cardPrefab").objectReferenceValue = cardPrefab;
            viewSo.FindProperty("cardContainer").objectReferenceValue = cards;
            viewSo.FindProperty("backButton").objectReferenceValue = backButton;
            viewSo.FindProperty("confirmButton").objectReferenceValue = confirmButton;
            viewSo.FindProperty("nameplatePortrait").objectReferenceValue = platePortraitImage;
            viewSo.FindProperty("nameplateName").objectReferenceValue = plateNameText;
            viewSo.FindProperty("lockedText").objectReferenceValue = lockedText;
            viewSo.FindProperty("hintText").objectReferenceValue = hintText;
            viewSo.ApplyModifiedPropertiesWithoutUndo();
            EnsureCardTemplate(view);

            content.gameObject.SetActive(false); // 기본 닫힘 — Show 가 켠다

            PrefabUtility.SaveAsPrefabAsset(root, PanelPath, out bool success);
            Debug.Log($"{Tag} 패널 프리팹 저장 {(success ? "성공" : "실패")}: {PanelPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ── 가운데 카드 원본(할 일 109 후속) ──────────────────────

    public const string CardTemplateName = "Card_Template";

    /// <summary>
    /// 패널 <c>cardContainer</c> 아래에 카드 프리팹 인스턴스 <c>Card_Template</c> 를 가운데 자리(<c>focusedSlot</c>·<c>focusScale</c>)에 두고
    /// 패널 <c>cardTemplate</c> 칸에 넣는다 — 프리팹을 열면 가운데 카드가 보이고, 거기서 고친 값(초상화 크기·회전, Ring_Focus 크기·색·스프라이트)을
    /// 런타임 카드가 복제해 쓴다. 미리보기 모양 = 가운데 카드(초록·테두리 켬·로스터 0번 초상화) — 런타임은 Bind·SetState·SetRingAlpha 가 다시 그린다.
    /// 이미 있으면 손대지 않고 연결만 맞춘다(손댄 값 유지).
    /// </summary>
    public static CharacterSelectCard EnsureCardTemplate(CharacterSelectPanel view)
    {
        var viewSo = new SerializedObject(view);
        var container = viewSo.FindProperty("cardContainer").objectReferenceValue as RectTransform;
        var cardPrefab = viewSo.FindProperty("cardPrefab").objectReferenceValue as CharacterSelectCard;
        if (container == null || cardPrefab == null)
        {
            Debug.LogError($"{Tag} cardContainer·cardPrefab 가 비어 가운데 카드 원본을 만들 수 없다.");
            return null;
        }

        Transform existing = container.Find(CardTemplateName);
        CharacterSelectCard template = existing != null ? existing.GetComponent<CharacterSelectCard>() : null;
        if (template == null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab.gameObject, container);
            go.name = CardTemplateName;
            template = go.GetComponent<CharacterSelectCard>();

            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = viewSo.FindProperty("focusedSlot").vector2Value;
            rect.localScale = Vector3.one * viewSo.FindProperty("focusScale").floatValue;

            var cardSo = new SerializedObject(template);
            (cardSo.FindProperty("focusedVisual").objectReferenceValue as GameObject)?.SetActive(true);
            (cardSo.FindProperty("sideBackground").objectReferenceValue as Image)?.gameObject.SetActive(false);
            (cardSo.FindProperty("focusRing").objectReferenceValue as GameObject)?.SetActive(true);
            var portrait = cardSo.FindProperty("portraitImage").objectReferenceValue as Image;
            var roster = viewSo.FindProperty("roster").objectReferenceValue as CharacterRoster;
            if (portrait != null && roster != null && roster.Count > 0 && roster.Entries[0]?.Portrait != null)
            {
                portrait.sprite = roster.Entries[0].Portrait; // 크기·회전을 맞춰 볼 그림(런타임은 Bind 가 바꾼다)
                portrait.enabled = true;
            }
        }

        viewSo.FindProperty("cardTemplate").objectReferenceValue = template;
        viewSo.ApplyModifiedPropertiesWithoutUndo();
        return template;
    }

    // ── SELECT 셰브론 = 선택 버튼(할 일 109) ──────────────────

    /// <summary>
    /// 셰브론 Image 를 선택 버튼으로 만든다 — Raycast Target 켬 + Button(Navigation None) + 삼각형 클릭 영역(<see cref="UiTriangleRaycastFilter"/>).
    /// 이미 붙어 있으면 그 Button·필터를 그대로 쓴다(색 전이·꼭짓점 등 손댄 값 유지). 반환 = 패널 <c>confirmButton</c> 칸에 넣을 Button.
    /// </summary>
    public static Button MakeChevronButton(GameObject chevron)
    {
        Image image = chevron.GetComponent<Image>();
        image.raycastTarget = true;
        Button button = chevron.GetComponent<Button>();
        if (button == null)
            button = chevron.AddComponent<Button>();
        button.targetGraphic = image;
        // 뒤로가기와 같다 — None 이면 클릭해도 EventSystem 선택이 남지 않아 다음 Enter/Space 가 두 번 먹지 않는다.
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        UiTriangleRaycastFilter filter = chevron.GetComponent<UiTriangleRaycastFilter>();
        if (filter == null)
            filter = chevron.AddComponent<UiTriangleRaycastFilter>(); // 기본 꼭짓점 = chevron.png 삼각형
        AddChevronHoverOutline(chevron, button, filter);
        return button;
    }

    const string ChevronOutlineName = "Outline_Hover";

    /// <summary>
    /// hover 흰 아웃라인 — 자식 <see cref="UiTriangleOutline"/>(클릭 삼각형을 두르는 선, 기본 꺼짐) + 셰브론의 <see cref="UiHoverOutline"/>.
    /// 이미 있으면 두께·색 등 손댄 값은 두고 연결만 맞춘다.
    /// </summary>
    static void AddChevronHoverOutline(GameObject chevron, Button button, UiTriangleRaycastFilter filter)
    {
        Transform existing = chevron.transform.Find(ChevronOutlineName);
        UiTriangleOutline outline = existing != null ? existing.GetComponent<UiTriangleOutline>() : null;
        if (outline == null)
        {
            RectTransform rect = existing != null ? (RectTransform)existing : NewRect(ChevronOutlineName, chevron.transform);
            Stretch(rect);
            outline = rect.gameObject.AddComponent<UiTriangleOutline>();
            outline.color = Color.white;
        }
        outline.raycastTarget = false; // hover·클릭 영역은 셰브론 삼각형만
        outline.Shape = filter;

        UiHoverOutline hover = chevron.GetComponent<UiHoverOutline>();
        if (hover == null)
            hover = chevron.AddComponent<UiHoverOutline>();
        hover.Setup(outline, button); // 기본 꺼짐
    }

    // ── 미리보기 캡처 ─────────────────────────────────────────

    [MenuItem("Tools/UI/Preview/Character Select Panel 캡처")]
    public static void CapturePreview() => RunPreview(CaptureDir, CaptureStill);

    /// <summary>
    /// 카드 이동 연출(할 일 60) — 지금 0번·포커스 0번에서 다음 카드로 MoveFocus(1) 한 뒤, 패널 시계를 직접 넣어 샘플링한다.
    /// 이동 중(위치 ≈ 50%, 시간 ≈ 50%·테두리 꺼짐)과 정착 뒤(테두리 켜짐·확대)를 찍는다.
    /// </summary>
    [MenuItem("Tools/UI/Preview/Character Select Panel 이동 연출 캡처")]
    public static void CaptureMotionPreview() => RunPreview(MotionCaptureDir, CaptureMotion);

    static void CaptureMotion(GameObject instance, Camera camera, RenderTexture rt, CharacterSelectPanel panel)
    {
        var move = new SerializedObject(panel);
        float moveSeconds = move.FindProperty("moveSeconds").floatValue;
        float ringFadeSeconds = move.FindProperty("ringFadeSeconds").floatValue;
        float settleSeconds = Mathf.Max(ringFadeSeconds, move.FindProperty("focusScaleSeconds").floatValue);

        panel.Show(0, false);
        Shoot(instance, camera, rt, "CharacterSelect_anim_0_idle.png");

        panel.MoveFocus(1); // 시각 0 에 출발
        // EaseOutCubic 위치 50% = 시간 1 - 0.5^(1/3) ≈ 20.6% — 테두리는 아직 반쯤 남아 있다.
        float pos50 = moveSeconds * (1f - Mathf.Pow(0.5f, 1f / 3f));
        SampleAt(panel, pos50);
        Shoot(instance, camera, rt, "CharacterSelect_anim_1_moving_pos50.png");

        // 시간 ≈ 50% 이면서 테두리 다 꺼짐 — 페이드 0.1초가 이동 0.18초의 절반보다 길어 둘 중 늦은 쪽.
        float ringOff = Mathf.Max(moveSeconds * 0.5f, ringFadeSeconds);
        SampleAt(panel, ringOff);
        Shoot(instance, camera, rt, "CharacterSelect_anim_2_moving_ring_off.png");

        float settled = moveSeconds + settleSeconds + 0.01f;
        SampleAt(panel, settled); // 이동 끝 → 정착 끝 → 정지
        Shoot(instance, camera, rt, "CharacterSelect_anim_3_settled.png");

        Debug.Log($"{Tag} 이동 연출 캡처 — 이동 {moveSeconds}s · 테두리 {ringFadeSeconds}s · 정착 {settleSeconds}s, " +
                  $"샘플 시각 {pos50:F3}s(위치 50%) / {ringOff:F3}s(시간 {ringOff / moveSeconds:P0}·테두리 꺼짐) / {settled:F3}s(정착)");
    }

    /// <summary>
    /// 이동 키 길게 누르기(할 일 60) — 앞으로 키를 시각 0 부터 누르고 있는 것으로 치고 60fps 프레임마다 반복 규칙을 돌려,
    /// 연속 미끄러짐 중(첫 반복 0.25s 뒤 0.25s → 0.1s 로 줄어드는 간격 재목표, 할 일 60.1) 프레임을 찍는다.
    /// 로그의 칸 이동 시각은 가속이 끝난 뒤까지(<c>LogUntil</c>) 남긴다.
    /// </summary>
    [MenuItem("Tools/UI/Preview/Character Select Panel 길게 누르기 캡처")]
    public static void CaptureHoldPreview() => RunPreview(MotionCaptureDir, CaptureHold);

    static void CaptureHold(GameObject instance, Camera camera, RenderTexture rt, CharacterSelectPanel panel)
    {
        var so = new SerializedObject(panel);
        var repeat = new CharacterSelectKeyRepeat
        {
            DelaySeconds = so.FindProperty("keyRepeatDelaySeconds").floatValue,
            StartIntervalSeconds = so.FindProperty("keyRepeatStartIntervalSeconds").floatValue,
            MinIntervalSeconds = so.FindProperty("keyRepeatMinIntervalSeconds").floatValue,
            RampSeconds = so.FindProperty("keyRepeatRampSeconds").floatValue
        };
        float[] shots = { 0.35f, 0.45f, 0.55f };
        const float Fps = 60f;
        const float LogUntil = 1.5f;

        panel.Show(0, false);
        var steps = new System.Text.StringBuilder();
        int next = 0;
        for (int frame = 0; next < shots.Length || frame / Fps <= LogUntil; frame++)
        {
            float t = frame / Fps;
            s_previewNow = t; // MoveFocus 가 이 시각으로 재목표한다
            int step = repeat.Tick(false, true, t);
            if (step != 0)
            {
                panel.MoveFocus(step);
                steps.Append($"{t:F3}s ");
            }
            SampleAt(panel, t);

            if (next < shots.Length && t + 0.5f / Fps >= shots[next])
            {
                Shoot(instance, camera, rt, $"CharacterSelect_hold_{next}_{shots[next]:F2}s.png");
                next++;
            }
        }

        Debug.Log($"{Tag} 길게 누르기 캡처 — 대기 {repeat.DelaySeconds}s · 간격 {repeat.StartIntervalSeconds}s → {repeat.MinIntervalSeconds}s" +
                  $"(램프 {repeat.RampSeconds}s), 60fps 칸 이동 시각 {steps}, " +
                  $"마지막 포커스 {panel.FocusedCharacterId}");
    }

    static void Shoot(GameObject instance, Camera camera, RenderTexture rt, string file)
    {
        Texture2D shot = Render(instance, camera, rt);
        Save(shot, file);
        Object.DestroyImmediate(shot);
    }

    // 프리뷰 씬은 Update 가 돌지 않는다 — 패널 연출 시계를 이 값으로 바꿔 끼우고 원하는 시각으로 직접 그린다.
    static float s_previewNow;
    static string s_captureDir = CaptureDir;

    static void SampleAt(CharacterSelectPanel panel, float time)
    {
        s_previewNow = time;
        panel.SamplePreviewMotion(() => s_previewNow);
    }

    /// <summary>시계를 1초 넘겨 이동·정착을 끝낸다(정지 상태로 찍기).</summary>
    static void SettleForCapture(CharacterSelectPanel panel) => SampleAt(panel, s_previewNow + 1f);

    static void RunPreview(string dir, System.Action<GameObject, Camera, RenderTexture, CharacterSelectPanel> capture)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Play 중에는 실행하지 않는다 — 먼저 정지할 것.");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        if (prefab == null)
        {
            Debug.LogError($"{Tag} 패널 프리팹이 없다 — 먼저 'Tools/UI/Authoring/Character Select Panel'.");
            return;
        }

        Directory.CreateDirectory(dir);
        s_captureDir = dir;
        Scene preview = EditorSceneManager.NewPreviewScene();
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        Camera camera = null;
        CharacterSelectPanel panel = null;
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);

            var cameraGo = new GameObject("PreviewCamera");
            SceneManager.MoveGameObjectToScene(cameraGo, preview);
            camera = cameraGo.AddComponent<Camera>();
            camera.scene = preview;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.magenta; // 배경이 덮지 못한 곳이 보이게
            camera.targetTexture = rt;
            camera.cameraType = CameraType.Game;

            Canvas canvas = instance.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;

            panel = instance.GetComponent<CharacterSelectPanel>();
            SampleAt(panel, 0f);
            capture(instance, camera, rt, panel);
        }
        finally
        {
            if (panel != null)
            {
                panel.SamplePreviewMotion(null);
                panel.Hide(); // 정적 열림 수 되돌리기(IsEscapeClaimed)
            }
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    static void CaptureStill(GameObject instance, Camera camera, RenderTexture rt, CharacterSelectPanel panel)
    {
        {
            // 1) 잠금 해제 — 지금 = 0번(팔라딘, 위 카드 "현재" 표식), 포커스 = 1번(거너). 이동 연출은 시계를 넘겨 정지 상태로 찍는다.
            panel.Show(0, false);
            panel.MoveFocus(1);
            SettleForCapture(panel);
            Texture2D unlocked = Render(instance, camera, rt);
            Save(unlocked, "CharacterSelect_unlocked.png");

            // 배경 일반판 vs _gamma 판 — 왼쪽(카드·이름판 없는 영역) 평균 밝기를 목업과 비교
            Texture2D mockup = LoadMockup();
            if (mockup != null)
            {
                SaveSideBySide(mockup, unlocked, "CharacterSelect_vs_mockup.png");
                float normal = LeftAreaLuma(unlocked), target = LeftAreaLuma(mockup);
                Image[] tiles = instance.transform.Find("Content/Bg_Rotate/Bg_Scroll").GetComponentsInChildren<Image>(true);
                Sprite gamma = LoadSprite(BackgroundLoopGammaSprite);
                Sprite original = tiles[0].sprite;
                foreach (Image t in tiles) t.sprite = gamma;
                Texture2D gammaShot = Render(instance, camera, rt);
                float gammaLuma = LeftAreaLuma(gammaShot);
                Save(gammaShot, "CharacterSelect_bg_gamma.png");
                foreach (Image t in tiles) t.sprite = original;
                Object.DestroyImmediate(gammaShot);
                Debug.Log($"{Tag} 배경 평균 밝기(왼쪽 영역, 0~255) — 목업 {target:F1} · 일반판 {normal:F1} · _gamma 판 {gammaLuma:F1} " +
                          $"→ {(Mathf.Abs(normal - target) <= Mathf.Abs(gammaLuma - target) ? "일반판" : "_gamma 판")}이 가깝다");
                Object.DestroyImmediate(mockup);
            }
            Object.DestroyImmediate(unlocked);

            // 2) 잠금 — 지금 = 1번, 포커스 = 2번(어쌔신), "준비를 해제해야" 문구
            panel.Show(1, true);
            panel.MoveFocus(1);
            SettleForCapture(panel);
            Texture2D locked = Render(instance, camera, rt);
            Save(locked, "CharacterSelect_locked.png");
            Object.DestroyImmediate(locked);
        }
    }

    static Texture2D Render(GameObject instance, Camera camera, RenderTexture rt)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in instance.GetComponentsInChildren<TMP_Text>(true))
            text.ForceMeshUpdate(true, true);
        Canvas.ForceUpdateCanvases();
        camera.Render();
        return ReadBack(rt);
    }

    static void Save(Texture2D texture, string file)
    {
        string path = Path.Combine(s_captureDir, file);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Debug.Log($"{Tag} 캡처 저장: {path}");
    }

    static Texture2D LoadMockup()
    {
        string mockupPath = Path.Combine(Application.dataPath, MockupRelative);
        if (!File.Exists(mockupPath))
        {
            Debug.LogWarning($"{Tag} 목업이 없어 비교를 건너뛴다: {mockupPath}");
            return null;
        }

        var mockup = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        mockup.LoadImage(File.ReadAllBytes(mockupPath));
        if (mockup.width == 1920 && mockup.height == 1080)
            return mockup;

        Debug.LogWarning($"{Tag} 목업 크기가 {mockup.width}x{mockup.height} 다(1920x1080 기대) — 비교를 건너뛴다.");
        Object.DestroyImmediate(mockup);
        return null;
    }

    static void SaveSideBySide(Texture2D left, Texture2D right, string file)
    {
        var combined = new Texture2D(left.width + right.width, left.height, TextureFormat.RGBA32, false);
        try
        {
            combined.SetPixels(0, 0, left.width, left.height, left.GetPixels());
            combined.SetPixels(left.width, 0, right.width, right.height, right.GetPixels());
            combined.Apply();
            Save(combined, file);
        }
        finally
        {
            Object.DestroyImmediate(combined);
        }
    }

    /// <summary>배경만 보이는 왼쪽 영역(x 300~900, 위에서 y 150~700) 평균 밝기.</summary>
    static float LeftAreaLuma(Texture2D texture)
    {
        Color32[] pixels = texture.GetPixels32();
        double sum = 0;
        int n = 0;
        for (int y = 1080 - 700; y < 1080 - 150; y += 2)
        for (int x = 300; x < 900; x += 2)
        {
            Color32 c = pixels[y * texture.width + x];
            sum += 0.299 * c.r + 0.587 * c.g + 0.114 * c.b;
            n++;
        }
        return (float)(sum / n);
    }

    static Texture2D ReadBack(RenderTexture rt)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        return texture;
    }

    // ── 공통 ─────────────────────────────────────────────────

    static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
    static readonly Vector2 Right = new Vector2(1f, 0.5f);

    static Vector2 NativeSize(Sprite sprite) => sprite.rect.size * (100f / sprite.pixelsPerUnit);

    /// <summary>1920×1080 좌상단 기준 디자인 rect → <paramref name="anchor"/>(화면 모서리·변) 기준 배치. 기준 해상도에서는 목업 자리 그대로.</summary>
    static void Place(RectTransform rect, Vector2 anchor, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x + width * 0.5f - anchor.x * 1920f, 1080f - (y + height * 0.5f) - anchor.y * 1080f);
        rect.sizeDelta = new Vector2(width, height);
    }

    /// <summary>부모 가운데 기준 자식.</summary>
    static RectTransform Child(string name, Transform parent, Vector2 position, Vector2 size)
    {
        RectTransform rect = NewRect(name, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Image AddImage(GameObject go, Sprite sprite, Color color)
    {
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>
    /// 한 줄 TMP. 기본 Overflow — NotoSansKR 줄 높이(≈1.45em)가 상자보다 크면 Ellipsis 는 글자를 통째로 숨긴다.
    /// 폭이 정해진 이름 칸만 <paramref name="ellipsis"/> 로 말줄임.
    /// </summary>
    static TMP_Text AddText(RectTransform rect, TMP_FontAsset font, string value, float size,
        TextAlignmentOptions align, Color color, bool ellipsis = false)
    {
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.alignment = align;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = ellipsis ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }
}
