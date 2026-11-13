using UnityEngine;

/// <summary>
/// 몬스터 프리팹 루트에 붙이면 머리 위(발밑) 체력바 위치를 공용 설정 대신 이 값으로 쓴다(할 일 274 — 로비 훈련장 봇 전용 튜닝).
/// 없으면 지금처럼 <see cref="MonsterOverheadHealthBarSettings"/> 의 등급별 값(footScreenOffset·midBossFootScreenOffset).
/// 스폰 때 <see cref="MonsterOverheadHealthBarRule.TryInstall"/> 가 읽고, Play 중 인스펙터·Scene 뷰 핸들로 바꾸면 이미 붙은 바도 바로 옮긴다.
/// 편집 미리보기: 선택하면 Scene/Game 카메라 기준으로 바가 뜰 자리를 기즈모 막대로(에디터 <c>MonsterOverheadHealthBarOffsetEditor</c> — 끌어서 값 변경).
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterOverheadHealthBarOffset : MonoBehaviour
{
    /// <summary>미리보기 막대 폭(캔버스 px) — 일반 몬스터 바와 비슷한 폭. 표시 전용.</summary>
    public const float PreviewBarWidth = 120f;

    [Tooltip("발밑에서 화면 아래로 내릴 픽셀(1080p 캔버스 단위). 공용 설정의 등급별 값을 이 몬스터만 덮어쓴다. Play 중 바꾸면 바로 반영.")]
    [SerializeField, Min(0f)] float footScreenOffset = 50f;

    public float FootScreenOffset
    {
        get => footScreenOffset;
        set { footScreenOffset = Mathf.Max(0f, value); ApplyToBar(); }
    }

    void OnValidate() => ApplyToBar();

    /// <summary>이미 붙은 체력바(자식 UnitOverheadHealthBar)를 지금 값으로 다시 놓는다. 바가 없으면(스폰 전·편집 모드) 아무 일도 안 한다.</summary>
    void ApplyToBar()
    {
        if (!Application.isPlaying) return;
        UnitOverheadHealthBar bar = GetComponentInChildren<UnitOverheadHealthBar>(true);
        if (bar != null)
            bar.ConfigurePlacement(0f, MonsterOverheadHealthBarVisibility.FootScreenOffset(footScreenOffset));
    }

    /// <summary>카메라 기준 바 중심 월드 점(발 위치에서 화면 아래로 오프셋). 카메라 뒤면 false.</summary>
    public bool TryGetPreviewPoint(Camera cam, out Vector3 world, out float pixelsPerUnit)
    {
        world = transform.position;
        pixelsPerUnit = 1f;
        if (cam == null) return false;
        Vector3 feet = cam.WorldToScreenPoint(transform.position);
        if (feet.z <= 0f) return false;
        pixelsPerUnit = MonsterOverheadHealthBarVisibility.ScreenPixelsPerCanvasUnit(cam.pixelHeight, OverheadHealthBarCanvas.ReferenceResolution.y);
        Vector3 bar = feet + Vector3.down * (footScreenOffset * pixelsPerUnit);
        world = cam.ScreenToWorldPoint(bar);
        return true;
    }

    void OnDrawGizmosSelected()
    {
        Camera cam = Camera.current;
        if (!TryGetPreviewPoint(cam, out Vector3 center, out float ppu)) return;
        Vector3 c = cam.WorldToScreenPoint(center);
        float half = PreviewBarWidth * 0.5f * ppu;
        Vector3 left = cam.ScreenToWorldPoint(c + Vector3.left * half);
        Vector3 right = cam.ScreenToWorldPoint(c + Vector3.right * half);
        Vector3 up = cam.ScreenToWorldPoint(c + Vector3.up * 4f * ppu) - center;
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.95f);
        Gizmos.DrawLine(left - up, right - up);
        Gizmos.DrawLine(left + up, right + up);
        Gizmos.DrawLine(left - up, left + up);
        Gizmos.DrawLine(right - up, right + up);
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
        Gizmos.DrawLine(transform.position, center);
    }
}
