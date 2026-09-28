using UnityEngine;

/// <summary>
/// 런타임에 흔들 수 있는 Cyanilux CRT 머티리얼 프로퍼티.
/// 값은 <see cref="RetroCRTController"/> 에 오버라이드로 올리고, Renderer Feature 가 MPB 로 밀어 넣는다.
/// </summary>
/// <remarks>
/// 🔴 여기 적힌 참조 이름은 셰이더 그래프에서 <b>직접 지정한 것만</b> 쓴다.
/// 머티리얼에는 <c>Vector1_1BFF6B46</c> 처럼 자동 생성된 이름도 섞여 있는데,
/// 그건 그래프를 다시 저장하면 조용히 바뀐다. 절대 하드코딩하지 않는다.
/// </remarks>
public enum CrtParam
{
    /// <summary>검은 CRT 베젤 크기. 0=없음, 1=기본.</summary>
    BezelSize = 0,

    /// <summary>화면 곡률. 머티리얼 저작값 0.035. 스칼라를 rg 채널에 함께 넣는다.</summary>
    WarpStrength = 1,

    /// <summary>이미지 왜곡. 키워드 <c>DISTORT</c> 가 꺼져 있으면 보이지 않는다.</summary>
    DistortionStrength = 2,

    /// <summary>어퍼처 그릴(RGB 스트라이프) 세기. 머티리얼 저작값 0.15.</summary>
    RgbStripeStrength = 3,

    /// <summary>스캔라인 한 줄 높이. 머티리얼 저작값 2.</summary>
    ScanlineHeight = 4,

    /// <summary>스캔라인 세기. 머티리얼 저작값 0.08.</summary>
    ScanlineStrength = 5,

    /// <summary>정적 노이즈 세기. 🔴 키워드 <c>STATIC</c> 이 꺼져 있으면 보이지 않는다.</summary>
    StaticStrength = 6,
}

/// <summary>
/// <see cref="CrtParam"/> 의 셰이더 참조 이름과 개수. 프로퍼티 ID 는 여기서만 만든다.
/// </summary>
public static class CrtParamInfo
{
    public const int Count = 7;

    /// <summary>스칼라가 아니라 Vector4 로 들어가는 프로퍼티(rg 채널에 같은 값을 넣는다).</summary>
    public static bool IsVector(CrtParam param) => param == CrtParam.WarpStrength;

    private static readonly string[] Names =
    {
        "_BezelSize",
        "_CRTWarpStrength",
        "_DistortionStrength",
        "_RGBStripeStrength",
        "_ScanlineHeight",
        "_ScanlineStrength",
        "_StaticStrength",
    };

    private static readonly int[] Ids = BuildIds();

    public static string NameOf(CrtParam param) => Names[(int)param];

    public static int IdOf(CrtParam param) => Ids[(int)param];

    private static int[] BuildIds()
    {
        var ids = new int[Names.Length];
        for (var i = 0; i < Names.Length; i++)
            ids[i] = Shader.PropertyToID(Names[i]);

        return ids;
    }

    /// <summary>
    /// 머티리얼이 이 프로퍼티들을 실제로 갖고 있는지 확인한다.
    /// 없는 이름에 MPB 로 값을 밀면 <b>예외 없이 조용히 무시</b>되므로 여기서 한 번 경고를 남긴다.
    /// </summary>
    public static void WarnMissingProperties(Material material, Object context)
    {
        if (material == null)
            return;

        for (var i = 0; i < Names.Length; i++)
        {
            if (!material.HasProperty(Ids[i]))
            {
                Debug.LogWarning(
                    $"[RetroCRT] 머티리얼 '{material.name}' 에 '{Names[i]}' 가 없다. " +
                    "그래프 재저장으로 참조 이름이 바뀌었을 수 있다. 이 파라미터는 무시된다.",
                    context);
            }
        }
    }
}
