/// <summary>
/// 디렉터가 "이 연출을 틀어라" 고 부르는 실제 재생 창구(할 일 118). 에셋·소켓은 구현 쪽에 그대로 있다.
/// 재생 여부(칸 on/off)는 디렉터 채널이 이미 정했다 — 구현은 자기 상태(스폰·칸 연결)만 본다.
/// </summary>
public interface IInterruptCueSink
{
    void PlayCue(InterruptProductionCue cue, in InterruptProductionEvent e);
}
