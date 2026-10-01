using UnityEngine;

/// <summary>
/// 데미지 숫자 이미지 글꼴. 숫자마다 채움(흰색 — 피해 유형 색으로 칠함)과 외곽선 스프라이트 한 쌍.
/// 배열 인덱스 = 숫자(0~9). 원본 시트(Assets/50.Art/UI/damage)는 1·2·…·9·0 순서라 `_9` 가 숫자 0 이다.
/// 채움과 외곽선은 각 스프라이트의 중심을 맞춰 겹친다.
/// </summary>
[CreateAssetMenu(fileName = "FloatingDamageDigitSet", menuName = "Combat/Floating Damage Digit Set")]
public sealed class FloatingDamageDigitSet : ScriptableObject
{
    public const int DigitCount = 10;

    [Tooltip("인덱스 = 숫자(0~9). 흰색으로 그려 두고 피해 유형 색을 곱한다.")]
    [SerializeField] Sprite[] fill = new Sprite[DigitCount];
    [Tooltip("인덱스 = 숫자(0~9).")]
    [SerializeField] Sprite[] outline = new Sprite[DigitCount];
    [Tooltip("글자 사이 간격(원본 px). 기울어진 글꼴이라 음수로 겹친다.")]
    [SerializeField] float spacing = -12f;
    [SerializeField] Color outlineColor = Color.white;

    public float Spacing => spacing;
    public Color OutlineColor => outlineColor;

    /// <summary>원본 글자 높이(px). 표시 높이를 이 값으로 나눠 배율을 구한다.</summary>
    public float ReferenceHeight
    {
        get
        {
            Sprite zero = GetFill(0);
            return zero != null ? zero.rect.height : 1f;
        }
    }

    public Sprite GetFill(int digit) => Get(fill, digit);
    public Sprite GetOutline(int digit) => Get(outline, digit);

    static Sprite Get(Sprite[] sprites, int digit)
    {
        return sprites != null && digit >= 0 && digit < sprites.Length ? sprites[digit] : null;
    }
}
