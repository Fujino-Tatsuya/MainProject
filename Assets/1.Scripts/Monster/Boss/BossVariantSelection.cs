/// <summary>
/// 보스 비교 전환(PLAN-boss-raid.md §3) — 에디터 툴바 "보스: 기존/레이드" 가 쓰고 <see cref="BossEncounterDirector"/> 가 읽는다.
/// 🔴 에디터 전용 설정이다. 빌드는 항상 기존 보스(W4 비교 후 기본값을 정한다 — 팀장 10-14 "툴바 토글이면 충분").
/// 키는 워크트리 범위(DevBootTarget.ScopedKey)라 MPPM 클론도 메인과 같은 값을 읽는다.
/// </summary>
public static class BossVariantSelection
{
#if UNITY_EDITOR
    static string PrefKey => DevBootTarget.ScopedKey("BossVariant.Raid");

    public static bool UseRaid
    {
        get => UnityEditor.EditorPrefs.GetBool(PrefKey, false);
        set => UnityEditor.EditorPrefs.SetBool(PrefKey, value);
    }
#else
    public static bool UseRaid => false;
#endif
}
