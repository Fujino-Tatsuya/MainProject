using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 스킬 조준 모드(오너 전용, 순수 로컬). 타겟팅 스킬키 입력 시 PlayerSkillController가 Begin으로 진입시킨다.
///
/// 흐름(SingleTarget):
///  1) 스킬키 → 조준 대기 모드: 사거리 인디케이터(GameObject) 활성화 + 매 프레임 마우스 상태 갱신
///  2) 좌클릭 → 타겟 포착 여부와 무관하게 조준 대기 모드 탈출(인디케이터 비활성화)
///     - 사거리 내 유효 타겟 → 즉시 시전
///     - 사거리 밖 유효 타겟 → 사거리 안에 들 때까지 자동 이동 후 시전(범위 진입 시 자동 이동 해제)
///     - 유효 타겟 없음 → 아무것도 안 함(취소)
///  3) Esc/스킬키 재입력 → 취소
///
/// FSM에는 진입하지 않는다 — 실제 시전 승인 시에만 Skill 상태로 들어간다. 조준/자동이동 동안 공격·다른 스킬 입력은
/// 억제하되(IsInterceptingInput), 실제 시전은 controller.ExecuteTargetedSkill이 위임받아 서버가 CanUse로 재검증한다.
/// </summary>
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerAimIndicator))]
public class PlayerSkillTargeting : MonoBehaviour
{
    private const float RaycastMaxDistance = 200f;
    // 자동 이동 시 사거리 경계보다 살짝 안쪽에서 시전(서버 위치 오차로 인한 CanUse 거부 방지).
    private const float RangeBuffer = 0.3f;

    [SerializeField] private SkillRangeIndicator rangeIndicator;
    [SerializeField] private SkillCursorView cursorView;

    private Player owner;
    private PlayerSkillController controller;
    private PlayerAimIndicator aimIndicator;
    private PlayerMovement movement;
    private PlayerInputReader input;

    private bool isTargeting;      // 조준 대기 모드(인디케이터 표시)
    private bool isMovingToCast;   // 사거리 확보 자동 이동 중
    private bool beganThisFrame;
    private bool justHandledConfirm;

    private PlayerSkillSlot currentSlot;
    private PlayerSkillData currentData;
    private SkillCursorState cursorState = SkillCursorState.Default;

    // 조준 미리보기 결과
    private Unit hoveredTarget;         // 커서 아래 유효 적(사거리 무관), 없으면 null
    private bool hoveredInRange;
    private Vector3 candidateGroundPoint;
    private bool hasCandidateGroundPoint;

    // 자동 이동 대기 시전 정보
    private Unit pendingTarget;
    private PlayerSkillSlot pendingSlot;
    private float pendingCastRange;

    public bool IsTargeting => isTargeting;
    // FSM이 조준 대기/확정 직후 프레임 동안만 일반 액션 입력을 억제한다.
    // 자동 이동(isMovingToCast)은 억제하지 않는다 — 다른 입력이 들어오면 그 행동이 수행되면서 자동 이동은 취소된다.
    public bool IsInterceptingInput => isTargeting || justHandledConfirm;

    private void Awake()
    {
        owner = GetComponent<Player>();
        controller = GetComponent<PlayerSkillController>();
        aimIndicator = GetComponent<PlayerAimIndicator>();
        movement = GetComponent<PlayerMovement>();
        input = GetComponent<PlayerInputReader>();

        // 인디케이터는 기본 비활성 — 조준 진입 시에만 켠다
        if (rangeIndicator != null)
            rangeIndicator.gameObject.SetActive(false);
    }

    /// <summary>조준 모드 진입. PlayerSkillController가 타겟팅 스킬 입력 시 호출한다.</summary>
    public bool Begin(PlayerSkillSlot slot)
    {
        if (isTargeting || isMovingToCast)
            return false;

        PlayerSkillBase skill = controller != null ? controller.GetSkill(slot) : null;
        if (skill == null || skill.Data == null || skill.Data.TargetingMode == SkillTargetingMode.None)
            return false;

        // 현재 ClickToConfirm만 구현 — 다른 확정 방식은 진입 안 함(추후 확장)
        if (skill.Data.ConfirmMode != SkillConfirmMode.ClickToConfirm)
            return false;

        isTargeting = true;
        beganThisFrame = true;
        currentSlot = slot;
        currentData = skill.Data;
        hoveredTarget = null;
        hoveredInRange = false;
        hasCandidateGroundPoint = false;

        if (rangeIndicator != null)
        {
            rangeIndicator.gameObject.SetActive(true);
            rangeIndicator.ShowRange(currentData.CastRange);
        }

        SetCursorState(SkillCursorState.Targeting);
        return true;
    }

    public void Cancel()
    {
        ExitStandby();
        StopMoveToCast();
    }

