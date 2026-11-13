using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로비 상단 플레이어 슬롯 3칸(이름·캐릭터·준비)과 <see cref="LobbySession"/> 사이의 얇은 뷰 — PLAN-48 C8·C9.
/// 상태는 갖지 않는다. 읽기 = LobbySession 의 복제 자리 목록, 쓰기 = LobbySession 의 캐릭터 요청 RPC(선택 패널 C12 가 부른다).
/// 옛 Ready·캐릭터 버튼은 C9 걷는 로비에서 씬째 지웠고, 준비 토글 진입점은 할 일 86 T2b 에서 지웠다(출격 집결로 대체).
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

    public void RequestLocalCharacterSelection(int characterId)
    {
        LobbySession.Active?.RequestCharacter(characterId);
    }

    public int GetLocalCharacterSelectionId()
    {
        return TryGetLocalEntry(out var entry) ? entry.CharacterId : LobbySession.DefaultCharacterId;
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

    private CharacterRoster.Entry GetCharacter(int characterId)
    {
        return characterRoster != null && characterRoster.TryGetAvailableCharacter(characterId, out var entry)
            ? entry
            : null;
    }

    // 배지 = 머리 위 이름과 같은 "P{n}"(UiStrings.OverheadPlayerLabel). 접속 자리는 clientId+1, 빈 자리는 칸 번호.
    private static string PlayerLabel(int number) =>
        UiStrings.OverheadPlayerLabel.Format(new System.Collections.Generic.Dictionary<string, string> { ["n"] = number.ToString() });

    // 이름 줄 = 캐릭터 이름(고르기 전이면 비움).
    private static string CharacterName(CharacterRoster.Entry character) => character != null ? character.DisplayName : string.Empty;

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
                    slot.SetState(false, false, null, PlayerLabel(i + 1), null);
                    continue;
                }

                var entry = session.GetEntry(i);
                var character = GetCharacter(entry.CharacterId);
                slot.SetState(true, entry.Ready, character, PlayerLabel((int)entry.ClientId + 1), CharacterName(character));
            }
        }

        StateChanged?.Invoke();
    }
}
