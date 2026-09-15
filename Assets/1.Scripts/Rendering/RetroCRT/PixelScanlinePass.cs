using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// 기존 픽셀레이트와 가로 스캔라인을 한 번의 블릿으로 처리한다.
internal sealed class PixelScanlinePass : ScriptableRenderPass
{
    private const string PassName = "PixelScanline FullScreen";
    private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);
    private static readonly int IdResolution = Shader.PropertyToID("_PixelScanlineResolution");
    private static readonly int IdPixel = Shader.PropertyToID("_PixelScanlinePixel");
    private static readonly int IdPattern = Shader.PropertyToID("_PixelScanlinePattern");
    private static readonly int IdColor = Shader.PropertyToID("_PixelScanlineColor");

    private Material _material;
    private RetroCRTController _controller;

    private sealed class PassData
    {
        public Material material;
        public TextureHandle source;
        public Vector4 resolution;
        public Vector4 pixel;
        public Vector4 pattern;
        public Color color;
    }

    public PixelScanlinePass()
    {
        profilingSampler = new ProfilingSampler(PassName);
        requiresIntermediateTexture = true;
    }

    public void Setup(Material material, RetroCRTController controller)
    {
        _material = material;
        _controller = controller;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_material == null || _controller == null)
            return;

        UniversalResourceData resources = frameData.Get<UniversalResourceData>();
        if (resources.isActiveTargetBackBuffer)
        {
            Debug.LogError("[RetroCRT] 카메라 컬러가 BackBuffer여서 픽셀레이트·스캔라인을 적용할 수 없다.");
            return;
        }

        TextureHandle source = resources.activeColorTexture;
        if (!source.IsValid())
            return;

        RenderTextureDescriptor cameraDescriptor = frameData.Get<UniversalCameraData>().cameraTargetDescriptor;
        int width = Mathf.Max(1, cameraDescriptor.width);
        int height = Mathf.Max(1, cameraDescriptor.height);

        TextureDesc descriptor = renderGraph.GetTextureDesc(source);
        descriptor.name = "_CameraColorPixelScanline";
        descriptor.clearBuffer = false;
        TextureHandle destination = renderGraph.CreateTexture(descriptor);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(PassName, out PassData data))
        {
            data.material = _material;
            data.source = source;
            data.resolution = new Vector4(width, height, 1f / width, 1f / height);
            data.pixel = _controller.PixelParameters;
            data.pattern = _controller.ScanlineParameters;
            data.color = _controller.ScanlineColor;

            builder.UseTexture(source, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
            builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
            {
                // 다른 카메라의 설정과 섞이지 않도록 기록해 둔 값을 실행 직전에 전달한다.
                d.material.SetVector(IdResolution, d.resolution);
                d.material.SetVector(IdPixel, d.pixel);
                d.material.SetVector(IdPattern, d.pattern);
                d.material.SetColor(IdColor, d.color);
                Blitter.BlitTexture(context.cmd, d.source, FullScreenScaleBias, d.material, 0);
            });
        }

        resources.cameraColor = destination;
    }
}
