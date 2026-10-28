using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 거너 Q — 충전 레이저 / 정신 집중 (character_gunner.md §6, D3·D4·D5, PLAN-gunner.md G4).
///
/// Q → 정신 집중(Focus 버프 + 감속, 이동 가능, 조준 방향을 계속 바라봄) — 시작 순간 과열 단계 저장.
/// 좌클릭 또는 (최대 집중 + 유지시간) 자동 → 현재 조준으로 발사: 사거리·피해는 집중 시간으로 최소→최대 보간,
/// 지형에서 끊기는 굵은 직선 안의 적·오브젝트에 피해, 아군(시전자 제외)에 보호막(합산) — 전부 대상별 1회.
/// 쿨타임은 발사 순간부터(수동 커밋). 발사 없이 끝나면(쓰러짐·사망·조작 불가·피격 취소) 쿨타임 없음.
/// Q 재입력은 취소가 아니다(§3.3 — 실행 중 같은 슬롯 시전은 컨트롤러가 거절). 과열도는 건드리지 않는다(축열 포격 특성만 첫 발에서 모두 소모).
/// 발사 후 회복 종료 = 발사 때 GunnerBeamView 가 트는 Gunner_Q_Fire 클립(gunner_skill_Q_fire)의 End 이벤트(서버).
/// 안 오면 maxActiveDuration 안전망(시전 시작부터).
/// </summary>
public class GunnerChargeLaserSkill : PlayerSkillBase, ISkillPreviewSource
{
    private GunnerHeat heat;
    private GunnerBeamAttack beam;
    private GunnerBeamView view;
    private PlayerMovement movement;
    private SkillLineIndicator lineIndicator;
    private PlayerEnemyHoverOutline hoverOutline;
    private readonly List<Player> allies = new List<Player>();

    // 서버 런타임
    private float chargeStartTime;
    // 시전 스냅샷(할 일 127 3/4) — 시작 순간 과열 단계 + 첫 발 직전 축열 소모. 원래 발 전체·잔류 포격이 같은 값을 쓴다.
    private readonly GunnerQCastSnapshot castSnapshot = new GunnerQCastSnapshot();
    private Vector3 aim;
    private bool fired;
    private bool statusApplied;
    private float chargeMaxTime;
    private float ownerChargeStartTime;
    private float ownerChargeMaxTime;

    // 특성(할 일 127) — 충격 레이저·집중 포화·잔류 포격. 서버에서만 쓴다.
    // 원래 발 묶음은 지금 단발 Q 1발이다. 집속 연사(4/4)는 발마다 같은 시전 ID·같은 echoPlan 에 AddOriginalShot 한다.
    private PlayerTraitEffects traitEffects;
    private GunnerBeamAttack.TargetHitHook originalHitHook;
    private GunnerBeamAttack.TargetHitHook echoHitHook;
    private Vector3 hitDirection;
    private readonly GunnerCastCcGate pushGate = new GunnerCastCcGate();
    private readonly GunnerEchoPlan echoPlan = new GunnerEchoPlan();
    private readonly GunnerDelayedShots echoShots = new GunnerDelayedShots();
    private readonly List<GunnerDerivedShot> dueEchoShots = new List<GunnerDerivedShot>();
    private readonly List<TraitDerivedAttack> derivedBuffer = new List<TraitDerivedAttack>();
    private bool lifeSubscribed;

    /// <summary>
    /// [서버] Q 를 실제로 <b>발사</b>했다(충전 시작·대시 취소·피격 종료는 안 난다). 튜토리얼 판정 훅(v8 Z1-5 "거너 Q 는 발사까지").
    /// 승인 시점 신호는 <see cref="PlayerSkillController.ServerSkillUsed"/>(충전 시작)이다.
    /// </summary>
    public static event System.Action<Player> ServerFired;

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 구독자가 남지 않게 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticEvents() => ServerFired = null;

    public override PlayerSkillSlot Slot => PlayerSkillSlot.Main;
    public override PlayerActionState EntryActionState => PlayerActionState.Focus; // 발사하면 Skill(GunnerBeamView)
    public override bool CanMoveWhileActive => true;
    public override bool CanMovementRotateWhileActive => false; // 이동 방향이 아니라 조준을 바라본다(§6.2)
    public override bool ConsumesPrimaryInput => true;
    public override bool WantsAimUpdates => true; // 자동 발사도 "현재 조준 방향"

