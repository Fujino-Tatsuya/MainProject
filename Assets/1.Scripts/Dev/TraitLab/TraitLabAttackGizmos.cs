#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>특성 랩 공격 범위 기즈모 분류(할 일 152) — 패널 체크 3개와 색이 이 단위다.</summary>
public enum TraitLabGizmoCategory
{
    Basic,
    Skill,
    Trait,
}

/// <summary>
/// 기즈모 한 개(할 일 152·153 6단계). 판정 도형 공격 = 판정 코드가 서버 판정에 넘기는 그 (<see cref="AttackShapeSet"/>, <see cref="AttackShapePose"/>) 를 그대로 담고
/// <see cref="AttackShapeDrawing"/> 으로만 그린다(여기서 수치·형상을 만들지 않는다).
/// 도형 판정이 아닌 것(거너 평타 캡슐 레이저·R 추적 구·간파 앵커 상자)만 콜라이더 형상(상자·구·캡슐)으로 담고 라벨에 '(도형 아님)' 을 붙인다.
/// </summary>
public readonly struct TraitLabGizmoShape
{
    public enum ShapeKind { Area, Box, Sphere, Capsule }

    public const string NotShapeSuffix = " (도형 아님)";

    public readonly ShapeKind Kind;
    public readonly TraitLabGizmoCategory Category;
    public readonly string Label;
    public readonly AttackShapeSet Area;     // Area — 판정 도형
    public readonly AttackShapePose Pose;    // Area — 판정 기준 포즈
    public readonly Vector3 Center;          // 상자 중심·구 중심·캡슐 끝 0
    public readonly Vector3 Extents;         // 상자 반 크기 / 캡슐 끝 1
    public readonly Quaternion Rotation;
    public readonly float Radius;

    TraitLabGizmoShape(ShapeKind kind, TraitLabGizmoCategory category, string label, AttackShapeSet area, in AttackShapePose pose,
        Vector3 center, Vector3 extents, Quaternion rotation, float radius)
    {
        Kind = kind;
        Category = category;
        Label = label;
        Area = area;
        Pose = pose;
        Center = center;
        Extents = extents;
        Rotation = rotation;
        Radius = radius;
    }

    /// <summary>판정 도형 공격 — 판정 함수가 낸 영역·포즈 그대로.</summary>
    public static TraitLabGizmoShape FromArea(TraitLabGizmoCategory c, string label, AttackShapeSet area, in AttackShapePose pose) =>
        new TraitLabGizmoShape(ShapeKind.Area, c, label, area, pose, Vector3.zero, Vector3.zero, Quaternion.identity, 0f);

    public static TraitLabGizmoShape Box(TraitLabGizmoCategory c, string label, Vector3 center, Vector3 halfExtents, Quaternion rotation) =>
        new TraitLabGizmoShape(ShapeKind.Box, c, label + NotShapeSuffix, null, default, center, halfExtents, rotation, 0f);

    public static TraitLabGizmoShape Sphere(TraitLabGizmoCategory c, string label, Vector3 center, float radius) =>
        new TraitLabGizmoShape(ShapeKind.Sphere, c, label + NotShapeSuffix, null, default, center, Vector3.zero, Quaternion.identity, radius);

    public static TraitLabGizmoShape Capsule(TraitLabGizmoCategory c, string label, Vector3 point0, Vector3 point1, float radius) =>
        new TraitLabGizmoShape(ShapeKind.Capsule, c, label + NotShapeSuffix, null, default, point0, point1, Quaternion.identity, radius);

    /// <summary>판정 앵커(ColliderInfo)의 지금 월드 형상 — 판정이 Overlap 에 넘기는 값과 같은 함수(Get*ColliderInfo). 형태가 없으면 false.</summary>
    public static bool TryFromAnchor(TraitLabGizmoCategory c, string label, ColliderInfo anchor, out TraitLabGizmoShape shape)
    {
        shape = default;
        if (anchor == null)
            return false;

        switch (anchor.OverlapCollider)
        {
            case OverlapCollider.Box:
                BoxColliderInfo box = default;
                anchor.GetBoxColliderInfo(ref box);
                shape = Box(c, label, box.center, box.halfExtents, box.orientation);
                return true;
            case OverlapCollider.Sphere:
                SphereColliderInfo sphere = default;
                anchor.GetSphereColliderInfo(ref sphere);
                shape = Sphere(c, label, sphere.center, sphere.radius);
                return true;
            case OverlapCollider.Capsule:
                CapsuleColliderInfo capsule = default;
                anchor.GetCapsuleColliderInfo(ref capsule);
                shape = Capsule(c, label, capsule.point0, capsule.point1, capsule.radius);
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// 플레이어 한 명의 공격 판정 영역을 모은다(할 일 152·153 6단계) — 지금 위치·바라보는 방향(<see cref="PlayerMovement.CurrentFacing"/>)에서 지금 쓰면 칠 영역.
/// 🔴 (도형, 포즈) 는 서버 판정이 부르는 바로 그 공용 함수에서만 받는다 — PlayerDefaultAttack.StepArea · AssassinBasicAttack.TryGetHitArea ·
/// FirstMeleeMainSkill.HoldArea/ShieldDashSampleArea · PaladinTraitRules.QBlastPose · FirstMeleeSubSkill.CounterArea/TraitPushArea ·
/// AssassinDashStrikeSkill.PathSegment · AssassinCircleStrikeSkill.ResolveStrikeArea · GunnerChargeLaserSkill.ShotArea. 여기서 복제하지 않는다.
/// 특성 파생 공격 명세(<see cref="PlayerTraitEffects.GetDerivedAttacks"/>)·거너 R 은 서버 전용이라 호스트 창에서만 나온다.
/// </summary>
public static class TraitLabAttackShapes
{
    static readonly List<TraitDerivedAttack> DerivedBuffer = new List<TraitDerivedAttack>();
    static readonly PaladinShieldDashPath ShieldDashPath = new PaladinShieldDashPath();
    static readonly PlayerSkillSlot[] Slots = { PlayerSkillSlot.Main, PlayerSkillSlot.Sub, PlayerSkillSlot.Ultimate, PlayerSkillSlot.Interrupt };

    const TraitLabGizmoCategory B = TraitLabGizmoCategory.Basic;
    const TraitLabGizmoCategory S = TraitLabGizmoCategory.Skill;
    const TraitLabGizmoCategory T = TraitLabGizmoCategory.Trait;

    public static void Collect(Player player, List<TraitLabGizmoShape> shapes)
    {
        if (player == null)
            return;

        Vector3 position = player.transform.position;
        // 조준/바라보는 방향 = 바닥 평면 단위 벡터(CurrentFacing). 판정 함수들이 받는 방향과 같은 꼴.
        Vector3 facing = player.TryGetComponent(out PlayerMovement movement)
            ? movement.CurrentFacing
            : new AttackShapePose(position, player.transform.forward).Forward;
        player.TryGetComponent(out PlayerTraitEffects traitEffects);

        CollectBasic(player, position, facing, traitEffects, shapes);
        CollectSkills(player, position, facing, traitEffects, shapes);
        CollectTrackingLasers(player, shapes);
    }

    // ── 기본 공격 ──

    static void CollectBasic(Player player, Vector3 position, Vector3 facing, PlayerTraitEffects traitEffects, List<TraitLabGizmoShape> shapes)
    {
        // 가붕이 — 근접(Overlap) 단마다 판정 도형.
        if (player.TryGetComponent(out DefaultAttackController melee) && melee.isActiveAndEnabled)
            AddDefaultSteps(melee.AttackSteps, position, facing, shapes);

        // 어쌔신 — 그 타의 판정 도형(칼날의 영역 배율)·포즈를 판정 함수 그대로.
        if (player.TryGetComponent(out AssassinBasicAttack assassin) && assassin.isActiveAndEnabled)
        {
            bool transformed = player.TryGetComponent(out AssassinState state) && state.IsTransformed;
            int normalCount = assassin.NormalStepCount;
            if (!transformed)
            {
                for (int i = 0; i < normalCount; i++)
                    AddAssassinStep(assassin, AssassinBasicAttackMode.Normal, i, facing, B, $"평타 {i + 1}타", shapes);
                AddAssassinStep(assassin, AssassinBasicAttackMode.Enhanced, 0, facing, B, "강타", shapes);
            }
            else
            {
                AddAssassinStep(assassin, AssassinBasicAttackMode.Transformed, 0, facing, B, "변신 묶음", shapes);
            }

            // 끝없는 칼날 — 일반 마지막 타·변신 묶음 마지막 판정과 같은 도형·포즈에 추가 베기.
            AssassinBasicAttackMode mode = transformed ? AssassinBasicAttackMode.Transformed : AssassinBasicAttackMode.Normal;
            if (traitEffects != null && HasDerived(traitEffects, AssassinBasicAttack.TraitKeyFor(mode), AssassinDerivedAttackKeys.ComboExtra))
                AddAssassinStep(assassin, mode, transformed ? 0 : normalCount - 1, facing, T, "끝없는 칼날", shapes);
        }

        // 거너 — 도형 판정 아님: 출발점에서 정면으로 Range 만큼, 반경 BeamWidth/2 SphereCast(벽에서 잘림).
        if (player.TryGetComponent(out GunnerBasicAttack gunner) && gunner.isActiveAndEnabled && gunner.Data != null)
        {
            GunnerBasicAttackData data = gunner.Data;
            Vector3 origin = gunner.ShotOrigin;
            float radius = data.BeamWidth * 0.5f;
            float length = player.TryGetComponent(out GunnerBeamAttack beam)
                ? beam.CastLength(origin, facing, data.Range, radius, data.BlockingLayers)
                : data.Range;
            shapes.Add(TraitLabGizmoShape.Capsule(B, $"평타 레이저 {length:0.#}m", origin, origin + facing * length, radius));

            if (traitEffects != null &&
                GunnerTraitRules.FindDerived(traitEffects, GunnerTraitRules.BasicAttackKey, GunnerTraitRules.BasicExtraDerivedKey, DerivedBuffer) != null)
                shapes.Add(TraitLabGizmoShape.Capsule(T, "이중 출력", origin, origin + facing * length, radius));
        }
    }

    /// <summary>가붕이 평타 — 근접(Overlap) 단마다 판정과 같은 <see cref="PlayerDefaultAttack.StepArea"/>.</summary>
    public static void AddDefaultSteps(IReadOnlyList<DefaultAttackStep> steps, Vector3 position, Vector3 facing, List<TraitLabGizmoShape> shapes)
    {
        if (steps == null)
            return;

        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i] == null || steps[i].HitType != DefaultAttackHitType.Overlap)
                continue;

            AttackShapeSet area = PlayerDefaultAttack.StepArea(steps[i], position, facing, out AttackShapePose pose);
            AddArea(B, $"평타 {i + 1}타", area, pose, shapes);
        }
    }

    static void AddAssassinStep(AssassinBasicAttack attack, AssassinBasicAttackMode mode, int step, Vector3 facing,
        TraitLabGizmoCategory category, string label, List<TraitLabGizmoShape> shapes)
    {
        if (attack.TryGetHitArea(mode, step, facing, out AttackShapeSet area, out AttackShapePose pose))
            AddArea(category, label, area, pose, shapes);
    }

    // ── 스킬 ──

    static void CollectSkills(Player player, Vector3 position, Vector3 facing, PlayerTraitEffects traitEffects, List<TraitLabGizmoShape> shapes)
    {
        if (!player.TryGetComponent(out PlayerSkillController controller))
            return;

        for (int s = 0; s < Slots.Length; s++)
        {
            PlayerSkillSlot slot = Slots[s];
            PlayerSkillBase skill = controller.GetSkill(slot);
            if (skill == null)
                continue;

            string slotName = SlotName(slot);
            TraitAttackKey key = TraitAttackKey.ForSkill(slot, controller.IsAlternateSkill(skill));

            // 앵커 판정(간파 상자 InterruptAttack 등) — 도형 판정 아님. OverlapHitboxAnchor 와 같은 앵커 월드 형상.
            if (TraitLabGizmoShape.TryFromAnchor(S, $"{slotName} 판정", skill.HitboxAnchor, out TraitLabGizmoShape anchorShape))
                shapes.Add(anchorShape);

            switch (skill)
            {
                case FirstMeleeMainSkill paladinQ when skill.Data is FirstMeleeMainSkillData qData:
                    AddPaladinQ(qData, position, facing, shapes);

                    // 돌파의 방패 — 경로 끝 표본(경로 = 판정과 같은 PaladinShieldDashPath, 모드·거리 = 판정과 같은 ResolveShieldDash).
                    PaladinShieldDashSettings dash = paladinQ.ResolveShieldDash(out bool hasDashTrait);
                    if (PaladinTraitRules.UsesShieldDash(hasDashTrait, dash))
                        AddShieldDashEnd(qData, dash, position, facing, shapes);

                    // 진격의 충격 — Q 가 지금 위치·방향에서 끝났다면(판정과 같은 파생 명세·QBlastPose).
                    if (traitEffects != null)
                    {
                        traitEffects.GetDerivedAttacks(key, DerivedBuffer);
                        for (int i = 0; i < DerivedBuffer.Count; i++)
                        {
                            if (DerivedBuffer[i].Key == PaladinTraitValues.QBlastKey)
                                AddQBlast(DerivedBuffer[i].HitArea, position, facing, shapes);
                        }
                    }
                    break;

                case FirstMeleeSubSkill paladinE when skill.Data is FirstMeleeSubSkillData eData:
                    AddPaladinE(eData, paladinE.TraitPushArea(), position, facing, shapes);
                    break;

                case AssassinShadowReturnSkill:
                    // 그림자 귀환(후속 Q)은 돌아가는 방향·거리를 따로 정한다 — 돌진 경로로 그리지 않는다.
                    break;

                case AssassinDashStrikeSkill dashStrike when dashStrike.DashData != null:
                    AddAssassinDash(dashStrike.DashData, slotName, position, facing,
                        traitEffects != null && HasDerived(traitEffects, key, AssassinDerivedAttackKeys.QTrail), shapes);
                    break;

                case AssassinCircleStrikeSkill circle when skill.Data is AssassinCircleStrikeSkillData cData:
                    AddAssassinCircle(cData, circle.RadiusMultiplier(), slotName, position, facing, shapes);
                    break;

                case GunnerChargeLaserSkill laser when skill.Data is GunnerChargeLaserData lData:
                    player.TryGetComponent(out GunnerBeamAttack laserBeam);
                    AddGunnerQ(laserBeam, lData, laser.EffectiveBeamWidth(lData), slotName, position, facing,
                        traitEffects != null &&
                        GunnerTraitRules.FindDerived(traitEffects, GunnerTraitRules.QAttackKey, GunnerTraitRules.QEchoDerivedKey, DerivedBuffer) != null,
                        shapes);
                    break;
            }
        }
    }

    /// <summary>가붕이 Q — 홀드 틱 판정(<see cref="FirstMeleeMainSkill.HoldArea"/>).</summary>
    public static void AddPaladinQ(FirstMeleeMainSkillData data, Vector3 position, Vector3 facing, List<TraitLabGizmoShape> shapes)
    {
        AttackShapeSet area = FirstMeleeMainSkill.HoldArea(data, position, facing, out AttackShapePose pose);
        AddArea(S, "Q 판정", area, pose, shapes);
    }

    /// <summary>돌파의 방패 — 경로 끝 표본(<see cref="FirstMeleeMainSkill.ShieldDashSampleArea"/>).</summary>
    public static void AddShieldDashEnd(FirstMeleeMainSkillData data, in PaladinShieldDashSettings dash, Vector3 position, Vector3 facing,
        List<TraitLabGizmoShape> shapes)
    {
        ShieldDashPath.Begin(position, facing, dash);
        AttackShapeSet area = FirstMeleeMainSkill.ShieldDashSampleArea(data, ShieldDashPath.PointAt(ShieldDashPath.Cap), position.y,
            ShieldDashPath.Heading, out AttackShapePose pose);
        ShieldDashPath.End();
        AddArea(T, $"돌파의 방패 끝 {dash.Distance:0.#}m", area, pose, shapes);
    }

    /// <summary>진격의 충격 — 파생 명세 hitArea + <see cref="PaladinTraitRules.QBlastPose"/>(Q 끝 지점 = 지금 위치).</summary>
    public static void AddQBlast(AttackShapeSet blastArea, Vector3 qEndPosition, Vector3 heading, List<TraitLabGizmoShape> shapes) =>
        AddArea(T, "진격의 충격", blastArea, PaladinTraitRules.QBlastPose(blastArea, qEndPosition, heading), shapes);

    /// <summary>가붕이 E — 반격(<see cref="FirstMeleeSubSkill.CounterArea"/>)과 수호의 파동(같은 포즈, 특성 없으면 null).</summary>
    public static void AddPaladinE(FirstMeleeSubSkillData data, AttackShapeSet pushArea, Vector3 position, Vector3 facing, List<TraitLabGizmoShape> shapes)
    {
        AttackShapeSet counter = FirstMeleeSubSkill.CounterArea(data, position, facing, out AttackShapePose pose);
        AddArea(S, "E 반격", counter, pose, shapes);
        AddArea(T, "수호의 파동", pushArea, pose, shapes);
    }

    /// <summary>어쌔신 Q — 돌진 경로 전체(0 ~ dashDistance, <see cref="AssassinDashStrikeSkill.PathSegment"/>). 추격의 궤적 = 같은 경로를 다시 훑는다.</summary>
    public static void AddAssassinDash(AssassinDashStrikeSkillData data, string slotName, Vector3 position, Vector3 facing, bool hasTrail,
        List<TraitLabGizmoShape> shapes)
    {
        AttackShapeSet area = AssassinDashStrikeSkill.PathSegment(data, position, position, facing, 0f, data.DashDistance, out AttackShapePose pose);
        AddArea(S, $"{slotName} 돌진 경로 {data.DashDistance:0.#}m", area, pose, shapes);
        if (hasTrail)
            AddArea(T, "추격의 궤적", area, pose, shapes);
    }

    /// <summary>어쌔신 변신 E — 원 중심 = 조준 방향 × 중심 거리(서버의 조준점 없음 경로), 칼날의 영역 반경 배율(<see cref="AssassinCircleStrikeSkill.ResolveStrikeArea"/>).</summary>
    public static void AddAssassinCircle(AssassinCircleStrikeSkillData data, float radiusMultiplier, string slotName, Vector3 position, Vector3 facing,
        List<TraitLabGizmoShape> shapes)
    {
        Vector3 aimPoint = position + facing * data.CenterDistance;
        AttackShapeSet area = AssassinCircleStrikeSkill.ResolveStrikeArea(data, position, aimPoint, radiusMultiplier, out AttackShapePose pose);
        AddArea(S, $"{slotName} 원형 5타", area, pose, shapes);
    }

    /// <summary>거너 Q — 최대 충전 1발(<see cref="GunnerChargeLaserSkill.ShotArea"/> — 지형 차단 길이·광폭 폭). 잔류 포격 = 첫 발 경로 재판정.</summary>
    public static void AddGunnerQ(GunnerBeamAttack beam, GunnerChargeLaserData data, float width, string slotName, Vector3 position, Vector3 facing,
        bool hasEcho, List<TraitLabGizmoShape> shapes)
    {
        Vector3 origin = GunnerChargeLaserSkill.FireOrigin(position, data);
        AttackShapeSet area = GunnerChargeLaserSkill.ShotArea(beam, data, origin, facing, data.RangeAt(1f), width, out AttackShapePose pose, out float length);
        AddArea(S, $"{slotName} 최대 충전 {length:0.#}m", area, pose, shapes);
        if (hasEcho)
            AddArea(T, "잔류 포격", area, pose, shapes);
    }

    // 거너 R — 떠 있는 추적 레이저마다 실제 피해 구(서버 값, 도형 판정 아님). 호스트 창에서만.
    static void CollectTrackingLasers(Player player, List<TraitLabGizmoShape> shapes)
    {
        GunnerTrackingLaser[] lasers = Object.FindObjectsByType<GunnerTrackingLaser>(FindObjectsSortMode.None);
        for (int i = 0; i < lasers.Length; i++)
        {
            if (lasers[i] != null && lasers[i].ServerCaster == player)
                shapes.Add(TraitLabGizmoShape.Sphere(S, "R 추적 레이저", lasers[i].transform.position, lasers[i].ServerDamageRadius));
        }
    }

    static void AddArea(TraitLabGizmoCategory category, string label, AttackShapeSet area, in AttackShapePose pose, List<TraitLabGizmoShape> shapes)
    {
        if (area != null && !area.IsEmpty)
            shapes.Add(TraitLabGizmoShape.FromArea(category, label, area, pose));
    }

    static bool HasDerived(PlayerTraitEffects effects, TraitAttackKey key, string derivedKey)
    {
        effects.GetDerivedAttacks(key, DerivedBuffer);
        return AssassinDerivedAttackKeys.TryFind(DerivedBuffer, derivedKey, out _);
    }

    static string SlotName(PlayerSkillSlot slot) => slot switch
    {
        PlayerSkillSlot.Main => "Q",
        PlayerSkillSlot.Sub => "E",
        PlayerSkillSlot.Ultimate => "R",
        PlayerSkillSlot.Interrupt => "간파",
        _ => slot.ToString(),
    };
}

