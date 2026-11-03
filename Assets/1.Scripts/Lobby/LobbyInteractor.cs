using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 로비 상호작용 입력(각 피어 로컬, 3.LobbyScene Management) — PLAN-48 §4.6, C10. 원본 패턴 = <see cref="ZoneBridgeGateManager"/>.
/// 매 프레임 로컬 PlayerObject 위치로 <see cref="NearestInteractable.Pick"/> → 고른 대상에 <see cref="InteractPrompt"/> 를 띄우고,
/// 상호작용 키(설정창 <c>Player/Interact</c>, 기본 F)를 누르면 종류별로 실행한다
/// (CharacterSelect = <see cref="CharacterSelectPanel.Show"/>(내 캐릭터, 잠금 = <see cref="LobbyTrainingDirector.CharacterChangeBlockFor"/>) → 고르면 <see cref="LobbySession.RequestCharacter"/> — C12).
/// Ready 종류는 고르지 않는다 — 준비 패드는 출격 집결 구역으로 바뀌었다(할 일 86 PLAN §7-2, 서 있으면 준비).
/// 판정 권한은 서버다 — 서버가 거리(영역)를 다시 본다. 여기 검사는 RPC·프롬프트용이다.
/// 캐릭터 선택은 반경이 아니라 영역(라커룸 <c>Area_Locker</c>) 안에서 프롬프트·F 가 된다(할 일 161, <see cref="LobbyInteractable.Area"/>). 들어가도 패널은 자동으로 뜨지 않는다.
/// <list type="bullet">
/// <item>막힘: UI 모달(결과 패널 등 <see cref="UiInputGateManager"/>) 또는 플레이어 입력 차단 토큰·연출 잠금(브리핑 — <see cref="PlayerEncounterLock"/>)·생명주기(Alive 아님)면 프롬프트를 끄고 F 를 무시한다.
/// ESC 는 쓰지 않는다 — 결과 패널 > 선택 패널 순서의 ESC 소유(각 패널의 IsEscapeClaimed)와 겹치지 않는다.</item>
/// <item>교체 뒤에도 <c>LocalClient.PlayerObject</c> 를 매 프레임 다시 읽으므로 새 오브젝트를 따라간다(옛 오브젝트 참조를 들고 있지 않는다).</item>
/// <item>플레이어 프리팹을 고치지 않으려고 씬에 둔다. 플레이어는 <c>NetworkManager.LocalClient.PlayerObject</c> 로 찾는다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyInteractor : MonoBehaviour
{
    private const string Tag = "[LobbyInteractor]";
    private const string InteractActionPath = "Player/Interact";

    private readonly List<NearestInteractable.Candidate> _candidates = new List<NearestInteractable.Candidate>();
    private readonly List<LobbyInteractable> _candidateTargets = new List<LobbyInteractable>();
    private InputAction _interactAction;
    private LobbyInteractable _current;

    [Tooltip("캐릭터 선택 패널(씬의 CharacterSelectPanel.prefab 인스턴스, C12). 비우면 캐릭터 선택 오브젝트를 고르지 않는다.")]
    [SerializeField] private CharacterSelectPanel characterSelectPanel;

    private void OnEnable()
    {
        KeyBindingConfig.Changed += DisposeInteractAction;   // 다음 프레임에 새 키로 다시 만든다
        if (characterSelectPanel != null)
            characterSelectPanel.CharacterChosen += HandleCharacterChosen;
    }

    private void OnDisable()
    {
        KeyBindingConfig.Changed -= DisposeInteractAction;
        if (characterSelectPanel != null)
        {
            characterSelectPanel.CharacterChosen -= HandleCharacterChosen;
            characterSelectPanel.Hide();
        }
        SetCurrent(null);
        DisposeInteractAction();
    }

    private void OnDestroy()
    {
        InteractPrompt.Hide(this);
        DisposeInteractAction();
    }

    private void Update()
    {
        var session = LobbySession.Active;
        SyncCharacterSelectPanel(session);

        // 패널이 열린 동안은 패널의 UiModalBlocker 가 UiInputGateManager 를 잡아 IsBlocked 가 참이다 — 프롬프트·F 를 멈춘다.
        if (session == null || !TryGetLocalPlayer(out var player) || IsBlocked(player))
        {
            SetCurrent(null);
            return;
        }

        var target = PickTarget(player.transform.position, session);
        SetCurrent(target);
        if (target == null)
            return;

        target.ApplyPromptText(target.GetPromptString(false).Text);

        if (InteractPressedThisFrame())
            Interact(target, session);
    }

    private LobbyInteractable PickTarget(Vector3 playerPosition, LobbySession session)
    {
        _candidates.Clear();
        _candidateTargets.Clear();
        var registry = LobbyInteractable.Registry;
        for (var i = 0; i < registry.Count; i++)
        {
            var target = registry[i];
            if (target == null)
                continue;

            _candidateTargets.Add(target);
            _candidates.Add(target.ToCandidate(playerPosition, IsUsable(target, session)));
        }

        var index = NearestInteractable.Pick(playerPosition, _candidates);
        return index >= 0 ? _candidateTargets[index] : null;
    }

    // CharacterSelect 는 패널이 연결돼 있을 때만 고른다(눌러도 아무 일 없는 프롬프트를 띄우지 않는다).
    // Ready 는 출격 집결로 대체돼 고르지 않는다(enum 값은 qa/LobbyDemo 하네스 호환으로 남김).
    private bool IsUsable(LobbyInteractable target, LobbySession session)
    {
        switch (target.Kind)
        {
            case LobbyInteractableKind.CharacterSelect:
                return session.IsSpawned && characterSelectPanel != null;
            default:
                return false;
        }
    }

    private void Interact(LobbyInteractable target, LobbySession session)
    {
        switch (target.Kind)
        {
            case LobbyInteractableKind.CharacterSelect:
                var locked = IsCharacterLocked(session);
                var characterId = GetLocalCharacterId(session);
                Debug.Log($"[SceneFlow] {Tag} F: 캐릭터 선택 패널 열기 (current={characterId} locked={locked})", this);
                characterSelectPanel.Show(characterId, locked);
                break;
        }
    }

    // 열린 패널의 잠금(훈련 명단·출격 카운트다운·출격 단계)을 따라간다. 로비 세션이 내려가면 닫는다.
    private void SyncCharacterSelectPanel(LobbySession session)
    {
        if (characterSelectPanel == null || !characterSelectPanel.IsOpen)
            return;

        if (session == null || !session.IsSpawned)
        {
            characterSelectPanel.Hide();
            return;
        }

        characterSelectPanel.SetLocked(IsCharacterLocked(session));
    }

    private void HandleCharacterChosen(int characterId)
    {
        var session = LobbySession.Active;
        Debug.Log($"[SceneFlow] {Tag} 캐릭터 선택 → 서버 요청 {characterId} (session={(session != null)})", this);
        if (session != null)
            session.RequestCharacter(characterId);
    }

    // 서버 RPC 의 진행기 거부(LobbyTrainingDirector.CharacterChangeBlockFor)를 패널 잠금으로 미리 보여 준다(출격 카운트다운·출격 확정 뒤만 — 훈련 명단 잠금 없음, 157 ③).
    private static bool IsCharacterLocked(LobbySession session)
    {
        var training = LobbyTrainingDirector.Active;
        var networkManager = NetworkManager.Singleton;
        return training != null && networkManager != null
            && training.CharacterChangeBlockFor(networkManager.LocalClientId) != LobbyCharacterChangeReject.None;
    }

    private static int GetLocalCharacterId(LobbySession session)
    {
        var networkManager = NetworkManager.Singleton;
        return networkManager != null && session.TryGetEntry(networkManager.LocalClientId, out var entry)
            ? entry.CharacterId
            : LobbySession.DefaultCharacterId;
    }

    private void SetCurrent(LobbyInteractable target)
    {
        // 참조 비교 — 대상이 파괴돼 Unity == null 이 되어도 Hide 를 건너뛰지 않는다.
        if (ReferenceEquals(target, _current))
            return;

        _current = target;
        if (target != null)
            InteractPrompt.Show(this, target.PromptView);
        else
            InteractPrompt.Hide(this);
    }

    private static bool IsBlocked(NetworkObject player)
    {
        if (UiInputGateManager.IsInputBlocked)
            return true;

        var reader = player.GetComponent<PlayerInputReader>();
        if (reader != null && reader.IsInputBlockedByToken)
            return true;

        // 연출 잠금(브리핑 — LobbyBriefing)은 토큰이 아니라 입력 정책이 끈다 — 같은 판정을 여기서도 본다.
        var encounterLock = player.GetComponent<PlayerEncounterLock>();
        if (encounterLock != null && PlayerLifeInputPolicy.BlocksForCinematic(
                encounterLock.IsCinematicLocked, encounterLock.IsCinematicInputLocked, encounterLock.IsCinematicHidden))
            return true;

        var life = player.GetComponent<PlayerLifeCycleController>();
        return life != null && life.State != PlayerLifeState.Alive;
    }

    private static bool TryGetLocalPlayer(out NetworkObject player)
    {
        var networkManager = NetworkManager.Singleton;
        player = networkManager != null && networkManager.IsListening ? networkManager.LocalClient?.PlayerObject : null;
        return player != null && player.IsSpawned;
    }

    // 설정창 키를 그대로 쓰는 독립 액션 — ZoneBridgeGateManager 와 같은 경로(플레이어 PlayerInput 에 Interact 가 없다).
    private bool InteractPressedThisFrame()
    {
        if (Keyboard.current == null)
            return false;

        if (_interactAction == null)
        {
            var path = KeyBindingConfig.EffectivePath(InteractActionPath) ?? "<Keyboard>/f";
            _interactAction = new InputAction("LobbyInteract", InputActionType.Button, path);
            _interactAction.Enable();
        }

        return _interactAction.WasPressedThisFrame();
    }

    private void DisposeInteractAction()
    {
        if (_interactAction == null)
            return;

        _interactAction.Disable();
        _interactAction.Dispose();
        _interactAction = null;
    }
}
