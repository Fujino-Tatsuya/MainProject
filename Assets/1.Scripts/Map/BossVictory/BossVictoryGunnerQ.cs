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
        [Tooltip("이 단계 도달 순간 카메라 흔들림 세기(295 — CameraFeedback.PlayImpactShake, 설정창 화면 흔들림 배율 곱). 0 = 끔.")]
        [Min(0f)] public float shakeIntensity;
        [Tooltip("카메라 흔들림 길이(초). 0 = 기본 0.2초.")]
        [Min(0f)] public float shakeDuration;

        public static Stage Make(float sphereScale, bool burst = true, float burstScale = 1f) =>
            new Stage { sphereScale = sphereScale, ease = BossVictoryKeyCurve.Linear, burst = burst, burstScale = burstScale };
    }

    /// <summary>흔들림 길이 0 = 이 값(초).</summary>
    public const float DefaultShakeDuration = 0.2f;

    /// <summary>흔들림 길이 칸 → 실제 초(0 이하 = 기본).</summary>
    public static float ShakeSeconds(float duration) => duration > 0f ? duration : DefaultShakeDuration;

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

    // ── 차징 이펙트 세부(할 일 295) — 마커마다(차징 시작·1·2·3단계) 파트별 배율·포인트 라이트, 마커 사이 = 그 단계 곡선(ease)으로 보간.
    //    기본값 = 지금 모습(배율 1·라이트 0·흔들림 0). 차징 루프 FX_GunnerCharge(SVN 아트) 프리팹은 그대로 — 런타임에 인스턴스 값을 배율로 덮고 끝나면 되돌린다. ──
    [Tooltip("차징 시작 마커 값(295) — 파트별 배율·포인트 라이트. 1단계까지 1단계 곡선으로 1단계 값에 다가간다.")]
    public BossVictoryChargeFx fxStart = new BossVictoryChargeFx();
    [Tooltip("1단계 마커 값(295).")]
    public BossVictoryChargeFx fxStage1 = new BossVictoryChargeFx();
    [Tooltip("2단계 마커 값(295).")]
    public BossVictoryChargeFx fxStage2 = new BossVictoryChargeFx();
    [Tooltip("3단계 마커 값(295) — 3단계 ~ 발사 유지.")]
    public BossVictoryChargeFx fxStage3 = new BossVictoryChargeFx();

    [Tooltip("최대 충전 알림(FX_GunnerChargeFull) 입자량 배율(295 — 버스트 개수·rate). 크기는 단계 칸 '알림 크기'.")]
    [Min(0f)] public float burstAmount = 1f;
    [Tooltip("최대 충전 알림 색 밝기(HDR 배율, 295).")]
    [Min(0f)] public float burstBrightness = 1f;
    [Tooltip("과충전 루프(FX_GunnerOvercharge) 입자량 배율(295). 크기는 '과충전 크기'.")]
    [Min(0f)] public float overchargeAmount = 1f;
    [Tooltip("과충전 루프 색 밝기(HDR 배율, 295).")]
    [Min(0f)] public float overchargeBrightness = 1f;

    [Tooltip("발사 순간 카메라 흔들림 세기(295). 0 = 끔.")]
    [Min(0f)] public float fireShakeIntensity;
    [Tooltip("발사 흔들림 길이(초). 0 = 기본 0.2초.")]
    [Min(0f)] public float fireShakeDuration = 0.25f;
    [Tooltip("발사 순간 포인트 라이트 번쩍 세기(295 — 발사 소켓, 길이 동안 0 으로 줄어듦). 0 = 끔.")]
    [Min(0f)] public float fireFlashIntensity;
    [Tooltip("발사 번쩍 길이(타임라인 초).")]
    [Min(0.01f)] public float fireFlashDuration = 0.15f;
    [Tooltip("발사 번쩍 반경(m).")]
    [Min(0f)] public float fireFlashRange = 6f;
    [Tooltip("발사 번쩍 색.")]
    public Color fireFlashColor = Color.white;

    [Tooltip("레이저 색 밝기(HDR 배율, 295).")]
    [Min(0f)] public float beamBrightness = 1f;
    [Tooltip("켬 = 레이저 색상을 아래 색으로(색상만 — 밝기·채도는 이펙트 것). 끔 = 한계 돌파 단계 색(지금).")]
    public bool beamColorOverride;
    [Tooltip("레이저 색(켰을 때 — 색상만 쓴다).")]
    public Color beamColor = Color.white;
    [Tooltip("레이저 페이드아웃(타임라인 초) — 유지 시간 뒤 이 길이 동안 알파 1 → 0 후 끔. 0 = 바로 끔(지금).")]
    [Min(0f)] public float beamFadeSeconds;

    public Stage StageAt(int stage) => stage switch
    {
        1 => stage1,
        2 => stage2,
        _ => stage3,
    };

    /// <summary>마커 k 의 차징 이펙트 값(0 = 차징 시작, 1~3 = 단계). 비었으면 기본(배율 1).</summary>
    public BossVictoryChargeFx FxAt(int k) => (k switch
    {
        0 => fxStart,
        1 => fxStage1,
        2 => fxStage2,
        _ => fxStage3,
    }) ?? BossVictoryChargeFx.Identity;
}

