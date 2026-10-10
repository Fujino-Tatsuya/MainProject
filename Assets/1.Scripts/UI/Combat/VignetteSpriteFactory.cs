using UnityEngine;

/// <summary>
/// 아트 스프라이트가 들어오기 전 화면 가장자리 비네팅용 임시 스프라이트 — 중앙 투명, 가장자리로 갈수록 불투명한 흰색.
/// 체력·실드·인터럽트 비네팅(+ 인터럽트 슬롯 발광)이 각자 하나씩 만들어 쓴다(공유하지 않음). 만든 쪽이 <see cref="Destroy"/> 로 정리한다.
/// </summary>
public static class VignetteSpriteFactory
{
    /// <param name="size">정사각 텍스처 한 변 픽셀 수.</param>
    /// <param name="innerRadius">이 거리까지 완전 투명(0 = 중앙, 1 = 변의 중점, √2 = 모서리).</param>
    /// <param name="outerRadius">이 거리부터 완전 불투명.</param>
    public static Sprite CreateRadial(string name, int size, float innerRadius, float outerRadius)
    {
        size = Mathf.Max(2, size);
        Texture2D texture = CreateTexture(name, size);

        var pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerRadius, outerRadius, distance));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        return CreateSprite(texture, pixels, name, size);
    }

    /// <summary>
    /// 사각 테두리 발광 — 가운데·바깥 끝 투명, <paramref name="peakEdge"/> 에서 가장 불투명한 흰색 띠(인터럽트 슬롯 강조).
    /// 거리는 사각 기준(max(|x|,|y|), 0 = 중앙, 1 = 변).
    /// </summary>
    /// <param name="innerEdge">이 거리까지 완전 투명.</param>
    /// <param name="peakEdge">가장 불투명한 거리. 여기서 변(1)까지 다시 투명해진다.</param>
    public static Sprite CreateSquareGlow(string name, int size, float innerEdge, float peakEdge)
    {
        size = Mathf.Max(2, size);
        Texture2D texture = CreateTexture(name, size);

        var pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Max(Mathf.Abs((x - half) / half), Mathf.Abs((y - half) / half));
                float alpha = distance < peakEdge
                    ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerEdge, peakEdge, distance))
                    : Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(peakEdge, 1f, distance));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        return CreateSprite(texture, pixels, name, size);
    }

    private static Texture2D CreateTexture(string name, int size)
    {
        return new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
    }

    private static Sprite CreateSprite(Texture2D texture, Color32[] pixels, string name, int size)
    {
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        sprite.name = name;
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    /// <summary><see cref="CreateRadial"/> 로 만든 스프라이트와 텍스처를 함께 파괴한다.</summary>
    public static void Destroy(Sprite generated)
    {
        if (generated == null)
            return;

        Texture2D texture = generated.texture;
        Object.Destroy(generated);
        if (texture != null)
            Object.Destroy(texture);
    }
}
