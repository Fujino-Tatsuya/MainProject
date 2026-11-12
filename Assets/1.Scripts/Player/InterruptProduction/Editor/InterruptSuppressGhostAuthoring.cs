using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 제압 간파 렌즈 고스트(할 일 270) 저작 메뉴 — <c>LF_InterruptSuppressGhost.asset</c>(LensFlareDataSRP)을 만들고 제압 단계 SO(<c>InterruptHitLightLook_V3_Suppress</c>)
/// 의 빈 <c>ghostFlare</c> 칸에 잇는다.
/// <list type="bullet">
/// <item>상 11개 = 부드러운 원(보케 느낌). 광원 = 위치 0, 화면 중심 = 1, 중심 너머 반대편 = 1 초과. 직선에서 살짝씩 비껴(positionOffset) 너무 반듯하지 않게.</item>
/// <item>크기 섞기 — 작고 밝은 점(노랑·흰·초록) + 중간 주황 원 + 크고 아주 옅은 원판. 색 = 따뜻한 앰버·주황·연노랑 기조에 초록·청록·옅은 보라 포인트.</item>
/// <item>가산·낮은 세기. 광원 쪽 빛은 넣지 않는다(스타버스트 <c>LF_InterruptHitFlashStarburst</c> 는 건드리지 않음).</item>
/// <item><b>만들기</b> = 없을 때만. <b>다시 만들기</b> = 기존 에셋의 상 목록을 이 기본값으로 덮어씀(guid 유지).</item>
/// </list>
/// 문서: Docs/tech/interrupt-presentation.md §9.11.
/// </summary>
public static class InterruptSuppressGhostAuthoring
{
    public const string GhostPath = "Assets/5.VFX/Interrupt/HitFlash/LF_InterruptSuppressGhost.asset";

    /// <summary>상 하나 — 위치(광원→중심 축)·옆 비낌(화면)·크기·색·세기·부드러움(낮을수록 퍼진 빛).</summary>
    private readonly struct Ghost
    {
        public readonly float Position;
        public readonly Vector2 Offset;
        public readonly float Size;
        public readonly Color Tint;
        public readonly float Intensity;
        public readonly float FallOff;

        public Ghost(float position, float offsetX, float offsetY, float size, Color tint, float intensity, float fallOff)
        {
            Position = position;
            Offset = new Vector2(offsetX, offsetY);
            Size = size;
            Tint = tint;
            Intensity = intensity;
            FallOff = fallOff;
        }
    }

    // 크기 = uniformScale. 작은 점 = fallOff 낮게(퍼진 빛), 큰 원판 = fallOff 높게(테두리만 부드러운 판).
    private static readonly Ghost[] Ghosts =
    {
        new Ghost(0.35f, 0.00f, 0.02f, 2.4f, new Color(1f, 0.85f, 0.6f), 0.05f, 0.7f),     // 큰 옅은 원판
        new Ghost(0.50f, 0.01f, -0.01f, 1.0f, new Color(1f, 0.95f, 0.8f), 0.08f, 0.6f),    // 연한 크림 원
        new Ghost(0.75f, -0.01f, 0.00f, 0.35f, new Color(1f, 0.85f, 0.4f), 0.35f, 0.3f),   // 작고 밝은 노랑 점
        new Ghost(0.90f, 0.01f, 0.01f, 0.7f, new Color(1f, 0.6f, 0.25f), 0.18f, 0.5f),     // 주황 원
        new Ghost(1.05f, 0.00f, -0.01f, 0.22f, new Color(1f, 1f, 0.8f), 0.4f, 0.25f),      // 아주 작은 흰노랑 점
        new Ghost(1.20f, -0.02f, 0.01f, 1.3f, new Color(1f, 0.55f, 0.2f), 0.1f, 0.65f),    // 큰 주황 원판
        new Ghost(1.35f, 0.01f, 0.02f, 0.25f, new Color(0.45f, 1f, 0.35f), 0.3f, 0.3f),    // 작은 초록 점
        new Ghost(1.55f, 0.02f, -0.01f, 0.9f, new Color(1f, 0.9f, 0.55f), 0.12f, 0.55f),   // 연노랑 원
        new Ghost(1.80f, -0.01f, 0.00f, 2.0f, new Color(0.9f, 0.7f, 0.45f), 0.06f, 0.7f),  // 큰 옅은 원판
        new Ghost(2.10f, 0.01f, 0.01f, 0.5f, new Color(0.4f, 0.9f, 0.6f), 0.12f, 0.45f),   // 청록 원
        new Ghost(2.35f, 0.00f, -0.02f, 1.4f, new Color(0.7f, 0.5f, 0.9f), 0.05f, 0.7f),   // 맨 끝 옅은 보라 원판
    };

    public static int GhostCount => Ghosts.Length;

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

        LinkSuppressLook(ghost);
        AssetDatabase.SaveAssets();
    }

    /// <summary>기존 에셋의 상 목록을 기본값으로 덮어쓴다(guid 유지 — 손으로 튜닝한 값은 사라진다).</summary>
    [MenuItem("Tools/Interrupt/제압 간파 렌즈 고스트 다시 만들기 — 덮어씀 (270)")]
    public static void Rebuild()
    {
        var ghost = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(GhostPath);
        if (ghost == null)
        {
            Build();
            return;
        }

        Undo.RecordObject(ghost, "제압 간파 렌즈 고스트 다시 만들기");
        ghost.elements = CreateElements();
        EditorUtility.SetDirty(ghost);
        LinkSuppressLook(ghost);
        AssetDatabase.SaveAssets();
        Debug.Log($"[InterruptGhost] 다시 만듦(상 {Ghosts.Length}개): {GhostPath}");
    }

    private static void LinkSuppressLook(LensFlareDataSRP ghost)
    {
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
    }

    private static LensFlareDataElementSRP[] CreateElements()
    {
        var elements = new LensFlareDataElementSRP[Ghosts.Length];
        for (int i = 0; i < Ghosts.Length; i++)
        {
            Ghost g = Ghosts[i];
            elements[i] = new LensFlareDataElementSRP
            {
                visible = true,
                position = g.Position,
                positionOffset = g.Offset,
                blendMode = SRPLensFlareBlendMode.Additive,
                flareType = SRPLensFlareType.Circle,
                tintColorType = SRPLensFlareColorType.Constant,
                uniformScale = g.Size,
                sizeXY = Vector2.one,
                preserveAspectRatio = true,
                tint = g.Tint,
                localIntensity = g.Intensity,
                fallOff = g.FallOff,
                edgeOffset = 0.1f,
                modulateByLightColor = false,
                allowMultipleElement = false,
                count = 1,
            };
        }

        return elements;
    }
}