/// <summary>
/// 특성 랩 공격 범위 기즈모 그리기(할 일 152·153 6단계) — <see cref="DebugTraitLab"/> 의 OnDrawGizmos 가 부른다. 씬의 모든 플레이어(호스트·클라 무관)를 매번 다시 모아
/// 획득 특성(배율·각도·반경)이 바뀌면 바로 반영된다. 판정 도형은 <see cref="AttackShapeDrawing"/> 만 그린다(바닥 진하게 + 높이 범위 옅게, 도넛 안쪽 고리 포함).
/// Gizmos 라 Game 뷰는 Gizmos 버튼이 켜져 있어야 보인다.
/// </summary>
public sealed class TraitLabAttackGizmos
{
    static readonly Color BasicColor = new Color(0.25f, 0.85f, 1f);
    static readonly Color SkillColor = new Color(1f, 0.62f, 0.15f);
    static readonly Color TraitColor = new Color(1f, 0.3f, 0.85f);
    const float LabelLift = 0.1f;

    public bool Enabled;
    public bool ShowBasic = true;
    public bool ShowSkill = true;
    public bool ShowTrait = true;

    readonly List<TraitLabGizmoShape> shapes = new List<TraitLabGizmoShape>();
    readonly List<(Vector3 at, TraitLabGizmoCategory category, string text)> labels = new List<(Vector3, TraitLabGizmoCategory, string)>();
    GUIStyle _labelStyle;

