#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 88 Q4 — 무인 QA 가 타이틀에서 멈추지 않게 START 를 대신 누른다(할 일 70: 부팅은 항상 1.TitleScene 을 거친다).
/// 1.TitleScene 이 활성 씬이 되고 한 프레임 지난 뒤 <see cref="DemoSessionDirector.TryHandleTitleStart"/> 를 인스턴스마다 1회 부른다.
/// 하네스(<see cref="QAAutoBootstrap"/>)에 붙어 48 시나리오·88·휴리스틱 에이전트 모드가 모두 같은 경로를 탄다.
/// 근거: Docs/tech/handoff-demo-title-start.md.
/// </summary>
public sealed class QATitleStartKick : MonoBehaviour
{
    private readonly QATitleStartGate _gate = new QATitleStartGate();

    private void Update()
    {
        if (!_gate.Observe(SceneManager.GetActiveScene().name, Time.frameCount))
            return;

        bool handled = DemoSessionDirector.TryHandleTitleStart();
        Debug.Log($"[QA] 타이틀 START 대신 호출 — TryHandleTitleStart={handled}" +
                  (handled ? "" : " (데모 흐름 꺼짐 — 옛 흐름이면 QASessionController 가 로비로 보낸다)"));
        enabled = false;
    }
}

/// <summary>
/// 타이틀 START 1회 호출 판단(순수). 타이틀이 처음 활성으로 보인 프레임은 건너뛰고(타이틀 씬 Start 와 겹치지 않게) 다음 프레임에 한 번만 true.
/// 타이틀을 벗어났다 돌아와도 다시 true 가 되지 않는다(판 끝 로비 복귀·재접속은 타이틀을 거치지 않는다).
/// </summary>
public sealed class QATitleStartGate
{
    public const string TitleScene = "1.TitleScene";

    private int _seenFrame = -1;

    public bool Fired { get; private set; }

    public bool Observe(string activeScene, int frame)
    {
        if (Fired)
            return false;
        if (activeScene != TitleScene)
        {
            _seenFrame = -1;
            return false;
        }
        if (_seenFrame < 0)
        {
            _seenFrame = frame;
            return false;
        }
        if (frame <= _seenFrame)
            return false;
        Fired = true;
        return true;
    }
}
#endif
