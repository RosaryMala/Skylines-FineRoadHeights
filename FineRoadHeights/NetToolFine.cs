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


public static class NetAIExtension
{
    /// <summary>
    /// Overrides the behavior of the GetInfo generic function of all NetAI derived classes, optionally returning only bridges, for example.
    /// </summary>
    /// <param name="AI"></param>
    /// <param name="minElevation"></param>
    /// <param name="maxElevation"></param>
    /// <param name="length"></param>
    /// <param name="incoming"></param>
    /// <param name="outgoing"></param>
    /// <param name="curved"></param>
    /// <param name="enableDouble"></param>
    /// <param name="errors"></param>
    /// <returns></returns>
    public static NetInfo GetOverriddenInfo(this NetAI AI, float minElevation, float maxElevation, float length, bool incoming, bool outgoing, bool curved, bool enableDouble, ref ToolBase.ToolErrors errors)
    {
        RoadAI roadAI = AI as RoadAI;
        if (roadAI != null)
        {
            switch (NetToolFine.currentBuildMode)
            {
                case NetToolFine.CurrentBuildMode.Normal:
                    break;
                case NetToolFine.CurrentBuildMode.Ground:
                    if (roadAI.m_info != null)
                    {
                        return roadAI.m_info;
                    }
                    break;
                case NetToolFine.CurrentBuildMode.Elevated:
                    if (roadAI.m_elevatedInfo != null)
                    {
                        return roadAI.m_elevatedInfo;
                    }
                    break;
                case NetToolFine.CurrentBuildMode.Bridge:
                    if (roadAI.m_bridgeInfo != null)
                    {
                        return roadAI.m_bridgeInfo;
                    }
                    break;
                default:
                    break;
            }
            return roadAI.GetInfo(minElevation, maxElevation, length, incoming, outgoing, curved, enableDouble, ref errors);
        }
        TrainTrackAI trainTrackAI = AI as TrainTrackAI;
        if (trainTrackAI != null)
        {
            switch (NetToolFine.currentBuildMode)
            {
                case NetToolFine.CurrentBuildMode.Normal:
                    break;
                case NetToolFine.CurrentBuildMode.Ground:
                    if (trainTrackAI.m_info != null)
                    {
                        return trainTrackAI.m_info;
                    }
                    break;
                case NetToolFine.CurrentBuildMode.Elevated:
                    if (trainTrackAI.m_elevatedInfo != null)
                    {
                        return trainTrackAI.m_elevatedInfo;
                    }
                    break;
                case NetToolFine.CurrentBuildMode.Bridge:
                    if (trainTrackAI.m_bridgeInfo != null)
                    {
                        return trainTrackAI.m_bridgeInfo;
                    }
                    break;
                default:
                    break;
            }
            return trainTrackAI.GetInfo(minElevation, maxElevation, length, incoming, outgoing, curved, enableDouble, ref errors);
        }
        return AI.GetInfo(minElevation, maxElevation, length, incoming, outgoing, curved, enableDouble, ref errors);
    }

}


public class NetToolFine : ToolBase
{
    public enum CurrentBuildMode
    {
        Normal,
        Ground,
        Elevated,
        Bridge
    }
    public static CurrentBuildMode currentBuildMode { get; private set; }
    private static float m_terrainStep = 3.0f;
    public static float TerrainStep
    {
        get
        {
            return m_terrainStep;
        }
        set
        {
            if (value < 1.0f)
                m_terrainStep = 1.0f;
            else
                m_terrainStep = value;
        }
    }
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
    //[NonSerialized]
    //public static FastList<NetTool.NodePosition> m_nodePositionsMain = new FastList<NetTool.NodePosition>();
    //[NonSerialized]
    //public static FastList<NetTool.NodePosition> m_nodePositionsSimulation = new FastList<NetTool.NodePosition>();
    public CursorInfo m_placementCursor;
    public NetInfo m_prefab;
    private int m_productionRate;
    public bool m_snap = true;
    private bool m_switchingDir;
    private FastList<ushort> m_tempUpgraded;
    public CursorInfo m_upgradeCursor;
    private HashSet<ushort> m_upgradedSegments;
    private bool m_upgrading;

    string StatusText
    {
        get
        {
            string status = m_elevation * TerrainStep + " m, ";
            status += currentBuildMode + " construction";
            return status;
        }
    }

    /// <summary>
    /// Asjusts the prefab elevation limits to take into consideration the elevation step.
    /// </summary>
    /// <param name="netAI">Prefab to get the limits from.</param>
    /// <param name="min"></param>
    /// <param name="max"></param>
    static void GetAdjustedElevationLimits(NetAI netAI, out int min, out int max)
    {
        netAI.GetElevationLimits(out min, out max);
        min = -3;
        max = 5;
        min = Mathf.RoundToInt(min * 12 / TerrainStep);
        max = Mathf.RoundToInt(max * 12 / TerrainStep);
        //min = Mathf.Min(min, -4); //this would make below ground level roads possible, but it glitches like hell.
    }


