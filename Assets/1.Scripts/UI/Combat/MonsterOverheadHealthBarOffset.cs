using UnityEngine;

/// <summary>
/// 몬스터 프리팹 루트에 붙이면 머리 위(발밑) 체력바 위치를 공용 설정 대신 이 값으로 쓴다(할 일 274 — 로비 훈련장 봇 전용 튜닝).
/// 없으면 지금처럼 <see cref="MonsterOverheadHealthBarSettings"/> 의 등급별 값(footScreenOffset·midBossFootScreenOffset), 기준점 = 루트(발).
/// <list type="bullet">
/// <item><see cref="anchorLocalOffset"/> = 바 기준점(루트 로컬, 루트 스케일·회전을 따라감). 월봇처럼 모델이 루트보다 앞으로 쏠린 몸이면
/// 루트 기준 바는 봇이 보는 방향마다 몸 대비 다른 자리에 뜬다 — 기준점을 몸 중심 아래로 옮겨 방향과 무관하게 한다.</item>
/// <item><see cref="footScreenOffset"/> = 그 기준점에서 화면 아래로 내릴 px.</item>
/// </list>
/// 스폰 때 <see cref="MonsterOverheadHealthBarRule.TryInstall"/> 가 읽고, Play 중 인스펙터·Scene 뷰 핸들로 바꾸면 이미 붙은 바도 바로 옮긴다.
/// 편집 미리보기: 선택하면 Scene/Game 카메라 기준으로 바가 뜰 자리를 기즈모 막대로(에디터 <c>MonsterOverheadHealthBarOffsetEditor</c> — 끌어서 값 변경).
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterOverheadHealthBarOffset : MonoBehaviour
{
    /// <summary>미리보기 막대 폭(캔버스 px) — 일반 몬스터 바와 비슷한 폭. 표시 전용.</summary>
    public const float PreviewBarWidth = 120f;

    [Tooltip("체력바 기준점(루트 로컬 좌표, 루트 스케일·회전을 따라감). 0 = 루트(발). 모델이 루트에서 쏠려 있으면 몸 중심 바닥으로 옮긴다(월봇 = 앞 z 1.2).")]
    [SerializeField] Vector3 anchorLocalOffset;
    [Tooltip("기준점에서 화면 아래로 내릴 픽셀(1080p 캔버스 단위). 공용 설정의 등급별 값을 이 몬스터만 덮어쓴다. Play 중 바꾸면 바로 반영.")]
    [SerializeField, Min(0f)] float footScreenOffset = 50f;

    public float FootScreenOffset
    {
        get => footScreenOffset;
        set { footScreenOffset = Mathf.Max(0f, value); ApplyToBar(); }
    }

    public Vector3 AnchorLocalOffset => anchorLocalOffset;

    /// <summary>바 기준점 월드 좌표.</summary>
    public Vector3 AnchorWorld => transform.TransformPoint(anchorLocalOffset);

    void OnValidate() => ApplyToBar();

    /// <summary>체력바를 이 값으로 놓는다 — 스폰 때(규칙)와 값이 바뀔 때. 바 = 루트 자식 UnitOverheadHealthBar.</summary>
    public void ApplyTo(UnitOverheadHealthBar bar)
    {
        if (bar == null) return;
        bar.transform.localPosition = anchorLocalOffset;
        bar.ConfigurePlacement(0f, MonsterOverheadHealthBarVisibility.FootScreenOffset(footScreenOffset));
    }

    void ApplyToBar()
    {
        if (!Application.isPlaying) return;
        ApplyTo(GetComponentInChildren<UnitOverheadHealthBar>(true));
    }

    /// <summary>카메라 기준 바 중심 월드 점(기준점에서 화면 아래로 오프셋). 카메라 뒤면 false.</summary>
    public bool TryGetPreviewPoint(Camera cam, out Vector3 world, out float pixelsPerUnit)
    {
        world = AnchorWorld;
        pixelsPerUnit = 1f;
        if (cam == null) return false;
        Vector3 anchor = cam.WorldToScreenPoint(AnchorWorld);
        if (anchor.z <= 0f) return false;
        pixelsPerUnit = MonsterOverheadHealthBarVisibility.ScreenPixelsPerCanvasUnit(cam.pixelHeight, OverheadHealthBarCanvas.ReferenceResolution.y);
        world = cam.ScreenToWorldPoint(anchor + Vector3.down * (footScreenOffset * pixelsPerUnit));
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
        Gizmos.DrawLine(AnchorWorld, center);
        Gizmos.DrawWireSphere(AnchorWorld, 0.08f);
    }
}
