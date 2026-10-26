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
            if (!ApplyHealthBar(root.transform))
            {
                Debug.LogError("[CombatHudResource] 적용 실패 — 저장하지 않았다. 위 오류를 확인할 것.");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
            Debug.Log($"[CombatHudResource] 적용 완료 — {HudPath}");
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
    /// ExBar 는 켜지 않는다(값 연결과 함께 별도 단계).
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
