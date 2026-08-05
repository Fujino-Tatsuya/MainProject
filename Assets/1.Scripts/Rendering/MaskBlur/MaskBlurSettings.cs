using UnityEngine;

// 화면공간 마스크 블러 튜닝값.
// 선명 영역은 화면 좌표로 고정한다 — 카메라가 플레이어를 따라가므로 결과적으로
// 플레이어를 따라가는 효과가 된다. 별도 추적 스크립트가 필요 없는 이유다.
[CreateAssetMenu(
    fileName = "MaskBlurSettings",
    menuName = "Rendering/Mask Blur Settings")]
public sealed class MaskBlurSettings : ScriptableObject
{
    [Header("On / Off")]
    [Tooltip("끄면 렌더 패스가 통째로 빠진다(비용 0).")]
    public bool enabled = true;

    [Header("선명 영역")]
    [Tooltip("중심(화면 UV). (0.5, 0.5) = 화면 정중앙.")]
    public Vector2 center = new Vector2(0.5f, 0.5f);

    [Tooltip("반경(화면 UV). x=가로, y=세로. 0.5 면 화면 절반까지 선명하다.")]
    public Vector2 size = new Vector2(0.30f, 0.34f);

    [Tooltip("켜면 size.x 를 화면 종횡비로부터 y 기준으로 유도한다 — 화면에서 정원(正圓)으로 보인다.\n" +
             "가로로 넓은 타원·사각형을 원하면 끄고 x 를 직접 준다.")]
    public bool matchAspect = true;

    [Tooltip("초타원 지수. 2 = 타원, 커질수록 사각형에 수렴한다(4~6 이 둥근 사각).")]
    [Range(2f, 16f)] public float roundness = 4f;

    [Tooltip("경계가 풀리는 폭. 작으면 테두리가 또렷해 인위적으로 보인다.")]
    [Range(0.01f, 1f)] public float feather = 0.35f;

    [Tooltip("절차 모양 대신 쓸 마스크 텍스처(R 채널, 흰색 = 선명). 비워두면 초타원을 쓴다.\n" +
             "머티리얼이 아니라 코드로만 참조되므로, 넣을 경우 빌드 스트립 여부를 확인할 것.")]
    public Texture2D maskTexture;

    [Header("블러")]
    [Tooltip("블러 세기. 텍셀 스텝에 곱해진다.")]
    [Range(0f, 4f)] public float blurStrength = 1f;

    [Tooltip("블러를 계산할 해상도. 0=풀, 1=1/2, 2=1/4.\n" +
             "배경 디포커스는 저주파라 1/2 이하로 깎아도 눈에 안 띄고 비용이 크게 준다.")]
    [Range(0, 2)] public int downsampleShift = 1;

    [Header("바깥 영역 톤 (선택)")]
    [Tooltip("블러 영역의 채도를 낮춘다. PLAN-vision §3 의 배경 톤다운이 이 자리다.\n" +
             "⚠️ 새 레퍼런스는 배경 채도가 살아 있는 그림이므로 기본 0 이다.")]
    [Range(0f, 1f)] public float desaturate = 0f;

    [Tooltip("블러 영역을 어둡게 한다. 비네트와 겹치므로 둘 중 하나만 쓰는 게 낫다.")]
    [Range(0f, 1f)] public float darken = 0f;

    private void OnValidate()
    {
        size.x = Mathf.Max(0.01f, size.x);
        size.y = Mathf.Max(0.01f, size.y);
    }

    // 실제 셰이더에 넘길 반경. matchAspect 면 화면 종횡비로 x 를 유도한다.
    //
    // 왜 CPU 에서 하는가: 셰이더에서 보정하면 마스크 텍스처를 쓸 때 UV 가 어긋난다.
    // 반경만 미리 맞춰 두면 절차 모양과 텍스처 모양이 같은 좌표계를 공유한다.
    public Vector2 ResolveSize(float pixelWidth, float pixelHeight)
    {
        if (!matchAspect || pixelWidth <= 0f || pixelHeight <= 0f)
            return size;

        return new Vector2(size.y * (pixelHeight / pixelWidth), size.y);
    }
}
