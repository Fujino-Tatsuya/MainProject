using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 23호 전기 장판 연출(2026-10-23 민경 grill — PLAN.md 「23호 전기 장판 VFX」 · Docs/tech/handoff-boss-electric-floor-vfx.md).
/// 23호 <c>VFX</c> 밑에 붙는다. 장판(<see cref="BossElectricFloor"/>, 경석)이 전 피어에 알리는 신호 3개만 듣고 연출을 전담한다 — 판정·타이밍은 모른다.
/// <list type="bullet">
/// <item>예고(<see cref="BossElectricFloor.ClientWarnShown"/>) — 칸마다 ① 예고 스파크 + (a) 발동 직전 몰아치기(<see cref="ElectricFloorTileFx"/>)</item>
/// <item>발동(<see cref="BossElectricFloor.ClientFired"/>) — ② 천둥 번개 + (b) 잔광. 즉사 방전은 번개 모양만 다르다(<see cref="ElectricFloorBolts"/> 즉사 모양 · 추가 연출 없음)</item>
/// <item>지우기(<see cref="BossElectricFloor.ClientCleared"/>) — 즉시 끔</item>
/// <item>③ 피격 — 발동 직후 <see cref="hitWindow"/> 초 안에 실제 피해(공격자 = 플레이어 아님)가 온 플레이어 중 발이 터진 칸 안인 사람에게
///   <see cref="hitEffect"/> 를 몸 가운데에 붙여 <see cref="hitTime"/> 초. 상태이상은 걸지 않는다(기획 §6.3)</item>
/// </list>
/// 칸 연출은 씬 루트의 <c>BossElectricFloorFx</c> 밑에 49개까지 만들어 재사용한다(보스가 움직이고 돌아서 보스 자식으로 두지 않는다 — 장판 표시와 같은 이유).
/// </summary>
[DisallowMultipleComponent]
public class BossElectricFloorVfx : MonoBehaviour
{
    [Header("칸 연출")]
    [Tooltip("FX_ElectricFloorTile — 칸 하나(ElectricFloorTileFx)")]
    [SerializeField] GameObject tilePrefab;
    [Tooltip("붉은 예고 판 위로 띄우는 높이(m). 예고 판은 바닥 + 0.06~0.07")]
    [SerializeField, Min(0f)] float height = 0.09f;

    [Header("③ 피격")]
    [Tooltip("FX_ElectricFloorHit 엔트리 — 맞은 플레이어 몸 가운데를 따라다닌다")]
    [SerializeField] EffectEntry hitEffect;
    [SerializeField, Min(0.05f)] float hitTime = 0.5f;
    [Tooltip("발동 신호와 실제 피해 신호 사이 허용 간격(초) — 둘은 다른 RPC 라 도착 순서가 바뀔 수 있다")]
    [SerializeField, Min(0f)] float hitWindow = 0.3f;

    struct ActiveHit
    {
        public EffectHandle handle;
        public float until;
    }

    TwentyThreeBoss _boss;
    BossElectricFloor _floor;
    ElectricFloorFxPool _pool;
    float _nextBind;

    // ③ 피격
    readonly Dictionary<Player, Action<DamageDealtInfo>> _watched = new Dictionary<Player, Action<DamageDealtInfo>>();
    readonly Dictionary<Player, float> _lastHurt = new Dictionary<Player, float>();
    readonly List<Player> _candidates = new List<Player>();
    readonly List<Player> _scratch = new List<Player>();
    readonly List<ActiveHit> _hits = new List<ActiveHit>();
    BossTileGrid _grid;
    float _fireTime = float.NegativeInfinity;

    void Update()
    {
        if (_floor == null && Time.time >= _nextBind) Bind();
        TickHits();
    }

    // 23호가 스폰 때 장판 컴포넌트를 붙인다(OnNetworkSpawn) — 그 뒤에 잡히도록 몇 번 다시 본다.
    void Bind()
    {
        _nextBind = Time.time + 0.5f;
        if (_boss == null) _boss = GetComponentInParent<TwentyThreeBoss>(true);
        if (_boss == null || !_boss.TryGetComponent(out _floor)) return;
        _floor.ClientWarnShown += OnWarn;
        _floor.ClientFired += OnFire;
        _floor.ClientCleared += OnClear;
    }

