using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 보스 아레나(<c>bossroom</c>) 표면을 데칼 수신자로 표시한다 — <b>모든 피어에서</b>.
///
/// 🔴 왜 스폰 훅이 아니라 자체 설치인가 (2026-09-04 실측):
/// ① 아레나는 <see cref="MapContentSpawner"/> 가 스폰하는 존이 <b>아니다.</b> 맵 밖(x≈500)에
///    씬에 고정 배치돼 있고, 플레이어를 그쪽으로 텔레포트시키는 구조다. 그래서 존 스폰 훅에
///    붙였더니 <b>진입 패드 존(ZoneS_typeBossEnter)만</b> 표시되고 아레나는 빠졌다.
/// ② 아레나를 아는 기존 코드(<c>BossEncounterDirector</c>·<c>BossTeleportManager</c>)는 둘 다
///    <c>if (!IsServer) return</c> 이다. 데칼은 <b>피어별 로컬 렌더링</b>이라 서버에서만 표시하면
///    클라 화면에는 아무것도 안 칠해진다.
/// ③ 씬(<c>4.MapScene</c>·<c>-trensparent</c>)에 컴포넌트를 배치하면 팀원 작업과 머지 충돌이 난다.
///
/// 그래서 런타임에 스스로 붙어서, 아레나 랜드마크(<c>BossLandingPoint</c>)가 나타나면 그 루트를
/// 표시한다. "아레나를 이름으로 찾는다"는 것은 이 프로젝트에 이미 있는 관용구다
/// (<c>BossEncounterDirector</c> 의 착지점 탐색, <c>BossTeleportManager</c> 의 도착 지점 탐색).
///
/// ⚠️ 표시는 <b>비트 추가</b>다 — 조명(Light Layer)은 bit 0 를 그대로 유지하므로 영향이 없다
///    (<see cref="DecalReceivers"/> 주석).
/// </summary>
[DisallowMultipleComponent]
public sealed class BossArenaDecalReceiverInstaller : MonoBehaviour
{
    /// <summary>아레나 랜드마크. 보스 착지점이라 아레나 안에만 있다.</summary>
    const string LandmarkName = "BossLandingPoint";

    /// <summary>이 이름으로 시작하는 조상을 아레나 루트로 본다. 못 찾으면 최상위를 쓴다.</summary>
    const string ArenaRootPrefix = "bossroom";

    const float PollInterval = 0.5f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("[Rendering] BossArenaDecalReceivers");
        go.AddComponent<BossArenaDecalReceiverInstaller>();
        DontDestroyOnLoad(go);
    }

    void Start()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        StartCoroutine(TagWhenArenaAppears());
    }

    void OnDestroy() => SceneManager.sceneLoaded -= HandleSceneLoaded;

    // 맵 씬은 나중에 Additive 로 붙는다 — 로드마다 다시 찾는다(아레나가 새 인스턴스일 수 있다).
    void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => StartCoroutine(TagWhenArenaAppears());

    GameObject _tagged;

    IEnumerator TagWhenArenaAppears()
    {
        // 무한 폴링 금지 — 아레나가 없는 씬(로비·타이틀)도 있다. 스캔 상한을 두고 조용히 끝낸다.
        for (int attempt = 0; attempt < 40; attempt++)
        {
            GameObject root = FindArenaRoot();
            if (root != null && root != _tagged)
            {
                int tagged = DecalReceivers.Tag(root);
                _tagged = root;

                // 🔴 성공을 남긴다 — 데칼은 안 보일 때 조용하므로, "표시가 됐는가"를 로그로 가려야 한다.
                Debug.Log($"[Rendering] 보스 아레나 '{root.name}' 데칼 수신자 {tagged}개 표시 " +
                          $"(mask 0x{DecalReceivers.Mask:X})", root);
                yield break;
            }

            yield return new WaitForSeconds(PollInterval);
        }
    }

    static GameObject FindArenaRoot()
    {
        GameObject landmark = GameObject.Find(LandmarkName);
        if (landmark == null) return null;

        // 아레나 루트까지 올라간다. 이름으로 못 찾으면 최상위를 쓴다 — 표시는 비트 추가라
        // 범위가 넓어도 부작용이 없고(데칼은 프로젝터 볼륨 안에서만 그려진다), 빠지는 것이 더 나쁘다.
        for (Transform t = landmark.transform; t != null; t = t.parent)
        {
            if (t.name.StartsWith(ArenaRootPrefix, System.StringComparison.OrdinalIgnoreCase))
                return t.gameObject;
        }

        return landmark.transform.root.gameObject;
    }
}
