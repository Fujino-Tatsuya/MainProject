using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 로비 문 — 살아 있는 플레이어가 다가가면 열리고 전원 멀어지면 닫힌다(할 일 78).
/// <list type="bullet">
/// <item>판정: <see cref="LobbyProximityDoorRules"/>(열림 반경 &lt; 닫힘 반경 히스테리시스). 대상은
/// <see cref="PlayerLifeCycleController.Active"/> 중 스폰된 <see cref="PlayerLifeState.Alive"/> — 매 프레임 씬을 뒤지지 않는다.</item>
/// <item>네트워크: 동기화 없음. 연출이라 각 피어가 자기 화면의 플레이어 위치로 따로 판정한다
/// (위치는 이미 NetworkTransform 으로 복제되므로 피어끼리 상태가 거의 같다).</item>
/// <item>회전: <see cref="leaf"/> 를 Awake 때의 로컬 회전(닫힘) 기준으로 로컬 Y 축으로 돌린다.
/// <c>PF_Room_DoorSingle_01_v003</c> 은 메쉬 피벗이 힌지 모서리(문짝은 로컬 -X 쪽 약 1.27m)라 루트를 그대로 돌린다.
/// 같은 오브젝트의 MeshCollider 도 함께 돌아 열린 문 사이로 지나갈 수 있다.</item>
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
    [Tooltip("이 평면 거리(m) 안에 살아 있는 플레이어가 들어오면 연다.")]
    [SerializeField, Min(0f)] private float openRadius = 1.8f;
    [Tooltip("열린 뒤 전원이 이 평면 거리(m) 밖으로 나가야 닫는다. 열림 반경 이상이어야 한다.")]
    [SerializeField, Min(0f)] private float closeRadius = 2.6f;
    [Tooltip("다 열렸을 때 로컬 Y 회전 각도(도). 부호로 여는 방향을 고른다.")]
    [SerializeField, Range(-180f, 180f)] private float openAngle = 90f;
    [Tooltip("닫힘 → 다 열림까지 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float openSeconds = 0.35f;
    [Tooltip("다 열림 → 닫힘까지 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float closeSeconds = 0.5f;

    private Quaternion _closedLocalRotation;
    private Vector3 _center;
    private bool _isOpen;
    private float _progress;

    public bool IsOpen => _isOpen;

    private Transform Leaf => leaf != null ? leaf : transform;

    private void Awake()
    {
        _closedLocalRotation = Leaf.localRotation;
        _center = Leaf.TransformPoint(centerOffset);
    }

    private void Update()
    {
        _isOpen = LobbyProximityDoorRules.NextOpen(_isOpen, NearestAlivePlayerSqrDistance(), openRadius, closeRadius);

        var next = LobbyProximityDoorRules.StepProgress(_progress, _isOpen, openSeconds, closeSeconds, Time.deltaTime);
        if (Mathf.Approximately(next, _progress))
            return;

        _progress = next;
        Leaf.localRotation = _closedLocalRotation * Quaternion.Euler(0f, openAngle * Mathf.SmoothStep(0f, 1f, _progress), 0f);
    }

    private float NearestAlivePlayerSqrDistance()
    {
        var nearest = float.PositiveInfinity;
        var players = PlayerLifeCycleController.Active;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null || !player.IsSpawned || player.State != PlayerLifeState.Alive)
                continue;

            var sqr = LobbyProximityDoorRules.PlanarSqrDistance(player.transform.position, _center);
            if (sqr < nearest)
                nearest = sqr;
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
