/// <summary>송전기(차징) 시퀀스의 결과.</summary>
public enum BossChargeResult
{
    InProgress = 0,
    /// <summary>송전탑 전부 파괴 — 보스가 <c>Groggy</c> 로 무력화된다(단 Break 로 승격하지 않는다).</summary>
    AllPylonsDestroyed,
    /// <summary>하나라도 도달했거나 제한시간 초과 — 보스가 <c>Rage</c> 돌진 3회로 넘어간다.</summary>
    Failed,
}

/// <summary>
/// 송전기 시퀀스 구현의 seam. 보스는 이 인터페이스만 알고, 송전탑(아레나 오브젝트) 구현은 갈아끼운다.
///
/// 🔴 **구현할 때 반드시 닫아야 하는 레거시 버그 2개**(정본 boss-fsm-detailed-spec.md §9.1):
///
/// 1. **송전탑 수는 1인 1 / 2인 2 / 3인 이상 4** 다.
///    레거시 `ChargeController` 는 `Mathf.Clamp(playerCount, 1, 3)` + `player3 = 3` 이라
///    **3인에 3개만** 켰다. Clamp 는 인원 **인덱스** 클램프이지 개수 클램프가 아니다.
///
/// 2. **완료 판정을 `==` 로 하면 교착한다.**
///    레거시는 `_destroyCount == _max` / `_reachedCount == _max` 를 각각 봤기 때문에
///    **파괴와 도달이 섞이면 두 플래그 모두 영원히 false** 가 되어 차징에서 못 나왔다.
///    → **`destroyed + reached >= max` 합산**으로 판정하고, 결과는 이분법이다:
///    **전부 파괴 → <see cref="BossChargeResult.AllPylonsDestroyed"/> /
///    하나라도 도달 → <see cref="BossChargeResult.Failed"/>.**
///    (2026-07-30자 진단 주석이 이 교착을 이미 정확히 기술해 뒀는데도 안 고쳐져 있었다.)
/// </summary>
public interface IBossChargeSequence
{
    /// <summary>시퀀스 시작(서버). <paramref name="pylonCount"/> 는 보스가 인원수로 산출해 넘긴다.</summary>
    void Begin(int pylonCount, float timeLimit);

    /// <summary>매 서버 틱 결과를 조회한다. <see cref="BossChargeResult.InProgress"/> 면 계속 대기.</summary>
    BossChargeResult Poll();

    /// <summary>보스가 죽거나 시퀀스가 중단될 때 정리한다(송전탑 비활성 등).</summary>
    void Cancel();
}
