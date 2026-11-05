using UnityEngine;

/// <summary>
/// 패널 배경 블러(할 일 201) 튜닝값 — 모든 패널이 같이 쓴다. <see cref="PanelBackdropBlurFeature"/>(PC_Renderer)가 들고 있다가 <see cref="PanelBackdropBlur.Settings"/> 로 넘긴다.
/// 기본값은 옛 특성 창 스냅샷 블러(TraitBackdropBlurSettings.asset — 세기 1.42·1/4·반복 3·채도 0.6·어둡게 0.25)를 그대로 옮겼다.
/// 기획 수치가 아니라 연출 기술 값이라 데이터 테이블 시트(<c>[DataTableSheet]</c>)를 달지 않는다. 문서: Docs/tech/panel-backdrop-blur.md.
/// </summary>
[CreateAssetMenu(fileName = "PanelBackdropBlurSettings", menuName = "Rendering/Panel Backdrop Blur Settings")]
public sealed class PanelBackdropBlurSettings : ScriptableObject
{
    [Tooltip("블러 세기. 축소된 해상도의 텍셀 스텝에 곱해진다(MaskBlurSettings.blurStrength 와 같은 뜻).")]
    [Range(0f, 4f)] public float blurStrength = 1.42f;

    [Tooltip("블러를 계산할 해상도. 0=풀, 1=1/2, 2=1/4, 3=1/8. 깎을수록 싸고 더 흐려진다.")]
    [Range(0, 3)] public int downsampleShift = 2;

    [Tooltip("가로·세로 블러 반복 횟수. 패널이 열린 동안 매 프레임 이만큼 돈다(1/4 해상도라 싸다).")]
    [Range(1, 4)] public int iterations = 3;

    [Tooltip("블러 결과 채도. 0 = 완전 흑백, 1 = 원색. 루마 = Rec.709 가중치.")]
    [Range(0f, 1f)] public float saturation = 0.6f;

    [Tooltip("어둡게. 0 = 그대로, 1 = 검정. 옛 특성 창 어둡게 막 알파(0.25)와 같은 뜻 — 블러 세기와 함께 보간된다.")]
    [Range(0f, 1f)] public float darken = 0.25f;

    [Tooltip("열고 닫을 때 블러가 0↔1 로 바뀌는 시간(초, unscaled — 슬로우·히트스톱과 무관).")]
    [Range(0f, 1f)] public float fadeSeconds = 0.15f;
}