    private void Update()
    {
        justHandledConfirm = false;

        // 오너/오프라인(로컬 조작자)만 조준 UI를 돌린다. 권위 상실 시 안전 종료.
        if (owner == null || !owner.IsMovementAuthority)
        {
            if (isTargeting || isMovingToCast)
                Cancel();
            return;
        }

        // 사망 시 전부 취소
        if (owner.CurrentState == PlayerActionState.Dead)
        {
            if (isTargeting || isMovingToCast)
                Cancel();
            return;
        }

        if (isMovingToCast)
        {
            TickMoveToCast();
            return;
        }

        if (!isTargeting)
            return;

        // 진입한 프레임의 입력(스킬키 press 등)이 취소/확정으로 오인되지 않도록 한 프레임 건너뛴다
        if (beganThisFrame)
        {
            beganThisFrame = false;
            return;
        }

        if (WasCancelPressed())
        {
            // 취소 유발 입력(우클릭 등)이 같은 프레임에 FSM 액션(단죄의 방패 등)으로 새지 않도록 1프레임 억제
            justHandledConfirm = true;
            ExitStandby();
            return;
        }

        UpdatePreview();

        if (WasConfirmPressed())
            HandleConfirm();
    }

    private bool WasCancelPressed()
    {
        // Esc / 우클릭 / 같은 스킬키 재입력으로 조준 취소.
        // 조준 중엔 FSM이 우클릭(단죄의 방패)을 억제하므로 우클릭을 취소로 재활용한다.
        bool escPressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool rightClick = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        return escPressed || rightClick || (controller != null && controller.WasSkillRePressed(currentSlot));
    }

    private bool WasConfirmPressed()
    {
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    private void UpdatePreview()
    {
        switch (currentData.TargetingMode)
        {
            case SkillTargetingMode.SingleTarget:
                UpdateSingleTargetPreview();
                break;
            case SkillTargetingMode.GroundPoint:
                UpdateGroundPointPreview();
                break;
        }
    }

    private void UpdateSingleTargetPreview()
    {
        hoveredTarget = null;
        hoveredInRange = false;

        Camera cam = aimIndicator != null ? aimIndicator.TargetCamera : null;
        if (cam == null || Mouse.current == null)
        {
            SetCursorState(SkillCursorState.Targeting);
            return;
        }

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, RaycastMaxDistance,
                currentData.TargetableLayers, QueryTriggerInteraction.Collide))
        {
            SetCursorState(SkillCursorState.Targeting);
            return;
        }

        Unit unit = ResolveUnit(hit.collider);
        if (unit == null || unit == owner || unit.CurrentHealth <= 0)
        {
            SetCursorState(SkillCursorState.InvalidTarget);
            return;
        }

