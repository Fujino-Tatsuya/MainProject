using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 글자가 그려진 이미지의 언어별 Sprite. 같은 오브젝트의 Image 에 현재 언어 Sprite 를 넣고 <see cref="Loc.Changed"/> 때 다시 넣는다.
/// en Sprite 가 없으면 ko 를 쓰고 경고를 한 번만 남긴다.
/// </summary>
[RequireComponent(typeof(Image))]
public sealed class LocalizedImage : MonoBehaviour
{
    [SerializeField] private Sprite ko;
    [SerializeField] private Sprite en;

    private Image _image;
    private bool _warnedMissingEn;

    private void Awake()
    {
        _image = GetComponent<Image>();
    }

    private void OnEnable()
    {
        Loc.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        Loc.Changed -= Refresh;
    }

    public void Refresh()
    {
        if (_image == null)
            return;

        Sprite sprite = Select(ko, en, Loc.Current, out bool fellBack);
        if (fellBack && !_warnedMissingEn)
        {
            _warnedMissingEn = true;
            Debug.LogWarning($"[Loc] '{name}' 의 en Sprite 가 없어 ko 를 쓴다.", this);
        }

        _image.sprite = sprite;
    }

    /// <summary>언어에 맞는 Sprite. en 인데 en 이 없으면 ko(fellBack = true).</summary>
    public static Sprite Select(Sprite koSprite, Sprite enSprite, GameLanguage language, out bool fellBack)
    {
        fellBack = language == GameLanguage.En && enSprite == null;
        return language == GameLanguage.En && enSprite != null ? enSprite : koSprite;
    }
}
