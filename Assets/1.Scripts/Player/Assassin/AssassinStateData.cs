using UnityEngine;

/// <summary>어쌔신 R 스택·변신 지속·수동 해제 제한 수치(character_assassin.md §4.2·§10.2, 임시값).</summary>
[CreateAssetMenu(fileName = "AssassinStateData", menuName = "Player/Assassin/State Data")]
[DataTableSheet("Assassin", Order = 1)]
public sealed class AssassinStateData : ScriptableObject
{
    [SerializeField, Min(1)] private int maxStacks = 4;

    [Tooltip("소모 스택 1/2/3/4개의 변신 지속시간(초). 원소 수가 상한보다 적으면 마지막 값을 쓴다.")]
    [SerializeField] private float[] transformDurations = { 4f, 6f, 8f, 10f };

    [Tooltip("변신 시작 후 이 시간 동안 R 재입력(수동 해제)을 무시한다.")]
    [SerializeField, Min(0f)] private float releaseLockSeconds = 2f;

    public AssassinStateRules Rules => new AssassinStateRules(maxStacks, transformDurations, releaseLockSeconds);
}
