using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 간파 제작용 허수아비 회복 시간(DebugInterruptLab 씬의 허수아비 인스턴스에만 붙인다, 할 일 115 C5).
///
/// 간파 성공 → <see cref="recoverySeconds"/> 뒤 인터럽트 가능 상태로 돌아오고, 그 상태는 시간 제한 없이 유지된다.
/// 순환 코드(<see cref="TrainingDummyInterruptCycle"/>)는 그대로다 — 이미 [취약 → idle → 인터럽트 가능(무기한)] 이므로
/// 이 컴포넌트는 같은 오브젝트의 <see cref="TrainingDummyInterrupt"/> 저작값(취약·idle 시간)을
/// <see cref="SplitRecovery"/> 대로 씬 인스턴스 오버라이드로 맞춰 줄 뿐이다(에디터에서, Play 밖에서).
///
/// <list type="bullet">
/// <item>회복 시간 전부가 취약(보라, CC 가 들어감), idle 0.</item>
/// <item>순환 시간은 서버 <see cref="Time.deltaTime"/> 이라 디버그 배속(<see cref="DebugTimeScale"/>)·슬로우를 따른다.</item>
/// <item>순환은 스폰 때 만들어진다 — Play 중 값을 바꾸면 다음 Play 부터 적용된다.</item>
/// <item>씬 오버라이드라 데이터 테이블(Map 시트) 값이 이 인스턴스엔 닿지 않는다 — Verify 가 이 씬을 경고하는 것은 의도다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TrainingDummyInterrupt))]
public sealed class DebugDummyInterruptRecovery : MonoBehaviour
{
    [Tooltip("간파 성공 후 다시 인터럽트 가능해지기까지(초). 이 동안 취약(CC 가능). Play 중 변경은 다음 Play 부터.")]
    [SerializeField, Min(0f)] float recoverySeconds = 2f;

    public float RecoverySeconds => recoverySeconds;

    /// <summary>회복 시간 → 허수아비 순환의 (취약, idle) 시간. 합 = 회복 시간.</summary>
    public static (float vulnerable, float idle) SplitRecovery(float recovery) => (Mathf.Max(0f, recovery), 0f);

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning($"[DebugInterruptLab] {name} 회복 시간 변경은 다음 Play 부터 적용된다.", this);
            return;
        }

        // OnValidate 안에서 다른 컴포넌트를 직렬화 수정하면 경고가 나므로 한 박자 미룬다.
        EditorApplication.delayCall += SyncDummyDurations;
    }

    void SyncDummyDurations()
    {
        if (this == null || Application.isPlaying || !TryGetComponent(out TrainingDummyInterrupt target))
            return;

        (float vulnerable, float idle) = SplitRecovery(recoverySeconds);
        var so = new SerializedObject(target);
        SerializedProperty vulnerableProp = so.FindProperty("vulnerableDuration");
        SerializedProperty idleProp = so.FindProperty("idleDuration");
        if (vulnerableProp == null || idleProp == null)
        {
            Debug.LogError("[DebugInterruptLab] TrainingDummyInterrupt 의 vulnerableDuration/idleDuration 칸을 찾지 못했다 — 필드 이름이 바뀌었나?", this);
            return;
        }

        if (Mathf.Approximately(vulnerableProp.floatValue, vulnerable) && Mathf.Approximately(idleProp.floatValue, idle))
            return;

        vulnerableProp.floatValue = vulnerable;
        idleProp.floatValue = idle;
        so.ApplyModifiedProperties();
    }
#endif
}
