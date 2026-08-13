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

    [Tooltip("선명 영역 반경. x=가로, y=세로.\n\n" +
             "matchAspect 가 켜져 있으면 화면 높이 기준 단위로 해석된다 — x 와 y 가 같으면 정원이고, " +
             "x 를 키우면 가로로 넓어진다(좌우를 넓히려면 여기 x 를 올린다).\n" +
             "꺼져 있으면 화면 UV 그대로다(16:9 에서 x=y 면 가로로 늘어난 타원이 된다).")]
    public Vector2 size = new Vector2(0.55f, 0.34f);

    [Tooltip("size 를 화면 높이 기준 단위로 해석해 종횡비를 보정한다.\n" +
             "끄면 size 가 화면 UV 그대로가 되어 해상도·종횡비가 바뀌면 모양이 변한다.")]
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

        // ⚠️ 처음에는 x 를 y 에서 유도해(size.y * 종횡비) 정원을 보장했는데, 그러면 인스펙터의
        //    size.x 가 조용히 무시된다. "값을 올렸는데 화면이 그대로"가 되는 대표적인 형태다.
        //    x 도 그대로 반영하고 종횡비 보정만 적용한다 — x == y 면 여전히 정원이고,
        //    x 를 키우면 가로로 넓어진다.
        return new Vector2(size.x * (pixelHeight / pixelWidth), size.y);
    }
}
