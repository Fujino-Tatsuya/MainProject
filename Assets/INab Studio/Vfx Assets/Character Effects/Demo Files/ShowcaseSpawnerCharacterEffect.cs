using UnityEngine;
using System.Collections.Generic;
using INab.VFXAssets;
using UnityEngine.VFX;



#if UNITY_EDITOR
using UnityEditor;
#endif

namespace INab.Demo
{
    [ExecuteInEditMode]
    public class ShowcaseSpawnerCharacterEffect : MonoBehaviour
    {
        public List<GameObject> effectPrefabs = new List<GameObject>();
        public GameObject meshesToSpawn;

        public float stepDistance = 2f;
        public Transform parentTransform;

        public Vector3 direction;
        public bool useCustomPositionOffset = false;
        public float positionOffset;
        // todo do i need to turn on innital event to on play?

        [SerializeField]private List<GameObject> spawnedObjects = new List<GameObject>();

        public void OnEnable()
        {
            PlayAll();
        }

        public void SpawnPrefabs()
        {
            for (int i = 0; i < effectPrefabs.Count; i++)
            {
                GameObject spawned = Instantiate(meshesToSpawn, new Vector3(i*stepDistance,0,0),Quaternion.Euler(0,180,0), parentTransform);
                spawned.name = effectPrefabs[i].name + "";

                //spawned.GetComponentInChildren<TextMeshPro>().text = effectPrefabs[i].name;

                var characters = spawned.GetComponentsInChildren<CharacterEffect>();
                characters[0].SetNewEffectPrefab(effectPrefabs[i]);
                var vfx0 = characters[0].GetComponentInChildren<VisualEffect>();
                if(vfx0.HasFloat("Position Offset") && useCustomPositionOffset) vfx0.SetFloat("Position Offset", positionOffset);
                if (vfx0.HasVector3("Effect Direction_direction")) vfx0.SetVector3("Effect Direction_direction", direction);

                vfx0.initialEventName = "OnPlay";

                spawnedObjects.Add(spawned);
                spawned.SetActive(true);
            }
        }
        public void DestroyPrefabs()
        {
            foreach (var obj in spawnedObjects)
            {
                if (obj != null)
                {
                    DestroyImmediate(obj);
                }
            }
            spawnedObjects.Clear();
        }

        public void PlayAll()
        {
            foreach (var obj in spawnedObjects)
            {
                if (obj != null)
                {
                    foreach (var item in obj.GetComponentsInChildren<CharacterEffect>())
                    {
                        item.StartEffect();
                    }
                }
            }
        }

        public void StopAll()
        {
            foreach (var obj in spawnedObjects)
            {
                if (obj != null)
                {
                    foreach (var item in obj.GetComponentsInChildren<CharacterEffect>())
                    {
                        item.StopEffect();
                    }
                }
            }
        }

    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ShowcaseSpawnerCharacterEffect))]
    public class ShowcaseSpawnerCharacterEffectEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // Draw the default inspector
            DrawDefaultInspector();

            ShowcaseSpawnerCharacterEffect spawner = (ShowcaseSpawnerCharacterEffect)target;

            EditorGUILayout.Space();

            if (GUILayout.Button("Play All Effects"))
            {
                spawner.PlayAll();
            }

            if (GUILayout.Button("Stop All Effects"))
            {
                spawner.StopAll();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Spawn New Prefabs"))
            {
                spawner.DestroyPrefabs();
                spawner.SpawnPrefabs();
            }

            if (GUILayout.Button("Destroy Prefabs"))
            {
                spawner.DestroyPrefabs();
            }

        }
    }
#endif
}