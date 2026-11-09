using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 보스 B 승리 연출(PLAN-boss-victory-cinematic) — 디렉터 몫은 **복제 상태 + 서버 진행**뿐이다(Codex C7).
// 표시(모든 피어)는 ZZZ 글리치 연출(PLAN-boss-victory-glitch — 교체 중, 옛 BossVictoryPresentation 은 Legacy). 디렉터가 씬 상주 NetworkObject 라 복제 값을 여기(partial)에 둔다
// — 런타임에 붙인 컴포넌트는 NetworkVariable 을 가질 수 없다(4구역 소개 연출과 같은 제약). RPC 없음: 늦은 합류도 같은 경로.
public sealed partial class BossEncounterDirector
{
    /// <summary>승리 연출 복제 상태 — 모든 피어가 이 값과 <see cref="BossVictoryConfig"/> 만으로 같은 화면을 그린다.</summary>
    public struct VictoryState : INetworkSerializable, IEquatable<VictoryState>
    {
        public bool Active;
        /// <summary>회차 — 시작마다 +1. 표시 쪽이 "새 연출인가"를 이 값으로 가른다.</summary>
        public int Sequence;
        /// <summary>처치 확정 ServerTime(t=0).</summary>
        public double StartServerTime;
        public ulong BossNetworkObjectId;
        /// <summary>막타 플레이어(좌측 전경·밝은 무채색).</summary>
        public ulong KillerClientId;
        public Vector3 HitPoint;
        /// <summary>서버가 한 번 계산한 24° 최종 구도(C8) — 0.72초부터 전원 같은 화면.</summary>
        public Vector3 FinalPosition;
        public Quaternion FinalRotation;
        /// <summary>32° 종료 구도(3.20초 이후 유지 — D4).</summary>
        public Vector3 EndPosition;
        public Quaternion EndRotation;
        public float Fov;
        /// <summary>추가 연출 구간 길이(초, 할 일 200) — 처치 순간 서버가 설정에서 읽어 담는다. 단계 시각 = 경과 − 이 값.</summary>
        public float PreludeSeconds;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Active);
            s.SerializeValue(ref Sequence);
            s.SerializeValue(ref StartServerTime);
            s.SerializeValue(ref BossNetworkObjectId);
            s.SerializeValue(ref KillerClientId);
            s.SerializeValue(ref HitPoint);
            s.SerializeValue(ref FinalPosition);
            s.SerializeValue(ref FinalRotation);
            s.SerializeValue(ref EndPosition);
            s.SerializeValue(ref EndRotation);
            s.SerializeValue(ref Fov);
            s.SerializeValue(ref PreludeSeconds);
        }

        public bool Equals(VictoryState o) =>
            Active == o.Active && Sequence == o.Sequence && StartServerTime.Equals(o.StartServerTime) &&
            BossNetworkObjectId == o.BossNetworkObjectId && KillerClientId == o.KillerClientId &&
            HitPoint == o.HitPoint && FinalPosition == o.FinalPosition && FinalRotation == o.FinalRotation &&
            EndPosition == o.EndPosition && EndRotation == o.EndRotation && Fov.Equals(o.Fov) && PreludeSeconds.Equals(o.PreludeSeconds);

        /// <summary>처치 확정부터 지난 시간(초).</summary>
        public float Elapsed(double serverNow) => (float)(serverNow - StartServerTime);

        /// <summary>단계 시각(경과 − 추가 연출 구간). <see cref="BossVictoryRules"/> 의 경계는 전부 이 시각 기준이다.</summary>
        public float StageTime(double serverNow) => BossVictoryRules.StageTime(Elapsed(serverNow), PreludeSeconds);
    }

    private readonly NetworkVariable<VictoryState> _victory = new NetworkVariable<VictoryState>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>[모든 피어] 승리 연출 상태(비활성이면 Active = false).</summary>
    public VictoryState Victory => IsSpawned ? _victory.Value : default;

    // [서버]
    private bool _spawnedVictoryVariant;    // 이번에 스폰한 보스가 B(raidBossPrefab) — 전역 선택값이 아니라 실제 인스턴스 기준(C13)
    private bool _victoryResultReady;       // 서버(호스트) 화면의 처치 연출이 끝났다(CRT 꺼짐 끝) — 그 전엔 결과 전환을 붙잡는다

    /// <summary>
    /// [서버] 이 보스의 사망 표현이 지금 흐르는 배율 — 승리 연출 대상이 아니면 1. 사망 연출 대기(<c>MonsterBase.DeathPresentationTimeScale</c>)가 읽는다.
    /// 값 = 서버(호스트) 피어의 표시가 정한 보스 배율(처치 연출 스킬 End 전 0 · 뒤 0.1 — <see cref="BossVictoryGlitchPresentation.BossTimeScale"/>).
    /// 각 피어가 자기 화면 애니 이벤트로 판단하므로(Q12) 서버도 자기 화면 기준이다.
    /// </summary>
    public float VictoryDeathTimeScaleFor(NetworkBehaviour boss)
    {
        if (!IsVictoryBoss(boss)) return 1f;
        BossVictoryGlitchPresentation presentation = BossVictoryGlitchPresentation.Instance;
        return presentation != null ? presentation.BossTimeScale : 1f;
    }

    /// <summary>[서버] 이 보스가 승리 연출(보스 B) 대상인가 — 디졸브·디스폰 없이 사망 자세로 남는다(할 일 200 G).</summary>
    public bool IsVictoryBoss(NetworkBehaviour boss)
    {
        if (!IsSpawned || boss == null) return false;
        VictoryState v = _victory.Value;
        return v.Active && v.BossNetworkObjectId == boss.NetworkObjectId;
    }

    // [서버] 보스 스폰 직후 — 이 인스턴스가 B 인가.
    private void MarkVictoryVariantServer(GameObject prefab) =>
        _spawnedVictoryVariant = prefab != null && prefab == raidBossPrefab;

    /// <summary>[서버] 보스 처치 확정(<see cref="HandleBossDefeated"/>) — B 면 승리 연출을 연다. 보상·클리어 기록은 호출측이 이미 했다.</summary>
    private void TryStartVictoryServer()
    {
        if (!IsServer || !_spawnedVictoryVariant || _victory.Value.Active) return;

        TwentyThreeBoss boss = _bossUnit as TwentyThreeBoss;
        if (boss == null) return;

        Vector3 bossPos = boss.transform.position;

        // 참가자 — clientId 순.
        var players = new List<Player>();
        foreach (KeyValuePair<ulong, NetworkClient> pair in NetworkManager.ConnectedClients)
        {
            Player p = pair.Value?.PlayerObject != null ? pair.Value.PlayerObject.GetComponent<Player>() : null;
            if (p != null) players.Add(p);
        }
        players.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        // 막타(C1) — HP·자폭 실드에 마지막으로 피해를 준 플레이어. 기록이 없거나 나갔으면 보스에 가장 가까운 생존자(K1 대체).
        Player killer = null;
        Vector3 hitPoint = bossPos + Vector3.up * 1.5f;
        if (boss.TryGetVictoryHit(out ulong hitClientId, out Vector3 recordedHit))
        {
            killer = players.Find(p => p.OwnerClientId == hitClientId);
            if (killer != null) hitPoint = recordedHit;
        }
        if (killer == null)
        {
            float best = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                if (!IsAliveForVictory(players[i])) continue;
                float d = (players[i].transform.position - bossPos).sqrMagnitude;
                if (d < best) { best = d; killer = players[i]; }
            }
            if (killer == null && players.Count > 0) killer = players[0];
        }
        if (killer == null)
        {
            Edit.LogWarning("[BossVictory] 플레이어가 없어 승리 연출을 건너뛴다.", this);
            return;
        }

        double now = NetworkManager.ServerTime.Time;
        _victory.Value = new VictoryState
        {
            Active = true,
            Sequence = _victory.Value.Sequence + 1,
            StartServerTime = now,
            BossNetworkObjectId = boss.NetworkObjectId,
            KillerClientId = killer.OwnerClientId,
            HitPoint = hitPoint,
        };

        // 처치 순간: 피해 무시(D3) · 전역 슬로모션 강제 해제(C11) · 전원 연출 잠금(실행 중 스킬·평타 취소 + 입력 차단 — 처치 연출 스킬은
        // 각 피어가 표시로만 새로 튼다) · 서버 판정 장판·투사체 디스폰(Q11 — 표시 소거는 각 피어 BossVictoryGlitchPresentation).
        for (int i = 0; i < players.Count; i++)
        {
            players[i].GetComponent<PlayerInvulnerability>()?.AddServerToken(InvulnerabilityCause.BossVictory, 0.0);
            players[i].GetComponent<PlayerEncounterLock>()?.SetCinematicInputLockServer(true);
        }
        GlobalTimeScale.Instance?.ServerForceReset();
        int despawned = DespawnCombatObjectsServer();

        _victoryResultReady = false;

        Edit.Log($"[BossVictory] 시작 — 막타 client {killer.OwnerClientId}(기록 {(killer.OwnerClientId == hitClientId ? "있음" : "없음 → 대체")}) · 장판·투사체 디스폰 {despawned}", this);
    }

    // [서버] 처치 판정 즉시 서버 판정 장판(AreaZone)·몬스터 투사체를 전부 디스폰한다(Q11 1차 범위 — 빠진 종류는 Play 에서 보이면 더한다).
    private static int DespawnCombatObjectsServer()
    {
        int count = 0;
        foreach (AreaZone zone in FindObjectsByType<AreaZone>(FindObjectsSortMode.None))
        {
            if (zone == null || zone.NetworkObject == null || !zone.NetworkObject.IsSpawned) continue;
            zone.Despawn();
            count++;
        }
        foreach (MonsterProjectile projectile in FindObjectsByType<MonsterProjectile>(FindObjectsSortMode.None))
        {
            if (projectile == null || projectile.NetworkObject == null || !projectile.NetworkObject.IsSpawned) continue;
            projectile.NetworkObject.Despawn();
            count++;
        }
        return count;
    }

    // [서버] Update — 서버(호스트) 피어의 처치 연출이 끝나는 순간(유지 → CRT 꺼짐 끝, 화면 검정) 결과 전환을 지금으로 당긴다(PLAN-boss-victory-glitch 6단계).
    // 연출 시계는 처치 판정부터 게임 시간으로 흘러 처치 연출 스킬 End 와 무관하게 반드시 끝난다(스킬 End 가 빠져도 붙잡히지 않는다).
    private void TickVictoryServer()
    {
        VictoryState v = _victory.Value;
        if (!v.Active || _victoryResultReady) return;
        BossVictoryGlitchPresentation presentation = BossVictoryGlitchPresentation.Instance;
        if (presentation == null || presentation.Sequence != v.Sequence || !presentation.Finished) return;
        _victoryResultReady = true;
        _resultTransitionAt = NetworkManager.ServerTime.Time;
        Edit.Log("[BossVictory] 호스트 화면 연출 끝 — 로비(결과 패널)로 전환", this);
    }

    // 디버그 랩(244, DebugBossVictory 씬) 진입점 — 구현은 BossEncounterDirector.Dev.cs(UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS).
    // 구현이 없으면 C# 이 호출째 지운다 → 릴리스 동작 불변.
    partial void DevBlockResultTransition(ref bool block);

    // 결과 화면 전환을 지금 막아야 하는가 — 승리 연출 중이고 호스트 화면 연출이 아직 안 끝났다(표시가 없으면 붙잡지 않는다).
    private bool HoldsResultForVictory() =>
        _victory.Value.Active && !_victoryResultReady && BossVictoryGlitchPresentation.Instance != null;

    private static bool IsAliveForVictory(Player p)
    {
        PlayerLifeCycleController life = p != null ? p.GetComponent<PlayerLifeCycleController>() : null;
        return p != null && (life == null || life.State == PlayerLifeState.Alive);
    }
}
