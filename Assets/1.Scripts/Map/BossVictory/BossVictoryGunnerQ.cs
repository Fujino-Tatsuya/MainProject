using System;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 거너 Q 3단계 차징·큰 레이저(할 일 294) — 캐릭터 프로필 칸(<see cref="BossVictoryProfile.gunnerQ"/>).
/// 시각 = 타임라인 '거너 Q' 줄 마커 5개(차징 시작 → 1단계 → 2단계 → 3단계 → 발사, <see cref="BossVictoryTimeline"/>), 값 = 여기.
/// 표시 전용(이 피어 — 전 피어가 같은 타임라인으로 각자), 새 RPC·복제 값 없음. 순수 계산 = <see cref="BossVictoryGunnerCharge"/>.
/// </summary>
[Serializable]
public sealed class BossVictoryGunnerQ
{
    /// <summary>단계 마커 하나의 값 — 도달 크기·앞 마커에서 오는 곡선·최대 충전 알림(chargeFullPlayer) 켜기·크기.</summary>
    [Serializable]
    public struct Stage
    {
        [Tooltip("이 단계 마커에 도달했을 때 차징 구체 크기(배율 — 거너 평소 0.25 → 1).")]
        [Min(0f)] public float sphereScale;
        [Tooltip("앞 마커(차징 시작 또는 앞 단계) → 이 마커까지 구체 크기 곡선.")]
        public BossVictoryKeyCurve ease;
        [Tooltip("이 단계 도달 순간 최대 충전 알림(FX_GunnerChargeFull) 터짐.")]
        public bool burst;
        [Tooltip("최대 충전 알림 크기(배율 — 소켓 저작 배율에 곱).")]
        [Min(0.01f)] public float burstScale;

        public static Stage Make(float sphereScale, bool burst = true, float burstScale = 1f) =>
            new Stage { sphereScale = sphereScale, ease = BossVictoryKeyCurve.Linear, burst = burst, burstScale = burstScale };
    }

    [Tooltip("차징 시작 마커의 차징 구체 크기(배율 — 거너 평소 0.25).")]
    [Min(0f)] public float startScale = 0.25f;
    public Stage stage1 = Stage.Make(0.5f);
    public Stage stage2 = Stage.Make(0.75f);
    public Stage stage3 = Stage.Make(1f);

    [Tooltip("과충전 루프(FX_GunnerOvercharge — 3단계 도달부터 발사까지) 크기(배율).")]
    [Min(0.01f)] public float overchargeScale = 1f;
    [Tooltip("발사 순간 방출 섬광(FX_GunnerQVent) 크기(배율).")]
    [Min(0.01f)] public float ventScale = 1f;
    [Tooltip("레이저 굵기 배율(보이는 것만 — 몸통 입자 폭).")]
    [Min(0.01f)] public float beamWidthScale = 1f;
    [Tooltip("레이저 길이(m) — 처치 연출에서는 벽을 무시하고 보스를 관통한다(착탄 이펙트 없음).")]
    [Min(0.1f)] public float beamLength = 30f;

    [Tooltip("차징 위치 오프셋(총구 기준 로컬 m — x = 옆(+ 오른쪽), y = 위, z = 앞(바라보는 방향)). 구체·최대 충전 알림·과충전에 같이.")]
    public Vector3 chargeOffset;
    [Tooltip("발사 위치 오프셋(총구 기준 로컬 m — x = 옆, y = 위, z = 앞). 방출 섬광·레이저 시작점에 같이.")]
    public Vector3 fireOffset;

    [Tooltip("차징 구체가 '차징 시작' 마커보다 늦게 나오는 시간(타임라인 초). 0 = 바로.")]
    [Min(0f)] public float sphereDelay;
    [Tooltip("레이저 유지 시간(타임라인 초) — 발사 마커부터.")]
    [Min(0.01f)] public float beamHoldSeconds = 0.5f;

    public Stage StageAt(int stage) => stage switch
    {
        1 => stage1,
        2 => stage2,
        _ => stage3,
    };
}

/// <summary>
/// 거너 Q 처치 연출 순수 계산(할 일 294) — 테스트 <c>BossVictoryGunnerQTests</c>. 런타임(<see cref="BossVictoryGlitchPresentation"/> → <see cref="GunnerBeamView"/>)과 창이 같은 식을 쓴다.
/// </summary>
public static class BossVictoryGunnerCharge
{
    /// <summary>옛 1.2초 고정 충전(GunnerChargeLaserData.maxChargeTime) — 옛 프로필 이관 때 발사 = 차징 시작 + 이 값.</summary>
    public const float LegacyChargeSeconds = 1.2f;

    /// <summary>마커 다섯 시각(타임라인 초). <see cref="Ordered"/> 로 앞 마커보다 앞이면 앞 마커에 붙인다.</summary>
    public struct Times
    {
        public float ChargeStart, Stage1, Stage2, Stage3, Fire;

        public float Stage(int k) => k switch { 1 => Stage1, 2 => Stage2, 3 => Stage3, _ => ChargeStart };

        /// <summary>뒤 마커가 앞 마커보다 앞이면 앞 마커 시각으로(차징 시작 ≤ 1 ≤ 2 ≤ 3 ≤ 발사).</summary>
        public Times Ordered()
        {
            Times o = this;
            o.Stage1 = Mathf.Max(o.Stage1, o.ChargeStart);
            o.Stage2 = Mathf.Max(o.Stage2, o.Stage1);
            o.Stage3 = Mathf.Max(o.Stage3, o.Stage2);
            o.Fire = Mathf.Max(o.Fire, o.Stage3);
            return o;
        }
    }

