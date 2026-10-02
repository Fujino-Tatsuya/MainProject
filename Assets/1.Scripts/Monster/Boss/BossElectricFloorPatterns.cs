using System;

/// <summary>
/// 전기 장판 범위 선택 규칙 — 순수 C#(난수 주입), EditMode 테스트 대상.
/// 기획: Docs/design/boss/boss-electric-floor.md §4 · §5. 타일 표기는 <see cref="BossTileGrid"/>.
///
/// 기록 수명이 둘로 갈린다:
/// - 일반 장판의 직전 줄 = 과충전·고출력 진입/종료에도 **유지**(§8). 보스 사망 때만 <see cref="ResetAll"/>.
/// - 충전 기믹의 A/B 순서·직전 A 조합·직전 B 방향 = 기믹이 끝날 때마다 <see cref="ResetCharge"/>(§5.4).
/// </summary>
public sealed class BossElectricFloorPatterns
{
    public enum ChargeGroup { A, B }

    // A 프리셋 4종 — 기획 표기 (행,열) 1-기준 → 0-기준.
    // A-1 (1,1)~(4,4) · A-2 (4,1)~(7,4) · A-3 (1,4)~(4,7) · A-4 (4,4)~(7,7)
    public static readonly ulong[] APresets =
    {
        BossTileGrid.Rect(0, 0, 3, 3),
        BossTileGrid.Rect(3, 0, 6, 3),
        BossTileGrid.Rect(0, 3, 3, 6),
        BossTileGrid.Rect(3, 3, 6, 6),
    };

    /// <summary>B 1단계 — 바깥 테두리 안전, 안쪽 5×5.</summary>
    public static readonly ulong BInner = BossTileGrid.Rect(1, 1, 5, 5);

    /// <summary>B 2단계 — 첫 번째 축(행) 1·3·5·7번.</summary>
    public static readonly ulong BOddRows = BossTileGrid.Row(0) | BossTileGrid.Row(2) | BossTileGrid.Row(4) | BossTileGrid.Row(6);
    /// <summary>B 2단계 — 두 번째 축(열) 1·3·5·7번.</summary>
    public static readonly ulong BOddCols = BossTileGrid.Col(0) | BossTileGrid.Col(2) | BossTileGrid.Col(4) | BossTileGrid.Col(6);

    readonly Random _rng;

    // 일반 장판
    int _lastRow = -1, _lastCol = -1;

    // 충전 기믹
    ChargeGroup? _lastGroup;
    int _lastA0 = -1, _lastA1 = -1;   // 직전 A 조합(작은 번호, 큰 번호)
    bool? _lastBRows;                  // 직전 B 2단계가 행(첫 번째 축)이었나

    public BossElectricFloorPatterns(Random rng) { _rng = rng ?? new Random(); }

    public int LastRow => _lastRow;
    public int LastCol => _lastCol;

    // ── 일반 전투: 두 축에서 1줄씩, 직전 줄 제외 ────────────────

    public ulong NextNormal()
    {
        int r = PickExcept(BossTileGrid.Size, _lastRow);
        int c = PickExcept(BossTileGrid.Size, _lastCol);
        _lastRow = r;
        _lastCol = c;
        return BossTileGrid.Row(r) | BossTileGrid.Col(c);
    }

    // ── 충전 기믹 ────────────────────────────────────────────────

    /// <summary>다음 그룹. 첫 번째는 50%, 이후 교대(같은 그룹 연속 금지).</summary>
    public ChargeGroup NextGroup()
    {
        ChargeGroup g = _lastGroup == null
            ? (_rng.Next(2) == 0 ? ChargeGroup.A : ChargeGroup.B)
            : (_lastGroup == ChargeGroup.A ? ChargeGroup.B : ChargeGroup.A);
        _lastGroup = g;
        return g;
    }

    /// <summary>A 패턴 — 서로 다른 프리셋 2개, 직전 A 와 완전히 같은 조합 제외. 겹치는 칸은 OR 로 한 번만.</summary>
    public ulong NextA()
    {
        int a0, a1;
        do
        {
            a0 = _rng.Next(APresets.Length);
            a1 = _rng.Next(APresets.Length - 1);
            if (a1 >= a0) a1++;
            if (a0 > a1) (a0, a1) = (a1, a0);
        } while (a0 == _lastA0 && a1 == _lastA1);
        _lastA0 = a0;
        _lastA1 = a1;
        return APresets[a0] | APresets[a1];
    }

    /// <summary>B 2단계 방향 — 첫 B 는 무작위, 다음 B 는 직전과 반대.</summary>
    public ulong NextBStage2(out bool rows)
    {
        rows = _lastBRows == null ? _rng.Next(2) == 0 : !_lastBRows.Value;
        _lastBRows = rows;
        return rows ? BOddRows : BOddCols;
    }

    public void ResetCharge()
    {
        _lastGroup = null;
        _lastA0 = _lastA1 = -1;
        _lastBRows = null;
    }

    public void ResetAll()
    {
        ResetCharge();
        _lastRow = _lastCol = -1;
    }

    int PickExcept(int n, int except)
    {
        if (except < 0 || except >= n) return _rng.Next(n);
        int v = _rng.Next(n - 1);
        return v >= except ? v + 1 : v;
    }
}
