using ICities;
using UnityEngine;
using ColossalFramework;
using System.Reflection;

namespace FineRoadHeights
{
    public class FineRoadHeightsLoadingExtension : LoadingExtensionBase
    {
        public static void ReplacePanels()
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
            //Do the same with beautification panels
            BeautificationPanel[] beautificationPanels = UnityEngine.Object.FindObjectsOfType<BeautificationPanel>();
            foreach (var beautificationPanel in beautificationPanels)
            {
                GameObject beautificationPanelObject = beautificationPanel.gameObject;
                BeautificationFinePanel beautificationFinePanel = beautificationPanelObject.AddComponent<BeautificationFinePanel>();
                beautificationFinePanel.m_DefaultInfoTooltipAtlas = beautificationPanel.m_DefaultInfoTooltipAtlas;
                beautificationFinePanel.m_OptionsBar = beautificationPanel.m_OptionsBar;
                Object.Destroy(beautificationPanel);
            }
        }
        public override void OnLevelLoaded(LoadMode mode)
        {
            ReplacePanels();

            //And finally with the road options panel.
            RoadsOptionPanel[] roadsOptionPanels = UnityEngine.Object.FindObjectsOfType<RoadsOptionPanel>();
            foreach (RoadsOptionPanel roadsOptionPanel in roadsOptionPanels)
            {
                GameObject roadsOptionsPanelObject = roadsOptionPanel.gameObject;
                RoadsOptionFinePanel roadsOptionFinePanel = roadsOptionsPanelObject.AddComponent<RoadsOptionFinePanel>();
                Object.Destroy(roadsOptionPanel);
            }
            //Find the tool controller, re-make its tool list, and force a re-make of the ToolsModifierControl tool list.
            ToolController toolController = UnityEngine.Object.FindObjectOfType<ToolController>();
            if (toolController != null)
            {
                NetToolFine netToolFine = toolController.gameObject.AddComponent<NetToolFine>();
                NetTool netTool = toolController.gameObject.GetComponent<NetTool>();
                FieldInfo toolControllerField = typeof(ToolController).GetField("m_tools", BindingFlags.Instance | BindingFlags.NonPublic);
                if (toolControllerField != null)
                    toolControllerField.SetValue(toolController, toolController.GetComponents<ToolBase>());
                FieldInfo toolModifierDictionary = typeof(ToolsModifierControl).GetField("m_Tools", BindingFlags.Static | BindingFlags.NonPublic);
                if (toolModifierDictionary != null)
                    toolModifierDictionary.SetValue(null, null);
            }

            if(mode == LoadMode.LoadAsset || mode == LoadMode.NewAsset) // || mode == LoadMode.LoadMap || mode == LoadMode.NewMap)
            {
                GameObject panelReplacer = UnityEngine.Object.Instantiate(new GameObject());
                panelReplacer.AddComponent<PanelReplacer>();
                panelReplacer.name = "PanelReplacer";
            }
        }
    }
}
