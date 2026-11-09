using UnityEngine;

/// <summary>
/// 획득 특성 다시 보기 줄의 벌집 배치(할 일 253) — 순수 함수, EditMode 로 확인한다.
/// 평평한 윗변 육각형 2열 지그재그: 획득 순서 i 의 열 = i % 2, 행 = i / 2. 왼쪽 열(0·2·4…)은 아래에서 위로,
/// 오른쪽 열(1·3·5…)은 반 칸 올라가 왼쪽 열 사이에 끼운다(열 간격 = 칸 폭 × 0.75 — 육각형 맞물림).
/// 좌표 = 담는 상자 왼쪽 아래를 원점으로 한 칸 가운데.
/// </summary>
public static class TraitIconHoneycomb
{
    public static Vector2 CellCenter(int index, Vector2 cellSize, float gap)
    {
        int column = index % 2;
        int row = index / 2;
        float stepY = cellSize.y + gap;
        return new Vector2(
            cellSize.x * 0.5f + column * (cellSize.x * 0.75f + gap),
            cellSize.y * 0.5f + row * stepY + column * stepY * 0.5f);
    }
}
