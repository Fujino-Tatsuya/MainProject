using UnityEngine;
using S = LobbyTutorialUiStrings;

/// <summary>
/// 로비 튜토리얼 UI 디버그 구동(할 일 158 C4) — 157 바인더 없이 명세 화면 01–27 의 목표·대사 입력을 재현한다.
/// 프리팹 <c>LobbyTutorialUI</c> 의 자식으로 <b>꺼 둔 채</b> 둔다. 켜고 인스펙터 ⋮ 메뉴(다음·이전·처음·전부 끄기)로 넘긴다.
/// 단축키 없음. 에디터·개발 빌드에서만 동작(릴리스 빌드에선 메뉴가 아무것도 안 한다).
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTutorialUIDebugDriver : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private const string Tag = "[LobbyTutorialUIDebug]";

    [Tooltip("지금 보여 주는 장면 번호(0부터). 켤 때 이 장면을 적용한다.")]
    [SerializeField, Min(0)] private int step;

    [Tooltip("집결·완료 수 분모(시연 기본 3).")]
    [SerializeField, Min(1)] private int partySize = 3;

    [Tooltip("화면 17 에서 켤 벽 표식 ID(LobbyTutorialWallTarget).")]
    [SerializeField] private string wallId = LobbyTutorialWallIds.TrainWest;

    private void OnEnable() => Apply();

    [ContextMenu("다음 장면")]
    private void Next()
    {
        step = (step + 1) % LobbyTutorialUIDebugScenes.Count;
        Apply();
    }

    [ContextMenu("이전 장면")]
    private void Prev()
    {
        step = (step - 1 + LobbyTutorialUIDebugScenes.Count) % LobbyTutorialUIDebugScenes.Count;
        Apply();
    }

    [ContextMenu("처음 장면")]
    private void First()
    {
        step = 0;
        Apply();
    }

    [ContextMenu("전부 끄기")]
    private void ClearAll()
    {
        LobbyTutorialUI ui = LobbyTutorialUI.Instance;
        if (ui != null)
            ui.ClearAll();
    }

    private void Apply()
    {
        LobbyTutorialUI ui = LobbyTutorialUI.Instance;
        if (ui == null)
        {
            Debug.LogWarning($"{Tag} LobbyTutorialUI.Instance 가 없다 — Play 중 프리팹 인스턴스가 켜져 있어야 한다.", this);
            return;
        }

        step = Mathf.Clamp(step, 0, LobbyTutorialUIDebugScenes.Count - 1);
        LobbyTutorialUIDebugScene scene = LobbyTutorialUIDebugScenes.Get(step, partySize);
        Debug.Log($"{Tag} 장면 {step} — 화면 {scene.Screen}", this);

        if (scene.DialogueLineKey != null)
        {
            ui.ShowDialogue(LobbyTutorialUiStrings.SpeakerSupervisor.Key, scene.DialogueLineKey, scene.Background);
        }
        else
        {
            ui.EndDialogue();
            ui.ShowObjective(scene.Objective);
        }

        ApplyWorld(ui, scene);
    }

    // 바닥 경로·벽 표식 — 대사 장면은 경로를 끈다(명세 L30: 브리핑 중 목표·경로 숨김).
    private void ApplyWorld(LobbyTutorialUI ui, LobbyTutorialUIDebugScene scene)
    {
        ui.SetPath(scene.PathId);
        ui.SetWallTarget(wallId, scene.WallVisible);
    }
#endif
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>디버그 장면 하나 — 목표 또는 대사, 그리고 켤 바닥 경로·벽 표식.</summary>
public struct LobbyTutorialUIDebugScene
{
    public string Screen;
    public LobbyObjectiveView Objective;
    public string DialogueLineKey;
    public LobbyTutorialBackground Background;
    public string PathId;
    public bool WallVisible;
}

