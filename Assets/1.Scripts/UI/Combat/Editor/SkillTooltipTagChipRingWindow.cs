using UnityEditor;
using UnityEngine;

/// <summary>
/// 스킬 툴팁 태그 칩 테두리(링 9-slice) 두께 조절 — 두께를 고르고 다시 구우면 <c>SkillTooltipTagChipRing.png</c> 를 그 자리에 다시 쓴다
/// (guid 그대로라 CombatHUD 참조는 안 바뀐다). 색은 태그 글자색(<see cref="SkillTooltipTagStyles"/>)을 그대로 쓴다.
/// </summary>
public sealed class SkillTooltipTagChipRingWindow : EditorWindow
{
    private const float PixelsPerUiUnit = 2f;   // PPU 200 ÷ 캔버스 참조 PPU 100
    private const float ChipHeight = 26f;       // 칩 원본 높이(UI 단위)

    private float thickness;

    [MenuItem("Tools/UI/스킬 툴팁 태그 칩 테두리 두께")]
    private static void Open()
    {
        var window = GetWindow<SkillTooltipTagChipRingWindow>(true, "태그 칩 테두리 두께");
        window.thickness = SkillTooltipAuthoring.BakedRingThickness() ?? SkillTooltipAuthoring.DefaultTagChipRingThickness;
        window.minSize = new Vector2(340f, 110f);
    }

    private void OnGUI()
    {
        float? baked = SkillTooltipAuthoring.BakedRingThickness();
        EditorGUILayout.LabelField("지금 구운 두께", baked.HasValue ? $"{baked.Value:0.##} px" : "없음(다음 저작 때 기본값으로 굽는다)");
        thickness = EditorGUILayout.Slider("두께(px)", thickness,
            SkillTooltipAuthoring.MinTagChipRingThickness, SkillTooltipAuthoring.MaxTagChipRingThickness);
        float ui = thickness / PixelsPerUiUnit;
        EditorGUILayout.HelpBox($"{ui:0.##} UI 단위 = 칩 높이 {ChipHeight:0} 의 {ui / ChipHeight * 100f:0.#}% (기본 {SkillTooltipAuthoring.DefaultTagChipRingThickness} px)", MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("기본값"))
                thickness = SkillTooltipAuthoring.DefaultTagChipRingThickness;
            if (GUILayout.Button("다시 굽기"))
            {
                SkillTooltipAuthoring.EnsureTagChipRingSprite(thickness);
                Debug.Log($"[SkillTooltip] 태그 칩 테두리 두께 {thickness:0.##} px 로 다시 구웠다.");
            }
        }
    }
}
