using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using Unity.Behavior;

public class JumpController : NetworkBehaviour
{
    [SerializeField] BehaviorGraphAgent bt;
    [SerializeField] ColliderInfo colliderInfo;
    [SerializeField] GameObject signObject;
    [SerializeField] string followTargetTag;
    [SerializeField] LayerMask playerLayer;
    [SerializeField] int damage;

    BlackboardVariable<Vector3> LandingPoint;

    GameObject _target;
    Quaternion _baseRotation = Quaternion.identity;
    float _offset = 0.01f;
    bool _isJumping = false;

    public override void OnNetworkSpawn()
    {
        InitializeLocal();
        _baseRotation = signObject.transform.rotation;

        if (!IsServer) return;
        EnableObjectClientRpc(false);

        if (!bt.BlackboardReference.GetVariable<Vector3>("LandingPoint", out LandingPoint))
        {
            Debug.LogError("Blackboard variable 'LandingPoint' not found.", this);
        }
    }


    void Update()
    {
        if (!_isJumping || !IsServer) return;

        Move();
    }


    HashSet<GameObject> players = new HashSet<GameObject>();
    public void SetTarget()
    {
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
        if (Physics.Raycast(closestObject.transform.position, Vector3.down, out hit, Mathf.Infinity))
        {
            slopeRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
        }
        signObject.transform.rotation = _baseRotation * slopeRotation;

        Vector3 landingPos = _target.transform.position;
        landingPos.y = 0f;
        LandingPoint.Value = landingPos;

        Move();

        _isJumping = true;
        players.Clear();
        EnableObjectClientRpc(true);
    }

    void Move()
    {
        // 위치 이동
        signObject.transform.position = LandingPoint.Value;
        Vector3 up = signObject.transform.forward;
        Vector3 offset = up * _offset;
        signObject.transform.position += offset;
    }


    Collider[] results = new Collider[16];
    HashSet<Player> damagedPlayers = new HashSet<Player>();
    public void OnLanded()
    {
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

            Player player = hitCollider.GetComponentInParent<Player>();
            if (player == null)
            {
                Debug.LogError("해당 플레이어는 Player 컴포넌트를 부착하고 있지 않습니다.");
                continue;
            }

            if (!damagedPlayers.Add(player))
                continue;

            player.TakeDamage(damage);
        }

        Initialize();
    }

    void Initialize()
    {
        InitializeLocal();

        if (!IsServer)
            return;

        EnableObjectClientRpc(false);
    }

    void InitializeLocal()
    {
        _isJumping = false;
        _target = null;
        signObject.SetActive(false);
    }

    [ClientRpc]
    void EnableObjectClientRpc(bool enable)
    {
        signObject.SetActive(enable);
    }
}
