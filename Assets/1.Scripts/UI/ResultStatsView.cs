using TMPro;
using UnityEngine;

/// <summary>
/// ResultScene의 결과 표시. <see cref="SessionResult"/>를 읽어 텍스트에 채운다.
/// 참조가 비어 있으면 이름으로 자식에서 찾는다(Text_Outcome / Text_Survival / Text_Kills).
/// </summary>
public sealed class ResultStatsView : MonoBehaviour
{
    [SerializeField] private TMP_Text outcomeText;
    [SerializeField] private TMP_Text survivalText;
    [SerializeField] private TMP_Text killsText;

    [Header("Labels")]
    [SerializeField] private string clearedLabel = "CLEAR";
    [SerializeField] private string failedLabel = "FAILED";
    [SerializeField] private string survivalPrefix = "생존 시간  ";
    [SerializeField] private string killsPrefix = "처치  ";

    private void Awake()
    {
        outcomeText ??= FindText("Text_Outcome");
        survivalText ??= FindText("Text_Survival");
        killsText ??= FindText("Text_Kills");
    }

    private void Start()
    {
        Apply();
    }

    public void Apply()
    {
        if (!SessionResult.HasValue)
        {
            // 결과 없이 들어온 경우(직접 씬 실행 등) — 빈 값을 그대로 보여주지 않고 대시로 표기.
            SetText(outcomeText, "-");
            SetText(survivalText, survivalPrefix + "--:--");
            SetText(killsText, killsPrefix + "-");
            return;
        }

        SetText(outcomeText, SessionResult.Cleared ? clearedLabel : failedLabel);
        SetText(survivalText, survivalPrefix + SessionResult.FormatSurvival());
        SetText(killsText, killsPrefix + SessionResult.Kills);
    }

    private static void SetText(TMP_Text target, object value)
    {
        if (target != null)
            target.text = value?.ToString() ?? string.Empty;
    }

    private TMP_Text FindText(string childName)
    {
        foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.gameObject.name == childName)
                return text;
        }

        return null;
    }
}