        // 사거리 무관하게 포착 — 사거리 밖이면 확정 시 자동 이동으로 접근한다
        hoveredTarget = unit;
        hoveredInRange = IsWithinRange(unit.transform.position, currentData.CastRange);
        SetHighlightedTarget(unit); // 아웃라인은 보류 — 현재 훅만 (no-op)
        SetCursorState(hoveredInRange ? SkillCursorState.ValidTarget : SkillCursorState.OutOfRange);
    }

    private void UpdateGroundPointPreview()
    {
        hoveredTarget = null;
        hasCandidateGroundPoint = false;

        if (aimIndicator == null || !aimIndicator.HasAimGroundPoint)
        {
            if (rangeIndicator != null)
                rangeIndicator.SetGroundMarker(false, Vector3.zero);
            SetCursorState(SkillCursorState.Targeting);
            return;
        }

        Vector3 point = aimIndicator.AimGroundPoint;
        bool inRange = IsWithinRange(point, currentData.CastRange);

        // 사거리 밖이면 최대 사거리로 클램프 — GroundPoint는 항상 시전 가능(경계에 스냅)
        if (!inRange)
            point = ClampToRange(point, currentData.CastRange);

        candidateGroundPoint = point;
        hasCandidateGroundPoint = true;

        if (rangeIndicator != null)
            rangeIndicator.SetGroundMarker(true, point);

        SetCursorState(inRange ? SkillCursorState.ValidTarget : SkillCursorState.OutOfRange);
    }

    // 좌클릭 확정 — 타겟 포착 여부와 무관하게 조준 대기 모드는 탈출한다.
    private void HandleConfirm()
    {
        justHandledConfirm = true;

        if (currentData.TargetingMode == SkillTargetingMode.GroundPoint)
        {
            bool hasPoint = hasCandidateGroundPoint;
            Vector3 point = candidateGroundPoint;
            PlayerSkillSlot slot = currentSlot;
            ExitStandby();
            if (hasPoint && controller != null)
                controller.ExecuteTargetedSkill(slot, null, point, true);
            return;
        }

        // SingleTarget
        Unit target = hoveredTarget;
        bool inRange = hoveredInRange;
        PlayerSkillSlot targetSlot = currentSlot;
        float range = currentData.CastRange;

        ExitStandby();

        if (target == null)
            return; // 빈 곳/비적 클릭 → 취소만

        if (inRange)
        {
            controller?.ExecuteTargetedSkill(targetSlot, target, Vector3.zero, false);
        }
        else
        {
            BeginMoveToCast(target, targetSlot, range);
        }
    }

    // ── 자동 이동(사거리 확보) ──

    private void BeginMoveToCast(Unit target, PlayerSkillSlot slot, float castRange)
    {
        pendingTarget = target;
        pendingSlot = slot;
        pendingCastRange = castRange;
        isMovingToCast = true;
    }

    private void TickMoveToCast()
    {
        if (pendingTarget == null || pendingTarget.CurrentHealth <= 0)
        {
            StopMoveToCast();
            return;
        }

        // 다른 입력(이동/공격/다른 스킬/Esc)이 들어오면 자동 이동을 취소하고 조작권을 넘긴다.
        // 입력은 억제하지 않으므로 그 공격/스킬은 FSM이 이 프레임에 그대로 수행한다.
        if (ShouldCancelAutoMove())
        {
            StopMoveToCast();
            return;
        }

        Vector3 targetPos = pendingTarget.transform.position;

        if (IsWithinRange(targetPos, pendingCastRange - RangeBuffer))
        {
            PlayerSkillSlot slot = pendingSlot;
            Unit target = pendingTarget;
            StopMoveToCast();
            controller?.ExecuteTargetedSkill(slot, target, Vector3.zero, false);
            return;
        }

        if (movement != null)
            movement.MoveTowardsPoint(targetPos);

        if (owner != null)
            owner.SetAnimatorMoving(true);
    }

    // 자동 이동 중 "다른 입력"이 들어왔는지. 대기 중인 스킬키 재입력은 제외(같은 스킬은 '다른 입력'이 아님).
    private bool ShouldCancelAutoMove()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            return true;

        if (input == null)
            return false;

        if (input.HasMoveInput)
            return true;

        if (input.AttackPressed)
            return true;

        for (int i = 0; i < 4; i++)
        {
            PlayerSkillSlot slot = (PlayerSkillSlot)i;
            if (slot != pendingSlot && input.GetSkillPressed(slot))
                return true;
        }

        return false;
    }

    private void StopMoveToCast()
    {
        if (!isMovingToCast)
            return;

        isMovingToCast = false;
        pendingTarget = null;

        if (owner != null)
            owner.SetAnimatorMoving(false);
    }

    // ── 종료/정리 ──

    private void ExitStandby()
    {
        isTargeting = false;
        beganThisFrame = false;
        hoveredTarget = null;
        hasCandidateGroundPoint = false;

        if (rangeIndicator != null)
        {
            rangeIndicator.HideAll();
            rangeIndicator.gameObject.SetActive(false);
        }

        SetCursorState(SkillCursorState.Default);
    }

    // 사거리는 수평 거리 기준 (y 무시)
    private bool IsWithinRange(Vector3 worldPoint, float range)
    {
        if (range <= 0f)
            return false;

        Vector3 flat = worldPoint - owner.transform.position;
        flat.y = 0f;
        return flat.sqrMagnitude <= range * range;
    }

    private Vector3 ClampToRange(Vector3 worldPoint, float range)
    {
        Vector3 origin = owner.transform.position;
        Vector3 flat = worldPoint - origin;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f)
            return worldPoint;

        Vector3 clamped = origin + flat.normalized * range;
        clamped.y = worldPoint.y;
        return clamped;
    }

    private void SetCursorState(SkillCursorState state)
    {
        if (cursorState == state)
            return;

        cursorState = state;
        if (cursorView != null)
            cursorView.ApplyState(state);
    }

    // 타겟 강조(아웃라인 색 변경)는 보류 — 아웃라인 구현 확인 후 여기 배선한다.
    private void SetHighlightedTarget(Unit target)
    {
        // TODO(아웃라인 보류): target에 아웃라인 색을 적용하는 훅.
    }

    // 레이캐스트 히트 → Unit 해석 (Hurtbox 우선, 없으면 상위 Unit). FirstMeleeMainSkill.ResolveUnit 패턴과 동일.
    private static Unit ResolveUnit(Collider hit)
    {
        if (hit == null)
            return null;

        Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
        if (hurtbox != null && hurtbox.TryGetOwner(out Unit hurtboxOwner))
            return hurtboxOwner;

        return hit.GetComponentInParent<Unit>();
    }
}
