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
/// 기즈모 한 개 — 판정 코드가 만든 형상 값을 그대로 담는다(여기서 수치를 만들지 않는다).
/// 상자(OverlapBox·SkillHitVolume.Beam)·구·캡슐(SphereCast 경로 포함)·바닥 부채꼴(360 = 원)·원기둥(SkillHitVolume.CircleColumn).
/// </summary>
public readonly struct TraitLabGizmoShape
{
    public enum ShapeKind { Box, Sphere, Capsule, Sector, Column }

    public readonly ShapeKind Kind;
    public readonly TraitLabGizmoCategory Category;
    public readonly string Label;
    public readonly Vector3 Center;      // 상자 중심·구 중심·부채꼴 꼭짓점·원기둥 바닥 중심·캡슐 끝 0
    public readonly Vector3 Extents;     // 상자 반 크기 / 부채꼴 정면 / 캡슐 끝 1
    public readonly Quaternion Rotation;
    public readonly float Radius;
    public readonly float Angle;         // 부채꼴 전체 각도
    public readonly float Height;        // 원기둥 높이

    TraitLabGizmoShape(ShapeKind kind, TraitLabGizmoCategory category, string label, Vector3 center, Vector3 extents,
        Quaternion rotation, float radius, float angle, float height)
    {
        Kind = kind;
        Category = category;
        Label = label;
        Center = center;
        Extents = extents;
        Rotation = rotation;
        Radius = radius;
        Angle = angle;
        Height = height;
    }

    public static TraitLabGizmoShape Box(TraitLabGizmoCategory c, string label, Vector3 center, Vector3 halfExtents, Quaternion rotation) =>
        new TraitLabGizmoShape(ShapeKind.Box, c, label, center, halfExtents, rotation, 0f, 0f, 0f);

    public static TraitLabGizmoShape Sphere(TraitLabGizmoCategory c, string label, Vector3 center, float radius) =>
        new TraitLabGizmoShape(ShapeKind.Sphere, c, label, center, Vector3.zero, Quaternion.identity, radius, 0f, 0f);

    public static TraitLabGizmoShape Capsule(TraitLabGizmoCategory c, string label, Vector3 point0, Vector3 point1, float radius) =>
        new TraitLabGizmoShape(ShapeKind.Capsule, c, label, point0, point1, Quaternion.identity, radius, 0f, 0f);

    public static TraitLabGizmoShape Sector(TraitLabGizmoCategory c, string label, Vector3 apex, Vector3 forward, float radius, float angle) =>
        new TraitLabGizmoShape(ShapeKind.Sector, c, label, apex, forward, Quaternion.identity, radius, angle, 0f);

    public static TraitLabGizmoShape Column(TraitLabGizmoCategory c, string label, Vector3 bottomCenter, float radius, float height) =>
        new TraitLabGizmoShape(ShapeKind.Column, c, label, bottomCenter, Vector3.zero, Quaternion.identity, radius, 0f, height);

    public static TraitLabGizmoShape FromVolume(TraitLabGizmoCategory c, string label, in SkillHitVolume volume) =>
        volume.IsCircle
            ? Column(c, label, volume.CircleCenter, volume.Radius, volume.HalfExtents.y * 2f)
            : Box(c, label, volume.Center, volume.HalfExtents, volume.Rotation);

    /// <summary>판정 앵커(ColliderInfo)의 지금 월드 형상 — 판정이 Overlap 에 넘기는 값과 같은 함수(Get*ColliderInfo). 형태가 없으면 false.</summary>
    public static bool TryFromAnchor(TraitLabGizmoCategory c, string label, ColliderInfo anchor, Vector3 offset, out TraitLabGizmoShape shape)
    {
        shape = default;
        if (anchor == null)
            return false;

        switch (anchor.OverlapCollider)
        {
            case OverlapCollider.Box:
                BoxColliderInfo box = default;
                anchor.GetBoxColliderInfo(ref box);
                shape = Box(c, label, box.center + offset, box.halfExtents, box.orientation);
                return true;
            case OverlapCollider.Sphere:
                SphereColliderInfo sphere = default;
                anchor.GetSphereColliderInfo(ref sphere);
                shape = Sphere(c, label, sphere.center + offset, sphere.radius);
                return true;
            case OverlapCollider.Capsule:
                CapsuleColliderInfo capsule = default;
                anchor.GetCapsuleColliderInfo(ref capsule);
                shape = Capsule(c, label, capsule.point0 + offset, capsule.point1 + offset, capsule.radius);
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// 플레이어 한 명의 공격 판정 형상을 모은다(할 일 152) — 지금 위치·바라보는 방향(<see cref="PlayerMovement.CurrentFacing"/>)에서 지금 쓰면 칠 영역.
/// 🔴 수치·형상은 판정 코드가 쓰는 공용 함수·값에서만 읽는다(프리팹 앵커·SO·<see cref="PlayerTraitModifiers"/>·파생 공격 명세). 여기서 복제하지 않는다.
/// 특성 파생 공격 명세(<see cref="PlayerTraitEffects.GetDerivedAttacks"/>)는 서버 전용이라 호스트 창에서만 나온다.
/// </summary>
public static class TraitLabAttackShapes
{
    static readonly List<TraitDerivedAttack> DerivedBuffer = new List<TraitDerivedAttack>();
    static readonly PaladinShieldDashPath ShieldDashPath = new PaladinShieldDashPath();
    static readonly PlayerSkillSlot[] Slots = { PlayerSkillSlot.Main, PlayerSkillSlot.Sub, PlayerSkillSlot.Ultimate, PlayerSkillSlot.Interrupt };

    public static void Collect(Player player, List<TraitLabGizmoShape> shapes)
    {
        if (player == null)
            return;

        Vector3 position = player.transform.position;
        Vector3 facing = player.TryGetComponent(out PlayerMovement movement) ? movement.CurrentFacing : Flat(player.transform.forward);
        player.TryGetComponent(out PlayerTraitEffects traitEffects);

        CollectBasic(player, position, facing, traitEffects, shapes);
        CollectSkills(player, position, facing, traitEffects, shapes);
        CollectTrackingLasers(player, shapes);
    }

    // ── 기본 공격 ──

    static void CollectBasic(Player player, Vector3 position, Vector3 facing, PlayerTraitEffects traitEffects, List<TraitLabGizmoShape> shapes)
    {
        const TraitLabGizmoCategory B = TraitLabGizmoCategory.Basic;

        // 가붕이 — 단계의 판정 도형(할 일 153 3단계). 도형마다 넓은 질의 상자(직사각형이면 판정 영역 그대로)를 임시로 그린다 — 6단계에서 AttackShapeDrawing 으로 통합.
        // 여러 단계가 같은 상자면 한 번만 그리고 라벨에 단계를 모은다.
        if (player.TryGetComponent(out DefaultAttackController melee) && melee.isActiveAndEnabled && melee.AttackSteps != null)
        {
            IReadOnlyList<DefaultAttackStep> steps = melee.AttackSteps;
            var boxes = new List<(Vector3 center, Vector3 half, Quaternion rotation)>();
            var labels = new List<string>();
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] == null || steps[i].HitType != DefaultAttackHitType.Overlap || steps[i].HitArea == null)
                    continue;

                AttackShapePose pose = PlayerDefaultAttack.ResolvePose(steps[i], position, facing);
                foreach (AttackShape shape in steps[i].HitArea.Shapes)
                {
                    if (shape == null)
                        continue;

                    shape.GetBroadBox(pose, out Vector3 center, out Vector3 half, out Quaternion rotation);
                    int index = boxes.FindIndex(b => (b.center - center).sqrMagnitude < 1e-6f && (b.half - half).sqrMagnitude < 1e-6f
                        && Quaternion.Angle(b.rotation, rotation) < 0.01f);
                    if (index < 0)
                    {
                        boxes.Add((center, half, rotation));
                        labels.Add($"평타 {i + 1}");
                    }
                    else if (!labels[index].EndsWith($"{i + 1}"))
                    {
                        labels[index] += $"·{i + 1}";
                    }
                }
            }

            for (int i = 0; i < boxes.Count; i++)
                shapes.Add(TraitLabGizmoShape.Box(B, labels[i] + "타", boxes[i].center, boxes[i].half, boxes[i].rotation));
        }

        // 어쌔신 — 그 타의 앵커 상자를 조준 방향·칼날의 영역 배율로 놓는 판정 함수 그대로.
        if (player.TryGetComponent(out AssassinBasicAttack assassin) && assassin.isActiveAndEnabled)
        {
            bool transformed = player.TryGetComponent(out AssassinState state) && state.IsTransformed;
            int normalCount = assassin.NormalStepCount;
            if (!transformed)
            {
                for (int i = 0; i < normalCount; i++)
                    AddStrikeBox(assassin, AssassinBasicAttackMode.Normal, i, facing, B, $"평타 {i + 1}타", shapes);
                AddStrikeBox(assassin, AssassinBasicAttackMode.Enhanced, 0, facing, B, "강타", shapes);
            }
            else
            {
                AddStrikeBox(assassin, AssassinBasicAttackMode.Transformed, 0, facing, B, "변신 묶음", shapes);
            }

            // 끝없는 칼날 — 일반 마지막 타·변신 묶음 마지막 판정과 같은 상자에 추가 베기.
            AssassinBasicAttackMode mode = transformed ? AssassinBasicAttackMode.Transformed : AssassinBasicAttackMode.Normal;
            if (traitEffects != null && HasDerived(traitEffects, AssassinBasicAttack.TraitKeyFor(mode), AssassinDerivedAttackKeys.ComboExtra))
                AddStrikeBox(assassin, mode, transformed ? 0 : normalCount - 1, facing, TraitLabGizmoCategory.Trait, "끝없는 칼날(추가 베기)", shapes);
        }

        // 거너 — 출발점에서 정면으로 Range 만큼, 반경 BeamWidth/2 SphereCast(벽에서 잘림).
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
                shapes.Add(TraitLabGizmoShape.Capsule(TraitLabGizmoCategory.Trait, "이중 출력(추가 발)", origin, origin + facing * length, radius));
        }
    }

    static void AddStrikeBox(AssassinBasicAttack attack, AssassinBasicAttackMode mode, int step, Vector3 facing,
        TraitLabGizmoCategory category, string label, List<TraitLabGizmoShape> shapes)
    {
        // 할 일 153 2단계 — 판정 도형(부채꼴)만 임시로 옮겨 그린다. 6단계에서 AttackShapeDrawing 으로 통합.
        if (!attack.TryGetHitArea(mode, step, facing, out AttackShapeSet area, out AttackShapePose pose))
            return;

        foreach (AttackShape shape in area.Shapes)
        {
            if (shape is AttackSectorShape sector)
            {
                AttackShapePose shapePose = sector.ShapePose(pose);
                shapes.Add(TraitLabGizmoShape.Sector(category, label, shapePose.Position, shapePose.Forward, sector.Range, sector.Angle));
            }
        }
    }

    // ── 스킬 ──

    static void CollectSkills(Player player, Vector3 position, Vector3 facing, PlayerTraitEffects traitEffects, List<TraitLabGizmoShape> shapes)
    {
        if (!player.TryGetComponent(out PlayerSkillController controller))
            return;

        const TraitLabGizmoCategory S = TraitLabGizmoCategory.Skill;
        const TraitLabGizmoCategory T = TraitLabGizmoCategory.Trait;

        for (int s = 0; s < Slots.Length; s++)
        {
            PlayerSkillSlot slot = Slots[s];
            PlayerSkillBase skill = controller.GetSkill(slot);
            if (skill == null)
                continue;

            string slotName = SlotName(slot);
            TraitAttackKey key = TraitAttackKey.ForSkill(slot, controller.IsAlternateSkill(skill));

            // 앵커 판정(가붕이 Q·간파 3종 등) — OverlapHitboxAnchor 와 같은 앵커 월드 형상.
            if (TraitLabGizmoShape.TryFromAnchor(S, $"{slotName} 판정", skill.HitboxAnchor, Vector3.zero, out TraitLabGizmoShape anchorShape))
                shapes.Add(anchorShape);

            switch (skill)
            {
                case FirstMeleeMainSkill paladinQ:
                    // 돌파의 방패 — 같은 앵커를 돌진 경로 끝까지 옮겨 훑는다(경로 = 판정과 같은 PaladinShieldDashPath).
                    PaladinShieldDashSettings dash = paladinQ.ResolveShieldDash(out bool hasDashTrait);
                    if (PaladinTraitRules.UsesShieldDash(hasDashTrait, dash))
                    {
                        ShieldDashPath.Begin(position, facing, dash);
                        Vector3 offset = ShieldDashPath.PointAt(ShieldDashPath.Cap) - position;
                        offset.y = 0f;
                        ShieldDashPath.End();
                        if (TraitLabGizmoShape.TryFromAnchor(T, $"돌파의 방패 끝 {dash.Distance:0.#}m", skill.HitboxAnchor, offset, out TraitLabGizmoShape dashEnd))
                            shapes.Add(dashEnd);
                    }

                    // 진격의 충격 — Q 가 여기서 끝났다면(기준 = 끝 지점 + 정면). 부채꼴 도형만 임시 표시(6단계에서 AttackShapeDrawing 으로 통합).
                    if (traitEffects != null)
                    {
                        traitEffects.GetDerivedAttacks(key, DerivedBuffer);
                        for (int i = 0; i < DerivedBuffer.Count; i++)
                        {
                            TraitDerivedAttack blast = DerivedBuffer[i];
                            if (blast.Key != PaladinTraitValues.QBlastKey)
                                continue;

                            AttackShapePose blastPose = PaladinTraitRules.QBlastPose(blast.HitArea, position, facing);
                            foreach (AttackShape shape in blast.HitArea.Shapes)
                            {
                                if (shape is not AttackSectorShape sector)
                                    continue;
                                AttackShapePose shapePose = sector.ShapePose(blastPose);
                                shapes.Add(TraitLabGizmoShape.Sector(T, $"진격의 충격 r{sector.Range:0.#} {sector.Angle:0}°", shapePose.Position, shapePose.Forward, sector.Range, sector.Angle));
                            }
                        }
                    }
                    break;

                case FirstMeleeSubSkill paladinE when skill.Data is FirstMeleeSubSkillData eData:
                    shapes.Add(TraitLabGizmoShape.Sector(S, $"E 반격 r{eData.CounterRange:0.#} {eData.CounterAngle:0}°", position, facing, eData.CounterRange, eData.CounterAngle));
                    float pushRadius = paladinE.TraitPushRadius();
                    if (pushRadius > 0f)
                        shapes.Add(TraitLabGizmoShape.Sector(T, $"수호의 파동 r{pushRadius:0.#}", position, facing, pushRadius, 360f));
                    break;

                case AssassinShadowReturnSkill:
                    // 그림자 귀환(후속 Q)은 돌아가는 방향·거리를 따로 정한다 — 돌진 경로로 그리지 않는다.
                    break;

                case AssassinDashStrikeSkill dashStrike when dashStrike.DashData != null:
                    AssassinDashStrikeSkillData qData = dashStrike.DashData;
                    AssassinDashStrikeSkill.PathBox(position, facing, 0f, qData.DashDistance, qData,
                        out Vector3 pathCenter, out Vector3 pathHalf, out Quaternion pathRotation);
                    shapes.Add(TraitLabGizmoShape.Box(S, $"{slotName} 돌진 경로 {qData.DashDistance:0.#}m", pathCenter, pathHalf, pathRotation));
                    if (traitEffects != null && HasDerived(traitEffects, key, AssassinDerivedAttackKeys.QTrail))
                        shapes.Add(TraitLabGizmoShape.Box(T, "추격의 궤적(지나온 길)", pathCenter, pathHalf, pathRotation));
                    break;

                case AssassinCircleStrikeSkill circle when skill.Data is AssassinCircleStrikeSkillData cData:
                    if (circle.TryGetGroundHitVolume(position, position + facing * cData.CenterDistance, out SkillHitVolume circleVolume))
                        shapes.Add(TraitLabGizmoShape.FromVolume(S, $"{slotName} 원형 5타 r{circleVolume.Radius:0.##}", circleVolume));
                    break;

                case GunnerChargeLaserSkill laser when skill.Data is GunnerChargeLaserData lData:
                    Vector3 laserOrigin = GunnerChargeLaserSkill.FireOrigin(position, lData);
                    float width = laser.EffectiveBeamWidth(lData);
                    player.TryGetComponent(out GunnerBeamAttack laserBeam);
                    float maxLength = ClippedLength(laserBeam, laserOrigin, facing, lData.RangeAt(1f), lData);
                    float minLength = ClippedLength(laserBeam, laserOrigin, facing, lData.RangeAt(0f), lData);
                    SkillHitVolume maxVolume = GunnerChargeLaserSkill.FireVolume(laserOrigin, facing, maxLength, width, lData);
                    shapes.Add(TraitLabGizmoShape.FromVolume(S, $"{slotName} 최대 충전 {maxLength:0.#}m 폭 {width:0.##}", maxVolume));
                    shapes.Add(TraitLabGizmoShape.FromVolume(S, $"{slotName} 최소 충전 {minLength:0.#}m",
                        GunnerChargeLaserSkill.FireVolume(laserOrigin, facing, minLength, width, lData)));
                    if (traitEffects != null &&
                        GunnerTraitRules.FindDerived(traitEffects, GunnerTraitRules.QAttackKey, GunnerTraitRules.QEchoDerivedKey, DerivedBuffer) != null)
                        shapes.Add(TraitLabGizmoShape.FromVolume(T, "잔류 포격(첫 발 경로 재판정)", maxVolume));
                    break;
            }
        }
    }

    // 거너 R — 떠 있는 추적 레이저마다 실제 피해 구(서버 값). 호스트 창에서만.
    static void CollectTrackingLasers(Player player, List<TraitLabGizmoShape> shapes)
    {
        GunnerTrackingLaser[] lasers = Object.FindObjectsByType<GunnerTrackingLaser>(FindObjectsSortMode.None);
        for (int i = 0; i < lasers.Length; i++)
        {
            if (lasers[i] != null && lasers[i].ServerCaster == player)
                shapes.Add(TraitLabGizmoShape.Sphere(TraitLabGizmoCategory.Skill, $"R 추적 레이저 r{lasers[i].ServerDamageRadius:0.#}",
                    lasers[i].transform.position, lasers[i].ServerDamageRadius));
        }
    }

    // 판정과 같은 지형 차단 길이(데이터 폭으로 잰다 — 광폭 레이저가 사거리를 바꾸지 않게).
    static float ClippedLength(GunnerBeamAttack beam, Vector3 origin, Vector3 direction, float range, GunnerChargeLaserData data) =>
        beam != null ? beam.CastLength(origin, direction, range, data.BeamWidth * 0.5f, data.BlockingLayers) : range;

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

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }
}

