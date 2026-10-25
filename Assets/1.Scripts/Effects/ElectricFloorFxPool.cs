using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 23호 전기 장판 연출 풀 — 7×7 칸마다 <see cref="ElectricFloorTileFx"/> 하나(2026-10-23 민경 grill).
/// 칸 인스턴스는 처음 쓰일 때 만들어 계속 재사용한다(발동마다 최대 49개 Instantiate/Destroy 를 피한다 — 기존 장판 표시와 같은 방식).
///
/// 좌표는 쓰는 쪽이 <see cref="SetFrame"/> 으로 넘긴다(칸 중심 49개 · 판 회전 · 칸 크기) — MainProject 는 BossTileGrid 에서,
/// MyProject 미리보기는 자기 배치에서. 마스크 비트 = 행 × 7 + 열(BossTileGrid 와 같다).
/// MyProject 미리보기와 MainProject 가 같은 파일을 쓴다.
/// </summary>
public sealed class ElectricFloorFxPool
{
    public const int Size = 7;
    public const int TileCount = Size * Size;

    readonly Transform _root;
    readonly ElectricFloorTileFx[] _tiles = new ElectricFloorTileFx[TileCount];
    readonly Vector3[] _centers = new Vector3[TileCount];
    GameObject _tileSource;
    Quaternion _rotation = Quaternion.identity;
    Vector2 _extent = Vector2.one;
    bool _hasFrame;
    int _fireCount;   // 발동 번호 — 같은 발동의 칸끼리 번개 끝점을 맞춘다

    public ElectricFloorFxPool(string rootName)
    {
        _root = new GameObject(rootName).transform;
    }

    public Transform Root => _root;
    public bool HasFrame => _hasFrame;

    /// <summary>칸 배치. <paramref name="centers"/> = 칸 중심(월드, 연출 높이 포함) 49개, 인덱스 = 행 × 7 + 열.</summary>
    public void SetFrame(IReadOnlyList<Vector3> centers, Quaternion rotation, Vector2 extent)
    {
        for (int i = 0; i < TileCount && i < centers.Count; i++) _centers[i] = centers[i];
        _rotation = rotation;
        _extent = extent;
        _hasFrame = true;
        for (int i = 0; i < TileCount; i++)
            if (_tiles[i] != null) Place(i);
    }

    public Vector3 TileCenter(int index) => _centers[index];

    public static bool Has(ulong mask, int index) => (mask & (1UL << index)) != 0;

    /// <summary>① 예고 시작.</summary>
    public void Warn(GameObject tilePrefab, ulong mask, float time)
    {
        if (!Prepare(tilePrefab)) return;
        for (int i = 0; i < TileCount; i++)
            if (Has(mask, i)) Tile(i).Warn(time);
    }

    /// <summary>② 발동. <paramref name="lethal"/> = 즉사 방전 — 번개 모양만 다르다(추가 연출 없음).</summary>
    public void Fire(GameObject tilePrefab, ulong mask, float time, bool lethal)
    {
        if (!Prepare(tilePrefab)) return;
        int seed = ++_fireCount;
        for (int i = 0; i < TileCount; i++)
            if (Has(mask, i)) Tile(i).Fire(time, lethal, seed, EdgeFlags(mask, i));
    }

    // 가장자리 전류 — 위·왼쪽 변은 늘, 아래·오른쪽 변은 그쪽 이웃이 같이 안 터질 때만(이웃이 자기 위·왼쪽으로 그린다).
    static int EdgeFlags(ulong mask, int i)
    {
        int r = i / Size, c = i % Size;
        int flags = ElectricFloorEdges.Top | ElectricFloorEdges.Left;
        if (r == Size - 1 || !Has(mask, i + Size)) flags |= ElectricFloorEdges.Bottom;
        if (c == Size - 1 || !Has(mask, i + 1)) flags |= ElectricFloorEdges.Right;
        return flags;
    }

    /// <summary>즉시 끔(보스 사망·제압·기믹 종료).</summary>
    public void Clear()
    {
        for (int i = 0; i < TileCount; i++)
            if (_tiles[i] != null) _tiles[i].Clear();
    }

    public void Dispose()
    {
        if (_root != null) Object.Destroy(_root.gameObject);
    }

    bool Prepare(GameObject tilePrefab)
    {
        if (!_hasFrame || tilePrefab == null) return false;
        // 프리팹이 바뀌었으면(인스펙터 교체) 만들어 둔 것을 버리고 다시 만든다.
        if (_tileSource != tilePrefab)
        {
            for (int i = 0; i < TileCount; i++)
                if (_tiles[i] != null) { Object.Destroy(_tiles[i].gameObject); _tiles[i] = null; }
            _tileSource = tilePrefab;
        }
        return true;
    }

    ElectricFloorTileFx Tile(int i)
    {
        if (_tiles[i] != null) return _tiles[i];
        GameObject go = Object.Instantiate(_tileSource, _root);
        go.name = $"Tile{i / Size}{i % Size}";
        ElectricFloorTileFx fx = go.GetComponent<ElectricFloorTileFx>();
        if (fx == null) fx = go.AddComponent<ElectricFloorTileFx>();
        _tiles[i] = fx;
        Place(i);
        return fx;
    }

    void Place(int i)
    {
        _tiles[i].transform.SetPositionAndRotation(_centers[i], _rotation);
        _tiles[i].Setup(_extent, i / Size, i % Size);
    }
}
