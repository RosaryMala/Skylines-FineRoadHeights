using UnityEngine;

namespace FineRoadHeights
{
    class PanelReplacer:MonoBehaviour
    {
        void Update()
        {
            //FineRoadHeightsLoadingExtension.ReplacePanels();
            NetTool netTool = ToolsModifierControl.GetCurrentTool<NetTool>();
            if (netTool == null)
                return;
            NetToolFine netToolFine = ToolsModifierControl.SetTool<NetToolFine>();
            if (netToolFine == null)
                return;
            netToolFine.m_mode = netTool.m_mode;
            netToolFine.m_placementCursor = netTool.m_placementCursor;
            netToolFine.m_prefab = netTool.m_prefab;
            netToolFine.m_snap = netTool.m_snap;
            netToolFine.m_upgradeCursor = netTool.m_upgradeCursor;

        }
    }
}
