using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 제압 간파 렌즈 고스트(할 일 270) 저작 메뉴 — <c>LF_InterruptSuppressGhost.asset</c>(LensFlareDataSRP)을 만들고 제압 단계 SO(<c>InterruptHitLightLook_V3_Suppress</c>)
/// 의 빈 <c>ghostFlare</c> 칸에 잇는다.
/// <list type="bullet">
/// <item>상 5개 = 원(가장자리 무지개빛 방사 그라데이션) 2 + 육각형(조리개, sideCount 6) 3. 광원 = 위치 0, 화면 중심 = 1, 중심 너머 반대편 = 1 초과.</item>
/// <item>색 = 파랑·자주·초록 계열, 가산·낮은 세기(은은하게). 광원 쪽 빛은 넣지 않는다(스타버스트 <c>LF_InterruptHitFlashStarburst</c> 는 건드리지 않음).</item>
/// <item><b>없을 때만 만든다</b> — 이미 있는 에셋(손으로 튜닝한 값)·채워진 SO 칸은 그대로.</item>
/// </list>
/// 문서: Docs/tech/interrupt-presentation.md §9.11.
/// </summary>
public static class InterruptSuppressGhostAuthoring
{
    public const string GhostPath = "Assets/5.VFX/Interrupt/HitFlash/LF_InterruptSuppressGhost.asset";

    /// <summary>상 하나 — 위치(광원→중심 축, 0 = 광원·1 = 중심·2 = 반대편 거울 자리)·모양·크기·색·세기.</summary>
    private readonly struct Ghost
    {
        public readonly float Position;
        public readonly bool Hexagon;
        public readonly float Size;
        public readonly Color Tint;
        public readonly float Intensity;

        public Ghost(float position, bool hexagon, float size, Color tint, float intensity)
        {
            Position = position;
            Hexagon = hexagon;
            Size = size;
            Tint = tint;
            Intensity = intensity;
        }
    }

    // 광원 쪽(0)은 비우고 중심 앞뒤·반대편에 흩어 놓는다. 크기 = uniformScale(0.1 × 화면 비율 단위).
    private static readonly Ghost[] Ghosts =
    {
        new Ghost(0.55f, true, 0.55f, new Color(0.35f, 0.55f, 1f), 0.22f),  // 작은 파랑 육각
        new Ghost(0.9f, false, 1.1f, new Color(0.75f, 0.55f, 1f), 0.16f),   // 중심 앞 원(무지개 테두리)
        new Ghost(1.3f, true, 0.85f, new Color(0.7f, 0.35f, 1f), 0.18f),    // 자주 육각
        new Ghost(1.65f, false, 1.6f, new Color(0.55f, 0.85f, 1f), 0.12f),  // 반대편 큰 원(무지개 테두리)
        new Ghost(2.05f, true, 0.45f, new Color(0.4f, 1f, 0.6f), 0.2f),     // 맨 끝 작은 초록 육각
    };

    [MenuItem("Tools/Interrupt/제압 간파 렌즈 고스트 에셋 만들기 (270)")]
    public static void Build()
    {
        var ghost = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(GhostPath);
        if (ghost == null)
        {
            ghost = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            ghost.elements = CreateElements();
            AssetDatabase.CreateAsset(ghost, GhostPath);
            Debug.Log($"[InterruptGhost] 만듦: {GhostPath}");
        }

        var look = AssetDatabase.LoadAssetAtPath<InterruptHitLightLook>(InterruptHitLightTierAuthoring.SuppressLookPath);
        if (look == null)
        {
            Debug.LogError($"[InterruptGhost] 제압 단계 SO 가 없다: {InterruptHitLightTierAuthoring.SuppressLookPath}");
            return;
        }

        var so = new SerializedObject(look);
        SerializedProperty slot = so.FindProperty("ghostFlare");
        if (slot.objectReferenceValue == null)
        {
            slot.objectReferenceValue = ghost;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(look);
            Debug.Log("[InterruptGhost] 제압 단계 SO ghostFlare 칸 연결");
        }

        AssetDatabase.SaveAssets();
    }

    private static LensFlareDataElementSRP[] CreateElements()
    {
        var elements = new LensFlareDataElementSRP[Ghosts.Length];
        for (int i = 0; i < Ghosts.Length; i++)
        {
            Ghost g = Ghosts[i];
            var e = new LensFlareDataElementSRP
            {
                visible = true,
                position = g.Position,
                blendMode = SRPLensFlareBlendMode.Additive,
                uniformScale = g.Size,
                sizeXY = Vector2.one,
                preserveAspectRatio = true,
                tint = g.Tint,
                localIntensity = g.Intensity,
                modulateByLightColor = false,
                allowMultipleElement = false,
                count = 1,
            };

            if (g.Hexagon)
            {
                // 조리개 모양 — 살짝 둥근 육각형, 단색, 가장자리만 부드럽게.
                e.flareType = SRPLensFlareType.Polygon;
                e.sideCount = 6;
                e.sdfRoundness = 0.15f;
                e.tintColorType = SRPLensFlareColorType.Constant;
                e.fallOff = 0.75f;
                e.edgeOffset = 0.15f;
                e.rotation = 15f * i;
            }
            else
            {
                // 원 — 안쪽은 옅은 색, 가장자리에 파랑 → 자주 → 초록 무지개 띠(색수차 느낌).
                e.flareType = SRPLensFlareType.Circle;
                e.tintColorType = SRPLensFlareColorType.RadialGradient;
                e.tintGradient = new TextureGradient(RainbowRim(g.Tint));
                e.fallOff = 0.9f;
                e.edgeOffset = 0.05f;
            }

            elements[i] = e;
        }

        return elements;
    }

    // 방사 그라데이션(0 = 중심, 1 = 가장자리) — 안은 옅은 몸 색, 바깥 25% 에 파랑·자주·초록 띠.
    private static Gradient RainbowRim(Color body)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(body * 0.25f, 0f),
                new GradientColorKey(body * 0.35f, 0.62f),
                new GradientColorKey(new Color(0.3f, 0.5f, 1f), 0.76f),
                new GradientColorKey(new Color(0.85f, 0.35f, 1f), 0.86f),
                new GradientColorKey(new Color(0.35f, 1f, 0.55f), 0.95f),
                new GradientColorKey(Color.black, 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }
}