/// <summary>
/// 차징 루프 파트 하나 배율(할 일 295). 파트 = 렌더 모드로 가른다(<see cref="BossVictoryChargeFxApplier.PartOf"/>):
/// 빛줄기 = Stretch 입자(FX_GunnerCharge 의 ChargeParticle — lengthScale 15) · 입자 = Mesh 입자(ChargeLine) · 코어 = 그 밖(Charge 빌보드).
/// </summary>
[Serializable]
public sealed class BossVictoryChargePartFx
{
    [Tooltip("크기 배율 — 구체 크기(294)와 곱해 입자 크기·속도·방출 반지름에(이 파트가 Local 스케일이라 루트 크기가 안 먹어서 295 부터 이렇게).")]
    [Min(0f)] public float size = 1f;
    [Tooltip("입자량 배율 — 버스트 개수·rate.")]
    [Min(0f)] public float amount = 1f;
    [Tooltip("버스트 반복 간격(초). 0 = 프리팹 그대로(0.5초).")]
    [Min(0f)] public float repeatInterval;
    [Tooltip("수명 배율(새로 나오는 입자부터).")]
    [Min(0.01f)] public float lifetime = 1f;
    [Tooltip("재생 속도 배율(simulationSpeed — 처치 연출 이펙트 배율에 곱).")]
    [Min(0.01f)] public float speed = 1f;
    [Tooltip("색 밝기(HDR 배율 — 재질 색 칸, 1 = 그대로 손대지 않음).")]
    [Min(0f)] public float brightness = 1f;
    [Tooltip("길이 배율(lengthScale) — Stretch 파트(빛줄기)만.")]
    [Min(0.01f)] public float length = 1f;
}

/// <summary>차징 이펙트 마커 하나 값(할 일 295) — 파트 셋 + 포인트 라이트(차징 오프셋 자리).</summary>
[Serializable]
public sealed class BossVictoryChargeFx
{
    [Tooltip("빛줄기(Stretch 입자 — ChargeParticle).")]
    public BossVictoryChargePartFx line = new BossVictoryChargePartFx();
    [Tooltip("입자(Mesh 입자 — ChargeLine).")]
    public BossVictoryChargePartFx particle = new BossVictoryChargePartFx();
    [Tooltip("코어(빌보드 — Charge).")]
    public BossVictoryChargePartFx core = new BossVictoryChargePartFx();
    [Tooltip("포인트 라이트 세기(차징 중 총구 = 차징 오프셋 자리). 0 = 끔.")]
    [Min(0f)] public float lightIntensity;
    [Tooltip("포인트 라이트 반경(m).")]
    [Min(0f)] public float lightRange = 3f;
    [Tooltip("포인트 라이트 색.")]
    public Color lightColor = Color.white;

