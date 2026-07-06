using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using Unity.Behavior;
using Unity.Netcode.Components;

public class JumpController : NetworkBehaviour
{
    [SerializeField] BehaviorGraphAgent bt;
    [SerializeField] ColliderInfo colliderInfo;
    [SerializeField] SpriteRenderer signObject;
    [SerializeField] string followTargetTag;
    [SerializeField] LayerMask playerLayer;
    [SerializeField] int damage;

    BlackboardVariable<Vector3> ArrivePoint;

    GameObject _target;
    Quaternion _baseRotation = Quaternion.identity;
    Vector3 _signPos;
    Quaternion _signRot;
    float _offset = 0.01f;
    bool _isJumping = false;

    public override void OnNetworkSpawn()
    {
        Initialize();
        _baseRotation = signObject.transform.rotation;

        if (!IsServer) return;

        if (!bt.BlackboardReference.GetVariable<Vector3>("ArrivePoint", out ArrivePoint))
        {
            Debug.LogError("Blackboard variable 'ArrivePoint' not found.", this);
        }
    }


    void LateUpdate()
    {
        if (!_isJumping) return;

        signObject.transform.SetPositionAndRotation(_signPos, _signRot);
    }


    HashSet<GameObject> players = new HashSet<GameObject>();
    public void SetTarget()
    {
        if (!IsServer) return;

        GameObject[] gameObjects = GameObject.FindGameObjectsWithTag(followTargetTag);
        float closestDistance = Mathf.Infinity;
        GameObject closestObject = null;

        foreach (GameObject gameObject in gameObjects)
        {
            if (!players.Add(gameObject.transform.root.gameObject)) continue;

            float distanceSq = Vector3.SqrMagnitude(gameObject.transform.root.position - transform.position);
            if (closestDistance > distanceSq || closestObject == null)
            {
                closestDistance = distanceSq;
                closestObject = gameObject.transform.root.gameObject;
            }
        }

        if (closestObject == null)
        {
            Debug.LogError($"{followTargetTag} 태그를 가진 오브젝트가 존재하지 않습니다.");
            Initialize();
            return;
        }
        _target = closestObject;

        // 경사면을 고려한 회전 변경
        RaycastHit hit;
        Quaternion slopeRotation = Quaternion.identity;
        if (Physics.Raycast(closestObject.transform.position, Vector3.down, out hit, Mathf.Infinity, LayerMask.GetMask("Ground")))
        {
            slopeRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
        }
        signObject.transform.rotation = _baseRotation * slopeRotation;
        _signRot = signObject.transform.rotation;

        Vector3 landingPos = _target.transform.position;
        landingPos.y = 0f;
        ArrivePoint.Value = landingPos;

        Move();

        _isJumping = true;
        players.Clear();
        ShowSignClientRpc(_signPos, _signRot);
    }

    void Move()
    {
        // 위치 이동
        signObject.transform.position = ArrivePoint.Value;
        Vector3 up = signObject.transform.forward;
        Vector3 offset = up * _offset;
        signObject.transform.position += offset;
        _signPos = signObject.transform.position;
    }


    Collider[] results = new Collider[16];
    HashSet<Unit> damagedPlayers = new HashSet<Unit>();
    public void OnLanded()
    {
        if (!IsServer) return;

        SphereColliderInfo sphereInfo = new SphereColliderInfo();
        colliderInfo.GetSphereColliderInfo(ref sphereInfo);

        int hitCount = Physics.OverlapSphereNonAlloc(
            sphereInfo.center,
            sphereInfo.radius,
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
                Debug.LogError("해당 플레이어는 Unit 컴포넌트를 부착하고 있지 않습니다.");
                continue;
            }

            if (!damagedPlayers.Add(unit))
                continue;

            unit.TakeDamage(new AttackInfo(damage));
        }

        HideSignClientRpc();
    }

    void Initialize()
    {
        _isJumping = false;
        _target = null;
        signObject.enabled = false;
    }

    [ClientRpc]
    void ShowSignClientRpc(Vector3 position, Quaternion rotation)
    {
        _signPos = position;
        _signRot = rotation;
        _isJumping = true;

        signObject.transform.SetPositionAndRotation(position, rotation);
        signObject.enabled = true;
    }

    [ClientRpc]
    void HideSignClientRpc()
    {
        _isJumping = false;
        signObject.enabled = false;
    }
}
