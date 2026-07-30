using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 아레나(<c>bossroom.prefab</c>)가 <b>스스로 들고 다니는</b> 구성 요소 묶음.
///
/// 왜 필요한가: 예전에는 아레나의 부품(착지점·BossArea·충전 기둥 4개·도착 지점)을
/// <see cref="BossEncounterDirector"/>가 <b>씬 참조로</b> 하나씩 물고 있었다. 그래서
/// <list type="bullet">
/// <item>bossroom을 다른 씬에 인스턴스화하면 참조가 전부 비어 조용히 안 돌았고,</item>
/// <item>저작 도구(<c>Rebuild Boss Room Bounds</c>)가 기준점을 재생성하면 참조가 끊겼고,</item>
/// <item>착지점을 못 찾으면 <c>GameObject.Find("BossLandingPoint")</c> 이름 전역검색으로 떨어져
/// 씬에 동명 오브젝트가 둘이면 어느 것을 잡을지 보장이 없었다.</item>
/// </list>
/// 이제 프리팹이 자기 부품을 자기 안에서 찾아 들고 있고, Director는 이 컴포넌트 하나만 물어본다.
///
/// <b>좌표는 전부 자기 자식 transform에서 온다 — 절대좌표를 넣지 않는다.</b> 아레나는 맵 밖
/// 좌표(x≈+500)로 옮겨져 있어서, 어디든 절대값이 박히면 그 즉시 월드 원점 쪽으로 어긋난다.
///
/// 스폰·BT 개방은 하지 않는다. 그건 Director의 일이다
/// (<c>TwentyThreeArenaContext</c>는 BossScene 전용 스포너이므로 여기에 붙이면 보스 이중 스폰).
/// </summary>
[DisallowMultipleComponent]
public sealed class BossArenaContext : MonoBehaviour
{
    public const string BossAreaTag = "BossArea";
    public const int ChargePillarCount = 4;

    const string BossLandingName = "BossLandingPoint";
    const string ArrivalRootName = "PlayerArrivalPoints";

    [Header("기준점 (비우면 자기 자식에서 이름/태그로 자동 해결)")]
    [Tooltip("보스 착지점 = 방 중앙. 자식 'BossLandingPoint'.")]
    [SerializeField] private Transform bossLandingPoint;

    [Tooltip("No.23 BT가 태그로 찾아 켜고 끄는 아레나 트리거. 자식 중 tag 'BossArea'.")]
    [SerializeField] private Collider bossArea;

    [Tooltip("플레이어 도착 지점. 자식 'PlayerArrivalPoints'의 자식들.")]
    [SerializeField] private List<Transform> playerArrivalPoints = new List<Transform>();

    [Header("충전 기둥")]
    [Tooltip("정확히 4개. 비우면 자식에서 ChargingObject를 전부 수집한다.")]
    [SerializeField] private List<ChargingObject> chargingPillars = new List<ChargingObject>();

    bool _resolved;

    public Transform BossLandingPoint { get { Resolve(); return bossLandingPoint; } }
    public Collider BossArea { get { Resolve(); return bossArea; } }
    public IReadOnlyList<Transform> PlayerArrivalPoints { get { Resolve(); return playerArrivalPoints; } }
    public IReadOnlyList<ChargingObject> ChargingPillars { get { Resolve(); return chargingPillars; } }

    void Awake() => Resolve();

    /// <summary>
    /// 비어 있는 참조를 자기 계층 안에서만 채운다(씬 전역 검색 금지 — 그게 동명 오브젝트 사고의 원인).
    /// 저작 없이 프리팹을 인스턴스화해도 동작하게 하는 안전망이며, 이미 채워진 값은 건드리지 않는다.
    /// </summary>
    public void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        if (bossLandingPoint == null)
            bossLandingPoint = FindChildByName(BossLandingName);

        if (bossArea == null)
            bossArea = FindChildColliderByTag(BossAreaTag);

        if (playerArrivalPoints == null) playerArrivalPoints = new List<Transform>();
        playerArrivalPoints.RemoveAll(t => t == null);
        if (playerArrivalPoints.Count == 0)
        {
            Transform root = FindChildByName(ArrivalRootName);
            if (root != null)
                foreach (Transform child in root)
                    playerArrivalPoints.Add(child);
        }

