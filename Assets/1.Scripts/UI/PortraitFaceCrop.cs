using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 작은 초상화 칸에서 상반신 일러스트의 얼굴만 보이게 하는 크롭 값(할 일 276 후속).
/// 캐릭터마다 <see cref="CharacterRoster.Entry.FaceCrop"/> 에 둔다. 원본 스프라이트를 그대로 쓰고,
/// 초상화 Image 를 키우고 옮길 뿐이다 — 잘라내기는 부모의 <see cref="RectMask2D"/>·<see cref="Mask"/> 가 한다.
/// </summary>
[Serializable]
public struct PortraitFaceCrop
{
    [Tooltip("얼굴 가운데 — 스프라이트 안 비율 좌표(0,0 = 왼쪽 아래, 1,1 = 오른쪽 위). 칸 가운데에 온다.")]
    public Vector2 center;

    [Tooltip("칸(짧은 변)에 꽉 차게 보일 정사각 영역 한 변 = 스프라이트 너비의 몇 배. 작을수록 확대. 1 = 그림 전체.")]
    [Range(0.05f, 1f)]
    public float size;

    public static PortraitFaceCrop Full => new PortraitFaceCrop { center = new Vector2(0.5f, 0.5f), size = 1f };

    /// <summary>
    /// 칸 크기 <paramref name="viewport"/>·스프라이트 크기 <paramref name="spriteSize"/> 에서 초상화 Image 크기와
    /// 칸 가운데 기준 위치(가운데 앵커·가운데 피벗)를 구한다. 스프라이트가 없거나 값이 이상하면 칸에 꽉 채운다.
    /// </summary>
    public static void Layout(Vector2 viewport, Vector2 spriteSize, PortraitFaceCrop crop, out Vector2 size, out Vector2 position)
    {
        if (spriteSize.x <= 0f || spriteSize.y <= 0f || crop.size <= 0f)
        {
            size = viewport;
            position = Vector2.zero;
            return;
        }

        float scale = Mathf.Min(viewport.x, viewport.y) / (crop.size * spriteSize.x);
        size = spriteSize * scale;
        position = new Vector2((0.5f - crop.center.x) * size.x, (0.5f - crop.center.y) * size.y);
    }

    /// <summary>
    /// <paramref name="image"/> 의 부모에 <see cref="RectMask2D"/>(사각) 또는 <see cref="Mask"/>(원형 등 그래픽 모양)가 있으면 그 칸 안에서 얼굴이 가운데 오게 Image 를 키우고 옮긴다.
    /// 마스크가 없는 칸(예: 5.ResultScene 행)은 건드리지 않는다. 스프라이트가 null 이면 칸에 꽉 채운다(미접속 회색 칸).
    /// </summary>
    public static void Apply(Image image, PortraitFaceCrop crop)
    {
        if (image == null || !(image.transform.parent is RectTransform viewport) || (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null))
            return;

        Sprite sprite = image.sprite;
        Vector2 spriteSize = sprite != null ? sprite.rect.size : Vector2.zero;
        Layout(viewport.rect.size, spriteSize, crop, out Vector2 size, out Vector2 position);

        RectTransform rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        image.preserveAspect = false;
    }
}
