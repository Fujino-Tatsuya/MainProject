using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 포드 강하 단계 시각표(읽기 전용, 할 일 100 C2d → 106) — <see cref="PodDropSettings"/>·<see cref="LobbyPodLaunch"/> 인스펙터가 같이 그린다.
/// 클립 단계는 실제로는 클립 애니 이벤트가 넘긴다 — 여기선 FBX 클립의 PodClipEvent 위치(1배속)로 어림한 시각을 보인다(이벤트가 없으면 클립 끝, <see cref="PodAnimEventAuthoring"/>).
/// 로비 보행 시간은 열린 씬의 <see cref="LobbyPodLaunch"/> 사람별 경로(걷기 시작 지점 → 개인 경로점 → 공용 경로점(합류 이후) → 문턱) 길이 ÷ 걷는 속도 — 서버가 연출 시작 때 복제하는 값과 같은 계산.
/// 씬이 안 열려 있으면 보행 부분은 '경로 길이 미지'로 둔다.
/// </summary>
public static class PodDropScheduleView
{
    /// <summary>시각표에 그리는 인원 — 기본 파티(3인).</summary>
    public const int DisplayWalkers = 3;

    /// <summary>시각표 두 칸(로비·맵)을 그린다. <paramref name="launch"/> 가 null 이면 열린 씬에서 찾는다.</summary>
    public static void Draw(PodDropSettings settings, LobbyPodLaunch launch = null)
    {
        if (settings == null)
        {
            EditorGUILayout.HelpBox("PodDropSettings 가 비어 시각표를 못 만든다.", MessageType.Info);
            return;
        }

        if (launch == null) launch = FindLaunch(settings);
        float[] walkSeconds = launch != null ? launch.EditorWalkSecondsPerSlot(DisplayWalkers, settings) : null;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("단계 시각표 (읽기 전용 — 클립 단계는 FBX 클립 이벤트 위치로 어림)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(LobbyText(settings, walkSeconds, launch != null ? launch.gameObject.scene.name : null), MessageType.None);
        EditorGUILayout.HelpBox(MapText(settings), MessageType.None);
    }

    // 이 설정을 쓰는 걸 먼저, 없으면 아무거나(경로는 씬 것이고 수치는 보고 있는 설정으로 계산한다).
    private static LobbyPodLaunch FindLaunch(PodDropSettings settings)
    {
        LobbyPodLaunch any = null;
        foreach (LobbyPodLaunch launch in Object.FindObjectsByType<LobbyPodLaunch>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (launch.EditorSettings == settings) return launch;
            if (any == null) any = launch;
        }

        return any;
    }

    /// <param name="walkSeconds">슬롯별 보행 시간 — null 이면 경로 길이를 모른다(씬 안 열림).</param>
    public static string LobbyText(PodDropSettings settings, float[] walkSeconds, string sceneName)
    {
        PodDropLobbyTimings lobby = settings.Lobby;
        bool known = walkSeconds != null;
        PodDropLobbyTimeline t = settings.CreateLobbyTimeline(known ? walkSeconds : new float[DisplayWalkers]);
        var sb = new StringBuilder();

        sb.Append($"로비 — t=0 = 출격 집결 확정, {DisplayWalkers}인 기준");
        sb.Append(known ? $" (경로: {sceneName})" : "");
        sb.Append($"\n  레터박스 들어옴   0 ~ {t.LetterboxInSeconds:0.##}s ({t.LetterboxInEase})");
        if (t.BlackoutSeconds > 0f)
        {
            float holdAt = t.BlackoutFadeInSeconds;
            float lightAt = holdAt + t.BlackoutHoldSeconds;
            sb.Append($"\n  시작 암전         검게 0 ~ {holdAt:0.##}s · 유지 ~ {lightAt:0.##}s · 밝게 ~ {t.BlackoutSeconds:0.##}s");
            sb.Append($"\n                    (유지 한가운데 {t.TeleportAt:0.##}s 에 걷기 시작 지점으로 순간이동)");
        }
        else
        {
            sb.Append("\n  시작 암전         없음 (t=0 에 순간이동)");
        }

        // 해치 열림 = 해치 클립 시작(순간이동) + 클립 이벤트 위치(1배속). 해치 애니가 없으면 곧바로.
        float hatchEvent = PodAnimEventAuthoring.EventSecondsOf(lobby.hatchState, out bool hatchFound);
        double hatchOpened = t.HatchAt + hatchEvent;
        double board = t.BoardAt(hatchOpened);
        sb.Append(string.IsNullOrEmpty(lobby.hatchState)
            ? $"\n  해치 열림         없음 — {t.TeleportAt:0.##}s 에 곧바로"
            : $"\n  해치 열림         {t.HatchAt:0.##} ~ ≈{hatchOpened:0.##}s ({lobby.hatchState} 1배속, {EventText(hatchEvent, hatchFound)} 에서 멈춤)");

        if (!known)
        {
            sb.Append($"\n  걷기              ≈{board:0.##}s ~ ?  — 경로 길이 미지, 로비 씬(3.LobbyScene)을 열면 계산");
            sb.Append($"\n                    (해치 이벤트 뒤, 빨라도 {t.WalkReadyAt:0.##}s · 속도 {lobby.walkSpeed:0.##}m/s, 출발 간격 {t.WalkInterval:0.##}s, 문턱 → 좌석 미끄러짐 {t.SeatSlideSeconds:0.##}s)");
            sb.Append(LaunchLine(lobby, "가장 늦게 앉는 순간"));
            sb.Append(CameraLine(t.CameraZoomDelaySeconds, t.CameraZoomSeconds, t.CameraStartDistance, t.CameraEndDistance, t.CameraZoomEase));
            return sb.ToString();
        }

        for (int slot = 0; slot < t.WalkerCount; slot++)
        {
            float length = t.WalkSecondsOf(slot) * lobby.walkSpeed;
            sb.Append($"\n  걷기 P{slot + 1}           ≈{t.WalkStartAt(slot, board):0.##} ~ {t.WalkArriveAt(slot, board):0.##}s ({length:0.##}m) → 미끄러짐 ~ {t.WalkEndAt(slot, board):0.##}s");
        }

        double launchAt = t.LaunchAt(board);
        sb.Append($"\n                    (해치 이벤트 뒤, 빨라도 {t.WalkReadyAt:0.##}s · 속도 {lobby.walkSpeed:0.##}m/s, 출발 간격 {t.WalkInterval:0.##}s, 미끄러짐 {t.SeatSlideSeconds:0.##}s, 가장 늦게 앉는 사람 ≈{launchAt:0.##}s)");
        sb.Append(LaunchLine(lobby, $"≈{launchAt:0.##}s"));
        sb.Append(CameraLine(t.CameraZoomDelaySeconds, t.CameraZoomSeconds, t.CameraStartDistance, t.CameraEndDistance, t.CameraZoomEase));
        sb.Append($"\n  총 ≈{launchAt + LaunchRealSeconds(lobby, out _, out _):0.##}초 → 로딩 화면(페이드 없음)");
        return sb.ToString();
    }

    // 발사 클립 시작 → 끝 이벤트(실제 초) — 가속 이벤트까지 1배속, 그 뒤 가속 배율.
    private static float LaunchRealSeconds(PodDropLobbyTimings lobby, out float fastClipSeconds, out bool fastFound)
    {
        float end = PodAnimEventAuthoring.EventSecondsOf(lobby.launchState, out _);
        fastClipSeconds = PodAnimEventAuthoring.EventSecondsOf(lobby.launchState, out fastFound, PodAnimEventRelay.KindSpeedUp);
        if (!fastFound || fastClipSeconds >= end) return end;
        float speed = lobby.launchFastSpeed > 0f ? lobby.launchFastSpeed : 1f;
        return fastClipSeconds + (end - fastClipSeconds) / speed;
    }

    private static string LaunchLine(PodDropLobbyTimings lobby, string from)
    {
        if (string.IsNullOrEmpty(lobby.launchState)) return $"\n  발사 애니         없음 — {from} 곧바로 로딩";
        float seconds = PodAnimEventAuthoring.EventSecondsOf(lobby.launchState, out bool found);
        float real = LaunchRealSeconds(lobby, out float fast, out bool fastFound);
        string fastText = fastFound
            ? $"클립 {fast:0.##}s 가속 이벤트부터 ×{lobby.launchFastSpeed:0.##}"
            : "⚠ 가속 이벤트 없음 — 끝까지 1배속";
        return $"\n  발사 애니         {from} 부터 {lobby.launchState} 처음부터 ({fastText}, 클립 {EventText(seconds, found)} = 실제 ≈{real:0.##}s 에 로딩 — 해치 닫힘·갱도 문·포드 하강)";
    }

    private static string EventText(float seconds, bool found) =>
        found ? $"이벤트 {seconds:0.##}s" : $"⚠ 이벤트 없음 — 클립 끝 {seconds:0.##}s";

    // 줌 시작 지연이 있으면 그동안 시작 거리로 멈춰 있다.
    private static string CameraLine(float delay, float seconds, float from, float to, EuniTween.Ease ease)
    {
        string hold = delay > 0f ? $" — 0 ~ {delay:0.##}s 는 시작 거리로 멈춤" : "";
        return $"\n  카메라 줌         {delay:0.##} ~ {delay + seconds:0.##}s ({from:0.##} → {to:0.##}배, {ease}){hold}";
    }

    public static string MapText(PodDropSettings settings)
    {
        PodDropMapTimings map = settings.Map;
        PodDropMapTimeline t = settings.CreateMapTimeline();
        var sb = new StringBuilder("맵 — t=0 = 로딩 화면이 걷힌 순간");
        sb.Append(t.FadeInSeconds > 0f
            ? $"\n  도착 페이드인     0 ~ {t.FadeInSeconds:0.##}s (검은 화면 → 밝게, {t.FadeInEase}, 다른 단계와 겹침)"
            : "\n  도착 페이드인     없음 (로딩 화면이 걷히자마자 맵이 보인다)");
        sb.Append("\n  레터박스          진입 때부터 덮임 → 디졸브 시작에 걷힘");
        sb.Append($"\n  카메라 지점       착지점 위 {t.HoverHeight + t.CameraStartHeightOffset:0.##}m 고정 → {t.BrakeAt:0.##}s 부터 포드 추적 → 디졸브 끝에 플레이어");
        sb.Append($"\n  빠른 낙하         {t.FallAt:0.##} ~ {t.BrakeAt:0.##}s ({t.FallHeight:0.##}m → {t.BrakeHeight:0.##}m, {t.FallEase})");
        sb.Append($"\n  급감속            {t.BrakeAt:0.##} ~ {t.HoverAt:0.##}s ({t.BrakeHeight:0.##}m → {t.HoverHeight:0.##}m, {t.BrakeEase}) — 날개·다리 펴기 동시 시작");

        float deploy = MaxEvent(map.DeployStates, out string deployText);
        double deployed = t.BrakeAt + deploy;
        sb.Append($"\n  날개·다리 펴기    {t.BrakeAt:0.##} ~ ≈{deployed:0.##}s ({deployText})");

        double landStart = t.LandStartAt(deployed);
        double touchdown = t.TouchdownAt(deployed);
        sb.Append($"\n  착륙              ≈{landStart:0.##} ~ {touchdown:0.##}s ({t.HoverHeight:0.##}m → 0(+여유 {map.podGroundClearance:0.###}m), {t.LandSeconds:0.##}s {t.LandEase}, 닿는 순간 이펙트·흔들림)");

        float open = MaxEvent(map.OpenStates, out string openText);
        double opened = touchdown + open;
        sb.Append($"\n  해치 열기·날개 접기 ≈{touchdown:0.##} ~ {opened:0.##}s ({openText})");
        sb.Append($"\n  디졸브            ≈{t.DissolveAt(opened):0.##} ~ {t.ReleaseAt(opened):0.##}s (열기 끝 {t.DissolveDelaySeconds:0.##}s 뒤 — 시작 순간 플레이어 보임·레터박스 걷히기 시작, 실루엣은 끝까지 끔)");
        sb.Append($"\n  레터박스 걷힘     ≈{t.LetterboxOutAt(opened):0.##} ~ {t.LetterboxOutAt(opened) + t.LetterboxOutSeconds:0.##}s ({t.LetterboxOutEase})");
        sb.Append($"\n  조작 해제         ≈{t.ReleaseAt(opened):0.##}s (디졸브 끝 — 입력·실루엣 복귀, 카메라 플레이어 추적)");
        sb.Append($"\n  연출 끝           ≈{t.EndAt(opened):0.##}s (UI·튜토리얼 S0 — S0 환영 자막은 그 뒤 착지 1초, 튜토리얼 값)");
        sb.Append(CameraLine(t.CameraZoomDelaySeconds, t.CameraZoomSeconds, t.CameraStartDistance, t.CameraEndDistance, t.CameraZoomEase));
        return sb.ToString();
    }

    // 같이 재생하는 클립 중 가장 늦은 이벤트(둘 다 와야 넘어간다).
    private static float MaxEvent(string[] states, out string text)
    {
        float max = 0f;
        var sb = new StringBuilder();
        foreach (string state in states)
        {
            if (string.IsNullOrEmpty(state)) continue;
            float seconds = PodAnimEventAuthoring.EventSecondsOf(state, out bool found);
            max = Mathf.Max(max, seconds);
            if (sb.Length > 0) sb.Append(" + ");
            sb.Append($"{state} {EventText(seconds, found)}");
        }

        text = sb.Length > 0 ? sb.ToString() : "애니 없음 — 곧바로";
        return max;
    }
}
