/// <summary>로딩 씬이 뜬 뒤 소스 씬(로딩 직전 활성 씬)을 어떻게 내릴지.</summary>
public enum SourceSceneUnloadMode
{
    /// <summary>내릴 씬이 없다(소스 없음 · 소스 = 로딩/타깃 · NGO 소스의 클라).</summary>
    None,
    /// <summary>로컬 언로드 — 서버·클라가 각자 <c>SceneManager.UnloadSceneAsync</c>.</summary>
    Local,
    /// <summary>[서버] 동적 디스폰 → 활성 씬을 로딩으로 → NGO UnloadScene. 클라는 NGO 동기화로 따라간다.</summary>
    Network,
}

/// <summary>
/// 소스 씬 언로드 분기 규칙(순수) — <see cref="NetworkLoadingFlowController"/> 가 쓴다.
/// NGO 가 로드한 소스(로비·결과를 NGO 씬으로 바꾼 뒤)는 서버만 NGO 로 내리고 클라는 손대지 않는다 —
/// 클라가 먼저 로컬로 지우면 NGO Unload 이벤트가 씬 핸들을 못 찾는다. 로컬 소스(지금의 3.LobbyScene)는 양쪽 다 로컬 언로드.
/// 원본: PLAN-48 §4.3 · R2.
/// </summary>
public static class SourceSceneUnloadRule
{
    public static SourceSceneUnloadMode Decide(
        string sourceScene, string loadingScene, string targetScene, bool sourceIsNetworkScene, bool isServer)
    {
        if (string.IsNullOrEmpty(sourceScene) || sourceScene == loadingScene || sourceScene == targetScene)
        {
            return SourceSceneUnloadMode.None;
        }

        if (sourceIsNetworkScene)
        {
            return isServer ? SourceSceneUnloadMode.Network : SourceSceneUnloadMode.None;
        }

        return SourceSceneUnloadMode.Local;
    }
}
