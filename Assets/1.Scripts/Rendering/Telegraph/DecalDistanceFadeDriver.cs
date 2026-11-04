using UnityEngine;

/// <summary>
/// 데칼 거리 감쇠(할 일 202)의 기준점 — 매 프레임 로컬 플레이어(오너 화면) 위치를 전역 셰이더 값
/// <see cref="DecalDistanceFade.GlobalProperty"/> 하나로 넣는다. 텍스처·메시를 만들지 않는다.
///
/// 씬 배치 없이 스스로 붙는다(<see cref="BossArenaDecalReceiverInstaller"/> 와 같은 방식) — 씬·프리팹 머지 충돌을 피한다.
/// 로컬 플레이어가 없거나(로비 전·관전·씬 전환 중) 이 오브젝트가 꺼지면 w = 0 → 감쇠 1(202 이전 화면).
/// 피어마다 자기 로컬 플레이어 기준이라 네트워크 값이 아니다(표시 전용).
/// </summary>
[DisallowMultipleComponent]
public sealed class DecalDistanceFadeDriver : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("[Rendering] DecalDistanceFade");
        go.AddComponent<DecalDistanceFadeDriver>();
        DontDestroyOnLoad(go);
    }

    void LateUpdate()
    {
        Player local = Player.LocalPlayer;
        Vector3? position = local != null ? local.transform.position : (Vector3?)null;
        Shader.SetGlobalVector(DecalDistanceFade.GlobalPropertyId, DecalDistanceFade.GlobalValue(position));
    }

    void OnDisable() => Shader.SetGlobalVector(DecalDistanceFade.GlobalPropertyId, Vector4.zero);
}
