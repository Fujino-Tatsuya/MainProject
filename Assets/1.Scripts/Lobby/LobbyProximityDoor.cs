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
/// <see cref="LobbyProximityDoorRules.IsDoorTarget"/>)의 몸 캡슐과 겹치면 그 틱은 진행도·자세를 그대로 둔다 — 비키면 다시 돈다.</item>
/// <item>타이밍(할 일 99): 판정·회전은 <c>FixedUpdate</c> 에서 플레이어 모터보다 먼저(<see cref="DefaultExecutionOrder"/> 음수) 돈다.
/// 플레이어 위치는 보간으로 최대 1틱 뒤처지는 transform 대신 <see cref="Rigidbody.position"/> 을 읽고, 문짝 자세는 leaf 의
/// kinematic Rigidbody 에 <see cref="Rigidbody.position"/>/<see cref="Rigidbody.rotation"/> 로 바로 넣는다 — Auto Sync Transforms 가
/// 꺼져 있어 transform 으로 돌리면 다음 Simulate 전까지 모터 스윕에 안 보여 같은 틱에 몸이 문짝 속으로 들어간다.</item>
/// <item>판정 중심: <see cref="centerOffset"/>(leaf 로컬, 닫힌 문 가운데)을 Awake 때 월드 좌표로 굳힌다 — 문이 돌아도 중심은 그대로.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(LobbyProximityDoor.ExecutionOrder)]
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
    private Rigidbody _leafBody;

    /// <summary>실행 순서 — 실행 순서 지정이 없는(0) <see cref="PlayerMotor"/> 보다 같은 물리 틱에서 먼저 돈다.</summary>
    public const int ExecutionOrder = -100;

    public bool IsOpen => _isOpen;

    private Transform Leaf => leaf != null ? leaf : transform;

    private void Awake()
    {
        var t = Leaf;
        var colliders = t.GetComponentsInChildren<Collider>(true);
        _hasLeafBox = LeafBox(t, colliders, out _leafBox);
        _closedLocalPosition = t.localPosition;
        _closedLocalRotation = t.localRotation;
        _center = t.TransformPoint(centerOffset);
        _closedForward = t.forward;
        _leafBody = EnsureKinematicBody(t, colliders);
    }

    private void FixedUpdate()
    {
        var nearestSqr = NearestAlivePlayer(out var nearestPosition);
        var wasOpen = _isOpen;
        _isOpen = LobbyProximityDoorRules.NextOpen(_isOpen, nearestSqr, openRadius, closeRadius);

        // 다 닫힌 상태에서 열 때만 방향을 고른다 — 닫히는 도중 다시 열리면 가던 방향으로 되돌아간다.
        if (_isOpen && !wasOpen && _progress <= 0f)
            _swingSign = LobbyProximityDoorRules.SwingSign(Vector3.Dot(nearestPosition - _center, _closedForward));

        var next = LobbyProximityDoorRules.StepProgress(_progress, _isOpen, openSeconds, closeSeconds, Time.fixedDeltaTime);
        if (Mathf.Approximately(next, _progress))
            return;

        var angle = _swingSign * openAngle * Mathf.SmoothStep(0f, 1f, next);
        var pivot = new Vector3(0f, 0f, _swingSign > 0 ? frontFaceZ : backFaceZ);
        LobbyProximityDoorRules.LeafPose(_closedLocalPosition, _closedLocalRotation, angle, pivot,
            out var position, out var rotation);

        var parent = Leaf.parent;
        var worldPosition = parent != null ? parent.TransformPoint(position) : position;
        var worldRotation = parent != null ? parent.rotation * rotation : rotation;

        next = LobbyProximityDoorRules.ResolveProgress(_progress, next, NextPoseBlocked(worldPosition, worldRotation));
        if (Mathf.Approximately(next, _progress))
            return;

        _progress = next;
        // 물리에 바로 반영 — 이어서 도는 모터 스윕이 이 틱에 새 자세를 본다. transform 은 Simulate 뒤 따라온다.
        _leafBody.position = worldPosition;
        _leafBody.rotation = worldRotation;
    }

    /// <summary>다음 월드 자세의 문짝 상자가 판정 대상 플레이어의 몸 캡슐과 하나라도 겹치는가.</summary>
    private bool NextPoseBlocked(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (!_hasLeafBox)
            return false;

        var players = PlayerLifeCycleController.Active;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null || !LobbyProximityDoorRules.IsDoorTarget(player.IsSpawned, player.State))
                continue;
            if (!player.TryGetComponent(out PlayerMotor motor) || !motor.TryGetComponent(out CapsuleCollider body))
                continue;

            PlayerPose(player, out var playerPosition, out var playerRotation);
            if (BodyOverlapsLeafBox(_leafBox, worldPosition, worldRotation, body, player.transform, playerPosition, playerRotation))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 플레이어 루트의 물리 자세 — Rigidbody 가 있으면 그 <see cref="Rigidbody.position"/>/<see cref="Rigidbody.rotation"/>,
    /// 없으면 transform. 보간 중인 transform 은 물리보다 최대 1틱 뒤처진다(대시 0.3m 안팎).
    /// </summary>
    public static void PlayerPose(Component player, out Vector3 position, out Quaternion rotation)
    {
        if (player.TryGetComponent(out Rigidbody body))
        {
            position = body.position;
            rotation = body.rotation;
            return;
        }

        var t = player.transform;
        position = t.position;
        rotation = t.rotation;
    }

    /// <summary>
    /// 문짝에 kinematic Rigidbody 를 붙인다(이미 있으면 재사용). 중력 끔·보간 없음 — 자세는 <see cref="Rigidbody.position"/> 으로 직접 넣는다.
    /// 비볼록 MeshCollider 는 non-kinematic 바디에 붙으면 오류라 붙이는 동안 콜라이더를 잠깐 끈다.
    /// </summary>
    public static Rigidbody EnsureKinematicBody(Transform leafTransform, Collider[] colliders)
    {
        if (leafTransform.TryGetComponent(out Rigidbody existing))
        {
            ConfigureKinematic(existing);
            return existing;
        }

        var wasEnabled = new bool[colliders.Length];
        for (var i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                continue;
            wasEnabled[i] = colliders[i].enabled;
            colliders[i].enabled = false;
        }

        var body = leafTransform.gameObject.AddComponent<Rigidbody>();
        ConfigureKinematic(body);

        for (var i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = wasEnabled[i];
        }

        return body;
    }

    private static void ConfigureKinematic(Rigidbody body)
    {
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
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

    /// <summary>
    /// 문짝이 월드 자세 (<paramref name="leafPosition"/>, <paramref name="leafRotation"/>)이고 플레이어 루트(<paramref name="root"/>)가
    /// 자세 (<paramref name="rootPosition"/>, <paramref name="rootRotation"/>)일 때 <see cref="LeafBox"/> 가 몸 캡슐(Y 축)과 겹치는가.
    /// 캡슐 중심은 루트 기준 상대 위치를 그 자세로 옮겨 잰다 — transform 이 물리보다 뒤처져도 물리 자세로 판정한다.
    /// </summary>
    public static bool BodyOverlapsLeafBox(Bounds leafBox, Vector3 leafPosition, Quaternion leafRotation, CapsuleCollider body,
        Transform root, Vector3 rootPosition, Quaternion rootRotation)
    {
        if (body == null || !body.enabled)
            return false;

        var bodyTransform = body.transform;
        var scale = bodyTransform.lossyScale;
        var offset = Quaternion.Inverse(root.rotation) * (bodyTransform.TransformPoint(body.center) - root.position);
        return LobbyProximityDoorRules.LeafBoxOverlapsUprightCapsule(leafBox, leafPosition, leafRotation,
            rootPosition + rootRotation * offset, body.height * Mathf.Abs(scale.y),
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

            PlayerPose(player, out var playerPosition, out _);
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