    /// <summary>값 없는 칸 대신(배율 1·라이트 꺼짐) — 고치지 말 것.</summary>
    public static readonly BossVictoryChargeFx Identity = new BossVictoryChargeFx();
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
        public bool LaserOn;        // 발사 ≤ t < 발사 + 유지 시간 + 페이드아웃(295)
        public float LaserFade;     // 레이저 알파(295) — 유지 중 1, 페이드아웃 동안 1 → 0
        public float FlashIntensity; // 발사 번쩍 라이트 세기(295) — 발사부터 길이 동안 세기 → 0
        public ChargeFxSample Fx;   // 차징 이펙트 파트 배율·포인트 라이트(295 — 마커 사이 보간)
    }

    /// <summary>파트 하나의 이 순간 배율(295) — <see cref="BossVictoryChargePartFx"/> 를 마커 사이 보간한 값.</summary>
    public struct PartSample
    {
        public float Size, Amount, RepeatInterval, Lifetime, Speed, Brightness, Length;

        public static PartSample One => new PartSample { Size = 1f, Amount = 1f, RepeatInterval = 0f, Lifetime = 1f, Speed = 1f, Brightness = 1f, Length = 1f };

        public static PartSample From(BossVictoryChargePartFx p) => p == null ? One : new PartSample
        {
            Size = p.size, Amount = p.amount, RepeatInterval = p.repeatInterval, Lifetime = p.lifetime,
            Speed = p.speed, Brightness = p.brightness, Length = p.length,
        };
    }

    /// <summary>차징 이펙트 이 순간 값(295) — 파트 셋·포인트 라이트.</summary>
    public struct ChargeFxSample
    {
        public PartSample Line, Particle, Core;
        public float LightIntensity, LightRange;
        public Color LightColor;

        public static ChargeFxSample From(BossVictoryChargeFx fx)
        {
            fx ??= BossVictoryChargeFx.Identity;
            return new ChargeFxSample
            {
                Line = PartSample.From(fx.line), Particle = PartSample.From(fx.particle), Core = PartSample.From(fx.core),
                LightIntensity = fx.lightIntensity, LightRange = fx.lightRange, LightColor = fx.lightColor,
            };
        }
    }

    public static Frame Evaluate(Times times, BossVictoryGunnerQ q, float t)
    {
        Times o = times.Ordered();
        var f = new Frame
        {
            Stage = StageAt(o, t),
            Fired = t >= o.Fire,
            SphereScale = SphereScale(o, q, t),
            Fx = EvaluateFx(o, q, t),
        };
        f.Charging = t >= o.ChargeStart && !f.Fired;
        f.SphereVisible = f.Charging && t >= o.ChargeStart + Mathf.Max(0f, q.sphereDelay);
        float sinceFire = t - o.Fire;
        float hold = Mathf.Max(0.01f, q.beamHoldSeconds), fade = Mathf.Max(0f, q.beamFadeSeconds);
        f.LaserOn = f.Fired && sinceFire < hold + fade;
        f.LaserFade = f.LaserOn ? LaserFade(sinceFire, hold, fade) : 0f;
        f.FlashIntensity = FireFlash(q.fireFlashIntensity, q.fireFlashDuration, sinceFire);
        return f;
    }

    /// <summary>레이저 알파(295) — 발사 뒤 <paramref name="hold"/> 동안 1, 그 뒤 <paramref name="fade"/> 동안 1 → 0(선형), 넘으면 0. 페이드 0 = 유지 끝에 바로 0.</summary>
    public static float LaserFade(float sinceFire, float hold, float fade)
    {
        if (sinceFire < hold) return 1f;
        if (fade <= 0f) return 0f;
        return Mathf.Clamp01(1f - (sinceFire - hold) / fade);
    }

    /// <summary>발사 번쩍 세기(295) — 발사 순간 <paramref name="peak"/> → 길이 동안 선형으로 0. 발사 전·길이 뒤·세기 0 = 0.</summary>
    public static float FireFlash(float peak, float duration, float sinceFire)
    {
        if (peak <= 0f || sinceFire < 0f || duration <= 0f || sinceFire >= duration) return 0f;
        return peak * (1f - sinceFire / duration);
    }

    /// <summary>
    /// 시각 t 가 어느 마커 사이인가 — from·to = 마커 번호(0 = 차징 시작, 1~3 = 단계), w = 도착 마커(to) 곡선을 지난 비율(0~1, 곡선에 따라 넘칠 수 있음).
    /// 차징 시작 전 = (0,0,0), 3단계 뒤 = (3,3,0), 두 마커가 같은 시각이면 뒤 마커로 바로(w = 1). 구체 크기(294)·차징 이펙트(295) 공용.
    /// </summary>
    public static void Segment(Times ordered, BossVictoryGunnerQ q, float t, out int from, out int to, out float w)
    {
        from = to = 0;
        w = 0f;
        float prevTime = ordered.ChargeStart;
        if (t < prevTime) return;
        for (int k = 1; k <= 3; k++)
        {
            float at = ordered.Stage(k);
            if (t < at)
            {
                float u = at > prevTime ? (t - prevTime) / (at - prevTime) : 1f;
                from = k - 1;
                to = k;
                w = BossVictoryKeys.Ease(q.StageAt(k).ease, u);
                return;
            }
            prevTime = at;
        }
        from = to = 3;
    }

    /// <summary>차징 이펙트(295) — 마커 값(차징 시작·1·2·3단계)을 구체 크기와 같은 구간·곡선으로 보간.</summary>
    public static ChargeFxSample EvaluateFx(Times ordered, BossVictoryGunnerQ q, float t)
    {
        Segment(ordered, q, t, out int from, out int to, out float w);
        ChargeFxSample a = ChargeFxSample.From(q.FxAt(from));
        if (from == to) return a;
        return Lerp(a, ChargeFxSample.From(q.FxAt(to)), w);
    }

    public static ChargeFxSample Lerp(in ChargeFxSample a, in ChargeFxSample b, float w) => new ChargeFxSample
    {
        Line = Lerp(a.Line, b.Line, w),
        Particle = Lerp(a.Particle, b.Particle, w),
        Core = Lerp(a.Core, b.Core, w),
        LightIntensity = Mathf.Max(0f, Mathf.LerpUnclamped(a.LightIntensity, b.LightIntensity, w)),
        LightRange = Mathf.Max(0f, Mathf.LerpUnclamped(a.LightRange, b.LightRange, w)),
        LightColor = Color.LerpUnclamped(a.LightColor, b.LightColor, w),
    };

    /// <summary>
    /// 파트 배율 보간 — 반복 간격은 한쪽이 0(= 프리팹 그대로)이면 섞지 않고 도착(w ≥ 1)부터 뒤 값, 그 전엔 앞 값(계단). 둘 다 값이 있으면 보간.
    /// 배율은 0 아래로 내려가지 않는다(곡선이 넘쳐도).
    /// </summary>
    public static PartSample Lerp(in PartSample a, in PartSample b, float w) => new PartSample
    {
        Size = Mathf.Max(0f, Mathf.LerpUnclamped(a.Size, b.Size, w)),
        Amount = Mathf.Max(0f, Mathf.LerpUnclamped(a.Amount, b.Amount, w)),
        RepeatInterval = a.RepeatInterval > 0f && b.RepeatInterval > 0f
            ? Mathf.Max(0.01f, Mathf.LerpUnclamped(a.RepeatInterval, b.RepeatInterval, w))
            : (w >= 1f ? b.RepeatInterval : a.RepeatInterval),
        Lifetime = Mathf.Max(0.01f, Mathf.LerpUnclamped(a.Lifetime, b.Lifetime, w)),
        Speed = Mathf.Max(0.01f, Mathf.LerpUnclamped(a.Speed, b.Speed, w)),
        Brightness = Mathf.Max(0f, Mathf.LerpUnclamped(a.Brightness, b.Brightness, w)),
        Length = Mathf.Max(0.01f, Mathf.LerpUnclamped(a.Length, b.Length, w)),
    };

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
        Segment(ordered, q, t, out int from, out int to, out float w);
        float a = ScaleAt(q, from);
        return from == to ? a : Mathf.LerpUnclamped(a, ScaleAt(q, to), w);
    }

    static float ScaleAt(BossVictoryGunnerQ q, int k) => k <= 0 ? q.startScale : q.StageAt(k).sphereScale;

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
