using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// <c>CombatHUD.prefab</c> 에 미니맵·보스타이머 슬롯을 만든다.
///
/// 🔴 <b>왜 저작 스크립트인가</b> — <c>CombatHUD.prefab</c> 은 5000줄이 넘는다. YAML 을 손으로
///    고치면 HUD 전체가 조용히 깨진다. 기존 선례(<c>BossRoomAuthoring</c> ·
///    <c>MonsterSceneBossSetup</c> · <c>BossVariantAuthoring</c>)와 같은 방식으로,
///    <b>유니티가 직렬화하게</b> 둔다.
///
/// 🔴 <b>멱등하다</b> — 다시 돌려도 중복 생성되지 않고 위치·배선만 갱신한다.
///    2026-09-19 에 <c>.meta</c> 누락으로 스프라이트 참조 7건이 끊긴 전례가 있으므로,
///    배선이 끊기면 이걸 다시 돌려 복구한다.
///
/// 좌표는 레퍼런스 목업(<c>minimap.png</c> 합성본, 1920×1080)에서 실측한 값이다.
/// </summary>
public static class CombatHudSlotAuthoring
{
    const string HudPath = "Assets/2.Prefabs/UI/CombatHUD.prefab";
    const string ArtDir = "Assets/50.Art/UI/HUD/";

    const string MinimapSlotName = "MinimapSlot";
    const string TimerSlotName = "BossTimerSlot";

    // 레퍼런스 실측(1920×1080 기준, 우하단 앵커):
    //  타이머  — 우측 끝 x≈1885 / 바닥 y≈25, 크기는 아트 네이티브(461×103)
    //
    // 🔴 미니맵은 레퍼런스 목업 좌표(중심 1637,305 · 360 정사각)를 **그대로 쓸 수 없다.**
    //    실제 게임의 키가이드가 목업보다 아래에 있어서, 실측하면:
    //      키가이드 아래끝 UI y ≈ 404 / 타이머 위끝 y ≈ 128  → 쓸 수 있는 띠는 **약 276 단위**뿐.
    //    360 정사각을 그 자리에 두면 키가이드를 81 만큼 침범한다(2026-09-20 실측).
    //    그래서 띠 안에 들어가도록 줄이고 우측으로 붙였다.
    //    ⚠️ 이 슬롯 크기가 곧 미니맵 크기다 — MinimapController 가 슬롯 한 변을 √2 로 나눠
    //       PanelSize 를 잡는다(회전한 정사각형의 외접). 280 → PanelSize 약 198.
    static readonly Vector2 MinimapSize = new Vector2(280f, 280f);
    static readonly Vector2 MinimapPos = new Vector2(-160f, 265f);
    static readonly Vector2 TimerSize = new Vector2(461f, 103f);
    static readonly Vector2 TimerPos = new Vector2(-35f, 25f);

    [MenuItem("Tools/UI/Authoring/CombatHUD — 미니맵·보스타이머 슬롯 생성")]
    public static void BuildSlots()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HudPath);
        if (root == null)
        {
            Debug.LogError($"[CombatHudSlot] 프리팹을 열 수 없다: {HudPath}");
            return;
        }

        try
        {
            BuildMinimapSlot(root.transform);
            BuildTimerSlot(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
            Debug.Log($"[CombatHudSlot] 슬롯 생성/갱신 완료 — {HudPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>미니맵 슬롯은 빈 RectTransform 하나뿐이다 — 내용은 MinimapController 가 런타임에 채운다.</summary>
    static void BuildMinimapSlot(Transform parent)
    {
        RectTransform slot = EnsureChild(parent, MinimapSlotName);
        AnchorBottomRight(slot, MinimapPos, MinimapSize, new Vector2(0.5f, 0.5f));
    }

    static void BuildTimerSlot(Transform parent)
    {
        RectTransform slot = EnsureChild(parent, TimerSlotName);
        AnchorBottomRight(slot, TimerPos, TimerSize, new Vector2(1f, 0f));

        // ── 틀 ──────────────────────────────────────────────────────
        RectTransform frame = EnsureChild(slot, "Frame");
        Stretch(frame);
        Image frameImage = EnsureComponent<Image>(frame.gameObject);
        frameImage.sprite = LoadSprite("slot_timer");
        frameImage.type = Image.Type.Simple;
        frameImage.raycastTarget = false;

        // ── 채움 ────────────────────────────────────────────────────
        // 틀(461×103) 안에서 3칸이 차지하는 영역에 맞춘다. 채움 아트는 384×53.
        RectTransform fill = EnsureChild(slot, "Fill");
        Stretch(fill);
        fill.offsetMin = new Vector2(14f, 25f);
        fill.offsetMax = new Vector2(-63f, -25f);
        Image fillImage = EnsureComponent<Image>(fill.gameObject);
        fillImage.sprite = LoadSprite("gauge_timer");
        fillImage.raycastTarget = false;
        // 🔴 Filled/Horizontal 이어야 BossTimerHUD 의 fillAmount 가 먹는다.
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 0f;

        // ── 해골 캡 ─────────────────────────────────────────────────
        RectTransform skull = EnsureChild(slot, "BossIcon");
        skull.anchorMin = skull.anchorMax = new Vector2(1f, 0.5f);
        skull.pivot = new Vector2(1f, 0.5f);
        skull.sizeDelta = new Vector2(90f, 82f);
        skull.anchoredPosition = new Vector2(-6f, 0f);
        Image skullImage = EnsureComponent<Image>(skull.gameObject);
        skullImage.sprite = LoadSprite("icon_timer_boss");
        skullImage.type = Image.Type.Simple;
        skullImage.raycastTarget = false;

        // ── 표시 스크립트 배선 ──────────────────────────────────────
        // 🔴 표시 on/off 는 CanvasGroup 알파로 한다 — GameObject 를 끄면 BossTimerHUD.Update 가
        //    멈춰서 매니저가 나중에 생겨도 되살아나지 못한다.
        CanvasGroup group = EnsureComponent<CanvasGroup>(slot.gameObject);

        BossTimerHUD hud = EnsureComponent<BossTimerHUD>(slot.gameObject);
        var so = new SerializedObject(hud);
        so.FindProperty("fillImage").objectReferenceValue = fillImage;
        so.FindProperty("group").objectReferenceValue = group;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 형제 순서: 틀 → 채움 → 해골.
        frame.SetSiblingIndex(0);
        fill.SetSiblingIndex(1);
        skull.SetSiblingIndex(2);
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────

    static RectTransform EnsureChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            var rt = existing as RectTransform;
            if (rt != null) return rt;

            // RectTransform 이 아니면 UI 로 못 쓴다 — 지우고 다시 만든다.
            Object.DestroyImmediate(existing.gameObject);
        }

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static void AnchorBottomRight(RectTransform rt, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    static Sprite LoadSprite(string fileName)
    {
        string path = ArtDir + fileName + ".png";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

        // 🔴 아트는 SVN 이고 `.meta` 가 빠지면 참조가 조용히 끊긴다(2026-09-19 7건 사고).
        //    조용히 null 을 넣지 말고 크게 울린다.
        if (sprite == null)
            Debug.LogError($"[CombatHudSlot] 스프라이트를 못 찾았다: {path} — SVN 업데이트와 .meta 를 확인할 것.");

        return sprite;
    }
}