    //Unmodified
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
        currentBuildMode = CurrentBuildMode.Normal;
    }

    //Unmodified
    private static bool CanAddNode(ushort segmentID, ushort nodeID, Vector3 position, Vector3 direction)
    {
        NetNode netNode = Singleton<NetManager>.instance.m_nodes.m_buffer[(int)nodeID];
        if ((netNode.m_flags & NetNode.Flags.Double) != NetNode.Flags.None)
        {
            return false;
        }
        if ((netNode.m_flags & (NetNode.Flags.Moveable | NetNode.Flags.Untouchable)) == NetNode.Flags.Moveable)
        {
            return true;
        }
        NetInfo info = Singleton<NetManager>.instance.m_segments.m_buffer[(int)segmentID].Info;
        NetInfo info2 = netNode.Info;
        if (!info.m_netAI.CanModify())
        {
            return false;
        }
        float minNodeDistance = info2.GetMinNodeDistance();
        Vector2 vector = new Vector2(netNode.m_position.x - position.x, netNode.m_position.z - position.z);
        float magnitude = vector.magnitude;
        return magnitude >= minNodeDistance;
    }

    //Unmodified
    private static bool CanAddNode(ushort segmentID, Vector3 position, Vector3 direction, bool checkDirection, ulong[] collidingSegmentBuffer)
    {
        bool flag = true;
        NetSegment netSegment = Singleton<NetManager>.instance.m_segments.m_buffer[(int)segmentID];
        if ((netSegment.m_flags & NetSegment.Flags.Untouchable) != NetSegment.Flags.None)
        {
            flag = false;
        }
        if (checkDirection)
        {
            Vector3 vector;
            Vector3 vector2;
            netSegment.GetClosestPositionAndDirection(position, out vector, out vector2);
            float num = direction.x * vector2.x + direction.z * vector2.z;
            if (num > 0.75f || num < -0.75f)
            {
                flag = false;
            }
        }
        if (!CanAddNode(segmentID, netSegment.m_startNode, position, direction))
        {
            flag = false;
        }
        if (!CanAddNode(segmentID, netSegment.m_endNode, position, direction))
        {
            flag = false;
        }
        if (!flag && collidingSegmentBuffer != null)
        {
            collidingSegmentBuffer[segmentID >> 6] |= 1uL << (int)segmentID;
        }
        return flag;
    }

    //Unmodified
    private static bool CanAddSegment(ushort nodeID, Vector3 direction, ulong[] collidingSegmentBuffer, ushort ignoreSegment)
    {
        NetNode netNode = Singleton<NetManager>.instance.m_nodes.m_buffer[(int)nodeID];
        bool flag = (netNode.m_flags & NetNode.Flags.Double) != NetNode.Flags.None && ignoreSegment == 0;
        bool result = true;
        if ((netNode.m_flags & (NetNode.Flags.Middle | NetNode.Flags.Untouchable)) == (NetNode.Flags.Middle | NetNode.Flags.Untouchable) && netNode.CountSegments(NetSegment.Flags.Untouchable, ignoreSegment) >= 2)
        {
            flag = true;
        }
        for (int i = 0; i < 8; i++)
        {
            ushort segment = netNode.GetSegment(i);
            if (segment != 0 && segment != ignoreSegment)
            {
                NetSegment netSegment = Singleton<NetManager>.instance.m_segments.m_buffer[(int)segment];
                Vector3 vector = (nodeID != netSegment.m_startNode) ? netSegment.m_endDirection : netSegment.m_startDirection;
                float num = direction.x * vector.x + direction.z * vector.z;
                if (flag || num > 0.75f)
                {
                    if (collidingSegmentBuffer != null)
                    {
                        collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
                    }
                    result = false;
                }
            }
        }
        return result;
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

    //Unmodified
    private static ToolBase.ToolErrors CanCreateSegment(NetInfo segmentInfo, ushort startNode, ushort upgrading, Vector3 endPos, Vector3 startDir, Vector3 endDir, ulong[] collidingSegmentBuffer)
    {
        ToolBase.ToolErrors toolErrors = ToolBase.ToolErrors.None;
        NetManager instance = Singleton<NetManager>.instance;
        for (int i = 0; i < 8; i++)
        {
            ushort segment = instance.m_nodes.m_buffer[(int)startNode].GetSegment(i);
            if (segment != 0 && segment != upgrading)
            {
                NetInfo info = instance.m_segments.m_buffer[(int)segment].Info;
                bool flag = instance.m_segments.m_buffer[(int)segment].m_startNode == startNode;
                ushort num = (!flag) ? instance.m_segments.m_buffer[(int)segment].m_endNode : instance.m_segments.m_buffer[(int)segment].m_startNode;
                ushort num2 = (!flag) ? instance.m_segments.m_buffer[(int)segment].m_startNode : instance.m_segments.m_buffer[(int)segment].m_endNode;
                Vector3 position = instance.m_nodes.m_buffer[(int)num].m_position;
                Vector3 position2 = instance.m_nodes.m_buffer[(int)num2].m_position;
                Vector3 vector = (!flag) ? instance.m_segments.m_buffer[(int)segment].m_endDirection : instance.m_segments.m_buffer[(int)segment].m_startDirection;
                Vector3 vector2 = (!flag) ? instance.m_segments.m_buffer[(int)segment].m_startDirection : instance.m_segments.m_buffer[(int)segment].m_endDirection;
                Vector3 b;
                Vector3 vector3;
                bool flag2;
                NetSegment.CalculateCorner(info, position, position2, vector, vector2, segmentInfo, endPos, startDir, endDir, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, num, false, true, out b, out vector3, out flag2);
                Vector3 b2;
                Vector3 vector4;
                NetSegment.CalculateCorner(info, position, position2, vector, vector2, segmentInfo, endPos, startDir, endDir, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, num, false, false, out b2, out vector4, out flag2);
                Vector3 a;
                Vector3 vector5;
                bool flag3;
                NetSegment.CalculateCorner(info, position2, position, vector2, vector, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, num2, false, false, out a, out vector5, out flag3);
                Vector3 a2;
                Vector3 vector6;
                NetSegment.CalculateCorner(info, position2, position, vector2, vector, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, num2, false, true, out a2, out vector6, out flag3);
                if ((a.x - b.x) * vector.x + (a.z - b.z) * vector.z < 2f)
                {
                    collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
                    toolErrors |= ToolBase.ToolErrors.ObjectCollision;
                }
                if ((b.x - a.x) * vector2.x + (b.z - a.z) * vector2.z < 2f)
                {
                    collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
                    toolErrors |= ToolBase.ToolErrors.ObjectCollision;
                }
                if ((a2.x - b2.x) * vector.x + (a2.z - b2.z) * vector.z < 2f)
                {
                    collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
                    toolErrors |= ToolBase.ToolErrors.ObjectCollision;
                }
                if ((b2.x - a2.x) * vector2.x + (b2.z - a2.z) * vector2.z < 2f)
                {
                    collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
                    toolErrors |= ToolBase.ToolErrors.ObjectCollision;
                }
                if (VectorUtils.LengthSqrXZ(a - b) * info.m_maxSlope * info.m_maxSlope * 4f < (a.y - b.y) * (a.y - b.y))
                {
                    collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
                    toolErrors |= ToolBase.ToolErrors.SlopeTooSteep;
                }
                if (VectorUtils.LengthSqrXZ(a2 - b2) * info.m_maxSlope * info.m_maxSlope * 4f < (a2.y - b2.y) * (a2.y - b2.y))
                {
                    collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
                    toolErrors |= ToolBase.ToolErrors.SlopeTooSteep;
                }
            }
        }
        return toolErrors;
    }
    //Unmodified
    private static ToolBase.ToolErrors CanCreateSegment(ushort segment, ushort endNode, ushort otherNode, NetInfo info1, Vector3 startPos, Vector3 endPos, Vector3 startDir, Vector3 endDir, NetInfo info2, Vector3 endPos2, Vector3 startDir2, Vector3 endDir2, ulong[] collidingSegmentBuffer)
    {
        ToolBase.ToolErrors toolErrors = ToolBase.ToolErrors.None;
        NetManager instance = Singleton<NetManager>.instance;
        bool flag = true;
        if ((instance.m_nodes.m_buffer[(int)endNode].m_flags & (NetNode.Flags.Middle | NetNode.Flags.Moveable | NetNode.Flags.Untouchable)) == (NetNode.Flags.Middle | NetNode.Flags.Moveable))
        {
            for (int i = 0; i < 8; i++)
            {
                ushort segment2 = instance.m_nodes.m_buffer[(int)endNode].GetSegment(i);
                if (segment2 != 0 && segment2 != segment)
                {
                    segment = segment2;
                    info1 = instance.m_segments.m_buffer[(int)segment].Info;
                    ushort num;
                    if (instance.m_segments.m_buffer[(int)segment].m_startNode == endNode)
                    {
                        num = instance.m_segments.m_buffer[(int)segment].m_endNode;
                        endDir = instance.m_segments.m_buffer[(int)segment].m_endDirection;
                    }
                    else
                    {
                        num = instance.m_segments.m_buffer[(int)segment].m_startNode;
                        endDir = instance.m_segments.m_buffer[(int)segment].m_startDirection;
                    }
                    if (num == otherNode)
                    {
                        flag = false;
                    }
                    else
                    {
                        endNode = num;
                        endPos = instance.m_nodes.m_buffer[(int)endNode].m_position;
                    }
                    break;
                }
            }
        }
        if (flag && (instance.m_nodes.m_buffer[(int)endNode].m_flags & (NetNode.Flags.Middle | NetNode.Flags.Moveable | NetNode.Flags.Untouchable)) == (NetNode.Flags.Middle | NetNode.Flags.Moveable))
        {
            return toolErrors;
        }
        Vector3 b;
        Vector3 vector;
        bool flag2;
        NetSegment.CalculateCorner(info1, startPos, endPos, startDir, endDir, info2, endPos2, startDir2, endDir2, null, Vector3.zero, Vector3.zero, Vector3.zero, 0, 0, false, true, out b, out vector, out flag2);
        Vector3 b2;
        Vector3 vector2;
        NetSegment.CalculateCorner(info1, startPos, endPos, startDir, endDir, info2, endPos2, startDir2, endDir2, null, Vector3.zero, Vector3.zero, Vector3.zero, 0, 0, false, false, out b2, out vector2, out flag2);
        Vector3 a;
        Vector3 vector3;
        bool flag3;
        NetSegment.CalculateCorner(info1, endPos, startPos, endDir, startDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, endNode, false, false, out a, out vector3, out flag3);
        Vector3 a2;
        Vector3 vector4;
        NetSegment.CalculateCorner(info1, endPos, startPos, endDir, startDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, segment, endNode, false, true, out a2, out vector4, out flag3);
        if ((a.x - b.x) * startDir.x + (a.z - b.z) * startDir.z < 2f)
        {
            collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
            toolErrors |= ToolBase.ToolErrors.ObjectCollision;
        }
        if ((b.x - a.x) * endDir.x + (b.z - a.z) * endDir.z < 2f)
        {
            collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
            toolErrors |= ToolBase.ToolErrors.ObjectCollision;
        }
        if ((a2.x - b2.x) * startDir.x + (a2.z - b2.z) * startDir.z < 2f)
        {
            collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
            toolErrors |= ToolBase.ToolErrors.ObjectCollision;
        }
        if ((b2.x - a2.x) * endDir.x + (b2.z - a2.z) * endDir.z < 2f)
        {
            collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
            toolErrors |= ToolBase.ToolErrors.ObjectCollision;
        }
        if (VectorUtils.LengthSqrXZ(a - b) * info1.m_maxSlope * info1.m_maxSlope * 4f < (a.y - b.y) * (a.y - b.y))
        {
            collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
            toolErrors |= ToolBase.ToolErrors.SlopeTooSteep;
        }
        if (VectorUtils.LengthSqrXZ(a2 - b2) * info1.m_maxSlope * info1.m_maxSlope * 4f < (a2.y - b2.y) * (a2.y - b2.y))
        {
            collidingSegmentBuffer[segment >> 6] |= 1uL << (int)segment;
            toolErrors |= ToolBase.ToolErrors.SlopeTooSteep;
        }
        return toolErrors;
    }
    //Unmodified
    private static ToolBase.ToolErrors CanCreateSegment(NetInfo segmentInfo, ushort startNode, ushort startSegment, ushort endNode, ushort endSegment, ushort upgrading, Vector3 startPos, Vector3 endPos, Vector3 startDir, Vector3 endDir, ulong[] collidingSegmentBuffer)
    {
        ToolBase.ToolErrors toolErrors = ToolBase.ToolErrors.None;
        NetManager instance = Singleton<NetManager>.instance;
        Vector3 b;
        Vector3 b2;
        if (startSegment != 0 && startNode == 0)
        {
            NetInfo info = instance.m_segments.m_buffer[(int)startSegment].Info;
            Vector3 vector;
            Vector3 vector2;
            instance.m_segments.m_buffer[(int)startSegment].GetClosestPositionAndDirection(startPos, out vector, out vector2);
            vector2 = VectorUtils.NormalizeXZ(vector2);
            ushort startNode2 = instance.m_segments.m_buffer[(int)startSegment].m_startNode;
            ushort endNode2 = instance.m_segments.m_buffer[(int)startSegment].m_endNode;
            Vector3 position = instance.m_nodes.m_buffer[(int)startNode2].m_position;
            Vector3 position2 = instance.m_nodes.m_buffer[(int)endNode2].m_position;
            Vector3 startDirection = instance.m_segments.m_buffer[(int)startSegment].m_startDirection;
            Vector3 endDirection = instance.m_segments.m_buffer[(int)startSegment].m_endDirection;
            Vector3 vector3;
            bool flag;
            NetSegment.CalculateCorner(segmentInfo, startPos, endPos, startDir, endDir, info, position, -vector2, startDirection, info, position2, vector2, endDirection, 0, 0, false, true, out b, out vector3, out flag);
            Vector3 vector4;
            NetSegment.CalculateCorner(segmentInfo, startPos, endPos, startDir, endDir, info, position, -vector2, startDirection, info, position2, vector2, endDirection, 0, 0, false, false, out b2, out vector4, out flag);
            toolErrors |= CanCreateSegment(startSegment, startNode2, endNode, info, startPos, position, -vector2, startDirection, segmentInfo, endPos, startDir, endDir, collidingSegmentBuffer);
            toolErrors |= CanCreateSegment(startSegment, endNode2, endNode, info, startPos, position2, vector2, endDirection, segmentInfo, endPos, startDir, endDir, collidingSegmentBuffer);
        }
        else
        {
            Vector3 vector3;
            bool flag;
            NetSegment.CalculateCorner(segmentInfo, startPos, endPos, startDir, endDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, upgrading, startNode, false, true, out b, out vector3, out flag);
            Vector3 vector4;
            NetSegment.CalculateCorner(segmentInfo, startPos, endPos, startDir, endDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, upgrading, startNode, false, false, out b2, out vector4, out flag);
            if (startNode != 0)
            {
                toolErrors |= CanCreateSegment(segmentInfo, startNode, upgrading, endPos, startDir, endDir, collidingSegmentBuffer);
            }
        }
        Vector3 a;
        Vector3 a2;
        if (endSegment != 0 && endNode == 0)
        {
            NetInfo info2 = instance.m_segments.m_buffer[(int)endSegment].Info;
            Vector3 vector5;
            Vector3 vector6;
            instance.m_segments.m_buffer[(int)endSegment].GetClosestPositionAndDirection(startPos, out vector5, out vector6);
            vector6 = VectorUtils.NormalizeXZ(vector6);
            ushort startNode3 = instance.m_segments.m_buffer[(int)endSegment].m_startNode;
            ushort endNode3 = instance.m_segments.m_buffer[(int)endSegment].m_endNode;
            Vector3 position3 = instance.m_nodes.m_buffer[(int)startNode3].m_position;
            Vector3 position4 = instance.m_nodes.m_buffer[(int)endNode3].m_position;
            Vector3 startDirection2 = instance.m_segments.m_buffer[(int)endSegment].m_startDirection;
            Vector3 endDirection2 = instance.m_segments.m_buffer[(int)endSegment].m_endDirection;
            Vector3 vector7;
            bool flag2;
            NetSegment.CalculateCorner(segmentInfo, endPos, startPos, endDir, startDir, info2, position3, -vector6, startDirection2, info2, position4, vector6, endDirection2, 0, 0, false, false, out a, out vector7, out flag2);
            Vector3 vector8;
            NetSegment.CalculateCorner(segmentInfo, endPos, startPos, endDir, startDir, info2, position3, -vector6, startDirection2, info2, position4, vector6, endDirection2, 0, 0, false, true, out a2, out vector8, out flag2);
            toolErrors |= CanCreateSegment(endSegment, startNode3, startNode, info2, endPos, position3, -vector6, startDirection2, segmentInfo, startPos, endDir, startDir, collidingSegmentBuffer);
            toolErrors |= CanCreateSegment(endSegment, endNode3, startNode, info2, endPos, position4, vector6, endDirection2, segmentInfo, startPos, endDir, startDir, collidingSegmentBuffer);
        }
        else
        {
            Vector3 vector7;
            bool flag2;
            NetSegment.CalculateCorner(segmentInfo, endPos, startPos, endDir, startDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, upgrading, endNode, false, false, out a, out vector7, out flag2);
            Vector3 vector8;
            NetSegment.CalculateCorner(segmentInfo, endPos, startPos, endDir, startDir, null, Vector3.zero, Vector3.zero, Vector3.zero, null, Vector3.zero, Vector3.zero, Vector3.zero, upgrading, endNode, false, true, out a2, out vector8, out flag2);
            if (endNode != 0)
            {
                toolErrors |= CanCreateSegment(segmentInfo, endNode, upgrading, startPos, endDir, startDir, collidingSegmentBuffer);
            }
        }
        if ((a.x - b.x) * startDir.x + (a.z - b.z) * startDir.z < 2f)
        {
            toolErrors |= ToolBase.ToolErrors.TooShort;
        }
        if ((b.x - a.x) * endDir.x + (b.z - a.z) * endDir.z < 2f)
        {
            toolErrors |= ToolBase.ToolErrors.TooShort;
        }
        if ((a2.x - b2.x) * startDir.x + (a2.z - b2.z) * startDir.z < 2f)
        {
            toolErrors |= ToolBase.ToolErrors.TooShort;
        }
        if ((b2.x - a2.x) * endDir.x + (b2.z - a2.z) * endDir.z < 2f)
        {
            toolErrors |= ToolBase.ToolErrors.TooShort;
        }
        if (VectorUtils.LengthSqrXZ(a - b) * segmentInfo.m_maxSlope * segmentInfo.m_maxSlope * 4f < (a.y - b.y) * (a.y - b.y))
        {
            toolErrors |= ToolBase.ToolErrors.SlopeTooSteep;
        }
        if (VectorUtils.LengthSqrXZ(a2 - b2) * segmentInfo.m_maxSlope * segmentInfo.m_maxSlope * 4f < (a2.y - b2.y) * (a2.y - b2.y))
        {
            toolErrors |= ToolBase.ToolErrors.SlopeTooSteep;
        }
        return toolErrors;
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
                if ((segment != 0) && ((segmentMask[segment >> 6] & (((ulong)1L) << segment)) == 0))
                {
                    return;
                }
            }
            buildingMask[building >> 6] &= (ulong)~(((long)1L) << building);
        }
    }

    private static ToolBase.ToolErrors CheckNodeHeights(NetInfo info, FastList<NetTool.NodePosition> nodeBuffer)
    {
        bool flag = info.m_netAI.BuildUnderground();
        bool flag2 = info.m_netAI.SupportUnderground();
        int num = 0;
        while (true)
        {
            bool flag3 = false;
            for (int i = 1; i < nodeBuffer.m_size; i++)
            {
                NetTool.NodePosition nodePosition = nodeBuffer.m_buffer[i - 1];
                NetTool.NodePosition nodePosition2 = nodeBuffer.m_buffer[i];
                float num2 = VectorUtils.LengthXZ(nodePosition2.m_position - nodePosition.m_position);
                float num3 = num2 * info.m_maxSlope;
                nodePosition2.m_minY = Mathf.Max(nodePosition2.m_minY, nodePosition.m_minY - num3);
                nodePosition2.m_maxY = Mathf.Min(nodePosition2.m_maxY, nodePosition.m_maxY + num3);
                nodeBuffer.m_buffer[i] = nodePosition2;
            }
            for (int j = nodeBuffer.m_size - 2; j >= 0; j--)
            {
                NetTool.NodePosition nodePosition3 = nodeBuffer.m_buffer[j + 1];
                NetTool.NodePosition nodePosition4 = nodeBuffer.m_buffer[j];
                float num4 = VectorUtils.LengthXZ(nodePosition4.m_position - nodePosition3.m_position);
                float num5 = num4 * info.m_maxSlope;
                nodePosition4.m_minY = Mathf.Max(nodePosition4.m_minY, nodePosition3.m_minY - num5);
                nodePosition4.m_maxY = Mathf.Min(nodePosition4.m_maxY, nodePosition3.m_maxY + num5);
                nodeBuffer.m_buffer[j] = nodePosition4;
            }
            for (int k = 0; k < nodeBuffer.m_size; k++)
            {
                NetTool.NodePosition nodePosition5 = nodeBuffer.m_buffer[k];
                if (nodePosition5.m_minY > nodePosition5.m_maxY)
                {
                    return ToolBase.ToolErrors.SlopeTooSteep;
                }
                if (nodePosition5.m_position.y > nodePosition5.m_maxY)
                {
                    nodePosition5.m_position.y = nodePosition5.m_maxY;
                    if (!flag && nodePosition5.m_elevation >= -8f)
                    {
                        nodePosition5.m_minY = nodePosition5.m_maxY;
                    }
                    flag3 = true;
                }
                else if (nodePosition5.m_position.y < nodePosition5.m_minY)
                {
                    nodePosition5.m_position.y = nodePosition5.m_minY;
                    if (flag || nodePosition5.m_elevation < -8f)
                    {
                        nodePosition5.m_maxY = nodePosition5.m_minY;
                    }
                    flag3 = true;
                }
                nodeBuffer.m_buffer[k] = nodePosition5;
            }
            if (num++ == nodeBuffer.m_size << 1)
            {
                return ToolBase.ToolErrors.SlopeTooSteep;
            }
            if (!flag3)
            {
                goto Block_11;
            }
        }
        return ToolBase.ToolErrors.SlopeTooSteep;
    Block_11:
        for (int l = 1; l < nodeBuffer.m_size - 1; l++)
        {
            NetTool.NodePosition nodePosition6 = nodeBuffer.m_buffer[l - 1];
            NetTool.NodePosition nodePosition7 = nodeBuffer.m_buffer[l];
            float num6 = VectorUtils.LengthXZ(nodePosition7.m_position - nodePosition6.m_position);
            float num7 = num6 * info.m_maxSlope;
            if (flag || nodePosition7.m_elevation < -8f)
            {
                if (nodePosition7.m_position.y > nodePosition6.m_position.y + num7)
                {
                    nodePosition7.m_position.y = nodePosition6.m_position.y + num7;
                }
            }
            else if (nodePosition7.m_position.y < nodePosition6.m_position.y - num7)
            {
                nodePosition7.m_position.y = nodePosition6.m_position.y - num7;
            }
            nodeBuffer.m_buffer[l] = nodePosition7;
        }
        for (int m = nodeBuffer.m_size - 2; m > 0; m--)
        {
            NetTool.NodePosition nodePosition8 = nodeBuffer.m_buffer[m + 1];
            NetTool.NodePosition nodePosition9 = nodeBuffer.m_buffer[m];
            float num8 = VectorUtils.LengthXZ(nodePosition9.m_position - nodePosition8.m_position);
            float num9 = num8 * info.m_maxSlope;
            if (flag || nodePosition9.m_elevation < -8f)
            {
                if (nodePosition9.m_position.y > nodePosition8.m_position.y + num9)
                {
                    nodePosition9.m_position.y = nodePosition8.m_position.y + num9;
                }
            }
            else if (nodePosition9.m_position.y < nodePosition8.m_position.y - num9)
            {
                nodePosition9.m_position.y = nodePosition8.m_position.y - num9;
            }
            nodeBuffer.m_buffer[m] = nodePosition9;
        }
        int num10;
        int num11;
        info.m_netAI.GetElevationLimits(out num10, out num11);
        if (num11 > num10 && !flag)
        {
            int num12;
            for (int n = 0; n < nodeBuffer.m_size - 1; n = num12)
            {
                NetTool.NodePosition nodePosition10 = nodeBuffer.m_buffer[n];
                num12 = n + 1;
                float num13 = 0f;
                bool flag4 = nodeBuffer.m_buffer[num12].m_position.y >= nodeBuffer.m_buffer[num12].m_terrainHeight + 8f;
                bool flag5 = nodeBuffer.m_buffer[num12].m_position.y <= nodeBuffer.m_buffer[num12].m_terrainHeight - 8f;
                if (!flag2)
                {
                    flag5 = false;
                }
                if (flag4 || flag5)
                {
                    while (num12 < nodeBuffer.m_size)
                    {
                        NetTool.NodePosition nodePosition11 = nodeBuffer.m_buffer[num12];
                        num13 += VectorUtils.LengthXZ(nodePosition11.m_position - nodePosition10.m_position);
                        if (flag4 && nodePosition11.m_position.y < nodePosition11.m_terrainHeight + 8f)
                        {
                            break;
                        }
                        if (flag5 && nodePosition11.m_position.y > nodePosition11.m_terrainHeight - 8f)
                        {
                            break;
                        }
                        nodePosition10 = nodePosition11;
                        if (num12 == nodeBuffer.m_size - 1)
                        {
                            break;
                        }
                        num12++;
                    }
                }
                float y = nodeBuffer.m_buffer[n].m_position.y;
                float y2 = nodeBuffer.m_buffer[num12].m_position.y;
                nodePosition10 = nodeBuffer.m_buffer[n];
                float num14 = 0f;
                num13 = Mathf.Max(1f, num13);
                for (int num15 = n + 1; num15 < num12; num15++)
                {
                    NetTool.NodePosition nodePosition12 = nodeBuffer.m_buffer[num15];
                    num14 += VectorUtils.LengthXZ(nodePosition12.m_position - nodePosition10.m_position);
                    if (flag5)
                    {
                        nodePosition12.m_position.y = Mathf.Min(nodePosition12.m_position.y, Mathf.Lerp(y, y2, num14 / num13));
                    }
                    else
                    {
                        nodePosition12.m_position.y = Mathf.Max(nodePosition12.m_position.y, Mathf.Lerp(y, y2, num14 / num13));
                    }
                    nodeBuffer.m_buffer[num15] = nodePosition12;
                    nodePosition10 = nodePosition12;
                }
            }
        }
        ToolBase.ToolErrors toolErrors = ToolBase.ToolErrors.None;
        for (int num16 = 1; num16 < nodeBuffer.m_size - 1; num16++)
        {
            NetTool.NodePosition nodePosition13 = nodeBuffer.m_buffer[num16 - 1];
            NetTool.NodePosition nodePosition14 = nodeBuffer.m_buffer[num16 + 1];
            NetTool.NodePosition nodePosition15 = nodeBuffer.m_buffer[num16];
            if (flag)
            {
                if (nodePosition15.m_terrainHeight < nodePosition15.m_position.y)
                {
                    toolErrors |= ToolBase.ToolErrors.SlopeTooSteep;
                }
            }
            else if (nodePosition15.m_elevation < -8f)
            {
                if (nodePosition15.m_terrainHeight <= nodePosition15.m_position.y + 8f)
                {
                    toolErrors |= ToolBase.ToolErrors.SlopeTooSteep;
                }
            }
            else if (!flag2 && nodePosition15.m_terrainHeight > nodePosition15.m_position.y + 8f)
            {
                toolErrors |= ToolBase.ToolErrors.SlopeTooSteep;
            }
            nodePosition15.m_direction.y = VectorUtils.NormalizeXZ(nodePosition14.m_position - nodePosition13.m_position).y;
            nodeBuffer.m_buffer[num16] = nodePosition15;
        }
        return toolErrors;
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
                    collidingSegmentBuffer[startSegment >> 6] |= ((ulong)1L) << startSegment;
                    collidingSegmentBuffer[endSegment >> 6] |= ((ulong)1L) << endSegment;
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
                    collidingSegmentBuffer[startSegment >> 6] |= ((ulong)1L) << startSegment;
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
                    collidingSegmentBuffer[endSegment >> 6] |= ((ulong)1L) << endSegment;
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
                            collidingSegmentBuffer[segment >> 6] |= ((ulong)1L) << segment;
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

    //Unmodified
    public static ToolBase.ToolErrors CreateNode(NetInfo info, NetTool.ControlPoint startPoint, NetTool.ControlPoint middlePoint, NetTool.ControlPoint endPoint, FastList<NetTool.NodePosition> nodeBuffer, int maxSegments, bool test, bool visualize, bool autoFix, bool needMoney, bool invert, bool switchDir, ushort relocateBuildingID, out ushort node, out ushort segment, out int cost, out int productionRate)
    {
        ushort num;
        ushort num2;
        ToolBase.ToolErrors toolErrors = CreateNode(info, startPoint, middlePoint, endPoint, nodeBuffer, maxSegments, test, visualize, autoFix, needMoney, invert, switchDir, relocateBuildingID, out num, out num2, out segment, out cost, out productionRate);
        if (toolErrors == ToolBase.ToolErrors.None)
        {
            if (num2 != 0)
            {
                node = num2;
            }
            else
            {
                node = num;
            }
        }
        else
        {
            node = 0;
        }
        return toolErrors;
    }

    //Modified
    public static ToolBase.ToolErrors CreateNode(NetInfo info, NetTool.ControlPoint startPoint, NetTool.ControlPoint middlePoint, NetTool.ControlPoint endPoint, FastList<NetTool.NodePosition> nodeBuffer, int maxSegments, bool test, bool visualize, bool autoFix, bool needMoney, bool invert, bool switchDir, ushort relocateBuildingID, out ushort firstNode, out ushort lastNode, out ushort segment, out int cost, out int productionRate)
    {
        ushort midleSegment = middlePoint.m_segment;
        NetInfo oldInfo = null;
        if (startPoint.m_segment == midleSegment || endPoint.m_segment == midleSegment)
        {
            midleSegment = 0;
        }
        uint currentBuildIndex = Singleton<SimulationManager>.instance.m_currentBuildIndex;
        bool flag = invert;
        bool smoothStart = true;
        bool smoothEnd = true;
        bool enableDouble = false;
        if (midleSegment != 0)
        {
            enableDouble = (DefaultTool.FindSecondarySegment(midleSegment) != 0);
            maxSegments = Mathf.Min(1, maxSegments);
            cost = -Singleton<NetManager>.instance.m_segments.m_buffer[(int)midleSegment].Info.m_netAI.GetConstructionCost(startPoint.m_position, endPoint.m_position, startPoint.m_elevation, endPoint.m_elevation);
            currentBuildIndex = Singleton<NetManager>.instance.m_segments.m_buffer[(int)midleSegment].m_buildIndex;
            smoothStart = ((Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startPoint.m_node].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None);
            smoothEnd = ((Singleton<NetManager>.instance.m_nodes.m_buffer[(int)endPoint.m_node].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None);
            autoFix = false;
            if (switchDir)
            {
                flag = !flag;
                info = Singleton<NetManager>.instance.m_segments.m_buffer[(int)midleSegment].Info;
            }
            if (!test && !visualize)
            {
                if ((Singleton<NetManager>.instance.m_segments.m_buffer[(int)midleSegment].m_flags & NetSegment.Flags.Invert) != NetSegment.Flags.None)
                {
                    flag = !flag;
                }
                oldInfo = Singleton<NetManager>.instance.m_segments.m_buffer[(int)midleSegment].Info;
                Singleton<NetManager>.instance.ReleaseSegment(midleSegment, true);
                midleSegment = 0;
            }
        }
        else
        {
            if (autoFix && Singleton<SimulationManager>.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.True)
            {
                flag = !flag;
            }
            cost = 0;
        }
        ToolController properties = Singleton<ToolManager>.instance.m_properties;
        ulong[] numArray = null;
        ulong[] numArray2 = null;
        ToolBase.ToolErrors toolErrors = ToolBase.ToolErrors.None;
        if (test || !visualize)
        {
            properties.BeginColliding(out numArray, out numArray2);
        }
        ToolBase.ToolErrors result;
        try
        {
            ushort num2 = 0;
            BuildingInfo buildingInfo;
            Vector3 zero;
            Vector3 forward;
            if (midleSegment != 0 && switchDir)
            {
                buildingInfo = null;
                zero = Vector3.zero;
                forward = Vector3.forward;
                productionRate = 0;
                if (info.m_hasForwardVehicleLanes == info.m_hasBackwardVehicleLanes)
                {
                    toolErrors |= ToolBase.ToolErrors.CannotUpgrade;
                }
            }
            else
            {
                toolErrors |= info.m_netAI.CheckBuildPosition(test, visualize, false, autoFix, ref startPoint, ref middlePoint, ref endPoint, out buildingInfo, out zero, out forward, out productionRate);
            }
            if (test)
            {
                Vector3 direction = middlePoint.m_direction;
                Vector3 direction2 = -endPoint.m_direction;
                if (maxSegments != 0 && midleSegment == 0 && direction.x * direction2.x + direction.z * direction2.z >= 0.8f)
                {
                    toolErrors |= ToolBase.ToolErrors.InvalidShape;
                }
                if (maxSegments != 0 && !CheckStartAndEnd(midleSegment, startPoint.m_segment, startPoint.m_node, endPoint.m_segment, endPoint.m_node, numArray))
                {
                    toolErrors |= ToolBase.ToolErrors.ObjectCollision;
                }
                if (startPoint.m_node != 0)
                {
                    if (maxSegments != 0 && !CanAddSegment(startPoint.m_node, direction, numArray, midleSegment))
                    {
                        toolErrors |= ToolBase.ToolErrors.ObjectCollision;
                    }
                }
                else if (startPoint.m_segment != 0 && !CanAddNode(startPoint.m_segment, startPoint.m_position, direction, maxSegments != 0, numArray))
                {
                    toolErrors |= ToolBase.ToolErrors.ObjectCollision;
                }
                if (endPoint.m_node != 0)
                {
                    if (maxSegments != 0 && !CanAddSegment(endPoint.m_node, direction2, numArray, midleSegment))
                    {
                        toolErrors |= ToolBase.ToolErrors.ObjectCollision;
                    }
                }
                else if (endPoint.m_segment != 0 && !CanAddNode(endPoint.m_segment, endPoint.m_position, direction2, maxSegments != 0, numArray))
                {
                    toolErrors |= ToolBase.ToolErrors.ObjectCollision;
                }
                if (!Singleton<NetManager>.instance.CheckLimits())
                {
                    toolErrors |= ToolBase.ToolErrors.TooManyObjects;
                }
            }
            if (buildingInfo != null)
            {
                if (visualize)
                {
                    RenderNodeBuilding(buildingInfo, zero, forward);
                }
                else if (test)
                {
                    toolErrors |= TestNodeBuilding(buildingInfo, zero, forward, 0, 0, 0, test, numArray, numArray2);
                }
                else
                {
                    float angle = Mathf.Atan2(-forward.x, forward.z);
                    if (Singleton<BuildingManager>.instance.CreateBuilding(out num2, ref Singleton<SimulationManager>.instance.m_randomizer, buildingInfo, zero, angle, 0, Singleton<SimulationManager>.instance.m_currentBuildIndex))
                    {
                        Building[] expr_47C_cp_0 = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
                        ushort expr_47C_cp_1 = num2;
                        expr_47C_cp_0[(int)expr_47C_cp_1].m_flags = (expr_47C_cp_0[(int)expr_47C_cp_1].m_flags | Building.Flags.FixedHeight);
                        Singleton<SimulationManager>.instance.m_currentBuildIndex += 1u;
                    }
                }
            }
            bool isCurved = middlePoint.m_direction.x * endPoint.m_direction.x + middlePoint.m_direction.z * endPoint.m_direction.z <= 0.999f;
            Vector2 vector = new Vector2(startPoint.m_position.x - middlePoint.m_position.x, startPoint.m_position.z - middlePoint.m_position.z);
            float magnitude = vector.magnitude;
            Vector2 vector2 = new Vector2(middlePoint.m_position.x - endPoint.m_position.x, middlePoint.m_position.z - endPoint.m_position.z);
            float magnitude2 = vector2.magnitude;
            float length = magnitude + magnitude2;
            if (test && maxSegments != 0)
            {
                float num4 = 7f;
                if (isCurved && midleSegment == 0)
                {
                    if (magnitude < num4)
                    {
                        toolErrors |= ToolBase.ToolErrors.TooShort;
                    }
                    if (magnitude2 < num4)
                    {
                        toolErrors |= ToolBase.ToolErrors.TooShort;
                    }
                }
                else if (length < num4)
                {
                    toolErrors |= ToolBase.ToolErrors.TooShort;
                }
            }
            segment = 0;
            int nodesNeeded = Mathf.Min(maxSegments, Mathf.FloorToInt(length / 100f) + 1);
            if (nodesNeeded >= 2)
            {
                enableDouble = true;
            }
            ushort startNodeIndex = startPoint.m_node;
            Vector3 startPosition = startPoint.m_position;
            Vector3 middleDirection = middlePoint.m_direction;
            Vector3 middlePosition1;
            Vector3 middlePosition2;
            NetSegment.CalculateMiddlePoints(startPoint.m_position, middlePoint.m_direction, endPoint.m_position, -endPoint.m_direction, smoothStart, smoothEnd, out middlePosition1, out middlePosition2);
            nodeBuffer.Clear();
            NetTool.NodePosition item;
            item.m_position = startPosition;
            item.m_direction = middleDirection;
            item.m_minY = startPosition.y;
            item.m_maxY = startPosition.y;
            item.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(item.m_position);
            item.m_elevation = startPoint.m_elevation;
            if (Mathf.Abs(item.m_elevation) < TerrainStep - 1)
            {
                item.m_elevation = 0f;
            }
            item.m_double = false;
            if (startPoint.m_node != 0)
            {
                item.m_double = ((Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startPoint.m_node].m_flags & NetNode.Flags.Double) != NetNode.Flags.None);
            }
            else if (info.m_netAI.RequireDoubleSegments() && maxSegments <= 1)
            {
                item.m_double = true;
            }
            item.m_nodeInfo = null;
            nodeBuffer.Add(item);
            for (int i = 1; i <= nodesNeeded; i++)
            {
                item.m_elevation = Mathf.Lerp(startPoint.m_elevation, endPoint.m_elevation, (float)i / (float)nodesNeeded);
                if (Mathf.Abs(item.m_elevation) < TerrainStep - 1)
                {
                    item.m_elevation = 0f;
                }
                item.m_double = false;
                if (i == nodesNeeded)
                {
                    item.m_position = endPoint.m_position;
                    item.m_direction = endPoint.m_direction;
                    item.m_minY = endPoint.m_position.y;
                    item.m_maxY = endPoint.m_position.y;
                    item.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(item.m_position);
                    if (endPoint.m_node != 0)
                    {
                        item.m_double = ((Singleton<NetManager>.instance.m_nodes.m_buffer[(int)endPoint.m_node].m_flags & NetNode.Flags.Double) != NetNode.Flags.None);
                    }
                }
                else if (isCurved)
                {
                    item.m_position = Bezier3.Position(startPoint.m_position, middlePosition1, middlePosition2, endPoint.m_position, (float)i / (float)nodesNeeded);
                    item.m_direction = Bezier3.Tangent(startPoint.m_position, middlePosition1, middlePosition2, endPoint.m_position, (float)i / (float)nodesNeeded);
                    item.m_position.y = NetSegment.SampleTerrainHeight(info, item.m_position, visualize, item.m_elevation);
                    item.m_direction = VectorUtils.NormalizeXZ(item.m_direction);
                    item.m_minY = 0f;
                    item.m_maxY = 1280f;
                    item.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(item.m_position);
                }
                else
                {
                    float lengthSnap = info.m_netAI.GetLengthSnap();
                    item.m_position = LerpPosition(startPoint.m_position, endPoint.m_position, (float)i / (float)nodesNeeded, lengthSnap);
                    item.m_direction = endPoint.m_direction;
                    item.m_position.y = NetSegment.SampleTerrainHeight(info, item.m_position, visualize, item.m_elevation);
                    item.m_minY = 0f;
                    item.m_maxY = 1280f;
                    item.m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(item.m_position);
                }
                nodeBuffer.Add(item);
            }
            ToolBase.ToolErrors toolErrors2 = CheckNodeHeights(info, nodeBuffer);
            if (toolErrors2 != ToolBase.ToolErrors.None && test)
            {
                toolErrors |= toolErrors2;
            }
            float heightAboveGround = nodeBuffer.m_buffer[0].m_position.y - nodeBuffer.m_buffer[0].m_terrainHeight;
            if (heightAboveGround > 0f && nodesNeeded >= 1 && nodeBuffer.m_buffer[1].m_position.y - nodeBuffer.m_buffer[1].m_terrainHeight < -8f)
            {
                heightAboveGround = 0f;
                nodeBuffer.m_buffer[0].m_terrainHeight = nodeBuffer.m_buffer[0].m_position.y;
            }
            nodeBuffer.m_buffer[0].m_nodeInfo = info.m_netAI.GetOverriddenInfo(heightAboveGround, heightAboveGround, length, startPoint.m_outside, false, isCurved, enableDouble, ref toolErrors);
            for (int j = 1; j <= nodesNeeded; j++)
            {
                heightAboveGround = nodeBuffer.m_buffer[j].m_position.y - nodeBuffer.m_buffer[j].m_terrainHeight;
                if (heightAboveGround > 0f)
                {
                    if (nodeBuffer.m_buffer[j - 1].m_position.y - nodeBuffer.m_buffer[j - 1].m_terrainHeight < -8f)
                    {
                        heightAboveGround = 0f;
                        nodeBuffer.m_buffer[j].m_terrainHeight = nodeBuffer.m_buffer[j].m_position.y;
                    }
                    if (nodesNeeded > j && nodeBuffer.m_buffer[j + 1].m_position.y - nodeBuffer.m_buffer[j + 1].m_terrainHeight < -8f)
                    {
                        heightAboveGround = 0f;
                        nodeBuffer.m_buffer[j].m_terrainHeight = nodeBuffer.m_buffer[j].m_position.y;
                    }
                }
                nodeBuffer.m_buffer[j].m_nodeInfo = info.m_netAI.GetOverriddenInfo(heightAboveGround, heightAboveGround, length, false, j == nodesNeeded && endPoint.m_outside, isCurved, enableDouble, ref toolErrors);
            }
            int currentNode = 1;
            int num8 = 0;
            NetInfo netInfo = null;
            while (currentNode <= nodesNeeded)
            {
                NetInfo nodeInfo = nodeBuffer.m_buffer[currentNode].m_nodeInfo;
                if (currentNode != nodesNeeded && nodeInfo == netInfo)
                {
                    num8++;
                    currentNode++;
                }
                else
                {
                    if (num8 != 0 && netInfo.m_netAI.RequireDoubleSegments())
                    {
                        int num9 = currentNode - num8 - 1;
                        int num10 = currentNode;
                        if ((num8 & 1) == 0)
                        {
                            nodeBuffer.RemoveAt(currentNode - 1);
                            nodesNeeded--;
                            num10--;
                            for (int l = num9 + 1; l < num10; l++)
                            {
                                float t = (float)(l - num9) / (float)num8;
                                nodeBuffer.m_buffer[l].m_position = Vector3.Lerp(nodeBuffer.m_buffer[num9].m_position, nodeBuffer.m_buffer[num10].m_position, t);
                                nodeBuffer.m_buffer[l].m_direction = VectorUtils.NormalizeXZ(Vector3.Lerp(nodeBuffer.m_buffer[num9].m_direction, nodeBuffer.m_buffer[num10].m_direction, t));
                                nodeBuffer.m_buffer[l].m_elevation = Mathf.Lerp(nodeBuffer.m_buffer[num9].m_elevation, nodeBuffer.m_buffer[num10].m_elevation, t);
                                nodeBuffer.m_buffer[l].m_terrainHeight = Singleton<TerrainManager>.instance.SampleRawHeightSmooth(nodeBuffer.m_buffer[l].m_position);
                            }
                        }
                        else
                        {
                            currentNode++;
                        }
                        for (int m = num9 + 1; m < num10; m++)
                        {
                            NetTool.NodePosition[] expr_DBB_cp_0 = nodeBuffer.m_buffer;
                            int expr_DBB_cp_1 = m;
                            expr_DBB_cp_0[expr_DBB_cp_1].m_double = (expr_DBB_cp_0[expr_DBB_cp_1].m_double | (m - num9 & 1) == 1);
                        }
                    }
                    else
                    {
                        currentNode++;
                    }
                    num8 = 1;
                }
                netInfo = nodeInfo;
            }
            NetInfo startNodeInfo = nodeBuffer[0].m_nodeInfo;
            bool flag3 = false;
            if (startNodeIndex == 0 && !test && !visualize)
            {
                if (startPoint.m_segment != 0)
                {
                    if (SplitSegment(startPoint.m_segment, out startNodeIndex, startPosition))
                    {
                        flag3 = true;
                    }
                    startPoint.m_segment = 0;
                }
                else if (Singleton<NetManager>.instance.CreateNode(out startNodeIndex, ref Singleton<SimulationManager>.instance.m_randomizer, startNodeInfo, startPosition, Singleton<SimulationManager>.instance.m_currentBuildIndex))
                {
                    if (startPoint.m_outside)
                    {
                        NetNode[] expr_EA2_cp_0 = Singleton<NetManager>.instance.m_nodes.m_buffer;
                        ushort expr_EA2_cp_1 = startNodeIndex;
                        expr_EA2_cp_0[(int)expr_EA2_cp_1].m_flags = (expr_EA2_cp_0[(int)expr_EA2_cp_1].m_flags | NetNode.Flags.Outside);
                    }
                    if (startPosition.y - nodeBuffer.m_buffer[0].m_terrainHeight < -8f && (startNodeInfo.m_netAI.SupportUnderground() || startNodeInfo.m_netAI.IsUnderground()))
                    {
                        Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startNodeIndex].m_flags |= NetNode.Flags.Underground;
                    }
                    else if (startPosition.y - nodeBuffer.m_buffer[0].m_terrainHeight < TerrainStep - 1)
                    {
                        Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startNodeIndex].m_flags |= NetNode.Flags.OnGround;
                    }
                    if (nodeBuffer.m_buffer[0].m_double)
                    {
                        Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startNodeIndex].m_flags |= NetNode.Flags.Double;
                    }
                    if (startNodeInfo.m_netAI.IsUnderground())
                    {
                        Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startNodeIndex].m_elevation = (byte)Mathf.Clamp(Mathf.RoundToInt(-nodeBuffer[0].m_elevation), 0, 255);
                    }
                    else
                    {
                        Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startNodeIndex].m_elevation = (byte)Mathf.Clamp(Mathf.RoundToInt(nodeBuffer[0].m_elevation), 0, 255);
                    }
                    Singleton<SimulationManager>.instance.m_currentBuildIndex += 1u;
                    flag3 = true;
                }
                startPoint.m_node = startNodeIndex;
            }
            NetNode netNode = default(NetNode);
            netNode.m_position = startPosition;
            if (nodeBuffer.m_buffer[0].m_double)
            {
                netNode.m_flags |= NetNode.Flags.Double;
            }
            if (startPosition.y - nodeBuffer.m_buffer[0].m_terrainHeight < -8f && (startNodeInfo.m_netAI.SupportUnderground() || startNodeInfo.m_netAI.IsUnderground()))
            {
                netNode.m_flags |= NetNode.Flags.Underground;
            }
            else if (startPosition.y - nodeBuffer.m_buffer[0].m_terrainHeight < TerrainStep - 1)
            {
                netNode.m_flags |= NetNode.Flags.OnGround;
            }
            if (startPoint.m_outside)
            {
                netNode.m_flags |= NetNode.Flags.Outside;
            }
            BuildingInfo nodeBuilding;
            float heightOffset;
            startNodeInfo.m_netAI.GetNodeBuilding(0, ref netNode, out nodeBuilding, out heightOffset);
            if (visualize)
            {
                if (nodeBuilding != null && (startNodeIndex == 0 || midleSegment != 0))
                {
                    Vector3 position = startPosition;
                    position.y += heightOffset;
                    RenderNodeBuilding(nodeBuilding, position, middleDirection);
                }
                if (startNodeInfo.m_netAI.DisplayTempSegment())
                {
                    RenderNode(startNodeInfo, startPosition, middleDirection);
                }
            }
            else if (nodeBuilding != null && (netNode.m_flags & NetNode.Flags.Outside) == NetNode.Flags.None)
            {
                ushort startPointNode = startPoint.m_node;
                ushort startPointSegment = startPoint.m_segment;
                ushort ignoredBuilding = GetIgnoredBuilding(startPoint);
                toolErrors2 = TestNodeBuilding(nodeBuilding, startPosition, middleDirection, startPointNode, startPointSegment, ignoredBuilding, test, numArray, numArray2);
                if (test && toolErrors2 != ToolBase.ToolErrors.None)
                {
                    toolErrors |= toolErrors2;
                }
            }
            if (num2 != 0 && startNodeIndex != 0 && (Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startNodeIndex].m_flags & NetNode.Flags.Untouchable) == NetNode.Flags.None)
            {
                Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startNodeIndex].m_flags |= NetNode.Flags.Untouchable;
                Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startNodeIndex].m_nextBuildingNode = Singleton<BuildingManager>.instance.m_buildings.m_buffer[(int)num2].m_netNode;
                Singleton<BuildingManager>.instance.m_buildings.m_buffer[(int)num2].m_netNode = startNodeIndex;
            }
            for (int nodeIndex = 1; nodeIndex <= nodesNeeded; nodeIndex++)
            {
                Vector3 thisPosition = nodeBuffer[nodeIndex].m_position;
                Vector3 thisDirection = nodeBuffer[nodeIndex].m_direction;
                Vector3 middlePos1;
                Vector3 middlePos2;
                NetSegment.CalculateMiddlePoints(startPosition, middleDirection, thisPosition, -thisDirection, smoothStart, smoothEnd, out middlePos1, out middlePos2);
                startNodeInfo = nodeBuffer.m_buffer[nodeIndex].m_nodeInfo;
                float lastElevation = nodeBuffer[nodeIndex - 1].m_position.y - nodeBuffer[nodeIndex - 1].m_terrainHeight;
                float thisElevation = nodeBuffer[nodeIndex].m_position.y - nodeBuffer[nodeIndex].m_terrainHeight;
                NetInfo currentNodeInfo;
                if (nodeBuffer.m_buffer[nodeIndex].m_double)
                {
                    currentNodeInfo = nodeBuffer.m_buffer[nodeIndex].m_nodeInfo;
                    netNode.m_flags |= NetNode.Flags.Double;
                }
                else if (nodeBuffer.m_buffer[nodeIndex - 1].m_double)
                {
                    currentNodeInfo = nodeBuffer.m_buffer[nodeIndex - 1].m_nodeInfo;
                    netNode.m_flags &= ~NetNode.Flags.Double;
                }
                else
                {
                    float minElevation = Mathf.Min(thisElevation, lastElevation);
                    float maxElevation = Mathf.Max(thisElevation, lastElevation);
                    if (maxElevation >= -8f)
                    {
                        for (int num15 = 1; num15 < 8; num15++)
                        {
                            Vector3 worldPos = Bezier3.Position(startPosition, middlePos1, middlePos2, thisPosition, (float)num15 / 8f);
                            heightAboveGround = worldPos.y - Singleton<TerrainManager>.instance.SampleRawHeightSmooth(worldPos);
                            maxElevation = Mathf.Max(maxElevation, heightAboveGround);
                        }
                    }
                    currentNodeInfo = info.m_netAI.GetOverriddenInfo(minElevation, maxElevation, length, nodeIndex == 1 && startPoint.m_outside, nodeIndex == nodesNeeded && endPoint.m_outside, isCurved, false, ref toolErrors);
                    netNode.m_flags &= ~NetNode.Flags.Double;
                }
                bool isOverGround = (netNode.m_flags & NetNode.Flags.Underground) != NetNode.Flags.None;
                bool isUnderGround = !isOverGround;
                netNode.m_position = thisPosition;
                if (thisElevation < -8f && (startNodeInfo.m_netAI.SupportUnderground() || startNodeInfo.m_netAI.IsUnderground()))
                {
                    netNode.m_flags |= NetNode.Flags.Underground;
                    netNode.m_flags &= ~NetNode.Flags.OnGround;
                    isOverGround = false;
                }
                else if (thisElevation < TerrainStep - 1)
                {
                    netNode.m_flags |= NetNode.Flags.OnGround;
                    netNode.m_flags &= ~NetNode.Flags.Underground;
                    isUnderGround = false;
                }
                else
                {
                    netNode.m_flags &= ~NetNode.Flags.OnGround;
                    netNode.m_flags &= ~NetNode.Flags.Underground;
                    isUnderGround = false;
                }
                if (nodeIndex == nodesNeeded && endPoint.m_outside)
                {
                    netNode.m_flags |= NetNode.Flags.Outside;
                }
                else
                {
                    netNode.m_flags &= ~NetNode.Flags.Outside;
                }
                startNodeInfo.m_netAI.GetNodeBuilding(0, ref netNode, out nodeBuilding, out heightOffset);
                if (visualize)
                {
                    if (nodeBuilding != null && (nodeIndex != nodesNeeded || endPoint.m_node == 0 || midleSegment != 0))
                    {
                        Vector3 position3 = thisPosition;
                        position3.y += heightOffset;
                        RenderNodeBuilding(nodeBuilding, position3, thisDirection);
                    }
                    if (currentNodeInfo.m_netAI.DisplayTempSegment())
                    {
                        if (nodeBuffer.m_buffer[nodeIndex].m_double || isUnderGround)
                        {
                            RenderSegment(currentNodeInfo, thisPosition, startPosition, -thisDirection, -middleDirection, smoothStart, smoothEnd);
                        }
                        else
                        {
                            RenderSegment(currentNodeInfo, startPosition, thisPosition, middleDirection, thisDirection, smoothStart, smoothEnd);
                        }
                    }
                }
                else
                {
                    if (currentNodeInfo.m_canCollide)
                    {
                        int num16 = Mathf.Max(2, 16 / nodesNeeded);
                        Vector3 b2 = new Vector3(middleDirection.z, 0f, -middleDirection.x) * currentNodeInfo.m_halfWidth;
                        Quad3 quad = default(Quad3);
                        quad.a = startPosition - b2;
                        quad.d = startPosition + b2;
                        for (int num17 = 1; num17 <= num16; num17++)
                        {
                            ushort ignoreNode = 0;
                            ushort ignoreNode2 = 0;
                            ushort ignoreSegment = 0;
                            ushort ignoreBuilding = 0;
                            bool mOutside = false;
                            if (nodeIndex == 1 && num17 - 1 << 1 < num16)
                            {
                                ignoreNode = startPoint.m_node;
                                if (nodeIndex == nodesNeeded && num17 << 1 >= num16)
                                {
                                    ignoreNode2 = endPoint.m_node;
                                }
                                ignoreSegment = startPoint.m_segment;
                                ignoreBuilding = GetIgnoredBuilding(startPoint);
                                mOutside = startPoint.m_outside;
                            }
                            else if (nodeIndex == nodesNeeded && num17 << 1 > num16)
                            {
                                ignoreNode = endPoint.m_node;
                                if (nodeIndex == 1 && num17 - 1 << 1 <= num16)
                                {
                                    ignoreNode2 = startPoint.m_node;
                                }
                                ignoreSegment = endPoint.m_segment;
                                ignoreBuilding = GetIgnoredBuilding(endPoint);
                                mOutside = endPoint.m_outside;
                            }
                            else if (num17 - 1 << 1 < num16)
                            {
                                ignoreNode = startNodeIndex;
                            }
                            Vector3 a = Bezier3.Position(startPosition, middlePos1, middlePos2, thisPosition, (float)num17 / (float)num16);
                            b2 = Bezier3.Tangent(startPosition, middlePos1, middlePos2, thisPosition, (float)num17 / (float)num16);
                            Vector3 vector8 = new Vector3(b2.z, 0f, -b2.x);
                            b2 = vector8.normalized * currentNodeInfo.m_halfWidth;
                            quad.b = a - b2;
                            quad.c = a + b2;
                            float minY = Mathf.Min(Mathf.Min(quad.a.y, quad.b.y), Mathf.Min(quad.c.y, quad.d.y)) + currentNodeInfo.m_minHeight;
                            float maxY = Mathf.Max(Mathf.Max(quad.a.y, quad.b.y), Mathf.Max(quad.c.y, quad.d.y)) + currentNodeInfo.m_maxHeight;
                            Quad2 quad2 = Quad2.XZ(quad);
                            ItemClass.CollisionType collisionType = ItemClass.CollisionType.Elevated;
                            if (currentNodeInfo.m_flattenTerrain)
                                collisionType = ItemClass.CollisionType.Terrain;
                            else if (currentNodeInfo.m_netAI.IsUnderground())
                                collisionType = ItemClass.CollisionType.Underground;
                            Singleton<NetManager>.instance.OverlapQuad(quad2, minY, maxY, collisionType, currentNodeInfo.m_class.m_layer, ignoreNode, ignoreNode2, ignoreSegment, numArray);
                            Singleton<BuildingManager>.instance.OverlapQuad(quad2, minY, maxY, collisionType, currentNodeInfo.m_class.m_layer, ignoreBuilding, ignoreNode, ignoreNode2, numArray2);
                            if (test)
                            {
                                if ((properties.m_mode & ItemClass.Availability.AssetEditor) != ItemClass.Availability.None)
                                {
                                    float num18 = 256f;
                                    if (quad2.a.x < -num18 || quad2.a.x > num18 || quad2.a.y < -num18 || quad2.a.y > num18)
                                    {
                                        toolErrors |= ToolBase.ToolErrors.OutOfArea;
                                    }
                                    if (quad2.b.x < -num18 || quad2.b.x > num18 || quad2.b.y < -num18 || quad2.b.y > num18)
                                    {
                                        toolErrors |= ToolBase.ToolErrors.OutOfArea;
                                    }
                                    if (quad2.c.x < -num18 || quad2.c.x > num18 || quad2.c.y < -num18 || quad2.c.y > num18)
                                    {
                                        toolErrors |= ToolBase.ToolErrors.OutOfArea;
                                    }
                                    if (quad2.d.x < -num18 || quad2.d.x > num18 || quad2.d.y < -num18 || quad2.d.y > num18)
                                    {
                                        toolErrors |= ToolBase.ToolErrors.OutOfArea;
                                    }
                                }
                                else if (!mOutside && Singleton<GameAreaManager>.instance.QuadOutOfArea(quad2))
                                {
                                    toolErrors |= ToolBase.ToolErrors.OutOfArea;
                                }
                            }
                            quad.a = quad.b;
                            quad.d = quad.c;
                        }
                    }
                    if (nodeBuilding != null && (netNode.m_flags & NetNode.Flags.Outside) == NetNode.Flags.None)
                    {
                        ushort ignoreNode3 = (nodeIndex != nodesNeeded) ? (ushort)0 : endPoint.m_node;
                        ushort ignoreSegment2 = (nodeIndex != nodesNeeded) ? (ushort)0 : endPoint.m_segment;
                        ushort ignoreBuilding2 = (nodeIndex != nodesNeeded) ? (ushort)0 : GetIgnoredBuilding(endPoint);
                        Vector3 position4 = thisPosition;
                        position4.y += heightOffset;
                        toolErrors2 = TestNodeBuilding(nodeBuilding, position4, thisDirection, ignoreNode3, ignoreSegment2, ignoreBuilding2, test, numArray, numArray2);
                        if (test && toolErrors2 != ToolBase.ToolErrors.None)
                        {
                            toolErrors |= toolErrors2;
                        }
                    }
                    if (test)
                    {
                        cost += currentNodeInfo.m_netAI.GetConstructionCost(startPosition, thisPosition, lastElevation, thisElevation);
                        if (needMoney && cost > 0 && Singleton<EconomyManager>.instance.PeekResource(EconomyManager.Resource.Construction, cost) != cost)
                        {
                            toolErrors |= ToolBase.ToolErrors.NotEnoughMoney;
                        }
                        if (!currentNodeInfo.m_netAI.BuildUnderground())
                        {
                            float num19 = Singleton<TerrainManager>.instance.WaterLevel(VectorUtils.XZ(startPosition));
                            float num20 = Singleton<TerrainManager>.instance.WaterLevel(VectorUtils.XZ(thisPosition));
                            if (num19 > startPosition.y || num20 > thisPosition.y)
                            {
                                toolErrors |= ToolBase.ToolErrors.CannotBuildOnWater;
                            }
                        }
                        ushort startNode = 0;
                        ushort startSegment = 0;
                        ushort endNode = 0;
                        ushort endSegment = 0;
                        if (nodeIndex == 1)
                        {
                            startNode = startPoint.m_node;
                            startSegment = startPoint.m_segment;
                        }
                        if (nodeIndex == nodesNeeded)
                        {
                            endNode = endPoint.m_node;
                            endSegment = endPoint.m_segment;
                        }
                        toolErrors |= CanCreateSegment(currentNodeInfo, startNode, startSegment, endNode, endSegment, midleSegment, startPosition, thisPosition, middleDirection, -thisDirection, numArray);
                    }
                    else
                    {
                        cost += currentNodeInfo.m_netAI.GetConstructionCost(startPosition, thisPosition, lastElevation, thisElevation);
                        if (needMoney && cost > 0)
                        {
                            cost -= Singleton<EconomyManager>.instance.FetchResource(EconomyManager.Resource.Construction, cost, currentNodeInfo.m_class);
                            if (cost > 0)
                            {
                                toolErrors |= ToolBase.ToolErrors.NotEnoughMoney;
                            }
                        }
                        bool isUnSplit = startNodeIndex == 0;
                        bool isSplit = false;
                        ushort endPointNode = endPoint.m_node;
                        if (nodeIndex != nodesNeeded || endPointNode == 0)
                        {
                            if (nodeIndex == nodesNeeded && endPoint.m_segment != 0)
                            {
                                if (SplitSegment(endPoint.m_segment, out endPointNode, thisPosition))
                                {
                                    isSplit = true;
                                }
                                else
                                {
                                    isUnSplit = true;
                                }
                                endPoint.m_segment = 0;
                            }
                            else if (Singleton<NetManager>.instance.CreateNode(out endPointNode, ref Singleton<SimulationManager>.instance.m_randomizer, startNodeInfo, thisPosition, Singleton<SimulationManager>.instance.m_currentBuildIndex))
                            {
                                if (nodeIndex == nodesNeeded && endPoint.m_outside)
                                {
                                    Singleton<NetManager>.instance.m_nodes.m_buffer[endPointNode].m_flags |= NetNode.Flags.Outside;
                                }
                                if (thisElevation < -8f && (startNodeInfo.m_netAI.SupportUnderground() || startNodeInfo.m_netAI.IsUnderground()))
                                {
                                    Singleton<NetManager>.instance.m_nodes.m_buffer[endPointNode].m_flags |= NetNode.Flags.Underground;
                                }
                                else if (thisElevation < TerrainStep - 1)
                                {
                                    Singleton<NetManager>.instance.m_nodes.m_buffer[endPointNode].m_flags |= NetNode.Flags.OnGround;
                                }
                                if (nodeBuffer.m_buffer[nodeIndex].m_double)
                                {
                                    Singleton<NetManager>.instance.m_nodes.m_buffer[endPointNode].m_flags |= NetNode.Flags.Double;
                                }
                                if (startNodeInfo.m_netAI.IsUnderground())
                                {
                                    Singleton<NetManager>.instance.m_nodes.m_buffer[(int)endPointNode].m_elevation = (byte)Mathf.Clamp(Mathf.RoundToInt(-nodeBuffer[nodeIndex].m_elevation), 0, 255);
                                }
                                else
                                {
                                    Singleton<NetManager>.instance.m_nodes.m_buffer[(int)endPointNode].m_elevation = (byte)Mathf.Clamp(Mathf.RoundToInt(nodeBuffer[nodeIndex].m_elevation), 0, 255);
                                }
                                Singleton<SimulationManager>.instance.m_currentBuildIndex += 1u;
                                isSplit = true;
                            }
                            else
                            {
                                isUnSplit = true;
                            }
                            if (nodeIndex == nodesNeeded)
                            {
                                endPoint.m_node = endPointNode;
                            }
                        }
                        if (!isUnSplit && !isCurved && Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startNodeIndex].m_elevation == Singleton<NetManager>.instance.m_nodes.m_buffer[(int)endPointNode].m_elevation)
                        {
                            Vector3 endPos = startPosition;
                            if (nodeIndex == 1)
                            {
                                TryMoveNode(ref startNodeIndex, ref middleDirection, currentNodeInfo, thisPosition);
                                endPos = Singleton<NetManager>.instance.m_nodes.m_buffer[(int)startNodeIndex].m_position;
                            }
                            if (nodeIndex == nodesNeeded)
                            {
                                Vector3 a2 = -thisDirection;
                                TryMoveNode(ref endPointNode, ref a2, currentNodeInfo, endPos);
                                thisDirection = -a2;
                            }
                        }
                        if (!isUnSplit)
                        {
                            if (nodeBuffer.m_buffer[nodeIndex].m_double || isUnderGround)
                            {
                                isUnSplit = !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, currentNodeInfo, endPointNode, startNodeIndex, -thisDirection, middleDirection, currentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, !flag);
                            }
                            else if (nodeBuffer.m_buffer[nodeIndex - 1].m_double || isOverGround)
                            {
                                isUnSplit = !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, currentNodeInfo, startNodeIndex, endPointNode, middleDirection, -thisDirection, currentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, flag);
                            }
                            else if ((nodesNeeded - nodeIndex & 1) == 0 && nodeIndex != 1 && isCurved)
                            {
                                isUnSplit = !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, currentNodeInfo, endPointNode, startNodeIndex, -thisDirection, middleDirection, currentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, !flag);
                            }
                            else
                            {
                                isUnSplit = !Singleton<NetManager>.instance.CreateSegment(out segment, ref Singleton<SimulationManager>.instance.m_randomizer, currentNodeInfo, startNodeIndex, endPointNode, middleDirection, -thisDirection, currentBuildIndex, Singleton<SimulationManager>.instance.m_currentBuildIndex, flag);
                            }
                            if (!isUnSplit)
                            {
                                Singleton<SimulationManager>.instance.m_currentBuildIndex += 2u;
                                currentBuildIndex = Singleton<SimulationManager>.instance.m_currentBuildIndex;
                                NetTool.DispatchPlacementEffect(startPosition, middlePos1, middlePos2, thisPosition, info.m_halfWidth, false);
                                currentNodeInfo.m_netAI.ManualActivation(segment, ref Singleton<NetManager>.instance.m_segments.m_buffer[(int)segment], oldInfo);
                            }
                        }
                        if (isUnSplit)
                        {
                            if (flag3 && startNodeIndex != 0)
                            {
                                Singleton<NetManager>.instance.ReleaseNode(startNodeIndex);
                                startNodeIndex = 0;
                            }
                            if (isSplit && endPointNode != 0)
                            {
                                Singleton<NetManager>.instance.ReleaseNode(endPointNode);
                                endPointNode = 0;
                            }
                        }
                        if (num2 != 0 && endPointNode != 0 && (Singleton<NetManager>.instance.m_nodes.m_buffer[(int)endPointNode].m_flags & NetNode.Flags.Untouchable) == NetNode.Flags.None)
                        {
                            NetNode[] expr_226A_cp_0 = Singleton<NetManager>.instance.m_nodes.m_buffer;
                            ushort expr_226A_cp_1 = endPointNode;
                            expr_226A_cp_0[(int)expr_226A_cp_1].m_flags = (expr_226A_cp_0[(int)expr_226A_cp_1].m_flags | NetNode.Flags.Untouchable);
                            Singleton<NetManager>.instance.m_nodes.m_buffer[(int)endPointNode].m_nextBuildingNode = Singleton<BuildingManager>.instance.m_buildings.m_buffer[(int)num2].m_netNode;
                            Singleton<BuildingManager>.instance.m_buildings.m_buffer[(int)num2].m_netNode = endPointNode;
                        }
                        if (num2 != 0 && segment != 0 && (Singleton<NetManager>.instance.m_segments.m_buffer[(int)segment].m_flags & NetSegment.Flags.Untouchable) == NetSegment.Flags.None)
                        {
                            NetSegment[] expr_2318_cp_0 = Singleton<NetManager>.instance.m_segments.m_buffer;
                            ushort expr_2318_cp_1 = segment;
                            expr_2318_cp_0[(int)expr_2318_cp_1].m_flags = (expr_2318_cp_0[(int)expr_2318_cp_1].m_flags | NetSegment.Flags.Untouchable);
                        }
                        startNodeIndex = endPointNode;
                    }
                }
                startPosition = thisPosition;
                middleDirection = thisDirection;
                flag3 = false;
            }
            if (visualize)
            {
                if (startNodeInfo.m_netAI.DisplayTempSegment())
                {
                    RenderNode(startNodeInfo, startPosition, -middleDirection);
                }
            }
            else
            {
                BuildingTool.IgnoreRelocateSegments(relocateBuildingID, numArray, numArray2);
                if (NetTool.CheckCollidingSegments(numArray, numArray2, midleSegment) && (toolErrors & (ToolBase.ToolErrors.InvalidShape | ToolBase.ToolErrors.TooShort | ToolBase.ToolErrors.SlopeTooSteep | ToolBase.ToolErrors.HeightTooHigh | ToolBase.ToolErrors.TooManyConnections)) == ToolBase.ToolErrors.None)
                {
                    toolErrors |= ToolBase.ToolErrors.ObjectCollision;
                }
                if (BuildingTool.CheckCollidingBuildings(null, numArray2, numArray))
                {
                    toolErrors |= ToolBase.ToolErrors.ObjectCollision;
                }
                if (!test)
                {
                    NetTool.ReleaseNonImportantSegments(numArray);
                    BuildingTool.ReleaseNonImportantBuildings(numArray2);
                }
            }
            for (int num22 = 0; num22 <= nodesNeeded; num22++)
            {
                nodeBuffer.m_buffer[num22].m_nodeInfo = null;
            }
            firstNode = startPoint.m_node;
            lastNode = endPoint.m_node;
            result = toolErrors;
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
        return result;
    }


    //Modified.
    private bool CreateNodeImpl(bool switchDirection)
    {
        NetInfo prefab = this.m_prefab;
        if (prefab != null)
        {
            if (this.m_mode == NetTool.Mode.Upgrade && this.m_controlPointCount < 2)
            {
                prefab.m_netAI.UpgradeFailed();
            }
            else
            {
                if (this.m_mode == NetTool.Mode.Straight && this.m_controlPointCount < 1)
                {
                    int min;
                    int max;
                    GetAdjustedElevationLimits(prefab.m_netAI, out min, out max);
                    this.m_elevation = Mathf.Clamp(Mathf.RoundToInt(this.m_controlPoints[this.m_controlPointCount].m_elevation / TerrainStep), min, max);
                    this.m_controlPoints[this.m_controlPointCount + 1] = this.m_controlPoints[this.m_controlPointCount];
                    this.m_controlPoints[this.m_controlPointCount + 1].m_node = 0;
                    this.m_controlPoints[this.m_controlPointCount + 1].m_segment = 0;
                    this.m_controlPointCount++;
                    return true;
                }
                if ((this.m_mode == NetTool.Mode.Curved || this.m_mode == NetTool.Mode.Freeform) && this.m_controlPointCount < 2 && (this.m_controlPointCount == 0 || (this.m_controlPoints[1].m_node == 0 && this.m_controlPoints[1].m_segment == 0)))
                {
                    int min;
                    int max;
                    GetAdjustedElevationLimits(prefab.m_netAI, out min, out max);
                    this.m_elevation = Mathf.Clamp(Mathf.RoundToInt(this.m_controlPoints[this.m_controlPointCount].m_elevation / TerrainStep), min, max);
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
                NetTool.ControlPoint controlPoint;
                NetTool.ControlPoint controlPoint2;
                NetTool.ControlPoint controlPoint3;
                if (this.m_controlPointCount == 1)
                {
                    controlPoint = this.m_controlPoints[0];
                    controlPoint2 = this.m_controlPoints[1];
                    controlPoint3 = this.m_controlPoints[1];
                    controlPoint3.m_node = 0;
                    controlPoint3.m_segment = 0;
                    controlPoint3.m_position = (this.m_controlPoints[0].m_position + this.m_controlPoints[1].m_position) * 0.5f;
                    controlPoint3.m_elevation = (this.m_controlPoints[0].m_elevation + this.m_controlPoints[1].m_elevation) * 0.5f;
                }
                else
                {
                    controlPoint = this.m_controlPoints[0];
                    controlPoint3 = this.m_controlPoints[1];
                    controlPoint2 = this.m_controlPoints[2];
                }
                NetTool.ControlPoint startPoint = controlPoint;
                NetTool.ControlPoint middlePoint = controlPoint3;
                NetTool.ControlPoint endPoint = controlPoint2;
                bool secondaryControlPoints = GetSecondaryControlPoints(prefab, ref startPoint, ref middlePoint, ref endPoint);
                if (this.CreateNodeImpl(prefab, needMoney, switchDirection, controlPoint, controlPoint3, controlPoint2))
                {
                    if (secondaryControlPoints)
                    {
                        this.CreateNodeImpl(prefab, needMoney, switchDirection, startPoint, middlePoint, endPoint);
                    }
                    return true;
                }
            }
        }
        return false;
    }

    //Modified
    private bool CreateNodeImpl(NetInfo info, bool needMoney, bool switchDirection, NetTool.ControlPoint startPoint, NetTool.ControlPoint middlePoint, NetTool.ControlPoint endPoint)
    {
        bool flag = endPoint.m_node != 0 || endPoint.m_segment != 0;
        ushort num;
        ushort num2;
        int num3;
        int num4;
        if (CreateNode(info, startPoint, middlePoint, endPoint, NetTool.m_nodePositionsSimulation, 1000, true, false, true, needMoney, false, switchDirection, 0, out num, out num2, out num3, out num4) == ToolBase.ToolErrors.None)
        {
            CreateNode(info, startPoint, middlePoint, endPoint, NetTool.m_nodePositionsSimulation, 1000, false, false, true, needMoney, false, switchDirection, 0, out num, out num2, out num3, out num4);
            NetManager instance = Singleton<NetManager>.instance;
            endPoint.m_segment = 0;
            endPoint.m_node = num;
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
                if (instance.m_segments.m_buffer[(int)num2].m_startNode == num)
                {
                    endPoint.m_direction = -instance.m_segments.m_buffer[(int)num2].m_startDirection;
                }
                else if (instance.m_segments.m_buffer[(int)num2].m_endNode == num)
                {
                    endPoint.m_direction = -instance.m_segments.m_buffer[(int)num2].m_endDirection;
                }
            }
            this.m_controlPoints[0] = endPoint;
            //If we're extending a road, figure out how high we're supposed to be.
            if (!this.m_upgrading)
            {
                int min;
                int max;
                GetAdjustedElevationLimits(info.m_netAI, out min, out max);
                this.m_elevation = Mathf.Clamp(Mathf.RoundToInt(endPoint.m_elevation / TerrainStep), min, max);
            }
            if (num != 0 && (instance.m_nodes.m_buffer[(int)num].m_flags & NetNode.Flags.Outside) != NetNode.Flags.None)
            {
                this.m_controlPointCount = 0;
            }
            else if (this.m_mode == NetTool.Mode.Freeform && this.m_controlPointCount == 2)
            {
                middlePoint.m_position = endPoint.m_position * 2f - middlePoint.m_position;
                middlePoint.m_elevation = endPoint.m_elevation * 2f - middlePoint.m_elevation;
                middlePoint.m_direction = endPoint.m_direction;
                middlePoint.m_node = 0;
                middlePoint.m_segment = 0;
                this.m_controlPoints[1] = middlePoint;
                this.m_controlPointCount = 2;
            }
            else
            {
                this.m_controlPointCount = 1;
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
            if (this.m_upgrading)
            {
                info.m_netAI.UpgradeSucceeded();
            }
            else if (flag && num != 0)
            {
                info.m_netAI.ConnectionSucceeded(num, ref Singleton<NetManager>.instance.m_nodes.m_buffer[(int)num]);
            }
            Singleton<GuideManager>.instance.m_notEnoughMoney.Deactivate();
            if (Singleton<GuideManager>.instance.m_properties != null && !this.m_upgrading && num2 != 0 && this.m_bulldozerTool != null && this.m_bulldozerTool.m_lastNetInfo != null && this.m_bulldozerTool.m_lastNetInfo.m_netAI.CanUpgradeTo(info))
            {
                ushort startNode = instance.m_segments.m_buffer[(int)num2].m_startNode;
                ushort endNode = instance.m_segments.m_buffer[(int)num2].m_endNode;
                Vector3 position = instance.m_nodes.m_buffer[(int)startNode].m_position;
                Vector3 position2 = instance.m_nodes.m_buffer[(int)endNode].m_position;
                Vector3 startDirection = instance.m_segments.m_buffer[(int)num2].m_startDirection;
                Vector3 endDirection = instance.m_segments.m_buffer[(int)num2].m_endDirection;
                if (Vector3.SqrMagnitude(this.m_bulldozerTool.m_lastStartPos - position) < 1f && Vector3.SqrMagnitude(this.m_bulldozerTool.m_lastEndPos - position2) < 1f && Vector2.Dot(VectorUtils.XZ(this.m_bulldozerTool.m_lastStartDir), VectorUtils.XZ(startDirection)) > 0.99f && Vector2.Dot(VectorUtils.XZ(this.m_bulldozerTool.m_lastEndDir), VectorUtils.XZ(endDirection)) > 0.99f)
                {
                    Singleton<NetManager>.instance.m_manualUpgrade.Activate(Singleton<GuideManager>.instance.m_properties.m_manualUpgrade, info.m_class.m_service);
                }
            }
            return true;
        }
        return false;
    }


    /// <summary>
    /// Returns the elevation in real-world units of the current tool selection,
    /// clamping to the min and max values given by the road prefab.
    /// </summary>
    /// <param name="info">Road prefab to use for limits.</param>
    /// <returns>Height above the ground in meters.</returns>
    private float GetElevation(NetInfo info)
    {
        int min_height;
        int max_height;
        if (info == null)
        {
            return 0f;
        }
        GetAdjustedElevationLimits(info.m_netAI, out min_height, out max_height);
        if (min_height == max_height)
        {
            return 0f;
        }
        return (Mathf.Clamp(this.m_elevation, min_height, max_height) * TerrainStep);
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

    //New in 1.1.0
    private static bool GetSecondaryControlPoints(NetInfo info, ref NetTool.ControlPoint startPoint, ref NetTool.ControlPoint middlePoint, ref NetTool.ControlPoint endPoint)
    {
        ushort num = middlePoint.m_segment;
        if (startPoint.m_segment == num || endPoint.m_segment == num)
        {
            num = 0;
        }
        ushort num2 = 0;
        if (num != 0)
        {
            num2 = DefaultTool.FindSecondarySegment(num);
        }
        if (num2 != 0)
        {
            NetManager instance = Singleton<NetManager>.instance;
            startPoint.m_node = instance.m_segments.m_buffer[(int)num2].m_startNode;
            startPoint.m_segment = 0;
            startPoint.m_position = instance.m_nodes.m_buffer[(int)startPoint.m_node].m_position;
            startPoint.m_direction = instance.m_segments.m_buffer[(int)num2].m_startDirection;
            startPoint.m_elevation = (float)instance.m_nodes.m_buffer[(int)startPoint.m_node].m_elevation;
            if (instance.m_nodes.m_buffer[(int)startPoint.m_node].Info.m_netAI.IsUnderground())
            {
                startPoint.m_elevation = -startPoint.m_elevation;
            }
            startPoint.m_outside = ((instance.m_nodes.m_buffer[(int)startPoint.m_node].m_flags & NetNode.Flags.Outside) != NetNode.Flags.None);
            endPoint.m_node = instance.m_segments.m_buffer[(int)num2].m_endNode;
            endPoint.m_segment = 0;
            endPoint.m_position = instance.m_nodes.m_buffer[(int)endPoint.m_node].m_position;
            endPoint.m_direction = -instance.m_segments.m_buffer[(int)num2].m_endDirection;
            endPoint.m_elevation = (float)instance.m_nodes.m_buffer[(int)endPoint.m_node].m_elevation;
            if (instance.m_nodes.m_buffer[(int)endPoint.m_node].Info.m_netAI.IsUnderground())
            {
                endPoint.m_elevation = -endPoint.m_elevation;
            }
            endPoint.m_outside = ((instance.m_nodes.m_buffer[(int)endPoint.m_node].m_flags & NetNode.Flags.Outside) != NetNode.Flags.None);
            middlePoint.m_node = 0;
            middlePoint.m_segment = num2;
            middlePoint.m_position = startPoint.m_position + startPoint.m_direction * (info.GetMinNodeDistance() + 1f);
            middlePoint.m_direction = startPoint.m_direction;
            middlePoint.m_elevation = Mathf.Lerp(startPoint.m_elevation, endPoint.m_elevation, 0.5f);
            middlePoint.m_outside = false;
            return true;
        }
        return false;
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
                        segment.GetClosestPositionAndDirection((Vector3)((position + node3.m_position) * 0.5f), out vector4, out vector5);
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

    //Unmodified
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

    //Modified
    protected override void OnToolGUI(UnityEngine.Event e)
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
        else if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Tab)
        {
            switch (currentBuildMode)
            {
                case CurrentBuildMode.Normal:
                    currentBuildMode = CurrentBuildMode.Ground;
                    break;
                case CurrentBuildMode.Ground:
                    currentBuildMode = CurrentBuildMode.Elevated;
                    break;
                case CurrentBuildMode.Elevated:
                    currentBuildMode = CurrentBuildMode.Bridge;
                    break;
                case CurrentBuildMode.Bridge:
                    currentBuildMode = CurrentBuildMode.Normal;
                    break;
                default:
                    break;
            }
        }
        else if (current.type == EventType.KeyDown && current.keyCode == KeyCode.UpArrow && current.control)
        {
            TerrainStep++;
        }
        else if (current.type == EventType.KeyDown && current.keyCode == KeyCode.DownArrow && current.control)
        {
            TerrainStep--;
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

    //Unmodified
    protected override void OnToolLateUpdate()
    {
        NetInfo prefab = this.m_prefab;
        if (prefab == null)
        {
            return;
        }
        Vector3 mousePosition = Input.mousePosition;
        this.m_mouseRay = Camera.main.ScreenPointToRay(mousePosition);
        this.m_mouseRayLength = Camera.main.farClipPlane;
        this.m_mouseRayValid = (!this.m_toolController.IsInsideUI && Cursor.visible);
        if (this.m_lengthTimer > 0f)
        {
            this.m_lengthTimer = Mathf.Max(0f, this.m_lengthTimer - Time.deltaTime);
        }
        InfoManager.InfoMode mode;
        InfoManager.SubInfoMode subMode;
        prefab.m_netAI.GetPlacementInfoMode(out mode, out subMode, this.GetElevation(prefab));
        base.ForceInfoMode(mode, subMode);
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
                    base.ShowToolInfo(true, str + "\n" + StatusText, position);
                }
                else
                {
                    base.ShowToolInfo(true, StatusText, position);
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
                CreateNode(prefab, startPoint, startPoint, startPoint, NetTool.m_nodePositionsMain, 0, false, true, true, false, false, false, 0, out num, out num2, out num3, out num4);
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
                CreateNode(prefab, point2, point2, point2, NetTool.m_nodePositionsMain, 0, false, true, true, false, false, false, 0, out num5, out num6, out num7, out num8);
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
                    point4.m_position = (Vector3)((this.m_cachedControlPoints[0].m_position + this.m_cachedControlPoints[1].m_position) * 0.5f);
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
                CreateNode(prefab, point3, point4, point5, NetTool.m_nodePositionsMain, 0x3e8, false, true, true, false, false, false, 0, out num9, out num10, out num11, out num12);
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
            Vector3 vector = (Vector3)(new Vector3(direction.z, 0f, -direction.x) * info.m_halfWidth);
            Vector3 startPos = position + vector;
            Vector3 vector3 = position - vector;
            Vector3 endPos = vector3;
            Vector3 vector5 = startPos;
            float num2 = Mathf.Min((float)(info.m_halfWidth * 1.333333f), (float)16f);
            Vector3 vector6 = startPos - ((Vector3)(direction * num2));
            Vector3 vector7 = endPos - ((Vector3)(direction * num2));
            Vector3 vector8 = vector3 - ((Vector3)(direction * num2));
            Vector3 vector9 = vector5 - ((Vector3)(direction * num2));
            Vector3 vector10 = startPos + ((Vector3)(direction * num2));
            Vector3 vector11 = endPos + ((Vector3)(direction * num2));
            Vector3 vector12 = vector3 + ((Vector3)(direction * num2));
            Vector3 vector13 = vector5 + ((Vector3)(direction * num2));
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
                NetTool.RenderOverlay(cameraInfo, ref instance.m_segments.m_buffer[index], toolColor, toolColor);
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
                            Vector3 vector8 = vector5 + ((Vector3)(vector6 * (lengthSnap * j)));
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
            Vector3 transform = (Vector3)((startPosition + endPosition) * 0.5f);
            Quaternion identity = Quaternion.identity;
            Vector3 vector2 = (Vector3)(new Vector3(startDirection.z, 0f, -startDirection.x) * info.m_halfWidth);
            Vector3 startPos = startPosition - vector2;
            Vector3 vector4 = startPosition + vector2;
            vector2 = (Vector3)(new Vector3(endDirection.z, 0f, -endDirection.x) * info.m_halfWidth);
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
        if (prefab == null)
        {
            return;
        }
        if (this.m_mode == NetTool.Mode.Straight)
        {
            if (prefab.m_class.m_service == ItemClass.Service.Road || prefab.m_class.m_service == ItemClass.Service.PublicTransport || prefab.m_class.m_service == ItemClass.Service.Beautification)
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
            if (optionsNotUsed != null && !optionsNotUsed.m_disabled)
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
                GuideController properties2 = Singleton<GuideManager>.instance.m_properties;
                if (properties2 != null)
                {
                    Singleton<NetManager>.instance.m_elevationNotUsed.Activate(properties2.m_elevationNotUsed, prefab.m_class.m_service);
                }
            }
        }
        else
        {
            ServiceTypeGuide elevationNotUsed = Singleton<NetManager>.instance.m_elevationNotUsed;
            if (elevationNotUsed != null && !elevationNotUsed.m_disabled)
            {
                elevationNotUsed.Disable();
            }
        }
        if ((prefab.m_hasForwardVehicleLanes || prefab.m_hasBackwardVehicleLanes) && (!prefab.m_hasForwardVehicleLanes || !prefab.m_hasBackwardVehicleLanes) && (prefab.m_class.m_service == ItemClass.Service.Road || prefab.m_class.m_service == ItemClass.Service.PublicTransport || prefab.m_class.m_service == ItemClass.Service.Beautification) && this.m_controlPointCount >= 1)
        {
            GuideController properties3 = Singleton<GuideManager>.instance.m_properties;
            if (properties3 != null)
            {
                Singleton<NetManager>.instance.m_onewayRoadPlacement.Activate(properties3.m_onewayRoadPlacement);
            }
        }
        if (this.m_mode == NetTool.Mode.Upgrade)
        {
            ServiceTypeGuide manualUpgrade = Singleton<NetManager>.instance.m_manualUpgrade;
            manualUpgrade.Deactivate();
        }
        Vector3 position = this.m_controlPoints[this.m_controlPointCount].m_position;
        bool flag = false;
        if (this.m_mode == NetTool.Mode.Upgrade)
        {
            NetManager instance = Singleton<NetManager>.instance;
            ToolBase.RaycastInput input = new ToolBase.RaycastInput(this.m_mouseRay, this.m_mouseRayLength);
            input.m_netService = new ToolBase.RaycastService(prefab.m_class.m_service, prefab.m_class.m_subService, prefab.m_class.m_layer);
            input.m_ignoreTerrain = true;
            input.m_ignoreNodeFlags = NetNode.Flags.All;
            input.m_ignoreSegmentFlags = NetSegment.Flags.Untouchable;
            if (Singleton<InfoManager>.instance.CurrentMode == InfoManager.InfoMode.Transport || Singleton<InfoManager>.instance.CurrentMode == InfoManager.InfoMode.Traffic)
            {
                input.m_netService.m_itemLayers = (input.m_netService.m_itemLayers | ItemClass.Layer.MetroTunnels);
            }
            ToolBase.RaycastOutput raycastOutput;
            if (this.m_mouseRayValid && ToolBase.RayCast(input, out raycastOutput))
            {
                if (raycastOutput.m_netSegment != 0)
                {
                    NetInfo info = instance.m_segments.m_buffer[(int)raycastOutput.m_netSegment].Info;
                    if (info.m_class.m_service != prefab.m_class.m_service || info.m_class.m_subService != prefab.m_class.m_subService)
                    {
                        raycastOutput.m_netSegment = 0;
                    }
                    else if (this.m_upgradedSegments.Contains(raycastOutput.m_netSegment))
                    {
                        raycastOutput.m_netSegment = 0;
                    }
                }
                if (raycastOutput.m_netSegment != 0)
                {
                    NetTool.ControlPoint controlPoint;
                    controlPoint.m_node = instance.m_segments.m_buffer[(int)raycastOutput.m_netSegment].m_startNode;
                    controlPoint.m_segment = 0;
                    controlPoint.m_position = instance.m_nodes.m_buffer[(int)controlPoint.m_node].m_position;
                    controlPoint.m_direction = instance.m_segments.m_buffer[(int)raycastOutput.m_netSegment].m_startDirection;
                    controlPoint.m_elevation = (float)instance.m_nodes.m_buffer[(int)controlPoint.m_node].m_elevation;
                    if (instance.m_nodes.m_buffer[(int)controlPoint.m_node].Info.m_netAI.IsUnderground())
                    {
                        controlPoint.m_elevation = -controlPoint.m_elevation;
                    }
                    controlPoint.m_outside = ((instance.m_nodes.m_buffer[(int)controlPoint.m_node].m_flags & NetNode.Flags.Outside) != NetNode.Flags.None);
                    NetTool.ControlPoint controlPoint2;
                    controlPoint2.m_node = instance.m_segments.m_buffer[(int)raycastOutput.m_netSegment].m_endNode;
                    controlPoint2.m_segment = 0;
                    controlPoint2.m_position = instance.m_nodes.m_buffer[(int)controlPoint2.m_node].m_position;
                    controlPoint2.m_direction = -instance.m_segments.m_buffer[(int)raycastOutput.m_netSegment].m_endDirection;
                    controlPoint2.m_elevation = (float)instance.m_nodes.m_buffer[(int)controlPoint2.m_node].m_elevation;
                    if (instance.m_nodes.m_buffer[(int)controlPoint2.m_node].Info.m_netAI.IsUnderground())
                    {
                        controlPoint2.m_elevation = -controlPoint2.m_elevation;
                    }
                    controlPoint2.m_outside = ((instance.m_nodes.m_buffer[(int)controlPoint2.m_node].m_flags & NetNode.Flags.Outside) != NetNode.Flags.None);
                    NetTool.ControlPoint controlPoint3;
                    controlPoint3.m_node = 0;
                    controlPoint3.m_segment = raycastOutput.m_netSegment;
                    controlPoint3.m_position = controlPoint.m_position + controlPoint.m_direction * (prefab.GetMinNodeDistance() + 1f);
                    controlPoint3.m_direction = controlPoint.m_direction;
                    controlPoint3.m_elevation = Mathf.Lerp(controlPoint.m_elevation, controlPoint2.m_elevation, 0.5f);
                    controlPoint3.m_outside = false;
                    this.m_controlPoints[0] = controlPoint;
                    this.m_controlPoints[1] = controlPoint3;
                    this.m_controlPoints[2] = controlPoint2;
                    this.m_controlPointCount = 2;
                }
                else
                {
                    this.m_controlPointCount = 0;
                    this.m_controlPoints[this.m_controlPointCount] = default(NetTool.ControlPoint);
                }
            }
            else
            {
                this.m_controlPointCount = 0;
                this.m_controlPoints[this.m_controlPointCount] = default(NetTool.ControlPoint);
                flag = true;
            }
        }
        else
        {
            NetTool.ControlPoint controlPoint4 = default(NetTool.ControlPoint);
            float elevation = this.GetElevation(prefab);
            NetNode.Flags ignoreNodeFlags;
            NetSegment.Flags ignoreSegmentFlags;
            if ((this.m_mode == NetTool.Mode.Curved || this.m_mode == NetTool.Mode.Freeform) && this.m_controlPointCount == 1)
            {
                ignoreNodeFlags = NetNode.Flags.All;
                ignoreSegmentFlags = NetSegment.Flags.All;
            }
            else
            {
                ignoreNodeFlags = NetNode.Flags.None;
                ignoreSegmentFlags = NetSegment.Flags.None;
            }
            Building.Flags ignoreBuildingFlags;
            if (prefab.m_snapBuildingNodes)
            {
                ignoreBuildingFlags = Building.Flags.Untouchable;
            }
            else
            {
                ignoreBuildingFlags = Building.Flags.All;
            }
            bool tunnels = Singleton<InfoManager>.instance.CurrentMode == InfoManager.InfoMode.Transport || Singleton<InfoManager>.instance.CurrentMode == InfoManager.InfoMode.Traffic;
            if (this.m_mouseRayValid && NetTool.MakeControlPoint(this.m_mouseRay, this.m_mouseRayLength, prefab, false, ignoreNodeFlags, ignoreSegmentFlags, ignoreBuildingFlags, elevation, tunnels, out controlPoint4))
            {
                bool flag2 = false;
                if (controlPoint4.m_node == 0 && controlPoint4.m_segment == 0 && !controlPoint4.m_outside)
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
                            this.Snap(this.m_prefab, ref controlPoint4.m_position, ref controlPoint4.m_direction, zero, 0f);
                            flag2 = true;
                        }
                        else
                        {
                            Singleton<NetManager>.instance.GetClosestSegments(controlPoint4.m_position, this.m_closeSegments, out this.m_closeSegmentCount);
                            controlPoint4.m_direction = Vector3.zero;
                            float num3 = 256f;
                            ushort num4 = 0;
                            for (int i = 0; i < this.m_closeSegmentCount; i++)
                            {
                                Singleton<NetManager>.instance.m_segments.m_buffer[(int)this.m_closeSegments[i]].GetClosestZoneBlock(controlPoint4.m_position, ref num3, ref num4);
                            }
                            if (num4 != 0)
                            {
                                ZoneBlock zoneBlock = Singleton<ZoneManager>.instance.m_blocks.m_buffer[(int)num4];
                                this.Snap(this.m_prefab, ref controlPoint4.m_position, ref controlPoint4.m_direction, zoneBlock.m_position, zoneBlock.m_angle);
                                flag2 = true;
                            }
                        }
                    }
                    controlPoint4.m_position.y = NetSegment.SampleTerrainHeight(prefab, controlPoint4.m_position, false, controlPoint4.m_elevation);
                }
                else
                {
                    flag2 = true;
                }
                bool flag3 = false;
                if (this.m_controlPointCount == 2 && this.m_mode == NetTool.Mode.Freeform)
                {
                    Vector3 vector = controlPoint4.m_position - this.m_controlPoints[0].m_position;
                    Vector3 direction = this.m_controlPoints[1].m_direction;
                    vector.y = 0f;
                    direction.y = 0f;
                    float num5 = Vector3.SqrMagnitude(vector);
                    vector = Vector3.Normalize(vector);
                    float num6 = Mathf.Min(1.17809725f, Mathf.Acos(Vector3.Dot(vector, direction)));
                    float d = Mathf.Sqrt(0.5f * num5 / Mathf.Max(0.001f, 1f - Mathf.Cos(3.14159274f - 2f * num6)));
                    this.m_controlPoints[1].m_position = this.m_controlPoints[0].m_position + direction * d;
                    controlPoint4.m_direction = controlPoint4.m_position - this.m_controlPoints[1].m_position;
                    controlPoint4.m_direction.y = 0f;
                    controlPoint4.m_direction.Normalize();
                }
                else if (this.m_controlPointCount != 0)
                {
                    NetTool.ControlPoint oldPoint = this.m_controlPoints[this.m_controlPointCount - 1];
                    controlPoint4.m_direction = controlPoint4.m_position - oldPoint.m_position;
                    controlPoint4.m_direction.y = 0f;
                    controlPoint4.m_direction.Normalize();
                    float num7 = prefab.GetMinNodeDistance();
                    num7 *= num7;
                    float num8 = num7;
                    NetTool.ControlPoint controlPoint5;
                    if (this.m_snap)
                    {
                        controlPoint5 = NetTool.SnapDirection(controlPoint4, oldPoint, prefab, out flag3, out num7);
                        controlPoint4 = controlPoint5;
                    }
                    else
                    {
                        controlPoint5 = controlPoint4;
                    }
                    if (controlPoint4.m_segment != 0 && num7 < num8)
                    {
                        NetSegment netSegment = Singleton<NetManager>.instance.m_segments.m_buffer[(int)controlPoint4.m_segment];
                        controlPoint5.m_position = netSegment.GetClosestPosition(controlPoint4.m_position, controlPoint4.m_direction);
                    }
                    else if (controlPoint4.m_segment == 0 && controlPoint4.m_node == 0 && !controlPoint4.m_outside && this.m_snap)
                    {
                        float lengthSnap = prefab.m_netAI.GetLengthSnap();
                        if (this.m_mode != NetTool.Mode.Freeform && (flag3 || !flag2) && lengthSnap != 0f)
                        {
                            Vector3 a = controlPoint4.m_position - oldPoint.m_position;
                            Vector3 vector2 = new Vector3(a.x, 0f, a.z);
                            float magnitude = vector2.magnitude;
                            if (magnitude < 0.001f)
                            {
                                controlPoint5.m_position = oldPoint.m_position;
                            }
                            else
                            {
                                int num9 = Mathf.Max(1, Mathf.RoundToInt(magnitude / lengthSnap));
                                controlPoint5.m_position = oldPoint.m_position + a * ((float)num9 * lengthSnap / magnitude);
                            }
                        }
                    }
                    controlPoint4 = controlPoint5;
                }
            }
            else
            {
                flag = true;
            }
            this.m_controlPoints[this.m_controlPointCount] = controlPoint4;
        }
        bool flag4 = (Singleton<ToolManager>.instance.m_properties.m_mode & ItemClass.Availability.Game) != ItemClass.Availability.None;
        int num12;
        int productionRate;
        ToolBase.ToolErrors toolErrors;
        if (this.m_controlPointCount == 2)
        {
            if (Vector3.SqrMagnitude(position - this.m_controlPoints[this.m_controlPointCount].m_position) > 1f)
            {
                this.m_lengthChanging = true;
            }
            NetTool.ControlPoint startPoint = this.m_controlPoints[this.m_controlPointCount - 2];
            NetTool.ControlPoint middlePoint = this.m_controlPoints[this.m_controlPointCount - 1];
            NetTool.ControlPoint endPoint = this.m_controlPoints[this.m_controlPointCount];
            ushort num10;
            ushort num11;
            toolErrors = CreateNode(prefab, startPoint, middlePoint, endPoint, NetTool.m_nodePositionsSimulation, 1000, true, false, true, flag4, false, this.m_switchingDir, 0, out num10, out num11, out num12, out productionRate);
            if (GetSecondaryControlPoints(prefab, ref startPoint, ref middlePoint, ref endPoint))
            {
                int num13;
                toolErrors |= CreateNode(prefab, startPoint, middlePoint, endPoint, NetTool.m_nodePositionsSimulation, 1000, true, false, true, flag4, false, this.m_switchingDir, 0, out num10, out num11, out num13, out productionRate);
                num12 += num13;
            }
        }
        else if (this.m_controlPointCount == 1)
        {
            if (Vector3.SqrMagnitude(position - this.m_controlPoints[this.m_controlPointCount].m_position) > 1f)
            {
                this.m_lengthChanging = true;
            }
            NetTool.ControlPoint middlePoint2 = this.m_controlPoints[1];
            if ((this.m_mode != NetTool.Mode.Curved && this.m_mode != NetTool.Mode.Freeform) || middlePoint2.m_node != 0 || middlePoint2.m_segment != 0)
            {
                middlePoint2.m_node = 0;
                middlePoint2.m_segment = 0;
                middlePoint2.m_position = (this.m_controlPoints[0].m_position + this.m_controlPoints[1].m_position) * 0.5f;
                ushort num14;
                ushort num15;
                toolErrors = CreateNode(prefab, this.m_controlPoints[this.m_controlPointCount - 1], middlePoint2, this.m_controlPoints[this.m_controlPointCount], NetTool.m_nodePositionsSimulation, 1000, true, false, true, flag4, false, this.m_switchingDir, 0, out num14, out num15, out num12, out productionRate);
            }
            else
            {
                this.m_toolController.ClearColliding();
                toolErrors = ToolBase.ToolErrors.None;
                num12 = 0;
                productionRate = 0;
            }
        }
        else
        {
            this.m_toolController.ClearColliding();
            toolErrors = ToolBase.ToolErrors.None;
            num12 = 0;
            productionRate = 0;
        }
        if (flag)
        {
            toolErrors |= ToolBase.ToolErrors.RaycastFailed;
        }
        while (!Monitor.TryEnter(this.m_cacheLock, SimulationManager.SYNCHRONIZE_TIMEOUT))
        {
        }
        try
        {
            this.m_buildErrors = toolErrors;
            this.m_constructionCost = ((!flag4) ? 0 : num12);
            this.m_productionRate = productionRate;
        }
        finally
        {
            Monitor.Exit(this.m_cacheLock);
        }
        if (this.m_mode == NetTool.Mode.Upgrade && this.m_upgrading && this.m_controlPointCount == 2 && this.m_buildErrors == ToolBase.ToolErrors.None)
        {
            this.CreateNodeImpl(this.m_switchingDir);
        }
    }

    private void Snap(NetInfo info, ref Vector3 point, ref Vector3 direction, Vector3 refPoint, float refAngle)
    {
        direction = new Vector3(Mathf.Cos(refAngle), 0f, Mathf.Sin(refAngle));
        Vector3 vector = (Vector3)(direction * 8f);
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
            Singleton<NetManager>.instance.m_nodes.m_buffer[node].m_elevation = (byte)((node2.m_elevation + node3.m_elevation) / 2);
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
        Vector2 vector = (Vector2)(new Vector2(direction.x, direction.z) * ((info.m_cellLength * 4f) - 0.8f));
        Vector2 vector2 = (Vector2)(new Vector2(direction.z, -direction.x) * ((info.m_cellWidth * 4f) - 0.8f));
        if (info.m_circular)
        {
            vector2 = (Vector2)(vector2 * 0.7f);
            vector = (Vector2)(vector * 0.7f);
        }
        ItemClass.CollisionType collisionType = ItemClass.CollisionType.Terrain;
        if (info.m_class.m_layer == ItemClass.Layer.WaterPipes)
            collisionType = ItemClass.CollisionType.Underground;
        Vector2 vector3 = VectorUtils.XZ(position);
        Quad2 quad = new Quad2
        {
            a = (vector3 - vector2) - vector,
            b = (vector3 - vector2) + vector,
            c = (vector3 + vector2) + vector,
            d = (vector3 + vector2) - vector
        };
        ToolBase.ToolErrors none = ToolBase.ToolErrors.None;
        float minY = Mathf.Min(position.y, Singleton<TerrainManager>.instance.SampleRawHeightSmooth(position));
        float maxY = position.y + info.m_generatedInfo.m_size.y;
        Singleton<NetManager>.instance.OverlapQuad(quad, minY, maxY, collisionType, info.m_class.m_layer, ignoreNode, 0, ignoreSegment, collidingSegmentBuffer);
        Singleton<BuildingManager>.instance.OverlapQuad(quad, minY, maxY, collisionType, info.m_class.m_layer, ignoreBuilding, ignoreNode, 0, collidingBuildingBuffer);
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
            uint num = (uint)this.SPC;
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
            uint num = (uint)this.SPC;
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
            uint num = (uint)this.SPC;
            this.SPC = -1;
            switch (num)
            {
                case 0:
                    this.result = false;
                    this.info = this.f_this.m_prefab;
                    if (this.info != null)
                    {
                        GetAdjustedElevationLimits(this.info.m_netAI, out this.min_height, out this.max_height);
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
            uint num = (uint)this.SPC;
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
            uint num = (uint)this.SPC;
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

