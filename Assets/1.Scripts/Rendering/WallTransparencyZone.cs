using System;
using System.Collections.Generic;
using UnityEngine;
using VeyTrace.Rendering.Occlusion;

// 트리거 박스 안에 점유자가 몇 "명" 있는지만 세고, 0명↔1명 경계에서 벽 그룹에 켜/꺼를 알린다.
//
// 이 컴포넌트는 플레이어에 대해 아무것도 모른다. Player·Unit·NetworkObject 어느 것도 참조하지
// 않는다 — 박스 안에 지정 레이어 콜라이더가 있느냐가 전부다. 그래서 테스트 씬에서 레이어만 바꾼
// 캡슐 하나로 검증할 수 있고, 네트워크를 띄울 필요가 없다.
//
// 판정은 클라이언트마다 따로 돈다. 상태를 만들지도 보내지도 않으므로 클라이언트끼리 결과가
// 달라도 상관없다.
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class WallTransparencyZone : MonoBehaviour
{
    private const float PruneInterval = 0.5f;

    [Header("감지")]
    [Tooltip("점유자로 셀 콜라이더 레이어. 기본값 Player(6). 이 레이어를 쓰는 것은 플레이어 프리팹뿐이다.")]
    [SerializeField] private LayerMask occupantLayers = 1 << 6;

    [Header("벽 그룹")]
    [Tooltip("이 구역이 점유될 때 함께 투명해질 그룹. 여러 구역이 같은 그룹을 가리켜도 된다(OR).")]
    [SerializeField] private WallTransparencyGroup[] targetGroups =
        Array.Empty<WallTransparencyGroup>();

    // 점유자를 루트 단위로 센다.
    //
    // 플레이어 프리팹에는 layer 6 BoxCollider 가 6개 더 있지만(AAC1~4·MainSkill·InterruptAttack)
    // 전부 ColliderInfo 가 붙어 있고, ColliderInfo 는 Awake 에서 자기 콜라이더를 끈다 —
    // 그것들은 물리 참여자가 아니라 OverlapAttack 이 읽는 모양 자료다. 그래서 런타임에 layer 6
    // 콜라이더는 루트 캡슐 하나뿐이고, 한 명이 여러 번 세어질 일이 없다.
    // 그래도 키를 루트로 접는 이유는 앞으로 콜라이더가 늘어도 인원수가 틀어지지 않게 하기 위해서다.
    private readonly HashSet<Transform> _occupants = new HashSet<Transform>();
    private readonly HashSet<WallTransparencyGroup> _uniqueGroups =
        new HashSet<WallTransparencyGroup>();
    private float _nextPruneTime;
    private bool _groupsEngaged;

    private void Awake()
    {
        RebuildGroupSet();
    }

    private void OnEnable()
    {
        _nextPruneTime = Time.time + PruneInterval;
    }

    private void OnDisable()
    {
        _occupants.Clear();
        SetGroupsEngaged(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!TryResolveOccupant(other, out Transform key))
            return;

        if (_occupants.Add(key))
            UpdateEngagement();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!TryResolveOccupant(other, out Transform key))
            return;

        if (_occupants.Remove(key))
            UpdateEngagement();
    }

    private void Update()
    {
        if (_occupants.Count == 0 || Time.time < _nextPruneTime)
            return;

        _nextPruneTime = Time.time + PruneInterval;
        Prune();
    }

    // 파괴·디스폰·비활성화에서는 Exit 가 오지 않는다. 죽은 점유자를 들고 있으면 구역이 영영
    // 점유 상태로 남으므로 주기적으로 털어낸다.
    private void Prune()
    {
        if (_occupants.RemoveWhere(IsGone) > 0)
            UpdateEngagement();
    }

    private static bool IsGone(Transform occupant)
    {
        return occupant == null || !occupant.gameObject.activeInHierarchy;
    }

    private bool TryResolveOccupant(Collider other, out Transform key)
    {
        key = null;

        if (other == null || (occupantLayers.value & (1 << other.gameObject.layer)) == 0)
            return false;

        // Rigidbody 가 있으면 그게 물리 단위의 주인이다(Paladin 은 루트에 있다). 없으면 계층
        // 루트를 쓴다 — 테스트 씬의 맨 캡슐도 이 경로로 자기 자신이 키가 된다.
        key = other.attachedRigidbody != null
            ? other.attachedRigidbody.transform
            : other.transform.root;

        return key != null;
    }

    private void UpdateEngagement()
    {
        SetGroupsEngaged(_occupants.Count > 0);
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
        GetComponent<BoxCollider>().isTrigger = true;
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
