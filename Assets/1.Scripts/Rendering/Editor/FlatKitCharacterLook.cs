using System.Linq;
using UnityEditor;
using UnityEngine;

// Flat Kit 캐릭터 룩 표준값 (PLAN-flatkit.md · 2026-09-30).
//
// 캐릭터 머티리얼(Assets/3.Materials/FlatKit/ 아래, FlatKit/Stylized Surface)에 **같은 룩**을 한 번에 적용한다.
// 값은 여기 한 곳에서만 고친다 — 몹마다 인스펙터로 따로 만지면 룩이 조금씩 어긋난다.
//
// 🔴 토글(Rim·Outline)은 float 만 바꾸면 안 켜진다. 셰이더 키워드(DR_RIM_ON 등)를 같이 켜야 한다 —
//    YAML 로 _RimEnabled: 1 만 쓰면 인스펙터엔 켜져 보이는데 화면엔 안 나온다.
// 🔴 외곽선은 머티리얼만으로 안 그려진다. LightMode "Outline" 패스는 URP 가 기본으로 안 그리므로
//    Flat Kit 의 ObjectOutlineRendererFeature 가 활성 렌더러(PC_Renderer)에 있어야 한다.
//    Flat Kit 인스펙터가 토글 때 부르는 ObjectOutlineEditorUtils.SetActive 를 그대로 써서 등록한다.
public static class FlatKitCharacterLook
{
    const string CharacterMaterialsRoot = "Assets/3.Materials/FlatKit";

    // ── 셀 음영 ─────────────────────────────────────────────
    // 그림자 쪽 색. 09-30 팀장: 기본 상태를 유지하고 명암은 빛 연산으로만 — 0.52 로 내린 안은 과했다.
    static readonly Color ShadedColor = new Color(0.6f, 0.6f, 0.6f, 1f);   // 09-30 팀장 확정(0.85 기본은 차이가 안 보였다)
    const float SelfShadingSize = 0.5f;
    const float ShadowEdgeSize = 0.05f;

    // ── 조명 반영 ───────────────────────────────────────────
    // 0 = 조명 색·세기 무시(흰 빛), 1 = 그대로. 맵에 IBL 이 들어올 예정이라 조명을 그대로 받는다(09-30 팀장).
    const float LightContribution = 1f;
    // 🔴 Point/Spot 거리 감쇠. Flat Kit 기본 0 = 계단(범위 안이면 거리 무관 최대 세기) → 보스방 포인트 라이트에서
    //    몸 전체가 하얗게 떴다(09-30). 1 = URP 와 같은 부드러운 감쇠.
    const float LightFalloffSize = 1f;

    // ── 림 ─────────────────────────────────────────────────
    // 09-30 팀장: 림은 켜 두되 최소로(0.55·0.35 안은 몸 전체가 하얗게 떴다). 세기는 a 로 올린다.
    static readonly Color RimColor = new Color(1f, 0.97f, 0.9f, 0.23f);   // a = 섞는 세기 (09-30 팀장 0.23)
    const float RimSize = 0.12f;
    const float RimEdgeSmoothness = 0.15f;
    const float RimLightAlign = 0.3f;

    // ── 외곽선 ─────────────────────────────────────────────
    static readonly Color OutlineColor = new Color(0.06f, 0.05f, 0.05f, 1f);
    const float OutlineWidth = 0.4f;   // 09-30 팀장(1920x1080 기준): 폭 = Width × 2.7px → 0.4 = 1.08px, 끊기지 않는 최소 1px. 0.25(0.68px)는 끊김, 0.5(1.35px)는 진함. 가시는 이 두께에서 안 보여 Smooth Normals 보류

    // 플레이어 전용(3.Materials/FlatKit/Player/). 09-30 팀장: 몬스터는 0.4 가 맞는데 플레이어만 가시가 보인다 —
    // Paladin 은 AI 생성(tripo) 메시라 법선이 고르지 않아 같은 두께에서도 밀어낸 뒷면이 삐져나온다. 두께를 따로 잡는다.
    const float PlayerOutlineWidth = 0.4f;   // 09-30 팀장: Smooth Normals 로 가시 제거 후 몬스터와 같은 0.4(1.08px @1080p)

    // 플레이어는 부드러운 법선으로 가시를 없앤다 — 외곽선 패스만 바꾼 복제 셰이더(스킨드 대응) + 임포트 굽기
    // (FlatKitSmoothNormalBaker). 몬스터는 0.4 에서 가시가 안 보여 원본 셰이더 그대로.
    public const string SkinnedOutlineShader = "Project/FlatKit Stylized Surface (Skinned Outline)";

    // 🔴 Flat Kit 데모 씬의 AutoLoadPipelineAsset 은 씬을 열면 Graphics·Quality 의 URP 에셋을 데모용으로 바꾸고
    //    닫을 때 되돌리는데, 되돌릴 값을 직렬화하지 않아 **도메인 리로드 한 번이면 복구 정보가 사라진다**.
    //    그러면 프로젝트가 데모 에셋에 묶인 채로 남는다(2026-09-30 실측 — GraphicsSettings·QualitySettings 둘 다).
    //    데모 폴더는 git 제외라 그대로 커밋되면 다른 사람 화면은 분홍이 된다.
    const string ProjectPipelinePath = "Assets/99.Settings/PC_RPAsset.asset";

