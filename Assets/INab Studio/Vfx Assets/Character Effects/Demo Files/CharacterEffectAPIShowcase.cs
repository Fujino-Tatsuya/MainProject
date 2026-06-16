using UnityEngine;
using INab.VFXAssets;

namespace INab.Demo
{
    /// <summary>
    /// Demonstrates how to use the Character Effect API.
    /// </summary>
    public class CharacterEffectAPIShowcase : MonoBehaviour
    {
        [Header("Effect Effect Reference")]
        public CharacterEffect characterEffect;

        [Header("Effect Prefabs")]
        public GameObject characterPrefab1;
        public GameObject characterPrefab2;

        /// <summary>
        /// Starts the character effect.
        /// </summary>
        public void StartEffect()
        {
            if (characterEffect != null)
                characterEffect.StartEffect();
        }

        /// <summary>
        /// Stops the character effect.
        /// </summary>
        public void EndEffect()
        {
            if (characterEffect != null)
                characterEffect.StopEffect();
        }


        /// <summary>
        /// Switches the trail effect to a new prefab at runtime.
        /// </summary>
        /// <param name="newPrefab">The new trail prefab to apply.</param>
        public void SetNewEffectPrefab(GameObject newPrefab)
        {
            if (characterEffect != null && newPrefab != null)
                characterEffect.SetNewEffectPrefab(newPrefab);
        }

        /// <summary>
        /// Applies the first demo trail prefab.
        /// </summary>
        public void SetEffectPrefab1()
        {
            SetNewEffectPrefab(characterPrefab1);
            StartEffect();
        }

        /// <summary>
        /// Applies the second demo trail prefab.
        /// </summary>
        public void SetEffectPrefab2()
        {
            SetNewEffectPrefab(characterPrefab2);
            StartEffect();
        }
    }
}
