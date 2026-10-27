using UnityEngine;

/// <summary>
/// 로비 바닥 안내 화살표 띠(할 일 111) — 루트 위치에서 루트 앞(+Z)으로 <see cref="length"/> 만큼 깔리고 화살표 무늬가 앞으로 흐른다.
/// <list type="bullet">
/// <item>메시 = 내장 Quad(프리팹 참조), 셰이더 = Lobby/FloorArrow(흐름은 셰이더가 _Time 으로 민다 — 매 프레임 스크립트 없음).</item>
/// <item>크기 = 자식 <see cref="visual"/> 의 위치·스케일, 색·반복 수·흐름 속도·끝 페이드 = <see cref="MaterialPropertyBlock"/>(머티리얼 복제 없음).</item>
/// <item>에디터에서 값을 바꾸면 바로 보인다(OnValidate). 단계별 켜고 끄기는 같은 루트의 <see cref="LobbyTrainingStageVisibility"/>.</item>
/// </list>
/// 상세: Docs/tech/lobby-training.md "바닥 안내 화살표".
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class LobbyFloorArrowStrip : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int TilingId = Shader.PropertyToID("_Tiling");
    private static readonly int ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");
    private static readonly int EndFadeId = Shader.PropertyToID("_EndFade");

    [Tooltip("화살표 쿼드(Lobby/FloorArrow 머티리얼). 프리팹의 Visual 자식.")]
    [SerializeField] private MeshRenderer visual;

    [Header("크기 (m)")]
    [Tooltip("띠 길이 — 루트에서 앞(+Z)으로.")]
    [SerializeField, Min(0.1f)] private float length = 4f;

    [Tooltip("띠 폭(화살표 하나의 폭).")]
    [SerializeField, Min(0.05f)] private float width = 1f;

    [Tooltip("화살표 하나가 차지하는 길이(간격). 폭과 같으면 화살표 비율이 원본 그대로.")]
    [SerializeField, Min(0.05f)] private float arrowSpacing = 1f;

    [Tooltip("바닥에서 띄우는 높이 — 바닥과 겹쳐 깜빡이지 않게.")]
    [SerializeField, Min(0f)] private float heightOffset = 0.02f;

    [Header("모양")]
    [ColorUsage(true, true)]
    [SerializeField] private Color color = new Color(0.3f, 0.85f, 1f, 0.9f);

    [Tooltip("흐름 속도(m/초). 음수면 뒤로 흐른다.")]
    [SerializeField] private float flowSpeed = 1.5f;

    [Tooltip("양 끝을 지우는 구간(길이 비율 0~0.5).")]
    [SerializeField, Range(0f, 0.5f)] private float endFade = 0.15f;

    private MaterialPropertyBlock _block;

    /// <summary>길이 방향 화살표 반복 수(셰이더 _Tiling).</summary>
    public static float TilingFor(float length, float arrowSpacing)
    {
        return Mathf.Max(0.01f, length / Mathf.Max(0.05f, arrowSpacing));
    }

    /// <summary>m/초 흐름을 셰이더 단위(반복/초)로 — 간격이 바뀌어도 화살표가 같은 실제 속도로 움직인다.</summary>
    public static float ScrollTilesPerSecond(float flowSpeed, float arrowSpacing)
    {
        return flowSpeed / Mathf.Max(0.05f, arrowSpacing);
    }

    private void OnEnable() => Apply();

    private void OnValidate() => Apply();

    private void Apply()
    {
        if (visual == null)
            return;

        // 내장 Quad 는 XY 평면(-Z 면). (90, -90, 0) 으로 눕히면 면이 위(+Y), U(화살표 방향) = 루트 +Z, 폭 = 루트 X.
        var t = visual.transform;
        t.localRotation = Quaternion.Euler(90f, -90f, 0f);
        t.localPosition = new Vector3(0f, heightOffset, length * 0.5f);
        t.localScale = new Vector3(length, width, 1f);

        _block ??= new MaterialPropertyBlock();
        visual.GetPropertyBlock(_block);
        _block.SetColor(BaseColorId, color);
        _block.SetFloat(TilingId, TilingFor(length, arrowSpacing));
        _block.SetFloat(ScrollSpeedId, ScrollTilesPerSecond(flowSpeed, arrowSpacing));
        _block.SetFloat(EndFadeId, endFade);
        visual.SetPropertyBlock(_block);
    }
}
