using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 로비 문 — 살아 있는 플레이어가 다가가면 열리고 전원 멀어지면 닫힌다(할 일 78).
/// <list type="bullet">
/// <item>판정: <see cref="LobbyProximityDoorRules"/>(열림 반경 &lt; 닫힘 반경 히스테리시스). 대상은
/// <see cref="PlayerLifeCycleController.Active"/> 중 스폰된 <see cref="PlayerLifeState.Alive"/> — 매 프레임 씬을 뒤지지 않는다.</item>
/// <item>방향: 닫힌 상태에서 열 때 가장 가까운 플레이어의 반대쪽으로 연다(양방향). 다 닫힐 때까지 방향을 바꾸지 않는다.</item>
/// <item>네트워크: 동기화 없음. 연출이라 각 피어가 자기 화면의 플레이어 위치로 따로 판정한다
/// (위치는 이미 NetworkTransform 으로 복제되므로 피어끼리 상태가 거의 같다).</item>
/// <item>회전: <see cref="leaf"/> 를 Awake 때의 로컬 자세(닫힘) 기준으로 로컬 Y 축으로 돌린다. 회전 중심은 힌지 모서리(로컬 원점)의
/// 넘어가는 쪽 면(<see cref="frontFaceZ"/>/<see cref="backFaceZ"/>)이라 문짝 두께가 문틀로 파고들지 않는다.
/// <c>PF_Room_DoorSingle_01_v003</c> 은 메쉬 피벗이 힌지 모서리(문짝은 로컬 -X 쪽 약 1.27m, 두께 Z -0.22~0.06)라 루트를 그대로 돌린다.
/// 같은 오브젝트의 MeshCollider 도 함께 돌아 열린 문 사이로 지나갈 수 있다.</item>
/// <item>끼임 방지(할 일 99): 다음 자세의 문짝 상자(<see cref="LeafBox"/>)가 판정 대상 플레이어(열림 판정과 같은
/// <see cref="LobbyProximityDoorRules.IsDoorTarget"/>)의 몸 캡슐과 겹치면 그 프레임은 진행도·자세를 그대로 둔다 — 비키면 다시 돈다.</item>
/// <item>판정 중심: <see cref="centerOffset"/>(leaf 로컬, 닫힌 문 가운데)을 Awake 때 월드 좌표로 굳힌다 — 문이 돌아도 중심은 그대로.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyProximityDoor : MonoBehaviour
{
    [Tooltip("돌릴 문짝. 비우면 이 오브젝트(힌지가 피벗이어야 한다).")]
    [SerializeField] private Transform leaf;
    [Tooltip("판정 중심(leaf 로컬, 닫힌 상태 기준). 기본값 = 문짝 메쉬 가운데 바닥.")]
    [SerializeField] private Vector3 centerOffset = new Vector3(-0.63f, 0f, -0.08f);
    [Tooltip("문짝 로컬 +Z 쪽 면의 Z — +Z 쪽으로 열 때 이 면의 힌지 모서리를 축으로 돈다.")]
    [SerializeField] private float frontFaceZ = 0.06f;
    [Tooltip("문짝 로컬 -Z 쪽 면의 Z — -Z 쪽으로 열 때 이 면의 힌지 모서리를 축으로 돈다.")]
    [SerializeField] private float backFaceZ = -0.22f;
    [Tooltip("이 평면 거리(m) 안에 살아 있는 플레이어가 들어오면 연다.")]
    [SerializeField, Min(0f)] private float openRadius = 1.8f;
    [Tooltip("열린 뒤 전원이 이 평면 거리(m) 밖으로 나가야 닫는다. 열림 반경 이상이어야 한다.")]
    [SerializeField, Min(0f)] private float closeRadius = 2.6f;
    [Tooltip("다 열렸을 때 회전 각도(도). 방향은 연 플레이어의 반대쪽으로 자동.")]
    [SerializeField, Range(0f, 180f)] private float openAngle = 90f;
    [Tooltip("닫힘 → 다 열림까지 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float openSeconds = 0.35f;
    [Tooltip("다 열림 → 닫힘까지 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float closeSeconds = 0.5f;

    private Vector3 _closedLocalPosition;
    private Quaternion _closedLocalRotation;
    private Vector3 _center;
    private Vector3 _closedForward;
    private bool _isOpen;
    private int _swingSign = 1;
    private float _progress;
    private Bounds _leafBox;
    private bool _hasLeafBox;

    public bool IsOpen => _isOpen;

    private Transform Leaf => leaf != null ? leaf : transform;

    private void Awake()
    {
        var t = Leaf;
        _hasLeafBox = LeafBox(t, t.GetComponentsInChildren<Collider>(true), out _leafBox);
        _closedLocalPosition = t.localPosition;
        _closedLocalRotation = t.localRotation;
        _center = t.TransformPoint(centerOffset);
        _closedForward = t.forward;
    }

    private void Update()
    {
        var nearestSqr = NearestAlivePlayer(out var nearestPosition);
        var wasOpen = _isOpen;
        _isOpen = LobbyProximityDoorRules.NextOpen(_isOpen, nearestSqr, openRadius, closeRadius);

        // 다 닫힌 상태에서 열 때만 방향을 고른다 — 닫히는 도중 다시 열리면 가던 방향으로 되돌아간다.
        if (_isOpen && !wasOpen && _progress <= 0f)
            _swingSign = LobbyProximityDoorRules.SwingSign(Vector3.Dot(nearestPosition - _center, _closedForward));

        var next = LobbyProximityDoorRules.StepProgress(_progress, _isOpen, openSeconds, closeSeconds, Time.deltaTime);
        if (Mathf.Approximately(next, _progress))
            return;

        var angle = _swingSign * openAngle * Mathf.SmoothStep(0f, 1f, next);
        var pivot = new Vector3(0f, 0f, _swingSign > 0 ? frontFaceZ : backFaceZ);
        LobbyProximityDoorRules.LeafPose(_closedLocalPosition, _closedLocalRotation, angle, pivot,
            out var position, out var rotation);

        next = LobbyProximityDoorRules.ResolveProgress(_progress, next, NextPoseBlocked(position, rotation));
        if (Mathf.Approximately(next, _progress))
            return;

        _progress = next;
        Leaf.SetLocalPositionAndRotation(position, rotation);
    }

    /// <summary>다음 로컬 자세의 문짝 상자가 판정 대상 플레이어의 몸 캡슐과 하나라도 겹치는가.</summary>
    private bool NextPoseBlocked(Vector3 localPosition, Quaternion localRotation)
    {
        if (!_hasLeafBox)
            return false;

        var parent = Leaf.parent;
        var worldPosition = parent != null ? parent.TransformPoint(localPosition) : localPosition;
        var worldRotation = parent != null ? parent.rotation * localRotation : localRotation;

        var players = PlayerLifeCycleController.Active;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null || !LobbyProximityDoorRules.IsDoorTarget(player.IsSpawned, player.State))
                continue;
            if (!player.TryGetComponent(out PlayerMotor motor) || !motor.TryGetComponent(out CapsuleCollider body))
                continue;

            if (BodyOverlapsLeafBox(_leafBox, worldPosition, worldRotation, body))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 문짝 콜라이더(트리거 제외 Box·Mesh)를 감싸는 상자 — 문짝 회전 기준, 월드 단위
    /// (점 w 는 <c>Inverse(leaf.rotation) * (w - leaf.position)</c>). 문짝이 어느 자세에 있든 같은 값이 나온다.
    /// 비볼록 MeshCollider 는 <c>Physics.ComputePenetration</c> 이 몸 캡슐이 얇은 문짝을 관통한 경우(반경 0.38 &gt; 두께 0.28)
    /// 양면 관통이 상쇄돼 겹침을 놓치므로 상자로 판정한다.
    /// </summary>
    public static bool LeafBox(Transform leafTransform, Collider[] colliders, out Bounds box)
    {
        box = default;
        var found = false;
        var toLeaf = Quaternion.Inverse(leafTransform.rotation);
        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || collider.isTrigger)
                continue;

            Bounds local;
            if (collider is BoxCollider boxCollider)
                local = new Bounds(boxCollider.center, boxCollider.size);
            else if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
                local = meshCollider.sharedMesh.bounds;
            else
                continue;

            var colliderTransform = collider.transform;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z);
                var leafPoint = toLeaf * (colliderTransform.TransformPoint(point) - leafTransform.position);
                if (found)
                    box.Encapsulate(leafPoint);
                else
                    box = new Bounds(leafPoint, Vector3.zero);
                found = true;
            }
        }

        return found;
    }

    /// <summary>문짝이 월드 자세 (<paramref name="leafPosition"/>, <paramref name="leafRotation"/>)일 때 <see cref="LeafBox"/> 가 몸 캡슐(Y 축)과 겹치는가.</summary>
    public static bool BodyOverlapsLeafBox(Bounds leafBox, Vector3 leafPosition, Quaternion leafRotation, CapsuleCollider body)
    {
        if (body == null || !body.enabled)
            return false;

        var bodyTransform = body.transform;
        var scale = bodyTransform.lossyScale;
        return LobbyProximityDoorRules.LeafBoxOverlapsUprightCapsule(leafBox, leafPosition, leafRotation,
            bodyTransform.TransformPoint(body.center), body.height * Mathf.Abs(scale.y),
            body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
    }

    private float NearestAlivePlayer(out Vector3 position)
    {
        var nearest = float.PositiveInfinity;
        position = default;
        var players = PlayerLifeCycleController.Active;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null || !LobbyProximityDoorRules.IsDoorTarget(player.IsSpawned, player.State))
                continue;

            var playerPosition = player.transform.position;
            var sqr = LobbyProximityDoorRules.PlanarSqrDistance(playerPosition, _center);
            if (sqr < nearest)
            {
                nearest = sqr;
                position = playerPosition;
            }
        }

        return nearest;
    }

    private void OnValidate()
    {
        if (closeRadius < openRadius)
            closeRadius = openRadius;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var center = Application.isPlaying ? _center : Leaf.TransformPoint(centerOffset);
        Handles.color = new Color(0.3f, 1f, 0.4f, 0.9f);
        Handles.DrawWireDisc(center, Vector3.up, openRadius);
        Handles.color = new Color(1f, 0.6f, 0.2f, 0.9f);
        Handles.DrawWireDisc(center, Vector3.up, closeRadius);
    }
#endif
}
