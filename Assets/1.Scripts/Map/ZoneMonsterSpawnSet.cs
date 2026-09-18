using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 존 프리팹이 들고 다니는 <b>몬스터 스폰 저작 데이터</b>. 순수 <see cref="MonoBehaviour"/> 다.
///
/// <b>왜 <see cref="MonsterSpawner"/> 를 쓰지 않는가</b> (2026-09-09):
///
/// ① <b>존에 붙은 <see cref="MonsterSpawner"/> 는 죽은 코드였다.</b> 그것은 <c>NetworkBehaviour</c> 이고
///    <c>IsServer</c> 는 계산 프로퍼티가 아니라 <b>네트워크 스폰 때 세팅되는 자동 프로퍼티</b>다.
///    존은 「비네트워크 규약」(<see cref="MapContentSpawner"/> 헤더 — 양쪽 피어에서 로컬 Instantiate)
///    이라 <c>Spawn()</c> 되지 않으므로 <c>IsServer</c> 가 <b>영원히 false</b> 이고
///    <c>SpawnWave()</c>·<c>SpawnAt()</c> 이 첫 줄에서 그대로 return 했다.
///    <see cref="MapContentSpawner"/> 가 그 컴포넌트에서 실제로 읽은 것은
///    <c>ResolveSpawnPoints()</c> 와 <c>DefaultMonsterPrefab</c> <b>둘뿐</b>이었다.
///
/// ② 🔴 <b>그런데 그 하나 때문에 NGO 가 존 프리팹에 <c>NetworkObject</c> 를 계속 붙였다.</b>
///    <c>NetworkBehaviourEditor.cs:321→413</c> 이 인스펙터를 그릴 때마다
///    "NetworkBehaviours require a NetworkObject — 추가할까요?" 다이얼로그를 띄우고 <b>기본 버튼이
///    "Yes"</b> 다. 제거해도 <c>NetworkObjectEditor.cs:188</c> 이 같은 검사를 다시 불러 되붙는다.
///    게이트는 <c>Check for NetworkObject Component</c> 인데 <b><c>EditorPrefs</c>(머신 단위)</b> 라
///    끄더라도 팀원은 각자 다시 밟는다. 2026-09-09 에 존 프리팹 7종 중 4종이 이렇게 감염됐다.
///    → <b>근본 해결은 존에서 <c>NetworkBehaviour</c> 를 없애는 것이다.</b> 그게 이 파일이다.
///
/// <see cref="MonsterSpawner"/> 자체는 폐기하지 않는다 — <c>MonsterScene</c>·<c>TrashMobScene</c>
/// 에서 <b>진짜 네트워크 스포너</b>로 쓰이고 있다(두 씬 모두 <c>NetworkManager</c> 보유).
/// 기반 클래스를 내리면 그쪽이 깨지므로 <b>존에서 떼는 쪽</b>을 택했다.
///
/// 저작 도구 = <c>Tools/Map/Authoring/존 몬스터 스포너 배선 (적용)</c> (멱등).
/// </summary>
[DisallowMultipleComponent]
public sealed class ZoneMonsterSpawnSet : MonoBehaviour
{
    [Header("기본 몬스터")]
    [Tooltip("지점의 Monster Prefab Override 가 비어 있을 때 쓸 프리팹. NetworkObject 필수.")]
    [SerializeField] private GameObject defaultMonsterPrefab;

    [Header("스폰 지점")]
    [Tooltip("⚠️ 비워 두는 것이 정상이다 — 비어 있으면 자식 계층에서 자동 수집한다. " +
             "채워 넣으면 그 뒤에 아트가 마커를 추가해도 반영되지 않는다.")]
    [SerializeField] private List<MonsterSpawnPoint> spawnPoints = new List<MonsterSpawnPoint>();

    /// <summary>지점에 개별 지정이 없을 때 쓸 기본 몬스터.</summary>
    public GameObject DefaultMonsterPrefab => defaultMonsterPrefab;

    /// <summary>
    /// 이 존이 쓸 스폰 지점을 확정한다. 인스펙터 목록이 비어 있으면 자식 계층에서 자동 수집한다.
    ///
    /// 🔴 수집 결과를 <c>spawnPoints</c> 에 <b>캐시하지 않는다.</b> 존은 양쪽 피어에서 로컬
    /// Instantiate 되므로 이 인스턴스는 매번 새로 만들어지고, 프리팹 애셋을 오염시키지 않아야 한다.
    /// </summary>
    public IReadOnlyList<MonsterSpawnPoint> ResolveSpawnPoints()
    {
        if (spawnPoints != null && spawnPoints.Count > 0) return spawnPoints;
        return GetComponentsInChildren<MonsterSpawnPoint>(true);
    }
}
