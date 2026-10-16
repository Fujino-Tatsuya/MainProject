#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using Unity.Netcode;
using UnityEngine;

// 튜토리얼 구간 스킵(할 일 34, QA·테스트용) — 단축키·패널은 각 피어의 TutorialSkipController, 실행은 서버.
// 🔴 에디터·Development Build·DEV_HOTKEYS 빌드에만 있다(심볼을 지운 릴리스 빌드엔 RPC 도 없다 — 호스트·클라는 같은 빌드여야 한다).
// 원본: Docs/tech/tutorial-director.md §10.5.
public sealed partial class TutorialDirector
{
    /// <summary>[아무 피어] 구간 스킵 요청 — 서버가 호스트(서버 자신) 요청인지 검증한 뒤 실행한다.</summary>
    public void RequestSkip(TutorialSkipTarget target)
    {
        if (!IsSpawned) return;
        RequestSkipServerRpc(target);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSkipServerRpc(TutorialSkipTarget target, ServerRpcParams p = default)
    {
        ulong sender = p.Receive.SenderClientId;
        // 호스트만 — 리슨 서버의 호스트 = 서버 자신의 clientId.
        if (sender != NetworkManager.LocalClientId)
        {
            Debug.LogWarning($"[TutorialSkip] 거부 — client {sender} 는 호스트가 아니다(구간 스킵은 호스트만).", this);
            return;
        }
        if (_model == null || !_active.Value)
        {
            Debug.LogWarning("[TutorialSkip] 거부 — 튜토리얼 비활성.", this);
            return;
        }

        TutorialSkipController skip = GetComponent<TutorialSkipController>();
        if (skip == null)
        {
            Debug.LogWarning("[TutorialSkip] 거부 — TutorialSkipController 가 아직 붙지 않았다.", this);
            return;
        }
        skip.RunServer(target);
    }

    /// <summary>[서버] 지금 열린 게이트 마스크(모델 원본 — 스킵 계획용).</summary>
    internal int OpenedGateMaskServer => _model != null ? _model.OpenedGateMask : 0;
}
#endif
