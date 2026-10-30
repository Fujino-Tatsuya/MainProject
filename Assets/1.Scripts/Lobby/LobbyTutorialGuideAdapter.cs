using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ⚠ 과도기 어댑터 — 158(목표/대사 패널) 병합 시 이 파일째 삭제(할 일 157 Q18, 계약 문서 §5).
/// [전 피어, 표시 전용] <see cref="LobbyTutorialState"/> 를 읽어 기존 상단 가이드 UI(<see cref="TutorialGuideUI"/>)에 임시로 띄운다:
/// 목표 = <see cref="TutorialGuideUI.SetObjective"/>(지시문 키 + T10 체크 항목 + 진행 n/N, T21 카운트다운은 '시작까지 {n}' 체크 줄 1개),대사(L11·L21) = <see cref="TutorialGuideUI.ShowSubtitle"/> → 대사 단계가 끝나면 자막 지움.
/// 옛 왼쪽 뷰(LobbyWeaponCheckView·LobbyTrainingCombatView)엔 의존하지 않는다. 브리핑(입력 잠금 대사 L31~L34)은 옛 <see cref="LobbyBriefing"/> 가 띄우므로 건드리지 않는다.
/// 씬 변경 없이 쓰려고 첫 씬 로드 때 숨은 DontDestroyOnLoad 오브젝트로 스스로 생긴다 — 파사드가 없으면(로비 밖) 아무것도 하지 않는다.
/// 문구 안 <c>{tag:ID}</c>(158 이 색을 칠하는 표기)는 여기서만 평문 <c>[넉백]</c> 등으로 바꿔 띄운다 — 색 없음, 모르는 태그는 그대로(<see cref="PlainTagArgs"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTutorialGuideAdapter : MonoBehaviour
{
    private static LobbyTutorialGuideAdapter s_instance;

    private TutorialGuideUI _ui;
    private string _shownObjective;
    private int _shownChecklistMask = -1;
    private int _shownCountdown = -1;
    private TutorialProgress _shownProgress;
    private int _shownDialogueRevision;
    private int _shownDialogueRunId;
    private bool _dialogueShowing;

    /// <summary>
    /// <c>{tag:ID}</c> → 평문 자리표시자 값(할 일 157 6b). 옛 UI 의 <see cref="TutorialGuideText.Fill"/> 가 <c>{…}</c> 를 args 로 채우므로 args 로 넘긴다 —
    /// 표에 없는 태그·닫히지 않은 <c>{tag:</c> 는 Fill 이 그대로 둔다.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PlainTagArgs => s_plainTags;

    private static readonly Dictionary<string, string> s_plainTags = new Dictionary<string, string>
    {
        { "tag:Knockback", "[넉백]" },
        { "tag:Stun", "[기절]" },
        { "tag:Suppress", "[제압]" },
    };

    /// <summary>테스트·확인용 — 어댑터가 띄울 때와 같은 치환.</summary>
    public static string ToPlainTags(string text) => TutorialGuideText.Fill(text, PlainTagArgs);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null)
            return;

        var go = new GameObject(nameof(LobbyTutorialGuideAdapter)) { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<LobbyTutorialGuideAdapter>();
    }

    private void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    private void Update()
    {
        var ui = TutorialGuideUI.Instance;
        if (ui != _ui)
        {
            // UI 가 바뀌었다(씬 전환) — 지난 UI 에 띄운 건 그 UI 와 함께 사라졌다.
            _ui = ui;
            ForgetShown();
        }

        if (_ui == null)
            return;

        if (!LobbyTutorialState.IsAvailable)
        {
            ClearShown();
            return;
        }

        ApplyObjective();
        ApplyDialogue();
    }

    private void ApplyObjective()
    {
        var step = LobbyTutorialState.CurrentStep;
        var visible = LobbyTutorialState.ObjectivePanelVisible && !string.IsNullOrEmpty(LobbyTutorialState.ObjectiveKey);
        if (!visible)
        {
            // 브리핑 묶음은 LobbyBriefing 이 목표를 직접 띄운다 — 덮지 않게 지우지 않고 잊기만 한다.
            if (step.LocksInput)
                _shownObjective = null;
            else
                ClearObjective();
            return;
        }

        var id = LobbyTutorialState.ObjectiveKey;
        var mask = -1;
        TutorialChecklistItem[] checklist = null;
        if (LobbyTutorialState.TryGetLocalChecklist(out var localMask, out var itemKeys))
        {
            mask = localMask;
            checklist = new TutorialChecklistItem[itemKeys.Count];
            for (var i = 0; i < itemKeys.Count; i++)
                checklist[i] = new TutorialChecklistItem(itemKeys[i], LobbyWeaponCheckRules.Has(localMask, (LobbyWeaponCheckItem)i));
        }

        var progress = default(TutorialProgress);
        var countdown = -1;
        if (LobbyTutorialState.TryGetProgress(out var n, out var total))
        {
            // T21 '시작까지 {n}' — 옛 UI 진행도 칸은 n/total 만 그리므로 체크 줄 한 개로 띄운다(할 일 157 ⑥).
            if (LobbyTutorialState.ProgressMode == LobbyTutorialProgressMode.Countdown)
                countdown = n;
            else
                progress = new TutorialProgress(n, total);
        }

        if (id == _shownObjective && mask == _shownChecklistMask && countdown == _shownCountdown
            && progress.Current == _shownProgress.Current && progress.Total == _shownProgress.Total)
            return;

        var args = PlainTagArgs;
        if (countdown >= 0)
        {
            args = new Dictionary<string, string>(s_plainTags) { { "n", countdown.ToString() } };
            checklist = new[] { new TutorialChecklistItem(LobbyTutorialState.ProgressKey, false) };
        }

        _ui.SetObjective(id, args, checklist, progress);
        _shownObjective = id;
        _shownChecklistMask = mask;
        _shownCountdown = countdown;
        _shownProgress = progress;
    }

    private void ApplyDialogue()
    {
        var dialogue = LobbyTutorialState.Dialogue;
        var active = LobbyTutorialState.IsDialogueActive && !LobbyTutorialState.CurrentStep.LocksInput
                     && !string.IsNullOrEmpty(dialogue.DialogueId);
        if (!active)
        {
            EndDialogue();
            return;
        }

        if (_dialogueShowing && _shownDialogueRevision == dialogue.Revision && _shownDialogueRunId == LobbyTutorialState.RunId)
            return;

        EndDialogue();
        _ui.ShowSubtitle(dialogue.DialogueId, PlainTagArgs);
        _dialogueShowing = true;
        _shownDialogueRevision = dialogue.Revision;
        _shownDialogueRunId = LobbyTutorialState.RunId;
    }

    // 대사 단계가 끝났다 — 자막 길이(SO 기본 6초)와 서버 대사 길이가 달라도 단계와 함께 지운다.
    private void EndDialogue()
    {
        if (!_dialogueShowing)
            return;

        _dialogueShowing = false;
        if (_ui != null)
            _ui.ClearSubtitles();
    }

    private void ClearObjective()
    {
        if (_shownObjective == null)
            return;

        _ui.SetObjective(null);
        _shownObjective = null;
        _shownChecklistMask = -1;
        _shownCountdown = -1;
        _shownProgress = default;
    }

    private void ClearShown()
    {
        ClearObjective();
        EndDialogue();
    }

    private void ForgetShown()
    {
        _shownObjective = null;
        _shownChecklistMask = -1;
        _shownCountdown = -1;
        _shownProgress = default;
        _dialogueShowing = false;
    }
}