    public static Color ColorOf(TraitLabGizmoCategory category) => category switch
    {
        TraitLabGizmoCategory.Basic => BasicColor,
        TraitLabGizmoCategory.Skill => SkillColor,
        _ => TraitColor,
    };

    public bool Shows(TraitLabGizmoCategory category) => category switch
    {
        TraitLabGizmoCategory.Basic => ShowBasic,
        TraitLabGizmoCategory.Skill => ShowSkill,
        _ => ShowTrait,
    };

    public void Draw()
    {
        if (!Enabled || !Application.isPlaying)
            return;

        shapes.Clear();
        labels.Clear();
        Player[] players = Object.FindObjectsByType<Player>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
            TraitLabAttackShapes.Collect(players[i], shapes);

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        for (int i = 0; i < shapes.Count; i++)
        {
            if (Shows(shapes[i].Category))
                DrawShape(shapes[i]);
        }

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;

        _labelStyle ??= new GUIStyle(EditorStyles.boldLabel);
        foreach ((Vector3 at, TraitLabGizmoCategory category, string text) in labels)
        {
            _labelStyle.normal.textColor = ColorOf(category);
            Handles.Label(at, text, _labelStyle);
        }
    }

    void DrawShape(in TraitLabGizmoShape shape)
    {
        Color color = ColorOf(shape.Category);
        Gizmos.color = color;
        Gizmos.matrix = Matrix4x4.identity;
        Vector3 labelAt;

        switch (shape.Kind)
        {
            case TraitLabGizmoShape.ShapeKind.Area:
                AttackShapeDrawing.DrawGizmos(shape.Area, shape.Pose, color);
                labelAt = AreaLabelPoint(shape.Area, shape.Pose);
                break;

            case TraitLabGizmoShape.ShapeKind.Box:
                Gizmos.matrix = Matrix4x4.TRS(shape.Center, shape.Rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, shape.Extents * 2f);
                Gizmos.matrix = Matrix4x4.identity;
                labelAt = shape.Center + shape.Rotation * new Vector3(0f, shape.Extents.y, shape.Extents.z);
                break;

            case TraitLabGizmoShape.ShapeKind.Sphere:
                Gizmos.DrawWireSphere(shape.Center, shape.Radius);
                labelAt = shape.Center + Vector3.up * shape.Radius;
                break;

            default: // Capsule
                DrawCapsule(shape.Center, shape.Extents, shape.Radius);
                labelAt = shape.Extents + Vector3.up * shape.Radius;
                break;
        }

        AddLabel(labelAt, shape.Category, shape.Label);
    }

