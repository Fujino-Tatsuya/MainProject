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
        public static readonly int DitherPropertyId = Shader.PropertyToID("_WallOccDither");

        // 디더를 프레임마다 흔들기 위한 오프셋.
        //
        // 왜 필요한가: 셰이더는 clip() 한 번으로 반투명을 흉내내므로 픽셀당 값이 0 아니면 1이다.
        // 패턴이 프레임마다 고정이면 항상 같은 픽셀만 살아남아 검은 점으로 굳어 보인다.
        // 매 프레임 오프셋을 주면 살아남는 픽셀이 바뀌고, TAA 가 그걸 누적해 매끈한
        // 반투명으로 녹인다. (TAA 없이 이것만 켜면 지글거리기만 하므로 둘은 세트다.)
        //
        // 5.588238 은 interleaved gradient noise 의 표준 시간축 상수다.
        // 프레임 인덱스를 그대로 곱하면 장시간 플레이에서 float 정밀도가 무너지므로 64로 접는다.
        private const float DitherFrameStride = 5.588238f;
        private const int DitherFrameCycle = 64;

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
            Shader.SetGlobalVector(DitherPropertyId, BuildDither(Time.frameCount));
        }

        // x = 이번 프레임의 디더 오프셋. 셰이더가 픽셀 좌표에 더해서 쓴다.
        public static Vector4 BuildDither(int frameCount)
        {
            float offset = (frameCount % DitherFrameCycle) * DitherFrameStride;
            return new Vector4(offset, 0f, 0f, 0f);
        }

        // 카메라나 플레이어가 없을 때 호출한다. 벽이 투명한 채로 남지 않게 한다.
        public static void Disable()
        {
            Shader.SetGlobalVector(RangePropertyId, DisabledRange);
        }
    }
}