/// <summary>
/// 특성 랩 공격 범위 기즈모 그리기(할 일 152) — <see cref="DebugTraitLab"/> 의 OnDrawGizmos 가 부른다. 씬의 모든 플레이어(호스트·클라 무관)를 매번 다시 모아
/// 획득 특성(배율·각도·반경)이 바뀌면 바로 반영된다. Gizmos 라 Game 뷰는 Gizmos 버튼이 켜져 있어야 보인다.
/// </summary>
public sealed class TraitLabAttackGizmos
{
    static readonly Color BasicColor = new Color(0.25f, 0.85f, 1f);
    static readonly Color SkillColor = new Color(1f, 0.62f, 0.15f);
    static readonly Color TraitColor = new Color(1f, 0.3f, 0.85f);
    const float GroundLift = 0.05f;
    const int ArcSegments = 32;

    public bool Enabled;
    public bool ShowBasic = true;
    public bool ShowSkill = true;
    public bool ShowTrait = true;

    readonly List<TraitLabGizmoShape> shapes = new List<TraitLabGizmoShape>();
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
    }

    void DrawShape(in TraitLabGizmoShape shape)
    {
        Color color = ColorOf(shape.Category);
        Gizmos.color = color;
        Gizmos.matrix = Matrix4x4.identity;
        Vector3 labelAt;

        switch (shape.Kind)
        {
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

            case TraitLabGizmoShape.ShapeKind.Capsule:
                DrawCapsule(shape.Center, shape.Extents, shape.Radius);
                labelAt = shape.Extents + Vector3.up * shape.Radius;
                break;

            case TraitLabGizmoShape.ShapeKind.Sector:
                DrawSector(shape.Center + Vector3.up * GroundLift, shape.Extents, shape.Radius, shape.Angle);
                labelAt = shape.Center + Flat(shape.Extents) * shape.Radius + Vector3.up * GroundLift;
                break;

            default: // Column
                DrawSector(shape.Center + Vector3.up * GroundLift, Vector3.forward, shape.Radius, 360f);
                DrawSector(shape.Center + Vector3.up * shape.Height, Vector3.forward, shape.Radius, 360f);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 rim = shape.Center + Quaternion.Euler(0f, 90f * i, 0f) * Vector3.forward * shape.Radius;
                    Gizmos.DrawLine(rim, rim + Vector3.up * shape.Height);
                }
                labelAt = shape.Center + Vector3.up * shape.Height;
                break;
        }

        _labelStyle ??= new GUIStyle(EditorStyles.boldLabel);
        _labelStyle.normal.textColor = color;
        Handles.Label(labelAt, shape.Label, _labelStyle);
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

    // 바닥 평면 부채꼴(전체 각도). 360 이상 = 원(변 없음).
    static void DrawSector(Vector3 apex, Vector3 forward, float radius, float angle)
    {
        forward = Flat(forward);
        float full = Mathf.Clamp(angle, 0f, 360f);
        bool circle = full >= 360f;
        float half = full * 0.5f;
        Vector3 previous = apex + Quaternion.Euler(0f, -half, 0f) * forward * radius;
        if (!circle)
            Gizmos.DrawLine(apex, previous);

        for (int i = 1; i <= ArcSegments; i++)
        {
            float yaw = -half + full * i / ArcSegments;
            Vector3 point = apex + Quaternion.Euler(0f, yaw, 0f) * forward * radius;
            Gizmos.DrawLine(previous, point);
            previous = point;
        }

        if (!circle)
            Gizmos.DrawLine(apex, previous);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }
}
#endif
