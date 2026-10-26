using UnityEngine;

/// <summary>
/// 로비 → 맵 포드 강하 연출 수치(할 일 100) — 로비 단계(탑승·강하)와 맵 단계(낙하·디졸브) 둘 다.
/// 시간표는 <see cref="PodDropLobbyTimeline"/>·<see cref="PodDropMapTimeline"/> 이 이 값으로 만든다. 웨이포인트·착지점 같은 위치는 씬 Transform 이 원본이다.
/// 데이터 테이블 `Lobby` 시트 구역(열 = 이 에셋 이름). 시트에 행이 없으면 이 에셋 값을 쓴다(Docs/tech/data-table.md §3).
/// </summary>
[DataTableSheet("Lobby", Order = 1)]
[CreateAssetMenu(fileName = "PodDropSettings", menuName = "Lobby/Pod Drop Settings")]
public sealed class PodDropSettings : ScriptableObject
{
    [Tooltip("로비 단계 — t=0 = 출격 집결 확정")]
    [SerializeField] private PodDropLobbyTimings lobby = PodDropLobbyTimings.Default;

    [Tooltip("맵 단계 — t=0 = 로딩 화면이 걷힌 순간")]
    [SerializeField] private PodDropMapTimings map = PodDropMapTimings.Default;

    public PodDropLobbyTimings Lobby => lobby;
    public PodDropMapTimings Map => map;

    /// <param name="walkerCount">걸어 들어가는 사람 수(참가자 수) — 서버·클라가 같은 명단으로 만들어야 같은 시간표가 된다.</param>
    public PodDropLobbyTimeline CreateLobbyTimeline(int walkerCount) => new PodDropLobbyTimeline(lobby, walkerCount);

    public PodDropMapTimeline CreateMapTimeline() => new PodDropMapTimeline(map);
}
