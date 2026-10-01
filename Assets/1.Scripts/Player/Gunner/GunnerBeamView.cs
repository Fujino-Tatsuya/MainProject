using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 🔸 거너 임시 레이저 연출 창구 — 민경 VFX 가 들어오면 교체한다. 판정 확인용으로 발사선을 잠깐 그린다.
/// 스킬(PlayerSkillBase)은 RPC 를 가질 수 없으므로 서버 판정 결과를 전 피어에 퍼뜨리는 창구도 여기 둔다.
/// </summary>
public class GunnerBeamView : BaseNetworkBehaviour
{
    public enum Kind { BasicAttack = 0, ChargeLaser = 1 }

    [SerializeField] private float basicDuration = 0.08f;
    [SerializeField] private Color basicColor = new Color(0.6f, 0.9f, 1f, 0.9f);
    [SerializeField] private float chargeDuration = 0.25f;
    [SerializeField] private Color chargeColor = new Color(1f, 0.85f, 0.3f, 0.9f);
    [Tooltip("Q 발사 시 Base 레이어에서 재생할 상태(없으면 건너뜀).")]
    [SerializeField] private string chargeFireStateName = "Gunner_Q_Fire";

    private readonly LineRenderer[] lines = new LineRenderer[2];
    private readonly float[] hideTimes = new float[2];
    private Animator animator;
    private PlayerStateController stateController;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        stateController = GetComponent<PlayerStateController>();
    }

    /// <summary>[서버] Q 발사 — 전 피어에 발사선 + 발사 애니메이션.</summary>
    public void ServerChargeLaserFired(Vector3 origin, Vector3 end, float width)
    {
        if (IsNetworkActive)
            ChargeLaserFiredRpc(origin, end, width);
        else
            PlayChargeLaser(origin, end, width);
    }

    // Reliable — 연출뿐 아니라 Focus → Skill 상태 전환도 싣는다(빠지면 그 피어만 집중 상태에 남는다).
    [Rpc(SendTo.ClientsAndHost)]
    private void ChargeLaserFiredRpc(Vector3 origin, Vector3 end, float width) => PlayChargeLaser(origin, end, width);

    private void PlayChargeLaser(Vector3 origin, Vector3 end, float width)
    {
        // 정신 집중 끝 — 같은 스킬을 유지한 채 Focus → Skill(발사 후 회복). 전 피어가 이 RPC 로 같이 넘어간다.
        stateController?.ChangeSkillPhase(PlayerActionState.Skill);

        ShowLocal(Kind.ChargeLaser, origin, end, width);

        if (animator != null && animator.runtimeAnimatorController != null && !string.IsNullOrEmpty(chargeFireStateName))
        {
            int hash = Animator.StringToHash(chargeFireStateName);
            if (animator.HasState(0, hash))
                animator.CrossFadeInFixedTime(hash, 0.05f, 0);
        }
    }

    /// <summary>[로컬] 발사선을 잠깐 그린다.</summary>
    public void ShowLocal(Kind kind, Vector3 origin, Vector3 end, float width)
    {
        int i = (int)kind;
        float duration = kind == Kind.BasicAttack ? basicDuration : chargeDuration;
        if (duration <= 0f)
            return;

        if (lines[i] == null)
        {
            var go = new GameObject($"GunnerBeamView_{kind}(임시)");
            go.transform.SetParent(transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = kind == Kind.BasicAttack ? basicColor : chargeColor;
            lines[i] = line;
        }

        lines[i].startWidth = lines[i].endWidth = width;
        lines[i].SetPosition(0, origin);
        lines[i].SetPosition(1, end);
        lines[i].enabled = true;
        hideTimes[i] = Time.time + duration;
    }

    private void LateUpdate()
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i] != null && lines[i].enabled && Time.time >= hideTimes[i])
                lines[i].enabled = false;
        }
    }
}
