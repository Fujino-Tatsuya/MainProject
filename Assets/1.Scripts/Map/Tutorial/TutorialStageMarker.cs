using UnityEngine;

/// <summary>
/// "이 스테이지는 튜토리얼이다" 표시(비네트워크). <c>StageTutorial.prefab</c> 루트에 붙는다
/// (<c>TutorialStageAuthoring.BuildStagePrefab</c> 이 붙인다).
///
/// 🔴 튜토리얼 판별은 <b>여기 한 곳</b>에서 한다 — <see cref="IsActive"/>. 튜토리얼 전용 규칙(진행 상태·타이머 지연·
///    패드 잠금 등)은 모두 이 값을 본다. 절차 맵(<c>Stage1_Procedural</c>)·Debug 씬에는 마커가 없어 false 다.
/// 판별 기준 = 켜진 스테이지 루트에 이 컴포넌트가 있는가. 꺼진 스테이지(GameObject 비활성)는 OnEnable 이 안 돌아 세지 않는다
/// (<see cref="MapGenerator"/> 가 활성 ZoneSlot 만 모으는 것과 같은 기준).
/// 선택하면 구간 감지(<see cref="TutorialAreaLayout"/>) 기즈모를 그린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialStageMarker : MonoBehaviour
{
    private static TutorialStageMarker s_active;

    /// <summary>지금 켜진 스테이지가 튜토리얼인가. 씬 로드 직후(OnEnable 뒤)부터 유효 — 맵 생성 이벤트 시점엔 확정돼 있다.</summary>
    public static bool IsActive => s_active != null;

    public static TutorialStageMarker Active => s_active;

    /// <summary>생성된 맵의 슬롯이 이 마커의 스테이지에 속하는가(마커와 실제 생성 스테이지가 어긋나지 않았는지 확인용).</summary>
    public static bool OwnsGeneratedMap(MapGenerator generator)
    {
        if (s_active == null || generator == null || generator.Slots.Count == 0) return false;
        ZoneSlot slot = generator.Slots[0];
        return slot != null && slot.transform.IsChildOf(s_active.transform);
    }

    private void OnEnable()
    {
        if (s_active != null && s_active != this)
            Debug.LogWarning($"[Tutorial] TutorialStageMarker 가 둘 켜져 있다('{s_active.name}' · '{name}') — 나중 것을 쓴다.", this);
        s_active = this;
    }

    private void OnDisable()
    {
        if (s_active == this) s_active = null;
    }

#if UNITY_EDITOR
    // 구간 감지(S2) 디버그 — 이 스테이지를 선택하면 존·복도·복도 중간 지점·여유 경계를 그린다.
    // 여유 값은 씬의 TutorialDirector 인스펙터 값(Play 중이 아니면 찾아서), 없으면 기본값.
    private void OnDrawGizmosSelected()
    {
        TutorialDirector director = TutorialDirector.Instance != null
            ? TutorialDirector.Instance : FindAnyObjectByType<TutorialDirector>(FindObjectsInactive.Include);
        var tracker = director != null
            ? new TutorialAreaTracker(director.AreaEnterMargin, director.AreaExitMargin)
            : new TutorialAreaTracker();
        TutorialAreaLayout.TryBuild(transform, tracker, out _);
        TutorialAreaLayout.DrawGizmos(tracker, transform.position.y + 0.2f);
    }
#endif
}
