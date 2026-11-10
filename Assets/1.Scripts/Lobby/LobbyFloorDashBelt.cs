using UnityEngine;

/// <summary>
/// 로비 바닥 대시 벨트(할 일 257) — 루트 위치에서 루트 앞(+Z)으로 <see cref="length"/> 만큼 띠를 깔고,
/// 그 위에 대시 아이콘(»)을 <see cref="iconSpacing"/> 간격으로 반복해 맥동하며 아주 느리게 앞으로 흘린다.
/// <list type="bullet">
/// <item>메시 = 내장 Quad(프리팹 참조), 셰이더 = Lobby/FloorDashBelt(흐름·맥동은 셰이더가 _Time 으로 — 매 프레임 스크립트 없음).</item>
/// <item>크기 = 자식 <see cref="visual"/> 의 위치·스케일, 나머지 수치 = <see cref="MaterialPropertyBlock"/>(머티리얼 복제 없음).</item>
/// <item>길이를 늘리면 아이콘이 늘어나지 않고 개수가 는다(칸 수 = 길이 / 간격, 아이콘 크기는 m 단위).</item>
/// <item>에디터에서 값을 바꾸면 바로 보인다(OnValidate). 늘 켜져 있다 — 배치·튜토리얼 연결은 아직 없음.</item>
/// </list>
/// 상세: Docs/tech/lobby-training.md "대시 벨트".
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class LobbyFloorDashBelt : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int BeltColorId = Shader.PropertyToID("_BeltColor");
    private static readonly int TilingId = Shader.PropertyToID("_Tiling");
    private static readonly int IconScaleUvId = Shader.PropertyToID("_IconScaleUV");
    private static readonly int IconRotationId = Shader.PropertyToID("_IconRotation");
    private static readonly int ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");
    private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
    private static readonly int PulsePhaseId = Shader.PropertyToID("_PulsePhase");
    private static readonly int PulseBrightnessId = Shader.PropertyToID("_PulseBrightness");
    private static readonly int PulseScaleId = Shader.PropertyToID("_PulseScale");
    private static readonly int EndFadeId = Shader.PropertyToID("_EndFade");

    [Tooltip("벨트 쿼드(Lobby/FloorDashBelt 머티리얼). 프리팹의 Visual 자식.")]
    [SerializeField] private MeshRenderer visual;

    [Header("크기 (m)")]
    [Tooltip("띠 길이 — 루트에서 앞(+Z)으로. 늘리면 아이콘 개수가 는다.")]
    [SerializeField, Min(0.1f)] private float length = 6f;

    [Tooltip("띠 폭.")]
    [SerializeField, Min(0.05f)] private float width = 1f;

    [Tooltip("아이콘 하나가 차지하는 길이(아이콘 중심 간격).")]
    [SerializeField, Min(0.05f)] private float iconSpacing = 1f;

    [Tooltip("아이콘 크기(정사각, m). 원본 그림 둘레 여백 포함. 폭·간격보다 크면 잘린다.")]
    [SerializeField, Min(0.01f)] private float iconSize = 1f;

    [Tooltip("아이콘 회전(도, 반시계). 원본 » 는 진행 방향(+Z)을 가리키므로 0. 그림이 바뀌면 여기만.")]
    [SerializeField] private float iconRotation = 0f;

    [Tooltip("바닥에서 띄우는 높이 — 바닥과 겹쳐 깜빡이지 않게.")]
    [SerializeField, Min(0f)] private float heightOffset = 0.02f;

    [Header("색")]
    [Tooltip("아이콘 색·투명도(HDR).")]
    [ColorUsage(true, true)]
    [SerializeField] private Color iconColor = new Color(0.3f, 0.85f, 1f, 0.9f);

    [Tooltip("띠 바탕 색·투명도. 알파 0 이면 아이콘만 보인다.")]
    [SerializeField] private Color beltColor = new Color(0.3f, 0.85f, 1f, 0.12f);

    [Header("흐름")]
    [Tooltip("흐름 속도(m/초). 기본 아주 느리게. 음수면 뒤로 흐른다.")]
    [SerializeField] private float flowSpeed = 0.15f;

    [Tooltip("양 끝을 지우는 구간(길이 비율 0~0.5).")]
    [SerializeField, Range(0f, 0.5f)] private float endFade = 0.1f;

    [Header("맥동")]
    [Tooltip("맥동 속도(회/초).")]
    [SerializeField, Min(0f)] private float pulseSpeed = 0.6f;

    [Tooltip("아이콘마다 밀리는 맥동 위상(회). 0 = 모두 함께, 0.1 안팎 = 앞으로 번지는 물결.")]
    [SerializeField] private float pulsePhasePerIcon = 0.12f;

    [Tooltip("밝기 맥동 세기(0 = 없음, 1 = 꺼졌다 켜짐).")]
    [SerializeField, Range(0f, 1f)] private float pulseBrightness = 0.5f;

    [Tooltip("크기 맥동 세기(아이콘 크기 ± 비율).")]
    [SerializeField, Range(0f, 0.5f)] private float pulseScale = 0.12f;

    private MaterialPropertyBlock _block;

    /// <summary>길이 방향 칸 수(셰이더 _Tiling) — 길이에 비례해 아이콘 개수가 는다.</summary>
    public static float TilingFor(float length, float iconSpacing)
    {
        return Mathf.Max(0.01f, length / Mathf.Max(0.05f, iconSpacing));
    }

    /// <summary>m/초 흐름을 셰이더 단위(칸/초)로 — 간격이 바뀌어도 같은 실제 속도.</summary>
    public static float ScrollCellsPerSecond(float flowSpeed, float iconSpacing)
    {
        return flowSpeed / Mathf.Max(0.05f, iconSpacing);
    }

    /// <summary>아이콘 크기를 칸 UV 비율로(x = 길이 방향 / 간격, y = 폭 방향 / 폭) — 정사각 아이콘이 찌그러지지 않게.</summary>
    public static Vector2 IconScaleUv(float iconSize, float iconSpacing, float width)
    {
        return new Vector2(iconSize / Mathf.Max(0.05f, iconSpacing), iconSize / Mathf.Max(0.05f, width));
    }

    private void OnEnable() => Apply();

    private void OnValidate() => Apply();

    private void Apply()
    {
        if (visual == null)
            return;

        // 내장 Quad 는 XY 평면(-Z 면). (90, -90, 0) 으로 눕히면 면이 위(+Y), U(아이콘 » 방향) = 루트 +Z, 폭 = 루트 X.
        var t = visual.transform;
        t.localRotation = Quaternion.Euler(90f, -90f, 0f);
        t.localPosition = new Vector3(0f, heightOffset, length * 0.5f);
        t.localScale = new Vector3(length, width, 1f);

        Vector2 iconUv = IconScaleUv(iconSize, iconSpacing, width);

        _block ??= new MaterialPropertyBlock();
        visual.GetPropertyBlock(_block);
        _block.SetColor(BaseColorId, iconColor);
        _block.SetColor(BeltColorId, beltColor);
        _block.SetFloat(TilingId, TilingFor(length, iconSpacing));
        _block.SetVector(IconScaleUvId, new Vector4(iconUv.x, iconUv.y, 0f, 0f));
        _block.SetFloat(IconRotationId, iconRotation);
        _block.SetFloat(ScrollSpeedId, ScrollCellsPerSecond(flowSpeed, iconSpacing));
        _block.SetFloat(PulseSpeedId, pulseSpeed);
        _block.SetFloat(PulsePhaseId, pulsePhasePerIcon);
        _block.SetFloat(PulseBrightnessId, pulseBrightness);
        _block.SetFloat(PulseScaleId, pulseScale);
        _block.SetFloat(EndFadeId, endFade);
        visual.SetPropertyBlock(_block);
    }
}
