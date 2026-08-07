using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 송전기(차징) 시퀀스 매니저. **보스에 붙는다**(자식이어도 된다).
//
// 기둥(`BossChargingPylon`)은 아레나(`bossroom.prefab`)에 있고 매니저는 보스에 있으므로
// 부모-자식 탐색으로는 서로를 못 찾는다 → 기둥이 **정적 레지스트리**에 자기를 등록하고
// 매니저가 거기서 필요한 개수만 고른다(`AreaZone.Active` 와 같은 패턴).
//
// 🔴 **레거시 `ChargeController` 의 버그 2개를 여기서 닫는다**(정본 §9.1):
//
// 1. **송전탑 수는 1인 1 / 2인 2 / 3인 이상 4.** 레거시는 `Clamp(playerCount, 1, 3)` + `player3 = 3` 이라
//    **3인에 3개만** 켰다. 인원→개수 매핑은 보스(`PylonCountFor`)가 구간으로 하고, 여기서는 받은 수만큼 쓴다.
//
// 2. **완료 판정을 `==` 로 하면 교착한다.** 레거시는 `_destroyCount == _max` /
//    `_reachedCount == _max` 를 각각 봤기 때문에 **파괴와 도달이 섞이면 두 플래그 모두 영원히 false** 가
//    되어 차징에서 못 나왔다. 여기서는 **참여 기둥 집합을 들고 `>=` 로 센다** — 카운터가 어긋날 여지가 없다.
//
// ⚠️ **"도달(reach)"은 실패 조건이 아니다.** 실측 결과 레거시의 `ReachEvent` 는
//    "기둥이 **상승을 완료해 때릴 수 있게 됨**"에 발생하고, `TakeDamage` 가 활성 상태에서만 통하므로
//    **모든 기둥이 반드시 도달한다.** 즉 "하나라도 도달 → Rage" 는 활성 직후 항상 실패가 된다.
//    → **실패는 제한시간 초과 단독**이다(정본 §9.1 의 "제한시간 초과 → Rage" 와 일치).
[DisallowMultipleComponent]
public class BossChargeSequence : MonoBehaviour, IBossChargeSequence
{
    [SerializeField, Min(0.1f)]
    [Tooltip("기둥이 상승을 마칠 때까지 기다려 주는 여유(초). 제한시간에 더해진다 — " +
             "상승 시간까지 제한시간에서 깎으면 플레이어가 부술 시간이 줄어든다.")]
    float riseGrace = 1.5f;

    [SerializeField]
    [Tooltip("기둥을 고르는 기준. 켜면 보스에서 가까운 순, 끄면 등록 순(씬 배치 순).")]
    bool pickNearest = true;

    readonly List<BossChargingPylon> _engaged = new List<BossChargingPylon>();
    float _deadline;
    bool _running;

    public void Begin(int pylonCount, float timeLimit)
    {
        if (!IsServerRuntime()) return;

        Cancel(); // 이전 시퀀스 잔존물 정리(멱등)

        int want = Mathf.Max(1, pylonCount);
        SelectPylons(want);

        if (_engaged.Count == 0)
        {
            Debug.LogError(
                $"{name}: 활성화할 BossChargingPylon 이 하나도 없다 — 아레나(bossroom)의 " +
                "Env_Mv_bosscharger_upper 에 BossChargingPylon 이 붙어 있고 스폰됐는지 확인할 것. " +
                "이번 시퀀스는 제한시간 뒤 실패로 끝난다.", this);
        }

        for (int i = 0; i < _engaged.Count; i++)
            _engaged[i].BeginCharge();

        if (_engaged.Count < want)
            Debug.LogWarning(
                $"{name}: 송전탑 {want}개가 필요한데 {_engaged.Count}개만 있다 — 있는 만큼만 활성한다.", this);

        _deadline = Time.time + Mathf.Max(0.1f, timeLimit) + riseGrace;
        _running = true;
    }

    public BossChargeResult Poll()
    {
        if (!_running) return BossChargeResult.InProgress;

        // 🔴 `>=` 로 센다. 참여 집합을 들고 있으므로 카운터가 어긋날 여지가 없다(레거시 교착의 뿌리).
        int destroyed = 0;
        for (int i = 0; i < _engaged.Count; i++)
        {
            BossChargingPylon p = _engaged[i];
            if (p != null && p.WasDestroyed) destroyed++;
        }

        if (_engaged.Count > 0 && destroyed >= _engaged.Count)
        {
            _running = false;
            return BossChargeResult.AllPylonsDestroyed;
        }

        // 실패는 제한시간 초과 단독이다(위 ⚠️ 참조).
        if (Time.time >= _deadline)
        {
            _running = false;
            return BossChargeResult.Failed;
        }

        return BossChargeResult.InProgress;
    }

    public void Cancel()
    {
        if (!IsServerRuntime()) return;

        for (int i = 0; i < _engaged.Count; i++)
            _engaged[i]?.EndCharge();   // 멱등 — 어느 상태에서든 내려간다

        _engaged.Clear();
        _running = false;
    }

    void SelectPylons(int count)
    {
        _engaged.Clear();

        List<BossChargingPylon> pool = BossChargingPylon.Active;
        for (int i = 0; i < pool.Count; i++)
            if (pool[i] != null) _engaged.Add(pool[i]);

        if (pickNearest)
        {
            Vector3 origin = transform.position;
            _engaged.Sort((a, b) =>
                (a.transform.position - origin).sqrMagnitude
                    .CompareTo((b.transform.position - origin).sqrMagnitude));
        }

        if (_engaged.Count > count)
            _engaged.RemoveRange(count, _engaged.Count - count);
    }

    // 보스가 미스폰 중첩 오브젝트에 이 컴포넌트를 둘 수도 있으므로 전역 판정을 쓴다(BossWells 와 동일 규약).
    static bool IsServerRuntime() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
}
