using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로비 상단 플레이어 슬롯 3칸(이름·캐릭터·준비)과 <see cref="LobbySession"/> 사이의 얇은 뷰 — PLAN-48 C8·C9.
/// 상태는 갖지 않는다. 읽기 = LobbySession 의 복제 자리 목록, 쓰기 = LobbySession 의 캐릭터 요청 RPC(선택 패널 C12 가 부른다).
/// 옛 Ready·캐릭터 버튼은 C9 걷는 로비에서 씬째 지웠고, 준비 토글 진입점은 할 일 86 T2b 에서 지웠다(출격 집결로 대체).
/// 슬롯 "준비 완료" = <see cref="LobbySlotReadyRules"/>(브리핑 끝 + 출격 구역 안, 할 일 276) — Docs/tech/lobby-player-slots.md.
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

    // 준비 표시는 위치에 따라 바뀌므로 출격 집결 동안만 짧은 주기로 다시 계산하고, 결과가 달라질 때만 슬롯을 갱신한다.
    private const float ReadyPollInterval = 0.1f;
    private float _nextReadyPoll;
    private int _lastReadyMask = -1;

    private void Update()
    {
        if (Time.unscaledTime < _nextReadyPoll)
        {
            return;
        }

        _nextReadyPoll = Time.unscaledTime + ReadyPollInterval;
        if (ComputeReadyMask() != _lastReadyMask)
        {
            ApplyUi();
        }
    }

    // 비트 i = 자리 i 가 "준비 완료"(LobbySlotReadyRules).
    private static int ComputeReadyMask()
    {
        var session = LobbySession.Active;
        var count = session != null ? session.EntryCount : 0;
        if (count == 0)
        {
            return 0;
        }

        var launchCommitted = LobbyTutorialState.IsAvailable && LobbyTutorialState.LaunchCommitted;
        var sortieZone = LobbyTutorialState.ActiveGatherPointId == LobbyGatherPoint.Sortie ? LobbyTutorialState.ActiveGatherZone : null;
        if (!launchCommitted && sortieZone == null)
        {
            return 0;
        }

        var mask = 0;
        for (var i = 0; i < count && i < 31; i++)
        {
            var inZone = false;
            var incapacitated = true;
            if (sortieZone != null && TryFindPlayer(session.GetEntry(i).ClientId, out var player))
            {
                inZone = sortieZone.ContainsPoint(player.transform.position);
                incapacitated = PartyGatherZone.IsIncapacitated(player);
            }

            if (LobbySlotReadyRules.IsReady(launchCommitted, sortieZone != null, inZone, incapacitated))
            {
                mask |= 1 << i;
            }
        }

        return mask;
    }

    // [전 피어] clientId 의 PlayerObject. SpawnManager.GetPlayerNetworkObject 는 클라에서 남의 것을 찾으면 에러를 찍어 스폰 목록을 훑는다
    // (GameSoundHooks.TryFindPlayer 와 같은 방식, 출격 집결 동안 0.1초마다만).
    private static bool TryFindPlayer(ulong clientId, out Player player)
    {
        player = null;
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.SpawnManager == null)
        {
            return false;
        }

        foreach (var networkObject in networkManager.SpawnManager.SpawnedObjectsList)
        {
            if (networkObject != null && networkObject.IsPlayerObject && networkObject.OwnerClientId == clientId
                && networkObject.TryGetComponent(out player))
            {
                return true;
            }
        }

        player = null;
        return false;
    }

    private void ApplyUi()
    {
        var readyMask = ComputeReadyMask();
        _lastReadyMask = readyMask;

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
                var ready = (readyMask & (1 << i)) != 0;
                slot.SetState(true, ready, character, PlayerLabel((int)entry.ClientId + 1), CharacterName(character));
            }
        }

        StateChanged?.Invoke();
    }
}

/// <summary>
/// 로비 상단 슬롯 "준비 완료" 기준(할 일 276, 사용자 결정) — 브리핑이 끝난 뒤(출격 집결 L40) 그 플레이어가 출격 구역(런치패드, <see cref="LobbyGatherPoint.Sortie"/>) 안.
/// 구역 안·쓰러짐 판정은 서버 집결 판정(<see cref="PartyGatherZone"/>)과 같은 함수를 전 피어가 복제된 위치·생명 상태로 돌린다(새 복제 값 없음).
/// 출격 확정 뒤(로딩)는 확정 = 전원 집결이었으므로 모두 준비 완료로 둔다. 옛 기준 <c>LobbyPlayerEntry.Ready</c>(준비 토글, qa/LobbyDemo 하네스 전용)는 슬롯에 쓰지 않는다.
/// </summary>
public static class LobbySlotReadyRules
{
    /// <param name="launchCommitted">출격 확정(스냅샷 launchCommitted).</param>
    /// <param name="sortieGatherActive">지금 단계 = 출격 집결(활성 집결 지점 Sortie) — 브리핑 끝.</param>
    /// <param name="inSortieZone">그 플레이어 본체가 출격 구역 박스 안.</param>
    /// <param name="incapacitated">쓰러짐·사망(PartyGatherZone.IsIncapacitated) 또는 PlayerObject 없음.</param>
    public static bool IsReady(bool launchCommitted, bool sortieGatherActive, bool inSortieZone, bool incapacitated)
    {
        if (launchCommitted)
            return true;
        return sortieGatherActive && inSortieZone && !incapacitated;
    }
}
