using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 숫자를 이미지 글자로 가로로 늘어놓는다. 외곽선을 전부 아래 층, 채움을 전부 위 층에 그린다 —
/// 기울어진 글자가 겹칠 때 옆 글자의 외곽선이 채움을 덮지 않게.
/// 글자 Image 는 처음 필요할 때 만들고 이후 재사용한다(풀링된 팝업과 수명이 같다).
/// </summary>
public sealed class FloatingDamageNumberView
{
    const int MaxDigits = 10; // int 최댓값 자릿수

    readonly RectTransform _outlineLayer;
    readonly RectTransform _fillLayer;
    readonly List<Image> _outlines = new();
    readonly List<Image> _fills = new();
    readonly int[] _digits = new int[MaxDigits];
    int _shown;

    public FloatingDamageNumberView(RectTransform parent)
    {
        _outlineLayer = CreateLayer("Outlines", parent);
        _fillLayer = CreateLayer("Fills", parent);
    }

    public void SetValue(int value, FloatingDamageDigitSet digitSet, float height)
    {
        int count = SplitDigits(Mathf.Max(0, value));
        float scale = height / Mathf.Max(1f, digitSet.ReferenceHeight);
        float spacing = digitSet.Spacing * scale;

        float totalWidth = spacing * (count - 1);
        for (int i = 0; i < count; i++)
            totalWidth += Width(digitSet.GetFill(_digits[i])) * scale;

        float x = -totalWidth * 0.5f;
        for (int i = 0; i < count; i++)
        {
            int digit = _digits[i];
            float width = Width(digitSet.GetFill(digit)) * scale;
            Vector2 center = new Vector2(x + width * 0.5f, 0f);

            Place(GetImage(_outlines, _outlineLayer, i), digitSet.GetOutline(digit), center, scale);
            Place(GetImage(_fills, _fillLayer, i), digitSet.GetFill(digit), center, scale);
            x += width + spacing;
        }

        for (int i = count; i < _shown; i++)
        {
            _outlines[i].enabled = false;
            _fills[i].enabled = false;
        }

        _shown = count;
    }

    public void SetColors(Color fill, Color outline)
    {
        for (int i = 0; i < _shown; i++)
        {
            _fills[i].color = fill;
            _outlines[i].color = outline;
        }
    }

    // 높은 자리부터 _digits 에 채운다. 문자열을 만들지 않는다(타격마다 호출된다).
    int SplitDigits(int value)
    {
        int count = 0;
        do
        {
            _digits[count++] = value % 10;
            value /= 10;
        } while (value > 0 && count < MaxDigits);

        System.Array.Reverse(_digits, 0, count);
        return count;
    }

    static void Place(Image image, Sprite sprite, Vector2 center, float scale)
    {
        image.sprite = sprite;
        image.enabled = sprite != null;
        if (sprite == null)
            return;

        RectTransform rect = image.rectTransform;
        rect.sizeDelta = sprite.rect.size * scale;
        rect.anchoredPosition = center;
    }

    static float Width(Sprite sprite) => sprite != null ? sprite.rect.width : 0f;

    static Image GetImage(List<Image> images, RectTransform layer, int index)
    {
        while (images.Count <= index)
        {
            var go = new GameObject("Digit", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(layer, false);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            images.Add(image);
        }

        return images[index];
    }

    static RectTransform CreateLayer(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        return rect;
    }
}
