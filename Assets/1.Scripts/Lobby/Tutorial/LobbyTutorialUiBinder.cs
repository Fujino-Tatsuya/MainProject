using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 목표 패널 내용이 바뀌었는지 보는 값(할 일 158 C10) — 같으면 패널을 다시 그리지 않는다(매 프레임 할당 방지).
/// 제목·설명 키는 행(revision)에 묶여 있어 (runId, revision) 으로 대신한다. 지시문 키는 같은 행에서도 카운트다운 표시(T21) 때 바뀌어 따로 본다.
/// </summary>
public readonly struct LobbyObjectiveSignature : IEquatable<LobbyObjectiveSignature>
{
    public LobbyObjectiveSignature(int runId, int revision, string objectiveId, LobbyTutorialProgressMode mode,
        bool hasChecklist, int checklistMask, bool hasProgress, int n, int total)
    {
        RunId = runId;
        Revision = revision;
        ObjectiveId = objectiveId ?? string.Empty;
        Mode = mode;
        ChecklistMask = hasChecklist ? checklistMask : -1;
        HasProgress = hasProgress;
        N = hasProgress ? n : 0;
        Total = hasProgress ? total : 0;
    }

    public int RunId { get; }
    public int Revision { get; }
    public string ObjectiveId { get; }
    public LobbyTutorialProgressMode Mode { get; }
    /// <summary>-1 = 체크 항목 없음.</summary>
    public int ChecklistMask { get; }
    public bool HasProgress { get; }
    public int N { get; }
    public int Total { get; }

    public bool Equals(LobbyObjectiveSignature other) =>
        RunId == other.RunId && Revision == other.Revision && string.Equals(ObjectiveId, other.ObjectiveId, StringComparison.Ordinal)
        && Mode == other.Mode && ChecklistMask == other.ChecklistMask && HasProgress == other.HasProgress && N == other.N && Total == other.Total;

    public override bool Equals(object obj) => obj is LobbyObjectiveSignature other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(RunId, Revision, ObjectiveId, Mode, ChecklistMask, N, Total);
}

/// <summary>
/// 157 상태 → 158 표시 입력 변환(할 일 158 C10, 계약 handoff-lobby-tutorial-contract.md §2) — 순수 매핑 + 파사드 읽기. MonoBehaviour 없음(EditMode 테스트).
/// StateId 문자열로 분기하지 않는다 — Kind·키 칸·진행 모드만 읽는다(계약 §1).
/// </summary>
public static class LobbyTutorialUiBinding
{
    /// <summary>157 진행 모드 → 158 진행 줄 종류. 이름·값이 같아 그대로 옮긴다(모르는 값 = None).</summary>
    public static LobbyTutorialProgressKind ToProgressKind(LobbyTutorialProgressMode mode)
    {
        switch (mode)
        {
            case LobbyTutorialProgressMode.Gather: return LobbyTutorialProgressKind.Gather;
            case LobbyTutorialProgressMode.PartyComplete: return LobbyTutorialProgressKind.PartyComplete;
            case LobbyTutorialProgressMode.Countdown: return LobbyTutorialProgressKind.Countdown;
            default: return LobbyTutorialProgressKind.None;
        }
    }

    /// <summary>대사 화자 키 — 행 칸이 비면 감독관(명세 3.3 공통 대사 패널).</summary>
    public static string SpeakerKeyOf(string speakerId) =>
        string.IsNullOrEmpty(speakerId) ? LobbyTutorialUiStrings.SpeakerSupervisor.Key : LobbyTutorialUiStrings.KeyOf(speakerId);

    /// <summary>
    /// 단계표 칸 값(접두 없는 id) → 목표 패널 한 장면. 체크 행 완료 = <see cref="LobbyWeaponCheckRules.Has"/>(키 순서 = <see cref="LobbyWeaponCheckItem"/> 값 순서, 계약 §2).
    /// 진행 값이 없으면(<paramref name="hasProgress"/> false) 진행 줄을 끈다.
    /// </summary>
    public static LobbyObjectiveView BuildObjective(string titleId, string objectiveId, IReadOnlyList<string> bodyIds,
        bool hasChecklist, IReadOnlyList<string> checklistIds, int checklistMask,
        LobbyTutorialProgressMode mode, bool hasProgress, int n, int total)
    {
        var view = new LobbyObjectiveView
        {
            ChapterKey = LobbyTutorialUiStrings.KeyOf(titleId),
            ObjectiveKey = LobbyTutorialUiStrings.KeyOf(objectiveId),
            BodyKeys = Keys(bodyIds),
            Progress = hasProgress ? new LobbyTutorialProgress(ToProgressKind(mode), n, total) : LobbyTutorialProgress.None,
        };

        if (hasChecklist && checklistIds != null && checklistIds.Count > 0)
        {
            var items = new LobbyTutorialCheckItem[checklistIds.Count];
            for (int i = 0; i < items.Length; i++)
                items[i] = new LobbyTutorialCheckItem(LobbyTutorialUiStrings.KeyOf(checklistIds[i]), LobbyWeaponCheckRules.Has(checklistMask, (LobbyWeaponCheckItem)i));
            view.Checklist = items;
        }

        return view;
    }

