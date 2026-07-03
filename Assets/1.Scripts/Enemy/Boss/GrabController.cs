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
    [SerializeField] int grabDamagePercentage;
    [SerializeField] int holdDamagePercentage;
    [SerializeField] float holdAttackPeriod;
    [SerializeField] int landingDamagePercentage;
    //[SerializeField] Vector3 throwDirection;
    //[SerializeField] float throwStrength;

    const int _maxPlayer = 3;
    Collider[] results = new Collider[_maxPlayer];

    int _resultCount;

    BlackboardVariable<bool> IsGrabbed;
    BlackboardVariable<GameObject> GrabbedPlayer;
    BlackboardVariable<TwentyThreeState> CurrentState;

    Unit _targetUnit;
    Player _targetPlayer;
    Rigidbody _targetRigidbody;
    int _targetHp;

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
        if (IsOwner)
        {
            if (IsGrabbed.Value && GrabbedPlayer.Value != null)
            {
                // 플레이어를 잡고 있는 동안의 로직
                _targetRigidbody.MovePosition(grabSocket.position);
                _targetRigidbody.MoveRotation(grabSocket.rotation);
            }
        }

        if (IsServer)
        {
            if (CurrentState.Value == TwentyThreeState.Hold && _targetUnit != null)
            {
                _holdTimer += Time.deltaTime;
                if (_holdTimer >= holdAttackPeriod)
                {
                    ApplyDamage(holdDamagePercentage);
                    _holdTimer = 0f;
                }
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

        _targetUnit = player.GetComponent<Unit>();
        if (_targetUnit == null)
        {
            Debug.LogError("해당 플레이어에 Unit 컴포넌트가 부착되어 있지 않습니다.");
            Clear();
            return;
        }

        _targetRigidbody = player.GetComponent<Rigidbody>();
        if (_targetRigidbody == null)
        {
            Debug.LogError("해당 플레이어에 Rigidbody 컴포넌트가 부착되어 있지 않습니다.");
            Clear();
            return;
        }

        //_targetUnit.BeginGrab(grabSocket, grabDamage);
        // 플레이어 상태 전환 함수 호출하기
        _targetPlayer = player.GetComponent<Player>();
        if (_targetPlayer == null)
        {
            Debug.LogError("Grab target Player component is missing.", this);
            Clear();
            return;
        }

        if (!_targetPlayer.BeginGrabbedByInstigator(gameObject))
        {
            Clear();
            return;
        }

        _targetHp = _targetUnit.CurrentHealth;
        ApplyDamage(grabDamagePercentage);
    }

    /// <summary>
    /// [애니메이션 이벤트]: 지정 방향으로 플레이어를 던집니다.
    /// </summary>
    public void Throw()
    {
        if (!IsServer) return;

        //Vector3 worldDir = grabSocket.transform.TransformDirection(throwDirection);
        ApplyDamage(landingDamagePercentage);

        // 플레이어 상태 전환 함수 호출하기

        Clear();
    }

    void Clear()
    {
        if (_targetPlayer != null)
            _targetPlayer.EndGrabbedByInstigator();

        IsGrabbed.Value = false;
        GrabbedPlayer.Value = null;
        _targetUnit = null;
        _targetPlayer = null;
        _targetRigidbody = null;
        _holdTimer = 0f;
        _targetHp = 0;
    }


    /// <summary>
    /// 타겟 유닛에게 percentage만큼 피해를 입히는 함수입니다.
    /// </summary>
    /// <param name="percentage">피해를 입힐 퍼센트</param>
    void ApplyDamage(int percentage)
    {
        if (!IsServer) return;
        if (_targetUnit == null)
        {
            Debug.LogError("타겟 유닛이 null입니다.");
            return;
        }

        int damage = Mathf.RoundToInt(_targetHp * (percentage / 100f));
        _targetUnit.TakeDamage(damage);
    }
}
