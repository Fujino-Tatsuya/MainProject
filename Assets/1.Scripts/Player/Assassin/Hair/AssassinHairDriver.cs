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

    [Tooltip("달리기(Start·Loop·Stop) 보정이 몸 방향을 따라가는 지연(초). 0 이면 몸과 똑같이 돈다. 크면 방향을 틀 때 머리카락이 늦게 따라온다.")]
    [SerializeField, Range(0f, 0.5f)] private float runFacingFollowSeconds = 0.08f;

    [Tooltip("보정 모양이 바뀌는 순간(Stop→Start·공격 취소→달리기·회피 앞뒤 등) 직전 모양에서 새 모양으로 넘어가는 시간(초). 0 이면 바로 바뀐다(그 순간 머리카락이 튈 수 있다).")]
    [SerializeField, Range(0f, 0.5f)] private float overlayBlendSeconds = 0.2f;

    private readonly AssassinHairClipTracker tracker = new();
    private readonly List<AnimatorClipInfo> clipInfos = new();

    private AssassinHairMotion hair;
    private Animator animator;
    private AssassinActionHairAnimation actionHair;
    private AssassinDiveHairAnimation diveHair;
    private bool overlaysReady;
    private Vector3 trailingDirection;
    private bool wasFollowingFacing;
    private AnimationClip cachedClip;
    private string cachedClipName;
    private AssassinHairArmClearance.Buffers armClearance;

    // 보정 전환 이어주기(관성 전환) — 마디마다 월드 회전. base = v13 결과, overlay = 보정 결과, applied = 실제로 얹은 보정 회전.
    private Quaternion[] baseWorld, overlayWorld, appliedDelta, carryOffset;
    private float carryElapsed = float.PositiveInfinity;
    private bool carryReady;

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

            int boneCount = 0;
            foreach (AssassinHairMotion.Chain chain in hair.chains)
                boneCount += chain.bones.Length;
            baseWorld = new Quaternion[boneCount];
            overlayWorld = new Quaternion[boneCount];
            appliedDelta = new Quaternion[boneCount];
            carryOffset = new Quaternion[boneCount];
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
        wasFollowingFacing = false;
        carryReady = false;
        carryElapsed = float.PositiveInfinity;
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
        if (overlaysReady)
            ReadHairRotations(baseWorld);
        ApplyOverlays(Time.deltaTime, out bool evaluated, out bool overlayChanged, out bool restarted);
        CarryOverlayChange(evaluated, overlayChanged, restarted, Time.deltaTime);

        // 마지막 덧쓰기 — 다음 프레임 v13 Advance 가 바인드 회전부터 다시 쓰므로 시뮬레이션 상태에 들어가지 않는다.
        if (armClearanceEnabled && hair.motionEnabled && hair.weight > 0f)
            AssassinHairArmClearance.Apply(hair, armClearance, armClearanceMaxDegrees);
    }

    private void ApplyOverlays(float deltaTime, out bool evaluated, out bool overlayChanged, out bool restarted)
    {
        evaluated = overlayChanged = restarted = false;
        if (!overlaysReady)
            return;

        AnimationClip clip = CurrentBaseClip(out AnimatorStateInfo state);
        if (clip == null || !hair.motionEnabled || hair.weight <= 0f)
        {
            ClearOverlays();
            tracker.Invalidate();
            wasFollowingFacing = false;
            return;
        }

        if (clip != cachedClip)
        {
            cachedClip = clip;
            cachedClipName = clip.name;
        }

        bool relaunch = tracker.Observe(state.fullPathHash, cachedClipName, state.normalizedTime, clip.isLooping, hair.resetCount);
        if (relaunch)
            ClearOverlays();
        evaluated = true;
        restarted = tracker.Restarted;
        // 같은 클립 루프 한 바퀴는 모양이 이어지고, 리셋은 v13 자체가 새로 시작하므로 이어주지 않는다.
        overlayChanged = relaunch && !tracker.LoopOnly && !tracker.Restarted;

        // 달리기 계열은 루프 한 바퀴·Start→Loop→Stop 에서 다시 잡지 않고 몸 방향을 따라간다(루프 경계 튐 방지).
        bool followingFacing = AssassinHairClipRules.FollowsFacing(cachedClipName);
        trailingDirection = AssassinHairClipRules.NextTrailing(trailingDirection, -(transform.rotation * Vector3.forward),
            relaunch, tracker.Restarted, followingFacing, wasFollowingFacing, deltaTime, runFacingFollowSeconds);
        wasFollowingFacing = followingFacing;

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

    /// <summary>
    /// 10-22 — 보정 모양이 바뀌는 순간의 튐을 없앤다(관성 전환). 클립이 바뀌면 보정 모양이 한 프레임에 바뀐다
    /// (예: Stop→Start 재진입 때 달리기 보정 세기 0.72 → 0, 공격 취소→Run_Loop 때 0 → 0.76).
    /// 전환 순간 지난 프레임에 얹었던 보정과 새 보정의 차이를 기억해 <see cref="overlayBlendSeconds"/> 동안 0 으로 줄인다.
    /// 전환이 없는 프레임은 뼈를 건드리지 않는다(보정 결과 그대로). v13 결과(base)는 손대지 않고, 회전만 쓴다.
    /// </summary>
    private void CarryOverlayChange(bool evaluated, bool overlayChanged, bool restarted, float deltaTime)
    {
        if (!overlaysReady)
            return;

        if (!evaluated || !(overlayBlendSeconds > 0f))
        {
            carryReady = false;
            carryElapsed = float.PositiveInfinity;
            return;
        }

        if (restarted)
            carryElapsed = float.PositiveInfinity;

        ReadHairRotations(overlayWorld);
        bool start = overlayChanged && carryReady;
        if (start)
            carryElapsed = 0f;
        float weight = AssassinHairClipRules.CarryWeight(carryElapsed, overlayBlendSeconds);

        AssassinHairMotion.Chain[] chains = hair.chains;
        int i = 0;
        for (int c = 0; c < chains.Length; c++)
        {
            Transform[] bones = chains[c].bones;
            for (int b = 0; b < bones.Length; b++, i++)
            {
                Quaternion delta = overlayWorld[i] * Quaternion.Inverse(baseWorld[i]);
                if (start)
                    carryOffset[i] = AssassinHairClipRules.CarryOffset(appliedDelta[i], delta);
                if (weight > 0f)
                {
                    delta = AssassinHairClipRules.ApplyCarry(carryOffset[i], weight, delta);
                    // 뿌리 마디부터 월드 회전을 차례로 정하므로 자식 마디도 정확히 이 값이 된다.
                    bones[b].rotation = delta * baseWorld[i];
                }
                appliedDelta[i] = delta;
            }
        }

        carryReady = true;
        if (!float.IsInfinity(carryElapsed))
            carryElapsed += Mathf.Max(0f, deltaTime);
    }

    private void ReadHairRotations(Quaternion[] into)
    {
        int i = 0;
        foreach (AssassinHairMotion.Chain chain in hair.chains)
            foreach (Transform bone in chain.bones)
                into[i++] = bone.rotation;
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
