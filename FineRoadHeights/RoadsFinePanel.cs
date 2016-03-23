// Decompiled with JetBrains decompiler
// Type: RoadsPanel
// Assembly: Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 25DD26D5-8AEC-4F0C-B468-EF9C5665764A
// Assembly location: E:\Games\SteamLibrary\SteamApps\common\Cities_Skylines\Cities_Data\Managed\Assembly-CSharp.dll

using ColossalFramework;
using ColossalFramework.UI;
using System;

public sealed class RoadsFinePanel : GeneratedScrollPanel
{
  public override ItemClass.Service service
  {
    get
    {
      return ItemClass.Service.Road;
    }
  }

  public override UIVerticalAlignment buttonsAlignment
  {
    get
    {
      return UIVerticalAlignment.Bottom;
    }
  }

  protected override void Start()
  {
    this.component.parent.eventVisibilityChanged += (PropertyChangedEventHandler<bool>) ((sender, visible) =>
    {
      if (visible)
        return;
        this.HideRoadsOptionPanel();
    });
    base.Start();
  }

  protected override bool IsPlacementRelevant(BuildingInfo info)
  {
    bool flag = true;
    if (this.isMapEditor)
    {
      for (int index = 0; index < info.m_paths.Length; ++index)
        flag &= HelperExtensions.IsFlagSet(info.m_paths[index].m_netInfo.m_availableIn, Singleton<ToolManager>.instance.m_properties.m_mode);
    }
    if (base.IsPlacementRelevant(info))
      return flag;
    return false;
  }

  protected override bool IsServiceValid(NetInfo info)
  {
    if (info.GetService() == this.service)
      return true;
    if (this.isMapEditor)
      return info.GetService() == ItemClass.Service.PublicTransport;
    return false;
  }

  public override void RefreshPanel()
  {
    base.RefreshPanel();
    this.PopulateAssets((GeneratedScrollPanel.AssetFilter) 3, new Comparison<PrefabInfo>(this.ItemsTypeSort));
  }

  protected override void OnButtonClicked(UIComponent comp)
  {
    object objectUserData = comp.objectUserData;
    NetInfo netInfo = objectUserData as NetInfo;
    BuildingInfo buildingInfo = objectUserData as BuildingInfo;
    if ((UnityEngine.Object) netInfo != (UnityEngine.Object) null)
    {
      this.ShowRoadsOptionPanel();
      NetToolFine netTool = ToolsModifierControl.SetTool<NetToolFine>();
      if ((UnityEngine.Object) netTool != (UnityEngine.Object) null)
        netTool.m_prefab = netInfo;
    }
    if (!((UnityEngine.Object) buildingInfo != (UnityEngine.Object) null))
      return;
    this.HideRoadsOptionPanel();
    BuildingTool buildingTool = ToolsModifierControl.SetTool<BuildingTool>();
    if (!((UnityEngine.Object) buildingTool != (UnityEngine.Object) null))
      return;
    buildingTool.m_prefab = buildingInfo;
    buildingTool.m_relocate = 0;
  }
}
