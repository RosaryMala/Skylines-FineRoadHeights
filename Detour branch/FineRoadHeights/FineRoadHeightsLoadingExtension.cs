using ICities;
using System;
using System.Reflection;

namespace FineRoadHeights
{
    public class FineRoadHeightsLoadingExtension : LoadingExtensionBase
    {
        public override void OnLevelLoaded(LoadMode mode)
        {
            RedirectionHelper.RedirectCalls(typeof(NetTool), typeof(FakeNetTool), "GetElevation", true);
            RedirectionHelper.RedirectCalls(typeof(NetTool), typeof(FakeNetTool), "CreateNodeImpl", new Type[] { typeof(bool) }, true);
            RedirectionHelper.RedirectCalls(typeof(NetTool), typeof(FakeNetTool), "CreateNodeImpl",
                new Type[] {
					typeof(NetInfo),
					typeof(bool),
					typeof(bool),
					typeof(NetTool.ControlPoint),
					typeof(NetTool.ControlPoint),
					typeof(NetTool.ControlPoint)
				}, true);
            RedirectionHelper.RedirectCalls(typeof(NetTool), typeof(FakeNetTool), "CreateNode",
                new Type[] {
					typeof(NetInfo),
					typeof(NetTool.ControlPoint),
					typeof(NetTool.ControlPoint),
					typeof(NetTool.ControlPoint),
					typeof(FastList<NetTool.NodePosition>),
					typeof(int),
					typeof(bool),
					typeof(bool),
					typeof(bool),
					typeof(bool),
					typeof(bool),
					typeof(bool),
					typeof(ushort),
					typeof(ushort).MakeByRefType(),
					typeof(ushort).MakeByRefType(),
					typeof(ushort).MakeByRefType(),
					typeof(int).MakeByRefType(),
					typeof(int).MakeByRefType(),
				}, false);
            RedirectionHelper.RedirectCalls(typeof(NetTool), typeof(FakeNetTool), "OnToolGUI", true);
        }
    }
}
