using ColossalFramework.UI;
using System;
public class RoadsOptionFinePanel : ToolsModifierControl
{
    private void Awake()
    {
        this.Hide();
        NetTool netTool = ToolsModifierControl.GetTool<NetTool>();
        if (netTool != null)
        {
            UITabstrip strip = base.Find<UITabstrip>("ToolMode");
            if (strip != null)
            {
                strip.eventSelectedIndexChanged += delegate(UIComponent sender, int index)
                {
                    netTool.m_mode = (NetTool.Mode)index;
                };
                base.component.eventVisibilityChanged += delegate(UIComponent sender, bool visible)
                {
                    netTool.m_mode = (NetTool.Mode)((!visible) ? 0 : strip.selectedIndex);
                };
            }
            UIMultiStateButton snap = base.Find<UIMultiStateButton>("SnappingToggle");
            if (snap != null)
            {
                snap.activeStateIndex = ((!netTool.m_snap) ? 0 : 1);
                snap.eventActiveStateIndexChanged += delegate(UIComponent sender, int index)
                {
                    netTool.m_snap = (index == 1);
                };
                base.component.eventVisibilityChanged += delegate(UIComponent sender, bool visible)
                {
                    netTool.m_snap = (!visible || snap.activeStateIndex == 1);
                };
            }
        }
        NetToolFine netToolFine = ToolsModifierControl.GetTool<NetToolFine>();
        if (netToolFine != null)
        {
            UITabstrip strip = base.Find<UITabstrip>("ToolMode");
            if (strip != null)
            {
                strip.eventSelectedIndexChanged += delegate(UIComponent sender, int index)
                {
                    netToolFine.m_mode = (NetTool.Mode)index;
                };
                base.component.eventVisibilityChanged += delegate(UIComponent sender, bool visible)
                {
                    netToolFine.m_mode = (NetTool.Mode)((!visible) ? 0 : strip.selectedIndex);
                };
            }
            UIMultiStateButton snap = base.Find<UIMultiStateButton>("SnappingToggle");
            if (snap != null)
            {
                snap.activeStateIndex = ((!netToolFine.m_snap) ? 0 : 1);
                snap.eventActiveStateIndexChanged += delegate(UIComponent sender, int index)
                {
                    netToolFine.m_snap = (index == 1);
                };
                base.component.eventVisibilityChanged += delegate(UIComponent sender, bool visible)
                {
                    netToolFine.m_snap = (!visible || snap.activeStateIndex == 1);
                };
            }
        }
    }
    public void Show()
    {
        base.component.Show();
    }
    public void Hide()
    {
        base.component.Hide();
    }
}
