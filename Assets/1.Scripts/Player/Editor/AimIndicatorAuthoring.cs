using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// base Player.prefab 의 AimIndicator 를 URP 데칼에서 바닥에 띄운 얇은 쿼드로 바꾼다(할 일 33). 멱등.
/// AimIndicator 게임오브젝트·Transform 은 그대로 두고 컴포넌트만 갈아 끼워 fileID 를 유지한다(Variant 오버라이드 보존).
/// </summary>
public static class AimIndicatorAuthoring
{
    private const string PlayerPrefabPath = "Assets/2.Prefabs/Player/Player.prefab";
    private const string DecalMaterialPath = "Assets/3.Materials/Player/AimIndicator/AimIndicator.mat";
    private const string QuadMaterialPath = "Assets/3.Materials/Player/AimIndicator/AimIndicator3D.mat";
    private const string IndicatorName = "AimIndicator";

    [MenuItem("Tools/Player/Authoring/Aim Indicator 3D Quad")]
    public static void Wire()
    {
        Material material = EnsureQuadMaterial();
        if (material == null)
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[AimIndicatorAuthoring] 프리팹을 열 수 없습니다: {PlayerPrefabPath}");
            return;
        }

        try
        {
            Transform indicator = root.transform.Find(IndicatorName);
            if (indicator == null)
            {
                Debug.LogError($"[AimIndicatorAuthoring] {IndicatorName} 자식이 없습니다 — 새로 만들지 않는다(fileID 유지 목적).");
                return;
            }

            // 이 오브젝트의 데칼만 지운다. SkillRangeIndicator 등 다른 데칼은 건드리지 않는다.
            if (indicator.TryGetComponent(out DecalProjector decal))
                Object.DestroyImmediate(decal, true);
            if (indicator.TryGetComponent(out Collider collider))
                Object.DestroyImmediate(collider, true);

            if (!indicator.TryGetComponent(out MeshFilter filter))
                filter = indicator.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            if (!indicator.TryGetComponent(out MeshRenderer renderer))
                renderer = indicator.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;

            // 에디터 미리보기 자세 — 런타임엔 PlayerAimIndicator 가 매 프레임 월드 기준으로 다시 잡는다.
            indicator.localPosition = new Vector3(0f, 0.08f, 0.6f);
            indicator.localRotation = Quaternion.Euler(90f, 90f, 0f);
            indicator.localScale = new Vector3(1.2f, 1.2f, 1f);

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[AimIndicatorAuthoring] 완료 — Player.prefab/AimIndicator 데칼 → 쿼드(AimIndicator3D.mat).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 데칼 셰이더그래프의 화살표 텍스처 프로퍼티(Raw_Range).
    private const string DecalArrowProperty = "_Raw_Range";

    // 투명 URP Lit 머티리얼. 텍스처는 기존 데칼 머티리얼의 화살표를 그대로 쓴다.
    // 화살표가 번들거리지 않게 그림자 받기·스페큘러·환경 반사를 끄고 Smoothness 0.
    private static Material EnsureQuadMaterial()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogError("[AimIndicatorAuthoring] URP Lit 셰이더를 찾지 못했습니다.");
            return null;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(QuadMaterialPath);
        if (material == null)
        {
            material = new Material(lit) { name = "AimIndicator3D" };
            AssetDatabase.CreateAsset(material, QuadMaterialPath);
        }
        else if (material.shader != lit)
        {
            // Unlit·Lit 모두 _BaseMap/_BaseColor 라 셰이더만 바꿔도 텍스처는 유지된다.
            material.shader = lit;
        }

        // _BaseMap 이 비었을 때만 채운다 — 사용자가 갈아 넣은 텍스처를 재실행이 덮어쓰지 않게.
        if (material.GetTexture("_BaseMap") == null)
        {
            Material decalMaterial = AssetDatabase.LoadAssetAtPath<Material>(DecalMaterialPath);
            Texture arrow = decalMaterial != null && decalMaterial.HasProperty(DecalArrowProperty)
                ? decalMaterial.GetTexture(DecalArrowProperty)
                : null;
            if (arrow == null)
                Debug.LogWarning("[AimIndicatorAuthoring] 기존 데칼 머티리얼에서 화살표 텍스처를 못 찾았습니다 — _BaseMap 이 비어 있음.");
            material.SetTexture("_BaseMap", arrow);
        }
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Surface", 1f);  // Transparent
        material.SetFloat("_Blend", 0f);    // Alpha
        // 🔴 2026-10-13: Preserve Specular 를 켜 두면(Lit 기본값) 블렌드가 One/OneMinusSrcAlpha(프리멀티)가 되고
        //    스페큘러·환경광(반사 OFF 여도 _GlossyEnvironmentColor) 항이 알파에 곱해지지 않아 알파 0 영역까지
        //    희미하게 더해진다 → 쿼드 전체가 **밝은 네모**로 보였다. 끄면 SrcAlpha 블렌드로 전부 알파에 곱해진다.
        material.SetFloat("_BlendModePreserveSpecular", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_Cull", (float)CullMode.Back);
        material.SetFloat("_ReceiveShadows", 0f);
        material.SetFloat("_SpecularHighlights", 0f);
        material.SetFloat("_EnvironmentReflections", 0f);
        material.SetFloat("_Smoothness", 0f);
        material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_ZTest"))
            material.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
        // 인스펙터(LitShader.ValidateMaterial)와 같은 경로 — 공통 키워드 + Lit 전용 키워드.
        BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);

        // 키워드 설정이 채우는 값을 명시적으로 다시 확인 — ZWrite Off·Transparent 큐.
        material.SetFloat("_ZWrite", 0f);
        material.renderQueue = (int)RenderQueue.Transparent;

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }
}
