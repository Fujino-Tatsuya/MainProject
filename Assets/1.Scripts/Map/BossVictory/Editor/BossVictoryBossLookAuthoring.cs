using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 표현(11-12) — 어쌔신 처치 연출 프로필의 보스 표현을 림라이트로. 메뉴 <c>Tools/Boss Victory/어쌔신 보스 표현 = 림라이트</c>.
/// SerializedObject 로 그 칸만 바꿔 그 에셋만 저장한다(다른 프로필·다른 값 그대로).
/// </summary>
public static class BossVictoryBossLookAuthoring
{
    [MenuItem("Tools/Boss Victory/어쌔신 보스 표현 = 림라이트")]
    public static void ApplyAssassinRim()
    {
        string path = BossVictoryProfileAuthoring.ProfilePath(TutorialCharacter.Assassin);
        var profile = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>(path);
        if (profile == null)
        {
            Debug.LogError($"[BossVictoryBossLook] 어쌔신 프로필 없음 — {path}");
            return;
        }
        var so = new SerializedObject(profile);
        so.FindProperty(nameof(BossVictoryProfile.bossLook)).enumValueIndex = (int)BossVictoryBossLookMode.RimLight;
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(profile);
        Debug.Log($"[BossVictoryBossLook] 어쌔신 보스 표현 = 림라이트 — {path}");
    }
}
