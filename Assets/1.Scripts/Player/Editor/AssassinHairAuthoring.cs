using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 47 — 어쌔신 머리카락 본(Hair_Source00~06_01~04, 7가닥 × 4마디) 2차 모션을 Armature 프리팹에 붙인다.
/// 전체 저작(<see cref="AssassinShellAuthoring"/>)을 다시 돌리지 않고 머리카락 컴포넌트만 다룬다 — 사용자가 손으로 고친
/// 단검 위치·회전과 애니 이벤트를 건드리지 않기 위해서다. 멱등: 없으면 추가, 있으면 바인드 포즈만 다시 캡처한다.
/// </summary>
public static class AssassinHairAuthoring
{
    private const string Tag = "[Assassin/Hair]";
    private const string ArmaturePath = "Assets/2.Prefabs/Player/Assassin/Assassin_Armature.prefab";

    [MenuItem("Tools/Player/Assassin/머리카락 본 연결")]
    private static void Run()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ArmaturePath);
        try
        {
            if (!Ensure(root))
                return;

            PrefabUtility.SaveAsPrefabAsset(root, ArmaturePath);
            Debug.Log($"{Tag} 머리카락 컴포넌트 연결·바인드 포즈 캡처: {ArmaturePath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Armature 루트에 v13 솔버를 보장하고 지금(애니메이션 전) 자세로 바인드 포즈를 캡처한다.
    /// 런타임 Awake 자동 캡처에 기대지 않는다 — 비활성 생성 등으로 첫 Awake 때 자세가 바인드가 아닐 수 있다.
    /// </summary>
    public static bool Ensure(GameObject root)
    {
        AssassinHairMotion hair = root.GetComponent<AssassinHairMotion>();
        if (hair == null)
            hair = root.AddComponent<AssassinHairMotion>();

        try
        {
            hair.CaptureBindPose();
        }
        catch (System.InvalidOperationException error)
        {
            Debug.LogError($"{Tag} 바인드 포즈 캡처 실패 — FBX 본 이름(Head·Hips·Hair_SourceNN_MM)을 확인할 것: {error.Message}", root);
            return false;
        }

        // 캡처가 내부에서 ResetSimulation 을 불러 진단 카운터가 오른다 — 재실행해도 프리팹 diff 가 없게 0 으로 둔다.
        hair.resetCount = hair.teleportResetCount = hair.collisionCorrections = 0;
        hair.maximumObservedSpeed = hair.maximumLengthError = hair.maximumBoneAngle = hair.maximumContactResidual = 0f;
        EditorUtility.SetDirty(hair);
        return true;
    }
}
