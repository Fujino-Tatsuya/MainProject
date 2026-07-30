using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    // 셰이더 전역 유니폼 갱신. 벽별 상태도, 물리 쿼리도, MaterialPropertyBlock도 없다.
    // 불투명도는 셰이더가 프래그먼트 월드 좌표로 직접 계산한다.
    public static class WallOcclusionGlobals
    {
        public static readonly int PlayerPropertyId = Shader.PropertyToID("_WallOccPlayerWS");
        public static readonly int CameraPropertyId = Shader.PropertyToID("_WallOccCameraWS");
        public static readonly int RangePropertyId = Shader.PropertyToID("_WallOccRange");
        public static readonly int ShapePropertyId = Shader.PropertyToID("_WallOccShape");

        // w = 0 이면 셰이더가 페이드를 통째로 건너뛴다(전부 원래 불투명도).
        public static Vector4 DisabledRange => new(0f, 1f, 1f, 0f);

        // x=innerRadius, y=outerRadius, z=minimumOpacity, w=enable
        public static Vector4 BuildRange(WallOcclusionSettings settings, bool enabled)
        {
            if (settings == null)
                return DisabledRange;

            float inner = Mathf.Max(0f, settings.innerRadius);
            float outer = Mathf.Max(inner + 0.01f, settings.outerRadius);
            return new Vector4(
                inner,
                outer,
                Mathf.Clamp01(settings.minimumOpacity),
                enabled ? 1f : 0f);
        }

        // x=floorNormalThreshold, y=behindFalloff, z=floorGuardDepth
        public static Vector4 BuildShape(WallOcclusionSettings settings)
        {
            if (settings == null)
                return new Vector4(0.35f, 1.5f, 0.5f, 0f);

            return new Vector4(
                Mathf.Clamp(settings.floorNormalThreshold, 0f, 0.95f),
                Mathf.Max(0.01f, settings.behindFalloff),
                Mathf.Max(0.01f, settings.floorGuardDepth),
                0f);
        }

        public static void Apply(
            WallOcclusionSettings settings,
            Vector3 cameraPosition,
            Vector3 playerPosition)
        {
            Shader.SetGlobalVector(
                PlayerPropertyId,
                new Vector4(playerPosition.x, playerPosition.y, playerPosition.z, 0f));
            Shader.SetGlobalVector(
                CameraPropertyId,
                new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 0f));
            Shader.SetGlobalVector(RangePropertyId, BuildRange(settings, true));
            Shader.SetGlobalVector(ShapePropertyId, BuildShape(settings));
        }

        // 카메라나 플레이어가 없을 때 호출한다. 벽이 투명한 채로 남지 않게 한다.
        public static void Disable()
        {
            Shader.SetGlobalVector(RangePropertyId, DisabledRange);
        }
    }
}