    // 공용 대시는 정신 집중만 끊는다(D1). 발사 후 회복(Skill)은 끊지 않는다.
    public override bool CanBeCanceledByDash(PlayerActionState phase) => phase == PlayerActionState.Focus;

    private GunnerChargeLaserData QData => Data as GunnerChargeLaserData;
    private ulong SourceId => owner != null ? owner.NetworkObjectId : 0;
    private bool HasAuthority => owner != null && (!owner.IsSpawned || owner.IsServer);
    private PlayerTraitModifiers Traits => owner != null ? owner.TraitModifiers : null;

    // 특성(할 일 127) — 고속 집속 = 최대 충전시간 ×0.75(피해·사거리 끝점·자동 발사 유지시간 그대로),
    // 광폭 레이저 = 판정 폭 ×1.3(사거리·높이 그대로). 시전 시작(충전시간)·발사(폭) 순간 값으로 쓴다.
    private float EffectiveMaxChargeTime(GunnerChargeLaserData data) =>
        GunnerTraitRules.QMaxChargeTimeFor(Traits, data.MaxChargeTime);

    private float EffectiveBeamWidth(GunnerChargeLaserData data) =>
        GunnerTraitRules.QBeamWidthFor(Traits, data.BeamWidth);

    private static float ChargeFactor(GunnerChargeLaserData data, float elapsed, float maxChargeTime) =>
        data.ChargeFactor(GunnerTraitRules.DataClockElapsed(elapsed, data.MaxChargeTime, maxChargeTime));

    public override void Initialize(Player owner, PlayerSkillController controller)
    {
        base.Initialize(owner, controller);
        heat = owner.GetComponent<GunnerHeat>();
        beam = owner.GetComponent<GunnerBeamAttack>();
        view = owner.GetComponent<GunnerBeamView>();
        movement = owner.GetComponent<PlayerMovement>();
        lineIndicator = owner.GetComponentInChildren<SkillLineIndicator>(true);
        hoverOutline = owner.GetComponent<PlayerEnemyHoverOutline>();
        originalHitHook = ApplyOriginalHit;
        echoHitHook = ApplyEchoHit;
    }

    private void OnEnable()
    {
        if (!lifeSubscribed)
        {
            PlayerLifeCycleController.ServerLifeStateChanged += HandleLifeStateChanged;
            lifeSubscribed = true;
        }
    }

    // 씬 종료·비활성 — 예약한 잔류 포격을 버린다(명세 §9).
    private void OnDisable()
    {
        if (lifeSubscribed)
        {
            PlayerLifeCycleController.ServerLifeStateChanged -= HandleLifeStateChanged;
            lifeSubscribed = false;
        }

        echoShots.Clear();
    }

    // 사망(살아 있음 → 그 밖) — 예약한 잔류 포격 취소·밀치기 승인 기억 정리(공용 밀치기 장부와 같은 시점).
    private void HandleLifeStateChanged(ulong clientId, PlayerLifeState previous, PlayerLifeState current)
    {
        if (owner != null && clientId == owner.OwnerClientId && previous == PlayerLifeState.Alive && current != PlayerLifeState.Alive)
        {
            echoShots.Clear();
            pushGate.Reset();
        }
    }

    public override bool CanUse(Vector3 direction, Unit target) => QData != null && beam != null;

    public override void OnServerStart(Vector3 direction, Unit target)
    {
        State = SkillState.Charging;
        chargeStartTime = Time.time;
        chargeMaxTime = EffectiveMaxChargeTime(QData);
        aim = direction;
        fired = false;
        // 새 시전은 새 묶음 — 이미 예약된 이전 시전의 잔류 포격(echoShots)은 덮어쓰지 않는다.
        echoPlan.Reset();

        // 시전 시작 순간의 단계를 저장 — 과열 중이면 3단계(§5.6). 정온 운용이면 0.
        int storedStage = heat != null ? heat.CaptureStage() : 0;
        castSnapshot.Begin(storedStage);

        StatusEffectController effects = owner.StatusEffects;
        if (effects != null)
        {
            effects.Apply(StatusEffectType.Focus, 0f, SourceId);
            effects.Apply(StatusEffectType.MoveSpeedModifier, QData.ChargeMoveSpeedMultiplier, 0f, SourceId);
            statusApplied = true;
        }

        Edit.Log($"[Gunner/Q] 정신 집중 시작 — 저장 단계 {storedStage}", this);
    }

