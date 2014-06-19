﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;

using Backend.ThreeAddressCode;
using Backend.Utils;

namespace Backend.Analysis
{
	public enum CFGNodeKind
	{
		Entry,
		Exit,
		BasicBlock
	}

	public class CFGLoop
	{
		public CFGNode Header { get; set; }
		public ISet<CFGNode> Body { get; private set; }

		public CFGLoop(CFGNode header)
		{
			this.Header = header;
			this.Body = new HashSet<CFGNode>();
			this.Body.Add(header);
		}

		public override string ToString()
		{
			var sb = new StringBuilder(" ");

			foreach (var node in this.Body)
				sb.AppendFormat("B{0} ", node.Id);

			return sb.ToString();
		}
	}

	public class CFGEdge
	{
		public CFGNode Source { get; set; }
		public CFGNode Target { get; set; }

		public CFGEdge(CFGNode source, CFGNode target)
		{
			this.Source = source;
			this.Target = target;
		}

		public override string ToString()
		{
			return string.Format("{0} -> {1}", this.Source, this.Target);
		}
	}

	public class CFGNode
	{
		private ISet<CFGNode> dominators;

		public int Id { get; private set; }
		public int ForwardIndex { get; set; }
		public int BackwardIndex { get; set; }
		public CFGNodeKind Kind { get; private set; }
		public ISet<CFGNode> Predecessors { get; private set; }
		public ISet<CFGNode> Successors { get; private set; }
		public IList<Instruction> Instructions { get; private set; }
		public CFGNode ImmediateDominator { get; set; }
		public ISet<CFGNode> Childs { get; private set; }
		public ISet<CFGNode> DominanceFrontier { get; private set; }

		public CFGNode(int id, CFGNodeKind kind)
		{
			this.Id = id;
			this.Kind = kind;
			this.ForwardIndex = -1;
			this.BackwardIndex = -1;
			this.Predecessors = new HashSet<CFGNode>();
			this.Successors = new HashSet<CFGNode>();
			this.Instructions = new List<Instruction>();
			this.Childs = new HashSet<CFGNode>();
			this.DominanceFrontier = new HashSet<CFGNode>();
		}

		public ISet<CFGNode> Dominators
		{
			get
			{
				if (this.dominators == null)
				{
					this.dominators = CFGNode.ComputeDominators(this);
				}

				return this.dominators;
			}
		}

		public override string ToString()
		{
			string result;

			switch (this.Kind)
			{
				case CFGNodeKind.Entry: result = "entry"; break;
				case CFGNodeKind.Exit: result = "exit"; break;
				default: result = string.Join("\n", this.Instructions); break;
			}

			return result;
		}

		private static ISet<CFGNode> ComputeDominators(CFGNode node)
		{
			var result = new HashSet<CFGNode>();

			do
			{
				result.Add(node);
				node = node.ImmediateDominator;
			}
			while (node != null);

			return result;
		}
	}

	public class ControlFlowGraph
	{
		private CFGNode[] forwardOrder;
		private CFGNode[] backwardOrder;

		public CFGNode Entry { get; private set; }
		public CFGNode Exit { get; private set; }
		public ISet<CFGNode> Nodes { get; private set; }
		public ISet<CFGLoop> Loops { get; private set; }

		public ControlFlowGraph()
		{
			this.Entry = new CFGNode(0, CFGNodeKind.Entry);
			this.Exit = new CFGNode(1, CFGNodeKind.Exit);
			this.Nodes = new HashSet<CFGNode>() { this.Entry, this.Exit };
		}

		public CFGNode[] ForwardOrder
		{
			get
			{
				if (this.forwardOrder == null)
				{
					this.forwardOrder = ControlFlowGraph.ComputeForwardTopologicalSort(this);
				}

				return this.forwardOrder;
			}
		}

		public CFGNode[] BackwardOrder
		{
			get
			{
				if (this.backwardOrder == null)
				{
					this.backwardOrder = ControlFlowGraph.ComputeBackwardTopologicalSort(this);
				}

				return this.backwardOrder;
			}
		}

		#region Generation

		public static ControlFlowGraph Generate(MethodBody method)
		{			
			var leaders = ControlFlowGraph.CreateNodes(method);
			var cfg = ControlFlowGraph.ConnectNodes(method, leaders);

			return cfg;
		}

