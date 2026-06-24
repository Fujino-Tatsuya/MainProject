using UnityEngine;
using Unity.Netcode;
using Unity.Behavior;
using Unity.VisualScripting;

public class GrabController : NetworkBehaviour
{
    [SerializeField] Transform grabSocket;
    [SerializeField] ColliderInfo grabColliderInfo;
    [SerializeField] LayerMask targetMask;
    [SerializeField] BehaviorGraphAgent bt;

    [Header("데미지, 주기, 던지기 방향")]
    [SerializeField] int grabDamage;
    [SerializeField] int holdDamage;
    [SerializeField] float holdAttackPeriod;
    [SerializeField] int landingDamage;
    [SerializeField] Vector3 throwDirection;
    [SerializeField] float throwStrength;

    const int _maxPlayer = 3;
    Collider[] results = new Collider[_maxPlayer];

    int _resultCount;

    BlackboardVariable<bool> IsGrabbed;
    BlackboardVariable<GameObject> GrabbedPlayer;
    BlackboardVariable<TwentyThreeState> CurrentState;

    PlayerGrabController _playerGrabController;

    float _holdTimer = 0f;

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
        }

        if (!bt.BlackboardReference.GetVariable("IsGrabbed", out IsGrabbed))
        {
            Debug.LogError("Blackboard variable 'IsGrabbed' not found.", this);
        }

        if (!bt.BlackboardReference.GetVariable("GrabbedPlayer", out GrabbedPlayer))
        {
            Debug.LogError("Blackboard variable 'GrabbedPlayer' not found.", this);
        }

        if(!bt.BlackboardReference.GetVariable("CurrentState", out CurrentState))
        {
            Debug.LogError("Blackboard variable 'CurrentState' not found.", this);
        }
    }

    void Update()
    {
        if (CurrentState.Value == TwentyThreeState.Hold)
        {
            _holdTimer += Time.deltaTime;
            if (_holdTimer >= holdAttackPeriod)
            {
                if (_playerGrabController == null)
                {
                    Debug.LogError("해당 플레이어에 PlayerGrabController컴포넌트가 부착되어 있지 않습니다.");
                    return;
                }

                _playerGrabController.ApplyHoldDamage(holdDamage);
                _holdTimer = 0f;
            }
        }
    }

    /// <summary>
    /// [애니메이션 이벤트]: 설정된 ColliderInfo의 콜라이더 형태에 맞춰 주변 대상 콜라이더를 감지합니다.
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
        switch (overlapCollider)
        {
            case OverlapCollider.Box:
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
                    break;
            }

            case OverlapCollider.Sphere:
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
                    break;
            }

            case OverlapCollider.Capsule:
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
                    break;
            }

        }

        UpdateBlackboard();
        CallPlayerBeginGrab();
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

        if (isGrabbed)
        {
            Debug.Log("그랩 성공!");
        }
        else 
        {
            Debug.Log("그랩 실패!");
        }
    }

    void CallPlayerBeginGrab()
    {
        if (IsGrabbed.Value == false || GrabbedPlayer.Value == null) return;

        GameObject player = GrabbedPlayer.Value;

        _playerGrabController = player.GetComponent<PlayerGrabController>();
        if (_playerGrabController == null)
        {
            Debug.LogError("해당 플레이어에 PlayerGrabController컴포넌트가 부착되어 있지 않습니다.");
            Clear();
            return;
        }

        _playerGrabController.BeginGrab(grabSocket, grabDamage);
    }

    /// <summary>
    /// [애니메이션 이벤트]: 지정 방향으로 플레이어를 던집니다.
    /// </summary>
    public void Throw()
    {
        if (!IsServer) return;

        Vector3 worldDir = grabSocket.transform.TransformDirection(throwDirection);

        if (_playerGrabController == null)
        {
            Debug.LogError("해당 플레이어에 PlayerGrabController컴포넌트가 부착되어 있지 않습니다.");
            return;
        }
        _playerGrabController.Throw(worldDir * throwStrength, landingDamage);

        Clear();
    }

    void Clear()
    {
        IsGrabbed.Value = false;
        GrabbedPlayer.Value = null;
        _playerGrabController = null;
        _holdTimer = 0f;
    }
}
