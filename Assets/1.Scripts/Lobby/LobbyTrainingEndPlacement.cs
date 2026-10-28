using UnityEngine;

/// <summary>
/// 집결 확정 텔레포트 자리(할 일 111) — 대상 집결 구역 박스 바닥 안 격자 자리를 고르는 순수 규칙. 사용 = <see cref="LobbyTrainingDirector"/>(서버).
/// 처음엔 훈련 종료(→ 브리핑 구역)용이라 이름이 그대로다 — 입구 확정(→ Train01Start)도 같은 규칙을 쓴다(<see cref="LobbyTrainingFlow.TeleportTargetOnConfirm"/>).
/// <list type="bullet">
/// <item>좌표 = 박스 중심 기준 XZ(m, 박스 로컬 축). 벽 쪽은 <c>margin</c> 만큼 비운 안쪽 사각형만 쓴다.</item>
/// <item>한 줄 = 로컬 X 방향으로 <c>spacing</c> 간격, 다 차면 다음 줄(-Z 쪽). 쓰는 줄 묶음과 마지막 줄은 가운데 정렬 — 혼자면 정중앙.</item>
/// <item>사람이 자리 수보다 많으면 처음 자리부터 다시(순환 — 그 사람끼리는 겹친다). 박스가 여백보다 좁으면 그 축은 중심 한 줄.</item>
/// </list>
/// </summary>
public static class LobbyTrainingEndPlacement
{
    /// <summary>박스 가장자리(벽 쪽)에서 비우는 폭(m).</summary>
    public const float DefaultMargin = 1f;

    /// <summary>자리 사이 간격(m) — 플레이어 캡슐끼리 겹치지 않을 만큼.</summary>
    public const float DefaultSpacing = 1.5f;

    /// <summary>박스 안에 둘 수 있는 자리 수(열 × 줄).</summary>
    public static int Capacity(Vector2 boxSize, float margin, float spacing)
    {
        Grid(boxSize, margin, spacing, out var columns, out var rows, out _);
        return columns * rows;
    }

    /// <summary><paramref name="count"/> 명 중 <paramref name="index"/> 번째(0부터, 범위 밖은 순환) 사람의 자리 — 박스 중심 기준 (로컬 X, 로컬 Z).</summary>
    public static Vector2 Slot(int index, int count, Vector2 boxSize, float margin, float spacing)
    {
        Grid(boxSize, margin, spacing, out var columns, out var rows, out var step);
        var capacity = columns * rows;
        count = Mathf.Max(1, count);
        index = (index % count + count) % count;

        var used = Mathf.Min(count, capacity);
        var slot = index % capacity;
        var row = slot / columns;
        var column = slot % columns;
        var usedRows = (used + columns - 1) / columns;
        var inRow = row == usedRows - 1 ? used - row * columns : columns;

        var x = (column - (inRow - 1) * 0.5f) * step;
        var z = ((usedRows - 1) * 0.5f - row) * step;
        return new Vector2(x, z);
    }

    private static void Grid(Vector2 boxSize, float margin, float spacing, out int columns, out int rows, out float step)
    {
        step = Mathf.Max(0.01f, spacing);
        var innerX = Mathf.Max(0f, Mathf.Abs(boxSize.x) - 2f * Mathf.Max(0f, margin));
        var innerZ = Mathf.Max(0f, Mathf.Abs(boxSize.y) - 2f * Mathf.Max(0f, margin));
        columns = Mathf.FloorToInt(innerX / step + 1e-4f) + 1;
        rows = Mathf.FloorToInt(innerZ / step + 1e-4f) + 1;
    }
}
