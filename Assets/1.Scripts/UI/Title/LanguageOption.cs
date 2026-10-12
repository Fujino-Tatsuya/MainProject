using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 옵션 Gameplay 탭의 언어 선택(한국어 / English). 두 토글은 같은 ToggleGroup 이라 하나만 켜진다.
/// <see cref="Loc.Current"/> 를 그대로 읽고 쓴다(저장 = <see cref="UserLanguageConfig"/>).
/// 배치 = <c>Tools/UI/Title/옵션 Gameplay — 언어 선택 배치</c>.
/// </summary>
public sealed class LanguageOption : MonoBehaviour
{
    [SerializeField] private Toggle koreanToggle;
    [SerializeField] private Toggle englishToggle;

    private void Awake()
    {
        if (koreanToggle != null)
            koreanToggle.onValueChanged.AddListener(OnKoreanChanged);
        if (englishToggle != null)
            englishToggle.onValueChanged.AddListener(OnEnglishChanged);
    }

    private void OnEnable()
    {
        // 탭을 열 때마다 저장값으로 맞춘다. 리스너는 안 불러 같은 값을 다시 쓰지 않는다.
        bool english = Loc.Current == GameLanguage.En;
        if (koreanToggle != null)
            koreanToggle.SetIsOnWithoutNotify(!english);
        if (englishToggle != null)
            englishToggle.SetIsOnWithoutNotify(english);
    }

    private void OnDestroy()
    {
        if (koreanToggle != null)
            koreanToggle.onValueChanged.RemoveListener(OnKoreanChanged);
        if (englishToggle != null)
            englishToggle.onValueChanged.RemoveListener(OnEnglishChanged);
    }

    private static void OnKoreanChanged(bool isOn)
    {
        if (isOn)
            Loc.Current = GameLanguage.Ko;
    }

    private static void OnEnglishChanged(bool isOn)
    {
        if (isOn)
            Loc.Current = GameLanguage.En;
    }
}
