using UnityEngine;

namespace INab.VFXAssets
{

    [ExecuteAlways]
    public class CharacterEffect : UniformMeshSample
    {
        public override string DefaultPrefabPath => "Assets/INab Studio/Vfx Assets/Character Effects/Effect Prefabs/";

        public EffectState effectState = EffectState.Off;

        public void PlayEffect_CharacterEffect()
        {
            SendPlayEvent();
            effectState = EffectState.On;

        }

        public void StopEffect_CharacterEffect()
        {
            SendStopEvent();
            effectState = EffectState.Off;
        }

    }
}
