using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ResultScene의 결과 표시. <see cref="SessionResult"/>를 읽어 판 결과·플레이 시간과 플레이어별 행을 채운다.
/// 참조가 비어 있으면 이름으로 자식에서 찾는다(Text_Outcome / Text_Survival / PlayerRows).
/// 행 프리팹·계층은 Docs/tech/result-stats-ui-setup.md.
/// </summary>
public sealed class ResultStatsView : MonoBehaviour
{
    [SerializeField] private TMP_Text outcomeText;
    [SerializeField] private TMP_Text survivalText;
    [Tooltip("구 팀 처치 수 텍스트. 처치 수는 플레이어 행으로 옮겨 갔으므로 있으면 숨긴다.")]
    [SerializeField] private TMP_Text killsText;

    [Header("Player Rows")]
    [Tooltip("행이 붙을 부모(VerticalLayoutGroup 권장). 비어 있으면 자식 'PlayerRows' 를 찾는다.")]
    [SerializeField] private Transform playerRowContainer;
    [Tooltip("플레이어 한 행 프리팹. 플레이어 수만큼 복제한다.")]
    [SerializeField] private ResultPlayerRowView playerRowPrefab;
    [Tooltip("캐릭터 이름·초상화 출처(로비와 같은 CharacterRoster).")]
    [SerializeField] private CharacterRoster characterRoster;

    [Header("Labels")]
    [SerializeField] private string clearedLabel = "CLEAR";
    [SerializeField] private string failedLabel = "FAILED";
    [SerializeField] private string abortedLabel = "ABORTED";
    [SerializeField] private string survivalPrefix = "생존 시간  ";

    private readonly List<ResultPlayerRowView> _rows = new List<ResultPlayerRowView>();

    private void Awake()
    {
        // ??= 는 Unity 의 가짜 null 을 못 거른다 — 명시적으로 == null 비교한다.
        if (outcomeText == null) outcomeText = FindChild<TMP_Text>("Text_Outcome");
        if (survivalText == null) survivalText = FindChild<TMP_Text>("Text_Survival");
        if (killsText == null) killsText = FindChild<TMP_Text>("Text_Kills");
        if (playerRowContainer == null) playerRowContainer = FindChild<Transform>("PlayerRows");
    }

    private void Start()
    {
        Apply();
    }

    public void Apply()
    {
        // 팀 처치 합은 표시하지 않는다(PLAN-result-stats) — 처치 수는 플레이어 행에 있다.
        if (killsText != null)
            killsText.gameObject.SetActive(false);

        if (!SessionResult.HasValue)
        {
            // 결과 없이 들어온 경우(직접 씬 실행 등) — 빈 값을 그대로 보여주지 않고 대시로 표기.
            SetText(outcomeText, "-");
            SetText(survivalText, survivalPrefix + "--:--");
            RebuildRows(null);
            return;
        }

        SetText(outcomeText, GetOutcomeLabel(SessionResult.Outcome));
        SetText(survivalText, survivalPrefix + SessionResult.FormatSurvival());
        RebuildRows(SessionResult.Players);
    }

    private string GetOutcomeLabel(SessionOutcome outcome)
    {
        switch (outcome)
        {
            case SessionOutcome.Cleared: return clearedLabel;
            case SessionOutcome.Aborted: return abortedLabel;
            default: return failedLabel;
        }
    }

    private void RebuildRows(IReadOnlyList<PlayerSessionStats> players)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] != null)
                Destroy(_rows[i].gameObject);
        }
        _rows.Clear();

        if (players == null || players.Count == 0)
            return;

        if (playerRowContainer == null || playerRowPrefab == null)
        {
            Debug.LogWarning("[ResultStatsView] 플레이어 행 컨테이너/프리팹이 비어 있어 행을 그리지 않는다 " +
                             "(Docs/tech/result-stats-ui-setup.md).", this);
            return;
        }

        ulong localClientId = GetLocalClientId();
        for (int i = 0; i < players.Count; i++)
        {
            PlayerSessionStats stats = players[i];
            ResultPlayerRowView row = Instantiate(playerRowPrefab, playerRowContainer);
            row.name = $"PlayerRow_P{i + 1}";
            row.Bind(i + 1, stats, FindCharacter(stats.CharacterId), stats.ClientId == localClientId);
            _rows.Add(row);
        }
    }

    private CharacterRoster.Entry FindCharacter(int characterId)
    {
        return characterRoster != null && characterRoster.TryGetAvailableCharacter(characterId, out CharacterRoster.Entry entry)
            ? entry
            : null;
    }

    // 결과 화면 시점에도 NetworkManager 연결은 유지된다. 오프라인이면 "나" 강조 없음.
    private static ulong GetLocalClientId()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening
            ? networkManager.LocalClientId
            : ulong.MaxValue;
    }

    private static void SetText(TMP_Text target, object value)
    {
        if (target != null)
            target.text = value?.ToString() ?? string.Empty;
    }

    private T FindChild<T>(string childName) where T : Component
    {
        foreach (T component in GetComponentsInChildren<T>(true))
        {
            if (component.gameObject.name == childName)
                return component;
        }

        return null;
    }
}
