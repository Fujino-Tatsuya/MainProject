using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 경로형 로비 바닥 안내 화살표(할 일 111 후속) — 시작점·경유지·도착점을 잇는 꺾은선을 따라 점선 조각을 깔고,
/// N 조각마다 작은 삼각형, 도착점에 큰 삼각형을 놓는다. 배치 계산 = <see cref="LobbyFloorArrowPathLayout"/>.
/// <list type="bullet">
/// <item>🔴 <b>에디터에서 굽는다</b>: 값·점 위치가 바뀌면(OnValidate·점 이동 뒤 지연 호출, 인스펙터 '다시 굽기') <see cref="piecesRoot"/> 아래 조각 오브젝트로 저장한다.
/// Play·빌드에선 아무것도 만들지 않는다 — 켜질 때 조각마다 <see cref="MaterialPropertyBlock"/> 만 넣는다.</item>
/// <item>조각 = 내장 Quad + 공유 머티리얼 하나(복제 없음). 텍스처 영역(UV)은 이 컴포넌트 값 — SVN 스프라이트 슬라이스에 기대지 않는다.</item>
/// <item>흐름 = 셰이더 Lobby/FloorArrowPath 가 조각별 경로 거리로 밝기 물결을 앞으로 민다(매 프레임 스크립트 없음).</item>
/// <item>스텝별 켜고 끄기 = 부모 <see cref="LobbyGuideSteps"/> 가 이 오브젝트를 통째로 켜고 끈다(LobbyGuideSteps.prefab 의 자식 3개).</item>
/// </list>
/// 상세: Docs/tech/lobby-training.md "바닥 안내 화살표".
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class LobbyFloorArrowPath : MonoBehaviour
{
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int UvRectId = Shader.PropertyToID("_UvRect");
    private static readonly int PathDistanceId = Shader.PropertyToID("_PathDistance");
    private static readonly int FlowSpeedId = Shader.PropertyToID("_FlowSpeed");
    private static readonly int FlowWavelengthId = Shader.PropertyToID("_FlowWavelength");
    private static readonly int FlowMinAlphaId = Shader.PropertyToID("_FlowMinAlpha");

    // ArrowLineSet.png(1254x1254) 의 점선·삼각형 영역 — 사용자 슬라이스 ArrowLineSet_0/_1 과 같은 픽셀 사각형.
    private const float AtlasSize = 1254f;

    [Serializable]
    private struct BakedPiece
    {
        public MeshRenderer renderer;
        public LobbyFloorArrowPieceKind kind;
        public float distance;
    }

    [Header("경로 (루트 기준 — 보통 이 오브젝트의 자식)")]
    [SerializeField] private Transform start;

    [Tooltip("꺾이는 점들. 순서대로 잇는다.")]
    [SerializeField] private List<Transform> waypoints = new List<Transform>();

    [SerializeField] private Transform end;

    [Header("모양 (m)")]
    [Tooltip("모든 조각이 함께 쓰는 머티리얼(Lobby/FloorArrowPath + ArrowLineSet).")]
    [SerializeField] private Material material;

    [Tooltip("조각 중심 사이 간격. 구간마다 이 값에 가장 가까운 등간격으로 맞춘다.")]
    [SerializeField, Min(0.05f)] private float spacing = 0.8f;

    [SerializeField, Min(0.02f)] private float dashLength = 0.45f;
    [SerializeField, Min(0.02f)] private float dashWidth = 0.2f;

    [Tooltip("몇 조각마다 하나를 작은 삼각형으로. 0 = 없음.")]
    [SerializeField, Min(0)] private int triangleEvery = 4;

    [Tooltip("작은 삼각형 폭. 길이는 텍스처 비율대로.")]
    [SerializeField, Min(0.02f)] private float smallTriangleSize = 0.35f;

    [Tooltip("도착점 큰 삼각형 폭(중심 = 도착점). 길이는 텍스처 비율대로.")]
    [SerializeField, Min(0.02f)] private float endTriangleSize = 0.9f;

    [Tooltip("바닥에서 띄우는 높이 — 바닥과 겹쳐 깜빡이지 않게.")]
    [SerializeField, Min(0f)] private float heightOffset = 0.02f;

    [ColorUsage(true, true)]
    [SerializeField] private Color color = new Color(1f, 0.86f, 0.17f, 0.9f);

    [Header("흐름 (셰이더)")]
    [Tooltip("밝기 물결이 앞으로 가는 속도(m/초). 0 = 정지, 음수 = 뒤로.")]
    [SerializeField] private float flowSpeed = 2f;

    [Tooltip("물결 한 주기 길이(m).")]
    [SerializeField, Min(0.1f)] private float flowWavelength = 3f;

    [Tooltip("물결 골의 불투명도 배율(1 = 물결 없음).")]
    [SerializeField, Range(0f, 1f)] private float flowMinAlpha = 0.35f;

    [Header("텍스처 영역 (UV 0~1, 왼쪽 아래 기준)")]
    [SerializeField] private Rect dashUv = new Rect(317f / AtlasSize, 556f / AtlasSize, 280f / AtlasSize, 125f / AtlasSize);

    [Tooltip("꼭짓점이 위(+V)인 삼각형.")]
    [SerializeField] private Rect triangleUv = new Rect(726f / AtlasSize, 540f / AtlasSize, 211f / AtlasSize, 188f / AtlasSize);

    [Header("구운 결과 (손대지 말 것)")]
    [Tooltip("구운 조각의 부모. 단계 표시 targets 로 이걸 건다.")]
    [SerializeField] private Transform piecesRoot;

    [SerializeField, HideInInspector] private List<BakedPiece> baked = new List<BakedPiece>();
    [SerializeField, HideInInspector] private int bakedHash;

    private MaterialPropertyBlock _block;

    public Transform PiecesRoot => piecesRoot;
    public int BakedCount => baked.Count;

    private void OnEnable() => ApplyBlocks();

    private void ApplyBlocks()
    {
        _block ??= new MaterialPropertyBlock();
        foreach (var piece in baked)
        {
            if (piece.renderer == null)
                continue;

            var uv = piece.kind == LobbyFloorArrowPieceKind.Dash ? dashUv : triangleUv;
            _block.Clear();
            _block.SetColor(BaseColorId, color);
            _block.SetVector(UvRectId, new Vector4(uv.x, uv.y, uv.width, uv.height));
            _block.SetFloat(PathDistanceId, piece.distance);
            _block.SetFloat(FlowSpeedId, flowSpeed);
            _block.SetFloat(FlowWavelengthId, flowWavelength);
            _block.SetFloat(FlowMinAlphaId, flowMinAlpha);
            piece.renderer.SetPropertyBlock(_block);
        }
    }

#if UNITY_EDITOR
    private static readonly List<Vector3> PointBuffer = new List<Vector3>();
    private static readonly List<LobbyFloorArrowPiece> LayoutBuffer = new List<LobbyFloorArrowPiece>();
    private bool _bakeScheduled;

    private void OnValidate()
    {
        ApplyBlocks();
        ScheduleBake();
    }

    private void OnDrawGizmos()
    {
        var current = IsBakeCurrent();
        CollectPoints(PointBuffer);
        Gizmos.color = current ? new Color(1f, 0.86f, 0.17f, 0.6f) : Color.red;
        for (var i = 1; i < PointBuffer.Count; i++)
            Gizmos.DrawLine(transform.TransformPoint(PointBuffer[i - 1]), transform.TransformPoint(PointBuffer[i]));

        // 점을 끌어 옮긴 뒤(드래그가 끝나면) 한 번 다시 굽는다. 빨간 선 = 구운 결과가 낡음.
        if (!current && GUIUtility.hotControl == 0 && !EditorApplication.isPlayingOrWillChangePlaymode)
            ScheduleBake();
    }

    /// <summary>인스펙터 '다시 굽기' — 지금 값으로 바로 굽는다(Play 중엔 거부).</summary>
    [ContextMenu("다시 굽기")]
    public void BakeNow()
    {
        if (!CanBake())
        {
            Debug.LogWarning($"[LobbyFloorArrowPath] {name}: Play 중이거나 디스크의 프리팹 에셋이라 굽지 않는다 — 에디터 편집 중(프리팹 모드 포함)에 굽는다.", this);
            return;
        }

        Bake();
    }

    public bool IsBakeCurrent()
    {
        if (bakedHash != ComputeHash() || piecesRoot == null)
            return false;

        foreach (var piece in baked)
        {
            if (piece.renderer == null || piece.renderer.sharedMaterial != material || piece.renderer.transform.parent != piecesRoot)
                return false;
        }

        return true;
    }

    private void ScheduleBake()
    {
        if (_bakeScheduled)
            return;
        _bakeScheduled = true;
        EditorApplication.delayCall += DelayedBake;
    }

    private void DelayedBake()
    {
        _bakeScheduled = false;
        if (this == null || !CanBake() || IsBakeCurrent())
            return;
        Bake();
    }

    private bool CanBake()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorUtility.IsPersistent(gameObject)
            && gameObject.scene.IsValid();
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
        // 직속 자식은 로컬 좌표 그대로 — 프리팹 인스턴스가 어디 놓여도 같은 값이라 열 때마다 다시 굽지 않는다.
        points.Add(point.parent == transform ? point.localPosition : transform.InverseTransformPoint(point.position));
    }

    private LobbyFloorArrowPathSettings Settings(out float triangleAspect)
    {
        var texture = material != null ? material.GetTexture(BaseMapId) : null;
        triangleAspect = LobbyFloorArrowPathLayout.RectAspect(triangleUv, texture != null ? texture.width : 1, texture != null ? texture.height : 1);
        return new LobbyFloorArrowPathSettings
        {
            Spacing = spacing,
            DashLength = dashLength,
            DashWidth = dashWidth,
            SmallTriangleLength = smallTriangleSize * triangleAspect,
            SmallTriangleWidth = smallTriangleSize,
            EndTriangleLength = endTriangleSize * triangleAspect,
            TriangleEvery = triangleEvery,
        };
    }

    private int ComputeHash()
    {
        CollectPoints(PointBuffer);
        var s = Settings(out var aspect);
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
            Add(s.Spacing);
            Add(s.DashLength);
            Add(s.DashWidth);
            Add(s.TriangleEvery);
            Add(smallTriangleSize);
            Add(endTriangleSize);
            Add(aspect);
            Add(heightOffset);
            return h;
        }
    }

    private void Bake()
    {
        CollectPoints(PointBuffer);
        var settings = Settings(out var triangleAspect);
        LobbyFloorArrowPathLayout.Layout(PointBuffer, settings, LayoutBuffer);

        Undo.RecordObject(this, "Bake floor arrow path");
        if (piecesRoot == null)
        {
            var rootObject = new GameObject("Pieces");
            Undo.RegisterCreatedObjectUndo(rootObject, "Bake floor arrow path");
            Undo.SetTransformParent(rootObject.transform, transform, "Bake floor arrow path");
            piecesRoot = rootObject.transform;
        }

        Undo.RecordObject(piecesRoot, "Bake floor arrow path");
        piecesRoot.localPosition = Vector3.zero;
        piecesRoot.localRotation = Quaternion.identity;
        piecesRoot.localScale = Vector3.one;

        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var up = Vector3.up * heightOffset;
        baked.Clear();
        for (var i = 0; i < LayoutBuffer.Count; i++)
        {
            var piece = LayoutBuffer[i];
            var go = i < piecesRoot.childCount ? piecesRoot.GetChild(i).gameObject : CreatePiece();
            var filter = go.TryGetComponent<MeshFilter>(out var f) ? f : Undo.AddComponent<MeshFilter>(go);
            var renderer = go.TryGetComponent<MeshRenderer>(out var r) ? r : Undo.AddComponent<MeshRenderer>(go);
            Undo.RecordObjects(new UnityEngine.Object[] { go, go.transform, filter, renderer }, "Bake floor arrow path");

            go.name = $"{piece.Kind}_{i:000}";
            go.SetActive(true);
            go.transform.localPosition = piece.Position + up;
            go.transform.localRotation = LobbyFloorArrowPathLayout.PieceRotation(piece.Kind, piece.Direction);
            go.transform.localScale = piece.Kind switch
            {
                LobbyFloorArrowPieceKind.Dash => LobbyFloorArrowPathLayout.PieceScale(piece.Kind, dashLength, dashWidth),
                LobbyFloorArrowPieceKind.SmallTriangle => LobbyFloorArrowPathLayout.PieceScale(piece.Kind, smallTriangleSize * triangleAspect, smallTriangleSize),
                _ => LobbyFloorArrowPathLayout.PieceScale(piece.Kind, endTriangleSize * triangleAspect, endTriangleSize),
            };
            filter.sharedMesh = quad;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            baked.Add(new BakedPiece { renderer = renderer, kind = piece.Kind, distance = piece.Distance });
        }

        // 남는 조각: 지울 수 있으면 지우고, 프리팹에서 온 조각(인스턴스에선 못 지움)은 끈다.
        for (var i = piecesRoot.childCount - 1; i >= LayoutBuffer.Count; i--)
        {
            var extra = piecesRoot.GetChild(i).gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(extra) && !PrefabUtility.IsAddedGameObjectOverride(extra))
            {
                Undo.RecordObject(extra, "Bake floor arrow path");
                extra.SetActive(false);
            }
            else
            {
                Undo.DestroyObjectImmediate(extra);
            }
        }

        bakedHash = ComputeHash();
        EditorUtility.SetDirty(this);
        ApplyBlocks();
    }

    private GameObject CreatePiece()
    {
        var go = new GameObject("Piece", typeof(MeshFilter), typeof(MeshRenderer));
        Undo.RegisterCreatedObjectUndo(go, "Bake floor arrow path");
        Undo.SetTransformParent(go.transform, piecesRoot, "Bake floor arrow path");
        return go;
    }
#endif
}