    /// <summary>라커룸 바닥 테두리를 켜는가 — L00(누적 방문) 단계 동안만. 전원 방문하면 단계가 넘어가 꺼진다(할 일 173, 집결 테두리와 같은 규칙).</summary>
    public static bool LockerAreaVisible(bool available, bool launchCommitted, LobbyTutorialStepKind kind) =>
        available && !launchCommitted && kind == LobbyTutorialStepKind.LockerVisit;

    /// <summary>
    /// 지금 파사드 값으로 목표 패널 서명을 만든다. false = 패널을 숨길 때(진행기 없음·대사·로딩·출격 확정 뒤·늦은 참가자·지시문 없음).
    /// </summary>
    public static bool TryCaptureObjective(out LobbyObjectiveSignature signature)
    {
        signature = default;
        if (!LobbyTutorialState.ObjectivePanelVisible || string.IsNullOrEmpty(LobbyTutorialState.ObjectiveKey))
            return false;

        bool hasChecklist = LobbyTutorialState.TryGetLocalChecklist(out int mask, out _);
        bool hasProgress = LobbyTutorialState.TryGetProgress(out int n, out int total);
        signature = new LobbyObjectiveSignature(LobbyTutorialState.RunId, LobbyTutorialState.Revision, LobbyTutorialState.ObjectiveKey,
            LobbyTutorialState.ProgressMode, hasChecklist, mask, hasProgress, n, total);
        return true;
    }

    /// <summary>서명과 지금 파사드 값으로 목표 패널 장면을 만든다(<see cref="TryCaptureObjective"/> 가 true 일 때).</summary>
    public static LobbyObjectiveView BuildObjective(in LobbyObjectiveSignature signature)
    {
        LobbyTutorialState.TryGetLocalChecklist(out _, out IReadOnlyList<string> itemKeys);
        return BuildObjective(LobbyTutorialState.TitleKey, signature.ObjectiveId, LobbyTutorialState.BodyKeys,
            signature.ChecklistMask >= 0, itemKeys, signature.ChecklistMask,
            signature.Mode, signature.HasProgress, signature.N, signature.Total);
    }

    private static string[] Keys(IReadOnlyList<string> ids)
    {
        if (ids == null || ids.Count == 0)
            return Array.Empty<string>();

        var keys = new string[ids.Count];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = LobbyTutorialUiStrings.KeyOf(ids[i]);
        return keys;
    }
}

/// <summary>
/// [전 피어, 표시 전용] 157 상태 파사드(<see cref="LobbyTutorialState"/>) → <see cref="LobbyTutorialUI"/> 입력 API(할 일 158 C10).
/// 매 프레임 파사드 값을 읽어 바뀐 것만 부른다(폴링 — 늦게 붙은 클라·이벤트 놓침도 같은 결과, 계약 §0). 새 RPC·NetworkVariable 없음.
/// <list type="bullet">
/// <item>목표 패널 = <see cref="LobbyTutorialState.ObjectivePanelVisible"/> + 키 칸·T10 체크·진행 n/N(집결 n/N 은 옛 상단 <c>PartyGatherStatusView</c> 대신 여기 진행 줄로).</item>
/// <item>대사 = (runId, revision) 이 바뀔 때마다 <see cref="LobbyTutorialUI.ShowDialogue(string,string,string)"/>, 대사 단계를 떠나면 <see cref="LobbyTutorialUI.EndDialogue"/>.</item>
/// <item>경로 = <see cref="LobbyTutorialState.ActivePathId"/>, 벽 = <see cref="LobbyTutorialState.ActiveTargetWallId"/>(이전 벽 끔 → 새 벽 켬).</item>
/// <item>라커룸 바닥 테두리 = L00 단계 동안(<see cref="LobbyTutorialUiBinding.LockerAreaVisible"/>).</item>
/// <item><see cref="LobbyTutorialState.IsAvailable"/> false = 아무것도 그리지 않는다(그려 둔 게 있으면 <see cref="LobbyTutorialUI.ClearAll"/> 한 번).
/// true 인 동안은 옛 3스텝 안내를 끈다(<see cref="LobbyTutorialUI.SetTutorialActive"/>).</item>
/// <item>같은 프리팹의 디버그 구동(<see cref="LobbyTutorialUIDebugDriver"/>)이 켜져 있으면 손 떼고 디버그 화면을 둔다.</item>
/// </list>
/// 로컬 플레이어 Transform 을 캐시하지 않는다 — 캐릭터 교체(계약 §6)에 따로 할 일 없음.
/// 상세: Docs/tech/lobby-tutorial-ui.md
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LobbyTutorialUI))]
public sealed class LobbyTutorialUiBinder : MonoBehaviour
{
    [SerializeField] private LobbyTutorialUI ui;
    [Tooltip("켜져 있으면 바인더가 손을 뗀다(디버그 화면 확인용). 비우면 자식에서 찾는다.")]
    [SerializeField] private LobbyTutorialUIDebugDriver debugDriver;