        if (chargingPillars == null) chargingPillars = new List<ChargingObject>();
        chargingPillars.RemoveAll(p => p == null);
        if (chargingPillars.Count == 0)
            chargingPillars.AddRange(GetComponentsInChildren<ChargingObject>(true));
    }

    /// <summary>
    /// 구성이 실제로 쓸 수 있는 상태인지 검사한다. 여기서 걸리는 것들은 전부 예외 없이 조용히
    /// 실패하는 종류라, 증상이 "보스 패턴이 이상하다"로만 나타나 원인 추적이 오래 걸린다.
    /// </summary>
    public void Validate()
    {
        Resolve();

        if (bossLandingPoint == null)
            Edit.LogError($"[BossArena] 착지점('{BossLandingName}')이 없습니다 — 보스가 어디로 내려올지 정해지지 않습니다.", this);

        if (bossArea == null)
        {
            Edit.LogError($"[BossArena] tag '{BossAreaTag}' 콜라이더가 아레나 안에 없습니다 — " +
                          "No.23 BT의 FindObjectWithTag가 아레나를 못 찾습니다.", this);
        }
        else if (!bossArea.CompareTag(BossAreaTag))
        {
            Edit.LogError($"[BossArena] '{bossArea.name}'의 태그가 '{BossAreaTag}'가 아닙니다.", bossArea);
        }

        if (playerArrivalPoints.Count == 0)
            Edit.LogWarning($"[BossArena] 플레이어 도착 지점이 없습니다 — 텔레포트가 방 중앙에 겹쳐 놓입니다.", this);

        if (chargingPillars.Count != ChargePillarCount)
        {
            Edit.LogWarning($"[BossArena] 충전 기둥이 {chargingPillars.Count}개입니다(기대 {ChargePillarCount}) — " +
                            "인원수별 활성 개수가 목록 범위로 잘립니다.", this);
        }

        foreach (ChargingObject pillar in chargingPillars)
        {
            if (pillar.GetComponent<Collider>() == null)
                Edit.LogError($"[BossArena] 기둥 '{pillar.name}'에 Collider가 없습니다 — 피격되지 않아 " +
                              "충전 패턴을 깰 수 없습니다.", pillar);

            if (pillar.GetComponent<Unity.Netcode.NetworkObject>() == null)
                Edit.LogError($"[BossArena] 기둥 '{pillar.name}'에 NetworkObject가 없습니다 — " +
                              "OnNetworkSpawn이 돌지 않아 서버 로직 전체가 꺼집니다.", pillar);
        }
    }

    /// <summary>
    /// 씬에서 아레나를 찾는다. 0개면 아레나가 없는 씬이고, 2개 이상이면 어느 아레나로 보스를 보낼지
    /// 보장할 수 없으므로 둘 다 에러로 알린다(조용히 첫 번째를 고르지 않는다).
    /// </summary>
    public static BossArenaContext FindInScene(Object context = null)
    {
        BossArenaContext[] found = FindObjectsByType<BossArenaContext>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (found.Length == 1) return found[0];

        if (found.Length == 0)
        {
            Edit.LogWarning("[BossArena] 씬에 BossArenaContext가 없습니다 — bossroom 프리팹에 " +
                            "'Tools/Map/Authoring/Wire Boss Arena Context'로 붙이세요.", context);
            return null;
        }

        Edit.LogError($"[BossArena] 씬에 아레나가 {found.Length}개입니다 — 어느 곳으로 보스를 보낼지 " +
                      "보장할 수 없습니다. 씬에 bossroom 인스턴스를 하나만 두세요.", context);
        return found[0];
    }

    Transform FindChildByName(string childName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t != transform && t.name == childName) return t;
        return null;
    }

    Collider FindChildColliderByTag(string tag)
    {
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            if (c.CompareTag(tag)) return c;
        return null;
    }

    void OnDrawGizmosSelected()
    {
        if (bossLandingPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(bossLandingPoint.position, 1f);
        }

        Gizmos.color = Color.red;
        foreach (ChargingObject pillar in chargingPillars)
            if (pillar != null) Gizmos.DrawWireCube(pillar.transform.position, Vector3.one);
    }
}
