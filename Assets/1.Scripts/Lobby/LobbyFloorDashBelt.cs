using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 경로형 로비 바닥 대시 벨트(할 일 257) — 시작점·경유지·도착점을 잇는 꺾은선을 따라 하나로 이어진 띠를 깔고,
/// 그 위에 대시 아이콘(»)을 <see cref="iconSpacing"/> 간격으로 반복해 맥동하며 아주 느리게 경로를 따라 흘린다.
/// <list type="bullet">
/// <item>띠 메시 = <see cref="LobbyFloorDashBeltLayout"/>(꺾임은 둥근 모서리 <see cref="cornerRadius"/>, 0 = 마이터). U = 누적 경로 길이라 아이콘이 꺾임을 지나 계속 흐르고 그 자리 진행 방향을 가리킨다.</item>
/// <item>🔴 메시는 켜질 때 한 번, 에디터에서 점·값이 바뀔 때만 다시 만든다(매 프레임 생성 없음). 메시와 그것을 그리는 자식은 저장하지 않는다(HideAndDontSave)
/// — 씬·프리팹에 구운 메시가 남지 않아 diff 가 없다.</item>
/// <item>흐름·맥동 = 셰이더 Lobby/FloorDashBelt 가 _Time 으로. 수치 = <see cref="MaterialPropertyBlock"/>(머티리얼 복제 없음).</item>
/// <item>늘 켜져 있다 — 배치·튜토리얼 연결은 아직 없음.</item>
/// </list>
/// 상세: Docs/tech/lobby-training.md "대시 벨트".
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class LobbyFloorDashBelt : MonoBehaviour
{
    private const string MeshObjectName = "__DashBeltMesh";
    private const float ArcStepDegrees = 10f;
    private const float MaxMiter = 4f;

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

    [Header("경로 (루트 기준 — 보통 이 오브젝트의 자식)")]
    [SerializeField] private Transform start;

    [Tooltip("꺾이는 점들. 순서대로 잇는다.")]
    [SerializeField] private List<Transform> waypoints = new List<Transform>();

    [SerializeField] private Transform end;

    [Header("띠 (m)")]
    [Tooltip("Lobby/FloorDashBelt + Common_DashIcon 머티리얼.")]
    [SerializeField] private Material material;

    [Tooltip("띠 폭(일정).")]
    [SerializeField, Min(0.05f)] private float width = 1f;

    [Tooltip("꺾임 둥근 모서리 반지름(중심선 기준). 0 = 마이터(뾰족). 구간이 짧으면 자동으로 줄어든다.")]
    [SerializeField, Min(0f)] private float cornerRadius = 0.5f;

    [Tooltip("바닥에서 띄우는 높이 — 바닥과 겹쳐 깜빡이지 않게.")]
    [SerializeField, Min(0f)] private float heightOffset = 0.02f;

    [Header("아이콘 (m)")]
    [Tooltip("아이콘 하나가 차지하는 길이(아이콘 중심 간격). 칸 수 = 경로 길이 ÷ 간격.")]
    [SerializeField, Min(0.05f)] private float iconSpacing = 1f;

    [Tooltip("아이콘 크기(정사각, m). 원본 그림 둘레 여백 포함. 폭·간격보다 크면 잘린다.")]
    [SerializeField, Min(0.01f)] private float iconSize = 1f;

    [Tooltip("아이콘 회전(도, 반시계). 원본 » 는 진행 방향을 가리키므로 0. 그림이 바뀌면 여기만.")]
    [SerializeField] private float iconRotation = 0f;

    [Header("색")]
    [Tooltip("아이콘 색·투명도(HDR).")]
    [ColorUsage(true, true)]
    [SerializeField] private Color iconColor = new Color(0.3f, 0.85f, 1f, 0.9f);

    [Tooltip("띠 바탕 색·투명도. 알파 0 이면 아이콘만 보인다.")]
    [SerializeField] private Color beltColor = new Color(0.3f, 0.85f, 1f, 0.12f);

    [Header("흐름")]
    [Tooltip("흐름 속도(m/초). 기본 아주 느리게. 음수면 뒤로 흐른다.")]
    [SerializeField] private float flowSpeed = 0.15f;

    [Tooltip("양 끝을 지우는 구간(경로 길이 비율 0~0.5).")]
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

    private static readonly List<Vector3> PointBuffer = new List<Vector3>();
    private static readonly List<Vector3> CenterBuffer = new List<Vector3>();
    private static readonly List<Vector3> VertexBuffer = new List<Vector3>();
    private static readonly List<Vector2> UvBuffer = new List<Vector2>();
    private static readonly List<int> TriangleBuffer = new List<int>();

    private MaterialPropertyBlock _block;
    private Mesh _mesh;
    private MeshFilter _filter;
    private MeshRenderer _renderer;
    private float _pathLength;
    private int _builtHash;
#if UNITY_EDITOR
    private bool _built;
#endif

    /// <summary>띠를 그리는 렌더러(저장 안 되는 숨은 자식). 아직 안 만들었으면 null.</summary>
    public MeshRenderer BeltRenderer => _renderer;

    /// <summary>둥근 모서리까지 반영한 중심선 길이(m).</summary>
    public float PathLength => _pathLength;

    /// <summary>길이 방향 칸 수(셰이더 _Tiling) — 경로 길이에 비례해 아이콘 개수가 는다.</summary>
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

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnDestroy()
    {
        // 숨은 자식은 부모와 함께 사라진다 — 메시만 따로 지운다.
        if (_mesh != null)
            DestroyImmediateOrDeferred(_mesh);
    }

    /// <summary>지금 점·값으로 띠 메시를 다시 만든다(켜질 때, 에디터에서 바뀔 때만).</summary>
    [ContextMenu("다시 만들기")]
    public void Rebuild()
    {
        // 디스크의 프리팹 에셋(씬 밖)엔 자식을 만들지 않는다 — 씬·프리팹 모드 인스턴스에서만.
        if (!gameObject.scene.IsValid())
            return;

        EnsureMeshObject();

        CollectPoints(PointBuffer);
        LobbyFloorDashBeltLayout.BuildCenterline(PointBuffer, cornerRadius, ArcStepDegrees, CenterBuffer);
        var up = Vector3.up * heightOffset;
        for (var i = 0; i < CenterBuffer.Count; i++)
            CenterBuffer[i] += up;
        _pathLength = LobbyFloorDashBeltLayout.BuildRibbon(CenterBuffer, width, MaxMiter, VertexBuffer, UvBuffer, TriangleBuffer);

        _mesh.Clear();
        _mesh.SetVertices(VertexBuffer);
        _mesh.SetUVs(0, UvBuffer);
        _mesh.SetTriangles(TriangleBuffer, 0);
        _mesh.RecalculateBounds();
        _renderer.enabled = TriangleBuffer.Count > 0;
        _renderer.sharedMaterial = material;

        _builtHash = ComputeHash();
#if UNITY_EDITOR
        _built = true;
#endif
        ApplyBlock();
    }

    private void EnsureMeshObject()
    {
        if (_filter == null)
        {
            // 도메인 리로드 뒤엔 필드만 비고 숨은 자식은 남아 있다 — 찾아서 다시 쓴다.
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name == MeshObjectName && child.TryGetComponent(out MeshFilter existing))
                {
                    _filter = existing;
                    break;
                }
            }
        }

        if (_filter == null)
        {
            var go = new GameObject(MeshObjectName, typeof(MeshFilter), typeof(MeshRenderer))
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = gameObject.layer,
            };
            go.transform.SetParent(transform, false);
            _filter = go.GetComponent<MeshFilter>();
        }

        _renderer = _filter.GetComponent<MeshRenderer>();
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        if (_mesh == null)
        {
            _mesh = _filter.sharedMesh != null && _filter.sharedMesh.name == MeshObjectName
                ? _filter.sharedMesh
                : new Mesh { name = MeshObjectName, hideFlags = HideFlags.HideAndDontSave };
            _mesh.MarkDynamic();
        }

        _filter.sharedMesh = _mesh;
    }

    private void ApplyBlock()
    {
        if (_renderer == null)
            return;

        var iconUv = IconScaleUv(iconSize, iconSpacing, width);
        _block ??= new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(_block);
        _block.SetColor(BaseColorId, iconColor);
        _block.SetColor(BeltColorId, beltColor);
        _block.SetFloat(TilingId, TilingFor(_pathLength, iconSpacing));
        _block.SetVector(IconScaleUvId, new Vector4(iconUv.x, iconUv.y, 0f, 0f));
        _block.SetFloat(IconRotationId, iconRotation);
        _block.SetFloat(ScrollSpeedId, ScrollCellsPerSecond(flowSpeed, iconSpacing));
        _block.SetFloat(PulseSpeedId, pulseSpeed);
        _block.SetFloat(PulsePhaseId, pulsePhasePerIcon);
        _block.SetFloat(PulseBrightnessId, pulseBrightness);
        _block.SetFloat(PulseScaleId, pulseScale);
        _block.SetFloat(EndFadeId, endFade);
        _renderer.SetPropertyBlock(_block);
    }

    private void CollectPoints(List<Vector3> points)
    {
        points.Clear();
        AddPoint(points, start);
        foreach (var waypoint in waypoints)
            AddPoint(points, waypoint);
        AddPoint(points, end);
    }

    private void AddPoint(List<Vector3> points, Transform point)
    {
        if (point == null)
            return;
        points.Add(point.parent == transform ? point.localPosition : transform.InverseTransformPoint(point.position));
    }

    private int ComputeHash()
    {
        CollectPoints(PointBuffer);
        unchecked
        {
            var h = 17;
            void Add(float v) => h = h * 31 + Mathf.RoundToInt(v * 1000f);
            foreach (var p in PointBuffer)
            {
                Add(p.x);
                Add(p.y);
                Add(p.z);
            }

            Add(PointBuffer.Count);
            Add(width);
            Add(cornerRadius);
            Add(heightOffset);
            h = h * 31 + (material != null ? material.GetInstanceID() : 0);
            return h;
        }
    }

    private static void DestroyImmediateOrDeferred(Object target)
    {
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

#if UNITY_EDITOR
    private bool _rebuildScheduled;

    private void OnValidate()
    {
        // OnValidate 안에서 오브젝트를 만들 수 없다 — 수치는 바로, 메시는 다음 틱에.
        ApplyBlock();
        if (_rebuildScheduled)
            return;
        _rebuildScheduled = true;
        EditorApplication.delayCall += () =>
        {
            _rebuildScheduled = false;
            if (this != null && isActiveAndEnabled)
                Rebuild();
        };
    }

    private void OnDrawGizmos()
    {
        // 점을 옮기면(드래그 중 포함) 바로 다시 만든다 — 해시가 같으면 아무것도 안 한다.
        if (!Application.isPlaying && isActiveAndEnabled && (!_built || _builtHash != ComputeHash()))
            Rebuild();

        CollectPoints(PointBuffer);
        Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.6f);
        for (var i = 1; i < PointBuffer.Count; i++)
            Gizmos.DrawLine(transform.TransformPoint(PointBuffer[i - 1]), transform.TransformPoint(PointBuffer[i]));
    }

    private void OnDrawGizmosSelected()
    {
        // 띠 테두리 — 숨은 자식이라 씬에서 집을 수 없으니 선택 시 윤곽을 보인다.
        if (_mesh == null || _mesh.vertexCount == 0)
            return;
        Gizmos.color = new Color(0.3f, 0.85f, 1f, 1f);
        Gizmos.matrix = transform.localToWorldMatrix;
        var vertices = _mesh.vertices;
        for (var i = 2; i < vertices.Length; i++)
            Gizmos.DrawLine(vertices[i - 2], vertices[i]);
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
