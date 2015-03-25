using UnityEngine;

namespace FineRoadHeights
{
    class PanelReplacer:MonoBehaviour
    {
        void Update()
        {
            //replace all roads panels in the game with our own, that call modified NetTool
            RoadsPanel[] roadsPanels = UnityEngine.Object.FindObjectsOfType<RoadsPanel>();
            foreach (var roadsPanel in roadsPanels)
            {
                GameObject roadsPanelObject = roadsPanel.gameObject;
                RoadsFinePanel roadsFinePanel = roadsPanelObject.AddComponent<RoadsFinePanel>();
                roadsFinePanel.m_DefaultInfoTooltipAtlas = roadsPanel.m_DefaultInfoTooltipAtlas;
                roadsFinePanel.m_OptionsBar = roadsPanel.m_OptionsBar;
                Object.Destroy(roadsPanel);
            }
            //Do the same with public transport panels
            PublicTransportPanel[] transportPanels = UnityEngine.Object.FindObjectsOfType<PublicTransportPanel>();
            foreach (var transportPanel in transportPanels)
            {
                GameObject transportPanelObject = transportPanel.gameObject;
                PublicTransportFinePanel transportFinePanel = transportPanelObject.AddComponent<PublicTransportFinePanel>();
                transportFinePanel.m_DefaultInfoTooltipAtlas = transportPanel.m_DefaultInfoTooltipAtlas;
                transportFinePanel.m_OptionsBar = transportPanel.m_OptionsBar;
                Object.Destroy(transportPanel);
            }
        }
    }
}
