using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using Unity.Behavior;

public class JumpController : NetworkBehaviour
{
    [SerializeField] BehaviorGraphAgent bt;
    [SerializeField] string followTargetTag;
    [SerializeField] LayerMask playerLayer;
    [SerializeField] int damage;
    [SerializeField] float jumpingTime = 0.5f; // 허공에 정지해있는 시간 (= 장판2 성장 시간)
    [SerializeField] List<GameObject> meshRenderer;

    [Header("장판")]
    [SerializeField] Transform floorRoot;      // 두 장판을 담는 위치 기준 컨테이너
    [SerializeField] SpriteRenderer floorBase;  // 장판1: 크기 고정 기준 + 데미지 범위 기준
    [SerializeField] FloorAreaEffect floorGrow; // 장판2: 0.1 → 장판1 크기로 시간 점증

    [Header("장판 시간 보정을 위한 변수")]
    [SerializeField] Animator animator;
    [SerializeField] string animClip;
    [SerializeField] string multiplier;
    [SerializeField] float clipStart = 0f;
    [SerializeField] float clipEnd = 100f;

    BlackboardVariable<Vector3> ArrivePoint;
    BlackboardVariable<float> JumpingTime;

    SpriteRenderer _floorGrowRenderer;

    GameObject _target;
    Quaternion _baseRotation = Quaternion.identity;
    Vector3 _floorRootPos;
    Quaternion _floorRootRot;
    float _jumpDiff;    // 장판 시간 계산으로 위해 총 정지 시간에서 더할 보정값
    bool _isJumping = false;
    bool _isCinematicLanding = false;

    /// <summary>
    /// 등장 연출 착지 모드. BossEncounterDirector가 하강 전에 켜고 전투 전환 시 끈다.
    /// 켜져 있는 동안 장판 표시와 착지 피해를 만들지 않는다 — 연출 착지는 공격이 아니다.
    /// (승인 계획 Task 4)
    /// </summary>
    public void SetCinematicLandingMode(bool enabled)
    {
        if (!IsServer && IsSpawned) return;

        _isCinematicLanding = enabled;
    }

    public override void OnNetworkSpawn()
    {
        _baseRotation = floorRoot.rotation;
        _floorGrowRenderer = floorGrow.GetComponent<SpriteRenderer>();
        Initialize();

        if (!IsServer) return;

        if (jumpingTime <= 0)
        {
            Edit.LogError("[No.23] 'jumpingTime' is below than 0.", this);
        }

        if (!bt.BlackboardReference.GetVariable<Vector3>("ArrivePoint", out ArrivePoint))
        {
            Edit.LogError("[No.23] Blackboard variable 'ArrivePoint' not found.", this);
        }

        if (!bt.BlackboardReference.GetVariable<float>("JumpingTime", out JumpingTime))
        {
            Edit.LogError("[No.23] Blackboard variable 'JumpingTime' not found.", this);
        }

        JumpingTime.Value = jumpingTime;

        _jumpDiff = AnimClipUtility.GetPlayTime(animator, animClip, multiplier, clipStart, clipEnd);
    }


    void LateUpdate()
    {
        if (!_isJumping) return;
        // 보스 이동으로 인한 장판 위치 보정
        floorRoot.SetPositionAndRotation(_floorRootPos, _floorRootRot);
    }

    public void SetTarget()
    {
        if (!IsServer) return;

        // 연출 착지는 대상 선정·장판·메시 숨김을 하지 않는다.
        if (_isCinematicLanding) return;

        GameObject target = FindTargetByDistance(true);

        if (target == null)
        {
            Edit.LogError($"[No.23] {followTargetTag} 태그를 가진 오브젝트가 존재하지 않습니다.");
            Initialize();
            return;
        }
        _target = target;

        // 경사면을 고려한 회전 변경
        RaycastHit hit;
        Quaternion slopeRotation = Quaternion.identity;
        if (Physics.Raycast(target.transform.position, Vector3.down, out hit, Mathf.Infinity, LayerMask.GetMask("Ground")))
        {
            slopeRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
        }
        _floorRootRot = _baseRotation * slopeRotation;

        Vector3 landingPos = _target.transform.position;
        landingPos.y = 0f;
        ArrivePoint.Value = landingPos;
        _floorRootPos = landingPos;

        _isJumping = true;
        ShowFloorsClientRpc(_floorRootPos, _floorRootRot);
        ShowMyMeshClientRpc(false);
    }


