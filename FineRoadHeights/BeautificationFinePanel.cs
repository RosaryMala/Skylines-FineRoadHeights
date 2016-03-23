using ColossalFramework.UI;
using System;
public sealed class BeautificationFinePanel : GeneratedScrollPanel
{
    public override ItemClass.Service service
    {
        get
        {
            return ItemClass.Service.Beautification;
        }
    }
    protected override void Start()
    {
        base.component.parent.eventVisibilityChanged += delegate(UIComponent sender, bool visible)
        {
            if (!visible)
            {
                this.HidePathsOptionPanel();
            }
        };
        base.Start();
    }
    public override void RefreshPanel()
    {
        base.RefreshPanel();
        base.PopulateAssets((GeneratedScrollPanel.AssetFilter)43);
    }
    protected override void OnButtonClicked(UIComponent comp)
    {
        object objectUserData = comp.objectUserData;
        BuildingInfo buildingInfo = objectUserData as BuildingInfo;
        NetInfo netInfo = objectUserData as NetInfo;
        TreeInfo treeInfo = objectUserData as TreeInfo;
        PropInfo propInfo = objectUserData as PropInfo;
        if (buildingInfo != null)
        {
            BuildingTool buildingTool = ToolsModifierControl.SetTool<BuildingTool>();
            if (buildingTool != null)
            {
                this.HidePathsOptionPanel();
                buildingTool.m_prefab = buildingInfo;
                buildingTool.m_relocate = 0;
            }
        }
        if (netInfo != null)
        {
            NetToolFine netTool = ToolsModifierControl.SetTool<NetToolFine>();
            if (netTool != null)
            {
                this.HidePathsOptionPanel();
                netTool.m_prefab = netInfo;
            }
        }
        if (treeInfo != null)
        {
            TreeTool treeTool = ToolsModifierControl.SetTool<TreeTool>();
            if (treeTool != null)
            {
                this.HidePathsOptionPanel();
  
                treeTool.m_prefab = treeInfo;
                treeTool.m_mode = TreeTool.Mode.Single;
            }
        }
        if (propInfo != null)
        {
            PropTool propTool = ToolsModifierControl.SetTool<PropTool>();
            if (propTool != null)
            {
                this.HidePathsOptionPanel();

                propTool.m_prefab = propInfo;
                propTool.m_mode = PropTool.Mode.Single;
            }
        }
    }
}
