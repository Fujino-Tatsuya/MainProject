namespace VeyTrace.UI
{
    /// <summary>
    /// 보스 제압 칸 줄의 배치·표시 규칙(BossHealthHUD 가 쓴다). 칸은 가로 한 줄, 원본 자리를 가운데로 좌우 대칭.
    /// 왼쪽부터 찬 칸이고, 줄어들면 HP 와 같은 방향(오른쪽부터) 빈 칸이 된다.
    /// </summary>
    public static class BossSuppressCellLayout
    {
        /// <summary>index 번째 칸 중심의 x — 줄 가운데(원본 자리)로부터의 거리(px).</summary>
        public static float CellCenterX(int index, int total, float cellWidth, float gap)
        {
            if (total <= 0) return 0f;
            float rowWidth = RowWidth(total, cellWidth, gap);
            return -rowWidth * 0.5f + cellWidth * 0.5f + index * (cellWidth + gap);
        }

        /// <summary>칸 total 개와 사이 간격을 합친 줄 전체 폭(px).</summary>
        public static float RowWidth(int total, float cellWidth, float gap)
        {
            if (total <= 0) return 0f;
            return cellWidth * total + gap * (total - 1);
        }

        /// <summary>index 번째 칸이 남은 칸인가(fill 이 보인다). 아니면 frame 만 보인다.</summary>
        public static bool IsFilled(int index, int remaining) => index >= 0 && index < remaining;
    }
}