    // 집중 애니는 Data.AnimatorStateName 이 튼다. 여기서는 **총구 충전 연출**만 켠다.
    // 🔴 이건 전 피어에서 돈다(PlayerSkillController.PlaySkillClientRpc → OnClientPlay).
    //    서버 가드를 넣으면 호스트에서만 보인다.
    public override void OnClientPlay(Vector3 direction)
    {
        ownerChargeStartTime = Time.time;
        if (QData != null)
        {
            // 연출 코드는 그대로 — 충전 완료 시점만 특성 충전시간에 맞춰 넘긴다.
            ownerChargeMaxTime = EffectiveMaxChargeTime(QData);
            view?.BeginCharge(ownerChargeMaxTime);
        }
    }

    public override void OnOwnerTick(Vector3 aimDirection)
    {
        movement?.RotateImmediately(aimDirection);

        if (view == null || !view.IsCharging)
        {
            lineIndicator?.HideCharge();
            return;
        }

        GunnerChargeLaserData data = QData;
        if (data == null || beam == null || lineIndicator == null || owner == null)
            return;

        aimDirection.y = 0f;
        aimDirection = aimDirection.sqrMagnitude > 0.001f
            ? aimDirection.normalized
            : owner.transform.forward;
        float factor = ChargeFactor(data, Time.time - ownerChargeStartTime, ownerChargeMaxTime);
        float range = data.RangeAt(factor);
        float width = EffectiveBeamWidth(data);
        Vector3 origin = owner.transform.position + Vector3.up * data.MuzzleHeight;
        // 지형 차단은 데이터 폭으로 잰다 — 광폭 레이저가 사거리를 바꾸지 않게(발사와 같은 식).
        float length = beam.CastLength(origin, aimDirection, range, data.BeamWidth * 0.5f, data.BlockingLayers);
        lineIndicator.ShowCharge(origin, aimDirection, 0f, length, width);
        // 지금 쏘면 맞을 적에 외곽선(할 일 58) — 발사(Fire)와 같은 사거리·폭·높이·앵커.
        hoverOutline?.ReportSkillHitArea(FireVolume(origin, aimDirection, length, width, data));
    }

    /// <summary>발사 판정 상자 — <see cref="Fire"/> 의 <see cref="GunnerBeamAttack.FirePiercing"/> 와 같은 인자(오너 미리보기 공용). 폭 = 데이터 폭(특성 없음).</summary>
    public static SkillHitVolume FireVolume(Vector3 origin, Vector3 direction, float length, GunnerChargeLaserData data) =>
        FireVolume(origin, direction, length, data.BeamWidth, data);

    /// <summary>폭을 따로 받는 판(광폭 레이저 — <see cref="GunnerTraitRules.QBeamWidthFor"/>).</summary>
    public static SkillHitVolume FireVolume(Vector3 origin, Vector3 direction, float length, float width, GunnerChargeLaserData data)
    {
        return SkillHitVolume.Beam(origin, direction, length, width, data.BeamHeight, data.HittableLayers);
    }

    public override void OnAimUpdated(Vector3 direction) => aim = direction;

    public override void OnPrimaryPressed(Vector3 direction)
    {
        if (State == SkillState.Charging)
            Fire(direction);
    }

    public override void OnTick()
    {
        GunnerChargeLaserData data = QData;
        if (data == null)
            return;

        if (State == SkillState.Charging && Time.time >= chargeStartTime + GunnerTraitRules.AutoFireDelay(chargeMaxTime, data.AutoFireHoldTime))
            Fire(aim);
    }

    public override void OnAnimationEvent(SkillAnimationEventType eventType)
    {
        // 발사 전(집중 중)에는 Q_Fire 가 재생되지 않는다 — 혹시 와도 무시.
        if (fired && eventType == SkillAnimationEventType.End)
            EndSelf(SkillEndReason.Completed);
    }

