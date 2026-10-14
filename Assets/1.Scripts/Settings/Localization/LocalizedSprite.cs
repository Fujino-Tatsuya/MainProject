using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 언어별 이미지 하나를 <see cref="Image"/> 에 채운다(켜질 때 + 언어가 바뀔 때). <see cref="LocalizedText"/> 의 이미지판.
/// 첫 사용처 = CombatHUD 키가이드(keyguide.png / SVN r429 keyguide_en.png, 둘 다 197×392 — 크기 맞춤 없음).
/// 영어 이미지가 비어 있으면 한국어 이미지를 그대로 둔다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class LocalizedSprite : MonoBehaviour
{
    [SerializeField] Sprite korean;
    [SerializeField] Sprite english;

    void OnEnable()
    {
        LanguageConfig.Changed += Apply;
        Apply();
    }

    void OnDisable() => LanguageConfig.Changed -= Apply;

    void Apply()
    {
        Sprite sprite = LanguageConfig.Current == GameLanguage.English && english != null ? english : korean;
        if (sprite != null) GetComponent<Image>().sprite = sprite;
    }
}
