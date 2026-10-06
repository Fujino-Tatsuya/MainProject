using UnityEngine;

/// <summary>어쌔신 일반 평타의 순서와 재입력 유예만 다루는 순수 모델.</summary>
public sealed class AssassinComboModel
{
    private readonly int stepCount;
    private readonly float continuationSeconds;

    private int nextStep;
    private float lastCompletionTime;
    private bool hasCompletion;

    public int NextStep => nextStep;
    public bool HasCompletion => hasCompletion;

    public AssassinComboModel(int stepCount, float continuationSeconds)
    {
        this.stepCount = Mathf.Max(1, stepCount);
        this.continuationSeconds = Mathf.Max(0f, continuationSeconds);
    }

    /// <summary>지금 시작할 타. 마지막 완료부터 유예를 넘겼으면 항상 1타(인덱스 0).</summary>
    public int Begin(float now)
    {
        // float 누적 시 0.8f 경계가 0.8000002처럼 보일 수 있어 작은 수치 오차를 허용한다.
        if (!hasCompletion || now - lastCompletionTime > continuationSeconds + 0.00001f)
            nextStep = 0;

        return nextStep;
    }

    /// <summary>현재 타를 정상 완료하고 다음 순서를 기록한다.</summary>
    public void Complete(int completedStep, float now)
    {
        int normalized = Mathf.Clamp(completedStep, 0, stepCount - 1);
        nextStep = (normalized + 1) % stepCount;
        lastCompletionTime = now;
        hasCompletion = true;
    }

    /// <summary>Q·E·R 전환, 공격 취소, 사망 등에서 1타로 되돌린다.</summary>
    public void Reset()
    {
        nextStep = 0;
        lastCompletionTime = 0f;
        hasCompletion = false;
    }
}