		private static IDictionary<string, CFGNode> CreateNodes(MethodBody method)
		{
			var leaders = new Dictionary<string, CFGNode>();
			var nextIsLeader = true;
			var nodeKind = CFGNodeKind.BasicBlock;
			var nodeId = 2;

			foreach (var instruction in method.Instructions)
			{
				var isLeader = false;

				if (instruction is TryInstruction ||
					instruction is CatchInstruction ||
					instruction is FinallyInstruction ||
					nextIsLeader)
				{
					isLeader = true;
					nextIsLeader = false;
					nodeKind = CFGNodeKind.BasicBlock;
				}

				if (isLeader && !leaders.ContainsKey(instruction.Label))
				{
					var node = new CFGNode(nodeId++, nodeKind);
					leaders.Add(instruction.Label, node);
				}

				if (instruction is UnconditionalBranchInstruction ||
					instruction is ConditionalBranchInstruction)
				{
					nextIsLeader = true;
					var branch = instruction as IBranchInstruction;

					if (!leaders.ContainsKey(branch.Target))
					{
						var node = new CFGNode(nodeId++, CFGNodeKind.BasicBlock);
						leaders.Add(branch.Target, node);
					}
				}
				else if (instruction is ReturnInstruction)
				//TODO: || instruction is ThrowInstruction
				{
					nextIsLeader = true;
				}
			}

			return leaders;
		}

		private static ControlFlowGraph ConnectNodes(MethodBody method, IDictionary<string, CFGNode> leaders)
		{
			var cfg = new ControlFlowGraph();
			var connectWithPreviousNode = true;
			var current = cfg.Entry;
			CFGNode previous;

			foreach (var instruction in method.Instructions)
			{
				if (leaders.ContainsKey(instruction.Label))
				{
					previous = current;
					current = leaders[instruction.Label];

					if (connectWithPreviousNode)
					{
						cfg.ConnectNodes(previous, current);
					}
				}

				connectWithPreviousNode = true;
				current.Instructions.Add(instruction);

				if (instruction is IBranchInstruction)
				{
					var branch = instruction as IBranchInstruction;
					var target = leaders[branch.Target];

					cfg.ConnectNodes(current, target);
					connectWithPreviousNode = instruction is ConditionalBranchInstruction ||
											  instruction is ExceptionalBranchInstruction;
				}
				else if (instruction is ReturnInstruction)
				{
					//TODO: not always connect to exit, could exists a finally block
					cfg.ConnectNodes(current, cfg.Exit);
				}
			}

			cfg.ConnectNodes(current, cfg.Exit);
			return cfg;
		}

		#endregion

		#region Topological Sort

		private static CFGNode[] ComputeForwardTopologicalSort(ControlFlowGraph cfg)
		{
			var stack = new Stack<CFGNode>();
			var result = new CFGNode[cfg.Nodes.Count];
			var visited = new bool[cfg.Nodes.Count];
			var index = cfg.Nodes.Count - 1;

			stack.Push(cfg.Entry);

			do
			{
				var node = stack.Peek();

				if (!visited[node.Id])
				{
					visited[node.Id] = true;

					foreach (var succ in node.Successors)
					{
						if (!visited[succ.Id])
						{
							stack.Push(succ);
						}
					}
				}
				else
				{
					stack.Pop();
					node.ForwardIndex = index;
					result[index] = node;
					index--;
				}
			}
			while (stack.Count > 0);

			return result;
		}

		private static CFGNode[] ComputeBackwardTopologicalSort(ControlFlowGraph cfg)
		{
			var stack = new Stack<CFGNode>();
			var result = new CFGNode[cfg.Nodes.Count];
			var visited = new bool[cfg.Nodes.Count];
			var index = cfg.Nodes.Count - 1;

			stack.Push(cfg.Exit);

			do
			{
				var node = stack.Peek();

				if (!visited[node.Id])
				{
					visited[node.Id] = true;

					foreach (var pred in node.Predecessors)
					{
						if (!visited[pred.Id])
						{
							stack.Push(pred);
						}
					}
				}
				else
				{
					stack.Pop();
					node.BackwardIndex = index;
					result[index] = node;
					index--;
				}
			}
			while (stack.Count > 0);

			return result;
		}

		#endregion

		#region Dominance

		public static void ComputeDominators(ControlFlowGraph cfg)
		{
			bool changed;
			var sorted_nodes = cfg.ForwardOrder;

			cfg.Entry.ImmediateDominator = cfg.Entry;			

			do
			{
				changed = false;

				// Skip first node: entry
				for (var i = 1; i < sorted_nodes.Length; ++i)
				{
					var node = sorted_nodes[i];
					var new_idom = node.Predecessors.First();
					var predecessors = node.Predecessors.Skip(1);

					foreach (var pred in predecessors)
					{
						if (pred.ImmediateDominator != null)
						{
							new_idom = ControlFlowGraph.FindCommonParent(pred, new_idom);
						}
					}

					var old_idom = node.ImmediateDominator;
					var equals = old_idom != null && old_idom.Equals(new_idom);

					if (!equals)
					{
						node.ImmediateDominator = new_idom;
						changed = true;
					}
				}
			}
			while (changed);

			cfg.Entry.ImmediateDominator = null;
		}

