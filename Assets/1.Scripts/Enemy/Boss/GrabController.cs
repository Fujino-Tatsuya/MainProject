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
        if (!IsServer)
            return;

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

        GameObject grabbedPlayer = null;
        for (int i = 0; i < _resultCount; i++)
        {
            Player player = results[i] != null ? results[i].GetComponentInParent<Player>() : null;
            if (player == null)
                continue;

            grabbedPlayer = player.gameObject;
            break;
        }

        bool isGrabbed = grabbedPlayer != null;
        IsGrabbed.Value = isGrabbed;
        GrabbedPlayer.Value = grabbedPlayer;

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

        _targetPlayer = GrabbedPlayer.Value.GetComponent<Player>();
        if (_targetPlayer == null)
        {
            Debug.LogError("해당 대상에 Player 컴포넌트가 부착되어 있지 않습니다.", this);
            Clear();
            return;
        }

        _targetUnit = _targetPlayer;
        if (!_targetPlayer.BeginGrabbedByInstigator(gameObject))
        {
            Debug.LogWarning("서버가 플레이어의 Grabbed 상태 진입을 거부했습니다.", this);
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

        ApplyDamage(landingDamagePercentage);
        if (_targetPlayer != null)
            _targetPlayer.EndGrabbedByInstigator();
        Clear();
    }

    public Transform GrabSocket => grabSocket;

    void Clear()
    {
        IsGrabbed.Value = false;
        GrabbedPlayer.Value = null;
        _targetUnit = null;
        _targetPlayer = null;
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
        _targetUnit.TakeDamage(new AttackInfo(damage));
    }
}
