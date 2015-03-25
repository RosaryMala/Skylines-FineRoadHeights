using ColossalFramework;
using ColossalFramework.Globalization;
using ColossalFramework.Math;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

public class NetToolFine : ToolBase
{
    private const float terrainStep = 3f;
    private SavedInputKey m_buildElevationDown;
    private SavedInputKey m_buildElevationUp;
    private ToolBase.ToolErrors m_buildErrors;
    private BulldozeTool m_bulldozerTool;
    private int m_cachedControlPointCount;
    private NetTool.ControlPoint[] m_cachedControlPoints;
    private ToolBase.ToolErrors m_cachedErrors;
    private object m_cacheLock;
    private int m_closeSegmentCount;
    private ushort[] m_closeSegments;
    private int m_constructionCost;
    private int m_controlPointCount;
    private NetTool.ControlPoint[] m_controlPoints;
    private int m_elevation;
    private bool m_lengthChanging;
    private float m_lengthTimer;
    public NetTool.Mode m_mode;
    private Ray m_mouseRay;
    private float m_mouseRayLength;
    private bool m_mouseRayValid;
    [NonSerialized]
    public static FastList<NetTool.NodePosition> m_nodePositionsMain = new FastList<NetTool.NodePosition>();
    [NonSerialized]
    public static FastList<NetTool.NodePosition> m_nodePositionsSimulation = new FastList<NetTool.NodePosition>();
    public CursorInfo m_placementCursor;
    public NetInfo m_prefab;
    private int m_productionRate;
    public bool m_snap = true;
    private bool m_switchingDir;
    private FastList<ushort> m_tempUpgraded;
    public CursorInfo m_upgradeCursor;
    private HashSet<ushort> m_upgradedSegments;
    private bool m_upgrading;

    protected override void Awake()
    {
        base.Awake();
        this.m_bulldozerTool = base.GetComponent<BulldozeTool>();
        this.m_controlPoints = new NetTool.ControlPoint[3];
        this.m_cachedControlPoints = new NetTool.ControlPoint[3];
        this.m_closeSegments = new ushort[0x10];
        this.m_cacheLock = new object();
        this.m_upgradedSegments = new HashSet<ushort>();
        this.m_tempUpgraded = new FastList<ushort>();
        this.m_buildElevationUp = new SavedInputKey(Settings.buildElevationUp, Settings.gameSettingsFile, DefaultSettings.buildElevationUp, true);
        this.m_buildElevationDown = new SavedInputKey(Settings.buildElevationDown, Settings.gameSettingsFile, DefaultSettings.buildElevationDown, true);
    }

    private static bool CanAddNode(ushort segmentID, ushort nodeID, Vector3 position, Vector3 direction)
    {
        NetNode node = Singleton<NetManager>.instance.m_nodes.m_buffer[nodeID];
        if ((node.m_flags & NetNode.Flags.Double) != NetNode.Flags.None)
        {
            return false;
        }
        if ((node.m_flags & (NetNode.Flags.Untouchable | NetNode.Flags.Moveable)) != NetNode.Flags.Moveable)
        {
            NetInfo info = node.Info;
            if (!info.m_netAI.CanModify())
            {
                return false;
            }
            float minNodeDistance = info.GetMinNodeDistance();
            Vector2 vector = new Vector2(node.m_position.x - position.x, node.m_position.z - position.z);
            if (vector.magnitude < minNodeDistance)
            {
                return false;
            }
        }
        return true;
    }

    private static bool CanAddNode(ushort segmentID, Vector3 position, Vector3 direction, bool checkDirection, ulong[] collidingSegmentBuffer)
    {
        bool flag = true;
        NetSegment segment = Singleton<NetManager>.instance.m_segments.m_buffer[segmentID];
        if ((segment.m_flags & NetSegment.Flags.Untouchable) != NetSegment.Flags.None)
        {
            flag = false;
        }
        if (checkDirection)
        {
            Vector3 vector;
            Vector3 vector2;
            segment.GetClosestPositionAndDirection(position, out vector, out vector2);
            float num = (direction.x * vector2.x) + (direction.z * vector2.z);
            if ((num > 0.75f) || (num < -0.75f))
            {
                flag = false;
            }
        }
        if (!CanAddNode(segmentID, segment.m_startNode, position, direction))
        {
            flag = false;
        }
        if (!CanAddNode(segmentID, segment.m_endNode, position, direction))
        {
            flag = false;
        }
        if (!flag && (collidingSegmentBuffer != null))
        {
            collidingSegmentBuffer[segmentID >> 6] |= ((ulong) 1L) << segmentID;
        }
        return flag;
    }

    private static bool CanAddSegment(ushort nodeID, Vector3 direction, ulong[] collidingSegmentBuffer, ushort ignoreSegment)
    {
        NetNode node = Singleton<NetManager>.instance.m_nodes.m_buffer[nodeID];
        bool flag = ((node.m_flags & NetNode.Flags.Double) != NetNode.Flags.None) || ((node.m_flags & (NetNode.Flags.Untouchable | NetNode.Flags.Middle)) == (NetNode.Flags.Untouchable | NetNode.Flags.Middle));
        bool flag2 = true;
        for (int i = 0; i < 8; i++)
        {
            ushort index = node.GetSegment(i);
            if ((index != 0) && (index != ignoreSegment))
            {
                NetSegment segment = Singleton<NetManager>.instance.m_segments.m_buffer[index];
                Vector3 vector = (nodeID != segment.m_startNode) ? segment.m_endDirection : segment.m_startDirection;
                float num3 = (direction.x * vector.x) + (direction.z * vector.z);
                if (flag || (num3 > 0.75f))
                {
                    if (collidingSegmentBuffer != null)
                    {
                        collidingSegmentBuffer[index >> 6] |= ((ulong) 1L) << index;
                    }
                    flag2 = false;
                }
            }
        }
        return flag2;
    }

    [DebuggerHidden]
    private IEnumerator CancelNode()
    {
        return new TCancelNodeTc__Iterator61 { TTf__this = this };
    }

    [DebuggerHidden]
    private IEnumerator CancelUpgrading()
    {
        return new TCancelUpgradingTc__Iterator62 { TTf__this = this };
    }

    private static ToolBase.ToolErrors CanCreateSegment(NetInfo segmentInfo, ushort startNode, ushort upgrading, Vector3 endPos, Vector3 startDir, Vector3 endDir, ulong[] collidingSegmentBuffer)
    {
        ToolBase.ToolErrors none = ToolBase.ToolErrors.None;
        NetManager instance = Singleton<NetManager>.instance;
        for (int i = 0; i < 8; i++)
        {
            ushort segment = instance.m_nodes.m_buffer[startNode].GetSegment(i);
            if ((segment != 0) && (segment != upgrading))
            {
                bool flag2;
                bool flag3;
                Vector3 vector5;
                Vector3 vector6;
                Vector3 vector7;
                Vector3 vector8;
                Vector3 vector9;
                Vector3 vector10;
                Vector3 vector11;
                Vector3 vector12;
                NetInfo info = instance.m_segments.m_buffer[segment].Info;
                bool flag = instance.m_segments.m_buffer[segment].m_startNode == startNode;
                ushort index = !flag ? instance.m_segments.m_buffer[segment].m_endNode : instance.m_segments.m_buffer[segment].m_startNode;
                ushort num4 = !flag ? instance.m_segments.m_buffer[segment].m_startNode : instance.m_segments.m_buffer[segment].m_endNode;
                Vector3 position = instance.m_nodes.m_buffer[index].m_position;
                Vector3 vector2 = instance.m_nodes.m_buffer[num4].m_position;
                Vector3 vector3 = !flag ? instance.m_segments.m_buffer[segment].m_endDirection : instance.m_segments.m_buffer[segment].m_startDirection;
                Vector3 vector4 = !flag ? instance.m_segments.m_buffer[segment].m_startDirection : instance.m_segments.m_buffer[segment].m_endDirection;
                NetSegment.CalculateCorner(info, position, vector2, vector3, vector4, segmentInfo, endPos, startDir, endDir, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, index, false, true, out vector5, out vector6, out flag2);
                NetSegment.CalculateCorner(info, position, vector2, vector3, vector4, segmentInfo, endPos, startDir, endDir, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, index, false, false, out vector7, out vector8, out flag2);
                NetSegment.CalculateCorner(info, vector2, position, vector4, vector3, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, num4, false, false, out vector9, out vector10, out flag3);
                NetSegment.CalculateCorner(info, vector2, position, vector4, vector3, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, num4, false, true, out vector11, out vector12, out flag3);
                if ((((vector9.x - vector5.x) * vector3.x) + ((vector9.z - vector5.z) * vector3.z)) < 2f)
                {
                    collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                    none |= ToolBase.ToolErrors.FirstVisibleError;
                }
                if ((((vector5.x - vector9.x) * vector4.x) + ((vector5.z - vector9.z) * vector4.z)) < 2f)
                {
                    collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                    none |= ToolBase.ToolErrors.FirstVisibleError;
                }
                if ((((vector11.x - vector7.x) * vector3.x) + ((vector11.z - vector7.z) * vector3.z)) < 2f)
                {
                    collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                    none |= ToolBase.ToolErrors.FirstVisibleError;
                }
                if ((((vector7.x - vector11.x) * vector4.x) + ((vector7.z - vector11.z) * vector4.z)) < 2f)
                {
                    collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                    none |= ToolBase.ToolErrors.FirstVisibleError;
                }
                if ((((VectorUtils.LengthSqrXZ(vector9 - vector5) * info.m_maxSlope) * info.m_maxSlope) * 4f) < ((vector9.y - vector5.y) * (vector9.y - vector5.y)))
                {
                    collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                    none |= ToolBase.ToolErrors.SlopeTooSteep;
                }
                if ((((VectorUtils.LengthSqrXZ(vector11 - vector7) * info.m_maxSlope) * info.m_maxSlope) * 4f) < ((vector11.y - vector7.y) * (vector11.y - vector7.y)))
                {
                    collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                    none |= ToolBase.ToolErrors.SlopeTooSteep;
                }
            }
        }
        return none;
    }

    private static ToolBase.ToolErrors CanCreateSegment(NetInfo segmentInfo, ushort startNode, ushort startSegment, ushort endNode, ushort endSegment, ushort upgrading, Vector3 startPos, Vector3 endPos, Vector3 startDir, Vector3 endDir, ulong[] collidingSegmentBuffer)
    {
        bool flag;
        bool flag2;
        Vector3 vector;
        Vector3 vector2;
        Vector3 vector3;
        Vector3 vector4;
        Vector3 vector11;
        Vector3 vector12;
        Vector3 vector13;
        Vector3 vector14;
        ToolBase.ToolErrors none = ToolBase.ToolErrors.None;
        NetManager instance = Singleton<NetManager>.instance;
        if ((startSegment != 0) && (startNode == 0))
        {
            Vector3 vector5;
            Vector3 vector6;
            NetInfo info = instance.m_segments.m_buffer[startSegment].Info;
            instance.m_segments.m_buffer[startSegment].GetClosestPositionAndDirection(startPos, out vector5, out vector6);
            vector6 = VectorUtils.NormalizeXZ(vector6);
            ushort index = instance.m_segments.m_buffer[startSegment].m_startNode;
            ushort num2 = instance.m_segments.m_buffer[startSegment].m_endNode;
            Vector3 position = instance.m_nodes.m_buffer[index].m_position;
            Vector3 vector8 = instance.m_nodes.m_buffer[num2].m_position;
            Vector3 startDirection = instance.m_segments.m_buffer[startSegment].m_startDirection;
            Vector3 endDirection = instance.m_segments.m_buffer[startSegment].m_endDirection;
            NetSegment.CalculateCorner(segmentInfo, startPos, endPos, startDir, endDir, info, position, -vector6, startDirection, info, vector8, vector6, endDirection, 0, 0, false, true, out vector, out vector2, out flag);
            NetSegment.CalculateCorner(segmentInfo, startPos, endPos, startDir, endDir, info, position, -vector6, startDirection, info, vector8, vector6, endDirection, 0, 0, false, false, out vector3, out vector4, out flag);
            none |= CanCreateSegment(startSegment, index, endNode, info, startPos, position, -vector6, startDirection, segmentInfo, endPos, startDir, endDir, collidingSegmentBuffer);
            none |= CanCreateSegment(startSegment, num2, endNode, info, startPos, vector8, vector6, endDirection, segmentInfo, endPos, startDir, endDir, collidingSegmentBuffer);
        }
        else
        {
            NetSegment.CalculateCorner(segmentInfo, startPos, endPos, startDir, endDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, upgrading, startNode, false, true, out vector, out vector2, out flag);
            NetSegment.CalculateCorner(segmentInfo, startPos, endPos, startDir, endDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, upgrading, startNode, false, false, out vector3, out vector4, out flag);
            if (startNode != 0)
            {
                none |= CanCreateSegment(segmentInfo, startNode, upgrading, endPos, startDir, endDir, collidingSegmentBuffer);
            }
        }
        if ((endSegment != 0) && (endNode == 0))
        {
            Vector3 vector15;
            Vector3 vector16;
            NetInfo info2 = instance.m_segments.m_buffer[endSegment].Info;
            instance.m_segments.m_buffer[endSegment].GetClosestPositionAndDirection(startPos, out vector15, out vector16);
            vector16 = VectorUtils.NormalizeXZ(vector16);
            ushort num3 = instance.m_segments.m_buffer[endSegment].m_startNode;
            ushort num4 = instance.m_segments.m_buffer[endSegment].m_endNode;
            Vector3 vector17 = instance.m_nodes.m_buffer[num3].m_position;
            Vector3 vector18 = instance.m_nodes.m_buffer[num4].m_position;
            Vector3 vector19 = instance.m_segments.m_buffer[endSegment].m_startDirection;
            Vector3 vector20 = instance.m_segments.m_buffer[endSegment].m_endDirection;
            NetSegment.CalculateCorner(segmentInfo, endPos, startPos, endDir, startDir, info2, vector17, -vector16, vector19, info2, vector18, vector16, vector20, 0, 0, false, false, out vector11, out vector12, out flag2);
            NetSegment.CalculateCorner(segmentInfo, endPos, startPos, endDir, startDir, info2, vector17, -vector16, vector19, info2, vector18, vector16, vector20, 0, 0, false, true, out vector13, out vector14, out flag2);
            none |= CanCreateSegment(endSegment, num3, startNode, info2, endPos, vector17, -vector16, vector19, segmentInfo, startPos, endDir, startDir, collidingSegmentBuffer);
            none |= CanCreateSegment(endSegment, num4, startNode, info2, endPos, vector18, vector16, vector20, segmentInfo, startPos, endDir, startDir, collidingSegmentBuffer);
        }
        else
        {
            NetSegment.CalculateCorner(segmentInfo, endPos, startPos, endDir, startDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, upgrading, endNode, false, false, out vector11, out vector12, out flag2);
            NetSegment.CalculateCorner(segmentInfo, endPos, startPos, endDir, startDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, upgrading, endNode, false, true, out vector13, out vector14, out flag2);
            if (endNode != 0)
            {
                none |= CanCreateSegment(segmentInfo, endNode, upgrading, startPos, endDir, startDir, collidingSegmentBuffer);
            }
        }
        if ((((vector11.x - vector.x) * startDir.x) + ((vector11.z - vector.z) * startDir.z)) < 2f)
        {
            none |= ToolBase.ToolErrors.TooShort;
        }
        if ((((vector.x - vector11.x) * endDir.x) + ((vector.z - vector11.z) * endDir.z)) < 2f)
        {
            none |= ToolBase.ToolErrors.TooShort;
        }
        if ((((vector13.x - vector3.x) * startDir.x) + ((vector13.z - vector3.z) * startDir.z)) < 2f)
        {
            none |= ToolBase.ToolErrors.TooShort;
        }
        if ((((vector3.x - vector13.x) * endDir.x) + ((vector3.z - vector13.z) * endDir.z)) < 2f)
        {
            none |= ToolBase.ToolErrors.TooShort;
        }
        if ((((VectorUtils.LengthSqrXZ(vector11 - vector) * segmentInfo.m_maxSlope) * segmentInfo.m_maxSlope) * 4f) < ((vector11.y - vector.y) * (vector11.y - vector.y)))
        {
            none |= ToolBase.ToolErrors.SlopeTooSteep;
        }
        if ((((VectorUtils.LengthSqrXZ(vector13 - vector3) * segmentInfo.m_maxSlope) * segmentInfo.m_maxSlope) * 4f) < ((vector13.y - vector3.y) * (vector13.y - vector3.y)))
        {
            none |= ToolBase.ToolErrors.SlopeTooSteep;
        }
        return none;
    }

    private static ToolBase.ToolErrors CanCreateSegment(ushort segment, ushort endNode, ushort otherNode, NetInfo info1, Vector3 startPos, Vector3 endPos, Vector3 startDir, Vector3 endDir, NetInfo info2, Vector3 endPos2, Vector3 startDir2, Vector3 endDir2, ulong[] collidingSegmentBuffer)
    {
        ToolBase.ToolErrors none = ToolBase.ToolErrors.None;
        NetManager instance = Singleton<NetManager>.instance;
        bool flag = true;
        if ((instance.m_nodes.m_buffer[endNode].m_flags & (NetNode.Flags.Untouchable | NetNode.Flags.Moveable | NetNode.Flags.Middle)) == (NetNode.Flags.Moveable | NetNode.Flags.Middle))
        {
            for (int i = 0; i < 8; i++)
            {
                ushort num2 = instance.m_nodes.m_buffer[endNode].GetSegment(i);
                if ((num2 != 0) && (num2 != segment))
                {
                    ushort startNode;
                    segment = num2;
                    info1 = instance.m_segments.m_buffer[segment].Info;
                    if (instance.m_segments.m_buffer[segment].m_startNode == endNode)
                    {
                        startNode = instance.m_segments.m_buffer[segment].m_endNode;
                        endDir = instance.m_segments.m_buffer[segment].m_endDirection;
                    }
                    else
                    {
                        startNode = instance.m_segments.m_buffer[segment].m_startNode;
                        endDir = instance.m_segments.m_buffer[segment].m_startDirection;
                    }
                    if (startNode == otherNode)
                    {
                        flag = false;
                    }
                    else
                    {
                        endNode = startNode;
                        endPos = instance.m_nodes.m_buffer[endNode].m_position;
                    }
                    break;
                }
            }
        }
        if (!flag || ((instance.m_nodes.m_buffer[endNode].m_flags & (NetNode.Flags.Untouchable | NetNode.Flags.Moveable | NetNode.Flags.Middle)) != (NetNode.Flags.Moveable | NetNode.Flags.Middle)))
        {
            bool flag2;
            bool flag3;
            Vector3 vector;
            Vector3 vector2;
            Vector3 vector3;
            Vector3 vector4;
            Vector3 vector5;
            Vector3 vector6;
            Vector3 vector7;
            Vector3 vector8;
            NetSegment.CalculateCorner(info1, startPos, endPos, startDir, endDir, info2, endPos2, startDir2, endDir2, null, Vector3.zero, Vector3.zero, Vector3.zero, 0, 0, false, true, out vector, out vector2, out flag2);
            NetSegment.CalculateCorner(info1, startPos, endPos, startDir, endDir, info2, endPos2, startDir2, endDir2, null, Vector3.zero, Vector3.zero, Vector3.zero, 0, 0, false, false, out vector3, out vector4, out flag2);
            NetSegment.CalculateCorner(info1, endPos, startPos, endDir, startDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, endNode, false, false, out vector5, out vector6, out flag3);
            NetSegment.CalculateCorner(info1, endPos, startPos, endDir, startDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, endNode, false, true, out vector7, out vector8, out flag3);
            if ((((vector5.x - vector.x) * startDir.x) + ((vector5.z - vector.z) * startDir.z)) < 2f)
            {
                collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                none |= ToolBase.ToolErrors.FirstVisibleError;
            }
            if ((((vector.x - vector5.x) * endDir.x) + ((vector.z - vector5.z) * endDir.z)) < 2f)
            {
                collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                none |= ToolBase.ToolErrors.FirstVisibleError;
            }
            if ((((vector7.x - vector3.x) * startDir.x) + ((vector7.z - vector3.z) * startDir.z)) < 2f)
            {
                collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                none |= ToolBase.ToolErrors.FirstVisibleError;
            }
            if ((((vector3.x - vector7.x) * endDir.x) + ((vector3.z - vector7.z) * endDir.z)) < 2f)
            {
                collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                none |= ToolBase.ToolErrors.FirstVisibleError;
            }
            if ((((VectorUtils.LengthSqrXZ(vector5 - vector) * info1.m_maxSlope) * info1.m_maxSlope) * 4f) < ((vector5.y - vector.y) * (vector5.y - vector.y)))
            {
                collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                none |= ToolBase.ToolErrors.SlopeTooSteep;
            }
            if ((((VectorUtils.LengthSqrXZ(vector7 - vector3) * info1.m_maxSlope) * info1.m_maxSlope) * 4f) < ((vector7.y - vector3.y) * (vector7.y - vector3.y)))
            {
                collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                none |= ToolBase.ToolErrors.SlopeTooSteep;
            }
        }
        return none;
    }

