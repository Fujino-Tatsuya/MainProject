// ----------------------------------------------------------------------------
//  아웃라인 전용 — Flat Kit 외곽선 패스 하나만 가진 셰이더 (PLAN-boss-1012.md §A · 2026-10-12)
//
//  용도: 재질을 바꿀 수 없는 오브젝트(맵 공용 아틀라스를 쓰는 송전기 등)에 **같은 메시의 자식 렌더러**로
//        얹어 테두리만 그린다. 본체 렌더·그림자·깊이 패스가 없으므로 PC_Renderer 의
//        「Flat Kit Per Object Outline」 피처(LightMode = Outline)가 그릴 때만 보인다.
//  패스 원본: Project/FlatKit Stylized Surface (Skinned Outline) 의 Outline 패스(부드러운 법선 복원 포함).
//  속성은 그 패스가 읽는 것만 둔다 — 나머지 StylizedInput 상수는 0 으로 남아도 외곽선과 무관하다.
// ----------------------------------------------------------------------------
Shader "Project/Outline Only"
{
    Properties
    {
        [Toggle(DR_OUTLINE_ON)] _OutlineEnabled("Enable Outline", Int) = 1
        [Toggle(DR_OUTLINE_SMOOTH_NORMALS)] _OutlineSmoothNormals("Smooth Normals (UV3)", Int) = 1
        [KeywordEnum(Screen, Object)] _OutlineSpace("Space", Int) = 0
        _OutlineWidth("Width", Float) = 1.0
        _OutlineColor("Color", Color) = (1, 0.85, 0.1, 1)
        _OutlineScale("Scale", Float) = 1.0
        _OutlineDepthOffset("Depth Offset", Range(0, 1)) = 0.0
        _CameraDistanceImpact("Camera Distance Impact", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        UsePass "Project/FlatKit Stylized Surface (Skinned Outline)/OUTLINE"
    }

    Fallback Off
}
