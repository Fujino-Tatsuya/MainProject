using System.Collections.Generic;
using System.Globalization;
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

    [Header("Optional (로비 결과 패널 — 없으면 무시)")]
    [SerializeField] private TMP_Text healingText;     // Text_Healing — 회복량
    [SerializeField] private TMP_Text damageTakenText; // Text_DamageTaken — 입은 피해
    [SerializeField] private TMP_Text rankText;        // Text_Rank — 순위 숫자(왕관 행은 숨김)
    [SerializeField] private Image crownImage;         // Image_Crown — 1위 왕관 배지

    [Header("Labels")]
    [SerializeField] private string slotFormat = "P{0}";
    [SerializeField] private string unknownCharacterLabel = "-";

    private bool _resolved;

    public void Bind(int slotNumber, PlayerSessionStats stats, CharacterRoster.Entry character, bool isLocal)
    {
        ResolveReferences();

        SetText(slotText, SlotText(slotNumber, slotFormat));
        SetText(characterText, character != null ? character.DisplayName : unknownCharacterLabel);
        SetText(damageText, stats.Damage);
        SetText(killsText, stats.Kills);
        SetText(countersText, stats.Counters);
        SetText(deathsText, stats.Deaths);
        SetText(healingText, stats.Healing);
        SetText(damageTakenText, stats.DamageTaken);
        SetRank(0, false); // 순위를 매기는 쪽(로비 패널)만 SetRank 로 다시 채운다

        if (portraitImage != null)
        {
            Sprite portrait = character != null ? character.Portrait : null;
            portraitImage.sprite = portrait;
            portraitImage.enabled = portrait != null;
        }

        if (localMarker != null)
            localMarker.SetActive(isLocal);
    }

    /// <summary>
    /// 순위 셀. <paramref name="crowned"/> 면 왕관만, 아니면 순위 숫자만 보인다. rank 0 이하 = 순위 없음(둘 다 숨김).
    /// 순위 셀이 없는 행(5.ResultScene)은 아무것도 안 한다.
    /// </summary>
    public void SetRank(int rank, bool crowned)
    {
        ResolveReferences();

        if (crownImage != null)
            crownImage.enabled = crowned;

        if (rankText != null)
        {
            rankText.text = rank > 0 ? rank.ToString(CultureInfo.InvariantCulture) : string.Empty;
            rankText.enabled = !crowned && rank > 0;
        }
    }

    /// <summary>
    /// 슬롯 라벨 —<c>UI.Result.PlayerSlot</c>({n}). 시트에 행이 없으면 직렬화 <c>slotFormat</c>(<c>P{0}</c>, string.Format 형식)
    /// — 빈 값이면 키의 ko. 자리표시자 이름이 다르다(시트 <c>{n}</c>, 프리팹 <c>{0}</c>).
    /// </summary>
    public static string SlotText(int slotNumber, string serializedFormat)
    {
        string fallback = string.IsNullOrEmpty(serializedFormat)
            ? UiStrings.ResultPlayerSlot.Ko
            : string.Format(serializedFormat, slotNumber);
        return UiStrings.ResultPlayerSlot.FormatOr(fallback,
            new Dictionary<string, string> { ["n"] = slotNumber.ToString(CultureInfo.InvariantCulture) });
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
        if (healingText == null) healingText = FindChild<TMP_Text>("Text_Healing");
        if (damageTakenText == null) damageTakenText = FindChild<TMP_Text>("Text_DamageTaken");
        if (rankText == null) rankText = FindChild<TMP_Text>("Text_Rank");
        if (crownImage == null) crownImage = FindChild<Image>("Image_Crown");
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