    [DebuggerHidden]
    private IEnumerator<bool> ChangeElevation(int delta)
    {
        return new TChangeElevationTc__Iterator63 { delta = delta, TSTdelta = delta, f_this = this };
    }

    private static void CheckCollidingNode(ushort node, ulong[] segmentMask, ulong[] buildingMask)
    {
        NetManager instance = Singleton<NetManager>.instance;
        ushort building = instance.m_nodes.m_buffer[node].m_building;
        if (building != 0)
        {
            for (int i = 0; i < 8; i++)
            {
                ushort segment = instance.m_nodes.m_buffer[node].GetSegment(i);
                if ((segment != 0) && ((segmentMask[segment >> 6] & (((ulong) 1L) << segment)) == 0))
                {
                    return;
                }
            }
            buildingMask[building >> 6] &= (ulong) ~(((long) 1L) << building);
        }
    }

    public static bool CheckCollidingSegments(ulong[] segmentMask, ulong[] buildingMask, ushort upgrading)
    {
        NetManager instance = Singleton<NetManager>.instance;
        int length = segmentMask.Length;
        bool flag = false;
        for (int i = 0; i < length; i++)
        {
            ulong num3 = segmentMask[i];
            if (num3 != 0)
            {
                for (int j = 0; j < 0x40; j++)
                {
                    if ((num3 & (((ulong) 1L) << j)) != 0)
                    {
                        int index = (i << 6) | j;
                        if (index != upgrading)
                        {
                            NetInfo info = instance.m_segments.m_buffer[index].Info;
                            if (((info.m_class.m_service > ItemClass.Service.Office) && !info.m_autoRemove) || ((instance.m_segments.m_buffer[index].m_flags & NetSegment.Flags.Untouchable) != NetSegment.Flags.None))
                            {
                                flag = true;
                            }
                            else
                            {
                                CheckCollidingNode(instance.m_segments.m_buffer[index].m_startNode, segmentMask, buildingMask);
                                CheckCollidingNode(instance.m_segments.m_buffer[index].m_endNode, segmentMask, buildingMask);
                            }
                        }
                        else
                        {
                            segmentMask[index >> 6] &= (ulong) ~(((long) 1L) << index);
                        }
                    }
                }
            }
        }
        return flag;
    }

    private static ToolBase.ToolErrors CheckNodeHeights(NetInfo info, FastList<NetTool.NodePosition> nodeBuffer)
    {
        bool flag2;
        int prefabMinElevation;
        int prefabMaxElevation;
        bool flag = info.m_netAI.BuildUnderground();
        int num = 0;
        do
        {
            flag2 = false;
            for (int m = 1; m < nodeBuffer.m_size; m++)
            {
                NetTool.NodePosition prevPosition = nodeBuffer.m_buffer[m - 1];
                NetTool.NodePosition thisPosition = nodeBuffer.m_buffer[m];
                float maxDifference = VectorUtils.LengthXZ(thisPosition.m_position - prevPosition.m_position) * info.m_maxSlope;
                thisPosition.m_minY = Mathf.Max(thisPosition.m_minY, prevPosition.m_minY - maxDifference);
                thisPosition.m_maxY = Mathf.Min(thisPosition.m_maxY, prevPosition.m_maxY + maxDifference);
                nodeBuffer.m_buffer[m] = thisPosition;
            }
            for (int n = nodeBuffer.m_size - 2; n >= 0; n--)
            {
                NetTool.NodePosition position3 = nodeBuffer.m_buffer[n + 1];
                NetTool.NodePosition position4 = nodeBuffer.m_buffer[n];
                float num7 = VectorUtils.LengthXZ(position4.m_position - position3.m_position) * info.m_maxSlope;
                position4.m_minY = Mathf.Max(position4.m_minY, position3.m_minY - num7);
                position4.m_maxY = Mathf.Min(position4.m_maxY, position3.m_maxY + num7);
                nodeBuffer.m_buffer[n] = position4;
            }
            for (int num8 = 0; num8 < nodeBuffer.m_size; num8++)
            {
                NetTool.NodePosition position5 = nodeBuffer.m_buffer[num8];
                if (position5.m_minY > position5.m_maxY)
                {
                    return ToolBase.ToolErrors.SlopeTooSteep;
                }
                if (position5.m_position.y > position5.m_maxY)
                {
                    position5.m_position.y = position5.m_maxY;
                    if (!flag)
                    {
                        position5.m_minY = position5.m_maxY;
                    }
                    flag2 = true;
                }
                else if (position5.m_position.y < position5.m_minY)
                {
                    position5.m_position.y = position5.m_minY;
                    if (flag)
                    {
                        position5.m_maxY = position5.m_minY;
                    }
                    flag2 = true;
                }
                nodeBuffer.m_buffer[num8] = position5;
            }
            if (num++ == (nodeBuffer.m_size << 1))
            {
                return ToolBase.ToolErrors.SlopeTooSteep;
            }
        }
        while (flag2);
        for (int i = 1; i < (nodeBuffer.m_size - 1); i++)
        {
            NetTool.NodePosition position6 = nodeBuffer.m_buffer[i - 1];
            NetTool.NodePosition position7 = nodeBuffer.m_buffer[i];
            float num11 = VectorUtils.LengthXZ(position7.m_position - position6.m_position) * info.m_maxSlope;
            if (flag)
            {
                if (position7.m_position.y > (position6.m_position.y + num11))
                {
                    position7.m_position.y = position6.m_position.y + num11;
                }
            }
            else if (position7.m_position.y < (position6.m_position.y - num11))
            {
                position7.m_position.y = position6.m_position.y - num11;
            }
            nodeBuffer.m_buffer[i] = position7;
        }
        for (int j = nodeBuffer.m_size - 2; j > 0; j--)
        {
            NetTool.NodePosition position8 = nodeBuffer.m_buffer[j + 1];
            NetTool.NodePosition position9 = nodeBuffer.m_buffer[j];
            float num14 = VectorUtils.LengthXZ(position9.m_position - position8.m_position) * info.m_maxSlope;
            if (flag)
            {
                if (position9.m_position.y > (position8.m_position.y + num14))
                {
                    position9.m_position.y = position8.m_position.y + num14;
                }
            }
            else if (position9.m_position.y < (position8.m_position.y - num14))
            {
                position9.m_position.y = position8.m_position.y - num14;
            }
            nodeBuffer.m_buffer[j] = position9;
        }
        info.m_netAI.GetElevationLimits(out prefabMinElevation, out prefabMaxElevation);
        if (prefabMaxElevation > prefabMinElevation)
        {
            int nextIndex;
            for (int index = 0; index < (nodeBuffer.m_size - 1); index = nextIndex)
            {
                NetTool.NodePosition thisPosition = nodeBuffer.m_buffer[index];
                nextIndex = index + 1;
                float b = 0f;
                while (nextIndex < nodeBuffer.m_size)
                {
                    NetTool.NodePosition nextPosition = nodeBuffer.m_buffer[nextIndex];
                    b += VectorUtils.LengthXZ(nextPosition.m_position - thisPosition.m_position);
                    if (nextPosition.m_position.y < (nextPosition.m_terrainHeight + 8f))
                    {
                        break;
                    }
                    thisPosition = nextPosition;
                    if (nextIndex == (nodeBuffer.m_size - 1))
                    {
                        break;
                    }
                    nextIndex++;
                }
                float y = nodeBuffer.m_buffer[index].m_position.y;
                float to = nodeBuffer.m_buffer[nextIndex].m_position.y;
                thisPosition = nodeBuffer.m_buffer[index];
                float num22 = 0f;
                b = Mathf.Max(1f, b);
                for (int num23 = index + 1; num23 < nextIndex; num23++)
                {
                    NetTool.NodePosition position12 = nodeBuffer.m_buffer[num23];
                    num22 += VectorUtils.LengthXZ(position12.m_position - thisPosition.m_position);
                    position12.m_position.y = Mathf.Max(position12.m_position.y, Mathf.Lerp(y, to, num22 / b));
                    nodeBuffer.m_buffer[num23] = position12;
                    thisPosition = position12;
                }
            }
        }
        ToolBase.ToolErrors none = ToolBase.ToolErrors.None;
        for (int k = 1; k < (nodeBuffer.m_size - 1); k++)
        {
            NetTool.NodePosition position13 = nodeBuffer.m_buffer[k - 1];
            NetTool.NodePosition position14 = nodeBuffer.m_buffer[k + 1];
            NetTool.NodePosition position15 = nodeBuffer.m_buffer[k];
            if (flag)
            {
                if (position15.m_terrainHeight < position15.m_position.y)
                {
                    none |= ToolBase.ToolErrors.SlopeTooSteep;
                }
            }
            else if (position15.m_terrainHeight > (position15.m_position.y + 8f))
            {
                none |= ToolBase.ToolErrors.SlopeTooSteep;
            }
            Vector3 vector = VectorUtils.NormalizeXZ(position14.m_position - position13.m_position);
            position15.m_direction.y = vector.y;
            nodeBuffer.m_buffer[k] = position15;
        }
        return none;
    }

    public static void CheckOverlayAlpha(ref NetSegment segment, ref float alpha)
    {
        NetInfo info = segment.Info;
        if ((info != null) && (((segment.m_flags & NetSegment.Flags.Untouchable) == NetSegment.Flags.None) || info.m_overlayVisible))
        {
            CheckOverlayAlpha(info, ref alpha);
        }
    }

    public static void CheckOverlayAlpha(NetInfo info, ref float alpha)
    {
        if (info != null)
        {
            alpha = Mathf.Min(alpha, 2f / Mathf.Max(1f, Mathf.Sqrt(info.m_halfWidth)));
        }
    }

    private static bool CheckStartAndEnd(ushort upgrading, ushort startSegment, ushort startNode, ushort endSegment, ushort endNode, ulong[] collidingSegmentBuffer)
    {
        bool flag = true;
        if ((startSegment != 0) && (endSegment != 0))
        {
            NetManager instance = Singleton<NetManager>.instance;
            ushort num = instance.m_segments.m_buffer[startSegment].m_startNode;
            ushort num2 = instance.m_segments.m_buffer[startSegment].m_endNode;
            ushort num3 = instance.m_segments.m_buffer[endSegment].m_startNode;
            ushort num4 = instance.m_segments.m_buffer[endSegment].m_endNode;
            if (((startSegment == endSegment) || (num == num3)) || (((num == num4) || (num2 == num3)) || (num2 == num4)))
            {
                if (collidingSegmentBuffer != null)
                {
                    collidingSegmentBuffer[startSegment >> 6] |= ((ulong) 1L) << startSegment;
                    collidingSegmentBuffer[endSegment >> 6] |= ((ulong) 1L) << endSegment;
                }
                flag = false;
            }
        }
        if ((startSegment != 0) && (endNode != 0))
        {
            NetManager manager2 = Singleton<NetManager>.instance;
            if ((manager2.m_segments.m_buffer[startSegment].m_startNode == endNode) || (manager2.m_segments.m_buffer[startSegment].m_endNode == endNode))
            {
                if (collidingSegmentBuffer != null)
                {
                    collidingSegmentBuffer[startSegment >> 6] |= ((ulong) 1L) << startSegment;
                }
                flag = false;
            }
        }
        if ((endSegment != 0) && (startNode != 0))
        {
            NetManager manager3 = Singleton<NetManager>.instance;
            if ((manager3.m_segments.m_buffer[endSegment].m_startNode == startNode) || (manager3.m_segments.m_buffer[endSegment].m_endNode == startNode))
            {
                if (collidingSegmentBuffer != null)
                {
                    collidingSegmentBuffer[endSegment >> 6] |= ((ulong) 1L) << endSegment;
                }
                flag = false;
            }
        }
        if (((startNode != 0) && (endNode != 0)) && (upgrading == 0))
        {
            NetManager manager4 = Singleton<NetManager>.instance;
            for (int i = 0; i < 8; i++)
            {
                ushort segment = manager4.m_nodes.m_buffer[startNode].GetSegment(i);
                if (segment != 0)
                {
                    ushort num7 = manager4.m_segments.m_buffer[segment].m_startNode;
                    ushort num8 = manager4.m_segments.m_buffer[segment].m_endNode;
                    if (((num7 == startNode) && (num8 == endNode)) || ((num7 == endNode) && (num8 == startNode)))
                    {
                        if (collidingSegmentBuffer != null)
                        {
                            collidingSegmentBuffer[segment >> 6] |= ((ulong) 1L) << segment;
                        }
                        flag = false;
                    }
                }
            }
        }
        return flag;
    }

    [DebuggerHidden]
    private IEnumerator CreateFailed()
    {
        return new TCreateFailedTc__Iterator60 { TTf__this = this };
    }

    [DebuggerHidden]
    private IEnumerator<bool> CreateNode(bool switchDirection)
    {
        return new TCreateNodeTc__Iterator5F { switchDirection = switchDirection, TSTswitchDirection = switchDirection, TTf__this = this };
    }

    public static ToolBase.ToolErrors CreateNode(NetInfo info, NetTool.ControlPoint startPoint, NetTool.ControlPoint middlePoint, NetTool.ControlPoint endPoint, FastList<NetTool.NodePosition> nodeBuffer, int maxSegments, bool test, bool visualize, bool autoFix, bool needMoney, bool invert, bool switchDir, ushort relocateBuildingID, out ushort node, out ushort segment, out int cost, out int productionRate)
    {
        ushort num;
        ushort num2;
        ToolBase.ToolErrors errors = CreateNode(info, startPoint, middlePoint, endPoint, nodeBuffer, maxSegments, test, visualize, autoFix, needMoney, invert, switchDir, relocateBuildingID, out num, out num2, out segment, out cost, out productionRate);
        if (errors == ToolBase.ToolErrors.None)
        {
            if (num2 != 0)
            {
                node = num2;
                return errors;
            }
            node = num;
            return errors;
        }
        node = 0;
        return errors;
    }

