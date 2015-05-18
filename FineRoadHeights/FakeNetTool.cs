using System;
using System.Threading;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using ColossalFramework;
using ColossalFramework.Math;

namespace FineRoadHeights
{
	public class FakeNetTool
	{
		static BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
		static BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
		static float terrainStep = 3f;

		public float GetElevation (NetInfo prefab)
		{
			NetTool netTool = ToolsModifierControl.GetTool<NetTool>();
			FieldInfo m_elevation = typeof(NetTool).GetField ("m_elevation", PrivateInstance);

			if (prefab == null)
			{
				return 0f;
			}
			int min;
			int max;
			prefab.m_netAI.GetElevationLimits(out min, out max);
			if (min == max)
			{
				return 0f;
			}
			min = Mathf.RoundToInt (min * 12f / terrainStep);
			max = Mathf.RoundToInt (max * 12f / terrainStep);
			return (float)Mathf.Clamp ((int)m_elevation.GetValue (netTool), min, max) * terrainStep;
		}
		public bool CreateNodeImpl (bool switchDirection)
		{
			NetTool netTool = ToolsModifierControl.GetTool<NetTool>();
			FieldInfo m_elevation = typeof(NetTool).GetField ("m_elevation", PrivateInstance);
			FieldInfo m_switchingDir = typeof(NetTool).GetField ("m_switchingDir", PrivateInstance);
			FieldInfo m_controlPoints = typeof(NetTool).GetField ("m_controlPoints", PrivateInstance);
			FieldInfo m_controlPointCount = typeof(NetTool).GetField ("m_controlPointCount", PrivateInstance);
			FieldInfo m_upgrading = typeof(NetTool).GetField ("m_upgrading", PrivateInstance);
			var controlPoints = (NetTool.ControlPoint[])m_controlPoints.GetValue(netTool);
			var controlPointCount = (int)m_controlPointCount.GetValue(netTool);

			NetInfo prefab = netTool.m_prefab;
			if (prefab != null)
			{
				if (netTool.m_mode == NetTool.Mode.Upgrade && controlPointCount < 2)
				{
					prefab.m_netAI.UpgradeFailed();
				}
				else
				{
					if (netTool.m_mode == NetTool.Mode.Straight && controlPointCount < 1)
					{
						m_elevation.SetValue(netTool, Mathf.Max(0, Mathf.RoundToInt(controlPoints[controlPointCount].m_elevation / terrainStep)));
						controlPoints[controlPointCount + 1] = controlPoints[controlPointCount];
						controlPoints[controlPointCount + 1].m_node = 0;
						controlPoints[controlPointCount + 1].m_segment = 0;
						m_controlPoints.SetValue (netTool, controlPoints);
						m_controlPointCount.SetValue (netTool, controlPointCount + 1);
						return true;
					}
					if ((netTool.m_mode == NetTool.Mode.Curved || netTool.m_mode == NetTool.Mode.Freeform) && controlPointCount < 2 && (controlPointCount == 0 || (controlPoints[1].m_node == 0 && controlPoints[1].m_segment == 0)))
					{
						m_elevation.SetValue(netTool, Mathf.Max(0, Mathf.RoundToInt(controlPoints[controlPointCount].m_elevation / terrainStep)));
						controlPoints[controlPointCount + 1] = controlPoints[controlPointCount];
						controlPoints[controlPointCount + 1].m_node = 0;
						controlPoints[controlPointCount + 1].m_segment = 0;
						m_controlPoints.SetValue (netTool, controlPoints);
						m_controlPointCount.SetValue (netTool, controlPointCount + 1);
						return true;
					}
					bool needMoney = (Singleton<ToolManager>.instance.m_properties.m_mode & ItemClass.Availability.Game) != ItemClass.Availability.None;
					if (netTool.m_mode == NetTool.Mode.Upgrade)
					{
						m_upgrading.SetValue (netTool, true);
						m_switchingDir.SetValue (netTool, switchDirection);
					}
					NetTool.ControlPoint controlPoint;
					NetTool.ControlPoint controlPoint2;
					NetTool.ControlPoint controlPoint3;
					if (controlPointCount == 1)
					{
						controlPoint = controlPoints[0];
						controlPoint2 = controlPoints[1];
						controlPoint3 = controlPoints[1];
						controlPoint3.m_node = 0;
						controlPoint3.m_segment = 0;
						controlPoint3.m_position = (controlPoints[0].m_position + controlPoints[1].m_position) * 0.5f;
						controlPoint3.m_elevation = (controlPoints[0].m_elevation + controlPoints[1].m_elevation) * 0.5f;
					}
					else
					{
						controlPoint = controlPoints[0];
						controlPoint3 = controlPoints[1];
						controlPoint2 = controlPoints[2];
					}
					NetTool.ControlPoint startPoint = controlPoint;
					NetTool.ControlPoint middlePoint = controlPoint3;
					NetTool.ControlPoint endPoint = controlPoint2;
					var arguments = new object[]{ prefab, startPoint, middlePoint, endPoint };
					var secondaryControlPoints = (bool)typeof(NetTool).GetMethod ("GetSecondaryControlPoints", PrivateStatic).Invoke (netTool, arguments);
					startPoint = (NetTool.ControlPoint)arguments [1];
					middlePoint = (NetTool.ControlPoint)arguments [2];
					endPoint = (NetTool.ControlPoint)arguments [3];
					if (CreateNodeImpl(prefab, needMoney, switchDirection, controlPoint, controlPoint3, controlPoint2))
					{
						if (secondaryControlPoints)
						{
							CreateNodeImpl(prefab, needMoney, switchDirection, startPoint, middlePoint, endPoint);
						}
						return true;
					}
				}
			}
			return false;
		}
		public bool CreateNodeImpl(NetInfo info, bool needMoney, bool switchDirection, NetTool.ControlPoint startPoint, NetTool.ControlPoint middlePoint, NetTool.ControlPoint endPoint)
		{
			NetTool netTool = ToolsModifierControl.GetTool<NetTool>();
			FieldInfo m_elevation = typeof(NetTool).GetField ("m_elevation", PrivateInstance);
			FieldInfo m_controlPoints = typeof(NetTool).GetField ("m_controlPoints", PrivateInstance);
			FieldInfo m_controlPointCount = typeof(NetTool).GetField ("m_controlPointCount", PrivateInstance);
			FieldInfo m_upgrading = typeof(NetTool).GetField ("m_upgrading", PrivateInstance);
			FieldInfo m_upgradedSegments = typeof(NetTool).GetField ("m_upgradedSegments", PrivateInstance);
			FieldInfo m_bulldozerTool = typeof(NetTool).GetField ("m_bulldozerTool", PrivateInstance);

			var controlPoints = (NetTool.ControlPoint[])m_controlPoints.GetValue(netTool);
			var controlPointCount = (int)m_controlPointCount.GetValue(netTool);
			var upgrading = (bool)m_upgrading.GetValue (netTool);
			var upgradedSegments = (HashSet<ushort>)m_upgradedSegments.GetValue (netTool);
			var bulldozerTool = (BulldozeTool)m_bulldozerTool.GetValue (netTool);
			bool flag = endPoint.m_node != 0 || endPoint.m_segment != 0;
			ushort num;
			ushort num2;
			int num3;
			int num4;
			if (NetTool.CreateNode(info, startPoint, middlePoint, endPoint, NetTool.m_nodePositionsSimulation, 1000, true, false, true, needMoney, false, switchDirection, 0, out num, out num2, out num3, out num4) == ToolBase.ToolErrors.None)
			{
				NetTool.CreateNode(info, startPoint, middlePoint, endPoint, NetTool.m_nodePositionsSimulation, 1000, false, false, true, needMoney, false, switchDirection, 0, out num, out num2, out num3, out num4);
				NetManager instance = Singleton<NetManager>.instance;
				endPoint.m_segment = 0;
				endPoint.m_node = num;
				if (num2 != 0)
				{
					if (upgrading)
					{
						while (!Monitor.TryEnter(upgradedSegments, SimulationManager.SYNCHRONIZE_TIMEOUT))
						{
						}
						try
						{
							upgradedSegments.Add(num2);
						}
						finally
						{
							Monitor.Exit(upgradedSegments);
						}
					}
					if (instance.m_segments.m_buffer[(int)num2].m_startNode == num)
					{
						endPoint.m_direction = -instance.m_segments.m_buffer[(int)num2].m_startDirection;
					}
					else if (instance.m_segments.m_buffer[(int)num2].m_endNode == num)
					{
						endPoint.m_direction = -instance.m_segments.m_buffer[(int)num2].m_endDirection;
					}
				}
				controlPoints [0] = endPoint;
				m_elevation.SetValue(netTool, Mathf.Max(0, Mathf.RoundToInt(endPoint.m_elevation / terrainStep)));
				if (num != 0 && (instance.m_nodes.m_buffer[(int)num].m_flags & NetNode.Flags.Outside) != NetNode.Flags.None)
				{
					controlPointCount = 0;
				}
				else if (netTool.m_mode == NetTool.Mode.Freeform && controlPointCount == 2)
				{
					middlePoint.m_position = endPoint.m_position * 2f - middlePoint.m_position;
					middlePoint.m_elevation = endPoint.m_elevation * 2f - middlePoint.m_elevation;
					middlePoint.m_direction = endPoint.m_direction;
					middlePoint.m_node = 0;
					middlePoint.m_segment = 0;
					controlPoints[1] = middlePoint;
					controlPointCount = 2;
				}
				else
				{
					controlPointCount = 1;
				}
				if (info.m_class.m_service > ItemClass.Service.Office)
				{
					int num5 = info.m_class.m_service - ItemClass.Service.Office - 1;
					Singleton<GuideManager>.instance.m_serviceNotUsed[num5].Disable();
					Singleton<GuideManager>.instance.m_serviceNeeded[num5].Deactivate();
				}
				if (info.m_class.m_service == ItemClass.Service.Road)
				{
					Singleton<CoverageManager>.instance.CoverageUpdated(ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.None);
					Singleton<NetManager>.instance.m_roadsNotUsed.Disable();
				}
				if ((info.m_class.m_service == ItemClass.Service.Road || info.m_class.m_service == ItemClass.Service.PublicTransport || info.m_class.m_service == ItemClass.Service.Beautification) && (info.m_hasForwardVehicleLanes || info.m_hasBackwardVehicleLanes) && (!info.m_hasForwardVehicleLanes || !info.m_hasBackwardVehicleLanes))
				{
					Singleton<NetManager>.instance.m_onewayRoadPlacement.Disable();
				}
				if (upgrading)
				{
					info.m_netAI.UpgradeSucceeded();
				}
				else if (flag && num != 0)
				{
					info.m_netAI.ConnectionSucceeded(num, ref Singleton<NetManager>.instance.m_nodes.m_buffer[(int)num]);
				}
				Singleton<GuideManager>.instance.m_notEnoughMoney.Deactivate();
				if (Singleton<GuideManager>.instance.m_properties != null && !upgrading && num2 != 0 && bulldozerTool != null && bulldozerTool.m_lastNetInfo != null && bulldozerTool.m_lastNetInfo.m_netAI.CanUpgradeTo(info))
				{
					ushort startNode = instance.m_segments.m_buffer[(int)num2].m_startNode;
					ushort endNode = instance.m_segments.m_buffer[(int)num2].m_endNode;
					Vector3 position = instance.m_nodes.m_buffer[(int)startNode].m_position;
					Vector3 position2 = instance.m_nodes.m_buffer[(int)endNode].m_position;
					Vector3 startDirection = instance.m_segments.m_buffer[(int)num2].m_startDirection;
					Vector3 endDirection = instance.m_segments.m_buffer[(int)num2].m_endDirection;
					if (Vector3.SqrMagnitude(bulldozerTool.m_lastStartPos - position) < 1f && Vector3.SqrMagnitude(bulldozerTool.m_lastEndPos - position2) < 1f && Vector2.Dot(VectorUtils.XZ(bulldozerTool.m_lastStartDir), VectorUtils.XZ(startDirection)) > 0.99f && Vector2.Dot(VectorUtils.XZ(bulldozerTool.m_lastEndDir), VectorUtils.XZ(endDirection)) > 0.99f)
					{
						Singleton<NetManager>.instance.m_manualUpgrade.Activate(Singleton<GuideManager>.instance.m_properties.m_manualUpgrade, info.m_class.m_service);
					}
				}
				m_controlPoints.SetValue (netTool, controlPoints);
				m_controlPointCount.SetValue (netTool, controlPointCount);
				return true;
			}
			return false;
		}
		public static ToolBase.ToolErrors CreateNode(NetInfo info, NetTool.ControlPoint startPoint, NetTool.ControlPoint middlePoint, NetTool.ControlPoint endPoint, FastList<NetTool.NodePosition> nodeBuffer, int maxSegments, bool test, bool visualize, bool autoFix, bool needMoney, bool invert, bool switchDir, ushort relocateBuildingID, out ushort firstNode, out ushort lastNode, out ushort segment, out int cost, out int productionRate)
		{
			NetTool netTool = ToolsModifierControl.GetTool<NetTool>();
			MethodInfo CheckStartAndEnd = typeof(NetTool).GetMethod ("CheckStartAndEnd", PrivateStatic);
			MethodInfo CanAddSegment = typeof(NetTool).GetMethod ("CanAddSegment", PrivateStatic);
			MethodInfo CanAddNode = typeof(NetTool).GetMethod ("CanAddNode", PrivateStatic, null, new Type[]{typeof(ushort),typeof(Vector3),typeof(Vector3),typeof(bool),typeof(ulong[])}, null);
			MethodInfo RenderNodeBuilding = typeof(NetTool).GetMethod ("RenderNodeBuilding", PrivateStatic);
			MethodInfo TestNodeBuilding = typeof(NetTool).GetMethod ("TestNodeBuilding", PrivateStatic);
			MethodInfo LerpPosition = typeof(NetTool).GetMethod ("LerpPosition", PrivateStatic);
			MethodInfo CheckNodeHeights = typeof(NetTool).GetMethod ("CheckNodeHeights", PrivateStatic);
			MethodInfo SplitSegment = typeof(NetTool).GetMethod ("SplitSegment", PrivateStatic);
			MethodInfo RenderNode = typeof(NetTool).GetMethod ("RenderNode", PrivateStatic);
			MethodInfo GetIgnoredBuilding = typeof(NetTool).GetMethod ("GetIgnoredBuilding", PrivateStatic);
			MethodInfo TryMoveNode = typeof(NetTool).GetMethod ("TryMoveNode", PrivateStatic);
			MethodInfo CanCreateSegment = typeof(NetTool).GetMethod ("CanCreateSegment", PrivateStatic, null, new Type[]{typeof(NetInfo),typeof(ushort),typeof(ushort),typeof(ushort),typeof(ushort),typeof(ushort),typeof(Vector3),typeof(Vector3),typeof(Vector3),typeof(Vector3),typeof(ulong[])}, null);
			MethodInfo RenderSegment = typeof(NetTool).GetMethod ("RenderSegment", PrivateStatic);

			BuildingInfo buildingInfo;
			Vector3 vector3;
			Vector3 vector31;
			Vector3 vector32;
			Vector3 vector33;
			NetTool.NodePosition mElevation = new NetTool.NodePosition();
			BuildingInfo buildingInfo1;
			float single;
			Vector3 vector34;
			Vector3 vector35;
			ToolBase.ToolErrors toolError;
			bool flag;
			ushort mNode;
			ushort mSegment;
			ushort ignoredBuilding;
			ushort num = middlePoint.m_segment;
			NetInfo netInfo = null;
			if (startPoint.m_segment == num || endPoint.m_segment == num)
			{
				num = 0;
			}
			uint mCurrentBuildIndex = Singleton<SimulationManager>.instance.m_currentBuildIndex;
			bool flag1 = invert;
			bool mFlags = true;
			bool mFlags1 = true;
			bool flag2 = false;
			if (num == 0)
			{
				if (autoFix && Singleton<SimulationManager>.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.True)
				{
					flag1 = !flag1;
				}
				cost = 0;
			}
			else
			{
				flag2 = DefaultTool.FindSecondarySegment(num) != 0;
				maxSegments = Mathf.Min(1, maxSegments);
				cost = -Singleton<NetManager>.instance.m_segments.m_buffer[num].Info.m_netAI.GetConstructionCost(startPoint.m_position, endPoint.m_position, startPoint.m_elevation, endPoint.m_elevation);
				mCurrentBuildIndex = Singleton<NetManager>.instance.m_segments.m_buffer[num].m_buildIndex;
				mFlags = (Singleton<NetManager>.instance.m_nodes.m_buffer[startPoint.m_node].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None;
				mFlags1 = (Singleton<NetManager>.instance.m_nodes.m_buffer[endPoint.m_node].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None;
				autoFix = false;
				if (switchDir)
				{
					flag1 = !flag1;
					info = Singleton<NetManager>.instance.m_segments.m_buffer[num].Info;
				}
				if (!test && !visualize)
				{
					if ((Singleton<NetManager>.instance.m_segments.m_buffer[num].m_flags & NetSegment.Flags.Invert) != NetSegment.Flags.None)
					{
						flag1 = !flag1;
					}
					netInfo = Singleton<NetManager>.instance.m_segments.m_buffer[num].Info;
					Singleton<NetManager>.instance.ReleaseSegment(num, true);
					num = 0;
				}
			}
			ToolController mProperties = Singleton<ToolManager>.instance.m_properties;
			ulong[] numArray = null;
			ulong[] numArray1 = null;
			ToolBase.ToolErrors toolError1 = ToolBase.ToolErrors.None;
			if (test || !visualize)
			{
				mProperties.BeginColliding(out numArray, out numArray1);
			}
			try
			{
				ushort num1 = 0;
				if (num == 0 || !switchDir)
				{
					toolError1 = toolError1 | info.m_netAI.CheckBuildPosition(test, visualize, false, autoFix, ref startPoint, ref middlePoint, ref endPoint, out buildingInfo, out vector3, out vector31, out productionRate);
				}
				else
				{
					buildingInfo = null;
					vector3 = Vector3.zero;
					vector31 = Vector3.forward;
					productionRate = 0;
					if (info.m_hasForwardVehicleLanes == info.m_hasBackwardVehicleLanes)
					{
						toolError1 = toolError1 | ToolBase.ToolErrors.CannotUpgrade;
					}
				}
				if (test)
				{
					Vector3 mDirection = middlePoint.m_direction;
					Vector3 mDirection1 = -endPoint.m_direction;
					if (maxSegments != 0 && num == 0 && mDirection.x * mDirection1.x + mDirection.z * mDirection1.z >= 0.8f)
					{
						toolError1 = toolError1 | ToolBase.ToolErrors.InvalidShape;
					}
					if (maxSegments != 0 && !(bool)CheckStartAndEnd.Invoke (netTool, new object[]{num, startPoint.m_segment, startPoint.m_node, endPoint.m_segment, endPoint.m_node, numArray}))
					{
						toolError1 = toolError1 | ToolBase.ToolErrors.ObjectCollision;
					}
					if (startPoint.m_node != 0)
					{
						if (maxSegments != 0 && !(bool)CanAddSegment.Invoke (netTool, new object[]{startPoint.m_node, mDirection, numArray, num}))
						{
							toolError1 = toolError1 | ToolBase.ToolErrors.ObjectCollision;
						}
					}
					else if (startPoint.m_segment != 0 && !(bool)CanAddNode.Invoke (netTool, new object[]{startPoint.m_segment, startPoint.m_position, mDirection, maxSegments != 0, numArray}))
					{
						toolError1 = toolError1 | ToolBase.ToolErrors.ObjectCollision;
					}
					if (endPoint.m_node != 0)
					{
						if (maxSegments != 0 && !(bool)CanAddSegment.Invoke (netTool, new object[]{endPoint.m_node, mDirection1, numArray, num}))
						{
							toolError1 = toolError1 | ToolBase.ToolErrors.ObjectCollision;
						}
					}
					else if (endPoint.m_segment != 0 && !(bool)CanAddNode.Invoke (netTool, new object[]{endPoint.m_segment, endPoint.m_position, mDirection1, maxSegments != 0, numArray}))
					{
						toolError1 = toolError1 | ToolBase.ToolErrors.ObjectCollision;
					}
					if (!Singleton<NetManager>.instance.CheckLimits())
					{
						toolError1 = toolError1 | ToolBase.ToolErrors.TooManyObjects;
					}
				}
				if (buildingInfo != null)
				{
					if (visualize)
					{
						RenderNodeBuilding.Invoke (netTool, new object[]{buildingInfo, vector3, vector31});
					}
					else if (!test)
					{
						float single1 = Mathf.Atan2(-vector31.x, vector31.z);
						if (Singleton<BuildingManager>.instance.CreateBuilding(out num1, ref Singleton<SimulationManager>.instance.m_randomizer, buildingInfo, vector3, single1, 0, Singleton<SimulationManager>.instance.m_currentBuildIndex))
						{
							Singleton<BuildingManager>.instance.m_buildings.m_buffer[num1].m_flags = Singleton<BuildingManager>.instance.m_buildings.m_buffer[num1].m_flags | Building.Flags.FixedHeight;
							SimulationManager simulationManager = Singleton<SimulationManager>.instance;
							simulationManager.m_currentBuildIndex = simulationManager.m_currentBuildIndex + 1;
						}
					}
					else
					{
						toolError1 = toolError1 | (ToolBase.ToolErrors)TestNodeBuilding.Invoke (netTool, new object[]{buildingInfo, vector3, vector31, 0, 0, 0, test, numArray, numArray1});
					}
				}
				bool mDirection2 = middlePoint.m_direction.x * endPoint.m_direction.x + middlePoint.m_direction.z * endPoint.m_direction.z <= 0.999f;
				Vector2 vector2 = new Vector2(startPoint.m_position.x - middlePoint.m_position.x, startPoint.m_position.z - middlePoint.m_position.z);
				float single2 = vector2.magnitude;
				Vector2 vector21 = new Vector2(middlePoint.m_position.x - endPoint.m_position.x, middlePoint.m_position.z - endPoint.m_position.z);
				float single3 = vector21.magnitude;
				float single4 = single2 + single3;
				if (test && maxSegments != 0)
				{
					float single5 = 7f;
					if (mDirection2 && num == 0)
					{
						if (single2 < single5)
						{
							toolError1 = toolError1 | ToolBase.ToolErrors.TooShort;
						}
						if (single3 < single5)
						{
							toolError1 = toolError1 | ToolBase.ToolErrors.TooShort;
						}
					}
					else if (single4 < single5)
					{
						toolError1 = toolError1 | ToolBase.ToolErrors.TooShort;
					}
				}
				segment = 0;
				int num2 = Mathf.Min(maxSegments, Mathf.FloorToInt(single4 / 100f) + 1);
				if (num2 >= 2)
				{
					flag2 = true;
				}
				ushort mNode1 = startPoint.m_node;
				Vector3 mPosition = startPoint.m_position;
				Vector3 mDirection3 = middlePoint.m_direction;
				NetSegment.CalculateMiddlePoints(startPoint.m_position, middlePoint.m_direction, endPoint.m_position, -endPoint.m_direction, mFlags, mFlags1, out vector32, out vector33);
				nodeBuffer.Clear();
				mElevation.m_position = mPosition;
				mElevation.m_direction = mDirection3;
				mElevation.m_minY = mPosition.y;
				mElevation.m_maxY = mPosition.y;
				mElevation.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(mElevation.m_position);
				mElevation.m_elevation = startPoint.m_elevation;
				if (mElevation.m_elevation < (terrainStep-1))
				{
					mElevation.m_elevation = 0f;
				}
				mElevation.m_nodeInfo = info.m_netAI.GetInfo(mElevation.m_position.y - mElevation.m_terrainHeight, single4, startPoint.m_outside, false, mDirection2, flag2, ref toolError1);
				mElevation.m_double = false;
				if (startPoint.m_node != 0)
				{
					mElevation.m_double = (Singleton<NetManager>.instance.m_nodes.m_buffer[startPoint.m_node].m_flags & NetNode.Flags.Double) != NetNode.Flags.None;
				}
				nodeBuffer.Add(mElevation);
				for (int i = 1; i <= num2; i++)
				{
					mElevation.m_elevation = Mathf.Lerp(startPoint.m_elevation, endPoint.m_elevation, (float)i / (float)num2);
					if (mElevation.m_elevation < (terrainStep-1))
					{
						mElevation.m_elevation = 0f;
					}
					mElevation.m_double = false;
					if (i == num2)
					{
						mElevation.m_position = endPoint.m_position;
						mElevation.m_direction = endPoint.m_direction;
						mElevation.m_minY = endPoint.m_position.y;
						mElevation.m_maxY = endPoint.m_position.y;
						mElevation.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(mElevation.m_position);
						if (endPoint.m_node != 0)
						{
							mElevation.m_double = (Singleton<NetManager>.instance.m_nodes.m_buffer[endPoint.m_node].m_flags & NetNode.Flags.Double) != NetNode.Flags.None;
						}
					}
					else if (!mDirection2)
					{
						float lengthSnap = info.m_netAI.GetLengthSnap();
						mElevation.m_position = (Vector3)LerpPosition.Invoke (netTool, new object[]{startPoint.m_position, endPoint.m_position, (float)i / (float)num2, lengthSnap});
						mElevation.m_direction = endPoint.m_direction;
						mElevation.m_position.y = NetSegment.SampleTerrainHeight(info, mElevation.m_position, visualize) + mElevation.m_elevation;
						mElevation.m_minY = 0f;
						mElevation.m_maxY = 1280f;
						mElevation.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(mElevation.m_position);
					}
					else
					{
						mElevation.m_position = Bezier3.Position(startPoint.m_position, vector32, vector33, endPoint.m_position, (float)i / (float)num2);
						mElevation.m_direction = Bezier3.Tangent(startPoint.m_position, vector32, vector33, endPoint.m_position, (float)i / (float)num2);
						mElevation.m_position.y = NetSegment.SampleTerrainHeight(info, mElevation.m_position, visualize) + mElevation.m_elevation;
						mElevation.m_direction = VectorUtils.NormalizeXZ(mElevation.m_direction);
						mElevation.m_minY = 0f;
						mElevation.m_maxY = 1280f;
						mElevation.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(mElevation.m_position);
					}
					mElevation.m_nodeInfo = null;
					nodeBuffer.Add(mElevation);
				}
				ToolBase.ToolErrors toolError2 = (ToolBase.ToolErrors)CheckNodeHeights.Invoke (netTool, new object[]{info, nodeBuffer});
				if (toolError2 != ToolBase.ToolErrors.None && test)
				{
					toolError1 = toolError1 | toolError2;
				}
				for (int j = 1; j <= num2; j++)
				{
					NetAI mNetAI = info.m_netAI;
					float mPosition1 = nodeBuffer.m_buffer[j].m_position.y - nodeBuffer.m_buffer[j].m_terrainHeight;
					float single6 = single4;
					flag = (j != num2 ? false : endPoint.m_outside);
					nodeBuffer.m_buffer[j].m_nodeInfo = mNetAI.GetInfo(mPosition1, single6, false, flag, mDirection2, flag2, ref toolError1);
				}
				int num3 = 1;
				int num4 = 0;
				NetInfo netInfo1 = null;
				while (num3 <= num2)
				{
					NetInfo mNodeInfo = nodeBuffer.m_buffer[num3].m_nodeInfo;
					if (num3 == num2 || mNodeInfo != netInfo1)
					{
						if (num4 == 0 || !netInfo1.m_netAI.RequireDoubleSegments())
						{
							num3++;
						}
						else
						{
							int num5 = num3 - num4 - 1;
							int num6 = num3;
							if ((num4 & 1) != 0)
							{
								num3++;
							}
							else
							{
								nodeBuffer.RemoveAt(num3 - 1);
								num2--;
								num6--;
								for (int k = num5 + 1; k < num6; k++)
								{
									float single7 = (float)(k - num5) / (float)num4;
									nodeBuffer.m_buffer[k].m_position = Vector3.Lerp(nodeBuffer.m_buffer[num5].m_position, nodeBuffer.m_buffer[num6].m_position, single7);
									nodeBuffer.m_buffer[k].m_direction = VectorUtils.NormalizeXZ(Vector3.Lerp(nodeBuffer.m_buffer[num5].m_direction, nodeBuffer.m_buffer[num6].m_direction, single7));
									nodeBuffer.m_buffer[k].m_elevation = Mathf.Lerp(nodeBuffer.m_buffer[num5].m_elevation, nodeBuffer.m_buffer[num6].m_elevation, single7);
									nodeBuffer.m_buffer[k].m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(nodeBuffer.m_buffer[k].m_position);
								}
							}
							for (int l = num5 + 1; l < num6; l++)
							{
								nodeBuffer.m_buffer[l].m_double = nodeBuffer.m_buffer[l].m_double | (l - num5 & 1) == 1;
							}
						}
						num4 = 1;
					}
					else
					{
						num4++;
						num3++;
					}
					netInfo1 = mNodeInfo;
				}
				NetInfo mNodeInfo1 = nodeBuffer[0].m_nodeInfo;
				bool flag3 = false;
				if (mNode1 == 0 && !test && !visualize)
				{
					if (startPoint.m_segment != 0)
					{
						var arg1 = new object[]{startPoint.m_segment, mNode1, mPosition};
						if ((bool)SplitSegment.Invoke (netTool, arg1))
						{
							flag3 = true;
						}
						mNode1 = (ushort)arg1[1];
						startPoint.m_segment = 0;
					}
					else if (Singleton<NetManager>.instance.CreateNode(out mNode1, ref Singleton<SimulationManager>.instance.m_randomizer, mNodeInfo1, mPosition, Singleton<SimulationManager>.instance.m_currentBuildIndex))
					{
						if (startPoint.m_outside)
						{
							Singleton<NetManager>.instance.m_nodes.m_buffer[mNode1].m_flags = Singleton<NetManager>.instance.m_nodes.m_buffer[mNode1].m_flags | NetNode.Flags.Outside;
						}
						if (mPosition.y - nodeBuffer.m_buffer[0].m_terrainHeight < (terrainStep-1))
						{
							Singleton<NetManager>.instance.m_nodes.m_buffer[mNode1].m_flags = Singleton<NetManager>.instance.m_nodes.m_buffer[mNode1].m_flags | NetNode.Flags.OnGround;
						}
						NetTool.NodePosition item = nodeBuffer[0];
						Singleton<NetManager>.instance.m_nodes.m_buffer[mNode1].m_elevation = (byte)Mathf.Clamp(Mathf.RoundToInt(item.m_elevation), 0, 255);
						SimulationManager mCurrentBuildIndex1 = Singleton<SimulationManager>.instance;
						mCurrentBuildIndex1.m_currentBuildIndex = mCurrentBuildIndex1.m_currentBuildIndex + 1;
						flag3 = true;
					}
					startPoint.m_node = mNode1;
				}
				NetNode netNode = new NetNode()
				{
					m_position = mPosition
				};
				if (nodeBuffer.m_buffer[0].m_double)
				{
					netNode.m_flags = netNode.m_flags | NetNode.Flags.Double;
				}
				if (mPosition.y - nodeBuffer.m_buffer[0].m_terrainHeight < (terrainStep-1))
				{
					netNode.m_flags = netNode.m_flags | NetNode.Flags.OnGround;
				}
				if (startPoint.m_outside)
				{
					netNode.m_flags = netNode.m_flags | NetNode.Flags.Outside;
				}
				mNodeInfo1.m_netAI.GetNodeBuilding(0, ref netNode, out buildingInfo1, out single);
				if (visualize)
				{
					if (buildingInfo1 != null && (mNode1 == 0 || num != 0))
					{
						Vector3 vector36 = mPosition;
						vector36.y = vector36.y + single;
						RenderNodeBuilding.Invoke(netTool, new object[]{buildingInfo1, vector36, mDirection3});
					}
					if (mNodeInfo1.m_netAI.DisplayTempSegment())
					{
						RenderNode.Invoke (netTool, new object[]{mNodeInfo1, mPosition, mDirection3});
					}
				}
				else if (buildingInfo1 != null && (netNode.m_flags & NetNode.Flags.Outside) == NetNode.Flags.None)
				{
					ushort mNode2 = startPoint.m_node;
					ushort mSegment1 = startPoint.m_segment;
					ushort ignoredBuilding1 = (ushort)GetIgnoredBuilding.Invoke (netTool, new object[]{startPoint});
					toolError2 =(ToolBase.ToolErrors)TestNodeBuilding.Invoke(netTool, new object[]{buildingInfo1, mPosition, mDirection3, mNode2, mSegment1, ignoredBuilding1, test, numArray, numArray1});
					if (toolError2 != ToolBase.ToolErrors.None)
					{
						toolError1 = toolError1 | toolError2;
					}
				}
				if (num1 != 0 && mNode1 != 0 && (Singleton<NetManager>.instance.m_nodes.m_buffer[mNode1].m_flags & NetNode.Flags.Untouchable) == NetNode.Flags.None)
				{
					Singleton<NetManager>.instance.m_nodes.m_buffer[mNode1].m_flags = Singleton<NetManager>.instance.m_nodes.m_buffer[mNode1].m_flags | NetNode.Flags.Untouchable;
					Singleton<NetManager>.instance.m_nodes.m_buffer[mNode1].m_nextBuildingNode = Singleton<BuildingManager>.instance.m_buildings.m_buffer[num1].m_netNode;
					Singleton<BuildingManager>.instance.m_buildings.m_buffer[num1].m_netNode = mNode1;
				}
				for (int m = 1; m <= num2; m++)
				{
					Vector3 mPosition2 = nodeBuffer[m].m_position;
					Vector3 mDirection4 = nodeBuffer[m].m_direction;
					NetSegment.CalculateMiddlePoints(mPosition, mDirection3, mPosition2, -mDirection4, mFlags, mFlags1, out vector34, out vector35);
					mNodeInfo1 = nodeBuffer.m_buffer[m].m_nodeInfo;
					NetInfo mNodeInfo2 = null;
					float mPosition3 = nodeBuffer[m - 1].m_position.y;
					NetTool.NodePosition nodePosition = nodeBuffer[m - 1];
					float mTerrainHeight = mPosition3 - nodePosition.m_terrainHeight;
					NetTool.NodePosition item1 = nodeBuffer[m];
					float mPosition4 = item1.m_position.y - nodeBuffer[m].m_terrainHeight;
					if (nodeBuffer.m_buffer[m].m_double)
					{
						mNodeInfo2 = nodeBuffer.m_buffer[m].m_nodeInfo;
						netNode.m_flags = netNode.m_flags | NetNode.Flags.Double;
					}
					else if (!nodeBuffer.m_buffer[m - 1].m_double)
					{
						float single8 = Mathf.Max(mPosition4, mTerrainHeight);
						for (int n = 1; n < 8; n++)
						{
							Vector3 vector37 = Bezier3.Position(mPosition, vector34, vector35, mPosition2, (float)n / 8f);
							float single9 = vector37.y - Singleton<TerrainManager>.instance.SampleRawHeightSmooth(vector37);
							single8 = Mathf.Max(single8, single9);
						}
						mNodeInfo2 = info.m_netAI.GetInfo(single8, single4, (m != 1 ? false : startPoint.m_outside), (m != num2 ? false : endPoint.m_outside), mDirection2, false, ref toolError1);
						netNode.m_flags = netNode.m_flags & (NetNode.Flags.Created | NetNode.Flags.Deleted | NetNode.Flags.Original | NetNode.Flags.Disabled | NetNode.Flags.End | NetNode.Flags.Middle | NetNode.Flags.Bend | NetNode.Flags.Junction | NetNode.Flags.Moveable | NetNode.Flags.Untouchable | NetNode.Flags.Outside | NetNode.Flags.Temporary | NetNode.Flags.Fixed | NetNode.Flags.OnGround | NetNode.Flags.Ambiguous | NetNode.Flags.Water | NetNode.Flags.Sewage | NetNode.Flags.ForbidLaneConnection | NetNode.Flags.Transition | NetNode.Flags.LevelCrossing | NetNode.Flags.OneWayOut | NetNode.Flags.TrafficLights | NetNode.Flags.OneWayIn | NetNode.Flags.OneWayOutTrafficLights);
					}
					else
					{
						mNodeInfo2 = nodeBuffer.m_buffer[m - 1].m_nodeInfo;
						netNode.m_flags = netNode.m_flags & (NetNode.Flags.Created | NetNode.Flags.Deleted | NetNode.Flags.Original | NetNode.Flags.Disabled | NetNode.Flags.End | NetNode.Flags.Middle | NetNode.Flags.Bend | NetNode.Flags.Junction | NetNode.Flags.Moveable | NetNode.Flags.Untouchable | NetNode.Flags.Outside | NetNode.Flags.Temporary | NetNode.Flags.Fixed | NetNode.Flags.OnGround | NetNode.Flags.Ambiguous | NetNode.Flags.Water | NetNode.Flags.Sewage | NetNode.Flags.ForbidLaneConnection | NetNode.Flags.Transition | NetNode.Flags.LevelCrossing | NetNode.Flags.OneWayOut | NetNode.Flags.TrafficLights | NetNode.Flags.OneWayIn | NetNode.Flags.OneWayOutTrafficLights);
					}
					netNode.m_position = mPosition2;
					if (mPosition4 >= (terrainStep-1))
					{
						netNode.m_flags = netNode.m_flags & (NetNode.Flags.Created | NetNode.Flags.Deleted | NetNode.Flags.Original | NetNode.Flags.Disabled | NetNode.Flags.End | NetNode.Flags.Middle | NetNode.Flags.Bend | NetNode.Flags.Junction | NetNode.Flags.Moveable | NetNode.Flags.Untouchable | NetNode.Flags.Outside | NetNode.Flags.Temporary | NetNode.Flags.Double | NetNode.Flags.Fixed | NetNode.Flags.Ambiguous | NetNode.Flags.Water | NetNode.Flags.Sewage | NetNode.Flags.ForbidLaneConnection | NetNode.Flags.Transition | NetNode.Flags.LevelCrossing | NetNode.Flags.OneWayOut | NetNode.Flags.TrafficLights | NetNode.Flags.OneWayIn | NetNode.Flags.OneWayOutTrafficLights);
					}
					else
					{
						netNode.m_flags = netNode.m_flags | NetNode.Flags.OnGround;
					}
					if (m == num2 && endPoint.m_outside)
					{
						netNode.m_flags = netNode.m_flags | NetNode.Flags.Outside;
					}
					mNodeInfo1.m_netAI.GetNodeBuilding(0, ref netNode, out buildingInfo1, out single);
					if (!visualize)
					{
						if (mNodeInfo2.m_canCollide)
						{
							int num7 = Mathf.Max(2, 16 / num2);
							Vector3 mHalfWidth = new Vector3(mDirection3.z, 0f, -mDirection3.x) * mNodeInfo2.m_halfWidth;
							Quad3 quad3 = new Quad3()
							{
								a = mPosition - mHalfWidth,
								d = mPosition + mHalfWidth
							};
							for (int o = 1; o <= num7; o++)
							{
								ushort mNode3 = 0;
								ushort mNode4 = 0;
								ushort mSegment2 = 0;
								ushort ignoredBuilding2 = 0;
								bool mOutside = false;
								if (m == 1 && o - 1 << 1 < num7)
								{
									mNode3 = startPoint.m_node;
									if (m == num2 && o << 1 >= num7)
									{
										mNode4 = endPoint.m_node;
									}
									mSegment2 = startPoint.m_segment;
									ignoredBuilding2 = (ushort)GetIgnoredBuilding.Invoke(netTool, new object[]{startPoint});
									mOutside = startPoint.m_outside;
								}
								else if (m == num2 && o << 1 > num7)
								{
									mNode3 = endPoint.m_node;
									if (m == 1 && o - 1 << 1 <= num7)
									{
										mNode4 = startPoint.m_node;
									}
									mSegment2 = endPoint.m_segment;
									ignoredBuilding2 = (ushort)GetIgnoredBuilding.Invoke(netTool, new object[]{endPoint});
									mOutside = endPoint.m_outside;
								}
								else if (o - 1 << 1 < num7)
								{
									mNode3 = mNode1;
								}
								Vector3 vector38 = Bezier3.Position(mPosition, vector34, vector35, mPosition2, (float)o / (float)num7);
								mHalfWidth = Bezier3.Tangent(mPosition, vector34, vector35, mPosition2, (float)o / (float)num7);
								Vector3 vector39 = new Vector3(mHalfWidth.z, 0f, -mHalfWidth.x);
								mHalfWidth = vector39.normalized * mNodeInfo2.m_halfWidth;
								quad3.b = vector38 - mHalfWidth;
								quad3.c = vector38 + mHalfWidth;
								float single10 = Mathf.Min(Mathf.Min(quad3.a.y, quad3.b.y), Mathf.Min(quad3.c.y, quad3.d.y)) + mNodeInfo2.m_minHeight;
								float single11 = Mathf.Max(Mathf.Max(quad3.a.y, quad3.b.y), Mathf.Max(quad3.c.y, quad3.d.y)) + mNodeInfo2.m_maxHeight;
								Quad2 quad2 = Quad2.XZ(quad3);
								Singleton<NetManager>.instance.OverlapQuad(quad2, single10, single11, mNodeInfo2.m_class.m_layer, mNode3, mNode4, mSegment2, numArray);
								Singleton<BuildingManager>.instance.OverlapQuad(quad2, single10, single11, mNodeInfo2.m_class.m_layer, ignoredBuilding2, mNode3, mNode4, numArray1);
								if ((mProperties.m_mode & ItemClass.Availability.AssetEditor) != ItemClass.Availability.None)
								{
									float single12 = 256f;
									if (quad2.a.x < -single12 || quad2.a.x > single12 || quad2.a.y < -single12 || quad2.a.y > single12)
									{
										toolError1 = toolError1 | ToolBase.ToolErrors.OutOfArea;
									}
									if (quad2.b.x < -single12 || quad2.b.x > single12 || quad2.b.y < -single12 || quad2.b.y > single12)
									{
										toolError1 = toolError1 | ToolBase.ToolErrors.OutOfArea;
									}
									if (quad2.c.x < -single12 || quad2.c.x > single12 || quad2.c.y < -single12 || quad2.c.y > single12)
									{
										toolError1 = toolError1 | ToolBase.ToolErrors.OutOfArea;
									}
									if (quad2.d.x < -single12 || quad2.d.x > single12 || quad2.d.y < -single12 || quad2.d.y > single12)
									{
										toolError1 = toolError1 | ToolBase.ToolErrors.OutOfArea;
									}
								}
								else if (!mOutside && Singleton<GameAreaManager>.instance.QuadOutOfArea(quad2))
								{
									toolError1 = toolError1 | ToolBase.ToolErrors.OutOfArea;
								}
								quad3.a = quad3.b;
								quad3.d = quad3.c;
							}
						}
						if (buildingInfo1 != null && (netNode.m_flags & NetNode.Flags.Outside) == NetNode.Flags.None)
						{
							if (m != num2)
							{
								mNode = 0;
							}
							else
							{
								mNode = endPoint.m_node;
							}
							ushort num8 = mNode;
							if (m != num2)
							{
								mSegment = 0;
							}
							else
							{
								mSegment = endPoint.m_segment;
							}
							ushort num9 = mSegment;
							if (m != num2)
							{
								ignoredBuilding = 0;
							}
							else
							{
								ignoredBuilding = (ushort)GetIgnoredBuilding.Invoke(netTool, new object[]{endPoint});
							}
							ushort num10 = ignoredBuilding;
							Vector3 vector310 = mPosition2;
							vector310.y = vector310.y + single;
							toolError2 = (ToolBase.ToolErrors)TestNodeBuilding.Invoke(netTool, new object[]{buildingInfo1, vector310, mDirection4, num8, num9, num10, test, numArray, numArray1});
							if (toolError2 != ToolBase.ToolErrors.None)
							{
								toolError1 = toolError1 | toolError2;
							}
						}
						if (!test)
						{
							cost = cost + mNodeInfo2.m_netAI.GetConstructionCost(mPosition, mPosition2, mTerrainHeight, mPosition4);
							if (needMoney && cost > 0)
							{
								cost = cost - Singleton<EconomyManager>.instance.FetchResource(EconomyManager.Resource.Construction, cost, mNodeInfo2.m_class);
								if (cost > 0)
								{
									toolError1 = toolError1 | ToolBase.ToolErrors.NotEnoughMoney;
								}
							}
							bool flag4 = mNode1 == 0;
							bool flag5 = false;
							ushort mNode5 = endPoint.m_node;
							if (m != num2 || mNode5 == 0)
							{
								if (m == num2 && endPoint.m_segment != 0)
								{
									var arg2 = new object[]{endPoint.m_segment, mNode5, mPosition2};
									if (!(bool)SplitSegment.Invoke(netTool, arg2))
									{
										flag4 = true;
									}
									else
									{
										flag5 = true;
									}
									mNode5 = (ushort)arg2[1];
									endPoint.m_segment = 0;
								}
								else if (!Singleton<NetManager>.instance.CreateNode(out mNode5, ref Singleton<SimulationManager>.instance.m_randomizer, mNodeInfo1, mPosition2, Singleton<SimulationManager>.instance.m_currentBuildIndex))
								{
									flag4 = true;
								}
								else
								{
									if (m == num2 && endPoint.m_outside)
									{
										Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_flags = Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_flags | NetNode.Flags.Outside;
									}
									if (mPosition4 < (terrainStep-1))
									{
										Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_flags = Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_flags | NetNode.Flags.OnGround;
									}
									if (nodeBuffer.m_buffer[m].m_double)
									{
										Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_flags = Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_flags | NetNode.Flags.Double;
									}
									NetTool.NodePosition nodePosition1 = nodeBuffer[m];
									Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_elevation = (byte)Mathf.Clamp(Mathf.RoundToInt(nodePosition1.m_elevation), 0, 255);
									SimulationManager simulationManager1 = Singleton<SimulationManager>.instance;
									simulationManager1.m_currentBuildIndex = simulationManager1.m_currentBuildIndex + 1;
									flag5 = true;
								}
								if (m == num2)
								{
									endPoint.m_node = mNode5;
								}
							}
							if (!flag4 && !mDirection2 && Singleton<NetManager>.instance.m_nodes.m_buffer[mNode1].m_elevation == Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_elevation)
							{
								Vector3 mPosition5 = mPosition;
								if (m == 1)
								{
									var arg3 = new object[]{mNode1, mDirection3, mNodeInfo2, mPosition2};
									TryMoveNode.Invoke(netTool,arg3);
									mNode1 = (ushort)arg3[0];
									mDirection3 = (Vector3)arg3[1];
									mPosition5 = Singleton<NetManager>.instance.m_nodes.m_buffer[mNode1].m_position;
								}
								if (m == num2)
								{
									Vector3 vector311 = -mDirection4;
									var arg4 = new object[]{mNode5, vector311, mNodeInfo2, mPosition2};
									TryMoveNode.Invoke(netTool,arg4);
									mNode5 = (ushort)arg4[0];
									vector311 = (Vector3)arg4[1];
									mDirection4 = -vector311;
								}
							}
							if (!flag4)
							{
								if (nodeBuffer.m_buffer[m].m_double)
								{
									flag4 = !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, mNodeInfo2, mNode5, mNode1, -mDirection4, mDirection3, mCurrentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, !flag1);
								}
								else if (!nodeBuffer.m_buffer[m - 1].m_double)
								{
									flag4 = ((num2 - m & 1) != 0 || m == 1 || !mDirection2 ? !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, mNodeInfo2, mNode1, mNode5, mDirection3, -mDirection4, mCurrentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, flag1) : !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, mNodeInfo2, mNode5, mNode1, -mDirection4, mDirection3, mCurrentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, !flag1));
								}
								else
								{
									flag4 = !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, mNodeInfo2, mNode1, mNode5, mDirection3, -mDirection4, mCurrentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, flag1);
								}
								if (!flag4)
								{
									SimulationManager mCurrentBuildIndex2 = Singleton<SimulationManager>.instance;
									mCurrentBuildIndex2.m_currentBuildIndex = mCurrentBuildIndex2.m_currentBuildIndex + 2;
									mCurrentBuildIndex = Singleton<SimulationManager>.instance.m_currentBuildIndex;
									NetTool.DispatchPlacementEffect(mPosition, vector34, vector35, mPosition2, info.m_halfWidth, false);
									mNodeInfo2.m_netAI.ManualActivation(segment, ref Singleton<NetManager>.instance.m_segments.m_buffer[segment], netInfo);
								}
							}
							if (flag4)
							{
								if (flag3 && mNode1 != 0)
								{
									Singleton<NetManager>.instance.ReleaseNode(mNode1);
									mNode1 = 0;
								}
								if (flag5 && mNode5 != 0)
								{
									Singleton<NetManager>.instance.ReleaseNode(mNode5);
									mNode5 = 0;
								}
							}
							if (num1 != 0 && mNode5 != 0 && (Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_flags & NetNode.Flags.Untouchable) == NetNode.Flags.None)
							{
								Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_flags = Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_flags | NetNode.Flags.Untouchable;
								Singleton<NetManager>.instance.m_nodes.m_buffer[mNode5].m_nextBuildingNode = Singleton<BuildingManager>.instance.m_buildings.m_buffer[num1].m_netNode;
								Singleton<BuildingManager>.instance.m_buildings.m_buffer[num1].m_netNode = mNode5;
							}
							if (num1 != 0 && segment != 0 && (Singleton<NetManager>.instance.m_segments.m_buffer[segment].m_flags & NetSegment.Flags.Untouchable) == NetSegment.Flags.None)
							{
								Singleton<NetManager>.instance.m_segments.m_buffer[segment].m_flags = Singleton<NetManager>.instance.m_segments.m_buffer[segment].m_flags | NetSegment.Flags.Untouchable;
							}
							mNode1 = mNode5;
						}
						else
						{
							cost = cost + mNodeInfo2.m_netAI.GetConstructionCost(mPosition, mPosition2, mTerrainHeight, mPosition4);
							if (needMoney && cost > 0 && Singleton<EconomyManager>.instance.PeekResource(EconomyManager.Resource.Construction, cost) != cost)
							{
								toolError1 = toolError1 | ToolBase.ToolErrors.NotEnoughMoney;
							}
							ushort mNode6 = 0;
							ushort mSegment3 = 0;
							ushort mNode7 = 0;
							ushort mSegment4 = 0;
							if (m == 1)
							{
								mNode6 = startPoint.m_node;
								mSegment3 = startPoint.m_segment;
							}
							if (m == num2)
							{
								mNode7 = endPoint.m_node;
								mSegment4 = endPoint.m_segment;
							}
							toolError1 = toolError1 | (ToolBase.ToolErrors)CanCreateSegment.Invoke(netTool, new object[]{mNodeInfo2, mNode6, mSegment3, mNode7, mSegment4, num, mPosition, mPosition2, mDirection3, -mDirection4, numArray});
						}
					}
					else
					{
						if (buildingInfo1 != null && (m != num2 || endPoint.m_node == 0 || num != 0))
						{
							Vector3 vector312 = mPosition2;
							vector312.y = vector312.y + single;
							RenderNodeBuilding.Invoke(netTool, new object[]{buildingInfo1, vector312, mDirection4});
						}
						if (mNodeInfo2.m_netAI.DisplayTempSegment())
						{
							if (!nodeBuffer.m_buffer[m].m_double)
							{
								RenderSegment.Invoke(netTool, new object[]{mNodeInfo2, mPosition, mPosition2, mDirection3, mDirection4, mFlags, mFlags1});
							}
							else
							{
								RenderSegment.Invoke(netTool, new object[]{mNodeInfo2, mPosition2, mPosition, -mDirection4, -mDirection3, mFlags, mFlags1});
							}
						}
					}
					mPosition = mPosition2;
					mDirection3 = mDirection4;
					flag3 = false;
				}
				if (!visualize)
				{
					BuildingTool.IgnoreRelocateSegments(relocateBuildingID, numArray, numArray1);
					if (NetTool.CheckCollidingSegments(numArray, numArray1, num) && (toolError1 & (ToolBase.ToolErrors.InvalidShape | ToolBase.ToolErrors.TooShort | ToolBase.ToolErrors.SlopeTooSteep | ToolBase.ToolErrors.HeightTooHigh | ToolBase.ToolErrors.TooManyConnections)) == ToolBase.ToolErrors.None)
					{
						toolError1 = toolError1 | ToolBase.ToolErrors.ObjectCollision;
					}
					if (BuildingTool.CheckCollidingBuildings(numArray1, numArray))
					{
						toolError1 = toolError1 | ToolBase.ToolErrors.ObjectCollision;
					}
					if (!test)
					{
						NetTool.ReleaseNonImportantSegments(numArray);
						BuildingTool.ReleaseNonImportantBuildings(numArray1);
					}
				}
				else if (mNodeInfo1.m_netAI.DisplayTempSegment())
				{
					RenderNode.Invoke(netTool, new object[]{mNodeInfo1, mPosition, -mDirection3});
				}
				for (int p = 0; p <= num2; p++)
				{
					nodeBuffer.m_buffer[p].m_nodeInfo = null;
				}
				firstNode = startPoint.m_node;
				lastNode = endPoint.m_node;
				toolError = toolError1;
			}
			finally
			{
				if (cost < 0)
				{
					cost = 0;
				}
				if (test || !visualize)
				{
					mProperties.EndColliding();
				}
			}
			return toolError;
		}
	}
}

