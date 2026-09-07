using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 플레이어 공격에 부서지는 상자 더미. <b>프롭 프리팹 루트</b>에 붙는다.
///
/// <b>왜 Unit이 아닌가.</b> <see cref="Unit"/>은 <c>NetworkBehaviour</c>라 상자마다 NetworkObject와
/// NetworkVariable 4개(hp·maxHp·shield·hasShield)가 생긴다. 맵에 상자가 100개면 그게 400개인데,
/// <b>상자가 동기화해야 할 상태는 없다</b> — "부서졌다"는 사건 하나뿐이다.
/// 기존 데미지 파이프라인은 Unit 없이도 돈다: <c>BaseAttack</c>이 Hurtbox를 찾으면 Unit을 거치지 않고
/// <c>Hurtbox.ReceiveAttack</c>으로 가고, 거기서 <see cref="IAttackReceiver"/>로 폴백한다.
///
/// <b>왜 더미 전체가 한 단위인가.</b> 프롭 프리팹의 상자들은 Rigidbody 없는 <b>정적 지오메트리</b>다.
/// 아래 상자만 끄면 위 상자가 허공에 뜬다. 게다가 3단 더미는 반듯한 탑이 아니라
/// 바닥 2개 + 걸쳐진 1개라 "누가 누구를 받치는가"가 단일 관계로 정해지지 않는다.
/// 아티스트가 "상자 더미"라는 하나의 프롭으로 저작한 단위를 그대로 파괴 단위로 삼는다.
///
/// ⚠️ <b>연출을 IsServer 안에서 재생하지 말 것.</b> 판정은 서버, 재생은 전 피어다
/// (이 레포의 반복 버그 — 호스트에서만 보인다).
/// </summary>
[DisallowMultipleComponent]
public class BreakableCrate : MonoBehaviour, IAttackReceiver
{
    [Header("내구도")]
    [Tooltip("서버에서만 쓰는 값이라 동기화하지 않는다. 0 이하가 되면 부서진다")]
    [SerializeField, Min(1)] int hp = 1;

    [Header("파괴 연출")]
    [Tooltip("파편 버스트 엔트리. 아래 pieces 각각의 위치에서 한 번씩 재생된다")]
    [SerializeField] EffectEntry burstEntry;

    [Tooltip("더미를 이루는 상자들. 비워두면 자식 MeshRenderer에서 자동 수집한다")]
    [SerializeField] Transform[] pieces;

    [Tooltip("상자마다 버스트를 어긋나게 재생하는 간격(초). 0이면 전부 동시에 터진다.\n" +
             "약간 어긋나야 '한 방에 뿅'이 아니라 '우르르 무너짐'으로 보인다")]
    [SerializeField, Min(0f)] float pieceStagger = 0.05f;

    [Header("식별")]
    [Tooltip("씬에 직접 배치한 상자의 ID. 'Tools > Crates > 씬의 상자에 ID 부여'가 채운다.\n" +
             "생성 맵의 상자는 MapContentSpawner가 런타임에 부여하므로 0으로 둔다.\n" +
             "두 ID 공간이 겹치지 않도록 저작 ID는 음수, 생성 ID는 양수를 쓴다")]
    [SerializeField] int authoredId;

    [Header("이벤트")]
    [Tooltip("파괴 연출 시점에 호출된다. 인자는 더미의 월드 좌표.\n\n" +
             "⚠️ 모든 피어에서 각자 불린다 — 소리·추가 이펙트 같은 연출만 연결할 것.\n" +
             "여기에 드롭·보상·점수를 걸면 인원수만큼 중복 실행된다(그건 onBrokenServer).")]
    public UnityEvent<Vector3> onBrokenLocal;

    [Tooltip("서버에서 파괴가 확정된 순간 1회 호출된다. 인자는 더미의 월드 좌표.\n\n" +
             "드롭 아이템·보상·처치 카운트처럼 게임플레이에 영향을 주는 것을 여기 연결한다.\n" +
             "연출을 여기 걸면 호스트에서만 보인다 — 이 레포의 반복 버그다.")]
    public UnityEvent<Vector3> onBrokenServer;

    int _crateId;
    int _hpLeft;
    bool _brokenServer;   // 서버 판정 래치 — 한 프레임에 여러 번 맞아도 RPC는 한 번
    bool _brokenLocal;    // 로컬 연출 래치 — RPC 재전송·호스트 중복 방어

    /// <summary>스폰 순번으로 부여된 ID. <see cref="MapContentSpawner"/>가 채운다.</summary>
    public int CrateId => _crateId;

    void Awake()
    {
        _hpLeft = hp;
        CollectPieces();

        // 씬에 직접 배치된 상자(BossScene 등 테스트 씬)는 스포너를 거치지 않으므로 스스로 등록한다.
        // 생성 맵 경로는 MapContentSpawner의 AssignId가 이 값을 덮어쓴다.
        if (authoredId != 0) AssignId(authoredId);
    }