    private bool _drawn;
    private bool _objectiveShown;
    private LobbyObjectiveSignature _objective;
    private bool _dialogueShown;
    private int _dialogueRunId;
    private int _dialogueRevision;
    private string _path;
    private string _wall;
    private bool _locker;

    private void Awake()
    {
        if (ui == null)
            ui = GetComponent<LobbyTutorialUI>();
        if (debugDriver == null)
            debugDriver = GetComponentInChildren<LobbyTutorialUIDebugDriver>(true);
    }

    private void OnDisable() => Forget();

    private void Update()
    {
        if (ui == null)
            return;

        // 디버그 구동 중엔 손 뗌 — 기억을 비워 두면 디버그를 끈 다음 프레임에 지금 상태로 전부 다시 그린다.
        if (debugDriver != null && debugDriver.isActiveAndEnabled)
        {
            Forget();
            return;
        }

        if (!LobbyTutorialState.IsAvailable)
        {
            if (_drawn)
            {
                ui.ClearAll();
                Forget();
            }

            return;
        }

        if (!_drawn)
        {
            ui.SetTutorialActive(true); // 경로 없는 단계에도 옛 3스텝 안내(LobbyGuideSteps)를 끈다.
            _drawn = true;
        }

        ApplyDialogue();
        ApplyObjective();
        ApplyPath();
        ApplyWall();
        ApplyLockerArea();
    }

    // 대사 먼저 — 대사 패널이 목표 패널 숨김 유예를 시작해야 같은 프레임의 목표 교체가 깜빡이지 않는다.
    private void ApplyDialogue()
    {
        if (!LobbyTutorialState.IsDialogueActive)
        {
            if (_dialogueShown)
            {
                ui.EndDialogue();
                _dialogueShown = false;
            }

            return;
        }

        LobbyTutorialDialogueView dialogue = LobbyTutorialState.Dialogue;
        int runId = LobbyTutorialState.RunId;
        if (_dialogueShown && _dialogueRunId == runId && _dialogueRevision == dialogue.Revision)
            return;

        // 연속 대사(L31→L34)는 끄지 않고 다시 부른다 — 사이에 목표 패널이 켜지지 않는다.
        ui.ShowDialogue(LobbyTutorialUiBinding.SpeakerKeyOf(dialogue.SpeakerKey), LobbyTutorialUiStrings.KeyOf(dialogue.DialogueId), dialogue.BackgroundId);
        _dialogueShown = true;
        _dialogueRunId = runId;
        _dialogueRevision = dialogue.Revision;
    }

    private void ApplyObjective()
    {
        if (!LobbyTutorialUiBinding.TryCaptureObjective(out LobbyObjectiveSignature signature))
        {
            if (_objectiveShown)
            {
                ui.HideObjective();
                _objectiveShown = false;
            }

            return;
        }

        if (_objectiveShown && signature.Equals(_objective))
            return;

        ui.ShowObjective(LobbyTutorialUiBinding.BuildObjective(signature));
        _objectiveShown = true;
        _objective = signature;
    }

    private void ApplyPath()
    {
        string path = LobbyTutorialState.ActivePathId;
        if (string.Equals(path, _path, StringComparison.Ordinal))
            return;

        ui.SetPath(path);
        _path = path;
    }

    private void ApplyWall()
    {
        string wall = LobbyTutorialState.ActiveTargetWallId;
        if (string.Equals(wall, _wall, StringComparison.Ordinal))
            return;

        if (!string.IsNullOrEmpty(_wall))
            ui.SetWallTarget(_wall, false);
        if (!string.IsNullOrEmpty(wall))
            ui.SetWallTarget(wall, true);
        _wall = wall;
    }

    private void ApplyLockerArea()
    {
        bool locker = LobbyTutorialUiBinding.LockerAreaVisible(LobbyTutorialState.IsAvailable, LobbyTutorialState.LaunchCommitted, LobbyTutorialState.CurrentStep.Kind);
        if (locker == _locker)
            return;

        ui.SetLockerArea(locker);
        _locker = locker;
    }

    private void Forget()
    {
        _locker = false;
        _drawn = false;
        _objectiveShown = false;
        _objective = default;
        _dialogueShown = false;
        _path = null;
        _wall = null;
    }
}