    public static ToolBase.ToolErrors CreateNode(NetInfo info, NetTool.ControlPoint startPoint, NetTool.ControlPoint middlePoint, NetTool.ControlPoint endPoint, FastList<NetTool.NodePosition> nodeBuffer, int maxSegments, bool test, bool visualize, bool autoFix, bool needMoney, bool invert, bool switchDir, ushort relocateBuildingID, out ushort firstNode, out ushort lastNode, out ushort segment, out int cost, out int productionRate)
    {
        ToolBase.ToolErrors errors3;
        ushort index = middlePoint.m_segment;
        NetInfo oldInfo = null;
        if ((startPoint.m_segment == index) || (endPoint.m_segment == index))
        {
            index = 0;
        }
        uint currentBuildIndex = Singleton<SimulationManager>.instance.m_currentBuildIndex;
        bool flag = invert;
        bool smoothStart = true;
        bool smoothEnd = true;
        if (index != 0)
        {
            maxSegments = Mathf.Min(1, maxSegments);
            cost = -Singleton<NetManager>.instance.m_segments.m_buffer[index].Info.m_netAI.GetConstructionCost(startPoint.m_position, endPoint.m_position, startPoint.m_elevation, endPoint.m_elevation);
            currentBuildIndex = Singleton<NetManager>.instance.m_segments.m_buffer[index].m_buildIndex;
            smoothStart = (Singleton<NetManager>.instance.m_nodes.m_buffer[startPoint.m_node].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None;
            smoothEnd = (Singleton<NetManager>.instance.m_nodes.m_buffer[endPoint.m_node].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None;
            autoFix = false;
            if (switchDir)
            {
                flag = !flag;
                info = Singleton<NetManager>.instance.m_segments.m_buffer[index].Info;
            }
            if (!test && !visualize)
            {
                if ((Singleton<NetManager>.instance.m_segments.m_buffer[index].m_flags & NetSegment.Flags.Invert) != NetSegment.Flags.None)
                {
                    flag = !flag;
                }
                oldInfo = Singleton<NetManager>.instance.m_segments.m_buffer[index].Info;
                Singleton<NetManager>.instance.ReleaseSegment(index, true);
                index = 0;
            }
        }
        else
        {
            if (autoFix && (Singleton<SimulationManager>.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.True))
            {
                flag = !flag;
            }
            cost = 0;
        }
        ToolController properties = Singleton<ToolManager>.instance.m_properties;
        ulong[] collidingSegments = null;
        ulong[] collidingBuildings = null;
        ToolBase.ToolErrors none = ToolBase.ToolErrors.None;
        if (test || !visualize)
        {
            properties.BeginColliding(out collidingSegments, out collidingBuildings);
        }
        try
        {
            BuildingInfo info3;
            Vector3 zero;
            Vector3 forward;
            Vector3 vector7;
            Vector3 vector8;
            NetTool.NodePosition position;
            NetInfo info5;
            BuildingInfo info7;
            float num21;
            ushort building = 0;
            if ((index != 0) && switchDir)
            {
                info3 = null;
                zero = Vector3.zero;
                forward = Vector3.forward;
                productionRate = 0;
                if (info.m_hasForwardVehicleLanes == info.m_hasBackwardVehicleLanes)
                {
                    none |= ToolBase.ToolErrors.CannotUpgrade;
                }
            }
            else
            {
                none |= info.m_netAI.CheckBuildPosition(test, visualize, false, autoFix, ref startPoint, ref middlePoint, ref endPoint, out info3, out zero, out forward, out productionRate);
            }
            if (test)
            {
                Vector3 vector3 = middlePoint.m_direction;
                Vector3 vector4 = -endPoint.m_direction;
                if (((maxSegments != 0) && (index == 0)) && (((vector3.x * vector4.x) + (vector3.z * vector4.z)) >= 0.8f))
                {
                    none |= ToolBase.ToolErrors.InvalidShape;
                }
                if ((maxSegments != 0) && !CheckStartAndEnd(index, startPoint.m_segment, startPoint.m_node, endPoint.m_segment, endPoint.m_node, collidingSegments))
                {
                    none |= ToolBase.ToolErrors.FirstVisibleError;
                }
                if (startPoint.m_node != 0)
                {
                    if ((maxSegments != 0) && !CanAddSegment(startPoint.m_node, vector3, collidingSegments, index))
                    {
                        none |= ToolBase.ToolErrors.FirstVisibleError;
                    }
                }
                else if ((startPoint.m_segment != 0) && !CanAddNode(startPoint.m_segment, startPoint.m_position, vector3, maxSegments != 0, collidingSegments))
                {
                    none |= ToolBase.ToolErrors.FirstVisibleError;
                }
                if (endPoint.m_node != 0)
                {
                    if ((maxSegments != 0) && !CanAddSegment(endPoint.m_node, vector4, collidingSegments, index))
                    {
                        none |= ToolBase.ToolErrors.FirstVisibleError;
                    }
                }
                else if ((endPoint.m_segment != 0) && !CanAddNode(endPoint.m_segment, endPoint.m_position, vector4, maxSegments != 0, collidingSegments))
                {
                    none |= ToolBase.ToolErrors.FirstVisibleError;
                }
                if (!Singleton<NetManager>.instance.CheckLimits())
                {
                    none |= ToolBase.ToolErrors.LastVisibleError;
                }
            }
            if (info3 != null)
            {
                if (visualize)
                {
                    RenderNodeBuilding(info3, zero, forward);
                }
                else if (test)
                {
                    none |= TestNodeBuilding(info3, zero, forward, 0, 0, 0, test, collidingSegments, collidingBuildings);
                }
                else
                {
                    float angle = Mathf.Atan2(-forward.x, forward.z);
                    if (Singleton<BuildingManager>.instance.CreateBuilding(out building, ref Singleton<SimulationManager>.instance.m_randomizer, info3, zero, angle, 0, Singleton<SimulationManager>.instance.m_currentBuildIndex))
                    {
                        Singleton<BuildingManager>.instance.m_buildings.m_buffer[building].m_flags |= Building.Flags.FixedHeight;
                        SimulationManager instance = Singleton<SimulationManager>.instance;
                        instance.m_currentBuildIndex++;
                    }
                }
            }
            bool curved = ((middlePoint.m_direction.x * endPoint.m_direction.x) + (middlePoint.m_direction.z * endPoint.m_direction.z)) <= 0.999f;
            Vector2 vector21 = new Vector2(startPoint.m_position.x - middlePoint.m_position.x, startPoint.m_position.z - middlePoint.m_position.z);
            float magnitude = vector21.magnitude;
            Vector2 vector22 = new Vector2(middlePoint.m_position.x - endPoint.m_position.x, middlePoint.m_position.z - endPoint.m_position.z);
            float num6 = vector22.magnitude;
            float length = magnitude + num6;
            if (test && (maxSegments != 0))
            {
                float num8 = 7f;
                if (curved && (index == 0))
                {
                    if (magnitude < num8)
                    {
                        none |= ToolBase.ToolErrors.TooShort;
                    }
                    if (num6 < num8)
                    {
                        none |= ToolBase.ToolErrors.TooShort;
                    }
                }
                else if (length < num8)
                {
                    none |= ToolBase.ToolErrors.TooShort;
                }
            }
            segment = 0;
            int num9 = Mathf.Min(maxSegments, Mathf.FloorToInt(length / 100f) + 1);
            ushort num10 = startPoint.m_node;
            Vector3 vector5 = startPoint.m_position;
            Vector3 direction = middlePoint.m_direction;
            NetSegment.CalculateMiddlePoints(startPoint.m_position, middlePoint.m_direction, endPoint.m_position, -endPoint.m_direction, smoothStart, smoothEnd, out vector7, out vector8);
            nodeBuffer.Clear();
            position.m_position = vector5;
            position.m_direction = direction;
            position.m_minY = vector5.y;
            position.m_maxY = vector5.y;
            position.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(position.m_position);
            position.m_elevation = startPoint.m_elevation;
            if (position.m_elevation < (terrainStep-1))
            {
                position.m_elevation = 0f;
            }
            position.m_nodeInfo = info.m_netAI.GetInfo(position.m_position.y - position.m_terrainHeight, length, startPoint.m_outside, false, curved, num9 >= 2, ref none);
            position.m_double = false;
            nodeBuffer.Add(position);
            for (int i = 1; i <= num9; i++)
            {
                position.m_elevation = Mathf.Lerp(startPoint.m_elevation, endPoint.m_elevation, ((float) i) / ((float) num9));
                if (position.m_elevation < (terrainStep - 1))
                {
                    position.m_elevation = 0f;
                }
                if (i == num9)
                {
                    position.m_position = endPoint.m_position;
                    position.m_direction = endPoint.m_direction;
                    position.m_minY = endPoint.m_position.y;
                    position.m_maxY = endPoint.m_position.y;
                    position.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(position.m_position);
                }
                else if (curved)
                {
                    position.m_position = Bezier3.Position(startPoint.m_position, vector7, vector8, endPoint.m_position, ((float) i) / ((float) num9));
                    position.m_direction = Bezier3.Tangent(startPoint.m_position, vector7, vector8, endPoint.m_position, ((float) i) / ((float) num9));
                    float introduced106 = NetSegment.SampleTerrainHeight(info, position.m_position, visualize);
                    position.m_position.y = introduced106 + position.m_elevation;
                    position.m_direction = VectorUtils.NormalizeXZ(position.m_direction);
                    position.m_minY = 0f;
                    position.m_maxY = 1280f;
                    position.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(position.m_position);
                }
                else
                {
                    float lengthSnap = info.m_netAI.GetLengthSnap();
                    position.m_position = LerpPosition(startPoint.m_position, endPoint.m_position, ((float) i) / ((float) num9), lengthSnap);
                    position.m_direction = endPoint.m_direction;
                    float introduced107 = NetSegment.SampleTerrainHeight(info, position.m_position, visualize);
                    position.m_position.y = introduced107 + position.m_elevation;
                    position.m_minY = 0f;
                    position.m_maxY = 1280f;
                    position.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(position.m_position);
                }
                position.m_nodeInfo = null;
                position.m_double = false;
                nodeBuffer.Add(position);
            }
            ToolBase.ToolErrors errors2 = CheckNodeHeights(info, nodeBuffer);
            if ((errors2 != ToolBase.ToolErrors.None) && test)
            {
                none |= errors2;
            }
            for (int j = 1; j <= num9; j++)
            {
                nodeBuffer.m_buffer[j].m_nodeInfo = info.m_netAI.GetInfo(nodeBuffer.m_buffer[j].m_position.y - nodeBuffer.m_buffer[j].m_terrainHeight, length, false, (j == num9) && endPoint.m_outside, curved, num9 >= 2, ref none);
            }
            int num14 = 1;
            int num15 = 0;
            for (NetInfo info4 = null; num14 <= num9; info4 = info5)
            {
                info5 = nodeBuffer.m_buffer[num14].m_nodeInfo;
                if ((num14 != num9) && (info5 == info4))
                {
                    num15++;
                    num14++;
                }
                else
                {
                    if ((num15 != 0) && info4.m_netAI.RequireDoubleSegments())
                    {
                        int num16 = (num14 - num15) - 1;
                        int num17 = num14;
                        if ((num15 & 1) == 0)
                        {
                            nodeBuffer.RemoveAt(num14 - 1);
                            num9--;
                            num17--;
                            for (int num18 = num16 + 1; num18 < num17; num18++)
                            {
                                float t = ((float) (num18 - num16)) / ((float) num15);
                                nodeBuffer.m_buffer[num18].m_position = Vector3.Lerp(nodeBuffer.m_buffer[num16].m_position, nodeBuffer.m_buffer[num17].m_position, t);
                                nodeBuffer.m_buffer[num18].m_direction = VectorUtils.NormalizeXZ(Vector3.Lerp(nodeBuffer.m_buffer[num16].m_direction, nodeBuffer.m_buffer[num17].m_direction, t));
                                nodeBuffer.m_buffer[num18].m_elevation = Mathf.Lerp(nodeBuffer.m_buffer[num16].m_elevation, nodeBuffer.m_buffer[num17].m_elevation, t);
                                nodeBuffer.m_buffer[num18].m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(nodeBuffer.m_buffer[num18].m_position);
                            }
                        }
                        else
                        {
                            num14++;
                        }
                        for (int n = num16 + 1; n < num17; n++)
                        {
                            nodeBuffer.m_buffer[n].m_double = ((n - num16) & 1) == 1;
                        }
                    }
                    else
                    {
                        num14++;
                    }
                    num15 = 1;
                }
            }
            NetTool.NodePosition position2 = nodeBuffer[0];
            NetInfo nodeInfo = position2.m_nodeInfo;
            bool flag5 = false;
            if (((num10 == 0) && !test) && !visualize)
            {
                if (startPoint.m_segment != 0)
                {
                    if (SplitSegment(startPoint.m_segment, out num10, vector5))
                    {
                        flag5 = true;
                    }
                    startPoint.m_segment = 0;
                }
                else if (Singleton<NetManager>.instance.CreateNode(out num10, ref Singleton<SimulationManager>.instance.m_randomizer, nodeInfo, vector5, Singleton<SimulationManager>.instance.m_currentBuildIndex))
                {
                    if (startPoint.m_outside)
                    {
                        Singleton<NetManager>.instance.m_nodes.m_buffer[num10].m_flags |= NetNode.Flags.Outside;
                    }
                    if ((vector5.y - nodeBuffer.m_buffer[0].m_terrainHeight) < (terrainStep - 1))
                    {
                        Singleton<NetManager>.instance.m_nodes.m_buffer[num10].m_flags |= NetNode.Flags.OnGround;
                    }
                    NetTool.NodePosition position3 = nodeBuffer[0];
                    Singleton<NetManager>.instance.m_nodes.m_buffer[num10].m_elevation = (byte) Mathf.Clamp(Mathf.RoundToInt(position3.m_elevation), 0, 0xff);
                    SimulationManager local2 = Singleton<SimulationManager>.instance;
                    local2.m_currentBuildIndex++;
                    flag5 = true;
                }
                startPoint.m_node = num10;
            }
            NetNode data = new NetNode {
                m_position = vector5
            };
            if ((vector5.y - nodeBuffer.m_buffer[0].m_terrainHeight) < (terrainStep - 1))
            {
                data.m_flags |= NetNode.Flags.OnGround;
            }
            if (startPoint.m_outside)
            {
                data.m_flags |= NetNode.Flags.Outside;
            }
            nodeInfo.m_netAI.GetNodeBuilding(0, ref data, out info7, out num21);
            if (visualize)
            {
                if ((info7 != null) && (num10 == 0))
                {
                    Vector3 vector9 = vector5;
                    vector9.y += num21;
                    RenderNodeBuilding(info7, vector9, direction);
                }
                if (nodeInfo.m_netAI.DisplayTempSegment())
                {
                    RenderNode(nodeInfo, vector5, direction);
                }
            }
            else if ((info7 != null) && ((data.m_flags & NetNode.Flags.Outside) == NetNode.Flags.None))
            {
                ushort node = startPoint.m_node;
                ushort ignoreSegment = startPoint.m_segment;
                ushort ignoredBuilding = GetIgnoredBuilding(startPoint);
                errors2 = TestNodeBuilding(info7, vector5, direction, node, ignoreSegment, ignoredBuilding, test, collidingSegments, collidingBuildings);
                if (errors2 != ToolBase.ToolErrors.None)
                {
                    none |= errors2;
                }
            }
            if (((building != 0) && (num10 != 0)) && ((Singleton<NetManager>.instance.m_nodes.m_buffer[num10].m_flags & NetNode.Flags.Untouchable) == NetNode.Flags.None))
            {
                Singleton<NetManager>.instance.m_nodes.m_buffer[num10].m_flags |= NetNode.Flags.Untouchable;
                Singleton<NetManager>.instance.m_nodes.m_buffer[num10].m_nextBuildingNode = Singleton<BuildingManager>.instance.m_buildings.m_buffer[building].m_netNode;
                Singleton<BuildingManager>.instance.m_buildings.m_buffer[building].m_netNode = num10;
            }
            for (int k = 1; k <= num9; k++)
            {
                Vector3 vector12;
                Vector3 vector13;
                NetTool.NodePosition position4 = nodeBuffer[k];
                Vector3 endPos = position4.m_position;
                NetTool.NodePosition position5 = nodeBuffer[k];
                Vector3 vector11 = position5.m_direction;
                NetSegment.CalculateMiddlePoints(vector5, direction, endPos, -vector11, smoothStart, smoothEnd, out vector12, out vector13);
                nodeInfo = nodeBuffer.m_buffer[k].m_nodeInfo;
                NetInfo info8 = null;
                NetTool.NodePosition position6 = nodeBuffer[k - 1];
                NetTool.NodePosition position7 = nodeBuffer[k - 1];
                float b = position6.m_position.y - position7.m_terrainHeight;
                NetTool.NodePosition position8 = nodeBuffer[k];
                NetTool.NodePosition position9 = nodeBuffer[k];
                float a = position8.m_position.y - position9.m_terrainHeight;
                if (nodeBuffer.m_buffer[k].m_double)
                {
                    info8 = nodeBuffer.m_buffer[k].m_nodeInfo;
                    data.m_flags |= NetNode.Flags.Double;
                }
                else if (nodeBuffer.m_buffer[k - 1].m_double)
                {
                    info8 = nodeBuffer.m_buffer[k - 1].m_nodeInfo;
                    data.m_flags &= ~NetNode.Flags.Double;
                }
                else
                {
                    float num28 = Mathf.Max(a, b);
                    for (int num29 = 1; num29 < 8; num29++)
                    {
                        Vector3 worldPos = Bezier3.Position(vector5, vector12, vector13, endPos, ((float) num29) / 8f);
                        float num30 = worldPos.y - Singleton<TerrainManager>.instance.SampleRawHeightSmooth(worldPos);
                        num28 = Mathf.Max(num28, num30);
                    }
                    info8 = info.m_netAI.GetInfo(num28, length, (k == 1) && startPoint.m_outside, (k == num9) && endPoint.m_outside, curved, false, ref none);
                    data.m_flags &= ~NetNode.Flags.Double;
                }
                data.m_position = endPos;
                if (a < (terrainStep - 1))
                {
                    data.m_flags |= NetNode.Flags.OnGround;
                }
                else
                {
                    data.m_flags &= ~NetNode.Flags.OnGround;
                }
                if ((k == num9) && endPoint.m_outside)
                {
                    data.m_flags |= NetNode.Flags.Outside;
                }
                nodeInfo.m_netAI.GetNodeBuilding(0, ref data, out info7, out num21);
                if (visualize)
                {
                    if ((info7 != null) && ((k != num9) || (endPoint.m_node == 0)))
                    {
                        Vector3 vector15 = endPos;
                        vector15.y += num21;
                        RenderNodeBuilding(info7, vector15, vector11);
                    }
                    if (info8.m_netAI.DisplayTempSegment())
                    {
                        if (nodeBuffer.m_buffer[k].m_double)
                        {
                            RenderSegment(info8, endPos, vector5, -vector11, -direction, smoothStart, smoothEnd);
                        }
                        else
                        {
                            RenderSegment(info8, vector5, endPos, direction, vector11, smoothStart, smoothEnd);
                        }
                    }
                }
                else
                {
                    if (info8.m_canCollide)
                    {
                        int num31 = Mathf.Max(2, 0x10 / num9);
                        Vector3 vector16 = (Vector3) (new Vector3(direction.z, 0f, -direction.x) * info8.m_halfWidth);
                        Quad3 quad = new Quad3 {
                            a = vector5 - vector16,
                            d = vector5 + vector16
                        };
                        for (int num32 = 1; num32 <= num31; num32++)
                        {
                            ushort num33 = 0;
                            ushort num34 = 0;
                            ushort num35 = 0;
                            ushort ignoreBuilding = 0;
                            bool outside = false;
                            if ((k == 1) && (((num32 - 1) << 1) < num31))
                            {
                                num33 = startPoint.m_node;
                                if ((k == num9) && ((num32 << 1) >= num31))
                                {
                                    num34 = endPoint.m_node;
                                }
                                num35 = startPoint.m_segment;
                                ignoreBuilding = GetIgnoredBuilding(startPoint);
                                outside = startPoint.m_outside;
                            }
                            else if ((k == num9) && ((num32 << 1) > num31))
                            {
                                num33 = endPoint.m_node;
                                if ((k == 1) && (((num32 - 1) << 1) <= num31))
                                {
                                    num34 = startPoint.m_node;
                                }
                                num35 = endPoint.m_segment;
                                ignoreBuilding = GetIgnoredBuilding(endPoint);
                                outside = endPoint.m_outside;
                            }
                            else if (((num32 - 1) << 1) < num31)
                            {
                                num33 = num10;
                            }
                            Vector3 vector17 = Bezier3.Position(vector5, vector12, vector13, endPos, ((float) num32) / ((float) num31));
                            vector16 = Bezier3.Tangent(vector5, vector12, vector13, endPos, ((float) num32) / ((float) num31));
                            Vector3 vector23 = new Vector3(vector16.z, 0f, -vector16.x);
                            vector16 = (Vector3) (vector23.normalized * info8.m_halfWidth);
                            quad.b = vector17 - vector16;
                            quad.c = vector17 + vector16;
                            float introduced109 = Mathf.Min(quad.a.y, quad.b.y);
                            float minY = Mathf.Min(introduced109, Mathf.Min(quad.c.y, quad.d.y)) + info8.m_minHeight;
                            float introduced110 = Mathf.Max(quad.a.y, quad.b.y);
                            float maxY = Mathf.Max(introduced110, Mathf.Max(quad.c.y, quad.d.y)) + info8.m_maxHeight;
                            Quad2 quad2 = Quad2.XZ(quad);
                            Singleton<NetManager>.instance.OverlapQuad(quad2, minY, maxY, info8.m_class.m_layer, num33, num34, num35, collidingSegments);
                            Singleton<BuildingManager>.instance.OverlapQuad(quad2, minY, maxY, info8.m_class.m_layer, ignoreBuilding, num33, num34, collidingBuildings);
                            if ((properties.m_mode & ItemClass.Availability.AssetEditor) != ItemClass.Availability.None)
                            {
                                float num39 = 256f;
                                if (((quad2.a.x < -num39) || (quad2.a.x > num39)) || ((quad2.a.y < -num39) || (quad2.a.y > num39)))
                                {
                                    none |= ToolBase.ToolErrors.OutOfArea;
                                }
                                if (((quad2.b.x < -num39) || (quad2.b.x > num39)) || ((quad2.b.y < -num39) || (quad2.b.y > num39)))
                                {
                                    none |= ToolBase.ToolErrors.OutOfArea;
                                }
                                if (((quad2.c.x < -num39) || (quad2.c.x > num39)) || ((quad2.c.y < -num39) || (quad2.c.y > num39)))
                                {
                                    none |= ToolBase.ToolErrors.OutOfArea;
                                }
                                if (((quad2.d.x < -num39) || (quad2.d.x > num39)) || ((quad2.d.y < -num39) || (quad2.d.y > num39)))
                                {
                                    none |= ToolBase.ToolErrors.OutOfArea;
                                }
                            }
                            else if (!outside && Singleton<GameAreaManager>.instance.QuadOutOfArea(quad2))
                            {
                                none |= ToolBase.ToolErrors.OutOfArea;
                            }
                            quad.a = quad.b;
                            quad.d = quad.c;
                        }
                    }
                    if ((info7 != null) && ((data.m_flags & NetNode.Flags.Outside) == NetNode.Flags.None))
                    {
                        ushort ignoreNode = (k != num9) ? ((ushort) 0) : endPoint.m_node;
                        ushort num41 = (k != num9) ? ((ushort) 0) : endPoint.m_segment;
                        ushort num42 = (k != num9) ? ((ushort) 0) : GetIgnoredBuilding(endPoint);
                        Vector3 vector18 = endPos;
                        vector18.y += num21;
                        errors2 = TestNodeBuilding(info7, vector18, vector11, ignoreNode, num41, num42, test, collidingSegments, collidingBuildings);
                        if (errors2 != ToolBase.ToolErrors.None)
                        {
                            none |= errors2;
                        }
                    }
                    if (test)
                    {
                        cost += info8.m_netAI.GetConstructionCost(vector5, endPos, b, a);
                        if ((needMoney && (cost > 0)) && (Singleton<EconomyManager>.instance.PeekResource(EconomyManager.Resource.Construction, cost) != cost))
                        {
                            none |= ToolBase.ToolErrors.NotEnoughMoney;
                        }
                        ushort startNode = 0;
                        ushort startSegment = 0;
                        ushort endNode = 0;
                        ushort endSegment = 0;
                        if (k == 1)
                        {
                            startNode = startPoint.m_node;
                            startSegment = startPoint.m_segment;
                        }
                        if (k == num9)
                        {
                            endNode = endPoint.m_node;
                            endSegment = endPoint.m_segment;
                        }
                        none |= CanCreateSegment(info8, startNode, startSegment, endNode, endSegment, index, vector5, endPos, direction, -vector11, collidingSegments);
                    }
                    else
                    {
                        cost += info8.m_netAI.GetConstructionCost(vector5, endPos, b, a);
                        if (needMoney && (cost > 0))
                        {
                            cost -= Singleton<EconomyManager>.instance.FetchResource(EconomyManager.Resource.Construction, cost, info8.m_class);
                            if (cost > 0)
                            {
                                none |= ToolBase.ToolErrors.NotEnoughMoney;
                            }
                        }
                        bool flag7 = num10 == 0;
                        bool flag8 = false;
                        ushort num47 = endPoint.m_node;
                        if ((k != num9) || (num47 == 0))
                        {
                            if ((k == num9) && (endPoint.m_segment != 0))
                            {
                                if (SplitSegment(endPoint.m_segment, out num47, endPos))
                                {
                                    flag8 = true;
                                }
                                else
                                {
                                    flag7 = true;
                                }
                                endPoint.m_segment = 0;
                            }
                            else if (Singleton<NetManager>.instance.CreateNode(out num47, ref Singleton<SimulationManager>.instance.m_randomizer, nodeInfo, endPos, Singleton<SimulationManager>.instance.m_currentBuildIndex))
                            {
                                if ((k == num9) && endPoint.m_outside)
                                {
                                    Singleton<NetManager>.instance.m_nodes.m_buffer[num47].m_flags |= NetNode.Flags.Outside;
                                }
                                if (a < (terrainStep - 1))
                                {
                                    Singleton<NetManager>.instance.m_nodes.m_buffer[num47].m_flags |= NetNode.Flags.OnGround;
                                }
                                if (nodeBuffer.m_buffer[k].m_double)
                                {
                                    Singleton<NetManager>.instance.m_nodes.m_buffer[num47].m_flags |= NetNode.Flags.Double;
                                }
                                NetTool.NodePosition position10 = nodeBuffer[k];
                                Singleton<NetManager>.instance.m_nodes.m_buffer[num47].m_elevation = (byte) Mathf.Clamp(Mathf.RoundToInt(position10.m_elevation), 0, 0xff);
                                SimulationManager local3 = Singleton<SimulationManager>.instance;
                                local3.m_currentBuildIndex++;
                                flag8 = true;
                            }
                            else
                            {
                                flag7 = true;
                            }
                            if (k == num9)
                            {
                                endPoint.m_node = num47;
                            }
                        }
                        if ((!flag7 && !curved) && (Singleton<NetManager>.instance.m_nodes.m_buffer[num10].m_elevation == Singleton<NetManager>.instance.m_nodes.m_buffer[num47].m_elevation))
                        {
                            Vector3 vector19 = vector5;
                            if (k == 1)
                            {
                                TryMoveNode(ref num10, ref direction, info8, endPos);
                                vector19 = Singleton<NetManager>.instance.m_nodes.m_buffer[num10].m_position;
                            }
                            if (k == num9)
                            {
                                Vector3 vector20 = -vector11;
                                TryMoveNode(ref num47, ref vector20, info8, vector19);
                                vector11 = -vector20;
                            }
                        }
                        if (!flag7)
                        {
                            if (nodeBuffer.m_buffer[k].m_double)
                            {
                                flag7 = !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, info8, num47, num10, -vector11, direction, currentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, !flag);
                            }
                            else if (nodeBuffer.m_buffer[k - 1].m_double)
                            {
                                flag7 = !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, info8, num10, num47, direction, -vector11, currentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, flag);
                            }
                            else if (((((num9 - k) & 1) == 0) && (k != 1)) && curved)
                            {
                                flag7 = !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, info8, num47, num10, -vector11, direction, currentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, !flag);
                            }
                            else
                            {
                                flag7 = !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, info8, num10, num47, direction, -vector11, currentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, flag);
                            }
                            if (!flag7)
                            {
                                SimulationManager local4 = Singleton<SimulationManager>.instance;
                                local4.m_currentBuildIndex += 2;
                                currentBuildIndex = Singleton<SimulationManager>.instance.m_currentBuildIndex;
                                DispatchPlacementEffect(vector5, vector12, vector13, endPos, info.m_halfWidth, false);
                                info8.m_netAI.ManualActivation(segment, ref Singleton<NetManager>.instance.m_segments.m_buffer[segment], oldInfo);
                            }
                        }
                        if (flag7)
                        {
                            if (flag5 && (num10 != 0))
                            {
                                Singleton<NetManager>.instance.ReleaseNode(num10);
                                num10 = 0;
                            }
                            if (flag8 && (num47 != 0))
                            {
                                Singleton<NetManager>.instance.ReleaseNode(num47);
                                num47 = 0;
                            }
                        }
                        if (((building != 0) && (num47 != 0)) && ((Singleton<NetManager>.instance.m_nodes.m_buffer[num47].m_flags & NetNode.Flags.Untouchable) == NetNode.Flags.None))
                        {
                            Singleton<NetManager>.instance.m_nodes.m_buffer[num47].m_flags |= NetNode.Flags.Untouchable;
                            Singleton<NetManager>.instance.m_nodes.m_buffer[num47].m_nextBuildingNode = Singleton<BuildingManager>.instance.m_buildings.m_buffer[building].m_netNode;
                            Singleton<BuildingManager>.instance.m_buildings.m_buffer[building].m_netNode = num47;
                        }
                        if (((building != 0) && (segment != 0)) && ((Singleton<NetManager>.instance.m_segments.m_buffer[segment].m_flags & NetSegment.Flags.Untouchable) == NetSegment.Flags.None))
                        {
                            Singleton<NetManager>.instance.m_segments.m_buffer[segment].m_flags |= NetSegment.Flags.Untouchable;
                        }
                        num10 = num47;
                    }
                }
                vector5 = endPos;
                direction = vector11;
                flag5 = false;
            }
            if (visualize)
            {
                if (nodeInfo.m_netAI.DisplayTempSegment())
                {
                    RenderNode(nodeInfo, vector5, -direction);
                }
            }
            else
            {
                BuildingTool.IgnoreRelocateSegments(relocateBuildingID, collidingSegments, collidingBuildings);
                if (CheckCollidingSegments(collidingSegments, collidingBuildings, index) && ((none & (ToolBase.ToolErrors.TooManyConnections | ToolBase.ToolErrors.HeightTooHigh | ToolBase.ToolErrors.SlopeTooSteep | ToolBase.ToolErrors.TooShort | ToolBase.ToolErrors.InvalidShape)) == ToolBase.ToolErrors.None))
                {
                    none |= ToolBase.ToolErrors.FirstVisibleError;
                }
                if (BuildingTool.CheckCollidingBuildings(collidingBuildings, collidingSegments))
                {
                    none |= ToolBase.ToolErrors.FirstVisibleError;
                }
                if (!test)
                {
                    ReleaseNonImportantSegments(collidingSegments);
                    BuildingTool.ReleaseNonImportantBuildings(collidingBuildings);
                }
            }
            for (int m = 0; m <= num9; m++)
            {
                nodeBuffer.m_buffer[m].m_nodeInfo = null;
            }
            firstNode = startPoint.m_node;
            lastNode = endPoint.m_node;
            errors3 = none;
        }
        finally
        {
            if (cost < 0)
            {
                cost = 0;
            }
            if (test || !visualize)
            {
                properties.EndColliding();
            }
        }
        return errors3;
    }

    private bool CreateNodeImpl(bool switchDirection)
    {
        NetInfo prefab = this.m_prefab;
        if (prefab != null)
        {
            if ((this.m_mode == NetTool.Mode.Upgrade) && (this.m_controlPointCount < 2))
            {
                prefab.m_netAI.UpgradeFailed();
            }
            else
            {
                NetTool.ControlPoint point;
                NetTool.ControlPoint point2;
                NetTool.ControlPoint point3;
                ushort num;
                ushort num2;
                int num3;
                int num4;
                if ((this.m_mode == NetTool.Mode.Straight) && (this.m_controlPointCount < 1))
                {
                    this.m_elevation = Mathf.Max(0, Mathf.RoundToInt(this.m_controlPoints[this.m_controlPointCount].m_elevation / terrainStep));
                    this.m_controlPoints[this.m_controlPointCount + 1] = this.m_controlPoints[this.m_controlPointCount];
                    this.m_controlPoints[this.m_controlPointCount + 1].m_node = 0;
                    this.m_controlPoints[this.m_controlPointCount + 1].m_segment = 0;
                    this.m_controlPointCount++;
                    return true;
                }
                if ((((this.m_mode == NetTool.Mode.Curved) || (this.m_mode == NetTool.Mode.Freeform)) && (this.m_controlPointCount < 2)) && ((this.m_controlPointCount == 0) || ((this.m_controlPoints[1].m_node == 0) && (this.m_controlPoints[1].m_segment == 0))))
                {
                    this.m_elevation = Mathf.Max(0, Mathf.RoundToInt(this.m_controlPoints[this.m_controlPointCount].m_elevation / terrainStep));
                    this.m_controlPoints[this.m_controlPointCount + 1] = this.m_controlPoints[this.m_controlPointCount];
                    this.m_controlPoints[this.m_controlPointCount + 1].m_node = 0;
                    this.m_controlPoints[this.m_controlPointCount + 1].m_segment = 0;
                    this.m_controlPointCount++;
                    return true;
                }
                bool needMoney = (Singleton<ToolManager>.instance.m_properties.m_mode & ItemClass.Availability.Game) != ItemClass.Availability.None;
                if (this.m_mode == NetTool.Mode.Upgrade)
                {
                    this.m_upgrading = true;
                    this.m_switchingDir = switchDirection;
                }
                if (this.m_controlPointCount == 1)
                {
                    point = this.m_controlPoints[0];
                    point3 = this.m_controlPoints[1];
                    point2 = this.m_controlPoints[1];
                    point2.m_node = 0;
                    point2.m_segment = 0;
                    point2.m_position = (Vector3) ((this.m_controlPoints[0].m_position + this.m_controlPoints[1].m_position) * 0.5f);
                    point2.m_elevation = (this.m_controlPoints[0].m_elevation + this.m_controlPoints[1].m_elevation) * 0.5f;
                }
                else
                {
                    point = this.m_controlPoints[0];
                    point2 = this.m_controlPoints[1];
                    point3 = this.m_controlPoints[2];
                }
                bool flag2 = (point3.m_node != 0) || (point3.m_segment != 0);
                if (CreateNode(prefab, point, point2, point3, m_nodePositionsSimulation, 0x3e8, true, false, true, needMoney, false, switchDirection, 0, out num, out num2, out num3, out num4) == ToolBase.ToolErrors.None)
                {
                    CreateNode(prefab, point, point2, point3, m_nodePositionsSimulation, 0x3e8, false, false, true, needMoney, false, switchDirection, 0, out num, out num2, out num3, out num4);
                    NetManager instance = Singleton<NetManager>.instance;
                    point3.m_segment = 0;
                    point3.m_node = num;
                    if (num2 != 0)
                    {
                        if (this.m_upgrading)
                        {
                            while (!Monitor.TryEnter(this.m_upgradedSegments, SimulationManager.SYNCHRONIZE_TIMEOUT))
                            {
                            }
                            try
                            {
                                this.m_upgradedSegments.Add(num2);
                            }
                            finally
                            {
                                Monitor.Exit(this.m_upgradedSegments);
                            }
                        }
                        if (instance.m_segments.m_buffer[num2].m_startNode == num)
                        {
                            point3.m_direction = -instance.m_segments.m_buffer[num2].m_startDirection;
                        }
                        else if (instance.m_segments.m_buffer[num2].m_endNode == num)
                        {
                            point3.m_direction = -instance.m_segments.m_buffer[num2].m_endDirection;
                        }
                    }
                    this.m_controlPoints[0] = point3;
                    this.m_elevation = Mathf.Max(0, Mathf.RoundToInt(point3.m_elevation / terrainStep));
                    if ((num != 0) && ((instance.m_nodes.m_buffer[num].m_flags & NetNode.Flags.Outside) != NetNode.Flags.None))
                    {
                        this.m_controlPointCount = 0;
                    }
                    else if ((this.m_mode == NetTool.Mode.Freeform) && (this.m_controlPointCount == 2))
                    {
                        point2.m_position = ((Vector3) (point3.m_position * 2f)) - point2.m_position;
                        point2.m_elevation = (point3.m_elevation * 2f) - point2.m_elevation;
                        point2.m_direction = point3.m_direction;
                        point2.m_node = 0;
                        point2.m_segment = 0;
                        this.m_controlPoints[1] = point2;
                        this.m_controlPointCount = 2;
                    }
                    else
                    {
                        this.m_controlPointCount = 1;
                    }
                    if (prefab.m_class.m_service > ItemClass.Service.Office)
                    {
                        int index = (((int) prefab.m_class.m_service) - 8) - 1;
                        Singleton<GuideManager>.instance.m_serviceNotUsed[index].Disable();
                        Singleton<GuideManager>.instance.m_serviceNeeded[index].Deactivate();
                    }
                    if (prefab.m_class.m_service == ItemClass.Service.Road)
                    {
                        Singleton<CoverageManager>.instance.CoverageUpdated(ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.None);
                        Singleton<NetManager>.instance.m_roadsNotUsed.Disable();
                    }
                    if (((((prefab.m_class.m_service == ItemClass.Service.Road) || (prefab.m_class.m_service == ItemClass.Service.PublicTransport)) || (prefab.m_class.m_service == ItemClass.Service.Beautification)) && (prefab.m_hasForwardVehicleLanes || prefab.m_hasBackwardVehicleLanes)) && (!prefab.m_hasForwardVehicleLanes || !prefab.m_hasBackwardVehicleLanes))
                    {
                        Singleton<NetManager>.instance.m_onewayRoadPlacement.Disable();
                    }
                    if (this.m_upgrading)
                    {
                        prefab.m_netAI.UpgradeSucceeded();
                    }
                    else if (flag2 && (num != 0))
                    {
                        prefab.m_netAI.ConnectionSucceeded(num, ref Singleton<NetManager>.instance.m_nodes.m_buffer[num]);
                    }
                    Singleton<GuideManager>.instance.m_notEnoughMoney.Deactivate();
                    if ((((Singleton<GuideManager>.instance.m_properties != null) && !this.m_upgrading) && ((num2 != 0) && (this.m_bulldozerTool != null))) && ((this.m_bulldozerTool.m_lastNetInfo != null) && this.m_bulldozerTool.m_lastNetInfo.m_netAI.CanUpgradeTo(prefab)))
                    {
                        ushort startNode = instance.m_segments.m_buffer[num2].m_startNode;
                        ushort endNode = instance.m_segments.m_buffer[num2].m_endNode;
                        Vector3 position = instance.m_nodes.m_buffer[startNode].m_position;
                        Vector3 vector2 = instance.m_nodes.m_buffer[endNode].m_position;
                        Vector3 startDirection = instance.m_segments.m_buffer[num2].m_startDirection;
                        Vector3 endDirection = instance.m_segments.m_buffer[num2].m_endDirection;
                        if (((Vector3.SqrMagnitude(this.m_bulldozerTool.m_lastStartPos - position) < 1f) && (Vector3.SqrMagnitude(this.m_bulldozerTool.m_lastEndPos - vector2) < 1f)) && ((Vector2.Dot(VectorUtils.XZ(this.m_bulldozerTool.m_lastStartDir), VectorUtils.XZ(startDirection)) > 0.99f) && (Vector2.Dot(VectorUtils.XZ(this.m_bulldozerTool.m_lastEndDir), VectorUtils.XZ(endDirection)) > 0.99f)))
                        {
                            Singleton<NetManager>.instance.m_manualUpgrade.Activate(Singleton<GuideManager>.instance.m_properties.m_manualUpgrade, prefab.m_class.m_service);
                        }
                    }
                    return true;
                }
            }
        }
        return false;
    }

    public static void DispatchPlacementEffect(Vector3 startPos, Vector3 middlePos1, Vector3 middlePos2, Vector3 endPos, float halfWidth, bool bulldozing)
    {
        EffectInfo bulldozeEffect;
        if (bulldozing)
        {
            bulldozeEffect = Singleton<NetManager>.instance.m_properties.m_bulldozeEffect;
        }
        else
        {
            bulldozeEffect = Singleton<NetManager>.instance.m_properties.m_placementEffect;
        }
        if (bulldozeEffect != null)
        {
            InstanceID instance = new InstanceID();
            Bezier3 bezier = new Bezier3(startPos, middlePos1, middlePos2, endPos);
            EffectInfo.SpawnArea spawnArea = new EffectInfo.SpawnArea(bezier, halfWidth, 0f);
            Singleton<EffectManager>.instance.DispatchEffect(bulldozeEffect, instance, spawnArea, Vector3.zero, 0f, 1f, Singleton<AudioManager>.instance.DefaultGroup);
        }
    }

    private float GetElevation(NetInfo info)
    {
        int min_height;
        int max_height;
        if (info == null)
        {
            return 0f;
        }
        info.m_netAI.GetElevationLimits(out min_height, out max_height);
        min_height = Mathf.RoundToInt(min_height * 12 / terrainStep);
        max_height = Mathf.RoundToInt(max_height * 12 / terrainStep);
        if (min_height == max_height)
        {
            return 0f;
        }
        return (Mathf.Clamp(this.m_elevation, min_height, max_height) * terrainStep);
    }

    public override ToolBase.ToolErrors GetErrors()
    {
        return this.m_buildErrors;
    }

    private static ushort GetIgnoredBuilding(NetTool.ControlPoint point)
    {
        if (point.m_node != 0)
        {
            return Singleton<NetManager>.instance.m_nodes.m_buffer[point.m_node].m_building;
        }
        if (point.m_segment == 0)
        {
            return 0;
        }
        NetManager instance = Singleton<NetManager>.instance;
        ushort startNode = instance.m_segments.m_buffer[point.m_segment].m_startNode;
        ushort endNode = instance.m_segments.m_buffer[point.m_segment].m_endNode;
        Vector3 position = instance.m_nodes.m_buffer[startNode].m_position;
        Vector3 vector2 = instance.m_nodes.m_buffer[endNode].m_position;
        if (Vector3.SqrMagnitude(position - point.m_position) < Vector3.SqrMagnitude(vector2 - point.m_position))
        {
            return Singleton<NetManager>.instance.m_nodes.m_buffer[startNode].m_building;
        }
        return Singleton<NetManager>.instance.m_nodes.m_buffer[endNode].m_building;
    }

    private static Vector3 LerpPosition(Vector3 refPos1, Vector3 refPos2, float t, float snap)
    {
        if (snap != 0f)
        {
            Vector2 vector = new Vector2(refPos2.x - refPos1.x, refPos2.z - refPos1.z);
            float magnitude = vector.magnitude;
            if (magnitude != 0f)
            {
                t = Mathf.Round(((t * magnitude) / snap) + 0.01f) * (snap / magnitude);
            }
        }
        return Vector3.Lerp(refPos1, refPos2, t);
    }

    public static bool MakeControlPoint(Ray ray, float rayLength, NetInfo info, bool ignoreTerrain, NetNode.Flags ignoreNodeFlags, NetSegment.Flags ignoreSegmentFlags, Building.Flags ignoreBuildingFlags, float elevation, out NetTool.ControlPoint p)
    {
        ToolBase.RaycastOutput output;
        p = new NetTool.ControlPoint();
        p.m_elevation = elevation;
        ItemClass connectionClass = info.GetConnectionClass();
        ToolBase.RaycastInput input = new ToolBase.RaycastInput(ray, rayLength) {
            m_netService = new ToolBase.RaycastService(connectionClass.m_service, connectionClass.m_subService, connectionClass.m_layer),
            m_buildingService = new ToolBase.RaycastService(connectionClass.m_service, connectionClass.m_subService, ItemClass.Layer.None)
        };
        if (info.m_intersectClass != null)
        {
            input.m_netService2 = new ToolBase.RaycastService(info.m_intersectClass.m_service, info.m_intersectClass.m_subService, info.m_intersectClass.m_layer);
        }
        input.m_netSnap = elevation;
        input.m_ignoreTerrain = ignoreTerrain;
        input.m_ignoreNodeFlags = ignoreNodeFlags;
        input.m_ignoreSegmentFlags = ignoreSegmentFlags;
        input.m_ignoreBuildingFlags = ignoreBuildingFlags;
        if (!ToolBase.RayCast(input, out output))
        {
            return false;
        }
        if (output.m_building != 0)
        {
            output.m_netNode = Singleton<BuildingManager>.instance.m_buildings.m_buffer[output.m_building].FindNode(connectionClass.m_service, connectionClass.m_subService, connectionClass.m_layer);
            output.m_building = 0;
        }
        p.m_position = output.m_hitPos;
        p.m_node = output.m_netNode;
        p.m_segment = output.m_netSegment;
        Vector3 position = p.m_position;
        if (p.m_node != 0)
        {
            NetNode node = Singleton<NetManager>.instance.m_nodes.m_buffer[p.m_node];
            p.m_position = node.m_position;
            p.m_direction = Vector3.zero;
            p.m_segment = 0;
            p.m_elevation = node.m_elevation;
        }
        else if (p.m_segment != 0)
        {
            NetSegment segment = Singleton<NetManager>.instance.m_segments.m_buffer[p.m_segment];
            NetNode node2 = Singleton<NetManager>.instance.m_nodes.m_buffer[segment.m_startNode];
            NetNode node3 = Singleton<NetManager>.instance.m_nodes.m_buffer[segment.m_endNode];
            if (!NetSegment.IsStraight(node2.m_position, segment.m_startDirection, node3.m_position, segment.m_endDirection))
            {
                Vector3 vector2;
                Vector3 vector3;
                segment.GetClosestPositionAndDirection(p.m_position, out vector2, out vector3);
                Vector3 vector4 = vector2 - node2.m_position;
                if (vector4.sqrMagnitude < 64f)
                {
                    p.m_position = node2.m_position;
                    p.m_direction = segment.m_startDirection;
                    p.m_node = segment.m_startNode;
                    p.m_segment = 0;
                }
                else
                {
                    Vector3 vector5 = vector2 - node3.m_position;
                    if (vector5.sqrMagnitude < 64f)
                    {
                        p.m_position = node3.m_position;
                        p.m_direction = segment.m_endDirection;
                        p.m_node = segment.m_endNode;
                        p.m_segment = 0;
                    }
                    else
                    {
                        p.m_position = vector2;
                        p.m_direction = vector3;
                    }
                }
            }
            else
            {
                p.m_position = segment.GetClosestPosition(p.m_position);
                p.m_direction = segment.m_startDirection;
                float num = ((p.m_position.x - node2.m_position.x) * (node3.m_position.x - node2.m_position.x)) + ((p.m_position.z - node2.m_position.z) * (node3.m_position.z - node2.m_position.z));
                float num2 = ((node3.m_position.x - node2.m_position.x) * (node3.m_position.x - node2.m_position.x)) + ((node3.m_position.z - node2.m_position.z) * (node3.m_position.z - node2.m_position.z));
                if (num2 != 0f)
                {
                    p.m_position = LerpPosition(node2.m_position, node3.m_position, num / num2, info.m_netAI.GetLengthSnap());
                }
            }
            p.m_elevation = Mathf.Lerp((float) node2.m_elevation, (float) node3.m_elevation, 0.5f);
        }
        else
        {
            float num3 = 8640f;
            float introduced18 = Mathf.Abs(p.m_position.x);
            if (introduced18 >= Mathf.Abs(p.m_position.z))
            {
                if (p.m_position.x > (num3 - (info.m_halfWidth * 3f)))
                {
                    p.m_position.x = num3 + (info.m_halfWidth * 0.8f);
                    p.m_position.z = Mathf.Clamp(p.m_position.z, info.m_halfWidth - num3, num3 - info.m_halfWidth);
                    p.m_outside = true;
                }
                if (p.m_position.x < ((info.m_halfWidth * 3f) - num3))
                {
                    p.m_position.x = -num3 - (info.m_halfWidth * 0.8f);
                    p.m_position.z = Mathf.Clamp(p.m_position.z, info.m_halfWidth - num3, num3 - info.m_halfWidth);
                    p.m_outside = true;
                }
            }
            else
            {
                if (p.m_position.z > (num3 - (info.m_halfWidth * 3f)))
                {
                    p.m_position.z = num3 + (info.m_halfWidth * 0.8f);
                    p.m_position.x = Mathf.Clamp(p.m_position.x, info.m_halfWidth - num3, num3 - info.m_halfWidth);
                    p.m_outside = true;
                }
                if (p.m_position.z < ((info.m_halfWidth * 3f) - num3))
                {
                    p.m_position.z = -num3 - (info.m_halfWidth * 0.8f);
                    p.m_position.x = Mathf.Clamp(p.m_position.x, info.m_halfWidth - num3, num3 - info.m_halfWidth);
                    p.m_outside = true;
                }
            }
            p.m_position.y = NetSegment.SampleTerrainHeight(info, p.m_position, false) + elevation;
        }
        if (p.m_node != 0)
        {
            NetNode node4 = Singleton<NetManager>.instance.m_nodes.m_buffer[p.m_node];
            if ((node4.m_flags & ignoreNodeFlags) != NetNode.Flags.None)
            {
                p.m_position = position;
                p.m_position.y = NetSegment.SampleTerrainHeight(info, p.m_position, false) + elevation;
                p.m_node = 0;
                p.m_segment = 0;
                p.m_elevation = elevation;
            }
        }
        else if (p.m_segment != 0)
        {
            NetSegment segment2 = Singleton<NetManager>.instance.m_segments.m_buffer[p.m_segment];
            if ((segment2.m_flags & ignoreSegmentFlags) != NetSegment.Flags.None)
            {
                p.m_position = position;
                p.m_position.y = NetSegment.SampleTerrainHeight(info, p.m_position, false) + elevation;
                p.m_node = 0;
                p.m_segment = 0;
                p.m_elevation = elevation;
            }
        }
        return true;
    }

    private static void MoveEndNode(ref ushort node, ref Vector3 direction, Vector3 position)
    {
        NetNode node2 = Singleton<NetManager>.instance.m_nodes.m_buffer[node];
        NetInfo info = node2.Info;
        Vector3 vector = node2.m_position - position;
        vector.y = 0f;
        float minNodeDistance = info.GetMinNodeDistance();
        if (vector.sqrMagnitude < (minNodeDistance * minNodeDistance))
        {
            Singleton<NetManager>.instance.ReleaseNode(node);
            node = 0;
        }
    }

    private static void MoveMiddleNode(ref ushort node, ref Vector3 direction, Vector3 position)
    {
        NetNode node2 = Singleton<NetManager>.instance.m_nodes.m_buffer[node];
        uint buildIndex = node2.m_buildIndex;
        NetInfo info = node2.Info;
        Vector3 vector = node2.m_position - position;
        vector.y = 0f;
        if (vector.sqrMagnitude < 2500f)
        {
            for (int i = 0; i < 8; i++)
            {
                ushort index = node2.GetSegment(i);
                if (index != 0)
                {
                    Vector3 vector4;
                    Vector3 vector5;
                    NetSegment segment = Singleton<NetManager>.instance.m_segments.m_buffer[index];
                    NetInfo info2 = segment.Info;
                    Vector3 startPos = Singleton<NetManager>.instance.m_nodes.m_buffer[segment.m_startNode].m_position;
                    Vector3 endPos = Singleton<NetManager>.instance.m_nodes.m_buffer[segment.m_endNode].m_position;
                    bool flag = !NetSegment.IsStraight(startPos, segment.m_startDirection, endPos, segment.m_endDirection);
                    bool flag2 = segment.m_startNode == node;
                    ushort num4 = !flag2 ? segment.m_startNode : segment.m_endNode;
                    uint num5 = segment.m_buildIndex;
                    NetNode node3 = Singleton<NetManager>.instance.m_nodes.m_buffer[num4];
                    vector = node3.m_position - position;
                    vector.y = 0f;
                    bool flag3 = vector.sqrMagnitude >= 10000f;
                    if (flag && flag3)
                    {
                        segment.GetClosestPositionAndDirection((Vector3) ((position + node3.m_position) * 0.5f), out vector4, out vector5);
                        if (flag2)
                        {
                            vector5 = -vector5;
                        }
                    }
                    else
                    {
                        vector4 = LerpPosition(position, node3.m_position, 0.5f, info.m_netAI.GetLengthSnap());
                        vector5 = !flag2 ? segment.m_startDirection : segment.m_endDirection;
                    }
                    direction = vector5;
                    Singleton<NetManager>.instance.ReleaseSegment(index, true);
                    Singleton<NetManager>.instance.ReleaseNode(node);
                    if (flag3)
                    {
                        if (Singleton<NetManager>.instance.CreateNode(out node, ref Singleton<SimulationManager>.instance.m_randomizer, info, vector4, buildIndex))
                        {
                            Singleton<NetManager>.instance.m_nodes.m_buffer[node].m_elevation = node2.m_elevation;
                            if (flag2)
                            {
                                if (Singleton<NetManager>.instance.CreateSegment(out index, ref Singleton<SimulationManager>.instance.m_randomizer, info2, node, num4, -vector5, segment.m_endDirection, num5, Singleton<SimulationManager>.instance.m_currentBuildIndex, (segment.m_flags & NetSegment.Flags.Invert) != NetSegment.Flags.None))
                                {
                                    SimulationManager instance = Singleton<SimulationManager>.instance;
                                    instance.m_currentBuildIndex += 2;
                                }
                            }
                            else if (Singleton<NetManager>.instance.CreateSegment(out index, ref Singleton<SimulationManager>.instance.m_randomizer, info2, num4, node, segment.m_startDirection, -vector5, num5, Singleton<SimulationManager>.instance.m_currentBuildIndex, (segment.m_flags & NetSegment.Flags.Invert) != NetSegment.Flags.None))
                            {
                                SimulationManager local2 = Singleton<SimulationManager>.instance;
                                local2.m_currentBuildIndex += 2;
                            }
                        }
                    }
                    else
                    {
                        node = num4;
                    }
                    break;
                }
            }
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        base.ToolCursor = null;
        Singleton<TerrainManager>.instance.RenderZones = false;
        this.m_controlPointCount = 0;
        this.m_lengthChanging = false;
        this.m_lengthTimer = 0f;
        this.m_constructionCost = 0;
        this.m_productionRate = 0;
        this.m_buildErrors = ToolBase.ToolErrors.Pending;
        this.m_cachedErrors = ToolBase.ToolErrors.Pending;
        this.m_upgrading = false;
        this.m_switchingDir = false;
        while (!Monitor.TryEnter(this.m_upgradedSegments, SimulationManager.SYNCHRONIZE_TIMEOUT))
        {
        }
        try
        {
            this.m_upgradedSegments.Clear();
        }
        finally
        {
            Monitor.Exit(this.m_upgradedSegments);
        }
        this.m_mouseRayValid = false;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Singleton<TerrainManager>.instance.RenderZones = true;
        this.m_controlPointCount = 0;
        this.m_lengthChanging = false;
        this.m_lengthTimer = 0f;
        this.m_constructionCost = 0;
        this.m_productionRate = 0;
        this.m_buildErrors = ToolBase.ToolErrors.Pending;
        this.m_cachedErrors = ToolBase.ToolErrors.Pending;
        this.m_upgrading = false;
        this.m_switchingDir = false;
        while (!Monitor.TryEnter(this.m_upgradedSegments, SimulationManager.SYNCHRONIZE_TIMEOUT))
        {
        }
        try
        {
            this.m_upgradedSegments.Clear();
        }
        finally
        {
            Monitor.Exit(this.m_upgradedSegments);
        }
        base.m_toolController.ClearColliding();
    }

    protected override void OnToolGUI()
    {
        bool isInsideUI = base.m_toolController.IsInsideUI;
        UnityEngine.Event current = UnityEngine.Event.current;
        if (current.type == EventType.MouseDown)
        {
            if (!isInsideUI)
            {
                if (current.button == 0)
                {
                    if (this.m_cachedErrors == ToolBase.ToolErrors.None)
                    {
                        Singleton<SimulationManager>.instance.AddAction<bool>(this.CreateNode(false));
                    }
                    else
                    {
                        Singleton<SimulationManager>.instance.AddAction(this.CreateFailed());
                    }
                }
                else if (current.button == 1)
                {
                    if (this.m_mode == NetTool.Mode.Upgrade)
                    {
                        Singleton<SimulationManager>.instance.AddAction<bool>(this.CreateNode(true));
                    }
                    else
                    {
                        Singleton<SimulationManager>.instance.AddAction(this.CancelNode());
                    }
                }
            }
        }
        else if (current.type == EventType.MouseUp)
        {
            if ((current.button == 0) || (current.button == 1))
            {
                Singleton<SimulationManager>.instance.AddAction(this.CancelUpgrading());
            }
        }
        else if (this.m_buildElevationUp.IsPressed(current))
        {
            Singleton<SimulationManager>.instance.AddAction<bool>(this.ChangeElevation(1));
        }
        else if (this.m_buildElevationDown.IsPressed(current))
        {
            Singleton<SimulationManager>.instance.AddAction<bool>(this.ChangeElevation(-1));
        }
    }

    protected override void OnToolLateUpdate()
    {
        NetInfo prefab = this.m_prefab;
        if (prefab != null)
        {
            InfoManager.InfoMode mode;
            InfoManager.SubInfoMode mode2;
            Vector3 mousePosition = Input.mousePosition;
            this.m_mouseRay = Camera.main.ScreenPointToRay(mousePosition);
            this.m_mouseRayLength = Camera.main.farClipPlane;
            this.m_mouseRayValid = !base.m_toolController.IsInsideUI && Cursor.visible;
            if (this.m_lengthTimer > 0f)
            {
                this.m_lengthTimer = Mathf.Max((float) 0f, (float) (this.m_lengthTimer - Time.deltaTime));
            }
            prefab.m_netAI.GetPlacementInfoMode(out mode, out mode2);
            base.ForceInfoMode(mode, mode2);
        }
    }

    protected override void OnToolUpdate()
    {
        NetInfo prefab = this.m_prefab;
        if (prefab != null)
        {
            while (!Monitor.TryEnter(this.m_cacheLock, SimulationManager.SYNCHRONIZE_TIMEOUT))
            {
            }
            try
            {
                for (int i = 0; i < this.m_controlPoints.Length; i++)
                {
                    this.m_cachedControlPoints[i] = this.m_controlPoints[i];
                }
                this.m_cachedControlPointCount = this.m_controlPointCount;
                this.m_cachedErrors = this.m_buildErrors;
            }
            finally
            {
                Monitor.Exit(this.m_cacheLock);
            }
            if ((!base.m_toolController.IsInsideUI && Cursor.visible) && ((this.m_cachedErrors & (ToolBase.ToolErrors.Pending | ToolBase.ToolErrors.RaycastFailed)) == ToolBase.ToolErrors.None))
            {
                Vector3 position;
                if ((this.m_mode == NetTool.Mode.Upgrade) && (this.m_cachedControlPointCount >= 2))
                {
                    position = this.m_cachedControlPoints[1].m_position;
                }
                else
                {
                    position = this.m_cachedControlPoints[this.m_cachedControlPointCount].m_position;
                }
                int constructionCost = this.m_constructionCost;
                if (((base.m_toolController.m_mode & ItemClass.Availability.Game) != ItemClass.Availability.None) && (constructionCost != 0))
                {
                    string str;
                    if (this.m_mode == NetTool.Mode.Upgrade)
                    {
                        str = string.Format(Locale.Get("TOOL_UPGRADE_COST"), constructionCost / 100);
                    }
                    else
                    {
                        str = string.Format(Locale.Get("TOOL_CONSTRUCTION_COST"), constructionCost / 100);
                    }
                    string constructionInfo = prefab.m_netAI.GetConstructionInfo(this.m_productionRate);
                    if (constructionInfo != null)
                    {
                        str = str + "\n" + constructionInfo;
                    }
                    base.ShowToolInfo(true, str, position);
                }
                else
                {
                    base.ShowToolInfo(true, null, position);
                }
            }
            else
            {
                base.ShowToolInfo(false, null, Vector3.zero);
            }
            CursorInfo upgradeCursor = null;
            if (this.m_mode == NetTool.Mode.Upgrade)
            {
                upgradeCursor = prefab.m_upgradeCursor;
            }
            if (upgradeCursor == null)
            {
                upgradeCursor = prefab.m_placementCursor;
            }
            if ((upgradeCursor == null) && (this.m_mode == NetTool.Mode.Upgrade))
            {
                upgradeCursor = this.m_upgradeCursor;
            }
            if (upgradeCursor == null)
            {
                upgradeCursor = this.m_placementCursor;
            }
            base.ToolCursor = upgradeCursor;
        }
    }

    public override void PlayAudio(AudioManager.ListenerInfo listenerInfo)
    {
        base.PlayAudio(listenerInfo);
        if (this.m_lengthChanging)
        {
            this.m_lengthTimer = 0.1f;
            this.m_lengthChanging = false;
        }
        if (this.m_lengthTimer != 0f)
        {
            NetProperties properties = Singleton<NetManager>.instance.m_properties;
            if ((properties != null) && (properties.m_drawSound != null))
            {
                Singleton<AudioManager>.instance.PlaySound(properties.m_drawSound, 1f);
            }
        }
    }

    public static bool ReleaseNonImportantSegments(ulong[] segmentMask)
    {
        NetManager instance = Singleton<NetManager>.instance;
        int length = segmentMask.Length;
        for (int i = 0; i < length; i++)
        {
            ulong num3 = segmentMask[i];
            if (num3 != 0)
            {
                for (int j = 0; j < 0x40; j++)
                {
                    if ((num3 & (((ulong) 1L) << j)) != 0)
                    {
                        int index = (i << 6) | j;
                        NetInfo info = instance.m_segments.m_buffer[index].Info;
                        if (((info.m_class.m_service <= ItemClass.Service.Office) || info.m_autoRemove) && ((instance.m_segments.m_buffer[index].m_flags & NetSegment.Flags.Untouchable) == NetSegment.Flags.None))
                        {
                            instance.ReleaseSegment((ushort) index, false);
                            num3 &= (ulong) ~(((long) 1L) << j);
                        }
                    }
                    segmentMask[i] = num3;
                }
            }
        }
        return false;
    }

    public static void RenderBulldozeNotification(RenderManager.CameraInfo cameraInfo, ref NetSegment segment)
    {
        NetInfo info = segment.Info;
        if (((info != null) && (((segment.m_flags & NetSegment.Flags.Untouchable) == NetSegment.Flags.None) || info.m_overlayVisible)) && (((info.m_class.m_service <= ItemClass.Service.Office) || info.m_autoRemove) && ((segment.m_flags & NetSegment.Flags.Untouchable) == NetSegment.Flags.None)))
        {
            Vector3 position = Singleton<NetManager>.instance.m_nodes.m_buffer[segment.m_startNode].m_position;
            Vector3 vector2 = Singleton<NetManager>.instance.m_nodes.m_buffer[segment.m_endNode].m_position;
            Vector3 vector3 = (Vector3) ((position + vector2) * 0.5f);
            vector3.y += info.m_maxHeight;
            NotificationEvent.RenderInstance(cameraInfo, NotificationEvent.Type.Bulldozer, vector3, 1f, 1f);
        }
    }

    public override void RenderGeometry(RenderManager.CameraInfo cameraInfo)
    {
        base.RenderGeometry(cameraInfo);
        NetInfo prefab = this.m_prefab;
        if ((((prefab != null) && !base.m_toolController.IsInsideUI) && (Cursor.visible && ((this.m_cachedErrors & (ToolBase.ToolErrors.Pending | ToolBase.ToolErrors.RaycastFailed)) == ToolBase.ToolErrors.None))) && ((this.m_mode != NetTool.Mode.Upgrade) || (this.m_cachedControlPointCount >= 2)))
        {
            if ((this.m_mode == NetTool.Mode.Straight) && (this.m_cachedControlPointCount < 1))
            {
                ushort num;
                ushort num2;
                int num3;
                int num4;
                base.m_toolController.RenderCollidingNotifications(cameraInfo, 0, 0);
                NetTool.ControlPoint startPoint = this.m_cachedControlPoints[0];
                startPoint.m_direction = Vector3.forward;
                CreateNode(prefab, startPoint, startPoint, startPoint, m_nodePositionsMain, 0, false, true, true, false, false, false, 0, out num, out num2, out num3, out num4);
            }
            else if (((this.m_mode == NetTool.Mode.Curved) || (this.m_mode == NetTool.Mode.Freeform)) && ((this.m_cachedControlPointCount < 2) && ((this.m_cachedControlPointCount == 0) || ((this.m_cachedControlPoints[1].m_node == 0) && (this.m_cachedControlPoints[1].m_segment == 0)))))
            {
                ushort num5;
                ushort num6;
                int num7;
                int num8;
                base.m_toolController.RenderCollidingNotifications(cameraInfo, 0, 0);
                NetTool.ControlPoint point2 = this.m_cachedControlPoints[0];
                point2.m_direction = Vector3.forward;
                CreateNode(prefab, point2, point2, point2, m_nodePositionsMain, 0, false, true, true, false, false, false, 0, out num5, out num6, out num7, out num8);
            }
            else
            {
                NetTool.ControlPoint point3;
                NetTool.ControlPoint point4;
                NetTool.ControlPoint point5;
                ushort num9;
                ushort num10;
                int num11;
                int num12;
                base.m_toolController.RenderCollidingNotifications(cameraInfo, 0, 0);
                if (this.m_cachedControlPointCount == 1)
                {
                    point3 = this.m_cachedControlPoints[0];
                    point5 = this.m_cachedControlPoints[1];
                    point4 = this.m_cachedControlPoints[1];
                    point4.m_node = 0;
                    point4.m_segment = 0;
                    point4.m_position = (Vector3) ((this.m_cachedControlPoints[0].m_position + this.m_cachedControlPoints[1].m_position) * 0.5f);
                    point4.m_elevation = (this.m_cachedControlPoints[0].m_elevation + this.m_cachedControlPoints[1].m_elevation) * 0.5f;
                }
                else
                {
                    point3 = this.m_cachedControlPoints[0];
                    point4 = this.m_cachedControlPoints[1];
                    point5 = this.m_cachedControlPoints[2];
                }
                if (point4.m_direction == Vector3.zero)
                {
                    point3.m_direction = Vector3.forward;
                    point4.m_direction = Vector3.forward;
                    point5.m_direction = Vector3.forward;
                }
                CreateNode(prefab, point3, point4, point5, m_nodePositionsMain, 0x3e8, false, true, true, false, false, false, 0, out num9, out num10, out num11, out num12);
            }
        }
    }

    private static void RenderNode(NetInfo info, Vector3 position, Vector3 direction)
    {
        if (info.m_nodes != null)
        {
            NetManager instance = Singleton<NetManager>.instance;
            position.y += 0.15f;
            Quaternion identity = Quaternion.identity;
            float vScale = 0.05f;
            Vector3 vector = (Vector3) (new Vector3(direction.z, 0f, -direction.x) * info.m_halfWidth);
            Vector3 startPos = position + vector;
            Vector3 vector3 = position - vector;
            Vector3 endPos = vector3;
            Vector3 vector5 = startPos;
            float num2 = Mathf.Min((float) (info.m_halfWidth * 1.333333f), (float) 16f);
            Vector3 vector6 = startPos - ((Vector3) (direction * num2));
            Vector3 vector7 = endPos - ((Vector3) (direction * num2));
            Vector3 vector8 = vector3 - ((Vector3) (direction * num2));
            Vector3 vector9 = vector5 - ((Vector3) (direction * num2));
            Vector3 vector10 = startPos + ((Vector3) (direction * num2));
            Vector3 vector11 = endPos + ((Vector3) (direction * num2));
            Vector3 vector12 = vector3 + ((Vector3) (direction * num2));
            Vector3 vector13 = vector5 + ((Vector3) (direction * num2));
            Matrix4x4 matrixx = NetSegment.CalculateControlMatrix(startPos, vector6, vector7, endPos, startPos, vector6, vector7, endPos, position, vScale);
            Matrix4x4 matrixx2 = NetSegment.CalculateControlMatrix(vector3, vector12, vector13, vector5, vector3, vector12, vector13, vector5, position, vScale);
            Matrix4x4 matrixx3 = NetSegment.CalculateControlMatrix(startPos, vector10, vector11, endPos, startPos, vector10, vector11, endPos, position, vScale);
            Matrix4x4 matrixx4 = NetSegment.CalculateControlMatrix(vector3, vector8, vector9, vector5, vector3, vector8, vector9, vector5, position, vScale);
            matrixx.SetRow(3, matrixx.GetRow(3) + new Vector4(0.2f, 0.2f, 0.2f, 0.2f));
            matrixx2.SetRow(3, matrixx2.GetRow(3) + new Vector4(0.2f, 0.2f, 0.2f, 0.2f));
            matrixx3.SetRow(3, matrixx3.GetRow(3) + new Vector4(0.2f, 0.2f, 0.2f, 0.2f));
            matrixx4.SetRow(3, matrixx4.GetRow(3) + new Vector4(0.2f, 0.2f, 0.2f, 0.2f));
            Vector4 vector14 = new Vector4(0.5f / info.m_halfWidth, 1f / info.m_segmentLength, 0.5f - ((info.m_pavementWidth / info.m_halfWidth) * 0.5f), (info.m_pavementWidth / info.m_halfWidth) * 0.5f);
            Vector4 zero = Vector4.zero;
            zero.w = (((matrixx.m33 + matrixx2.m33) + matrixx3.m33) + matrixx4.m33) * 0.25f;
            Vector4 vector16 = new Vector4((info.m_pavementWidth / info.m_halfWidth) * 0.5f, 1f, (info.m_pavementWidth / info.m_halfWidth) * 0.5f, 1f);
            instance.m_materialBlock.Clear();
            instance.m_materialBlock.AddMatrix(instance.ID_LeftMatrix, matrixx);
            instance.m_materialBlock.AddMatrix(instance.ID_RightMatrix, matrixx2);
            instance.m_materialBlock.AddMatrix(instance.ID_LeftMatrixB, matrixx3);
            instance.m_materialBlock.AddMatrix(instance.ID_RightMatrixB, matrixx4);
            instance.m_materialBlock.AddVector(instance.ID_MeshScale, vector14);
            instance.m_materialBlock.AddVector(instance.ID_CenterPos, zero);
            instance.m_materialBlock.AddVector(instance.ID_SideScale, vector16);
            instance.m_materialBlock.AddColor(instance.ID_Color, info.m_color);
            for (int i = 0; i < info.m_nodes.Length; i++)
            {
                NetInfo.Node node = info.m_nodes[i];
                if (node.CheckFlags(NetNode.Flags.None))
                {
                    Singleton<ToolManager>.instance.m_drawCallData.m_defaultCalls++;
                    Graphics.DrawMesh(node.m_nodeMesh, position, identity, node.m_nodeMaterial, node.m_layer, null, 0, instance.m_materialBlock);
                }
            }
        }
    }

    private static void RenderNodeBuilding(BuildingInfo info, Vector3 position, Vector3 direction)
    {
        if ((!Singleton<ToolManager>.instance.m_properties.IsInsideUI && (direction.sqrMagnitude >= 0.5f)) && (info.m_mesh != null))
        {
            direction.y = 0f;
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
            Building data = new Building();
            BuildingManager instance = Singleton<BuildingManager>.instance;
            instance.m_materialBlock.Clear();
            instance.m_materialBlock.AddVector(instance.ID_BuildingState, new Vector4(0f, 1000f, 0f, 256f));
            instance.m_materialBlock.AddVector(instance.ID_ObjectIndex, RenderManager.DefaultColorLocation);
            instance.m_materialBlock.AddColor(instance.ID_Color, info.m_buildingAI.GetColor(0, ref data, Singleton<InfoManager>.instance.CurrentMode));
            Singleton<ToolManager>.instance.m_drawCallData.m_defaultCalls++;
            Graphics.DrawMesh(info.m_mesh, position, rotation, info.m_material, info.m_prefabDataLayer, null, 0, instance.m_materialBlock);
        }
    }

    public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
    {
        base.RenderOverlay(cameraInfo);
        NetInfo prefab = this.m_prefab;
        if (((prefab != null) && !base.m_toolController.IsInsideUI) && (Cursor.visible && (this.m_mode == NetTool.Mode.Upgrade)))
        {
            NetManager instance = Singleton<NetManager>.instance;
            Color toolColor = base.GetToolColor(false, false);
            this.m_tempUpgraded.Clear();
            while (!Monitor.TryEnter(this.m_upgradedSegments, SimulationManager.SYNCHRONIZE_TIMEOUT))
            {
            }
            try
            {
                foreach (ushort num in this.m_upgradedSegments)
                {
                    this.m_tempUpgraded.Add(num);
                }
            }
            finally
            {
                Monitor.Exit(this.m_upgradedSegments);
            }
            for (int i = 0; i < this.m_tempUpgraded.m_size; i++)
            {
                ushort index = this.m_tempUpgraded.m_buffer[i];
                RenderOverlay(cameraInfo, ref instance.m_segments.m_buffer[index], toolColor, toolColor);
            }
        }
        if ((((prefab != null) && !base.m_toolController.IsInsideUI) && (Cursor.visible && ((this.m_cachedErrors & (ToolBase.ToolErrors.Pending | ToolBase.ToolErrors.RaycastFailed)) == ToolBase.ToolErrors.None))) && ((this.m_mode != NetTool.Mode.Upgrade) || (this.m_cachedControlPointCount >= 2)))
        {
            NetTool.ControlPoint point;
            NetTool.ControlPoint point2;
            NetTool.ControlPoint point3;
            BuildingInfo info2;
            Vector3 vector;
            Vector3 vector2;
            int num5;
            Bezier3 bezier;
            Segment3 segment;
            Segment3 segment2;
            bool flag4;
            bool flag5;
            if (this.m_cachedControlPointCount >= 2)
            {
                point = this.m_cachedControlPoints[0];
                point2 = this.m_cachedControlPoints[1];
                point3 = this.m_cachedControlPoints[this.m_cachedControlPointCount];
            }
            else if (((this.m_mode == NetTool.Mode.Straight) || (this.m_cachedControlPoints[this.m_cachedControlPointCount].m_node != 0)) || (this.m_cachedControlPoints[this.m_cachedControlPointCount].m_segment != 0))
            {
                point = this.m_cachedControlPoints[0];
                point2 = this.m_cachedControlPoints[this.m_cachedControlPointCount];
                point3 = this.m_cachedControlPoints[this.m_cachedControlPointCount];
            }
            else
            {
                point = this.m_cachedControlPoints[0];
                point2 = this.m_cachedControlPoints[0];
                point3 = this.m_cachedControlPoints[0];
            }
            ushort ignoreSegment = point2.m_segment;
            if ((point.m_segment == ignoreSegment) || (point3.m_segment == ignoreSegment))
            {
                ignoreSegment = 0;
            }
            Color importantSegmentColor = base.GetToolColor(false, this.m_cachedErrors != ToolBase.ToolErrors.None);
            Color nonImportantSegmentColor = base.GetToolColor(true, false);
            base.m_toolController.RenderColliding(cameraInfo, importantSegmentColor, nonImportantSegmentColor, importantSegmentColor, nonImportantSegmentColor, ignoreSegment, 0);
            Vector3 position = point2.m_position;
            prefab.m_netAI.CheckBuildPosition(false, false, true, this.m_mode != NetTool.Mode.Upgrade, ref point, ref point2, ref point3, out info2, out vector, out vector2, out num5);
            bool flag = position != point2.m_position;
            bezier.a = point.m_position;
            bezier.d = point3.m_position;
            bool smoothStart = true;
            bool smoothEnd = true;
            if (this.m_mode == NetTool.Mode.Upgrade)
            {
                smoothStart = (Singleton<NetManager>.instance.m_nodes.m_buffer[point.m_node].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None;
                smoothEnd = (Singleton<NetManager>.instance.m_nodes.m_buffer[point3.m_node].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None;
            }
            NetSegment.CalculateMiddlePoints(bezier.a, point2.m_direction, bezier.d, -point3.m_direction, smoothStart, smoothEnd, out bezier.b, out bezier.c);
            if (((this.m_mode == NetTool.Mode.Curved) || (this.m_mode == NetTool.Mode.Freeform)) && (this.m_cachedControlPointCount >= 2))
            {
                segment.a = point.m_position;
                segment.b = point2.m_position;
                flag4 = true;
                segment2.a = point2.m_position;
                segment2.b = point3.m_position;
                flag5 = true;
            }
            else if (((this.m_mode == NetTool.Mode.Curved) || (this.m_mode == NetTool.Mode.Freeform)) && (this.m_cachedControlPointCount >= 1))
            {
                segment.a = point.m_position;
                segment.b = this.m_cachedControlPoints[1].m_position;
                flag4 = true;
                segment2.a = new Vector3(-100000f, 0f, -100000f);
                segment2.b = new Vector3(-100000f, 0f, -100000f);
                flag5 = false;
            }
            else
            {
                segment.a = new Vector3(-100000f, 0f, -100000f);
                segment.b = new Vector3(-100000f, 0f, -100000f);
                flag4 = false;
                segment2.a = new Vector3(-100000f, 0f, -100000f);
                segment2.b = new Vector3(-100000f, 0f, -100000f);
                flag5 = false;
            }
            if (flag4 && flag5)
            {
                Singleton<ToolManager>.instance.m_drawCallData.m_overlayCalls++;
                Singleton<RenderManager>.instance.OverlayEffect.DrawSegment(cameraInfo, importantSegmentColor, segment, segment2, prefab.m_halfWidth * 2f, 8f, -1f, 1280f, false, false);
            }
            else if (flag4)
            {
                Singleton<ToolManager>.instance.m_drawCallData.m_overlayCalls++;
                Singleton<RenderManager>.instance.OverlayEffect.DrawSegment(cameraInfo, importantSegmentColor, segment, prefab.m_halfWidth * 2f, 8f, -1f, 1280f, false, false);
            }
            else if (flag5)
            {
                Singleton<ToolManager>.instance.m_drawCallData.m_overlayCalls++;
                Singleton<RenderManager>.instance.OverlayEffect.DrawSegment(cameraInfo, importantSegmentColor, segment2, prefab.m_halfWidth * 2f, 8f, -1f, 1280f, false, false);
            }
            Singleton<ToolManager>.instance.m_drawCallData.m_overlayCalls++;
            Singleton<RenderManager>.instance.OverlayEffect.DrawBezier(cameraInfo, importantSegmentColor, bezier, prefab.m_halfWidth * 2f, -100000f, -100000f, -1f, 1280f, false, false);
            if ((this.m_cachedErrors == ToolBase.ToolErrors.None) && !flag)
            {
                float lengthSnap = prefab.m_netAI.GetLengthSnap();
                if (((this.m_mode == NetTool.Mode.Straight) || (this.m_mode == NetTool.Mode.Curved)) && ((lengthSnap > 0.5f) && (this.m_cachedControlPointCount >= 1)))
                {
                    float num7;
                    Vector3 vector4 = this.m_cachedControlPoints[this.m_cachedControlPointCount].m_position;
                    Vector3 vector5 = this.m_cachedControlPoints[this.m_cachedControlPointCount - 1].m_position;
                    Color color = importantSegmentColor;
                    color.a *= 0.25f;
                    lengthSnap *= 10f;
                    Vector3 vector6 = VectorUtils.NormalizeXZ(vector4 - vector5, out num7);
                    Vector3 vector7 = new Vector3(vector6.z * (prefab.m_halfWidth + 48f), 0f, -vector6.x * (prefab.m_halfWidth + 48f));
                    int num8 = Mathf.RoundToInt((num7 / lengthSnap) + 0.01f);
                    if (num8 >= 1)
                    {
                        for (int j = 0; j <= num8; j++)
                        {
                            Vector3 vector8 = vector5 + ((Vector3) (vector6 * (lengthSnap * j)));
                            Segment3 segment3 = new Segment3(vector8 + vector7, vector8 - vector7);
                            Singleton<ToolManager>.instance.m_drawCallData.m_overlayCalls++;
                            Singleton<RenderManager>.instance.OverlayEffect.DrawSegment(cameraInfo, color, segment3, 0f, 0f, -1f, 1280f, false, true);
                        }
                    }
                }
            }
            if ((this.m_cachedErrors == ToolBase.ToolErrors.None) && (Vector3.SqrMagnitude(bezier.d - bezier.a) >= 1f))
            {
                float num10;
                bool flag6;
                Color color5;
                prefab.m_netAI.GetEffectRadius(out num10, out flag6, out color5);
                if (num10 > prefab.m_halfWidth)
                {
                    Singleton<ToolManager>.instance.m_drawCallData.m_overlayCalls++;
                    Singleton<RenderManager>.instance.OverlayEffect.DrawBezier(cameraInfo, color5, bezier, num10 * 2f, !flag6 ? -100000f : num10, !flag6 ? -100000f : num10, -1f, 1280f, false, true);
                }
            }
        }
    }

    public static void RenderOverlay(RenderManager.CameraInfo cameraInfo, ref NetSegment segment, Color importantColor, Color nonImportantColor)
    {
        NetInfo info = segment.Info;
        if ((info != null) && (((segment.m_flags & NetSegment.Flags.Untouchable) == NetSegment.Flags.None) || info.m_overlayVisible))
        {
            Bezier3 bezier;
            bezier.a = Singleton<NetManager>.instance.m_nodes.m_buffer[segment.m_startNode].m_position;
            bezier.d = Singleton<NetManager>.instance.m_nodes.m_buffer[segment.m_endNode].m_position;
            NetSegment.CalculateMiddlePoints(bezier.a, segment.m_startDirection, bezier.d, segment.m_endDirection, false, false, out bezier.b, out bezier.c);
            bool flag = false;
            bool flag2 = false;
            Color color = (((info.m_class.m_service > ItemClass.Service.Office) && !info.m_autoRemove) || ((segment.m_flags & NetSegment.Flags.Untouchable) != NetSegment.Flags.None)) ? importantColor : nonImportantColor;
            Singleton<ToolManager>.instance.m_drawCallData.m_overlayCalls++;
            Singleton<RenderManager>.instance.OverlayEffect.DrawBezier(cameraInfo, color, bezier, info.m_halfWidth * 2f, !flag ? -100000f : info.m_halfWidth, !flag2 ? -100000f : info.m_halfWidth, -1f, 1280f, false, false);
        }
    }

    private static void RenderSegment(NetInfo info, Vector3 startPosition, Vector3 endPosition, Vector3 startDirection, Vector3 endDirection, bool smoothStart, bool smoothEnd)
    {
        if (info.m_segments != null)
        {
            Vector3 vector7;
            Vector3 vector8;
            Vector3 vector9;
            Vector3 vector10;
            NetManager instance = Singleton<NetManager>.instance;
            startPosition.y += 0.15f;
            endPosition.y += 0.15f;
            Vector3 transform = (Vector3) ((startPosition + endPosition) * 0.5f);
            Quaternion identity = Quaternion.identity;
            Vector3 vector2 = (Vector3) (new Vector3(startDirection.z, 0f, -startDirection.x) * info.m_halfWidth);
            Vector3 startPos = startPosition - vector2;
            Vector3 vector4 = startPosition + vector2;
            vector2 = (Vector3) (new Vector3(endDirection.z, 0f, -endDirection.x) * info.m_halfWidth);
            Vector3 endPos = endPosition - vector2;
            Vector3 vector6 = endPosition + vector2;
            NetSegment.CalculateMiddlePoints(startPos, startDirection, endPos, -endDirection, smoothStart, smoothEnd, out vector7, out vector8);
            NetSegment.CalculateMiddlePoints(vector4, startDirection, vector6, -endDirection, smoothStart, smoothEnd, out vector9, out vector10);
            float vScale = 0.05f;
            Matrix4x4 matrixx = NetSegment.CalculateControlMatrix(startPos, vector7, vector8, endPos, vector4, vector9, vector10, vector6, transform, vScale);
            Matrix4x4 matrixx2 = NetSegment.CalculateControlMatrix(vector4, vector9, vector10, vector6, startPos, vector7, vector8, endPos, transform, vScale);
            Vector4 vector11 = new Vector4(0.5f / info.m_halfWidth, 1f / info.m_segmentLength, 1f, 1f);
            instance.m_materialBlock.Clear();
            instance.m_materialBlock.AddMatrix(instance.ID_LeftMatrix, matrixx);
            instance.m_materialBlock.AddMatrix(instance.ID_RightMatrix, matrixx2);
            instance.m_materialBlock.AddVector(instance.ID_MeshScale, vector11);
            instance.m_materialBlock.AddVector(instance.ID_ObjectIndex, RenderManager.DefaultColorLocation);
            instance.m_materialBlock.AddColor(instance.ID_Color, info.m_color);
            for (int i = 0; i < info.m_segments.Length; i++)
            {
                bool flag;
                NetInfo.Segment segment = info.m_segments[i];
                if (segment.CheckFlags(NetSegment.Flags.None, out flag))
                {
                    Singleton<ToolManager>.instance.m_drawCallData.m_defaultCalls++;
                    Graphics.DrawMesh(segment.m_segmentMesh, transform, identity, segment.m_segmentMaterial, segment.m_layer, null, 0, instance.m_materialBlock);
                }
            }
        }
    }

    public override void SimulationStep()
    {
        NetInfo prefab = this.m_prefab;
        if (prefab != null)
        {
            ToolBase.ToolErrors errors;
            int num15;
            int num16;
            if (this.m_mode == NetTool.Mode.Straight)
            {
                if (((prefab.m_class.m_service == ItemClass.Service.Road) || (prefab.m_class.m_service == ItemClass.Service.PublicTransport)) || (prefab.m_class.m_service == ItemClass.Service.Beautification))
                {
                    GuideController properties = Singleton<GuideManager>.instance.m_properties;
                    if (properties != null)
                    {
                        Singleton<NetManager>.instance.m_optionsNotUsed.Activate(properties.m_roadOptionsNotUsed, prefab.m_class.m_service);
                    }
                }
            }
            else
            {
                ServiceTypeGuide optionsNotUsed = Singleton<NetManager>.instance.m_optionsNotUsed;
                if ((optionsNotUsed != null) && !optionsNotUsed.m_disabled)
                {
                    optionsNotUsed.Disable();
                }
            }
            if (this.m_elevation == 0)
            {
                int num;
                int num2;
                prefab.m_netAI.GetElevationLimits(out num, out num2);
                if (num2 > num)
                {
                    GuideController controller2 = Singleton<GuideManager>.instance.m_properties;
                    if (controller2 != null)
                    {
                        Singleton<NetManager>.instance.m_elevationNotUsed.Activate(controller2.m_elevationNotUsed, prefab.m_class.m_service);
                    }
                }
            }
            else
            {
                ServiceTypeGuide elevationNotUsed = Singleton<NetManager>.instance.m_elevationNotUsed;
                if ((elevationNotUsed != null) && !elevationNotUsed.m_disabled)
                {
                    elevationNotUsed.Disable();
                }
            }
            if (((prefab.m_hasForwardVehicleLanes || prefab.m_hasBackwardVehicleLanes) && (!prefab.m_hasForwardVehicleLanes || !prefab.m_hasBackwardVehicleLanes)) && ((((prefab.m_class.m_service == ItemClass.Service.Road) || (prefab.m_class.m_service == ItemClass.Service.PublicTransport)) || (prefab.m_class.m_service == ItemClass.Service.Beautification)) && (this.m_controlPointCount >= 1)))
            {
                GuideController controller3 = Singleton<GuideManager>.instance.m_properties;
                if (controller3 != null)
                {
                    Singleton<NetManager>.instance.m_onewayRoadPlacement.Activate(controller3.m_onewayRoadPlacement);
                }
            }
            if (this.m_mode == NetTool.Mode.Upgrade)
            {
                Singleton<NetManager>.instance.m_manualUpgrade.Deactivate();
            }
            Vector3 position = this.m_controlPoints[this.m_controlPointCount].m_position;
            bool flag = false;
            if (this.m_mode == NetTool.Mode.Upgrade)
            {
                ToolBase.RaycastOutput output;
                NetManager instance = Singleton<NetManager>.instance;
                ToolBase.RaycastInput input = new ToolBase.RaycastInput(this.m_mouseRay, this.m_mouseRayLength) {
                    m_netService = new ToolBase.RaycastService(prefab.m_class.m_service, prefab.m_class.m_subService, prefab.m_class.m_layer),
                    m_ignoreTerrain = true,
                    m_ignoreNodeFlags = ~NetNode.Flags.None,
                    m_ignoreSegmentFlags = NetSegment.Flags.Untouchable
                };
                if (this.m_mouseRayValid && ToolBase.RayCast(input, out output))
                {
                    if (output.m_netSegment != 0)
                    {
                        NetInfo info = instance.m_segments.m_buffer[output.m_netSegment].Info;
                        if ((info.m_class.m_service != prefab.m_class.m_service) || (info.m_class.m_subService != prefab.m_class.m_subService))
                        {
                            output.m_netSegment = 0;
                        }
                        else if (this.m_upgradedSegments.Contains(output.m_netSegment))
                        {
                            output.m_netSegment = 0;
                        }
                    }
                    if (output.m_netSegment != 0)
                    {
                        NetTool.ControlPoint point;
                        NetTool.ControlPoint point2;
                        NetTool.ControlPoint point3;
                        point.m_node = instance.m_segments.m_buffer[output.m_netSegment].m_startNode;
                        point.m_segment = 0;
                        point.m_position = instance.m_nodes.m_buffer[point.m_node].m_position;
                        point.m_direction = instance.m_segments.m_buffer[output.m_netSegment].m_startDirection;
                        point.m_elevation = instance.m_nodes.m_buffer[point.m_node].m_elevation;
                        point.m_outside = (instance.m_nodes.m_buffer[point.m_node].m_flags & NetNode.Flags.Outside) != NetNode.Flags.None;
                        point3.m_node = instance.m_segments.m_buffer[output.m_netSegment].m_endNode;
                        point3.m_segment = 0;
                        point3.m_position = instance.m_nodes.m_buffer[point3.m_node].m_position;
                        point3.m_direction = -instance.m_segments.m_buffer[output.m_netSegment].m_endDirection;
                        point3.m_elevation = instance.m_nodes.m_buffer[point3.m_node].m_elevation;
                        point3.m_outside = (instance.m_nodes.m_buffer[point3.m_node].m_flags & NetNode.Flags.Outside) != NetNode.Flags.None;
                        point2.m_node = 0;
                        point2.m_segment = output.m_netSegment;
                        point2.m_position = point.m_position + ((Vector3) (point.m_direction * (prefab.GetMinNodeDistance() + 1f)));
                        point2.m_direction = point.m_direction;
                        point2.m_elevation = Mathf.Lerp(point.m_elevation, point3.m_elevation, 0.5f);
                        point2.m_outside = false;
                        this.m_controlPoints[0] = point;
                        this.m_controlPoints[1] = point2;
                        this.m_controlPoints[2] = point3;
                        this.m_controlPointCount = 2;
                    }
                    else
                    {
                        this.m_controlPointCount = 0;
                        this.m_controlPoints[this.m_controlPointCount] = new NetTool.ControlPoint();
                    }
                }
                else
                {
                    this.m_controlPointCount = 0;
                    this.m_controlPoints[this.m_controlPointCount] = new NetTool.ControlPoint();
                    flag = true;
                }
            }
            else
            {
                NetNode.Flags none;
                NetSegment.Flags flags2;
                Building.Flags untouchable;
                NetTool.ControlPoint p = new NetTool.ControlPoint();
                float elevation = this.GetElevation(prefab);
                if (((this.m_mode == NetTool.Mode.Curved) || (this.m_mode == NetTool.Mode.Freeform)) && (this.m_controlPointCount == 1))
                {
                    none = ~NetNode.Flags.None;
                    flags2 = ~NetSegment.Flags.None;
                }
                else
                {
                    none = NetNode.Flags.None;
                    flags2 = NetSegment.Flags.None;
                }
                if (prefab.m_snapBuildingNodes)
                {
                    untouchable = Building.Flags.Untouchable;
                }
                else
                {
                    untouchable = ~Building.Flags.None;
                }
                if (this.m_mouseRayValid && MakeControlPoint(this.m_mouseRay, this.m_mouseRayLength, prefab, false, none, flags2, untouchable, elevation, out p))
                {
                    bool flag2 = false;
                    if (((p.m_node == 0) && (p.m_segment == 0)) && !p.m_outside)
                    {
                        if (this.m_snap)
                        {
                            if ((Singleton<ToolManager>.instance.m_properties.m_mode & ItemClass.Availability.AssetEditor) != ItemClass.Availability.None)
                            {
                                Vector3 zero = Vector3.zero;
                                PrefabInfo editPrefabInfo = Singleton<ToolManager>.instance.m_properties.m_editPrefabInfo;
                                if (editPrefabInfo != null)
                                {
                                    if ((editPrefabInfo.GetWidth() & 1) != 0)
                                    {
                                        zero.x += 4f;
                                    }
                                    if ((editPrefabInfo.GetLength() & 1) != 0)
                                    {
                                        zero.z += 4f;
                                    }
                                }
                                this.Snap(this.m_prefab, ref p.m_position, ref p.m_direction, zero, 0f);
                                flag2 = true;
                            }
                            else
                            {
                                Singleton<NetManager>.instance.GetClosestSegments(p.m_position, this.m_closeSegments, out this.m_closeSegmentCount);
                                p.m_direction = Vector3.zero;
                                float distanceSq = 256f;
                                ushort num5 = 0;
                                for (int i = 0; i < this.m_closeSegmentCount; i++)
                                {
                                    Singleton<NetManager>.instance.m_segments.m_buffer[this.m_closeSegments[i]].GetClosestZoneBlock(p.m_position, ref distanceSq, ref num5);
                                }
                                if (num5 != 0)
                                {
                                    ZoneBlock block = Singleton<ZoneManager>.instance.m_blocks.m_buffer[num5];
                                    this.Snap(this.m_prefab, ref p.m_position, ref p.m_direction, block.m_position, block.m_angle);
                                    flag2 = true;
                                }
                            }
                        }
                        float introduced57 = NetSegment.SampleTerrainHeight(prefab, p.m_position, false);
                        p.m_position.y = introduced57 + p.m_elevation;
                    }
                    else
                    {
                        flag2 = true;
                    }
                    bool success = false;
                    if ((this.m_controlPointCount == 2) && (this.m_mode == NetTool.Mode.Freeform))
                    {
                        Vector3 a = p.m_position - this.m_controlPoints[0].m_position;
                        Vector3 direction = this.m_controlPoints[1].m_direction;
                        a.y = 0f;
                        direction.y = 0f;
                        float num7 = Vector3.SqrMagnitude(a);
                        a = Vector3.Normalize(a);
                        float num8 = Mathf.Min(1.178097f, Mathf.Acos(Vector3.Dot(a, direction)));
                        float num9 = Mathf.Sqrt((0.5f * num7) / Mathf.Max((float) 0.001f, (float) (1f - Mathf.Cos(3.141593f - (2f * num8)))));
                        this.m_controlPoints[1].m_position = this.m_controlPoints[0].m_position + ((Vector3) (direction * num9));
                        p.m_direction = p.m_position - this.m_controlPoints[1].m_position;
                        p.m_direction.y = 0f;
                        p.m_direction.Normalize();
                    }
                    else if (this.m_controlPointCount != 0)
                    {
                        NetTool.ControlPoint point6;
                        NetTool.ControlPoint oldPoint = this.m_controlPoints[this.m_controlPointCount - 1];
                        p.m_direction = p.m_position - oldPoint.m_position;
                        p.m_direction.y = 0f;
                        p.m_direction.Normalize();
                        float minNodeDistance = prefab.GetMinNodeDistance();
                        minNodeDistance *= minNodeDistance;
                        float num11 = minNodeDistance;
                        if (this.m_snap)
                        {
                            point6 = SnapDirection(p, oldPoint, prefab, out success, out minNodeDistance);
                            p = point6;
                        }
                        else
                        {
                            point6 = p;
                        }
                        if ((p.m_segment != 0) && (minNodeDistance < num11))
                        {
                            point6.m_position = Singleton<NetManager>.instance.m_segments.m_buffer[p.m_segment].GetClosestPosition(p.m_position, p.m_direction);
                        }
                        else if (((p.m_segment == 0) && (p.m_node == 0)) && (!p.m_outside && this.m_snap))
                        {
                            float lengthSnap = prefab.m_netAI.GetLengthSnap();
                            if (((this.m_mode != NetTool.Mode.Freeform) && (success || !flag2)) && (lengthSnap != 0f))
                            {
                                Vector3 vector5 = p.m_position - oldPoint.m_position;
                                Vector3 vector6 = new Vector3(vector5.x, 0f, vector5.z);
                                float magnitude = vector6.magnitude;
                                if (magnitude < 0.001f)
                                {
                                    point6.m_position = oldPoint.m_position;
                                }
                                else
                                {
                                    int num14 = Mathf.Max(1, Mathf.RoundToInt(magnitude / lengthSnap));
                                    point6.m_position = oldPoint.m_position + ((Vector3) (vector5 * ((num14 * lengthSnap) / magnitude)));
                                }
                            }
                        }
                        p = point6;
                    }
                }
                else
                {
                    flag = true;
                }
                this.m_controlPoints[this.m_controlPointCount] = p;
            }
            bool needMoney = (Singleton<ToolManager>.instance.m_properties.m_mode & ItemClass.Availability.Game) != ItemClass.Availability.None;
            if (this.m_controlPointCount == 2)
            {
                ushort num17;
                ushort num18;
                if (Vector3.SqrMagnitude(position - this.m_controlPoints[this.m_controlPointCount].m_position) > 1f)
                {
                    this.m_lengthChanging = true;
                }
                errors = CreateNode(prefab, this.m_controlPoints[this.m_controlPointCount - 2], this.m_controlPoints[this.m_controlPointCount - 1], this.m_controlPoints[this.m_controlPointCount], m_nodePositionsSimulation, 0x3e8, true, false, true, needMoney, false, this.m_switchingDir, 0, out num17, out num18, out num15, out num16);
            }
            else if (this.m_controlPointCount == 1)
            {
                if (Vector3.SqrMagnitude(position - this.m_controlPoints[this.m_controlPointCount].m_position) > 1f)
                {
                    this.m_lengthChanging = true;
                }
                NetTool.ControlPoint middlePoint = this.m_controlPoints[1];
                if (((this.m_mode != NetTool.Mode.Curved) && (this.m_mode != NetTool.Mode.Freeform)) || ((middlePoint.m_node != 0) || (middlePoint.m_segment != 0)))
                {
                    ushort num19;
                    ushort num20;
                    middlePoint.m_node = 0;
                    middlePoint.m_segment = 0;
                    middlePoint.m_position = (Vector3) ((this.m_controlPoints[0].m_position + this.m_controlPoints[1].m_position) * 0.5f);
                    errors = CreateNode(prefab, this.m_controlPoints[this.m_controlPointCount - 1], middlePoint, this.m_controlPoints[this.m_controlPointCount], m_nodePositionsSimulation, 0x3e8, true, false, true, needMoney, false, this.m_switchingDir, 0, out num19, out num20, out num15, out num16);
                }
                else
                {
                    base.m_toolController.ClearColliding();
                    errors = ToolBase.ToolErrors.None;
                    num15 = 0;
                    num16 = 0;
                }
            }
            else
            {
                base.m_toolController.ClearColliding();
                errors = ToolBase.ToolErrors.None;
                num15 = 0;
                num16 = 0;
            }
            if (flag)
            {
                errors |= ToolBase.ToolErrors.RaycastFailed;
            }
            while (!Monitor.TryEnter(this.m_cacheLock, SimulationManager.SYNCHRONIZE_TIMEOUT))
            {
            }
            try
            {
                this.m_buildErrors = errors;
                this.m_constructionCost = !needMoney ? 0 : num15;
                this.m_productionRate = num16;
            }
            finally
            {
                Monitor.Exit(this.m_cacheLock);
            }
            if (((this.m_mode == NetTool.Mode.Upgrade) && this.m_upgrading) && ((this.m_controlPointCount == 2) && (this.m_buildErrors == ToolBase.ToolErrors.None)))
            {
                this.CreateNodeImpl(this.m_switchingDir);
            }
        }
    }

    private void Snap(NetInfo info, ref Vector3 point, ref Vector3 direction, Vector3 refPoint, float refAngle)
    {
        direction = new Vector3(Mathf.Cos(refAngle), 0f, Mathf.Sin(refAngle));
        Vector3 vector = (Vector3) (direction * 8f);
        Vector3 vector2 = new Vector3(vector.z, 0f, -vector.x);
        if (info.m_halfWidth <= 4f)
        {
            refPoint.x += (vector.x * 0.5f) + (vector2.x * 0.5f);
            refPoint.z += (vector.z * 0.5f) + (vector2.z * 0.5f);
        }
        Vector2 vector3 = new Vector2(point.x - refPoint.x, point.z - refPoint.z);
        float num = Mathf.Round(((vector3.x * vector.x) + (vector3.y * vector.z)) * 0.015625f);
        float num2 = Mathf.Round(((vector3.x * vector2.x) + (vector3.y * vector2.z)) * 0.015625f);
        point.x = (refPoint.x + (num * vector.x)) + (num2 * vector2.x);
        point.z = (refPoint.z + (num * vector.z)) + (num2 * vector2.z);
    }

    public static NetTool.ControlPoint SnapDirection(NetTool.ControlPoint newPoint, NetTool.ControlPoint oldPoint, NetInfo info, out bool success, out float minDistanceSq)
    {
        minDistanceSq = info.GetMinNodeDistance();
        minDistanceSq *= minDistanceSq;
        NetTool.ControlPoint point = newPoint;
        success = false;
        if (oldPoint.m_node != 0)
        {
            NetNode node = Singleton<NetManager>.instance.m_nodes.m_buffer[oldPoint.m_node];
            for (int i = 0; i < 8; i++)
            {
                ushort index = node.GetSegment(i);
                if (index != 0)
                {
                    NetSegment segment = Singleton<NetManager>.instance.m_segments.m_buffer[index];
                    Vector3 v = (segment.m_startNode != oldPoint.m_node) ? segment.m_endDirection : segment.m_startDirection;
                    v.y = 0f;
                    if ((newPoint.m_node == 0) && !newPoint.m_outside)
                    {
                        Vector3 vector2 = (Vector3) Line2.Offset(VectorUtils.XZ(v), VectorUtils.XZ(oldPoint.m_position - newPoint.m_position));
                        float sqrMagnitude = vector2.sqrMagnitude;
                        if (sqrMagnitude < minDistanceSq)
                        {
                            vector2 = (newPoint.m_position + vector2) - oldPoint.m_position;
                            float num4 = (vector2.x * v.x) + (vector2.z * v.z);
                            point.m_position = oldPoint.m_position + ((Vector3) (v * num4));
                            point.m_position.y = newPoint.m_position.y;
                            point.m_direction = (num4 >= 0f) ? v : -v;
                            minDistanceSq = sqrMagnitude;
                            success = true;
                        }
                        if (info.m_maxBuildAngle > 89f)
                        {
                            v = new Vector3(v.z, 0f, -v.x);
                            vector2 = (Vector3) Line2.Offset(VectorUtils.XZ(v), VectorUtils.XZ(oldPoint.m_position - newPoint.m_position));
                            sqrMagnitude = vector2.sqrMagnitude;
                            if (sqrMagnitude < minDistanceSq)
                            {
                                vector2 = (newPoint.m_position + vector2) - oldPoint.m_position;
                                float num5 = (vector2.x * v.x) + (vector2.z * v.z);
                                point.m_position = oldPoint.m_position + ((Vector3) (v * num5));
                                point.m_position.y = newPoint.m_position.y;
                                point.m_direction = (num5 >= 0f) ? v : -v;
                                minDistanceSq = sqrMagnitude;
                                success = true;
                            }
                        }
                    }
                    else
                    {
                        float num6 = (newPoint.m_direction.x * v.x) + (newPoint.m_direction.z * v.z);
                        if (num6 > 0.999f)
                        {
                            point.m_direction = v;
                            success = true;
                        }
                        if (num6 < -0.999f)
                        {
                            point.m_direction = -v;
                            success = true;
                        }
                        if (info.m_maxBuildAngle > 89f)
                        {
                            v = new Vector3(v.z, 0f, -v.x);
                            num6 = (newPoint.m_direction.x * v.x) + (newPoint.m_direction.z * v.z);
                            if (num6 > 0.999f)
                            {
                                point.m_direction = v;
                                success = true;
                            }
                            if (num6 < -0.999f)
                            {
                                point.m_direction = -v;
                                success = true;
                            }
                        }
                    }
                }
            }
            return point;
        }
        if (oldPoint.m_direction.sqrMagnitude > 0.5f)
        {
            Vector3 direction = oldPoint.m_direction;
            if ((newPoint.m_node == 0) && !newPoint.m_outside)
            {
                Vector3 vector4 = (Vector3) Line2.Offset(VectorUtils.XZ(direction), VectorUtils.XZ(oldPoint.m_position - newPoint.m_position));
                float num7 = vector4.sqrMagnitude;
                if (num7 < minDistanceSq)
                {
                    vector4 = (newPoint.m_position + vector4) - oldPoint.m_position;
                    float num8 = (vector4.x * direction.x) + (vector4.z * direction.z);
                    point.m_position = oldPoint.m_position + ((Vector3) (direction * num8));
                    point.m_position.y = newPoint.m_position.y;
                    point.m_direction = (num8 >= 0f) ? direction : -direction;
                    minDistanceSq = num7;
                    success = true;
                }
                if (info.m_maxBuildAngle > 89f)
                {
                    direction = new Vector3(direction.z, 0f, -direction.x);
                    vector4 = (Vector3) Line2.Offset(VectorUtils.XZ(direction), VectorUtils.XZ(oldPoint.m_position - newPoint.m_position));
                    num7 = vector4.sqrMagnitude;
                    if (num7 < minDistanceSq)
                    {
                        vector4 = (newPoint.m_position + vector4) - oldPoint.m_position;
                        float num9 = (vector4.x * direction.x) + (vector4.z * direction.z);
                        point.m_position = oldPoint.m_position + ((Vector3) (direction * num9));
                        point.m_position.y = newPoint.m_position.y;
                        point.m_direction = (num9 >= 0f) ? direction : -direction;
                        minDistanceSq = num7;
                        success = true;
                    }
                }
                return point;
            }
            float num10 = (newPoint.m_direction.x * direction.x) + (newPoint.m_direction.z * direction.z);
            if (num10 > 0.999f)
            {
                point.m_direction = direction;
                success = true;
            }
            if (num10 < -0.999f)
            {
                point.m_direction = -direction;
                success = true;
            }
            if (info.m_maxBuildAngle <= 89f)
            {
                return point;
            }
            direction = new Vector3(direction.z, 0f, -direction.x);
            num10 = (newPoint.m_direction.x * direction.x) + (newPoint.m_direction.z * direction.z);
            if (num10 > 0.999f)
            {
                point.m_direction = direction;
                success = true;
            }
            if (num10 < -0.999f)
            {
                point.m_direction = -direction;
                success = true;
            }
        }
        return point;
    }

    private static bool SplitSegment(ushort segment, out ushort node, Vector3 position)
    {
        Vector3 vector;
        Vector3 vector2;
        NetSegment segment2 = Singleton<NetManager>.instance.m_segments.m_buffer[segment];
        NetInfo info = segment2.Info;
        uint buildIndex = segment2.m_buildIndex;
        NetNode node2 = Singleton<NetManager>.instance.m_nodes.m_buffer[segment2.m_startNode];
        NetNode node3 = Singleton<NetManager>.instance.m_nodes.m_buffer[segment2.m_endNode];
        segment2.GetClosestPositionAndDirection(position, out vector, out vector2);
        Singleton<NetManager>.instance.ReleaseSegment(segment, true);
        bool flag = false;
        if ((node2.m_flags & (NetNode.Flags.Untouchable | NetNode.Flags.Moveable)) == NetNode.Flags.Moveable)
        {
            if ((node2.m_flags & NetNode.Flags.Middle) != NetNode.Flags.None)
            {
                MoveMiddleNode(ref segment2.m_startNode, ref segment2.m_startDirection, position);
            }
            else if ((node2.m_flags & NetNode.Flags.End) != NetNode.Flags.None)
            {
                MoveEndNode(ref segment2.m_startNode, ref segment2.m_startDirection, position);
            }
        }
        if ((node3.m_flags & (NetNode.Flags.Untouchable | NetNode.Flags.Moveable)) == NetNode.Flags.Moveable)
        {
            if ((node3.m_flags & NetNode.Flags.Middle) != NetNode.Flags.None)
            {
                MoveMiddleNode(ref segment2.m_endNode, ref segment2.m_endDirection, position);
            }
            else if ((node3.m_flags & NetNode.Flags.End) != NetNode.Flags.None)
            {
                MoveEndNode(ref segment2.m_endNode, ref segment2.m_endDirection, position);
            }
        }
        ushort num2 = 0;
        ushort num3 = 0;
        if (Singleton<NetManager>.instance.CreateNode(out node, ref Singleton<SimulationManager>.instance.m_randomizer, info, position, buildIndex))
        {
            Singleton<NetManager>.instance.m_nodes.m_buffer[node].m_elevation = (byte) ((node2.m_elevation + node3.m_elevation) / 2);
            if (segment2.m_startNode != 0)
            {
                if (Singleton<NetManager>.instance.CreateSegment(out num2, ref Singleton<SimulationManager>.instance.m_randomizer, info, segment2.m_startNode, node, segment2.m_startDirection, -vector2, buildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, (segment2.m_flags & NetSegment.Flags.Invert) != NetSegment.Flags.None))
                {
                    SimulationManager instance = Singleton<SimulationManager>.instance;
                    instance.m_currentBuildIndex += 2;
                }
                else
                {
                    flag = true;
                }
            }
            if (segment2.m_endNode != 0)
            {
                if (info.m_requireContinuous)
                {
                    if (Singleton<NetManager>.instance.CreateSegment(out num3, ref Singleton<SimulationManager>.instance.m_randomizer, info, node, segment2.m_endNode, vector2, segment2.m_endDirection, buildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, (segment2.m_flags & NetSegment.Flags.Invert) != NetSegment.Flags.None))
                    {
                        SimulationManager local2 = Singleton<SimulationManager>.instance;
                        local2.m_currentBuildIndex += 2;
                    }
                    else
                    {
                        flag = true;
                    }
                }
                else if (Singleton<NetManager>.instance.CreateSegment(out num3, ref Singleton<SimulationManager>.instance.m_randomizer, info, segment2.m_endNode, node, segment2.m_endDirection, vector2, buildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, (segment2.m_flags & NetSegment.Flags.Invert) == NetSegment.Flags.None))
                {
                    SimulationManager local3 = Singleton<SimulationManager>.instance;
                    local3.m_currentBuildIndex += 2;
                }
                else
                {
                    flag = true;
                }
            }
        }
        else
        {
            flag = true;
        }
        if (flag && (node != 0))
        {
            Singleton<NetManager>.instance.ReleaseNode(node);
            node = 0;
        }
        return !flag;
    }

    private static ToolBase.ToolErrors TestNodeBuilding(BuildingInfo info, Vector3 position, Vector3 direction, ushort ignoreNode, ushort ignoreSegment, ushort ignoreBuilding, bool test, ulong[] collidingSegmentBuffer, ulong[] collidingBuildingBuffer)
    {
        Vector2 vector = (Vector2) (new Vector2(direction.x, direction.z) * ((info.m_cellLength * 4f) - 0.8f));
        Vector2 vector2 = (Vector2) (new Vector2(direction.z, -direction.x) * ((info.m_cellWidth * 4f) - 0.8f));
        if (info.m_circular)
        {
            vector2 = (Vector2) (vector2 * 0.7f);
            vector = (Vector2) (vector * 0.7f);
        }
        Vector2 vector3 = VectorUtils.XZ(position);
        Quad2 quad = new Quad2 {
            a = (vector3 - vector2) - vector,
            b = (vector3 - vector2) + vector,
            c = (vector3 + vector2) + vector,
            d = (vector3 + vector2) - vector
        };
        ToolBase.ToolErrors none = ToolBase.ToolErrors.None;
        float minY = Mathf.Min(position.y, Singleton<TerrainManager>.instance.SampleRawHeightSmooth(position));
        float maxY = position.y + info.m_generatedInfo.m_size.y;
        Singleton<NetManager>.instance.OverlapQuad(quad, minY, maxY, info.m_class.m_layer, ignoreNode, 0, ignoreSegment, collidingSegmentBuffer);
        Singleton<BuildingManager>.instance.OverlapQuad(quad, minY, maxY, info.m_class.m_layer, ignoreBuilding, ignoreNode, 0, collidingBuildingBuffer);
        if ((Singleton<ToolManager>.instance.m_properties.m_mode & ItemClass.Availability.AssetEditor) != ItemClass.Availability.None)
        {
            float num3 = 256f;
            if (((quad.a.x < -num3) || (quad.a.x > num3)) || ((quad.a.y < -num3) || (quad.a.y > num3)))
            {
                none |= ToolBase.ToolErrors.OutOfArea;
            }
            if (((quad.b.x < -num3) || (quad.b.x > num3)) || ((quad.b.y < -num3) || (quad.b.y > num3)))
            {
                none |= ToolBase.ToolErrors.OutOfArea;
            }
            if (((quad.c.x < -num3) || (quad.c.x > num3)) || ((quad.c.y < -num3) || (quad.c.y > num3)))
            {
                none |= ToolBase.ToolErrors.OutOfArea;
            }
            if (((quad.d.x < -num3) || (quad.d.x > num3)) || ((quad.d.y < -num3) || (quad.d.y > num3)))
            {
                none |= ToolBase.ToolErrors.OutOfArea;
            }
        }
        else if (Singleton<GameAreaManager>.instance.QuadOutOfArea(quad))
        {
            none |= ToolBase.ToolErrors.OutOfArea;
        }
        if (!Singleton<BuildingManager>.instance.CheckLimits())
        {
            none |= ToolBase.ToolErrors.LastVisibleError;
        }
        return none;
    }

    private static void TryMoveNode(ref ushort node, ref Vector3 direction, NetInfo segmentInfo, Vector3 endPos)
    {
        NetManager instance = Singleton<NetManager>.instance;
        if (((instance.m_nodes.m_buffer[node].m_flags & (NetNode.Flags.Untouchable | NetNode.Flags.Moveable | NetNode.Flags.End)) == (NetNode.Flags.Moveable | NetNode.Flags.End)) && instance.m_nodes.m_buffer[node].Info.IsCombatible(segmentInfo))
        {
            for (int i = 0; i < 8; i++)
            {
                ushort segment = instance.m_nodes.m_buffer[node].GetSegment(i);
                if (segment != 0)
                {
                    ushort startNode = instance.m_segments.m_buffer[segment].m_startNode;
                    ushort endNode = instance.m_segments.m_buffer[segment].m_endNode;
                    Vector3 startDirection = instance.m_segments.m_buffer[segment].m_startDirection;
                    Vector3 endDirection = instance.m_segments.m_buffer[segment].m_endDirection;
                    Vector3 position = instance.m_nodes.m_buffer[startNode].m_position;
                    Vector3 vector4 = instance.m_nodes.m_buffer[endNode].m_position;
                    if (NetSegment.IsStraight(position, startDirection, vector4, endDirection))
                    {
                        Vector3 vector5 = (startNode != node) ? endDirection : startDirection;
                        float num5 = (direction.x * vector5.x) + (direction.z * vector5.z);
                        if (num5 < -0.999f)
                        {
                            MoveMiddleNode(ref node, ref direction, endPos);
                        }
                    }
                    return;
                }
            }
        }
    }


    [CompilerGenerated]
    private sealed class TCancelNodeTc__Iterator61 : IEnumerator, IDisposable, IEnumerator<object>
    {
        internal object Scurrent;
        internal int SPC;
        internal NetToolFine TTf__this;

        [DebuggerHidden]
        public void Dispose()
        {
            this.SPC = -1;
        }

        public bool MoveNext()
        {
            uint num = (uint) this.SPC;
            this.SPC = -1;
            switch (num)
            {
                case 0:
                    this.TTf__this.m_upgrading = false;
                    this.TTf__this.m_switchingDir = false;
                    while (!Monitor.TryEnter(this.TTf__this.m_upgradedSegments, SimulationManager.SYNCHRONIZE_TIMEOUT))
                    {
                    }
                    try
                    {
                        this.TTf__this.m_upgradedSegments.Clear();
                    }
                    finally
                    {
                        Monitor.Exit(this.TTf__this.m_upgradedSegments);
                    }
                    if (this.TTf__this.m_mode == NetTool.Mode.Upgrade)
                    {
                        this.TTf__this.m_controlPointCount = 0;
                    }
                    else
                    {
                        this.TTf__this.m_controlPointCount = Mathf.Max(0, this.TTf__this.m_controlPointCount - 1);
                    }
                    this.Scurrent = 0;
                    this.SPC = 1;
                    return true;

                case 1:
                    this.SPC = -1;
                    break;
            }
            return false;
        }

        [DebuggerHidden]
        public void Reset()
        {
            throw new NotSupportedException();
        }

        object IEnumerator<object>.Current
        {
            [DebuggerHidden]
            get
            {
                return this.Scurrent;
            }
        }

        object IEnumerator.Current
        {
            [DebuggerHidden]
            get
            {
                return this.Scurrent;
            }
        }
    }

    [CompilerGenerated]
    private sealed class TCancelUpgradingTc__Iterator62 : IEnumerator, IDisposable, IEnumerator<object>
    {
        internal object Scurrent;
        internal int SPC;
        internal NetToolFine TTf__this;

        [DebuggerHidden]
        public void Dispose()
        {
            this.SPC = -1;
        }

        public bool MoveNext()
        {
            uint num = (uint) this.SPC;
            this.SPC = -1;
            switch (num)
            {
                case 0:
                    this.TTf__this.m_upgrading = false;
                    this.TTf__this.m_switchingDir = false;
                    while (!Monitor.TryEnter(this.TTf__this.m_upgradedSegments, SimulationManager.SYNCHRONIZE_TIMEOUT))
                    {
                    }
                    try
                    {
                        this.TTf__this.m_upgradedSegments.Clear();
                    }
                    finally
                    {
                        Monitor.Exit(this.TTf__this.m_upgradedSegments);
                    }
                    this.Scurrent = 0;
                    this.SPC = 1;
                    return true;

                case 1:
                    this.SPC = -1;
                    break;
            }
            return false;
        }

        [DebuggerHidden]
        public void Reset()
        {
            throw new NotSupportedException();
        }

        object IEnumerator<object>.Current
        {
            [DebuggerHidden]
            get
            {
                return this.Scurrent;
            }
        }

        object IEnumerator.Current
        {
            [DebuggerHidden]
            get
            {
                return this.Scurrent;
            }
        }
    }

    [CompilerGenerated]
    private sealed class TChangeElevationTc__Iterator63 : IEnumerator, IDisposable, IEnumerator<bool>
    {
        internal bool Scurrent;
        internal int SPC;
        internal int TSTdelta;
        internal NetToolFine f_this;
        internal int elevation;
        internal NetInfo info;
        internal int max_height;
        internal int min_height;
        internal bool result;
        internal int delta;

        [DebuggerHidden]
        public void Dispose()
        {
            this.SPC = -1;
        }

        public bool MoveNext()
        {
            uint num = (uint) this.SPC;
            this.SPC = -1;
            switch (num)
            {
                case 0:
                    this.result = false;
                    this.info = this.f_this.m_prefab;
                    if (this.info != null)
                    {
                        this.info.m_netAI.GetElevationLimits(out this.min_height, out this.max_height);
                        min_height = Mathf.RoundToInt(min_height * 12 / terrainStep);
                        max_height = Mathf.RoundToInt(max_height * 12 / terrainStep);
                        if (this.max_height > this.min_height)
                        {
                            this.elevation = Mathf.Clamp(Mathf.Clamp(this.f_this.m_elevation, this.min_height, this.max_height) + this.delta, this.min_height, this.max_height);
                            if (this.elevation != this.f_this.m_elevation)
                            {
                                this.f_this.m_elevation = this.elevation;
                                this.result = true;
                            }
                        }
                    }
                    this.Scurrent = this.result;
                    this.SPC = 1;
                    return true;

                case 1:
                    this.SPC = -1;
                    break;
            }
            return false;
        }

        [DebuggerHidden]
        public void Reset()
        {
            throw new NotSupportedException();
        }

        bool IEnumerator<bool>.Current
        {
            [DebuggerHidden]
            get
            {
                return this.Scurrent;
            }
        }

        object IEnumerator.Current
        {
            [DebuggerHidden]
            get
            {
                return this.Scurrent;
            }
        }
    }

    [CompilerGenerated]
    private sealed class TCreateFailedTc__Iterator60 : IEnumerator, IDisposable, IEnumerator<object>
    {
        internal object Scurrent;
        internal int SPC;
        internal NetToolFine TTf__this;
        internal GuideController TguideControllerT__1;
        internal NetInfo TinfoT__0;

        [DebuggerHidden]
        public void Dispose()
        {
            this.SPC = -1;
        }

        public bool MoveNext()
        {
            uint num = (uint) this.SPC;
            this.SPC = -1;
            switch (num)
            {
                case 0:
                    this.TinfoT__0 = this.TTf__this.m_prefab;
                    if (this.TinfoT__0 != null)
                    {
                        if ((this.TTf__this.m_buildErrors & ToolBase.ToolErrors.NotEnoughMoney) == ToolBase.ToolErrors.None)
                        {
                            if (this.TTf__this.m_mode == NetTool.Mode.Upgrade)
                            {
                                this.TinfoT__0.m_netAI.UpgradeFailed();
                            }
                            break;
                        }
                        this.TguideControllerT__1 = Singleton<GuideManager>.instance.m_properties;
                        if (this.TguideControllerT__1 != null)
                        {
                            Singleton<GuideManager>.instance.m_notEnoughMoney.Activate(this.TguideControllerT__1.m_notEnoughMoney);
                        }
                    }
                    break;

                case 1:
                    this.SPC = -1;
                    goto Label_00CA;

                default:
                    goto Label_00CA;
            }
            this.Scurrent = 0;
            this.SPC = 1;
            return true;
        Label_00CA:
            return false;
        }

        [DebuggerHidden]
        public void Reset()
        {
            throw new NotSupportedException();
        }

        object IEnumerator<object>.Current
        {
            [DebuggerHidden]
            get
            {
                return this.Scurrent;
            }
        }

        object IEnumerator.Current
        {
            [DebuggerHidden]
            get
            {
                return this.Scurrent;
            }
        }
    }

    [CompilerGenerated]
    private sealed class TCreateNodeTc__Iterator5F : IEnumerator, IDisposable, IEnumerator<bool>
    {
        internal bool Scurrent;
        internal int SPC;
        internal bool TSTswitchDirection;
        internal NetToolFine TTf__this;
        internal bool switchDirection;

        [DebuggerHidden]
        public void Dispose()
        {
            this.SPC = -1;
        }

        public bool MoveNext()
        {
            uint num = (uint) this.SPC;
            this.SPC = -1;
            switch (num)
            {
                case 0:
                    this.Scurrent = this.TTf__this.CreateNodeImpl(this.switchDirection);
                    this.SPC = 1;
                    return true;

                case 1:
                    this.SPC = -1;
                    break;
            }
            return false;
        }

        [DebuggerHidden]
        public void Reset()
        {
            throw new NotSupportedException();
        }

        bool IEnumerator<bool>.Current
        {
            [DebuggerHidden]
            get
            {
                return this.Scurrent;
            }
        }

        object IEnumerator.Current
        {
            [DebuggerHidden]
            get
            {
                return this.Scurrent;
            }
        }
    }


}

