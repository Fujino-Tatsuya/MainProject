using UnityEngine;

/// <summary>
/// 보스방 바닥 7×7 타일 — 전기 장판(<see cref="BossElectricFloor"/>)과 드론 반경의 공통 기준.
/// 기획: Docs/design/boss/boss-electric-floor.md §3 · PLAN-boss-electric-drone.md §2.
///
/// - 범위 = 벽 안쪽 사각형(<c>InvisibleBoundaries/Boundary_*</c> 안쪽 면, 23호 <c>ResolveArena</c> 와 같은 값)을 7등분.
/// - 🔴 좌표는 **방 로컬**이다. 존은 90° 단위로 돌아 배치되므로 월드 X/Z 로 자르면 회전된 방에서 칸이 뒤집힌다.
/// - 타일 (행 r, 열 c), 0..6. 기획서 표기 <c>(r+1, c+1)</c>. 첫 번째 축 = 행, 두 번째 축 = 열.
///   행 0 = 방 로컬 +Z 끝, 열 0 = 방 로컬 −X 끝. 화면 위 모서리 = (0,0) — 패턴이 대칭이라 어느 모서리를 잡아도 결과는 같다.
/// - 마스크 = 49비트 <c>ulong</c>, 비트 = r*7 + c. RPC 에 그대로 실린다.
/// </summary>
public readonly struct BossTileGrid
{
    public const int Size = 7;
    public const int TileCount = Size * Size;
    public const ulong AllMask = (1UL << TileCount) - 1UL;

    readonly Matrix4x4 _worldToLocal;
    readonly Matrix4x4 _localToWorld;
    readonly float _minX, _maxZ, _tileW, _tileH;
    readonly float _floorY;

    public bool IsValid => _tileW > 0f && _tileH > 0f;
    /// <summary>타일 한 변(m). 가로·세로가 조금 다를 수 있어 작은 쪽 — 드론 반경(0.25칸) 계산용.</summary>
    public float TileSize => Mathf.Min(_tileW, _tileH);

    /// <param name="frame">방 로컬 기준 트랜스폼(<c>InvisibleBoundaries</c>).</param>
    /// <param name="minX">방 로컬 안쪽 면 경계.</param>
    public BossTileGrid(Transform frame, float minX, float maxX, float minZ, float maxZ, float floorY)
        : this(frame.localToWorldMatrix, minX, maxX, minZ, maxZ, floorY) { }

    public BossTileGrid(Matrix4x4 localToWorld, float minX, float maxX, float minZ, float maxZ, float floorY)
    {
        _localToWorld = localToWorld;
        _worldToLocal = localToWorld.inverse;
        _minX = minX;
        _maxZ = maxZ;
        _tileW = (maxX - minX) / Size;
        _tileH = (maxZ - minZ) / Size;
        _floorY = floorY;
    }

    public static int Bit(int r, int c) => r * Size + c;
    public static bool Has(ulong mask, int r, int c) => (mask & (1UL << Bit(r, c))) != 0;

    /// <summary>월드 위치(발)가 들어 있는 타일. 방 밖이면 false.</summary>
    public bool TryGetTile(Vector3 world, out int r, out int c)
    {
        Vector3 p = _worldToLocal.MultiplyPoint3x4(world);
        float fc = (p.x - _minX) / _tileW;
        float fr = (_maxZ - p.z) / _tileH;
        r = Mathf.FloorToInt(fr);
        c = Mathf.FloorToInt(fc);
        return r >= 0 && r < Size && c >= 0 && c < Size;
    }

    public bool Contains(ulong mask, Vector3 world) => TryGetTile(world, out int r, out int c) && Has(mask, r, c);

    /// <summary>타일 중심(월드). 높이 = 바닥.</summary>
    public Vector3 TileCenter(int r, int c)
    {
        var local = new Vector3(_minX + (c + 0.5f) * _tileW, _floorY, _maxZ - (r + 0.5f) * _tileH);
        return _localToWorld.MultiplyPoint3x4(local);
    }

    /// <summary>방 회전(월드 Y 축 각). 사각 예고를 칸에 맞춰 돌릴 때.</summary>
    public Quaternion Rotation => _localToWorld.rotation;
    public Vector2 TileExtent => new Vector2(_tileW, _tileH);

    // ── 마스크 빌더 ─────────────────────────────────────────────

    public static ulong Row(int r)
    {
        ulong m = 0;
        for (int c = 0; c < Size; c++) m |= 1UL << Bit(r, c);
        return m;
    }

    public static ulong Col(int c)
    {
        ulong m = 0;
        for (int r = 0; r < Size; r++) m |= 1UL << Bit(r, c);
        return m;
    }

    /// <summary>행 r0..r1 · 열 c0..c1 (양끝 포함) 사각형.</summary>
    public static ulong Rect(int r0, int c0, int r1, int c1)
    {
        ulong m = 0;
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
                m |= 1UL << Bit(r, c);
        return m;
    }

    public static int Count(ulong mask)
    {
        int n = 0;
        while (mask != 0) { mask &= mask - 1; n++; }
        return n;
    }
}
