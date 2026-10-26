using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 포드 강하 단계 시각표(읽기 전용, 할 일 100 C2d) — <see cref="PodDropSettings"/>·<see cref="LobbyPodLaunch"/> 인스펙터가 같이 그린다.
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
        EditorGUILayout.LabelField("단계 시각표 (읽기 전용)", EditorStyles.boldLabel);
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
        sb.Append($"\n  레터박스 들어옴   0 ~ {t.LetterboxInSeconds:0.##}s");
        sb.Append(t.TeleportBlackoutSeconds > 0f
            ? $"\n  순간이동 암전     0 ~ {t.TeleportBlackoutSeconds:0.##}s (가장 깊을 때 {t.TeleportAt:0.##}s 에 걷기 시작 지점으로)"
            : "\n  순간이동 암전     없음 (t=0 에 순간이동)");

        if (!known)
        {
            sb.Append($"\n  걷기              {t.WalkAt:0.##}s ~ ?  — 경로 길이 미지, 로비 씬(3.LobbyScene)을 열면 계산");
            sb.Append($"\n                    (속도 {lobby.walkSpeed:0.##}m/s, 출발 간격 {t.WalkInterval:0.##}s, 문턱 → 좌석 미끄러짐 {t.SeatSlideSeconds:0.##}s)");
            sb.Append(HatchLine(t, relative: true));
            sb.Append($"\n  포드 하강         +{t.DescendSeconds:0.##}s ({t.DescendDepth:0.##}m, {t.DescendEase})");
            sb.Append($"\n  끝 암전(페이드)   +{t.FadeOutSeconds:0.##}s");
            sb.Append($"\n  총 = 가장 늦게 앉는 사람 + {t.HatchCloseSeconds + t.DescendSeconds + t.FadeOutSeconds:0.##}s → 맵 로딩");
            return sb.ToString();
        }

        for (int slot = 0; slot < t.WalkerCount; slot++)
        {
            float length = t.WalkSecondsOf(slot) * lobby.walkSpeed;
            sb.Append($"\n  걷기 P{slot + 1}           {t.WalkStartAt(slot):0.##} ~ {t.WalkArriveAt(slot):0.##}s ({length:0.##}m) → 미끄러짐 ~ {t.WalkEndAt(slot):0.##}s");
        }

        sb.Append($"\n                    (속도 {lobby.walkSpeed:0.##}m/s, 출발 간격 {t.WalkInterval:0.##}s, 미끄러짐 {t.SeatSlideSeconds:0.##}s, 가장 늦게 앉는 사람 {t.HatchCloseAt:0.##}s)");
        sb.Append(HatchLine(t, relative: false));
        sb.Append($"\n  포드 하강         {t.DescendAt:0.##} ~ {t.FadeOutAt:0.##}s ({t.DescendDepth:0.##}m, {t.DescendEase})");
        sb.Append($"\n  끝 암전(페이드)   {t.FadeOutAt:0.##} ~ {t.LoadAt:0.##}s");
        sb.Append($"\n  카메라 줌         0 ~ {t.CameraZoomSeconds:0.##}s ({t.CameraStartDistance:0.##} → {t.CameraEndDistance:0.##}배, {t.CameraZoomEase})");
        sb.Append($"\n  총 {t.LoadAt:0.##}초 → 맵 로딩");
        return sb.ToString();
    }

    private static string HatchLine(PodDropLobbyTimeline t, bool relative)
    {
        if (t.HatchCloseSeconds <= 0f) return "\n  해치 닫힘         0 (생략)";
        return relative
            ? $"\n  해치 닫힘         +{t.HatchCloseSeconds:0.##}s"
            : $"\n  해치 닫힘         {t.HatchCloseAt:0.##} ~ {t.DescendAt:0.##}s";
    }

    public static string MapText(PodDropSettings settings)
    {
        PodDropMapTimeline t = settings.CreateMapTimeline();
        var sb = new StringBuilder("맵 — t=0 = 로딩 화면이 걷힌 순간 (레터박스는 덮인 채 시작)");
        sb.Append($"\n  대기              0 ~ {t.FallAt:0.##}s");
        sb.Append($"\n  포드 낙하         {t.FallAt:0.##} ~ {t.ImpactAt:0.##}s ({t.FallHeight:0.##}m, {t.FallEase})");
        sb.Append($"\n  착지 여운         {t.ImpactAt:0.##} ~ {t.DissolveAt:0.##}s (착지 순간 이펙트·흔들림)");
        sb.Append($"\n  디졸브            {t.DissolveAt:0.##} ~ {t.LetterboxOutAt:0.##}s (시작 순간 플레이어 보임)");
        sb.Append($"\n  레터박스 걷힘     {t.LetterboxOutAt:0.##} ~ {t.ReleaseAt:0.##}s");
        sb.Append($"\n  해제              {t.ReleaseAt:0.##}s (UI·조작·튜토리얼 S0 — S0 환영 자막은 그 뒤 착지 1초, 튜토리얼 값)");
        sb.Append($"\n  카메라 줌         0 ~ {t.CameraZoomSeconds:0.##}s ({t.CameraStartDistance:0.##} → {t.CameraEndDistance:0.##}배, {t.CameraZoomEase})");
        sb.Append($"\n  총 {t.ReleaseAt:0.##}초");
        return sb.ToString();
    }
}