    private void Fire(Vector3 direction)
    {
        GunnerChargeLaserData data = QData;
        lineIndicator?.HideCharge();
        hoverOutline?.ClearSkillHitArea();
        State = SkillState.Active;
        fired = true;

        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : owner.transform.forward;
        movement?.RotateImmediately(direction);

        float factor = ChargeFactor(data, Time.time - chargeStartTime, chargeMaxTime);
        float range = data.RangeAt(factor);
        float width = EffectiveBeamWidth(data);
        // 축열 포격(할 일 127 3/4) — 첫 발 실제 발사 직전 남은 열을 모두 소모(열 0·과열 해제)하고 그 비율로 Q 피해 보너스.
        // 소모와 발사 커밋이 같은 서버 호출 안이다(원자). 충전 취소·발사 전 종료는 여기 오지 않아 소모 없음, 빗나가도 소모.
        GunnerHeatTraits heatTraits = heat != null ? heat.Traits : GunnerHeatTraits.From(Traits);
        if (!castSnapshot.FirstShotCommitted)
        {
            float spent = heatTraits.SpendsHeatOnQ && heat != null ? heat.ServerSpendAll() : 0f;
            castSnapshot.CommitFirstShot(heatTraits, spent);
        }

        // 툴팁의 최소~최대 계산도 같은 집중·과열 배율을 쓰므로 판정 공식을 바꿀 때 같이 바꿀 것(GunnerChargeLaserData·GunnerTraitTooltip).
        // 한계 돌파 = 단계 배율표 교체, 정온 운용 = 0단계. 보호막 단계 배율은 원래 표(축열 보너스 없음).
        int storedStage = heatTraits.EffectiveStage(castSnapshot.StoredStage);
        int damage = castSnapshot.ShotDamage(damageSnapshot, data.DamageMultiplierAt(factor),
            heatTraits.SkillStageScale(storedStage, data.StageDamageMultiplier(storedStage)));
        int shield = Mathf.Max(0, Mathf.RoundToInt(data.ShieldAmount * data.StageShieldMultiplier(storedStage)));

        Vector3 origin = owner.transform.position + Vector3.up * data.MuzzleHeight;
        float length = beam.CastLength(origin, direction, range, data.BeamWidth * 0.5f, data.BlockingLayers);

        hitDirection = direction;
        int hitCount = beam.FirePiercing(origin, direction, length, width, data.BeamHeight,
                                         data.HittableLayers, damage, DamageAttackType,
                                         DamageHitPattern, data.TriggersOnHit,
                                         TraitHitFlags.Original, originalHitHook);
        // 잔류 포격 묶음 — 예정 단일 대상 피해(실제 총 피해·맞힌 수 아님)와 첫 발 위치·방향·최종 충전 사거리·폭.
        echoPlan.AddOriginalShot(origin, direction, range, width, damage);

        beam.CollectAllies(origin, direction, length, width, data.BeamHeight, data.AllyLayers, allies);
        for (int i = 0; i < allies.Count; i++)
            allies[i].AddShield(ShieldType.GunnerCharge, SourceId, shield, data.ShieldDuration);

        controller.CommitCooldownServer(Slot);
        ClearStatus();
        ServerFired?.Invoke(owner);

        // 🔴 **지형에 막혔는지를 같이 보낸다.** 끝점 좌표만으로는 벽에 맞은 것과
        //    최대 사거리까지 뻗어 허공에서 끝난 것을 구분할 수 없다 —
        //    착탄 연출을 띄울지 말지가 여기서 갈린다(좌클릭의 stopped 와 같은 신호).
        bool stopped = length < range - 0.01f;
        // 보호막 연출은 안 싣는다 — 받은 아군의 PlayerShieldVfx 가 복제된 보호막 목록을 보고 띄운다.
        // 🔴 연출 폭은 데이터 폭 그대로(광폭 레이저 VFX 폭 반영은 별도 할 일 — 판정만 넓힌다).
        view?.ServerChargeLaserFired(origin, origin + direction * length, data.BeamWidth,
                                     stopped, BuildHitPoints(origin, direction, length));

        Edit.Log($"[Gunner/Q] 발사 — 집중 {factor:0.00}(최대 {chargeMaxTime:0.##}초), 사거리 {length:0.0}/{range:0.0}, 폭 {width:0.##}, 피해 {damage} ×{hitCount}, " +
                 $"보호막 {shield} ×{allies.Count}, 저장 단계 {storedStage}, 과열 모드 {heatTraits.Mode}" +
                 (heatTraits.SpendsHeatOnQ ? $", 축열 소모 {castSnapshot.HeatSpentRatio:0.00} ×{castSnapshot.HeatShotScale:0.00}" : ""), this);
    }

