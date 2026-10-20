using UnityEngine;

/// <summary>
/// 로비 수치 — PLAN-48 §4.6. <see cref="LobbySession"/>(카운트다운·교체 쿨다운)과 C10 상호작용(반경)이 읽는다.
/// 아직 데이터 테이블(xlsx) 대상이 아니다([DataTableSheet] 없음). 시트로 옮길 땐 열 = countdownSeconds · interactRadius · characterSwapCooldownSeconds.
/// </summary>
[CreateAssetMenu(fileName = "LobbyRules", menuName = "Lobby/Lobby Rules")]
public sealed class LobbyRules : ScriptableObject
{
    [Tooltip("전원 준비 후 게임 시작까지(초, 서버 ServerTime 기준).")]
    [Min(0f)]
    [SerializeField] private float countdownSeconds = 5f;

    [Tooltip("준비·캐릭터 선택 오브젝트 상호작용 반경(m). C10 LobbyInteractor·서버 거리 재검증이 쓴다.")]
    [Min(0f)]
    [SerializeField] private float interactRadius = 2f;

    [Tooltip("캐릭터 교체 요청 사이 최소 간격(초). 서버가 거부한다.")]
    [Min(0f)]
    [SerializeField] private float characterSwapCooldownSeconds = 0.5f;

    public float CountdownSeconds => countdownSeconds;
    public float InteractRadius => interactRadius;
    public float CharacterSwapCooldownSeconds => characterSwapCooldownSeconds;
}
