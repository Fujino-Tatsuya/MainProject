using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VeyTrace.Rendering.Occlusion;

// 플레이어가 구역 안에 있는지만 로컬에서 감지하고, 벽의 표현은 Occlusion 어셈블리에 맡긴다.
// 네트워크 상태를 만들거나 전송하지 않으므로 각 클라이언트의 판정이 서로 달라도 괜찮다.
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class WallTransparencyZone : MonoBehaviour
{
    private const float PruneInterval = 0.5f;

    [Header("감지")]
    [Tooltip("플레이어 콜라이더 레이어. 기본값은 Player(6)이다.")]
    [SerializeField] private LayerMask playerLayers = 1 << 6;

    [Tooltip("켜면 이 클라이언트가 소유한 플레이어만 센다. 끄면 모든 플레이어를 센다.")]
    [SerializeField] private bool localPlayerOnly;

    [Header("벽 그룹")]
    [SerializeField] private WallTransparencyGroup[] targetGroups =
        Array.Empty<WallTransparencyGroup>();

    private readonly HashSet<Transform> _insideRoots = new HashSet<Transform>();
    private readonly HashSet<WallTransparencyGroup> _uniqueGroups =
        new HashSet<WallTransparencyGroup>();
    private BoxCollider _box;
    private float _nextPruneTime;
    private bool _groupsEngaged;

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
        RebuildGroupSet();
    }

    private void OnEnable()
    {
        _nextPruneTime = Time.time + PruneInterval;
    }

    private void OnDisable()
    {
        _insideRoots.Clear();
        SetGroupsEngaged(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!TryResolvePlayer(other, out Transform root, out Player player))
            return;

        if (player.CurrentHealth <= 0 || !PassesOwnerFilter(player))
            return;

        if (_insideRoots.Add(root) && _insideRoots.Count == 1)
            SetGroupsEngaged(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!TryResolvePlayer(other, out Transform root, out Player player))
            return;

        if (!PassesOwnerFilter(player))
            return;

        // 공격 히트박스 하나가 꺼져 Exit가 와도 플레이어 본체가 아직 박스 안이면 유지한다.
        if (_box.bounds.Contains(root.position))
            return;

        if (_insideRoots.Remove(root) && _insideRoots.Count == 0)
            SetGroupsEngaged(false);
    }

    private void Update()
    {
        if (_insideRoots.Count == 0 || Time.time < _nextPruneTime)
            return;

        _nextPruneTime = Time.time + PruneInterval;

        // 사망·디스폰·비활성화에서는 Exit가 오지 않을 수 있으므로 주기적으로 실제 점유를 고친다.
        int removed = _insideRoots.RemoveWhere(ShouldPrune);
        if (removed > 0 && _insideRoots.Count == 0)
            SetGroupsEngaged(false);
    }

    private bool ShouldPrune(Transform root)
    {
        if (root == null || !root.gameObject.activeInHierarchy)
            return true;

        Player player = root.GetComponent<Player>();
        if (player == null || player.CurrentHealth <= 0)
            return true;

        if (localPlayerOnly && !PassesOwnerFilter(player))
            return true;

        return !_box.bounds.Contains(root.position);
    }

    private bool TryResolvePlayer(Collider other, out Transform root, out Player player)
    {
        root = null;
        player = null;

        if (other == null || (playerLayers.value & (1 << other.gameObject.layer)) == 0)
            return false;

        // attachedRigidbody가 있으면 물리 루트를 우선한다. 없으면 계층 루트를 써서
        // Paladin의 캡슐과 공격 히트박스 6개가 모두 플레이어 하나로 합쳐지게 한다.
        Transform physicsRoot = other.attachedRigidbody != null
            ? other.attachedRigidbody.transform
            : other.transform.root;

        player = physicsRoot.GetComponent<Player>();
        if (player == null)
            player = other.GetComponentInParent<Player>();
        if (player == null)
            return false;

        // 히트박스에 별도 Rigidbody가 추가되어도 카운트 키는 항상 Player 루트 하나다.
        root = player.transform;
        return true;
    }

    private bool PassesOwnerFilter(Player player)
    {
        if (!localPlayerOnly)
            return true;

        NetworkObject networkObject = player.GetComponent<NetworkObject>();
        return networkObject != null && networkObject.IsOwner;
    }

    private void SetGroupsEngaged(bool engaged)
    {
        if (_groupsEngaged == engaged)
            return;

        _groupsEngaged = engaged;
        foreach (WallTransparencyGroup group in _uniqueGroups)
        {
            if (group == null)
                continue;

            if (engaged)
                group.AcquireTransparency();
            else
                group.ReleaseTransparency();
        }
    }

    private void RebuildGroupSet()
    {
        _uniqueGroups.Clear();
        if (targetGroups == null)
            return;

        for (int i = 0; i < targetGroups.Length; i++)
        {
            if (targetGroups[i] != null)
                _uniqueGroups.Add(targetGroups[i]);
        }
    }

    private void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    private void OnValidate()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
            box.isTrigger = true;

#if UNITY_EDITOR
        if (targetGroups == null)
            return;

        var seen = new HashSet<WallTransparencyGroup>();
        for (int i = 0; i < targetGroups.Length; i++)
        {
            WallTransparencyGroup group = targetGroups[i];
            if (group != null && !seen.Add(group))
            {
                Debug.LogWarning(
                    $"[WallTransparencyZone] '{group.name}' 그룹이 목록에 중복되어 있습니다. " +
                    "런타임에는 한 번만 호출합니다.",
                    this);
            }
        }
#endif
    }
}
