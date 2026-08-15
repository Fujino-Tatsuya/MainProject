using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="EffectEntry"/>의 수명이 지금 어떻게 정해지고 있는지를 인스펙터에 드러낸다.
/// 자동인지 손으로 적은 값인지, 적은 값이 실제 파티클보다 짧지는 않은지.
/// </summary>
[CustomEditor(typeof(EffectEntry))]
[CanEditMultipleObjects]
public class EffectEntryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (targets.Length > 1) return;

        var entry = (EffectEntry)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("실제 적용되는 수명", EditorStyles.boldLabel);

        DrawLifetime("duration", entry.duration, entry.ComputedDuration,
                     EffectLifetime.Estimate(entry.parts), entry.LongestPartDelay);

        if (entry.outroParts != null && entry.outroParts.Length > 0)
        {
            DrawLifetime("outroDuration", entry.outroDuration, entry.ComputedOutroDuration,
                         EffectLifetime.Estimate(entry.outroParts), entry.LongestOutroDelay);
        }

        if (GUILayout.Button("프리팹에서 다시 계산"))
        {
            Undo.RecordObject(entry, "Recompute Effect Lifetimes");
            entry.RecomputeLifetimes();
            EditorUtility.SetDirty(entry);
        }
    }

    private static void DrawLifetime(string label, float manual, float stored, float live, float longestDelay)
    {
        bool automatic = manual <= 0f;
        float applied = automatic ? stored : manual;

        EditorGUILayout.LabelField(
            $"{label}: {applied:F2}s",
            automatic ? "자동 (프리팹 계산)" : "직접 입력 (오버라이드)");

        if (live == EffectLifetime.Unknown)
        {
            EditorGUILayout.HelpBox(
                $"{label}의 파트에 루프 파티클이 있어 자동 계산을 할 수 없다. " +
                "루프 이펙트라면 정상이다 — 수명은 Release() 이후 outroParts로만 센다.",
                MessageType.None);
            return;
        }

        if (live <= 0f)
        {
            EditorGUILayout.HelpBox(
                $"{label}의 파트에 ParticleSystem이 없어 자동 계산이 안 된다. 값을 직접 적을 것.",
                MessageType.Warning);
            return;
        }

        if (!automatic && manual < live)
        {
            EditorGUILayout.HelpBox(
                $"적어 넣은 {manual:F2}s가 파티클이 실제로 죽는 {live:F2}s보다 짧다. " +
                "의도한 것이 아니라면 이펙트가 잘린 채 반납된다. (0으로 두면 자동으로 맞춰진다)",
                MessageType.Warning);
        }

        if (applied < longestDelay)
        {
            EditorGUILayout.HelpBox(
                $"{label}({applied:F2}s)이 가장 늦은 파트의 delay({longestDelay:F2}s)보다 짧다. " +
                "그 파트는 발화되기 전에 반납된다.",
                MessageType.Error);
        }

        if (automatic && !Mathf.Approximately(stored, live))
        {
            EditorGUILayout.HelpBox(
                $"저장된 계산값({stored:F2}s)이 지금 프리팹({live:F2}s)과 다르다. " +
                "아래 버튼으로 갱신할 것.",
                MessageType.Warning);
        }
    }
}
