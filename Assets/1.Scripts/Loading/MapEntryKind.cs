/// <summary>
/// 타깃 씬(4.MapScene)에 어떻게 들어왔는가(할 일 100 C3) — 서버 <see cref="NetworkLoadingFlowController.EntryKind"/> 가 들고, 맵 포드 착지 연출(<see cref="MapPodArrival"/>)이 맵 씬 스폰 때 읽는다.
/// 클라는 이 값을 직접 보지 않는다 — 맵 연출이 판단 결과(연출 켜짐)를 자기 NetworkVariable 로 복제한다.
/// </summary>
public enum MapEntryKind : byte
{
    Direct,     // 로비 포드 연출을 거치지 않은 진입 — Dev Boot·디버그 호스트·로비 포드 폴백(곧바로 로딩). 기본값
    Lobby,      // 로비 포드 탑승·강하 연출 끝(LobbyPodLaunch → StartGameLoading) — 맵 포드 착지 연출을 탄다
    Restart,    // 판 재시작(Alt+0, TryRestartTargetScene)
}
