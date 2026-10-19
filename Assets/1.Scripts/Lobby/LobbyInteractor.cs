using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 로비 상호작용 입력(각 피어 로컬, 3.LobbyScene Management) — PLAN-48 §4.6, C10. 원본 패턴 = <see cref="ZoneBridgeGateManager"/>.
/// 매 프레임 로컬 PlayerObject 위치로 <see cref="NearestInteractable.Pick"/> → 고른 대상에 <see cref="InteractPrompt"/> 를 띄우고,
/// 상호작용 키(설정창 <c>Player/Interact</c>, 기본 F)를 누르면 종류별로 실행한다(Ready = <see cref="LobbySession.RequestToggleReady"/>).
/// 판정 권한은 서버다 — 서버가 거리를 다시 본다. 여기 검사는 RPC·프롬프트용이다.
/// <list type="bullet">
/// <item>막힘: UI 모달(결과 패널 등 <see cref="UiInputGateManager"/>) 또는 플레이어 입력 차단 토큰·생명주기(Alive 아님)면 프롬프트를 끄고 F 를 무시한다.
/// ESC 는 쓰지 않는다 — 결과 패널의 ESC 소유와 겹치지 않는다.</item>
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

    private void OnEnable()
    {
        KeyBindingConfig.Changed += DisposeInteractAction;   // 다음 프레임에 새 키로 다시 만든다
    }

    private void OnDisable()
    {
        KeyBindingConfig.Changed -= DisposeInteractAction;
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
        if (session == null || !TryGetLocalPlayer(out var player) || IsBlocked(player))
        {
            SetCurrent(null);
            return;
        }

        var target = PickTarget(player.transform.position, session);
        SetCurrent(target);
        if (target == null)
            return;

        target.ApplyPromptText(target.GetPromptString(IsLocalReady(session)).Text);

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
            _candidates.Add(new NearestInteractable.Candidate(target.Position, target.Radius, IsUsable(target, session)));
        }

        var index = NearestInteractable.Pick(playerPosition, _candidates);
        return index >= 0 ? _candidateTargets[index] : null;
    }

    // CharacterSelect 는 C12 에서 패널을 붙일 때까지 고르지 않는다(눌러도 아무 일 없는 프롬프트를 띄우지 않는다).
    private static bool IsUsable(LobbyInteractable target, LobbySession session)
    {
        switch (target.Kind)
        {
            case LobbyInteractableKind.Ready:
                return session.IsSpawned;
            default:
                return false;
        }
    }

    private void Interact(LobbyInteractable target, LobbySession session)
    {
        switch (target.Kind)
        {
            case LobbyInteractableKind.Ready:
                Debug.Log($"[SceneFlow] {Tag} F: 준비 토글 요청 (ready={IsLocalReady(session)})", this);
                session.RequestToggleReady();
                break;
        }
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

        var life = player.GetComponent<PlayerLifeCycleController>();
        return life != null && life.State != PlayerLifeState.Alive;
    }

    private static bool IsLocalReady(LobbySession session)
    {
        var networkManager = NetworkManager.Singleton;
        return networkManager != null && session.TryGetEntry(networkManager.LocalClientId, out var entry) && entry.Ready;
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
