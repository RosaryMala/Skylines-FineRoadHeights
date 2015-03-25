using UnityEngine;

namespace FineRoadHeights
{
    class PanelReplacer:MonoBehaviour
    {
        void Update()
        {
            FineRoadHeightsLoadingExtension.ReplacePanels();
        }
    }
}
