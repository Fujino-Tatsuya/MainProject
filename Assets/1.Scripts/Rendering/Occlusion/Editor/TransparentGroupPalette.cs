// 투명화 그룹 툴 — 그룹 색 자동 배정.
// PLAN-transparent-group-tool.md 결정 13.
//
// NetVis 는 48색 하드코딩 배열을 쓴다. 여기서는 그 배열을 옮겨오지 않고 같은 성질
// (인덱스 → 고정된 색, 인접 인덱스끼리 잘 구분됨)을 황금비 색상환 걸음으로 만든다.
// 값이 코드에 박혀 있지 않아 개수 제한도 없다.

using UnityEngine;

namespace VeyTrace.Rendering.Occlusion.Editor
{
    public static class TransparentGroupPalette
    {
        // 황금비. 색상환을 이 비율로 돌면 연속한 인덱스의 색상이 최대한 멀어진다.
        const float k_GoldenRatioConjugate = 0.618033988f;

        // 첫 색을 초록 근처에서 시작한다(선택 하이라이트의 주황·파랑과 덜 겹친다).
        const float k_HueOffset = 0.33f;

        /// <summary>인덱스가 같으면 항상 같은 색. 음수도 받는다.</summary>
        public static Color GetColor(int index)
        {
            var i = index % int.MaxValue;
            if (i < 0) i += int.MaxValue;

            var hue = Mathf.Repeat(k_HueOffset + i * k_GoldenRatioConjugate, 1f);

            // 채도·명도를 3주기로 흔들어 색상만으로 구분이 어려운 경우를 보완한다.
            var cycle = i % 3;
            var saturation = cycle == 1 ? 0.55f : 0.85f;
            var value = cycle == 2 ? 0.70f : 0.95f;

            return Color.HSVToRGB(hue, saturation, value);
        }
    }
}
