using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 캐릭터별 프로필 저작(할 일 267). 메뉴 <c>Tools/Boss Victory/캐릭터별 프로필 만들기 (267)</c> —
/// 표에 프로필이 없는 캐릭터(팔라딘·거너·어쌔신)를 <b>코드 기본값</b>(<see cref="BossVictoryProfile.CreateDefault"/> 와 같은 값)으로 만들어 표에 넣는다.
/// 이미 있는 것은 건너뛴다(덮어쓰지 않음). 타임라인 창 '이 캐릭터 프로필 만들기' 버튼도 같은 길.
/// 기본 프로필 에셋은 만들지 않는다(11-12 삭제 — 표에 없는 캐릭터는 코드 기본값). 복제 원본을 코드 기본값으로 둔 이유: 다른 캐릭터 프로필(예: 팔라딘)을 복제하면
/// 그 캐릭터 전용 칸(손 검 번개·낙하 검 배율·백대쉬 동시 등)까지 딸려 온다. 비슷하게 시작하려면 창 '다른 캐릭터에 복사' 로 노드를 옮긴다.
/// </summary>
public static class BossVictoryProfileAuthoring
{
    const string SettingsPath = "Assets/Resources/BossVictory/BossVictoryGlitchSettings.asset";
    public const string Folder = "Assets/Resources/BossVictory/Profiles";

    static readonly TutorialCharacter[] Characters = { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin };

    public static string ProfilePath(TutorialCharacter character) => $"{Folder}/BossVictoryProfile_{character}.asset";

    [MenuItem("Tools/Boss Victory/캐릭터별 프로필 만들기 (267)")]
    public static void CreateMissing()
    {
        var settings = AssetDatabase.LoadAssetAtPath<BossVictoryGlitchSettings>(SettingsPath);
        if (settings == null)
        {
            Debug.LogError($"[BossVictory] 설정 에셋이 없다 — {SettingsPath}");
            return;
        }
        int made = 0;
        foreach (TutorialCharacter c in Characters)
        {
            if (settings.HasOwnProfile(c)) continue;
            CreateCharacterProfile(settings, c);
            made++;
        }
        Debug.Log($"[BossVictory] 캐릭터별 프로필 — 새로 만든 것 {made}개(이미 있는 것은 건너뜀). 표: {SettingsPath}");
    }

    /// <summary>그 캐릭터 프로필을 코드 기본값으로 만들고(파일이 이미 있으면 그 파일) 표에 넣는다(빈 줄이 있으면 그 줄을 채움). 만든 프로필. None 은 만들지 않는다(null).</summary>
    public static BossVictoryProfile CreateCharacterProfile(BossVictoryGlitchSettings settings, TutorialCharacter character)
    {
        if (character == TutorialCharacter.None) return null;
        string path = ProfilePath(character);
        BossVictoryProfile profile = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>(path);
        if (profile == null) profile = CreateAsset(path);
        Undo.RecordObject(settings, "보스 처치 연출 캐릭터 프로필 만들기");
        int row = settings.characterProfiles.FindIndex(r => r.character == character);
        var entry = new BossVictoryGlitchSettings.CharacterProfile { character = character, profile = profile };
        if (row >= 0) settings.characterProfiles[row] = entry;
        else settings.characterProfiles.Add(entry);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log($"[BossVictory] {character} 프로필 — {path}(코드 기본값)", profile);
        return profile;
    }

    const string AssassinPrefabPath = "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab";

    /// <summary>
    /// 할 일 272 처치자 이동을 정본 프로필에 넣는다 — 어쌔신 = 순간이동 Q(스킬 연출 시작 = 백대쉬 시작 마커, 끝 자르기 = 정지 프레임 71f) · 거너 = 후퇴 차징.
    /// 지정한 칸만 바꾸고 Unity 직렬화로 저장한다(다른 칸 — 사용자 튜닝 값 — 은 그대로). 팔라딘은 기본(백대쉬) 그대로.
    /// </summary>
    [MenuItem("Tools/Boss Victory/처치자 이동 프로필 적용 (272)")]
    public static void ApplyKillerMoves()
    {
        var assassin = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>(ProfilePath(TutorialCharacter.Assassin));
        if (assassin != null)
        {
            Undo.RecordObject(assassin, "처치자 이동 프로필 적용 (272)");
            assassin.killerMove = BossVictoryKillerMove.TeleportDash;
            assassin.timeline.finisherStartTime = assassin.timeline.backDashStartTime;
            AnimationClip clip = TeleportMoveClip(assassin.teleportMoveState);
            float fps = clip != null ? clip.frameRate : 60f;
            assassin.timeline.finisherTrimSeconds = BossVictoryAnimTrim.ClampTrim(
                BossVictoryKillerMoves.FreezeSeconds(BossVictoryKillerMoves.DefaultFreezeFrame, fps), clip != null ? clip.length : 0f, fps);
            EditorUtility.SetDirty(assassin);
            AssetDatabase.SaveAssetIfDirty(assassin);
            Debug.Log($"[BossVictory] 어쌔신 프로필 — 순간이동 Q · 스킬 연출 시작 {assassin.timeline.finisherStartTime:0.###}s · 정지 {assassin.timeline.finisherTrimSeconds:0.####}s({clip?.name})", assassin);
        }
        var gunner = AssetDatabase.LoadAssetAtPath<BossVictoryProfile>(ProfilePath(TutorialCharacter.Gunner));
        if (gunner != null)
        {
            Undo.RecordObject(gunner, "처치자 이동 프로필 적용 (272)");
            gunner.killerMove = BossVictoryKillerMove.RetreatCharge;
            EditorUtility.SetDirty(gunner);
            AssetDatabase.SaveAssetIfDirty(gunner);
            Debug.Log("[BossVictory] 거너 프로필 — 후퇴 차징", gunner);
        }
    }

    /// <summary>어쌔신 Variant Animator 의 순간이동 Q 상태 클립(없으면 null).</summary>
    public static AnimationClip TeleportMoveClip(string state)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssassinPrefabPath);
        Animator animator = prefab != null ? prefab.GetComponentInChildren<Animator>(true) : null;
        return animator != null && animator.runtimeAnimatorController != null
            ? AnimationEventRequirements.FindStateClip(animator.runtimeAnimatorController, state) : null;
    }

    static BossVictoryProfile CreateAsset(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var p = ScriptableObject.CreateInstance<BossVictoryProfile>();
        p.name = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(p, AssetDatabase.GenerateUniqueAssetPath(path));
        AssetDatabase.SaveAssets();
        return p;
    }
}
