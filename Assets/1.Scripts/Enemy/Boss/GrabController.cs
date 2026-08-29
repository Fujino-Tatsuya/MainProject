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

    [Tooltip("grabSocket에서 아래로 바닥을 훑는 최대 거리(m). 소켓은 보스 손 높이라 이보다 멀면 바닥이 아니다")]
    [SerializeField, Min(0f)] float groundProbeDistance = 30f;

    // 바닥 탐색용 레이 버퍼. NonAlloc의 목적이 호출마다 배열을 새로 만들지 않는 것이므로 필드로 둔다.
    // 크기 8: 소켓 아래에 겹칠 수 있는 바닥/슬래브 수를 넉넉히 잡은 값이다. 버퍼가 꽉 차면
    // 유니티가 결과를 잘라내므로(정렬도 안 한다) 그 안에 최근접이 없을 수 있다 — 늘려야 하면 이 값을 키운다.
    readonly RaycastHit[] _groundHits = new RaycastHit[8];

    int grabDamagePercentage;
    int holdDamagePercentage;
    float holdAttackPeriod;
    int landingDamagePercentage;
    //[SerializeField] Vector3 throwDirection;
    //[SerializeField] float throwStrength;

    /// <summary>
    /// Grab 데미지 퍼센티지들과 홀드 주기를 외부에서 주입한다. (SO 종속 없이 값만 받음)
    /// </summary>
    public void SetGrabFigures(int grabPercentage, int holdPercentage, int landingPercentage, float attackPeriod)
    {
        grabDamagePercentage = Mathf.Max(0, grabPercentage);
        holdDamagePercentage = Mathf.Max(0, holdPercentage);
        landingDamagePercentage = Mathf.Max(0, landingPercentage);
        holdAttackPeriod = Mathf.Max(0f, attackPeriod);
    }

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
            Edit.LogError("[No.23] BehaviorTree is null.", this);
            return;
        }

        if (bt.BlackboardReference == null)
        {
            Edit.LogError("[No.23] BlackboardReference is null.", this);
        }

        if (!bt.BlackboardReference.GetVariable("IsGrabbed", out IsGrabbed))
        {
            Edit.LogError("[No.23] Blackboard variable 'IsGrabbed' not found.", this);
        }

        if (!bt.BlackboardReference.GetVariable("GrabbedPlayer", out GrabbedPlayer))
        {
            Edit.LogError("[No.23] Blackboard variable 'GrabbedPlayer' not found.", this);
        }

        if (!bt.BlackboardReference.GetVariable("CurrentState", out CurrentState))
        {
            Edit.LogError("[No.23] Blackboard variable 'CurrentState' not found.", this);
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
                    PlayGrabbedLightningVFXClientRpc();
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
            Edit.LogError("[No.23] Grab ColliderInfo is null.", this);
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
            Edit.LogError("[No.23] Grab blackboard variables are not initialized.", this);
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
            Edit.Log("[No.23] 그랩 성공!");
        }
        else
        {
            Edit.Log("[No.23] 그랩 실패!");
        }
    }

    void CallPlayerBeginGrab()
    {
        if (IsGrabbed.Value == false || GrabbedPlayer.Value == null) return;

        _targetPlayer = GrabbedPlayer.Value.GetComponent<Player>();
        if (_targetPlayer == null)
        {
            Edit.LogError("[No.23] 해당 대상에 Player 컴포넌트가 부착되어 있지 않습니다.", this);
            Clear();
            return;
        }

        _targetUnit = _targetPlayer;
        if (!_targetPlayer.BeginGrabbedByInstigator(gameObject))
        {
            Edit.LogWarning("[No.23] 서버가 플레이어의 Grabbed 상태 진입을 거부했습니다.", this);
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

        PlayThrowLightningVFXClientRpc();
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
            Edit.LogError("[No.23] 타겟 유닛이 null입니다.");
            return;
        }

        int damage = Mathf.RoundToInt(_targetHp * (percentage / 100f));
        _targetUnit.TakeDamage(new AttackInfo(damage));
    }

    [ClientRpc]
    public void PlayLightningVFXClientRpc()
    {
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        effects.Play(effects.Catalog.Grab_Lightning, transform.position, Quaternion.identity);
    }

    [ClientRpc]
    void PlayGrabbedLightningVFXClientRpc()
    {
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        effects.Play(effects.Catalog.Grabbed_Electric, grabSocket.transform.position, Quaternion.identity);
    }

    [ClientRpc]
    void PlayThrowLightningVFXClientRpc()
    {
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        // 바닥을 못 찾으면 소켓 위치에 그대로 재생한다 — 이펙트가 통째로 사라지는 것보다 낫다.
        Vector3 spawnPoint = grabSocket.position;
        Quaternion slopeRotation = Quaternion.identity;
        if (GroundProbe.TryFindGround(grabSocket.position, 0, out RaycastHit hit, out string report))
        {
            spawnPoint = hit.point;
            slopeRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
        }

        effects.Play(effects.Catalog.Throw_Lightning, spawnPoint, slopeRotation);
    }
}