		private static CFGNode FindCommonParent(CFGNode a, CFGNode b)
		{
			while (a.ForwardIndex != b.ForwardIndex)
			{
				while (a.ForwardIndex > b.ForwardIndex)
					a = a.ImmediateDominator;

				while (b.ForwardIndex > a.ForwardIndex)
					b = b.ImmediateDominator;
			}

			return a;
		}

		//public static void ComputeDominators(ControlFlowGraph cfg)
		//{
		//    bool changed;
		//    var sorted_nodes = cfg.ForwardOrder;
		//    var result = new Subset<CFGNode>[sorted_nodes.Length];

		//    var entry_dom = sorted_nodes.ToEmptySubset();
		//    entry_dom.Add(cfg.Entry.Id);
		//    result[cfg.Entry.Id] = entry_dom;

		//    // Skip first node: entry
		//    for (var i = 1; i < sorted_nodes.Length; ++i)
		//    {
		//        result[i] = sorted_nodes.ToSubset();
		//    }			

		//    do
		//    {
		//        changed = false;

		//        // Skip first node: entry
		//        for (var i = 1; i < sorted_nodes.Length; ++i)
		//        {
		//            var node = sorted_nodes[i];
		//            var old_dom = result[node.Id];
		//            var new_dom = sorted_nodes.ToSubset();

		//            foreach (var pred in node.Predecessors)
		//            {
		//                var pre_dom = result[pred.Id];
		//                new_dom.Intersect(pre_dom);
		//            }

		//            new_dom.Add(node.Id);
		//            var equals = old_dom.Equals(new_dom);

		//            if (!equals)
		//            {
		//                result[node.Id] = new_dom;
		//                changed = true;
		//            }
		//        }
		//    }
		//    while (changed);

		//    for (var i = 0; i < sorted_nodes.Length; ++i)
		//    {
		//        var node = sorted_nodes[i];
		//        var node_dom = result[node.Id];

		//        node_dom.ToSet(node.Dominators);
		//    }
		//}

		#endregion

		#region Dominator Tree
		
		public static void ComputeDominatorTree(ControlFlowGraph cfg)
		{
			foreach (var node in cfg.Nodes)
			{
				if (node.ImmediateDominator == null) continue;

				node.ImmediateDominator.Childs.Add(node);
			}
		}

		#endregion

		#region Dominance Frontier

		public static void ComputeDominanceFrontiers(ControlFlowGraph cfg)
		{
			foreach (var node in cfg.Nodes)
			{
				node.DominanceFrontier.Clear();
				if (node.Predecessors.Count < 2) continue;

				foreach (var pred in node.Predecessors)
				{
					var runner = pred;

					while (runner.Id != node.ImmediateDominator.Id)
					{
						runner.DominanceFrontier.Add(node);
						runner = runner.ImmediateDominator;
					}
				}
			}
		}

		#endregion

		#region Loops

		public static void IdentifyLoops(ControlFlowGraph cfg)
		{
			cfg.Loops = new HashSet<CFGLoop>();
			var back_edges = ControlFlowGraph.IdentifyBackEdges(cfg);

			foreach (var edge in back_edges)
			{
				var loop = ControlFlowGraph.IdentifyLoop(edge);
				cfg.Loops.Add(loop);
			}
		}

		private static ISet<CFGEdge> IdentifyBackEdges(ControlFlowGraph cfg)
		{
			var back_edges = new HashSet<CFGEdge>();

			foreach (var node in cfg.Nodes)
			{
				foreach (var succ in node.Successors)
				{
					if (node.Dominators.Contains(succ))
					{
						var edge = new CFGEdge(node, succ);
						back_edges.Add(edge);
					}
				}
			}

			return back_edges;
		}

		private static CFGLoop IdentifyLoop(CFGEdge back_edge)
		{			
			var loop = new CFGLoop(back_edge.Target);
			var nodes = new Stack<CFGNode>();

			nodes.Push(back_edge.Source);

			do
			{
				var node = nodes.Pop();
				var new_node = loop.Body.Add(node);

				if (new_node)
				{
					foreach (var pred in node.Predecessors)
						nodes.Push(pred);
				}
			}
			while (nodes.Count > 0);

			return loop;
		}

		#endregion

		public void ConnectNodes(CFGNode predecessor, CFGNode successor)
		{
			successor.Predecessors.Add(predecessor);
			predecessor.Successors.Add(successor);
			this.Nodes.Add(predecessor);
			this.Nodes.Add(successor);
		}
	}
}
