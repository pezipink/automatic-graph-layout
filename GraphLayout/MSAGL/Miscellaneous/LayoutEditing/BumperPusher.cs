/*
Microsoft Automatic Graph Layout,MSAGL 

Copyright (c) Microsoft Corporation

All rights reserved. 

MIT License 

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
""Software""), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Msagl.Core.DataStructures;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.DebugHelpers;

namespace Microsoft.Msagl.Miscellaneous.LayoutEditing {
    /// <summary>
    /// pushes the nodes it got bumped to: pushes horizontally or vertically
    /// </summary>
    public class BumperPusher {
        readonly double separation;
        readonly Set<Node> fixedNodes = new Set<Node>();
        readonly RTree<Node> rtree;
        Node[] pushingNodes;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pushedNodes">nodes that are being pushed</param>
        /// <param name="separation"></param>
        /// <param name="pushingNodes"></param>
        public BumperPusher(IEnumerable<Node> pushedNodes, double separation, Node[] pushingNodes) {
            this.separation = separation;
            rtree = new RTree<Node>(RectangleNode<Node>.CreateRectangleNodeOnEnumeration(
                pushedNodes.Select(n => new RectangleNode<Node>(n, GetPaddedBoxOfNode(n)))));

            //LayoutAlgorithmSettings.ShowDebugCurvesEnumeration(rtree.GetAllLeaves().Select(n=>new DebugCurve(n.BoundaryCurve)));
            this.pushingNodes = pushingNodes;
        }

        internal IEnumerable<Node> FixedNodes { get { return fixedNodes; } }

        Rectangle GetPaddedBoxOfNode(Node n) {
            var ret= n.BoundaryCurve.BoundingBox;
            ret.Pad(separation/2);
            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IList<Node> PushNodes() {
            fixedNodes.Clear();
            fixedNodes.InsertRange(pushingNodes);

            var q = new Queue<Node>(pushingNodes);
            var ret = new List<Node>();
            while (q.Count > 0) {
                var n = q.Dequeue();
                foreach (var node in PushByNodeAndReportPushedAsFixed(n)) {
                    q.Enqueue(node);
                    fixedNodes.Insert(node);
                    ret.Add(node);                    
                }
            }
            return ret;
        }

        IEnumerable<Node> PushByNodeAndReportPushedAsFixed(Node pushingNode) {
            var ret = new List<Node>();
            var pushingNodeBox = GetPaddedBoxOfNode(pushingNode);
            foreach (var rectNode in rtree.GetAllLeavesIntersectingRectangle(pushingNodeBox)) {
                if (fixedNodes.Contains(rectNode.UserData)) continue;
                if (PushNodeAndUpdateRTree(pushingNode, rectNode))
                    ret.Add(rectNode.UserData);
            }
            return ret;
        }

        bool PushNodeAndUpdateRTree(Node pushingNode, RectangleNode<Node> pushed) {
            var del = pushed.UserData.Center - pushingNode.Center;
            var w = pushingNode.Width / 2 + pushed.UserData.Width / 2;
            var h = pushingNode.Height / 2 + pushed.UserData.Height / 2;
            var absDelXBetweenCenters = Math.Abs(del.X);
            var absDelYBetweenCenters = Math.Abs(del.Y);

            var xSep = absDelXBetweenCenters - w;
            var ySep = absDelYBetweenCenters - h;
            if (xSep >= separation || ySep >= separation)
                return false;
            if (absDelXBetweenCenters >= absDelYBetweenCenters) {
                double d = del.X > 0 ? separation - xSep : xSep - separation;
                PushByX(d, pushed);
            }
            else {
                double d = del.Y > 0 ? separation - ySep : ySep - separation;
                PushByY(d, pushed);
            }
            UpdateBoundingBoxesOfPushedAndUpParents(pushed);
            return true;
        }
        

        void PushByX(double del, RectangleNode<Node> pushed) {
            var delPoint = new Point(del, 0);
            PushByPoint(pushed, delPoint);
        }

        static void PushByPoint(RectangleNode<Node> pushed, Point delPoint) {
            pushed.UserData.Center += delPoint;
            var cluster = pushed.UserData as Cluster;
            if (cluster != null)
                cluster.DeepContentsTranslation(delPoint, true);
        }

        void PushByY(double del, RectangleNode<Node> pushed) {
            var delPoint = new Point(0, del);
            PushByPoint(pushed, delPoint);
        }

        void UpdateBoundingBoxesOfPushedAndUpParents(RectangleNode<Node> pushed) {
            pushed.Rectangle = GetPaddedBoxOfNode(pushed.UserData);
            var parent = pushed.Parent;
            while (parent != null) {
                parent.Rectangle = new Rectangle(parent.Left.Rectangle, parent.Right.Rectangle);
                parent = parent.Parent;
            }

        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="cluster"></param>
        /// <param name="previousBox"></param>
        public void UpdateRTreeByChangedNodeBox(Node cluster, Rectangle previousBox) {
            RectangleNode<Node> rectNode = FindClusterNode(cluster, previousBox);
            UpdateBoundingBoxesOfPushedAndUpParents(rectNode);
        }

        RectangleNode<Node> FindClusterNode(Node cluster, Rectangle previousBox) {
            var node = rtree.RootNode;
            return FindClusterNodeRecurse(node, cluster, previousBox);
        }

        RectangleNode<Node> FindClusterNodeRecurse(RectangleNode<Node> node, Node cluster, Rectangle previousBox) {
            if (node.UserData != null)
                return node.UserData == cluster ? node : null;
            
            RectangleNode<Node> n0=null;
            if (previousBox.Intersects(node.Left.Rectangle))
                n0 = FindClusterNodeRecurse(node.Left, cluster, previousBox);
            if (n0 != null) return n0;
            if (previousBox.Intersects(node.Right.Rectangle))
                return FindClusterNodeRecurse(node.Right, cluster, previousBox);
            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Node FirstPushingNode() {
            return pushingNodes[0];
        }
    }
}