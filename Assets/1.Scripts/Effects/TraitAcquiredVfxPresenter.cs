using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 증강(튜토리얼 특성)을 얻은 순간 <b>고른 본인 화면에서만</b> 레벨업 연출을 한 번 낸다(2026-10-22 민경 grill).
/// 각 캐릭터 Variant 의 <c>VFX</c> 에 붙는다(<see cref="StatusEffectVfxPresenter"/> 가 올라가는 <c>VFX/States</c> 의 부모).
///
/// <b>언제.</b> 내 획득 수가 늘어나는 순간 — 직접 고른 것·자동 선택(출구 열림·보스 타이머·전멸 후 재선택)을 가리지 않는다.
/// <see cref="TutorialTraitDirector"/> 가 이미 전 피어에 복제하는 획득 목록을 읽기만 한다 — 그쪽 코드(은희)는 건드리지 않고,
/// 네트워크도 늘지 않는다. 확정된 목록이 내 화면에 도착하는 순간이라 선택 창이 닫히는 순간과 같다.
///
/// <b>기준값.</b> 연결된 순간의 획득 수를 기준으로 잡는다 — 씬 진입·늦은 접속 때 이미 가진 증강으로 터지지 않게.
/// 전멸로 줄어들면 기준도 같이 내려가서 재선택 때 다시 터진다.
///
/// <b>몸을 따라간다.</b> 원샷 연출이지만 <see cref="EffectManager.PlayLooping"/> 로 플레이어 루트를 추종시키고 곧바로 해제한다 —
/// 레벨업 프리팹엔 루프 시스템이 없어 해제해도 끊기지 않고, 엔트리 outroDuration 동안 계속 따라간다.
/// (일반 원샷 <c>Play</c> 는 위치 고정이라 선택 직후 걸어가면 뒤에 남는다.)
/// </summary>
[DisallowMultipleComponent]
public class TraitAcquiredVfxPresenter : MonoBehaviour
{
    [Tooltip("증강 획득 연출. Level_up_Entry — outroDuration 이 연출 길이보다 길어야 끝까지 따라간다")]
    [SerializeField] EffectEntry effect;

    [SerializeField, Min(0.01f)] float scale = 1f;

    readonly List<int> _acquired = new List<int>();
    NetworkObject _player;
    TutorialTraitDirector _director;
    int _baseline;
    bool _dirty;

    void Awake() => _player = GetComponentInParent<NetworkObject>();

    void OnDisable() => Bind(null);

    void Update()
    {
        // 디렉터는 맵 씬에만 있다 — 플레이어보다 늦게 생기거나 씬과 함께 사라진다.
        TutorialTraitDirector current = TutorialTraitDirector.Instance;
        if (current != _director) Bind(current);

        if (!_dirty || _director == null) return;
        _dirty = false;

        int count = AcquiredCount();
        if (count > _baseline && IsLocalPlayer()) Play();
        _baseline = count;
    }

    void Bind(TutorialTraitDirector director)
    {
        if (_director != null) _director.ClientStateChanged -= OnStateChanged;
        _director = director;
        _dirty = false;
        if (_director == null) return;

        _director.ClientStateChanged += OnStateChanged;
        _baseline = AcquiredCount();
    }

    // 한 프레임에 여러 번 올 수 있다 — 표시만 해 두고 Update 에서 한 번 센다.
    void OnStateChanged() => _dirty = true;

    int AcquiredCount()
    {
        if (_director == null || _player == null || !_player.IsSpawned) return 0;
        _director.GetAcquired(_player.OwnerClientId, _acquired);
        return _acquired.Count;
    }

    bool IsLocalPlayer() => _player != null && _player.IsSpawned && _player.IsOwner;

    void Play()
    {
        if (effect == null || !EffectManager.TryGet(out EffectManager effects, this)) return;

        EffectHandle handle = effects.PlayLooping(effect, _player.transform, Vector3.zero, scale);
        effects.Release(handle);
    }
}