    void OnDestroy() => CrateRegistry.Unregister(this);

    /// <summary>
    /// [<see cref="MapContentSpawner"/>] 스폰 순번 ID 부여 + 등록.
    /// 모든 피어가 같은 시드로 같은 순서로 맵을 만들므로 이 값은 자동으로 일치한다.
    /// </summary>
    public void AssignId(int id)
    {
        _crateId = id;
        CrateRegistry.Register(this);
    }

    #region 서버 판정

    /// <summary>
    /// 공격 수신. <c>BaseAttack</c>이 <b>서버에서만</b> 부르므로 여기서 별도 가드를 두지 않는다
    /// (<c>BaseAttack.TryResolveHit</c>의 <c>if (!IsServer) return false</c>).
    ///
    /// <b>한 번의 휘두르기에 여러 번 불릴 수 있다.</b> <c>Hurtbox</c>는 상자마다 하나씩이고
    /// (콜라이더와 같은 오브젝트여야 하므로), <c>PlayerDefaultAttack</c>은 Hurtbox 단위로 중복을
    /// 거른다. 3단 더미를 한 번에 쓸면 여기가 3번 불린다 — <see cref="_brokenServer"/> 래치가
    /// RPC를 한 번으로 묶지만, <b>hp는 그만큼 여러 번 깎인다.</b>
    /// hp를 1보다 크게 줄 거라면 이 점을 감안할 것.
    /// </summary>
    public bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        if (_brokenServer) return false;

        _hpLeft -= Mathf.Max(0, attackInfo.damage);
        if (_hpLeft > 0) return true;

        _brokenServer = true;

        // 게임플레이 훅 — 서버에서 1회. 드롭·보상이 여기 걸린다.
        // BreakLocal보다 먼저 부른다: 폴백 경로에서 BreakLocal이 곧바로 오브젝트를 끄기 때문이다.
        onBrokenServer?.Invoke(transform.position);

        // ID가 없으면 전파할 방법이 없다. 조용히 넘기면 "때려도 안 부서진다"로만 드러나
        // 원인을 찾는 데 오래 걸린다(실제로 겪었다).
        if (_crateId == 0)
        {
            Edit.LogError(
                $"[Crate] '{name}'에 ID가 없어 파괴를 전파할 수 없다 — 이 피어에서만 부순다.\n" +
                "생성 맵의 상자는 MapContentSpawner가 부여하고, 씬에 직접 배치한 상자는 " +
                "'Tools > Crates > 씬의 상자에 ID 부여'를 돌려야 한다.", this);

            BreakLocal();
            return true;
        }

        CrateBreakBroadcaster.ServerBreak(_crateId);
        return true;
    }

    #endregion

    #region 전 피어 연출

    /// <summary>
    /// [RPC 수신부] 로컬로 부순다. <b>모든 피어에서 각자 불린다</b>.
    /// 파편은 피어마다 다르게 흩어지지만 연출이라 무방하다 — 그래서 파편에 콜라이더를 달지 않는다.
    /// </summary>
    public void BreakLocal()
    {
        if (_brokenLocal) return;
        _brokenLocal = true;

        PlayBursts();

        // ⚠️ SetActive(false)보다 먼저 부른다. 이 뒤로는 이 오브젝트가 비활성이라
        //    리스너가 여기서 StartCoroutine을 걸면 즉시 죽는다 — 지연이 필요하면
        //    PlayBursts처럼 EffectManager 쪽에 태울 것.
        onBrokenLocal?.Invoke(transform.position);

        // 즉시 숨긴다. 버스트가 터지는데 멀쩡한 더미가 그 자리에 남아 있으면 안 된다.
        gameObject.SetActive(false);
    }

    void PlayBursts()
    {
        if (burstEntry == null || EffectManager.Instance == null) return;

        for (int i = 0; i < pieces.Length; i++)
        {
            if (pieces[i] == null) continue;

            Vector3 position = pieces[i].position;
            Quaternion rotation = pieces[i].rotation;

            if (pieceStagger <= 0f || i == 0)
            {
                EffectManager.Instance.Play(burstEntry, position, rotation);
                continue;
            }

            // ⚠️ 이 오브젝트는 곧 SetActive(false)가 되므로 자기 코루틴을 쓸 수 없다.
            //    매니저 쪽에 태운다.
            EffectManager.Instance.StartCoroutine(
                PlayAfter(burstEntry, position, rotation, i * pieceStagger));
        }
    }

    static IEnumerator PlayAfter(EffectEntry entry, Vector3 position, Quaternion rotation, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (EffectManager.Instance != null)
            EffectManager.Instance.Play(entry, position, rotation);
    }

    #endregion

    void CollectPieces()
    {
        if (pieces != null && pieces.Length > 0) return;

        MeshRenderer[] found = GetComponentsInChildren<MeshRenderer>(true);
        pieces = new Transform[found.Length];
        for (int i = 0; i < found.Length; i++) pieces[i] = found[i].transform;
    }
}
