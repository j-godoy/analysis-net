﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Backend.Analysis
{
	public class DataFlowAnalysisResult<T>
	{
		public T Input { get; set; }
		public T Output { get; set; }
	}

	public abstract class DataFlowAnalysis<T>
	{
		protected ControlFlowGraph cfg;
		protected DataFlowAnalysisResult<T>[] result;

		public abstract void Analyze();

		protected abstract bool CompareValues(T left, T right);

		protected abstract T InitialValue(CFGNode node);

		protected abstract T DefaultValue(CFGNode node);

		protected abstract T MergeValues(T left, T right);

		protected abstract T Flow(CFGNode node, T input);
	}

	public abstract class ForwardDataFlowAnalysis<T> : DataFlowAnalysis<T>
	{
		public override void Analyze()
		{
			bool changed;
			var sorted_nodes = this.cfg.ForwardOrder;

			this.result = new DataFlowAnalysisResult<T>[sorted_nodes.Length];

			var entry_result = new DataFlowAnalysisResult<T>();
			entry_result.Output = this.InitialValue(cfg.Entry);
			this.result[cfg.Entry.Id] = entry_result;

			// Skip first node: entry
			for (var i = 1; i < sorted_nodes.Length; ++i)
			{
				var node_result = new DataFlowAnalysisResult<T>();
				var node = sorted_nodes[i];

				node_result.Output = this.DefaultValue(node);
				this.result[node.Id]  = node_result;
			}

			do
			{
				changed = false;

				// Skip first node: entry
				for (var i = 1; i < sorted_nodes.Length; ++i)
				{
					var node = sorted_nodes[i];
					var node_result = this.result[node.Id];

					var other_predecessors = node.Predecessors.Skip(1);
					var first_pred = node.Predecessors.First();
					var pred_result = this.result[first_pred.Id];
					var node_input = pred_result.Output;

					foreach (var pred in other_predecessors)
					{
						pred_result = this.result[pred.Id];
						node_input = this.MergeValues(node_input, pred_result.Output);
					}

					node_result.Input = node_input;

					var old_output = node_result.Output;
					var new_output = this.Flow(node, node_input);
					var equals = this.CompareValues(new_output, old_output);

					if (!equals)
					{
						node_result.Output = new_output;
						changed = true;
					}
				}
			}
			while (changed);
		}
	}

	public abstract class BackwardDataFlowAnalysis<T> : DataFlowAnalysis<T>
	{
		public override void Analyze()
		{
			bool changed;
			var sorted_nodes = this.cfg.BackwardOrder;

			this.result = new DataFlowAnalysisResult<T>[sorted_nodes.Length];

			var exit_result = new DataFlowAnalysisResult<T>();
			exit_result.Input = this.InitialValue(cfg.Exit);
			this.result[cfg.Exit.Id] = exit_result;

			// Skip first node: exit
			for (var i = 1; i < sorted_nodes.Length; ++i)
			{
				var node_result = new DataFlowAnalysisResult<T>();
				var node = sorted_nodes[i];

				node_result.Input = this.DefaultValue(node);
				this.result[node.Id] = node_result;
			}

			do
			{
				changed = false;

				// Skip first node: exit
				for (var i = 1; i < sorted_nodes.Length; ++i)
				{
					var node = sorted_nodes[i];
					var node_result = this.result[node.Id];

					var other_successors = node.Successors.Skip(1);
					var first_succ = node.Successors.First();
					var succ_result = this.result[first_succ.Id];
					var node_output = succ_result.Output;

					foreach (var succ in other_successors)
					{
						succ_result = this.result[succ.Id];
						node_output = this.MergeValues(node_output, succ_result.Input);
					}

					node_result.Output = node_output;

					var old_input = node_result.Input;
					var new_input = this.Flow(node, node_output);
					var equals = new_input.Equals(old_input);

					if (!equals)
					{
						node_result.Input = new_input;
						changed = true;
					}
				}
			}
			while (changed);
		}
	}
}
