using TMPro;
using UnityEngine;

/// <summary>
/// 고정 라벨 하나를 <see cref="SettingsStrings"/> 키로 채운다(켜질 때 + 언어가 바뀔 때).
/// 설정 패널에는 저작 메뉴가 영어 원문으로 키를 역조회해 자동으로 붙인다 — 손으로 붙일 일은 없다.
/// 코드가 매번 바꾸는 문구(드롭다운 항목·값 표시·확인 창 카운트다운)에는 붙이지 않는다 — 각 컨트롤러가 채운다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class LocalizedText : MonoBehaviour
{
    [SerializeField] string key;

    public string Key => key;

    void OnEnable()
    {
        LanguageConfig.Changed += Apply;
        Apply();
    }

    void OnDisable() => LanguageConfig.Changed -= Apply;

    void Apply()
    {
        if (string.IsNullOrEmpty(key)) return;
        GetComponent<TMP_Text>().text = SettingsStrings.Get(key);
    }
}
