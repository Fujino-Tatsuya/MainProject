using UnityEngine;

/// <summary>
/// 로비 튜토리얼 UI 파사드(할 일 158, 클라 로컬 · 네트워크 없음) — 좌상단 목표 패널 + 감독관 대사 패널(+ 전면 삽화).
/// 프리팹 <c>Assets/2.Prefabs/UI/Lobby/Tutorial/LobbyTutorialUI.prefab</c> 을 로비 씬에 하나 둔다(DDOL 안 함). 접근 = <see cref="Instance"/>.
/// 각 피어가 복제된 튜토리얼 상태(할 일 157)를 읽어 이 API 를 부른다 — 바인더는 157 계약 뒤.
/// <list type="bullet">
/// <item>CombatHUD·TutorialGuideUI 와 별도 Overlay 캔버스(sortingOrder 50). GraphicRaycaster 없음 — 클릭을 막지 않는다.</item>
/// <item>대사 중 + 대사 끝 뒤 유예(<see cref="dialogueGraceSeconds"/>) 동안 목표 패널을 숨긴다(<see cref="LobbyDialogueObjectiveGate"/>).</item>
/// <item>바닥 경로·벽 표식은 월드 쪽 컴포넌트로 넘긴다 — 157 바인더가 부를 입력 API 를 이 파사드 한 곳에 모으려는 것.</item>
/// </list>
/// 상세: Docs/tech/lobby-tutorial-ui.md
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTutorialUI : MonoBehaviour
{
    private const string Tag = "[LobbyTutorialUI]";

    public static LobbyTutorialUI Instance { get; private set; }

    [SerializeField] private LobbyObjectivePanelView objectivePanel;
    [SerializeField] private LobbyDialoguePanelView dialoguePanel;

    [Tooltip("대사가 끝난 뒤 목표 패널을 다시 보이기까지 기다리는 시간(초). 그 안에 다음 대사가 오면 계속 숨김.")]
    [SerializeField, Min(0f)] private float dialogueGraceSeconds = 0.3f;

    private LobbyDialogueObjectiveGate _gate;
    private bool _hasObjective;

    private LobbyDialogueObjectiveGate Gate => _gate ??= new LobbyDialogueObjectiveGate(dialogueGraceSeconds);

    public bool HasObjective => _hasObjective;
    public bool DialogueActive => Gate.DialogueActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"{Tag} LobbyTutorialUI 가 둘 이상이다 — 먼저 놓인 것만 Instance 로 쓴다: {name}", this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnEnable() => Loc.Changed += HandleLanguageChanged;

    private void OnDisable() => Loc.Changed -= HandleLanguageChanged;

    private void Update()
    {
        Gate.GraceSeconds = dialogueGraceSeconds;
        if (objectivePanel != null)
            objectivePanel.SetVisible(_hasObjective && !Gate.IsObjectiveHidden(Time.unscaledTimeAsDouble));
    }

    // ---------------- API ----------------

    /// <summary>목표 패널 내용을 바꾼다. 대사 중이면 내용만 바꾸고 대사·유예가 끝난 뒤 보인다.</summary>
    public void ShowObjective(LobbyObjectiveView view)
    {
        _hasObjective = view != null;
        if (objectivePanel != null)
            objectivePanel.Render(view);
    }

    public void HideObjective()
    {
        _hasObjective = false;
        if (objectivePanel != null)
        {
            objectivePanel.Render(null);
            objectivePanel.SetVisible(false);
        }
    }

    /// <summary>감독관 대사 한 줄. 키 = Strings 키(<see cref="LobbyTutorialUiStrings"/>). 연속 대사는 그냥 다시 부른다.</summary>
    public void ShowDialogue(string speakerKey, string lineKey, LobbyTutorialBackground background = LobbyTutorialBackground.None)
    {
        Gate.BeginDialogue();
        if (objectivePanel != null)
            objectivePanel.SetVisible(false);
        if (dialoguePanel != null)
            dialoguePanel.Show(speakerKey, lineKey, background);
    }

    /// <summary>대사 패널을 닫는다(삽화도). 목표 패널은 유예 뒤 다시 보인다. 대사 중이 아니면 아무것도 안 한다.</summary>
    public void EndDialogue()
    {
        if (!Gate.DialogueActive)
            return;

        Gate.EndDialogue(Time.unscaledTimeAsDouble);
        if (dialoguePanel != null)
            dialoguePanel.Hide();
    }

    /// <summary>
    /// 지금 단계의 바닥 경로 하나만 켠다(<see cref="LobbyTutorialPathSet"/> — 월드 쪽). null·빈 값 = 전부 끔.
    /// 입력 API 를 이 파사드 한 곳에 모으려고 넘겨 준다.
    /// </summary>
    public void SetPath(string pathId)
    {
        LobbyTutorialPathSet set = LobbyTutorialPathSet.Active;
        if (set != null)
            set.SetPath(pathId);
        else if (!string.IsNullOrEmpty(pathId))
            Debug.LogWarning($"{Tag} 경로 '{pathId}' 를 켜려 했지만 씬에 켜진 LobbyTutorialPathSet 이 없다.", this);
    }

    /// <summary>지정 벽 표식 켜고 끄기(ID = <see cref="LobbyTutorialWallTarget.WallId"/> — 월드 쪽).</summary>
    public void SetWallTarget(string wallId, bool visible) => LobbyTutorialWallTarget.SetVisible(wallId, visible);

    /// <summary>씬 종료·회차 초기화 — 목표·대사·경로·벽 표식을 유예 없이 전부 끈다.</summary>
    public void ClearAll()
    {
        HideObjective();
        Gate.Reset();
        if (dialoguePanel != null)
            dialoguePanel.Hide(immediate: true);
        SetPath(null);
        LobbyTutorialWallTarget.HideAll();
    }

    private void HandleLanguageChanged()
    {
        if (objectivePanel != null)
            objectivePanel.Refresh();
        if (dialoguePanel != null)
            dialoguePanel.RefreshLanguage();
    }
}
