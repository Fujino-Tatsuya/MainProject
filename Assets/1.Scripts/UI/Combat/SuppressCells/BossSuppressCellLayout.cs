using System;

namespace VeyTrace.UI
{
    /// <summary>
    /// 보스 제압 칸 줄의 배치·표시 규칙(BossHealthHUD 가 쓴다). 칸은 가로 한 줄, 원본 자리를 가운데로 좌우 대칭.
    /// 줄 폭은 고정(기본 = HP 바 폭)이고 칸 수로 N등분한다(할 일 151): 칸 폭 = (줄 폭 − 간격×(N−1)) / N.
    /// 왼쪽부터 찬 칸이고, 줄어들면 HP 와 같은 방향(오른쪽부터) 빈 칸이 된다.
    /// </summary>
    public static class BossSuppressCellLayout
    {
        /// <summary>한 칸의 좌우 끝(px, 줄 가운데 기준). 정수 픽셀로 맞춘 값.</summary>
        public readonly struct CellSpan
        {
            public readonly float Left;
            public readonly float Right;

            public CellSpan(float left, float right)
            {
                Left = left;
                Right = right;
            }

            public float Width => Right - Left;
            public float CenterX => (Left + Right) * 0.5f;
        }

        /// <summary>줄 폭을 칸 total 개로 나눈 칸 폭(px, 반올림 전). 칸이 없거나 간격이 줄 폭을 다 먹으면 0.</summary>
        public static float CellWidth(float rowWidth, int total, float gap)
        {
            if (total <= 0) return 0f;
            return Math.Max(0f, (rowWidth - gap * (total - 1)) / total);
        }

        /// <summary>
        /// index 번째 칸의 좌우 끝 — 줄 가운데로부터의 거리(px). 줄 폭은 정수로 반올림하고, 줄 왼쪽 끝 = −⌊줄 폭/2⌋.
        /// 칸 경계는 모두 정수 픽셀이고 반올림 오차는 칸마다 ±1px 안에서 흩어진다. 마지막 칸 오른쪽 끝은 항상 줄 오른쪽 끝이다(오차를 줄 끝에서 흡수).
        /// 칸이 없거나 index 가 범위 밖이면 폭 0.
        /// </summary>
        public static CellSpan PixelCell(int index, int total, float rowWidth, float gap)
        {
            if (total <= 0 || index < 0 || index >= total) return default;

            float row = Px(Math.Max(0f, rowWidth));
            float rowLeft = -MathF.Floor(row * 0.5f);
            float cell = CellWidth(row, total, gap);
            float pitch = cell + gap;

            float left = rowLeft + Px(index * pitch);
            float right = index == total - 1
                ? rowLeft + row
                : rowLeft + Px(index * pitch + cell);
            if (right < left) right = left;
            return new CellSpan(left, right);
        }

        // 0.5 는 늘 위로(MathF.Round 기본값은 짝수 쪽이라 칸마다 반올림 방향이 달라진다).
        static float Px(float v) => MathF.Round(v, MidpointRounding.AwayFromZero);

        /// <summary>index 번째 칸이 남은 칸인가(fill 이 보인다). 아니면 frame 만 보인다.</summary>
        public static bool IsFilled(int index, int remaining) => index >= 0 && index < remaining;
    }
}
