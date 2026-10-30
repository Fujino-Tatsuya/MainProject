using UnityEngine;

/// <summary>
/// 특성 선택 창 배경 블러(할 일 165 후속 A안) 튜닝값. 창이 열릴 때 1회 화면 캡처 → 축소 → 가로·세로 블러를 이 값으로 돈다.
/// 기본값은 <c>Assets/99.Settings/MaskBlurSettings.asset</c> 의 블러 값(blurStrength 1.42·downsampleShift 1)에서 옮겨 왔다.
/// 기획 수치가 아니라 연출 기술 값이라 데이터 테이블 시트(<c>[DataTableSheet]</c>)를 달지 않는다.
/// </summary>
[CreateAssetMenu(fileName = "TraitBackdropBlurSettings", menuName = "Traits/Trait Backdrop Blur Settings")]
public sealed class TraitBackdropBlurSettings : ScriptableObject
{
    [Tooltip("블러 셰이더(Hidden/Rendering/MaskBlur — 0번 가로·1번 세로 패스만 쓴다).\n" +
             "🔴 직렬화 참조로 물려 둬야 빌드에서 스트립되지 않는다. 비면 블러 없이 예전처럼 어둡게 막만 쓴다.")]
    public Shader blurShader;

    [Tooltip("블러 세기. 축소된 해상도의 텍셀 스텝에 곱해진다(MaskBlurSettings.blurStrength 와 같은 뜻).")]
    [Range(0f, 4f)] public float blurStrength = 1.42f;

    [Tooltip("블러를 계산할 해상도. 0=풀, 1=1/2, 2=1/4, 3=1/8. 깎을수록 싸고 더 흐려진다.")]
    [Range(0, 3)] public int downsampleShift = 1;

    [Tooltip("가로·세로 블러 반복 횟수. 1 = 한 번. 늘리면 더 흐려지고 여는 순간 비용만 는다(매 프레임 비용은 그대로 0).")]
    [Range(1, 4)] public int iterations = 1;

    [Tooltip("블러 위에 까는 창 어둡게 막의 알파. 블러가 없을 때(셰이더 비었음)는 프리팹 값(0.55)을 그대로 쓴다.")]
    [Range(0f, 1f)] public float dimAlpha = 0.25f;

    public bool CanBlur => blurShader != null && blurShader.isSupported;
}