    // 라벨 자리 = 첫 도형의 넓은 질의 상자 윗면 가운데(도형 클래스 함수) — 같은 자리 같은 분류 라벨은 한 줄로 모은다(평타 단이 같은 도형일 때).
    static Vector3 AreaLabelPoint(AttackShapeSet area, in AttackShapePose pose)
    {
        foreach (AttackShape s in area.Shapes)
        {
            if (s == null)
                continue;
            s.GetBroadBox(pose, out Vector3 center, out Vector3 half, out _);
            return center + Vector3.up * (half.y + LabelLift);
        }
        return pose.Position;
    }

    void AddLabel(Vector3 at, TraitLabGizmoCategory category, string text)
    {
        for (int i = 0; i < labels.Count; i++)
        {
            if (labels[i].category == category && (labels[i].at - at).sqrMagnitude < 1e-4f)
            {
                labels[i] = (labels[i].at, category, labels[i].text + " · " + text);
                return;
            }
        }
        labels.Add((at, category, text));
    }

    static void DrawCapsule(Vector3 p0, Vector3 p1, float radius)
    {
        Gizmos.DrawWireSphere(p0, radius);
        Gizmos.DrawWireSphere(p1, radius);
        Vector3 axis = p1 - p0;
        Vector3 side = axis.sqrMagnitude > 1e-6f ? Vector3.Cross(axis.normalized, Vector3.up) : Vector3.right;
        if (side.sqrMagnitude < 1e-6f)
            side = Vector3.right;
        side = side.normalized * radius;
        Vector3 up = (axis.sqrMagnitude > 1e-6f ? Vector3.Cross(side, axis.normalized) : Vector3.up).normalized * radius;
        Gizmos.DrawLine(p0 + side, p1 + side);
        Gizmos.DrawLine(p0 - side, p1 - side);
        Gizmos.DrawLine(p0 + up, p1 + up);
        Gizmos.DrawLine(p0 - up, p1 - up);
    }
}
#endif
