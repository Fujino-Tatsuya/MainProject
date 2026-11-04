using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 태그 칩 한 줄 그리기 — 칩 원본 복제·이름·색(<see cref="SkillTooltipTagStyles"/>)·폭 계산·줄바꿈 배치(<see cref="SkillTooltipView.FlowTags"/>).
/// 할 일 212 — 스킬 툴팁(<see cref="SkillTooltipView"/>)에서 뽑아 특성 카드(<see cref="TraitCardView"/>)와 같이 쓴다. 칩 모양 = 원본 프리팹(배경 Image + 자식
/// <see cref="SkillTooltipView.TagChipRingName"/> 링 Image + TMP), 테두리·글자 = 그 태그의 글자색. 보는 쪽마다 인스턴스 하나(복제한 칩을 들고 재사용).
/// </summary>
public sealed class SkillTooltipTagChipRow
{
    private readonly List<RectTransform> chips = new List<RectTransform>();
    private readonly List<float> widths = new List<float>();
    private readonly List<Vector2> positions = new List<Vector2>();
    private readonly HashSet<SkillTooltipTag> warnedUnstyledTags = new HashSet<SkillTooltipTag>();

    /// <summary>
    /// <paramref name="tags"/> 를 <paramref name="row"/> 왼쪽 위부터 칩으로 놓는다(남는 칩은 끈다). 태그가 없으면 줄을 끈다.
    /// 반환 = 칩 전체 높이(태그 없으면 0).
    /// </summary>
    /// <param name="logPrefix">경고 머리(예 <c>[SkillTooltip]</c>).</param>
    /// <param name="stylesFieldLabel">색 표가 비었을 때 경고에 적을 칸 이름(예 <c>SkillTooltipView.tagStyles</c>).</param>
    public float Refresh(IReadOnlyList<SkillTooltipTag> tags, RectTransform row, RectTransform template, SkillTooltipTagStyles styles,
        float padding, float spacing, string logPrefix, string stylesFieldLabel, Object context)
    {
        if (row == null || template == null)
            return 0f;

        int count = tags != null ? tags.Count : 0;
        while (chips.Count < count)
        {
            RectTransform chip = Object.Instantiate(template, row);
            chip.name = template.name + "_" + chips.Count;
            chips.Add(chip);
        }

        widths.Clear();
        for (int i = 0; i < chips.Count; i++)
        {
            bool used = i < count;
            chips[i].gameObject.SetActive(used);
            if (!used)
                continue;

            TMP_Text label = chips[i].GetComponentInChildren<TMP_Text>(true);
            string text = SkillTooltipTags.Name(tags[i]).Text;
            float width = padding * 2f;
            ChipColors(tags[i], styles, logPrefix, stylesFieldLabel, context, out Color32 background, out Color32 foreground);
            if (chips[i].TryGetComponent(out Image chipImage))
                chipImage.color = background;
            Transform ring = chips[i].Find(SkillTooltipView.TagChipRingName); // 둥근 테두리 = 글자색 그대로
            if (ring != null && ring.TryGetComponent(out Image ringImage))
                ringImage.color = foreground;
            if (label != null)
            {
                label.text = text;
                label.color = foreground;
                width += label.GetPreferredValues(text).x;
            }
            widths.Add(width);
        }

        float rowHeight = template.rect.height;
        float height = SkillTooltipView.FlowTags(widths, row.rect.width, spacing, rowHeight, positions);
        for (int i = 0; i < count; i++)
        {
            RectTransform chip = chips[i];
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0f, 1f);
            chip.anchoredPosition = positions[i];
            chip.sizeDelta = new Vector2(widths[i], rowHeight);
        }

        row.gameObject.SetActive(count > 0);
        return height;
    }

    // 색 표에서 칩 색. 표가 없거나 줄이 없으면 기본색 + 태그마다 경고 한 번(테이블 Play·빌드 검증도 경고한다).
    private void ChipColors(SkillTooltipTag tag, SkillTooltipTagStyles styles, string logPrefix, string stylesFieldLabel, Object context,
        out Color32 background, out Color32 foreground)
    {
        if (styles != null)
        {
            if (styles.TryGetColors(tag, out background, out foreground)) // 없으면 표의 기본색을 담아 false
                return;
        }
        else
        {
            background = SkillTooltipTagStyles.FallbackBackground;
            foreground = SkillTooltipTagStyles.FallbackText;
        }

        if (warnedUnstyledTags.Add(tag))
            Debug.LogWarning($"{logPrefix} 태그 '{tag}' 의 색이 {(styles != null ? styles.name : stylesFieldLabel + "(비어 있음)")} 에 없다 — 기본색으로 그린다.", context);
    }
}
