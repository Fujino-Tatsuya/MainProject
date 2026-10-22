using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// L3 — 씬·프리팹 정적 라벨에 <see cref="LocalizedText"/> 를 붙이고 키를 적는다(멱등 — 이미 맞으면 저장하지 않는다).
/// 글자·위치·글꼴은 건드리지 않는다. 저작된 글자는 시트에 행이 없을 때의 폴백(ko 원문)으로 남는다.
/// 런타임에 코드가 글자를 쓰는 라벨(어쌔신 변신·거너 과열·머리 위 이름)은 여기 없다 — 코드가 <see cref="UiStrings"/> 를 쓴다.
/// 설정창(SettingsBody.prefab) 라벨은 여기가 아니라 <c>SettingsLocalizationLinker</c>(같은 <see cref="Apply"/> 사용)가 붙인다.
/// </summary>
public static class UiStringsAuthoring
{
    private const string Tag = "[UiStringsAuthoring]";
    private const string ResultScenePath = "Assets/0.Scenes/MainFlow/5.ResultScene.unity";
    private const string TitleScenePath = "Assets/0.Scenes/MainFlow/1.TitleScene.unity";
    private const string AssassinHudPath = "Assets/2.Prefabs/Player/Assassin/AssassinHUD.prefab";
    private const string InteractPromptPath = "Assets/2.Prefabs/UI/InteractPrompt.prefab";
    private const string LobbyResultPanelPath = "Assets/2.Prefabs/UI/Result/LobbyResultPanel.prefab";

    /// <summary>라벨 하나 — <see cref="ObjectPath"/> 는 루트 아래 이름 경로의 끝부분(예 <c>PlayerRowsHeader/Header_Kills</c>).</summary>
    private readonly struct Target
    {
        public Target(string objectPath, UiString text, bool authorKo = false)
        {
            ObjectPath = objectPath;
            Text = text;
            AuthorKo = authorKo;
        }

        public string ObjectPath { get; }
        public UiString Text { get; }
        /// <summary>저작 글자도 ko 로 맞춘다 — 시트에 행이 없을 때 영어 원문 대신 한국어가 보이게(타이틀 메뉴, 10-22).</summary>
        public bool AuthorKo { get; }
    }

    // 타이틀 모니터 메뉴 — 기본 한국어(경석 10-22). 영어는 시트 en(START·SETTING·EXIT).
    private static readonly Target[] TitleTargets =
    {
        new Target("Start_Button/Text (TMP)", UiStrings.TitleStart, authorKo: true),
        new Target("Option_Button/Text (TMP)", UiStrings.TitleSetting, authorKo: true),
        new Target("Exit_Button/Text (TMP)", UiStrings.TitleExit, authorKo: true),
    };

    private static readonly Target[] ResultTargets =
    {
        new Target("Header_Character", UiStrings.ResultCharacter),
        new Target("Header_Damage", UiStrings.ResultDamage),
        new Target("Header_Kills", UiStrings.ResultKills),
        new Target("Header_Counters", UiStrings.ResultCounters),
        new Target("Header_Deaths", UiStrings.ResultDeaths),
    };

    private static readonly Target[] AssassinHudTargets =
    {
        new Target("EnhancedReady", UiStrings.AssassinEnhancedReady),
    };

    private static readonly Target[] InteractPromptTargets =
    {
        new Target("InteractText", UiStrings.InteractPrompt),
    };

    // 로비 결과 패널 — 프리팹 저작 메뉴(LobbyResultPanelAuthoring)도 같은 키를 붙인다
    private static readonly Target[] LobbyResultPanelTargets =
    {
        new Target("Text_Title", LobbyResultUiStrings.Title),
        new Target("Text_PlayTimeLabel", LobbyResultUiStrings.PlayTimeLabel),
        new Target("Button_Close/Label", LobbyResultUiStrings.Close),
        new Target("Header_Rank/Label", LobbyResultUiStrings.HeaderRank),
        new Target("Header_Player/Label", LobbyResultUiStrings.HeaderPlayer),
        new Target("Header_Damage/Label", LobbyResultUiStrings.HeaderDamage),
        new Target("Header_Healing/Label", LobbyResultUiStrings.HeaderHealing),
        new Target("Header_Counters/Label", LobbyResultUiStrings.HeaderCounters),
        new Target("Header_DamageTaken/Label", LobbyResultUiStrings.HeaderDamageTaken),
        new Target("Header_Deaths/Label", LobbyResultUiStrings.HeaderDeaths),
    };

