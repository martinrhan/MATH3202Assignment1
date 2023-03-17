using Gurobi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MATH3202Assignment1.Common;

namespace MATH3202Assignment1 {
    internal class Model {
        private Model(Node[] nodes, double[] demands) {
            Nodes = nodes;
            Demands = demands;
            List<int>[] outPipelines = new List<int>[nodes.Length];
            List<int>[] inPipelines = new List<int>[nodes.Length];
            for (int i_node = 0; i_node < nodes.Length; i_node++) {
                outPipelines[i_node] = new();
                inPipelines[i_node] = new();
            }
            for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                ref Pipeline pipeline = ref Pipelines[i_pipeline];
                double x0 = nodes[pipeline.Node0].X;
                double y0 = nodes[pipeline.Node0].Y;
                double x1 = nodes[pipeline.Node1].X;
                double y1 = nodes[pipeline.Node1].Y;
                double dx = x0 - x1;
                double dy = y0 - y1;
                pipeline.Length = Math.Sqrt(dx * dx + dy * dy);
                outPipelines[pipeline.Node0].Add(i_pipeline);
                inPipelines[pipeline.Node1].Add(i_pipeline);
            }
            for (int i_node = 0; i_node < nodes.Length; i_node++) {
                nodes[i_node].OutPipelines = outPipelines[i_node].ToArray();
                nodes[i_node].InPipelines = inPipelines[i_node].ToArray();
            }
        }

        internal static Model FromFile1() {
            IEnumerable<string[]> lines = ReadLines("nodes.csv").Select(str => str.Split(',')).ToArray();
            var nodes = lines.Select(strs => new Node() { X = double.Parse(strs[1]), Y = double.Parse(strs[2]) }).ToArray();
            var demands = lines.Select(strs => double.Parse(strs[3])).ToArray();
            return new Model(nodes, demands);
        }

        internal static Model[] FromFile2() {
            IEnumerable<string[]> lines = ReadLines("nodes2.csv").Select(str => str.Split(',')).ToArray();
            var nodes = lines.Select(strs => new Node() { X = double.Parse(strs[1]), Y = double.Parse(strs[2]) }).ToArray();
            var demands = lines.SelectMany(strs => strs[3..]).Select(double.Parse).ToArray();
            IEnumerable<double> getDemandsOfDay(int day) {
                int i = day;
                while (i < demands.Length) {
                    yield return demands[i];
                    i += 14;
                }
            }
            return Enumerable.Range(0, 14).Select(day => new Model(nodes, getDemandsOfDay(day).ToArray())).ToArray();
        }

        Node[] Nodes { get; }
        double[] Demands { get; }

        internal double FindOptimal(bool pipelineLimit = true, bool log = true) {
            GRBModel model = new GRBModel(Env);
            GRBVar[] variables = new GRBVar[Pipelines.Length];
            GRBLinExpr totalCost = new();
            for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                ref Pipeline pipeline = ref Pipelines[i_pipeline];
                variables[i_pipeline] = model.AddVar(0, pipelineLimit ? 313 : double.PositiveInfinity, default, GRB.CONTINUOUS, $"Pipeline{pipeline}:{pipeline.Node0}->{pipeline.Node1}");
                totalCost.AddTerm(0.01 * Pipelines[i_pipeline].Length, variables[i_pipeline]);
            }
            for (int i_node = 0; i_node < Nodes.Length; i_node++) {
                ref Node node = ref Nodes[i_node];
                GRBLinExpr netInflow = new();
                foreach (int i_pipeline in node.InPipelines) {
                    netInflow.AddTerm(1, variables[i_pipeline]);
                }
                foreach (int i_pipeline in node.OutPipelines) {
                    netInflow.AddTerm(-1, variables[i_pipeline]);
                }
                Supplier supplier;
                try {
                    supplier = Suppliers.First(s => s.Node == i_node);
                    model.AddConstr(Demands[i_node] - netInflow <= supplier.Capacity, "CapacityConstraint");
                    totalCost.Add((Demands[i_node] - netInflow) * supplier.Cost);
                } catch (InvalidOperationException) {
                    model.AddConstr(netInflow >= Demands[i_node], "DemandConstraint");
                }
            }
            model.SetObjective(totalCost, GRB.MINIMIZE);
            model.Optimize();

            if (log) {
                string format = "{0,-5}{1,-8}{2,-8}{3,-20}{4,-20}{5,-20}";
                Console.WriteLine(format, "Node", "|Demand", "|Actual", "|Inflows", "|OutFlows", "|Errors");
                for (int i_node = 0; i_node < Nodes.Length; i_node++) {
                    string[] output;
                    ref Node node = ref Nodes[i_node];
                    IEnumerable<(int, double)> inflows = node.InPipelines.Select(i => (Pipelines[i].Node0, variables[i].X)).ToArray();
                    IEnumerable<(int, double)> outflows = node.OutPipelines.Select(i => (Pipelines[i].Node1, variables[i].X)).ToArray();
                    var inflows_simplified = inflows.Where(t => t.Item2.ToString("#.##") != "").ToArray();
                    var outflows_simplified = outflows.Where(t => t.Item2.ToString("#.##") != "").ToArray();
                    output = new string[]{
                    i_node.ToString(),
                    "|" + Demands[i_node],
                    "|" + (inflows.Select(t => t.Item2).Sum() - outflows.Select(t => t.Item2).Sum()),
                    "|" + string.Join(",", inflows_simplified.Select(t => $"{t.Item1}:{t.Item2.ToString("#.##")}")),
                    "|" + string.Join(",", outflows_simplified.Where(t => t.Item2.ToString("#.##") != "").Select(t => $"{t.Item1}:{t.Item2.ToString("#.##")}")),
                    ""
                };
                    if (inflows_simplified.Select(t => t.Item1).Intersect(outflows_simplified.Select(t => t.Item1)).Any()) {
                        output[^1] = " |ERROR:BidirectionalFlow";
                    }
                    Console.WriteLine(format, output);
                }
                Console.WriteLine("OptimalCost:" + model.ObjVal);
            }
            return model.ObjVal;
        }
    }
}