    void OnDestroy()
    {
        if (_floor != null)
        {
            _floor.ClientWarnShown -= OnWarn;
            _floor.ClientFired -= OnFire;
            _floor.ClientCleared -= OnClear;
        }
        foreach (KeyValuePair<Player, Action<DamageDealtInfo>> w in _watched)
            if (w.Key != null) w.Key.ClientDamagedAttributed -= w.Value;
        _watched.Clear();
        _pool?.Dispose();
    }

    bool EnsurePool()
    {
        if (_pool != null && _pool.HasFrame) return true;
        if (_boss == null || !_boss.TryGetTileGrid(out _grid)) return false;
        if (_pool == null) _pool = new ElectricFloorFxPool("BossElectricFloorFx");

        // 칸 중심 + 바닥 높이(장판 표시와 같은 샘플) + 띄움.
        var centers = new Vector3[ElectricFloorFxPool.TileCount];
        for (int r = 0; r < BossTileGrid.Size; r++)
            for (int c = 0; c < BossTileGrid.Size; c++)
            {
                Vector3 p = _grid.TileCenter(r, c);
                p.y = BossPatternVisuals.SampleFloorY(p, p.y) + height;
                centers[BossTileGrid.Bit(r, c)] = p;
            }
        _pool.SetFrame(centers, _grid.Rotation, _grid.TileExtent);
        return true;
    }

    void OnWarn(ulong mask, float time)
    {
        if (!EnsurePool()) return;
        _pool.Warn(tilePrefab, mask, time);
        WatchPlayers();
    }

    void OnFire(ulong mask, float time, bool lethal)
    {
        if (!EnsurePool()) return;
        _pool.Fire(tilePrefab, mask, time, lethal);

        // ③ 후보 = 지금 발이 터진 칸 안인 플레이어. 피해 신호가 이미 와 있으면 바로, 아니면 hitWindow 동안 기다린다.
        WatchPlayers();
        _fireTime = Time.time;
        _candidates.Clear();
        foreach (Player p in _watched.Keys)
            if (p != null && p.isActiveAndEnabled && _grid.Contains(mask, BossPatternTargets.FootPosition(p)))
                _candidates.Add(p);
        ResolveCandidates();
    }

    void OnClear()
    {
        _pool?.Clear();
        _candidates.Clear();
    }

    // 접속한 플레이어 피해 신호 구독 — 예고·발동 때마다 새 플레이어를 더하고 사라진 플레이어를 뺀다(장판 주기가 초 단위라 매번 찾아도 싸다).
    void WatchPlayers()
    {
        _scratch.Clear();
        foreach (Player p in _watched.Keys) if (p == null) _scratch.Add(p);
        foreach (Player p in _scratch) { _watched.Remove(p); _lastHurt.Remove(p); }

        foreach (Player p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (_watched.ContainsKey(p)) continue;
            Player target = p;
            Action<DamageDealtInfo> handler = info =>
            {
                // 플레이어가 준 피해(아군 오사 등)는 빼고 — 장판 피해는 공격자 없음(ulong.MaxValue).
                if (info.attackerClientId != ulong.MaxValue) return;
                _lastHurt[target] = Time.time;
                if (_candidates.Count > 0) ResolveCandidates();
            };
            p.ClientDamagedAttributed += handler;
            _watched.Add(p, handler);
        }
    }

    void ResolveCandidates()
    {
        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            Player p = _candidates[i];
            if (p == null) { _candidates.RemoveAt(i); continue; }
            if (_lastHurt.TryGetValue(p, out float t) && Mathf.Abs(t - _fireTime) <= hitWindow)
            {
                PlayHit(p);
                _candidates.RemoveAt(i);
            }
        }
    }

    void PlayHit(Player p)
    {
        if (hitEffect == null || !EffectManager.TryGet(out EffectManager effects, this)) return;
        float body = BossPatternTargets.BodyCenter(p.transform).y - p.transform.position.y;
        EffectHandle h = effects.PlayLooping(hitEffect, p.transform, Vector3.up * body);
        _hits.Add(new ActiveHit { handle = h, until = Time.time + hitTime });
    }

    void TickHits()
    {
        if (_candidates.Count > 0 && Time.time - _fireTime > hitWindow) _candidates.Clear();   // 끝내 피해가 안 왔다(무적·보호막 무효 등)

        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            if (Time.time < _hits[i].until) continue;
            if (EffectManager.TryGet(out EffectManager effects, this)) effects.Release(_hits[i].handle);
            _hits.RemoveAt(i);
        }
    }
}
