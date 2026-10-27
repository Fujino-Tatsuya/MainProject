using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로비 → 맵 포드 강하 연출 수치(할 일 100) — 로비 단계(탑승·강하)와 맵 단계(낙하·디졸브) 둘 다, 그리고 두 단계가 같이 쓰는 화면 값.
/// 시간표는 <see cref="PodDropLobbyTimeline"/>·<see cref="PodDropMapTimeline"/> 이 이 값으로 만든다. 웨이포인트·착지점 같은 위치는 씬 Transform 이 원본이다.
/// 데이터 테이블 `Lobby` 시트 구역(열 = 이 에셋 이름). 시트에 행이 없으면 이 에셋 값을 쓴다(Docs/tech/data-table.md §3).
/// 인스펙터 아래에 단계 시각표가 보인다(PodDropSettingsEditor — 열린 로비 씬의 걷기 경로 길이를 반영).
/// </summary>
[DataTableSheet("Lobby", Order = 1)]
[CreateAssetMenu(fileName = "PodDropSettings", menuName = "Lobby/Pod Drop Settings")]
public sealed class PodDropSettings : ScriptableObject
{
    [Header("화면 (로비·맵 공통)")]
    [Tooltip("레터박스 위·아래 띠 각각의 화면 높이 비율(보스 등장 연출 기본 0.04 와 같은 단위)")]
    [SerializeField, Range(0f, 0.2f)] private float letterboxHeight = 0.04f;

    [Tooltip("로비 단계 — t=0 = 출격 집결 확정")]
    [SerializeField] private PodDropLobbyTimings lobby = PodDropLobbyTimings.Default;

    [Tooltip("맵 단계 — t=0 = 로딩 화면이 걷힌 순간")]
    [SerializeField] private PodDropMapTimings map = PodDropMapTimings.Default;

    public float LetterboxHeight => letterboxHeight;
    public PodDropLobbyTimings Lobby => lobby;
    public PodDropMapTimings Map => map;

    /// <param name="walkSeconds">슬롯(보행 순서)별 보행 시간(초) — 서버가 <see cref="WalkSecondsFor"/> 로 계산해 복제한 값. 모든 피어가 같은 값으로 만들어야 같은 시간표가 된다.</param>
    public PodDropLobbyTimeline CreateLobbyTimeline(IReadOnlyList<float> walkSeconds) => new PodDropLobbyTimeline(lobby, walkSeconds);

    /// <summary>보행 경로 길이(m, 걷기 시작 지점 → 문턱) → 보행 시간(초) = 길이 ÷ 걷는 속도.</summary>
    public float WalkSecondsFor(float pathLength) => PodDropLobbyTimeline.WalkSecondsFor(pathLength, lobby.walkSpeed);

    public PodDropMapTimeline CreateMapTimeline() => new PodDropMapTimeline(map);
}
