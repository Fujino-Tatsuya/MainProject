using UnityEngine;

/// <summary><see cref="SlowMotionOwnerZoom"/> 한 프레임 입력 — 카메라가 <see cref="GlobalTimeScale"/>·디렉터에서 질의해 채운다.</summary>
public struct SlowMotionOwnerZoomInput
{
    /// <summary>내가 일으킨 슬로우가 진행 중이고 줌이 허용된다(낙하 뷰·관전·세션 미가동 아님).</summary>
    public bool Mine;
    public uint TriggerId;
    public SlowMotionPurpose Purpose;
    public SlowMotionPhase Phase;
    public float CurrentScale;
    public float ProfileScale;
    /// <summary>지금 슬로우 목적의 줌 배율(A3 꺼짐 = 1).</summary>
    public float ZoomFactor;
    /// <summary>내 강 시도가 아직 결론 전이다(오너 상태 기계 AttemptStrong) — 시도 복귀 중에도 줌을 붙잡는다.</summary>
    public bool AwaitingStrongResult;
}

/// <summary>
/// 오너 슬로우 줌 한 번(할 일 132) — 내 간파 한 건(발동 번호 사슬: 시도 → 같은 시전자의 성공)을 한 줌 세션으로 본다.
/// <list type="bullet">
/// <item>세션 = 내 슬로우가 이어지는 동안. 진행 중 내 성공 슬로우가 새 번호로 오면 같은 세션을 잇는다.
///       남의 슬로우가 덮음·강제 리셋·슬로우 끝·낙하/관전이면 세션이 끝나고 한 번 복귀한다.</item>
/// <item>목표 깊이는 복귀 단계(Exit·FailExit) 전까지 줄지 않는다 — 성공 진입이 시도 배율에서 다시 시작해도 줌이 빠지지 않는다.
///       강 시도가 결론 전이면(<see cref="SlowMotionOwnerZoomInput.AwaitingStrongResult"/>) 시도 Exit 중에도 붙잡는다.</item>
/// <item>배율은 세션에서 본 가장 깊은 쪽(min)만 따른다 — 강 시도(0.7) → 성공(0.85) 은 0.7 유지, 약(0.93) → 성공 은 0.85 로 더 깊게.</item>
/// <item>깊이·배율 모두 실시간 감쇠 — 튀지 않는다.</item>
/// </list>
/// 순수 C# 이다(EditMode 테스트 대상). 깊이 산식은 <see cref="SlowMotionCameraZoom"/>.
/// </summary>
public sealed class SlowMotionOwnerZoom
{
    private bool _sessionActive;
    private uint _triggerId;
    private float _inheritedFactor = 1f; // 사슬 앞 슬로우들의 가장 깊은 배율
    private float _currentFactor = 1f;   // 지금 슬로우의 배율(마지막 프레임)

    /// <summary>이번 프레임 목표 깊이 [0, 1].</summary>
    public float TargetDepth { get; private set; }
    /// <summary>감쇠한 깊이.</summary>
    public float Depth { get; private set; }
    /// <summary>감쇠한 배율.</summary>
    public float Factor { get; private set; } = 1f;
    public bool SessionActive => _sessionActive;

    /// <summary>FollowOffset 배율. 세션이 끝나고 깊이가 0 이 되면 정확히 1.</summary>
    public float Multiplier => SlowMotionCameraZoom.OffsetMultiplier(Depth, Factor);

    /// <summary>한 프레임 진행하고 <see cref="Multiplier"/> 를 돌려준다.</summary>
    public float Step(in SlowMotionOwnerZoomInput input, float unscaledDeltaTime)
    {
        if (!input.Mine)
        {
            _sessionActive = false;
            TargetDepth = 0f;
        }
        else
        {
            UpdateSession(input);
            TargetDepth = NextTargetDepth(input);
        }

        if (_sessionActive)
            StepFactor(Mathf.Min(_inheritedFactor, _currentFactor), unscaledDeltaTime);

        Depth = SlowMotionCameraZoom.StepDepth(Depth, TargetDepth, unscaledDeltaTime);
        return Multiplier;
    }

    private void UpdateSession(in SlowMotionOwnerZoomInput input)
    {
        float factor = SlowMotionCameraZoom.ClampZoomFactor(input.ZoomFactor);
        if (_sessionActive && input.TriggerId != _triggerId)
        {
            if (input.Purpose == SlowMotionPurpose.Success)
            {
                // 같은 시전자(나)의 성공이 이어졌다 — 앞 슬로우 배율을 물려받는다.
                _inheritedFactor = Mathf.Min(_inheritedFactor, _currentFactor);
            }
            else
            {
                // 내 새 시도 — 새 세션(깊이는 감쇠로 이어진다).
                _inheritedFactor = 1f;
                TargetDepth = 0f;
            }
        }
        else if (!_sessionActive)
        {
            _inheritedFactor = 1f;
            TargetDepth = 0f;
        }

        _sessionActive = true;
        _triggerId = input.TriggerId;
        _currentFactor = factor;
    }

    private float NextTargetDepth(in SlowMotionOwnerZoomInput input)
    {
        float slowDepth = SlowMotionCameraZoom.TargetDepth(true, true, true, input.CurrentScale, input.ProfileScale);
        return IsReturning(input) ? slowDepth : Mathf.Max(TargetDepth, slowDepth);
    }

    /// <summary>복귀 단계인가 — 강 시도가 결론 전이면 시도 Exit 는 복귀로 치지 않는다(성공이 이어질 수 있다).</summary>
    public static bool IsReturning(in SlowMotionOwnerZoomInput input)
    {
        switch (input.Phase)
        {
            case SlowMotionPhase.Exit:
                return !(input.Purpose == SlowMotionPurpose.Attempt && input.AwaitingStrongResult);
            case SlowMotionPhase.FailExit:
            case SlowMotionPhase.None:
                return true;
            default:
                return false;
        }
    }

    private void StepFactor(float target, float unscaledDeltaTime)
    {
        // 줌이 빠져 있으면 바로 맞춘다. 깊은 중에는 깊이 감쇠와 같은 체감 속도(전 거리 = DepthBlendSeconds)로 옮긴다.
        if (Depth <= 0f)
        {
            Factor = target;
            return;
        }

        float speed = Mathf.Max(1f - target, 1f - Factor) / SlowMotionCameraZoom.DepthBlendSeconds;
        Factor = Mathf.MoveTowards(Factor, target, Mathf.Max(0f, unscaledDeltaTime) * speed);
    }
}
