using System.Collections.Generic;
using UnityEngine;

/// <summary>집결 구역 박스 → 바닥 테두리 Quad 배치(순수). 박스는 바로 선(기울지 않은) 상태 전제.</summary>
public static class LobbyGatherBorderLayout
{
    public struct Placement
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        /// <summary>셰이더 _Size — 바닥 면 가로·세로(m).</summary>
        public Vector2 SizeMeters;
    }

    /// <summary>
    /// 내장 Quad(XY 평면 1×1, -Z 를 향함)를 구역 바닥에 눕힌다. 바닥 = 구역 오브젝트 원점 높이(로컬 y 0) — 씬의 Gather_* 는 원점이 바닥에 있고
    /// 박스는 바닥 아래로 0.5m 내려가 있어(center.y 1, size.y 3) 박스 바닥 면을 쓰면 테두리가 바닥 밑에 묻힌다(할 일 173).
    /// 위치 = 박스 XZ 중심 · 원점 높이 + 위로 <paramref name="heightOffset"/>. 크기 = 박스 size × lossyScale(XZ). 회전 = 박스 회전 × X 90°(Quad 앞면이 위를 본다).
    /// </summary>
    public static Placement Compute(Vector3 boxPosition, Quaternion boxRotation, Vector3 lossyScale, Vector3 center, Vector3 size, float heightOffset)
    {
        var scaled = Vector3.Scale(size, Abs(lossyScale));
        Vector3 localBottom = new Vector3(center.x, 0f, center.z);
        Vector3 bottom = boxPosition + boxRotation * Vector3.Scale(localBottom, lossyScale);
        Vector3 up = boxRotation * Vector3.up;

        return new Placement
        {
            Position = bottom + up * heightOffset,
            Rotation = boxRotation * Quaternion.Euler(90f, 0f, 0f),
            Scale = new Vector3(scaled.x, scaled.z, 1f),
            SizeMeters = new Vector2(scaled.x, scaled.z),
        };
    }

    /// <summary>테두리를 그리는가 — 활성이고 아직 확정 전인 구역만(확정 뒤 이탈은 표시와 무관).</summary>
    public static bool ShouldDraw(bool isActive, bool isConfirmed) => isActive && !isConfirmed;

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}