/// <summary>명세 §5 화면 01–27 의 표시 입력(문구 키·진행·대사·경로). 디버그 전용 — 실제 흐름은 157 이 상태 데이터로 정한다.</summary>
public static class LobbyTutorialUIDebugScenes
{
    private static readonly System.Func<int, LobbyTutorialUIDebugScene>[] Scenes =
    {
        n => Obj("01–02 L00", S.ChapterCharacterSelect, S.ObjCharacterSelect, null, null, default, LobbyTutorialPathIds.Locker),
        n => Obj("03–04 L10", S.ChapterTrainingPrep, S.ObjGatherCentral, null, null, Gather(1, n), LobbyTutorialPathIds.Central),
        n => Dlg("05 L11", S.DlgCentralIntro, LobbyTutorialBackground.None),
        n => Obj("06–07 L20", S.ChapterTrainingPrep, S.ObjGatherTraining, null, null, Gather(2, n), LobbyTutorialPathIds.TrainingHall),
        n => Dlg("08 L21", S.DlgTrainingIntro, LobbyTutorialBackground.None),
        n => Obj("09 T10", S.ChapterBasic, S.ObjBasic, null, BasicChecks(), new LobbyTutorialProgress(LobbyTutorialProgressKind.TrainingDone, 1, n), null),
        n => Obj("10 T20", S.ChapterCombat, S.ObjGatherZone, null, null, Gather(0, n), LobbyTutorialPathIds.CombatArea),
        n => Obj("11 T21", S.ChapterCombat, S.ObjCombatKillAll, null, null, new LobbyTutorialProgress(LobbyTutorialProgressKind.Countdown, 3, 0), null),
        n => Obj("12 T22", S.ChapterCombat, S.ObjCombatKillAll, null, null, default, null),
        n => Obj("13–14 T30", S.ChapterCounter, S.ObjGatherZone, null, null, Gather(1, n), LobbyTutorialPathIds.CounterArea),
        n => Obj("15 T31", S.ChapterCounter, S.ObjCounter, null, null, default, null),
        n => Obj("16 T32", S.ChapterCounter, S.ObjVulnerableCC, new[] { S.BodyVulnerableCC1.Key, S.BodyVulnerableCC2.Key }, null, default, null),
        n => Wall(Obj("17 T34", S.ChapterCounter, S.ObjWall, null, null, default, null)),
        n => Obj("18 T40", S.ChapterSuppress, S.ObjSuppressKill, new[] { S.BodySuppress.Key }, null, default, null),
        n => Obj("19 T50", S.ChapterFreePractice, S.ObjFreePractice, new[] { S.BodyFreePractice.Key }, null, Gather(0, n), LobbyTutorialPathIds.TrainingExit),
        n => Obj("20–21 L30", S.ChapterBriefing, S.ObjGatherCentral, null, null, Gather(2, n), LobbyTutorialPathIds.Central),
        n => Dlg("22 L31", S.DlgBrief01, LobbyTutorialBackground.None),
        n => Dlg("23 L32", S.DlgBrief02, LobbyTutorialBackground.Factory),
        n => Dlg("24 L33", S.DlgBrief03, LobbyTutorialBackground.Rebellion),
        n => Dlg("25 L34", S.DlgBrief04, LobbyTutorialBackground.None),
        n => Obj("26–27 L40", S.ChapterSortie, S.ObjGatherSortie, null, null, Gather(1, n), LobbyTutorialPathIds.Sortie),
    };

    public static int Count => Scenes.Length;

    public static LobbyTutorialUIDebugScene Get(int index, int partySize) => Scenes[Mathf.Clamp(index, 0, Scenes.Length - 1)](Mathf.Max(1, partySize));

    private static LobbyTutorialProgress Gather(int n, int total) =>
        new LobbyTutorialProgress(LobbyTutorialProgressKind.Gather, Mathf.Min(n, total), total);

    // 시연용 — 앞 두 항목만 끝난 상태.
    private static LobbyTutorialCheckItem[] BasicChecks()
    {
        var items = new LobbyTutorialCheckItem[S.BasicChecks.Count];
        for (int i = 0; i < items.Length; i++)
            items[i] = new LobbyTutorialCheckItem(S.BasicChecks[i].Key, i < 2);
        return items;
    }

    private static LobbyTutorialUIDebugScene Obj(string screen, UiString chapter, UiString objective, string[] body,
        LobbyTutorialCheckItem[] checks, LobbyTutorialProgress progress, string pathId) =>
        new LobbyTutorialUIDebugScene
        {
            Screen = screen,
            Objective = new LobbyObjectiveView
            {
                ChapterKey = chapter.Key,
                ObjectiveKey = objective.Key,
                BodyKeys = body ?? System.Array.Empty<string>(),
                Checklist = checks ?? System.Array.Empty<LobbyTutorialCheckItem>(),
                Progress = progress,
            },
            PathId = pathId,
        };

    private static LobbyTutorialUIDebugScene Dlg(string screen, UiString line, LobbyTutorialBackground background) =>
        new LobbyTutorialUIDebugScene { Screen = screen, DialogueLineKey = line.Key, Background = background };

    private static LobbyTutorialUIDebugScene Wall(LobbyTutorialUIDebugScene scene)
    {
        scene.WallVisible = true;
        return scene;
    }
}
#endif
