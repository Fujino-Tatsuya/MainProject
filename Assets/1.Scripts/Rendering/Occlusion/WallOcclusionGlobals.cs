using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    public static class WallOcclusionGlobals
    {
        public static readonly int CapsuleAPropertyId = Shader.PropertyToID("_WallOccCapsuleA");
        public static readonly int CapsuleBPropertyId = Shader.PropertyToID("_WallOccCapsuleB");
        public static readonly int MaskPropertyId = Shader.PropertyToID("_WallOccMask");
        public static readonly int CameraPropertyId = Shader.PropertyToID("_WallOccCameraWS");
        public static readonly int CameraForwardPropertyId = Shader.PropertyToID("_WallOccCameraForwardWS");
        public static readonly int DepthPropertyId = Shader.PropertyToID("_WallOccDepth");
        public static readonly int ViewProjectionPropertyId = Shader.PropertyToID("_WallOccViewProjection");
        public static readonly int ScreenRectPropertyId = Shader.PropertyToID("_WallOccScreenRect");
        public static readonly int StrengthPropertyId = Shader.PropertyToID("_WallOcclusionStrength");

        public static Vector4 BuildMask(float coreRadiusPixels, float featherPixels, bool enabled)
        {
            return new Vector4(
                Mathf.Max(0f, coreRadiusPixels),
                Mathf.Max(1f, featherPixels),
                enabled ? 1f : 0f,
                0f);
        }

        public static void ApplyScreenCapsule(
            Vector2 endpointA,
            Vector2 endpointB,
            float coreRadiusPixels,
            float featherPixels,
            Camera camera,
            float targetViewDepth,
            float behindFalloff)
        {
            Shader.SetGlobalVector(CapsuleAPropertyId, new Vector4(endpointA.x, endpointA.y, 0f, 0f));
            Shader.SetGlobalVector(CapsuleBPropertyId, new Vector4(endpointB.x, endpointB.y, 0f, 0f));
            Shader.SetGlobalVector(MaskPropertyId, BuildMask(coreRadiusPixels, featherPixels, true));

            Vector3 cameraPosition = camera.transform.position;
            Vector3 cameraForward = camera.transform.forward;
            Shader.SetGlobalVector(
                CameraPropertyId,
                new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 0f));
            Shader.SetGlobalVector(
                CameraForwardPropertyId,
                new Vector4(cameraForward.x, cameraForward.y, cameraForward.z, 0f));
            Shader.SetGlobalVector(
                DepthPropertyId,
                new Vector4(
                    Mathf.Max(0f, targetViewDepth),
                    Mathf.Max(0.01f, behindFalloff),
                    0f,
                    0f));
            Shader.SetGlobalMatrix(
                ViewProjectionPropertyId,
                camera.projectionMatrix * camera.worldToCameraMatrix);
            Rect pixelRect = camera.pixelRect;
            Shader.SetGlobalVector(
                ScreenRectPropertyId,
                new Vector4(pixelRect.x, pixelRect.y, pixelRect.width, pixelRect.height));
        }

        public static void Disable()
        {
            Shader.SetGlobalVector(MaskPropertyId, BuildMask(0f, 1f, false));
        }
    }
}
