using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로비 상단 플레이어 슬롯 3칸(이름·캐릭터·준비)과 <see cref="LobbySession"/> 사이의 얇은 뷰 — PLAN-48 C8·C9.
/// 상태는 갖지 않는다. 읽기 = LobbySession 의 복제 자리 목록, 쓰기 = LobbySession 의 RPC(준비·캐릭터 요청 진입점 — 상호작용 C10·선택 패널 C12 가 부른다).
/// 옛 Ready·캐릭터 버튼은 C9 걷는 로비에서 씬째 지웠다.
/// </summary>
public class LobbyUIController : MonoBehaviour
{
    public static LobbyUIController Active { get; private set; }

    public event System.Action StateChanged;

    public CharacterRoster Roster => characterRoster;

    [SerializeField] private LobbyPlayerSlotView[] slots;
    [SerializeField] private CharacterRoster characterRoster;

    private void Awake()
    {
        Active = this;
    }

    private void OnEnable()
    {
        LobbySession.StateChanged += ApplyUi;
        ApplyUi();
    }

    private void OnDisable()
    {
        LobbySession.StateChanged -= ApplyUi;
    }

    private void OnDestroy()
    {
        if (Active == this)
        {
            Active = null;
        }
    }

    public void ToggleLocalReady()
    {
        var session = LobbySession.Active;
        if (session == null)
        {
            Debug.LogWarning("[SceneFlow] LobbyUIController.ToggleLocalReady — 스폰된 LobbySession 이 없다.");
            return;
        }

        session.RequestToggleReady();
    }

    public void RequestLocalCharacterSelection(int characterId)
    {
        LobbySession.Active?.RequestCharacter(characterId);
    }

    public int GetLocalCharacterSelectionId()
    {
        return TryGetLocalEntry(out var entry) ? entry.CharacterId : LobbySession.DefaultCharacterId;
    }

    public bool IsLocalReady()
    {
        return TryGetLocalEntry(out var entry) && entry.Ready;
    }

    /// <summary>내 캐릭터 변경이 막혔는가(준비 상태·카운트다운 중) — 서버도 같은 규칙으로 거부한다. 호스트 예외 없음.</summary>
    public bool IsLocalCharacterSelectionLocked()
    {
        var session = LobbySession.Active;
        return IsLocalReady() || (session != null && session.IsCountdownActive);
    }

    private static bool TryGetLocalEntry(out LobbyPlayerEntry entry)
    {
        var session = LobbySession.Active;
        var networkManager = NetworkManager.Singleton;
        if (session != null && networkManager != null && networkManager.IsListening)
        {
            return session.TryGetEntry(networkManager.LocalClientId, out entry);
        }

        entry = default;
        return false;
    }

    private Sprite GetCharacterPortrait(int characterId)
    {
        return characterRoster != null && characterRoster.TryGetAvailableCharacter(characterId, out var entry)
            ? entry.Portrait
            : null;
    }

    // 이름 = 머리 위 이름과 같은 "P{clientId+1}"(UiStrings.OverheadPlayerLabel), 뒤에 캐릭터 이름.
    private string BuildSlotLabel(LobbyPlayerEntry entry)
    {
        var playerLabel = UiStrings.OverheadPlayerLabel.Format(
            new System.Collections.Generic.Dictionary<string, string> { ["n"] = (entry.ClientId + 1).ToString() });
        return characterRoster != null && characterRoster.TryGetAvailableCharacter(entry.CharacterId, out var character)
            ? $"{playerLabel} · {character.DisplayName}"
            : playerLabel;
    }

    private void ApplyUi()
    {
        if (slots != null)
        {
            var session = LobbySession.Active;
            var count = session != null ? session.EntryCount : 0;

            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                if (i >= count)
                {
                    slot.SetState(false, false, null);
                    continue;
                }

                var entry = session.GetEntry(i);
                slot.SetState(true, entry.Ready, GetCharacterPortrait(entry.CharacterId), BuildSlotLabel(entry));
            }
        }

        StateChanged?.Invoke();
    }
}
