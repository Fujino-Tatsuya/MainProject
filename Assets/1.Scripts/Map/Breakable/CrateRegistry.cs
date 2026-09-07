using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상자 ID → 인스턴스 조회표. 파괴 RPC가 "어느 상자인가"를 푸는 곳이다.
///
/// <b>왜 static인가.</b> 등록은 <b>순수 로컬 동작</b>이다 — 키가 스폰 시점에 결정적으로 부여된
/// ID이므로 피어마다 등록 순서가 달라도 결과가 같다. 그래서 소유자도, 씬 배치도 필요 없다.
///
/// <b>왜 씬 전환에서 비우는가.</b> 맵은 씬 로드마다 새로 생성되고 ID는 그 생성에 종속이다
/// (<see cref="MapContentSpawner"/>가 부여). 이전 맵의 항목이 남아 있으면 같은 ID가 옛 상자를
/// 가리켜 <b>엉뚱한 상자가 부서진다</b>.
/// </summary>
public static class CrateRegistry
{
    static readonly Dictionary<int, BreakableCrate> _byId = new Dictionary<int, BreakableCrate>();

    /// <summary>
    /// 등록. 같은 ID가 이미 있으면 <b>조용히 넘기지 않는다</b> —
    /// 하나를 때렸는데 둘이 부서지는 증상으로만 드러나 추적이 어렵다.
    /// </summary>
    public static void Register(BreakableCrate crate)
    {
        if (crate == null) return;

        // 0은 "미할당" 표식이라 유효한 ID가 될 수 없다. 두 원인을 모두 적는다 —
        // 한쪽만 적어두면 다른 쪽일 때 엉뚱한 곳을 뒤지게 된다(실제로 겪었다).
        if (crate.CrateId == 0)
        {
            Edit.LogError($"[Crate] '{crate.name}'의 ID가 0이라 등록할 수 없다. 둘 중 하나다:\n" +
                          "  ① 스포너를 안 거쳤다 — 씬에 직접 배치했다면 " +
                          "'Tools > Crates > 씬의 상자에 ID 부여'를 돌릴 것.\n" +
                          "  ② ID 계산이 0을 냈다 — MapContentSpawner.AssignCrateIds의 " +
                          "슬롯·인덱스 조합을 확인할 것.", crate);
            return;
        }

        if (_byId.TryGetValue(crate.CrateId, out BreakableCrate existing) && existing != crate)
        {
            Edit.LogError($"[Crate] ID {crate.CrateId} 중복 — '{existing.name}'와 '{crate.name}'가 " +
                          "함께 부서진다. 스폰 순번 부여 로직을 확인할 것.", crate);
            return;
        }

        _byId[crate.CrateId] = crate;
    }

    public static void Unregister(BreakableCrate crate)
    {
        if (crate == null) return;
        if (_byId.TryGetValue(crate.CrateId, out BreakableCrate existing) && existing == crate)
            _byId.Remove(crate.CrateId);
    }

    /// <summary>
    /// [RPC 수신부] 해당 상자를 로컬로 부순다.
    ///
    /// <b>못 찾으면 경고한다.</b> 조용히 넘기면 "한쪽 피어에서만 상자가 안 부서진다"로 나타나는데,
    /// 그건 등록 누락(스포너를 안 거침)과 ID 불일치(맵 생성 결정성 깨짐)를 구분할 수 없는 증상이다.
    /// </summary>
    public static void BreakLocal(int crateId)
    {
        if (_byId.TryGetValue(crateId, out BreakableCrate crate) && crate != null)
        {
            crate.BreakLocal();
            return;
        }

        Edit.LogWarning($"[Crate] ID {crateId}인 상자를 찾지 못해 파괴를 적용하지 못했다 " +
                        $"(등록된 상자 {_byId.Count}개). 스포너를 거치지 않았거나 " +
                        "피어 간 ID가 어긋난 것이다.");
    }

    /// <summary>맵을 다시 생성하기 직전에 비운다. <see cref="MapContentSpawner"/>가 부른다.</summary>
    public static void Clear() => _byId.Clear();
}