    [MenuItem("Tools/Localization/L3 — 씬·프리팹 라벨 키 부착")]
    private static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{Tag} Play 중에는 실행하지 않는다 — 먼저 정지할 것.");
            return;
        }

        int changed = 0;
        changed += AuthorPrefab(AssassinHudPath, AssassinHudTargets);
        changed += AuthorPrefab(InteractPromptPath, InteractPromptTargets);
        changed += AuthorPrefab(LobbyResultPanelPath, LobbyResultPanelTargets);
        changed += AuthorScene(ResultScenePath, ResultTargets);
        changed += AuthorScene(TitleScenePath, TitleTargets);
        Debug.Log($"{Tag} 완료 — 바뀐 라벨 {changed}개{(changed == 0 ? "(전부 이미 맞음, 저장 안 함)" : string.Empty)}");
    }

    /// <summary>
    /// <paramref name="go"/> 의 TMP_Text 옆에 LocalizedText(key) 가 있게 한다. 바꿨으면 true — 이미 같으면 아무것도 안 한다.
    /// TMP_Text 가 없으면 false(붙이지 않는다).
    /// </summary>
    public static bool Apply(GameObject go, string key)
    {
        if (go == null || go.GetComponent<TMP_Text>() == null)
            return false;

        bool changed = false;
        LocalizedText localized = go.GetComponent<LocalizedText>();
        if (localized == null)
        {
            localized = go.AddComponent<LocalizedText>();
            changed = true;
        }

        var so = new SerializedObject(localized);
        SerializedProperty keyProperty = so.FindProperty("key");
        if (keyProperty.stringValue != key)
        {
            keyProperty.stringValue = key;
            so.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }

        return changed;
    }

    private static int AuthorPrefab(string path, Target[] targets)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogError($"{Tag} 프리팹이 없다: {path}");
            return 0;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int changed = ApplyAll(root.transform, targets, path);
            if (changed > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"{Tag} 프리팹 저장: {path} (라벨 {changed}개)");
            }

            return changed;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static int AuthorScene(string path, Target[] targets)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedHere = !scene.isLoaded;
        if (openedHere)
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

        try
        {
            int changed = 0;
            foreach (Target target in targets)
            {
                Transform found = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    found = FindByPath(root.transform, target.ObjectPath);
                    if (found != null)
                        break;
                }

                changed += ApplyOne(found, target, path);
            }

            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"{Tag} 씬 저장: {path} (라벨 {changed}개)");
            }

            return changed;
        }
        finally
        {
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static int ApplyAll(Transform root, Target[] targets, string where)
    {
        int changed = 0;
        foreach (Target target in targets)
            changed += ApplyOne(FindByPath(root, target.ObjectPath), target, where);
        return changed;
    }

    private static int ApplyOne(Transform found, Target target, string where)
    {
        if (found == null)
        {
            Debug.LogError($"{Tag} {where} 에 '{target.ObjectPath}' 가 없다 — {target.Text.Key} 건너뜀.");
            return 0;
        }

        var text = found.GetComponent<TMP_Text>();
        if (text == null)
        {
            Debug.LogError($"{Tag} {where} › {target.ObjectPath} 에 TMP_Text 가 없다 — {target.Text.Key} 건너뜀.");
            return 0;
        }

        bool authored = false;
        if (text.text != target.Text.Ko)
        {
            if (target.AuthorKo)
            {
                var textSo = new SerializedObject(text);
                textSo.FindProperty("m_text").stringValue = target.Text.Ko;
                textSo.ApplyModifiedPropertiesWithoutUndo();
                authored = true;
            }
            else
            {
                Debug.LogWarning($"{Tag} {where} › {target.ObjectPath} 글자 '{text.text}' ≠ 키 ko '{target.Text.Ko}' — 시트 행이 없으면 저작 글자가 보인다.");
            }
        }

        return Apply(found.gameObject, target.Text.Key) || authored ? 1 : 0;
    }

    /// <summary><paramref name="root"/>(포함) 아래에서 이름 경로가 <paramref name="path"/> 로 끝나는 첫 오브젝트(비활성 포함).</summary>
    public static Transform FindByPath(Transform root, string path)
    {
        string[] names = path.Split('/');
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            Transform cursor = t;
            int i = names.Length - 1;
            while (i >= 0 && cursor != null && cursor.name == names[i])
            {
                cursor = cursor.parent;
                i--;
            }

            if (i < 0)
                return t;
        }

        return null;
    }
}
