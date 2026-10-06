using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 팔라딘 평타 4타 루트모션 재저작 도구 (PLAN-paladin-da C2).
/// 분석 = 읽기 전용. 클립을 Paladin 아바타에 샘플해 양발 궤적(캐릭터 로컬)을 콘솔·CSV 로 낸다.
/// </summary>
public static class PaladinAttackRootMotionTool
{
    private const string ArmaturePath = "Assets/2.Prefabs/Player/Paladin/Paladin_Armature.prefab";
    private const string ClipFolder = "Assets/4.Animations/Player/Garen/";
    private const float SampleRate = 60f;
    // 이 높이(최저점 기준)·수평 속도 이하면 디딘 발로 본다.
    private const float PlantHeightTolerance = 0.03f;
    private const float PlantSpeedTolerance = 0.6f;

    [MenuItem("Tools/Player/Paladin/평타 루트모션 분석 (읽기 전용)")]
    private static void Analyze()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ArmaturePath);
        try
        {
            Animator animator = root.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError($"[PaladinRootMotion] {ArmaturePath} 에 Humanoid Animator 가 없다.");
                return;
            }

            for (int i = 1; i <= 4; i++)
            {
                string path = $"{ClipFolder}Garen_Default_Attack_{i}.anim";
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    Debug.LogError($"[PaladinRootMotion] 클립 없음: {path}");
                    continue;
                }

                AnalyzeClip(animator, clip, i);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AnalyzeClip(Animator animator, AnimationClip clip, int hitNumber)
    {
        Transform character = animator.transform;
        Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);

        int frames = Mathf.CeilToInt(clip.length * SampleRate) + 1;
        var left = new Vector3[frames];
        var right = new Vector3[frames];
        var body = new Vector3[frames];

        for (int f = 0; f < frames; f++)
        {
            float t = Mathf.Min(f / SampleRate, clip.length);
            clip.SampleAnimation(animator.gameObject, t);
            left[f] = character.InverseTransformPoint(leftFoot.position);
            right[f] = character.InverseTransformPoint(rightFoot.position);
            body[f] = character.InverseTransformPoint(hips.position);
        }

        var csv = new StringBuilder("t,lx,ly,lz,rx,ry,rz,hx,hy,hz\n");
        for (int f = 0; f < frames; f++)
        {
            csv.AppendLine(string.Join(",",
                F(f / SampleRate),
                F(left[f].x), F(left[f].y), F(left[f].z),
                F(right[f].x), F(right[f].y), F(right[f].z),
                F(body[f].x), F(body[f].y), F(body[f].z)));
        }

        string csvPath = Path.GetFullPath($"Temp/PaladinRootMotion_{hitNumber}.csv");
        File.WriteAllText(csvPath, csv.ToString());

        var report = new StringBuilder();
        report.AppendLine($"[PaladinRootMotion] {hitNumber}타 {clip.name} 길이 {F(clip.length)}s · {frames}프레임 · CSV {csvPath}");
        AppendPlantSegments(report, "왼발", left);
        AppendPlantSegments(report, "오른발", right);
        report.AppendLine($"  골반 z: 시작 {F(body[0].z)} · 최대 {F(MaxZ(body))} · 끝 {F(body[frames - 1].z)}");
        Debug.Log(report.ToString());
    }

    private static void AppendPlantSegments(StringBuilder report, string label, Vector3[] foot)
    {
        float minY = float.MaxValue;
        foreach (Vector3 p in foot)
            minY = Mathf.Min(minY, p.y);

        report.AppendLine($"  {label}: 최저 y {F(minY)} · z {F(foot[0].z)} → {F(foot[foot.Length - 1].z)}");

        int start = -1;
        for (int f = 0; f <= foot.Length; f++)
        {
            bool planted = f < foot.Length && IsPlanted(foot, f, minY);
            if (planted && start < 0)
                start = f;

            if (!planted && start >= 0)
            {
                int end = f - 1;
                report.AppendLine($"    디딤 {F(start / SampleRate)}~{F(end / SampleRate)}s · z {F(foot[start].z)} → {F(foot[end].z)}");
                start = -1;
            }
        }
    }

    private static bool IsPlanted(Vector3[] foot, int f, float minY)
    {
        if (foot[f].y > minY + PlantHeightTolerance)
            return false;

        int prev = Mathf.Max(0, f - 1);
        int next = Mathf.Min(foot.Length - 1, f + 1);
        if (next == prev)
            return true;

        Vector3 delta = foot[next] - foot[prev];
        delta.y = 0f;
        float speed = delta.magnitude * SampleRate / (next - prev);
        return speed <= PlantSpeedTolerance;
    }

    private static float MaxZ(Vector3[] points)
    {
        float max = float.MinValue;
        foreach (Vector3 p in points)
            max = Mathf.Max(max, p.z);
        return max;
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
