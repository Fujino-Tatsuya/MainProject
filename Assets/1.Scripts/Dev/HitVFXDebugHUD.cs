// HitVFXDebugHUD.cs
// 피격 이펙트 런타임 교체 HUD — 에디터/개발 빌드 전용 (릴리스 빌드에서는 컴파일 제외).
//
// 사용법:
//   1) 아무 씬(보통 4.MapScene)의 빈 GameObject에 이 컴포넌트를 추가한다.
//   2) 플레이 → F1로 이펙트 종류 순환, F2로 타격점 계산 방식 순환.
//   3) 현재 적용 중인 두 값이 화면에 표시된다.
//
// 두 값의 성격이 다르다:
//   - 이펙트 종류: 유닛마다 자기 값(hitVFXType)을 갖고, 이건 그 위에 씌우는 오버라이드다.
//     그래서 순환에 "프리팹 값"(오버라이드 해제) 상태가 끼어 있다 — 별도 해제 키가 필요 없다.
//   - 타격점 방식: 전 유닛 공통이라 EffectManager가 값 자체를 소유한다. 해제 상태가 없다.
//
// ⚠️ 변경은 이 머신에만 걸린다. 피격 이펙트는 각 피어가 자기 로컬에서 해석해 재생하므로
//    (HitVFXPlayback / MonsterBase.PlayHitVFXRpc 참조), 키를 누른 창만 바뀐다. 이건 결함이 아니라
//    의도다 — MPPM 창 두 개를 나란히 놓고 서로 다른 설정을 동시에 비교할 수 있다.
//
// 주의: 이 컴포넌트는 #if UNITY_EDITOR || DEVELOPMENT_BUILD 로 감싸져 있어 릴리스 빌드엔 클래스가
//       없다. 릴리스로 출하되는 씬/프리팹에는 붙여두지 말 것(미싱 스크립트 경고 방지).
//       ProfilerHUD와 동일한 관례다.

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;   // 신 Input System (이 프로젝트: Active Input Handling = New)
#endif

[DisallowMultipleComponent]
public sealed class HitVFXDebugHUD : MonoBehaviour
{
    // 사용 중인 키를 피해서 배정했다: F8=ProfilerHUD · F10=디버그 부활 · M=맵 오버뷰 ·
    // F=다리 상호작용 · [ ]=카메라 전환/미니맵 줌 · ESC=씬 전환.
#if ENABLE_INPUT_SYSTEM
    private const Key CycleEffectKey = Key.F1;
    private const Key CyclePointModeKey = Key.F2;
#else
    private const KeyCode CycleEffectKey = KeyCode.F1;
    private const KeyCode CyclePointModeKey = KeyCode.F2;
#endif

    // enum 값을 하드코딩하지 않고 리플렉션으로 한 번만 뽑는다 — 종류가 늘어도 이 파일은 안 고친다.
    private static readonly EffectCatalog.HitVFXType[] VFXTypes =
        (EffectCatalog.HitVFXType[])Enum.GetValues(typeof(EffectCatalog.HitVFXType));

    private static readonly EffectHitPoint.HitPointMode[] PointModes =
        (EffectHitPoint.HitPointMode[])Enum.GetValues(typeof(EffectHitPoint.HitPointMode));

    [Header("표시")]
    [Tooltip("HUD 표시 on/off. 적용된 값 자체는 이 설정과 무관하게 유지된다.")]
    [SerializeField] private bool visible = true;

    [Tooltip("화면 왼쪽 위 기준 오프셋(px).")]
    [SerializeField] private Vector2 offset = new Vector2(12f, 12f);

    [SerializeField, Range(0.6f, 2.5f)] private float scale = 1f;

    private GUIStyle _style;

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null) return;   // 키보드 없는 환경(패드 전용 등) — 조용히 넘어간다.

        if (kb[CycleEffectKey].wasPressedThisFrame) CycleEffect();
        if (kb[CyclePointModeKey].wasPressedThisFrame) CyclePointMode();
#else
        if (Input.GetKeyDown(CycleEffectKey)) CycleEffect();
        if (Input.GetKeyDown(CyclePointModeKey)) CyclePointMode();
#endif
    }

    /// <summary>프리팹 값 → 1 → 2 → … → 마지막 → 다시 프리팹 값.</summary>
    private static void CycleEffect()
    {
        if (!EnsureManager()) return;

        EffectCatalog.HitVFXType? current = EffectManager.Instance.HitVFXOverride;

        // 오버라이드가 없으면(=프리팹 값) 첫 종류로, 마지막 종류였으면 다시 해제로 돌아간다.
        int next = current.HasValue ? Array.IndexOf(VFXTypes, current.Value) + 1 : 0;
        EffectCatalog.HitVFXType? applied =
            next < VFXTypes.Length ? VFXTypes[next] : (EffectCatalog.HitVFXType?)null;

        EffectManager.Instance.HitVFXOverride = applied;
        Edit.Log($"[HitVFX] 이펙트 → {Describe(applied)} (이 머신에만 적용)");
    }

    /// <summary>해제 상태가 없다 — EffectManager가 값 자체를 소유하므로 순환만 한다.</summary>
    private static void CyclePointMode()
    {
        if (!EnsureManager()) return;

        int index = Array.IndexOf(PointModes, EffectManager.Instance.HitPointMode);

        // IndexOf가 못 찾으면 -1 → +1 = 0 이라 첫 값으로 안전하게 떨어진다.
        EffectHitPoint.HitPointMode applied = PointModes[(index + 1) % PointModes.Length];

        EffectManager.Instance.HitPointMode = applied;
        Edit.Log($"[HitVFX] 타격점 방식 → {applied} (이 머신에만 적용)");
    }

    private static bool EnsureManager()
    {
        if (EffectManager.Instance != null) return true;

        // 조용히 실패하면 "키를 눌렀는데 아무 일도 안 일어난다"가 된다. 원인을 말해준다.
        Edit.LogWarning("[HitVFX] EffectManager가 씬에 없어 값을 바꿀 수 없습니다. " +
                        "5.VFX/EffectManager.prefab을 씬에 배치하세요.");
        return false;
    }

    // 오버라이드가 없을 때 특정 이름을 적으면 거짓말이 된다 — 유닛마다 자기 값을 쓰는 중이다.
    private static string Describe(EffectCatalog.HitVFXType? type) =>
        type.HasValue ? type.Value.ToString() : "프리팹 값";

    private void OnGUI()
    {
        if (!visible) return;

        _style ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(14f * scale),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
        };

        bool ready = EffectManager.Instance != null;
        EffectCatalog.HitVFXType? effect = ready ? EffectManager.Instance.HitVFXOverride : null;

        string effectLine = $"F1  이펙트     : {(ready ? Describe(effect) : "EffectManager 없음")}";
        string modeLine = $"F2  타격점 방식 : {(ready ? EffectManager.Instance.HitPointMode.ToString() : "-")}";

        float lineHeight = 22f * scale;
        DrawLine(new Rect(offset.x, offset.y, 520f * scale, lineHeight),
                 effectLine, effect.HasValue ? Color.cyan : Color.white);
        DrawLine(new Rect(offset.x, offset.y + lineHeight, 520f * scale, lineHeight),
                 modeLine, Color.white);
    }

    /// <summary>밝은 배경에서도 읽히도록 1px 그림자를 깔고 그린다.</summary>
    private void DrawLine(Rect rect, string text, Color color)
    {
        _style.normal.textColor = Color.black;
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _style);

        _style.normal.textColor = color;
        GUI.Label(rect, text, _style);
    }
}
#endif