/// <summary>
/// 집결 구역 민트 테두리(할 일 158 C6, 명세 4.2) — 복제된 <see cref="PartyGatherZone"/> 값(활성 ∧ 확정 전)을 읽어
/// 그 구역의 BoxCollider footprint 그대로 바닥에 테두리를 그린다. 논리 트리거와 시각 테두리가 같은 박스를 쓴다.
/// <list type="bullet">
/// <item>전 피어 로컬 표시. 새 NetworkObject·RPC 없음. 씬의 Gather_* 오브젝트는 손대지 않는다.</item>
/// <item>Quad 는 프리팹에 미리 만든 풀(<see cref="pool"/> — 저작 메뉴가 만든다). 런타임 메시·오브젝트 생성 없음. 풀보다 구역이 많으면 경고 1회.</item>
/// <item>숫자·카운트다운 없음(명세: 집결 카운트다운 미표시).</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyGatherZoneBorders : MonoBehaviour
{
    private const string Tag = "[LobbyGatherZoneBorders]";
    private static readonly int SizeId = Shader.PropertyToID("_Size");
    private static readonly int BorderWidthId = Shader.PropertyToID("_BorderWidth");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Tooltip("테두리 Quad 풀(내장 Quad + M_LobbyGatherBorder). 꺼 둔 채 둔다.")]
    [SerializeField] private List<MeshRenderer> pool = new List<MeshRenderer>();

    [Tooltip("테두리 폭(m).")]
    [SerializeField, Min(0.01f)] private float borderWidth = 0.18f;

    [Tooltip("박스 바닥 면에서 띄우는 높이 — 바닥과 겹쳐 깜빡이지 않게.")]
    [SerializeField, Min(0f)] private float heightOffset = 0.02f;

    [ColorUsage(true, true)]
    [SerializeField] private Color color = new Color(0.35f, 1f, 0.82f, 0.9f);

    [Tooltip("L00 라커룸 누적 방문 영역(씬 Area_Locker 의 BoxCollider) — 바인더가 L00 동안 켠다(SetLockerVisible). 비우면 그리지 않는다.")]
    [SerializeField] private BoxCollider lockerArea;

    private readonly Dictionary<PartyGatherZone, BoxCollider> _boxes = new Dictionary<PartyGatherZone, BoxCollider>();
    private MaterialPropertyBlock _block;
    private bool _warnedPool;
    private bool _lockerVisible;

    /// <summary>씬에 켜진 테두리 묶음(로비 1개). <see cref="LobbyTutorialUI.SetLockerArea"/> 가 찾는다.</summary>
    public static LobbyGatherZoneBorders Active { get; private set; }

    /// <summary>라커룸 영역 테두리 켜고 끄기 — 집결 구역처럼 그 BoxCollider footprint 를 바닥에 그린다.</summary>
    public void SetLockerVisible(bool visible) => _lockerVisible = visible;

    private void OnEnable()
    {
        Active = this;
        HideFrom(0);
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
        _boxes.Clear();
    }

    private void LateUpdate()
    {
        _block ??= new MaterialPropertyBlock();
        int used = 0;
        if (_lockerVisible && lockerArea != null)
            Place(lockerArea, ref used);

        IReadOnlyList<PartyGatherZone> zones = PartyGatherZone.Spawned;
        for (int i = 0; i < zones.Count; i++)
        {
            PartyGatherZone zone = zones[i];
            if (zone == null || !LobbyGatherBorderLayout.ShouldDraw(zone.IsActive, zone.IsConfirmed))
                continue;

            BoxCollider box = BoxOf(zone);
            if (box == null)
                continue;

            if (!Place(box, ref used))
                break;
        }

        HideFrom(used);
    }

    // 풀의 다음 Quad 를 박스 footprint 에 놓는다. 풀이 모자라면 false(경고 1회).
    private bool Place(BoxCollider box, ref int used)
    {
        if (used >= pool.Count)
        {
            if (!_warnedPool)
            {
                _warnedPool = true;
                Debug.LogWarning($"{Tag} 활성 집결 구역이 테두리 풀({pool.Count}개)보다 많다 — 저작 메뉴로 풀을 늘릴 것.", this);
            }

            return false;
        }

        MeshRenderer quad = pool[used];
        used++;
        if (quad == null)
            return true;

        Transform t = box.transform;
        LobbyGatherBorderLayout.Placement p = LobbyGatherBorderLayout.Compute(t.position, t.rotation, t.lossyScale, box.center, box.size, heightOffset);
        Transform q = quad.transform;
        q.SetPositionAndRotation(p.Position, p.Rotation);
        q.localScale = DivideByParentScale(q, p.Scale);

        _block.Clear();
        _block.SetColor(BaseColorId, color);
        _block.SetVector(SizeId, new Vector4(p.SizeMeters.x, p.SizeMeters.y, 0f, 0f));
        _block.SetFloat(BorderWidthId, borderWidth);
        quad.SetPropertyBlock(_block);

        if (!quad.gameObject.activeSelf)
            quad.gameObject.SetActive(true);
        return true;
    }

    private BoxCollider BoxOf(PartyGatherZone zone)
    {
        if (!_boxes.TryGetValue(zone, out BoxCollider box) || box == null)
        {
            box = zone.GetComponent<BoxCollider>();
            _boxes[zone] = box;
        }

        return box;
    }

    private void HideFrom(int index)
    {
        for (int i = index; i < pool.Count; i++)
        {
            if (pool[i] != null && pool[i].gameObject.activeSelf)
                pool[i].gameObject.SetActive(false);
        }
    }

    // 풀은 이 오브젝트의 자식 — 부모 스케일이 1 이 아니어도 월드 크기가 맞게.
    private static Vector3 DivideByParentScale(Transform child, Vector3 world)
    {
        Vector3 parent = child.parent != null ? child.parent.lossyScale : Vector3.one;
        return new Vector3(
            parent.x != 0f ? world.x / parent.x : world.x,
            parent.y != 0f ? world.y / parent.y : world.y,
            parent.z != 0f ? world.z / parent.z : world.z);
    }
}
