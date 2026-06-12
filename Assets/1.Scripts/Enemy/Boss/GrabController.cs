using UnityEngine;
using Unity.Netcode;
using Unity.Behavior;

public class GrabController : NetworkBehaviour
{
    [SerializeField] Transform grabSocket;
    [SerializeField] ColliderInfo grabColliderInfo;
    [SerializeField] LayerMask targetMask;
    [SerializeField] BehaviorGraphAgent bt;

    const int _maxPlayer = 3;
    Collider[] results = new Collider[_maxPlayer];

    int _resultCount;

    BlackboardVariable<bool> IsGrabbed;
    BlackboardVariable<GameObject> GrabbedPlayer;

    void Start()
    {
        if (bt == null)
        {
            Debug.LogError("BehaviorTree is null.", this);
            return;
        }

        if (bt.BlackboardReference == null)
        {
            Debug.LogError("BlackboardReference is null.", this);
            return;
        }

        if (!bt.BlackboardReference.GetVariable("IsGrabbed", out IsGrabbed))
        {
            Debug.LogError("Blackboard variable 'IsGrabbed' not found.", this);
            return;
        }

        if (!bt.BlackboardReference.GetVariable("GrabbedPlayer", out GrabbedPlayer))
        {
            Debug.LogError("Blackboard variable 'GrabbedPlayer' not found.", this);
            return;
        }
    }

    /// <summary>
    /// 설정된 ColliderInfo의 콜라이더 형태에 맞춰 주변 대상 콜라이더를 감지합니다.
    /// </summary>
    public void Detect()
    {
        _resultCount = 0;
        if (grabColliderInfo == null)
        {
            Debug.LogError("Grab ColliderInfo is null.", this);
            return;
        }

        if (results == null || results.Length == 0)
            results = new Collider[16];

        OverlapCollider overlapCollider = grabColliderInfo.OverlapCollider;
        if (overlapCollider.Equals(OverlapCollider.Box))
        {
            BoxColliderInfo info = new BoxColliderInfo();
            grabColliderInfo.GetBoxColliderInfo(ref info);
            _resultCount = Physics.OverlapBoxNonAlloc(
                info.center,
                info.halfExtents,
                results,
                info.orientation,
                targetMask,
                QueryTriggerInteraction.Ignore
            );
        }

        else if (overlapCollider.Equals(OverlapCollider.Capsule))
        {
            CapsuleColliderInfo info = new CapsuleColliderInfo();
            grabColliderInfo.GetCapsuleColliderInfo(ref info);
            _resultCount = Physics.OverlapCapsuleNonAlloc(
                info.point0,
                info.point1,
                info.radius,
                results,
                targetMask,
                QueryTriggerInteraction.Ignore
            );
        }

        else if (overlapCollider.Equals(OverlapCollider.Sphere))
        {
            SphereColliderInfo info = new SphereColliderInfo();
            grabColliderInfo.GetSphereColliderInfo(ref info);
            _resultCount = Physics.OverlapSphereNonAlloc(
                info.center,
                info.radius,
                results,
                targetMask,
                QueryTriggerInteraction.Ignore
            );
        }

        UpdateBlackboard();
    }

    void UpdateBlackboard()
    {
        if (IsGrabbed == null || GrabbedPlayer == null)
        {
            Debug.LogError("Grab blackboard variables are not initialized.", this);
            return;
        }

        bool isGrabbed = _resultCount > 0;
        IsGrabbed.Value = isGrabbed;
        GrabbedPlayer.Value = isGrabbed ? results[0].gameObject : null;
    }
}