    HashSet<GameObject> players = new HashSet<GameObject>();
    /// <summary>
    /// 후보 오브젝트 중 자신과의 거리가 가장 먼(또는 가장 가까운) 오브젝트를 반환한다.
    /// </summary>
    /// <param name="findFarthest">true면 가장 먼 대상, false면 가장 가까운 대상 반환</param>
    GameObject FindTargetByDistance(bool findFarthest)
    {
        GameObject[] gameObjects = GameObject.FindGameObjectsWithTag(followTargetTag);

        // 중복 제거: 같은 루트 오브젝트를 한 번만 후보로 등록
        players.Clear();
        foreach (GameObject gameObject in gameObjects)
        {
            players.Add(gameObject.transform.root.gameObject);
        }

        float bestDistanceSq = findFarthest ? -1f : Mathf.Infinity;
        GameObject bestObject = null;

        foreach (GameObject player in players)
        {
            if (player == null) continue;

            float distanceSq = Vector3.SqrMagnitude(player.transform.position - transform.position);
            bool isBetter = findFarthest ? distanceSq > bestDistanceSq : distanceSq < bestDistanceSq;
            if (bestObject == null || isBetter)
            {
                bestDistanceSq = distanceSq;
                bestObject = player;
            }
        }

        return bestObject;
    }


    Collider[] results = new Collider[16];
    HashSet<Unit> damagedPlayers = new HashSet<Unit>();
    public void OnLanded()
    {
        if (!IsServer) return;

        // 연출 착지는 피해를 주지 않는다. 장판도 켜지지 않았으므로 숨김 처리도 불필요.
        if (_isCinematicLanding) return;

        // 데미지 범위는 장판1(floorBase)의 실제 시각 크기 기준
        Vector3 center = floorBase.bounds.center;
        float radius = Mathf.Max(floorBase.bounds.extents.x, floorBase.bounds.extents.z);

        int hitCount = Physics.OverlapSphereNonAlloc(
            center,
            radius,
            results,
            playerLayer,
            QueryTriggerInteraction.Ignore
        );

        damagedPlayers.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = results[i];
            if (hitCollider == null)
                continue;

            Unit unit = hitCollider.GetComponent<Unit>();
            if (unit == null)
            {
                Edit.LogError("[No.23] 해당 플레이어는 Unit 컴포넌트를 부착하고 있지 않습니다.");
                continue;
            }

            if (!damagedPlayers.Add(unit))
                continue;

            unit.TakeDamage(new AttackInfo(damage));
        }

        HideFloorsClientRpc();
    }

    void EnableMeshRenderers(bool enable)
    {
        foreach (GameObject mesh in meshRenderer)
        {
            mesh.SetActive(enable);
        }
    }

    void SetFloorsEnable(bool enable)
    {
        floorBase.enabled = enable;
        _floorGrowRenderer.enabled = enable;
    }

    void Initialize()
    {
        _isJumping = false;
        _target = null;
        SetFloorsEnable(false);
    }

    [ClientRpc]
    public void ShowMyMeshClientRpc(bool enable)
    {
        EnableMeshRenderers(enable);
    }

    [ClientRpc]
    void ShowFloorsClientRpc(Vector3 position, Quaternion rotation)
    {
        _floorRootPos = position;
        _floorRootRot = rotation;
        _isJumping = true;

        floorRoot.SetPositionAndRotation(position, rotation);
        SetFloorsEnable(true);

        // 장판2: 0.1(prefab 시작 크기) → 장판1 크기까지 jumpingTime 동안 성장
        floorGrow.StartOverTimeGrow(jumpingTime + _jumpDiff, floorBase.transform.localScale);
    }

    [ClientRpc]
    void HideFloorsClientRpc()
    {
        _isJumping = false;
        SetFloorsEnable(false);
    }
}