    /// <summary>타임라인 마커 → 시각(정렬 전 그대로).</summary>
    public static Times From(BossVictoryTimeline tl) => new Times
    {
        ChargeStart = tl.ChargeStartTime,
        Stage1 = tl.gunnerStage1Time,
        Stage2 = tl.gunnerStage2Time,
        Stage3 = tl.gunnerStage3Time,
        Fire = tl.gunnerFireTime,
    };

    /// <summary>시각 t 의 상태 한 장 — 런타임이 매 프레임 뷰에 넘긴다.</summary>
    public struct Frame
    {
        public bool Charging;       // 차징 시작 ≤ t < 발사
        public bool SphereVisible;  // 차징 중 + 구체 지연 지남
        public float SphereScale;
        public int Stage;           // 지난 단계 마커 수(0~3) — 단계 색 = 한계 돌파 색 이 단계
        public bool Fired;          // t ≥ 발사
        public bool LaserOn;        // 발사 ≤ t < 발사 + 유지 시간
    }

    public static Frame Evaluate(Times times, BossVictoryGunnerQ q, float t)
    {
        Times o = times.Ordered();
        var f = new Frame
        {
            Stage = StageAt(o, t),
            Fired = t >= o.Fire,
            SphereScale = SphereScale(o, q, t),
        };
        f.Charging = t >= o.ChargeStart && !f.Fired;
        f.SphereVisible = f.Charging && t >= o.ChargeStart + Mathf.Max(0f, q.sphereDelay);
        f.LaserOn = f.Fired && t < o.Fire + Mathf.Max(0.01f, q.beamHoldSeconds);
        return f;
    }

    /// <summary>시각 t 까지 지난 단계 마커 수(0~3, 정렬된 시각 기준).</summary>
    public static int StageAt(Times ordered, float t)
    {
        int n = 0;
        for (int k = 1; k <= 3; k++)
            if (t >= ordered.Stage(k)) n = k;
        return n;
    }

    /// <summary>
    /// 차징 구체 크기 — 차징 시작 = 시작 크기, 각 단계 마커 = 그 단계 도달 크기, 마커 사이 = 그 단계 곡선으로 보간, 3단계 뒤 = 3단계 크기 유지.
    /// 차징 시작 전 = 시작 크기. 두 마커가 같은 시각이면 뒤 마커 크기로 바로.
    /// </summary>
    public static float SphereScale(Times ordered, BossVictoryGunnerQ q, float t)
    {
        float prevTime = ordered.ChargeStart, prevScale = q.startScale;
        if (t <= prevTime) return prevScale;
        for (int k = 1; k <= 3; k++)
        {
            BossVictoryGunnerQ.Stage s = q.StageAt(k);
            float at = ordered.Stage(k);
            if (t < at)
            {
                float u = at > prevTime ? (t - prevTime) / (at - prevTime) : 1f;
                return Mathf.LerpUnclamped(prevScale, s.sphereScale, BossVictoryKeys.Ease(s.ease, u));
            }
            prevTime = at;
            prevScale = s.sphereScale;
        }
        return prevScale;
    }

    /// <summary>총구 기준 로컬 오프셋(x = 옆, y = 위, z = 앞) → 월드 점. 앞 = <paramref name="facing"/>(수평), 위 = 월드 위.</summary>
    public static Vector3 OffsetPoint(Vector3 muzzle, Vector3 facing, Vector3 localOffset)
    {
        facing.y = 0f;
        Quaternion basis = facing.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(facing.normalized, Vector3.up) : Quaternion.identity;
        return muzzle + basis * localOffset;
    }

    /// <summary>레이저 끝점 = 시작점 + 수평 방향 × 길이(벽 무시 — 보스 관통).</summary>
    public static Vector3 BeamEnd(Vector3 start, Vector3 direction, float length)
    {
        direction.y = 0f;
        direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        return start + direction * Mathf.Max(0f, length);
    }

    /// <summary>
    /// 옛 프로필(이 칸 전) 이관 — 'Q 차징 시작' 마커는 그대로 '차징 시작'이 되고, 옛 1.2초 충전(시작 → 목표 크기·성장 곡선)을 단계 마커로 나눈다:
    /// 1·2·3단계 = 차징 시작 + 0.3·0.6·0.9초, 발사 = 차징 시작 + 1.2초(옛 발사 시각 그대로), 시작 크기 = 옛 시작 크기, k 단계 크기 = 옛 곡선(k/3) 값(3단계 = 목표).
    /// 옛 값이 있는지(옮길지)는 프로필이 정한다(<see cref="BossVictoryProfile.MigrateLegacyGunnerCharge"/> — 옮긴 뒤 옛 칸을 비워 멱등).
    /// </summary>
    public static void Migrate(BossVictoryGunnerQ q, BossVictoryTimeline tl, float legacyFrom, float legacyTo, AnimationCurve legacyCurve)
    {
        if (q == null || tl == null) return;
        float start = tl.ChargeStartTime;
        float step = LegacyChargeSeconds / 4f;
        tl.gunnerStage1Time = start + step;
        tl.gunnerStage2Time = start + step * 2f;
        tl.gunnerStage3Time = start + step * 3f;
        tl.gunnerFireTime = start + LegacyChargeSeconds;
        q.startScale = legacyFrom;
        q.stage1.sphereScale = LegacyScale(legacyFrom, legacyTo, legacyCurve, 1f / 3f);
        q.stage2.sphereScale = LegacyScale(legacyFrom, legacyTo, legacyCurve, 2f / 3f);
        q.stage3.sphereScale = LegacyScale(legacyFrom, legacyTo, legacyCurve, 1f);
    }

    static float LegacyScale(float from, float to, AnimationCurve curve, float u) =>
        Mathf.LerpUnclamped(from, to, curve != null && curve.length > 0 ? curve.Evaluate(u) : u);
}