    [MenuItem("Tools/Rendering/Flat Kit/Restore Project Pipeline (PC_RPAsset)")]
    public static void RestoreProjectPipeline()
    {
        var pc = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(ProjectPipelinePath);
        if (pc == null) { Debug.LogError($"[FlatKitLook] {ProjectPipelinePath} 없음"); return; }

        UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pc;
        // Quality 레벨마다 따로 들고 있다. Mobile 은 원래 Mobile_RPAsset 이라 데모 에셋으로 바뀐 레벨만 되돌린다.
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            var rp = QualitySettings.GetRenderPipelineAssetAt(i);
            if (rp != null && AssetDatabase.GetAssetPath(rp).StartsWith("Assets/FlatKit/"))
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pc;
            }
        }
        QualitySettings.SetQualityLevel(current, false);
        AssetDatabase.SaveAssets();
        Debug.Log($"[FlatKitLook] 파이프라인 복구 — Graphics · Quality → {pc.name}");
    }

    [MenuItem("Tools/Rendering/Flat Kit/Apply Character Look (3.Materials/FlatKit)")]
    public static void ApplyAll()
    {
        var active = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        if (active == null || AssetDatabase.GetAssetPath(active) != ProjectPipelinePath)
        {
            // 데모 에셋이 활성인 채로 돌리면 외곽선 피처가 **데모 렌더러**에 붙는다.
            Debug.LogError($"[FlatKitLook] 활성 URP 에셋이 {AssetDatabase.GetAssetPath(active)} 다 — 먼저 Restore Project Pipeline.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { CharacterMaterialsRoot });
        int applied = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Water/")) continue;   // 물은 캐릭터 룩이 아니다
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null || (m.shader.name != "FlatKit/Stylized Surface" && m.shader.name != SkinnedOutlineShader)) continue;
            // 🔴 Material Variant 는 건너뛴다 — 룩 값은 부모(FK_Paladin_Toon)에서 물려받는다. 여기서 값을 쓰면 전부 재정의로
            //    굳어 부모를 고쳐도 안 따라온다(플레이어 Variant 구조, 팀장 10-02).
            if (m.parent != null) continue;
            Apply(m);
            applied++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[FlatKitLook] 캐릭터 룩 적용 {applied}개 ({CharacterMaterialsRoot})");
    }

    public static void Apply(Material m)
    {
        // MCP 로 만든 머티리얼은 _BaseColor 가 (0,0,0,0) 으로 들어온다 — 텍스처가 있어도 새까맣게 나온다.
        if (m.GetColor("_BaseColor").maxColorComponent <= 0f) m.SetColor("_BaseColor", Color.white);

        m.SetFloat("_CelPrimaryMode", 1f);
        SetKeywordEnum(m, "_CELPRIMARYMODE", "SINGLE", "NONE", "SINGLE", "STEPS", "CURVE");
        m.SetColor("_ColorDim", ShadedColor);
        m.SetFloat("_SelfShadingSize", SelfShadingSize);
        m.SetFloat("_ShadowEdgeSize", ShadowEdgeSize);

        m.SetFloat("_LightContribution", LightContribution);
        m.SetFloat("_LightFalloffSize", LightFalloffSize);

        m.SetFloat("_RimEnabled", 1f);
        m.EnableKeyword("DR_RIM_ON");
        m.SetColor("_FlatRimColor", RimColor);
        m.SetFloat("_FlatRimSize", RimSize);
        m.SetFloat("_FlatRimEdgeSmoothness", RimEdgeSmoothness);
        m.SetFloat("_FlatRimLightAlign", RimLightAlign);

        // 투명 부위(안테나·날개 잔상·보호유리)는 외곽선을 끈다 — 반투명 판에 검은 테두리가 선다.
        if (m.GetFloat("_Surface") > 0.5f)
        {
            m.SetFloat("_OutlineEnabled", 0f);
            m.DisableKeyword("DR_OUTLINE_ON");
            ObjectOutlineEditorUtils.SetActive(m, false);
            EditorUtility.SetDirty(m);
            return;
        }

        m.SetFloat("_OutlineEnabled", 1f);
        m.EnableKeyword("DR_OUTLINE_ON");
        m.SetColor("_OutlineColor", OutlineColor);
        bool isPlayer = AssetDatabase.GetAssetPath(m).Contains("/FlatKit/Player/");
        if (isPlayer)
        {
            Shader skinned = Shader.Find(SkinnedOutlineShader);
            if (skinned != null && m.shader != skinned) m.shader = skinned;   // 같은 이름의 프로퍼티·키워드는 유지된다
            m.SetFloat("_VertexExtrusionSmoothNormals", 1f);
            m.EnableKeyword("DR_OUTLINE_SMOOTH_NORMALS");
        }
        m.SetFloat("_OutlineWidth", isPlayer ? PlayerOutlineWidth : OutlineWidth);
        m.SetFloat("_OutlineSpace", 0f);
        SetKeywordEnum(m, "_OUTLINESPACE", "SCREEN", "SCREEN", "OBJECT");
        ObjectOutlineEditorUtils.SetActive(m, true);   // PC_Renderer 에 외곽선 피처 등록(없으면 생성)

        EditorUtility.SetDirty(m);
    }

    static void SetKeywordEnum(Material m, string prefix, string on, params string[] all)
    {
        foreach (string k in all.Select(a => $"{prefix}_{a}"))
            if (k == $"{prefix}_{on}") m.EnableKeyword(k); else m.DisableKeyword(k);
    }
}
