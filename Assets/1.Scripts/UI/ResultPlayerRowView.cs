using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ResultScene 플레이어 한 행. <see cref="ResultStatsView"/> 가 행 프리팹으로 복제해 채운다.
/// 참조가 비어 있으면 이름으로 자식에서 찾는다 — 계층·이름은 Docs/tech/result-stats-ui-setup.md.
/// </summary>
public sealed class ResultPlayerRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text slotText;      // Text_Slot — "P1"
    [SerializeField] private TMP_Text characterText; // Text_Character — 캐릭터 이름
    [SerializeField] private Image portraitImage;    // Image_Portrait — 캐릭터 초상화
    [SerializeField] private TMP_Text damageText;    // Text_Damage
    [SerializeField] private TMP_Text killsText;     // Text_Kills
    [SerializeField] private TMP_Text countersText;  // Text_Counters
    [SerializeField] private TMP_Text deathsText;    // Text_Deaths
    [SerializeField] private GameObject localMarker; // Marker_Local — 로컬 플레이어("나") 강조

    [Header("Labels")]
    [SerializeField] private string slotFormat = "P{0}";
    [SerializeField] private string unknownCharacterLabel = "-";

    private bool _resolved;

    public void Bind(int slotNumber, PlayerSessionStats stats, CharacterRoster.Entry character, bool isLocal)
    {
        ResolveReferences();

        SetText(slotText, string.Format(slotFormat, slotNumber));
        SetText(characterText, character != null ? character.DisplayName : unknownCharacterLabel);
        SetText(damageText, stats.Damage);
        SetText(killsText, stats.Kills);
        SetText(countersText, stats.Counters);
        SetText(deathsText, stats.Deaths);

        if (portraitImage != null)
        {
            Sprite portrait = character != null ? character.Portrait : null;
            portraitImage.sprite = portrait;
            portraitImage.enabled = portrait != null;
        }

        if (localMarker != null)
            localMarker.SetActive(isLocal);
    }

    private void ResolveReferences()
    {
        if (_resolved)
            return;

        _resolved = true;
        // ??= 는 Unity 의 가짜 null 을 못 거른다 — 명시적으로 == null 비교한다.
        if (slotText == null) slotText = FindChild<TMP_Text>("Text_Slot");
        if (characterText == null) characterText = FindChild<TMP_Text>("Text_Character");
        if (portraitImage == null) portraitImage = FindChild<Image>("Image_Portrait");
        if (damageText == null) damageText = FindChild<TMP_Text>("Text_Damage");
        if (killsText == null) killsText = FindChild<TMP_Text>("Text_Kills");
        if (countersText == null) countersText = FindChild<TMP_Text>("Text_Counters");
        if (deathsText == null) deathsText = FindChild<TMP_Text>("Text_Deaths");
        if (localMarker == null)
        {
            Transform marker = FindChild<Transform>("Marker_Local");
            if (marker != null)
                localMarker = marker.gameObject;
        }
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
