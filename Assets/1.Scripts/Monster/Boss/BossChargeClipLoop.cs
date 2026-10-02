using UnityEngine;

/// <summary>
/// 23호 차징 클립 <b>구간 반복</b> — 처음 1회는 0 프레임부터 재생하고, <c>chargeLoopEndFrame</c> 에 닿으면
/// <c>chargeLoopStartFrame</c> 으로 되돌려 그 구간만 반복한다. 팔 모으는 동작이 처음에만 나오게(팀장 10-02).
///
/// 왜 코드인가: 클립(Boss_23_charging)은 SVN 아트 FBX 안에 있고 Loop Time 이 켜져 있어 통째로 반복된다.
/// 클립을 나누려면 아트 임포트 설정을 고쳐야 하므로, 재생 위치만 되돌린다(아트 무수정).
/// 애니메이션은 피어마다 로컬로 돈다 — 23호가 모든 피어에 붙인다(차징 상태 진입은 이미 ClientRpc 로 전 피어 재생).
/// </summary>
[DisallowMultipleComponent]
public sealed class BossChargeClipLoop : MonoBehaviour
{
    Animator _animator;
    int _stateHash;
    int _startFrame = -1, _endFrame = -1;

    public void Init(Animator animator, string stateName, int startFrame, int endFrame)
    {
        _animator = animator;
        _stateHash = string.IsNullOrEmpty(stateName) ? 0 : Animator.StringToHash(stateName);
        _startFrame = startFrame;
        _endFrame = endFrame;
        enabled = _animator != null && _stateHash != 0 && startFrame >= 0 && endFrame > startFrame;
    }

    void Update()
    {
        if (_animator == null || !_animator.isActiveAndEnabled) return;

        for (int layer = 0; layer < _animator.layerCount; layer++)
        {
            AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(layer);
            if (info.shortNameHash != _stateHash) continue;
            // 크로스페이드로 들어오는 중엔 손대지 않는다 — 전이가 끝난 뒤부터 센다.
            if (_animator.IsInTransition(layer)) return;

            AnimatorClipInfo[] clips = _animator.GetCurrentAnimatorClipInfo(layer);
            if (clips.Length == 0 || clips[0].clip == null) return;
            AnimationClip clip = clips[0].clip;
            if (clip.length <= 0f) return;

            float fps = clip.frameRate > 0f ? clip.frameRate : 30f;
            float endSec = Mathf.Min(_endFrame / fps, clip.length);
            float startSec = Mathf.Min(_startFrame / fps, endSec);

            // normalizedTime 은 Loop Time 클립에서 1 을 넘어 계속 커진다 — 시작부터 지난 시간(초)으로 본다.
            float t = info.normalizedTime * clip.length;
            if (t < endSec) return;

            // 넘친 만큼 이어 붙여 프레임 단위로 끊기지 않게 한다.
            float overshoot = (t - endSec) % Mathf.Max(0.0001f, endSec - startSec);
            _animator.Play(_stateHash, layer, (startSec + overshoot) / clip.length);
            return;
        }
    }
}
