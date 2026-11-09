using UnityEngine;

/// <summary>
/// 네이티브 커서(<see cref="WindowsNativeCursor"/>)에 넘길 픽셀·핫스팟 계산 — 플랫폼 무관 순수 함수라 EditMode 로 확인한다.
/// </summary>
public static class NativeCursorPixels
{
    /// <summary>
    /// Unity <c>GetPixels32</c>(행 0 = 아래, RGBA)를 Windows 32bpp top-down DIB(행 0 = 위, BGRA)로 바꾼다.
    /// 알파는 스트레이트(프리멀티 아님) — 32비트 아이콘·커서 형식이 스트레이트 알파를 받는다.
    /// </summary>
    public static byte[] ToTopDownBgra(Color32[] pixels, int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = (height - 1 - y) * width;
            int targetRow = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                Color32 c = pixels[sourceRow + x];
                int i = targetRow + x * 4;
                bgra[i] = c.b;
                bgra[i + 1] = c.g;
                bgra[i + 2] = c.r;
                bgra[i + 3] = c.a;
            }
        }
        return bgra;
    }

    /// <summary>
    /// 원본 기준 핫스팟(좌상단 원점, <c>Cursor.SetCursor</c> 와 같은 기준)을 배율만큼 옮기고 확대본 안으로 자른다.
    /// </summary>
    public static Vector2Int ScaledHotspot(Vector2 hotspot, float scale, int width, int height)
    {
        return new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt(hotspot.x * scale), 0, Mathf.Max(0, width - 1)),
            Mathf.Clamp(Mathf.RoundToInt(hotspot.y * scale), 0, Mathf.Max(0, height - 1)));
    }
}
