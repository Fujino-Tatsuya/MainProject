// ----------------------------------------------------------------------------
//  DevTelegraphProbe.cs — F6 로 바닥 장판을 내 발밑에 즉석 소환 (개발용 · 에디터 전용)
//
//  왜 있는가: 차징 오라는 **페이즈 66% 체력**에서만 나와서, 렌더링 한 가지를 확인하려고 매번
//  보스를 3분씩 때려야 했다. 그 루프를 2초로 줄인다 — 데칼이 보이는가 / 프롭 위로 이어지는가 /
//  캐릭터에 칠해지는가는 전투와 무관한 질문이므로 전투로 확인할 이유가 없다.
//
//  ⚠️ **에디터 전용**이다(AssetDatabase 사용). 빌드에는 클래스가 아예 없다.
//     검증이 끝나면 지워도 되는 도구다 — 남겨도 빌드에는 영향이 없다.
// ----------------------------------------------------------------------------
#if UNITY_EDITOR
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class DevTelegraphProbe : MonoBehaviour
{
    // 이미 쓰이는 키 — F1·F2(히트VFX) · F5(보스방 워프) · F8(프로파일러) · F9(LookAB).
#if ENABLE_INPUT_SYSTEM
    const Key SpawnKey = Key.F6;
#else
    const KeyCode SpawnKey = KeyCode.F6;
#endif

    const string DecalPrefabPath = "Assets/2.Prefabs/Monster/Boss/AoeDecalTelegraph.prefab";
    const string MeshPrefabPath = "Assets/2.Prefabs/Monster/Boss/JumpTelegraph.prefab";

    const float Radius = 3.5f;    // 차징 오라와 같은 반경(chargeAuraRadius)
    const float Lifetime = 6f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("[Dev] TelegraphProbe");
        go.AddComponent<DevTelegraphProbe>();
        DontDestroyOnLoad(go);
    }

    string _toast;
    float _toastUntil;

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        bool pressed = kb != null && kb[SpawnKey].wasPressedThisFrame;
#else
        bool pressed = Input.GetKeyDown(SpawnKey);
#endif
        if (!pressed) return;

        // 데칼 판 + 메시 판을 **나란히** 띄운다 — 같은 화면에서 비교해야 "데칼이 안 보인다"와
        // "둘 다 안 보인다(다른 원인)"가 구분된다.
        Vector3 at = ResolveOrigin();

        // 🔴 메시 판만 바닥에서 띄운다(표준 간격). 바닥과 정확히 같은 높이에 놓으면 z-fighting 으로
        //    부챗살 무늬가 나서 비교가 오염된다 — 실제 게임 경로도 이 간격을 쓴다.
        //    데칼은 표면에 투영되므로 0 이 맞다(그게 이 작업의 요점이다).
        Spawn(DecalPrefabPath, at + Vector3.left * 2f, "데칼");
        Spawn(MeshPrefabPath, at + Vector3.right * 2f + Vector3.up * GroundProbe.SurfaceOffset, "메시");
    }

    Vector3 ResolveOrigin()
    {
        NetworkObject player = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClient?.PlayerObject
            : null;

        Vector3 at = player != null ? player.transform.position : Vector3.zero;

        // 절대 Y 금지 — 실측 바닥을 찾아 그 위에 놓는다(GroundProbe 규약).
        if (GroundProbe.TryFindGround(at, 0, out RaycastHit ground, out _))
            at.y = ground.point.y;

        return at;
    }

    void Spawn(string path, Vector3 at, string label)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Toast($"F6: 프리팹을 못 찾았다 — {path}");
            return;
        }

        GameObject go = Instantiate(prefab, at, prefab.transform.rotation);
        go.name = $"[Dev] {label} 장판";

        if (go.TryGetComponent(out AoeTelegraph telegraph))
            telegraph.Show(Radius, Lifetime);
        else
            Debug.LogWarning($"[Dev/장판] {path} 에 AoeTelegraph 가 없다 — 표시할 방법이 없다.", go);

        Destroy(go, Lifetime + 0.5f);
        Debug.Log($"[Dev/장판] {label} 소환 — {at} (반경 {Radius}m · {Lifetime}초)", go);
        Toast($"F6: 장판 소환 — 왼쪽 데칼 / 오른쪽 메시");
    }

    void Toast(string message)
    {
        _toast = message;
        _toastUntil = Time.unscaledTime + 3f;
    }

    void OnGUI()
    {
        if (string.IsNullOrEmpty(_toast) || Time.unscaledTime > _toastUntil) return;

        var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        style.normal.textColor = Color.cyan;
        GUI.Label(new Rect(12f, 40f, 720f, 28f), _toast, style);
    }
}
#endif
