using UnityEngine;
using INab.Common;
using System.Collections.Generic;


namespace INab.Demo
{
    public class ShowcaseAutoPlay : MonoBehaviour
    {
        public List<GameObject> trailCategories = new List<GameObject>();
        private int selectedClipIndex = 0;

        private void SetActiveCategory()
        {
            foreach (var item in trailCategories)
            {
                item.SetActive(false);
            }
            trailCategories[selectedClipIndex].SetActive(true);
        }

        private void Start()
        {
            SetActiveCategory();
        }

        void Update()
        {

            if (Input.GetKeyDown(KeyCode.Q))
            {
                selectedClipIndex = (selectedClipIndex - 1 + trailCategories.Count) % trailCategories.Count;
                SetActiveCategory();
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                selectedClipIndex = (selectedClipIndex + 1) % trailCategories.Count;
                SetActiveCategory();
            }
        }
    }
}