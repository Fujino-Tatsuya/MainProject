using UnityEditor;
using UnityEngine;

/// <summary>
/// 튜토리얼 존 장부·휴면 디버그 메뉴(S4). <b>Play 중 호스트에서만</b> 동작한다 — 휴면·피해는 서버 권한.
/// 실제 기상 호출은 S8(존2)·S9(존3)·S10(존4)이 붙인다. 그 전까지 Play 확인용.
/// 처치는 DevMonsterKill(F9)과 같은 경로(TakeDamage)지만 휴면이면 무적이라 먼저 깨운다 — F9 는 휴면 몬스터를 못 죽인다.
/// </summary>
internal static class MapSceneMonsterDebugMenu
{
    const string Root = "Tools/MapScene/Debug/";

    static bool Ready()
    {
        MapSceneDirector d = MapSceneDirector.Instance;
        return Application.isPlaying && d != null && d.IsServer && d.IsTutorialActive;
    }

    [MenuItem(Root + "장부 로그", true)]
    [MenuItem(Root + "존2 기상", true)]
    [MenuItem(Root + "존3 기상", true)]
    [MenuItem(Root + "존4 기상 (중간보스 제외)", true)]
    [MenuItem(Root + "존4 중간보스 기상", true)]
    [MenuItem(Root + "존2~4 전부 휴면", true)]
    [MenuItem(Root + "존2 전멸 (깨워서 처치)", true)]
    [MenuItem(Root + "존3 핵심 처치 (스피너봇)", true)]
    [MenuItem(Root + "존4 핵심 처치 (중간보스)", true)]
    static bool Validate() => Ready();

    [MenuItem(Root + "장부 로그", false, 0)]
    static void LogLedger() => Debug.Log($"[TutorialDebug] {MapSceneDirector.Instance.DescribeMonsterZonesServer()}");

    [MenuItem(Root + "존2 기상", false, 20)]
    static void WakeZone2() => MapSceneDirector.Instance.WakeZoneServer(MapSceneArea.Zone2);

    [MenuItem(Root + "존3 기상", false, 21)]
    static void WakeZone3() => MapSceneDirector.Instance.WakeZoneServer(MapSceneArea.Zone3);

    [MenuItem(Root + "존4 기상 (중간보스 제외)", false, 22)]
    static void WakeZone4() => MapSceneDirector.Instance.WakeZoneServer(MapSceneArea.Zone4, MapSceneMonsterFilter.NormalOnly);

    [MenuItem(Root + "존4 중간보스 기상", false, 23)]
    static void WakeZone4Key() => MapSceneDirector.Instance.WakeZoneServer(MapSceneArea.Zone4, MapSceneMonsterFilter.KeyOnly);

    [MenuItem(Root + "존2~4 전부 휴면", false, 40)]
    static void SleepAll()
    {
        MapSceneDirector d = MapSceneDirector.Instance;
        d.SetZoneDormantServer(MapSceneArea.Zone2, true);
        d.SetZoneDormantServer(MapSceneArea.Zone3, true);
        d.SetZoneDormantServer(MapSceneArea.Zone4, true);
    }

    // 처치 코드는 런타임 구간 스킵(Alt+1~0)과 공용 — MapSceneDebugActions.
    [MenuItem(Root + "존2 전멸 (깨워서 처치)", false, 60)]
    static void KillZone2() => MapSceneDebugActions.KillZoneMonsters(MapSceneDirector.Instance, MapSceneArea.Zone2, MapSceneMonsterFilter.All);

    [MenuItem(Root + "존3 핵심 처치 (스피너봇)", false, 61)]
    static void KillZone3Key() => MapSceneDebugActions.KillZoneMonsters(MapSceneDirector.Instance, MapSceneArea.Zone3, MapSceneMonsterFilter.KeyOnly);

    [MenuItem(Root + "존4 핵심 처치 (중간보스)", false, 62)]
    static void KillZone4Key() => MapSceneDebugActions.KillZoneMonsters(MapSceneDirector.Instance, MapSceneArea.Zone4, MapSceneMonsterFilter.KeyOnly);
}
