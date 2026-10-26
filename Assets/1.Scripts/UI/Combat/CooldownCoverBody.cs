using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 쿨다운 덮개(Filled Vertical 마스크) 의 본체 구간. 새 마스크는 캔버스(99×101) 전체가 채워지는데 실제 본체는 그 일부라,
/// 남은 비율을 그대로 fillAmount 에 넣으면 시작 직후 본체 위 빈 캔버스를 지나는 동안 덮개가 꽉 찬 채 멈춰 보인다.
/// 남은 비율을 본체 높이에 선형으로 다시 매핑한다. 값은 1x 마스크 픽셀(캔버스 위에서부터, 본체 첫·끝 행 포함) — 2x 그림도 1x 기준으로 적는다.
/// </summary>
[System.Serializable]
public struct CooldownCoverBody
{
    [Tooltip("마스크 캔버스 높이(1x px).")]
    public float canvasHeightPx;

    [Tooltip("본체 맨 윗 행(1x px, 캔버스 위에서부터).")]
    public float bodyTopPx;

    [Tooltip("본체 맨 아랫 행(1x px, 캔버스 위에서부터, 포함).")]
    public float bodyBottomPx;

    public CooldownCoverBody(float canvasHeightPx, float bodyTopPx, float bodyBottomPx)
    {
        this.canvasHeightPx = canvasHeightPx;
        this.bodyTopPx = bodyTopPx;
        this.bodyBottomPx = bodyBottomPx;
    }

    /// <summary>mask_skill_body 실측(2026-10-26, 2x y42..189).</summary>
    public static CooldownCoverBody SkillBody => new CooldownCoverBody(101f, 21f, 94f);

    /// <summary>mask_passive_body_no_tabs·mask_ult_body_no_tabs 실측(2026-10-26, 2x y14..189).</summary>
    public static CooldownCoverBody SideBody => new CooldownCoverBody(101f, 7f, 94f);

    /// <summary>덮개 Image 에 남은 비율을 넣는다. 세로 채움이 아니거나 구간이 비어 있으면 남은 비율 그대로.</summary>
    public void Apply(Image cover, float remaining01)
    {
        if (cover == null)
            return;

        if (cover.fillMethod != Image.FillMethod.Vertical || canvasHeightPx <= 0f)
        {
            cover.fillAmount = Mathf.Clamp01(remaining01);
            return;
        }

        // 본체 아래 끝 = 끝 행의 아래 가장자리(+1). 채움 시작점에서 잰 0..1 로 바꾼다.
        float top01 = bodyTopPx / canvasHeightPx;
        float bottom01 = (bodyBottomPx + 1f) / canvasHeightPx;
        bool fromBottom = cover.fillOrigin == (int)Image.OriginVertical.Bottom;
        cover.fillAmount = fromBottom
            ? CanvasFill(remaining01, 1f - bottom01, 1f - top01)
            : CanvasFill(remaining01, top01, bottom01);
    }

    /// <summary>
    /// 남은 비율(0..1) → 캔버스 기준 fillAmount. <paramref name="bodyStart01"/>·<paramref name="bodyEnd01"/> 은 채움 시작점에서 잰 본체 구간.
    /// 0 이하 = 0(덮개 없음), 1 이상 = 본체 끝, 사이 = 본체 구간에 선형.
    /// </summary>
    public static float CanvasFill(float remaining01, float bodyStart01, float bodyEnd01)
    {
        if (remaining01 <= 0f)
            return 0f;

        return Mathf.Lerp(Mathf.Clamp01(bodyStart01), Mathf.Clamp01(bodyEnd01), Mathf.Clamp01(remaining01));
    }
}
