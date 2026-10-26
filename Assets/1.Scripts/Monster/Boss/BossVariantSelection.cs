/// <summary>
/// 보스 A/B 전환(PLAN-boss-raid.md §3) — 에디터 툴바 "보스: A/B" 가 쓰고 <see cref="BossEncounterDirector"/> 가 읽는다.
/// 🔴 기본 = **B(레이드, TwentyThree_Raid → No23_B)** — 경석 10-22. 에디터 툴바 처음 값·심볼 없는 빌드(기본 메뉴·Build Profiles·CLI) 모두 B.
/// A 는 툴바에서 고르거나 "Build/Windows64 Player (MainFlow · A 보스)" 메뉴(BOSS_A_BUILD 심볼)로 뽑는다.
/// 키는 워크트리 범위(DevBootTarget.ScopedKey)라 MPPM 클론도 메인과 같은 값을 읽는다.
/// </summary>
public static class BossVariantSelection
{
#if UNITY_EDITOR
    // 키를 바꿨다(10-22) — 예전 키엔 "A 를 고른 적 없음 = false" 가 저장돼 있을 수 있어 새 기본 B 가 안 먹는다.
    static string PrefKey => DevBootTarget.ScopedKey("BossVariant.RaidDefaultB");

    public static bool UseRaid
    {
        get => UnityEditor.EditorPrefs.GetBool(PrefKey, true);
        set => UnityEditor.EditorPrefs.SetBool(PrefKey, value);
    }
#elif BOSS_A_BUILD
    // A 보스 빌드(Build/Windows64 Player (MainFlow · A 보스)) — 그 메뉴만 이 심볼을 얹는다.
    public static bool UseRaid => false;
#else
    public static bool UseRaid => true;
#endif
}
