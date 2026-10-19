using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 할 일 47 — 게임 Animator 를 읽어 납품 머리카락 시스템(v13 물리 + v16 동작별 보정 + v15 회피)을 한 프레임 순서대로 구동한다.
/// 납품 연결 순서를 그대로 따른다: 원래 자세에서 Capture 1회 → 매 프레임 RestoreBasePose(Update) → Animator 평가·SDArmTwist
/// → v13 Advance 1회 → 지금 클립에 맞는 ApplyPose → 팔 밀어내기 후처리(<see cref="AssassinHairArmClearance"/>, LateUpdate).
/// v13 자동 LateUpdate 는 externalDrive 로 끈다(한 프레임 두 번 계산 금지).
/// 순수 표현이다 — 각 피어가 자기 Animator(NetworkAnimator 복제) 결과로 따로 계산하고, 네트워크로 아무것도 보내지 않는다.
/// 판단 규칙은 <see cref="AssassinHairClipRules"/>·<see cref="AssassinHairClipTracker"/>.
/// </summary>
[DefaultExecutionOrder(21000)] // SDArmTwist(20000)·Animator 뒤, v13 과 같은 순서대 — v13 을 직접 구동한다.
[DisallowMultipleComponent]
[RequireComponent(typeof(AssassinHairMotion))]
public sealed class AssassinHairDriver : MonoBehaviour
{
    private const string Tag = "[AssassinHair]";
    private const int BaseLayer = 0;

    [Tooltip("v16 동작별 보정(달리기·평타·Q 등). 끄면 그 클립들은 v13 물리만 쓴다.")]
    [SerializeField] private bool actionHairEnabled = true;

    [Tooltip("v15 회피(Dodge_Combat_F_0) 전용 머리. 끄면 회피도 v13 물리만 쓴다.")]
    [SerializeField] private bool diveHairEnabled = true;

    [Tooltip("v13·보정이 끝난 뒤 팔 캡슐(좌우 상완·전완)에 닿는 가닥만 회전으로 밀어낸다. 끄면 팔꿈치가 머리카락 밖으로 나올 수 있다.")]
    [SerializeField] private bool armClearanceEnabled = true;

    [Tooltip("팔 밀어내기가 마디 하나를 더 꺾는 최대 각도(도). 크면 팔은 덜 비치지만 가닥이 팔 위에서 갈라지듯 꺾인다.")]
    [SerializeField, Range(0f, 90f)] private float armClearanceMaxDegrees = 45f;

    private readonly AssassinHairClipTracker tracker = new();
    private readonly List<AnimatorClipInfo> clipInfos = new();

    private AssassinHairMotion hair;
    private Animator animator;
    private AssassinActionHairAnimation actionHair;
    private AssassinDiveHairAnimation diveHair;
    private bool overlaysReady;
    private Vector3 trailingDirection;
    private AnimationClip cachedClip;
    private string cachedClipName;
    private AssassinHairArmClearance.Buffers armClearance;

    private void Awake()
    {
        hair = GetComponent<AssassinHairMotion>();
        animator = GetComponent<Animator>();
        actionHair = GetComponent<AssassinActionHairAnimation>();
        diveHair = GetComponent<AssassinDiveHairAnimation>();
        hair.externalDrive = true;
        armClearance = AssassinHairArmClearance.Prepare(hair);

        // Awake 는 첫 Animator 평가 전이다 — 보정의 좌우 배치(fan)를 원래 자세에서 기록한다.
        try
        {
            if (actionHair == null || diveHair == null)
                throw new InvalidOperationException("AssassinActionHairAnimation·AssassinDiveHairAnimation 이 같은 오브젝트에 있어야 한다.");

            actionHair.Capture(hair, transform);
            diveHair.Capture(hair, transform);
            overlaysReady = true;
        }
        catch (Exception error)
        {
            overlaysReady = false;
            Debug.LogWarning($"{Tag} 동작별 머리 보정 캡처 실패 — 보정 없이 v13 물리만 쓴다: {error.Message}", this);
        }
    }

    private void OnEnable()
    {
        if (hair != null)
            hair.externalDrive = true;
        tracker.Invalidate();
    }

    private void OnDisable()
    {
        ClearOverlays();
        // 구동기만 꺼지면 v13 이 자기 LateUpdate 로 혼자 돈다.
        if (hair != null)
            hair.externalDrive = false;
    }

    private void Update()
    {
        if (!overlaysReady)
            return;

        // 지난 프레임 덧칠을 걷어 v13 결과로 되돌린다(Animator 평가·Advance 전).
        actionHair.RestoreBasePose();
        diveHair.RestoreBasePose();
    }

    private void LateUpdate()
    {
        if (!hair.isActiveAndEnabled)
            return;

        hair.Advance(Time.deltaTime);
        ApplyOverlays();

        // 마지막 덧쓰기 — 다음 프레임 v13 Advance 가 바인드 회전부터 다시 쓰므로 시뮬레이션 상태에 들어가지 않는다.
        if (armClearanceEnabled && hair.motionEnabled && hair.weight > 0f)
            AssassinHairArmClearance.Apply(hair, armClearance, armClearanceMaxDegrees);
    }

    private void ApplyOverlays()
    {
        if (!overlaysReady)
            return;

        AnimationClip clip = CurrentBaseClip(out AnimatorStateInfo state);
        if (clip == null || !hair.motionEnabled || hair.weight <= 0f)
        {
            ClearOverlays();
            tracker.Invalidate();
            return;
        }

        if (clip != cachedClip)
        {
            cachedClip = clip;
            cachedClipName = clip.name;
        }

        if (tracker.Observe(state.fullPathHash, cachedClipName, state.normalizedTime, clip.isLooping, hair.resetCount))
        {
            ClearOverlays();
            trailingDirection = Vector3.ProjectOnPlane(-(transform.rotation * Vector3.forward), Vector3.up);
        }

        float clipTime = AssassinHairClipRules.ClipTime(state.normalizedTime, clip.length, clip.isLooping);
        switch (AssassinHairClipRules.Select(cachedClipName))
        {
            case AssassinHairOverlayKind.Dive when diveHairEnabled:
                actionHair.ResetPose();
                diveHair.ApplyPose(clipTime, trailingDirection);
                break;
            case AssassinHairOverlayKind.Action when actionHairEnabled:
                diveHair.ResetPose();
                actionHair.ApplyPose(cachedClipName, clipTime, clip.length, trailingDirection);
                break;
            default:
                ClearOverlays();
                break;
        }
    }

    /// <summary>Base Layer 현재 상태의 가장 무거운 클립. 상·하체가 섞여도 UpperBody 레이어는 보지 않는다(사용자 결정).</summary>
    private AnimationClip CurrentBaseClip(out AnimatorStateInfo state)
    {
        state = default;
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            return null;

        state = animator.GetCurrentAnimatorStateInfo(BaseLayer);
        animator.GetCurrentAnimatorClipInfo(BaseLayer, clipInfos);
        AnimationClip best = null;
        float bestWeight = 0f;
        foreach (AnimatorClipInfo info in clipInfos)
        {
            if (info.clip != null && info.weight > bestWeight)
            {
                best = info.clip;
                bestWeight = info.weight;
            }
        }
        return best;
    }

    private void ClearOverlays()
    {
        if (!overlaysReady)
            return;

        actionHair.ResetPose();
        diveHair.ResetPose();
    }
}