    /// <summary>
    /// 피해를 준 몹마다 <b>빔 위의 어디서 맞았는지</b>를 구해 전 피어에 보낸다.
    ///
    /// 🔴 몹의 위치를 그대로 쓰지 않고 <b>빔 축에 투영</b>한다. 판정은 폭 1.2m 직육면체라
    /// 몹 중심이 빔에서 비껴 있을 수 있는데, 그 자리에 연출을 찍으면 레이저 옆 허공에서 터진다.
    ///
    /// 높이는 몹 중심을 쓴다 — 빔 축 높이(총구 1m)로 고정하면 큰 몹은 발치에서 터진다.
    /// </summary>
    private Vector3[] BuildHitPoints(Vector3 origin, Vector3 direction, float length)
    {
        IReadOnlyList<Unit> landed = beam != null ? beam.LastPierceLanded : null;
        if (landed == null || landed.Count == 0)
            return System.Array.Empty<Vector3>();

        // 관통이라 이론상 제한이 없다. RPC 를 작게 유지하려고 자른다 —
        // 한 발에 8곳이면 눈으로는 이미 "전부 맞았다"로 읽힌다.
        int count = Mathf.Min(landed.Count, 8);
        var points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 center = landed[i].transform.position;
            float along = Mathf.Clamp(Vector3.Dot(center - origin, direction), 0f, length);
            Vector3 onBeam = origin + direction * along;
            points[i] = new Vector3(onBeam.x, center.y + 1f, onBeam.z);
        }
        return points;
    }

    public override void OnEnd(SkillEndReason reason)
    {
        if (fired)
            GunnerSkillEventRules.WarnIfFallbackEnded(reason, "Q", "gunner_skill_Q_fire", SkillAnimationEventType.End, this);
        lineIndicator?.HideCharge();
        hoverOutline?.ClearSkillHitArea();
        // 🔴 **권한 가드 밖이다.** 발사 없이 끝나는 경로(대시 취소·피격·사망)에서도
        //    전 피어가 충전 루프를 꺼야 한다. 안 그러면 그 피어에 총구 연출이 남는다.
        //    (발사로 끝난 경우는 PlayChargeLaser 가 이미 껐고, Stop 은 두 번 불러도 무해하다.)
        view?.EndCharge();

        if (HasAuthority)
        {
            if (!fired && reason == SkillEndReason.DashCancelled)
            {
                // 대시로 끊으면 쿨타임 적용(D2 — "Q 는 취소 불가" 의도를 지킨다)
                controller.CommitCooldownServer(Slot);
                Edit.Log("[Gunner/Q] 대시로 집중 취소 — 발사 없음, 쿨타임 적용", this);
            }
            else if (!fired)
            {
                // 쓰러짐·사망·조작 불가·피격 취소 = 쿨타임 없음(수동 커밋이라 자연히 안 돈다 — §6.6)
                Edit.Log($"[Gunner/Q] 발사 없이 종료({reason}) — 쿨타임 없음", this);
            }
            ScheduleEcho(reason);
            ClearStatus();
        }

        fired = false;
        base.OnEnd(reason);
    }

    public bool TryGetPreview(Vector3 origin, Vector3 forward, out SkillPreviewShape shape)
    {
        GunnerChargeLaserData data = QData;
        if (data == null || beam == null || owner == null)
        {
            shape = default;
            return false;
        }

        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : owner.transform.forward;
        Vector3 castOrigin = owner.transform.position + Vector3.up * data.MuzzleHeight;
        float radius = data.BeamWidth * 0.5f;
        float clippedMin = beam.CastLength(castOrigin, forward, data.MinRange, radius, data.BlockingLayers);
        float clippedMax = beam.CastLength(castOrigin, forward, data.MaxRange, radius, data.BlockingLayers);
        shape = SkillPreviewShapes.ChargeLaser(
            data.MinRange,
            data.MaxRange,
            EffectiveBeamWidth(data),
            clippedMin,
            clippedMax);
        return true;
    }

    // ── 특성(할 일 127) ──────────────────────────────────────────────────

    private PlayerTraitEffects TraitEffects
    {
        get
        {
            if (traitEffects == null && owner != null)
                traitEffects = PlayerTraitEffects.For(owner);
            return traitEffects;
        }
    }

    // 원래 Q 발 대상 1건 — 집중 포화(자기 R 범위 안이면 피해 배율) → 충격 레이저(밀치기).
    private void ApplyOriginalHit(Unit target, ref AttackInfo info)
    {
        GunnerTrackingLaser.ApplyFocusFire(owner, target, ref info);
        ApplyImpactPush(target, ref info);
    }

    // 잔류 포격 대상 1건 — 집중 포화만(파생 = 밀치기·보호막·자원 없음).
    private void ApplyEchoHit(Unit target, ref AttackInfo info) => GunnerTrackingLaser.ApplyFocusFire(owner, target, ref info);

    /// <summary>
    /// 충격 레이저(GUN_3_Q_PUSH) — 공용 밀치기 경로(<see cref="PlayerTraitEffects.ModifyOutgoingAttackServer"/> → TraitKnockbackEffect.ApplyCrowdControl)를
    /// 레이저 진행 방향으로. 시전 전체에서 대상별 유효 승인 1회 — 지금 슈퍼아머(면역)인 대상은 요청하지 않아 같은 시전의 다음 원래 발에서 승인될 수 있다.
    /// 특성이 없으면 공용 경로가 아무것도 바꾸지 않는다(승인 기록도 없음).
    /// </summary>
    private void ApplyImpactPush(Unit target, ref AttackInfo info)
    {
        if (target == null || TraitEffects == null)
            return;

        int castId = ServerCastId;
        int targetId = target.GetInstanceID();
        if (!pushGate.ShouldRequest(castId, targetId, target.HasSuperArmor))
            return;

        float before = info.knockbackStrength * info.knockbackDuration;
        TraitEffects.ModifyOutgoingAttackServer(GunnerTraitRules.QAttackKey, castId, target, hitDirection, ref info);
        if (info.knockbackStrength * info.knockbackDuration > before)
            pushGate.MarkApproved(castId, targetId);
    }

    // [서버] 잔류 포격(GUN_4_ECHO_Q) — 정상 종료(발사 후 End 이벤트·그 안전망)에서만 지연 뒤 1회 예약. 중단·사망·발사 전 종료면 없음.
    private void ScheduleEcho(SkillEndReason reason)
    {
        TraitDerivedAttack echo = GunnerTraitRules.FindDerived(TraitEffects, GunnerTraitRules.QAttackKey,
            GunnerTraitRules.QEchoDerivedKey, derivedBuffer);
        if (echoPlan.TryBuildEcho(fired, reason, echo, out GunnerDerivedShot shot))
        {
            echoShots.Schedule(Time.timeAsDouble, echo.Delay, shot);
            Edit.Log($"[Gunner/Q] 잔류 포격 예약 — {echo.Delay:0.##}초 뒤, 피해 {shot.Damage}(예정 합계 {echoPlan.PlannedDamageSum})", this);
        }

        echoPlan.Reset();
    }

    // [서버] 예약된 잔류 포격 — 저장한 위치·방향·충전 사거리·폭, 지형 차단은 지금 다시 잰다. 판정만(VFX 없음, 할 일 127 범위 밖).
    // 열 소모·보호막·밀치기·쿨·적중 사건·연쇄 없음(Derived).
    private void Update()
    {
        if (echoShots.Count == 0 || !HasAuthority)
            return;

        echoShots.CollectDue(Time.timeAsDouble, dueEchoShots);
        GunnerChargeLaserData data = QData;
        if (data == null || beam == null || owner.CurrentHealth <= 0)
            return;

        for (int i = 0; i < dueEchoShots.Count; i++)
        {
            GunnerDerivedShot shot = dueEchoShots[i];
            float length = beam.CastLength(shot.Origin, shot.Direction, shot.Range, data.BeamWidth * 0.5f, data.BlockingLayers);
            int hitCount = beam.FirePiercing(shot.Origin, shot.Direction, length, shot.Width, data.BeamHeight,
                                             data.HittableLayers, shot.Damage, DamageAttackType, DamageHitPattern,
                                             false, TraitHitFlags.Derived, echoHitHook);
            Edit.Log($"[Gunner/Q] 잔류 포격 — 사거리 {length:0.0}/{shot.Range:0.0}, 폭 {shot.Width:0.##}, 피해 {shot.Damage} ×{hitCount}", this);
        }
    }

    private void ClearStatus()
    {
        if (!statusApplied || !HasAuthority)
            return;

        StatusEffectController effects = owner.StatusEffects;
        if (effects != null)
        {
            effects.Remove(StatusEffectType.Focus, SourceId);
            effects.Remove(StatusEffectType.MoveSpeedModifier, SourceId);
        }
        statusApplied = false;
    }
}
