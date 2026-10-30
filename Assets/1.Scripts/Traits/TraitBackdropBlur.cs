using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 특성 창 배경 블러 그리기(할 일 165 후속 A안) — 화면 1회 캡처 → 축소 → 가로·세로 블러. 창이 열릴 때 한 번만 부르고 매 프레임에는 부르지 않는다.
/// 블러는 MaskBlur 셰이더의 0번(가로)·1번(세로) 패스를 그대로 쓴다(<see cref="MaskBlurFeature"/> 와 같은 스텝 = 세기 / 축소 폭).
/// 끝에 채도 패스(TraitBackdropSaturate, Rec.709 루마)를 1회 더 블릿한다 — 축소 RT 두 장을 맞바꿔 써서 RT 를 더 빌리지 않는다.
/// 결과는 <see cref="RenderTexture.GetTemporary(int,int)"/> — 쓴 쪽이 <see cref="Release"/> 로 반납한다. 화면 크기 캡처 텍스처는 여기서 바로 반납한다.
/// </summary>
public static class TraitBackdropBlur
{
    private const int HorizontalPass = 0; // MaskBlurDownH
    private const int VerticalPass = 1;   // MaskBlurV
    private const int SaturationPass = 0; // TraitBackdropSaturate
    private static readonly int IdStep = Shader.PropertyToID("_MaskBlurStep");
    private static readonly int IdSaturation = Shader.PropertyToID("_Saturation");

    private static Material material;
    private static Shader materialShader;
    private static Material saturationMaterial;
    private static Shader saturationMaterialShader;
    private static CommandBuffer commands;

    /// <summary>
    /// 지금 화면(월드 + Screen Space Overlay UI 전부)을 캡처해 블러한 텍스처를 준다. WaitForEndOfFrame 뒤에 불러야 한다. 실패하면 null.
    /// </summary>
    public static RenderTexture CaptureScreen(TraitBackdropBlurSettings settings)
    {
        if (settings == null || !settings.CanBlur || Screen.width <= 0 || Screen.height <= 0)
            return null;

        RenderTexture screen = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
        try
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(screen);
            // D3D·Metal 등(UV 원점이 위)에서는 캡처가 위아래 뒤집혀 들어온다 — 축소 복사 때 바로잡는다.
            return Blur(screen, SystemInfo.graphicsUVStartsAtTop, settings);
        }
        finally
        {
            RenderTexture.ReleaseTemporary(screen);
        }
    }

    /// <summary>
    /// source 를 축소(필요하면 상하 뒤집기)한 뒤 가로·세로 블러를 settings.iterations 번 돈 텍스처를 준다. source 는 건드리지 않는다.
    /// 저작 미리보기(임의 텍스처)도 같은 경로를 쓴다.
    /// </summary>
    public static RenderTexture Blur(Texture source, bool flipY, TraitBackdropBlurSettings settings)
    {
        if (source == null || settings == null || !settings.CanBlur)
            return null;

        Material blur = MaterialFor(settings.blurShader);
        int divisor = 1 << Mathf.Clamp(settings.downsampleShift, 0, 3);
        int width = Mathf.Max(1, source.width / divisor);
        int height = Mathf.Max(1, source.height / divisor);

        RenderTexture result = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        RenderTexture scratch = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        result.filterMode = scratch.filterMode = FilterMode.Bilinear;
        result.wrapMode = scratch.wrapMode = TextureWrapMode.Clamp;
        try
        {
            if (flipY)
                Graphics.Blit(source, result, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else
                Graphics.Blit(source, result);

            blur.SetVector(IdStep, new Vector4(settings.blurStrength / width, settings.blurStrength / height, 0f, 0f));
            commands ??= new CommandBuffer { name = "TraitBackdropBlur" };
            commands.Clear();
            for (int i = 0; i < Mathf.Max(1, settings.iterations); i++)
            {
                Blitter.BlitTexture(commands, result, scratch, blur, HorizontalPass);
                Blitter.BlitTexture(commands, scratch, result, blur, VerticalPass);
            }

            // 채도 패스(열 때 1회) — result → scratch 로 그리고 둘을 맞바꿔 RT 를 더 빌리지 않는다. 남는 쪽은 finally 가 반납.
            if (settings.ShouldSaturate)
            {
                Material saturate = SaturationMaterialFor(settings.saturationShader);
                saturate.SetFloat(IdSaturation, settings.saturation);
                Blitter.BlitTexture(commands, result, scratch, saturate, SaturationPass);
                (result, scratch) = (scratch, result);
            }

            Graphics.ExecuteCommandBuffer(commands);
            commands.Clear();
            return result;
        }
        catch
        {
            RenderTexture.ReleaseTemporary(result);
            throw;
        }
        finally
        {
            RenderTexture.ReleaseTemporary(scratch);
        }
    }

    /// <summary>블러 결과 반납(null 무시).</summary>
    public static void Release(RenderTexture texture)
    {
        if (texture != null)
            RenderTexture.ReleaseTemporary(texture);
    }

    // 머티리얼은 셰이더당 하나를 재사용한다(창을 열 때마다 만들지 않는다).
    private static Material MaterialFor(Shader shader)
    {
        if (material != null && materialShader == shader)
            return material;

        if (material != null)
            CoreUtils.Destroy(material);
        material = CoreUtils.CreateEngineMaterial(shader);
        materialShader = shader;
        return material;
    }

    private static Material SaturationMaterialFor(Shader shader)
    {
        if (saturationMaterial != null && saturationMaterialShader == shader)
            return saturationMaterial;

        if (saturationMaterial != null)
            CoreUtils.Destroy(saturationMaterial);
        saturationMaterial = CoreUtils.CreateEngineMaterial(shader);
        saturationMaterialShader = shader;
        return saturationMaterial;
    }
}
