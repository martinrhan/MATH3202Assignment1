using Gurobi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MATH3202Assignment1.Common;

namespace MATH3202Assignment1 {
    internal class Model {
        private Model(Node[] nodes, double[] demands, int dayAmount) {
            Nodes = nodes;
            this.demands = demands;
            DayAmount = dayAmount;
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
            return new Model(nodes, demands, 1);
        }

        internal static Model FromFile1Repeat() {
            IEnumerable<string[]> lines = ReadLines("nodes.csv").Select(str => str.Split(',')).ToArray();
            var nodes = lines.Select(strs => new Node() { X = double.Parse(strs[1]), Y = double.Parse(strs[2]) }).ToArray();
            var demands = lines.SelectMany(strs => Enumerable.Repeat(double.Parse(strs[3]), 14)).ToArray();
            return new Model(nodes, demands, 14);
        }

        internal static Model FromFile2() {
            IEnumerable<string[]> lines = ReadLines("nodes2.csv").Select(str => str.Split(',')).ToArray();
            var nodes = lines.Select(strs => new Node() { X = double.Parse(strs[1]), Y = double.Parse(strs[2]) }).ToArray();
            var demands = lines.SelectMany(strs => strs[3..]).Select(double.Parse).ToArray();
            return new(nodes, demands.ToArray(), 14);
        }

        internal static Model FromFile2SingleDay(int day) {
            IEnumerable<string[]> lines = ReadLines("nodes2.csv").Select(str => str.Split(',')).ToArray();
            var nodes = lines.Select(strs => new Node() { X = double.Parse(strs[1]), Y = double.Parse(strs[2]) }).ToArray();
            var demands = lines.Select(strs => strs[3 + day]).Select(double.Parse).ToArray();
            return new(nodes, demands.ToArray(), 1);
        }

        Node[] Nodes { get; }
        double[] demands;
        double GetDemand(int day, int node) => demands[node * DayAmount + day];
        int DayAmount { get; }

        internal double FindOptimal(LogMode logMode = LogMode.Detailed,
            double pipelineLimit = double.PositiveInfinity, double supplierLimit = double.PositiveInfinity, double pipelineImbalanceLimit = 0) {
            GRBModel model = new GRBModel(Env);
            GRBVar[,] variables_time_pipeline_flow = new GRBVar[DayAmount, Pipelines.Length];
            GRBVar[,] variables_time_supplier = new GRBVar[DayAmount, Suppliers.Length];
            GRBVar[,] variables_time_pipeline_extraIn = new GRBVar[DayAmount, Pipelines.Length];
            GRBVar[,] variables_time_pipeline_extraOut = new GRBVar[DayAmount, Pipelines.Length];
            GRBLinExpr[] dailyCosts = new GRBLinExpr[DayAmount];
            for (int i_day = 0; i_day < DayAmount; i_day++) {
                dailyCosts[i_day] = new GRBLinExpr();
                for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                    ref Pipeline pipeline = ref Pipelines[i_pipeline];
                    GRBVar flow = model.AddVar(0, pipelineLimit, default, GRB.CONTINUOUS, $"T{i_day}P{pipeline}:{pipeline.Node0}->{pipeline.Node1}");
                    variables_time_pipeline_flow[i_day, i_pipeline] = flow;
                    dailyCosts[i_day].AddTerm(0.01 * Pipelines[i_pipeline].Length, flow);
                    GRBVar extraIn = model.AddVar(0, pipelineImbalanceLimit, default, GRB.CONTINUOUS, $"T{i_day}P{pipeline}ExI:{pipeline.Node0}->{pipeline.Node1}");
                    variables_time_pipeline_extraIn[i_day, i_pipeline] = extraIn;
                    dailyCosts[i_day].AddTerm(0.1, extraIn);
                    GRBVar extraOut = model.AddVar(0, pipelineImbalanceLimit, default, GRB.CONTINUOUS, $"T{i_day}P{pipeline}ExO:{pipeline.Node0}->{pipeline.Node1}");
                    variables_time_pipeline_extraOut[i_day, i_pipeline] = extraOut;
                    dailyCosts[i_day].AddTerm(0.1, extraOut);
                    model.AddConstr(flow + extraIn + extraOut <= pipelineLimit, $"PipelineConstrant{i_pipeline}");
                }
                for (int i_node = 0; i_node < Nodes.Length; i_node++) {
                    ref Node node = ref Nodes[i_node];
                    GRBLinExpr netInflow = new();
                    foreach (int i_pipeline in node.InPipelines) {
                        netInflow.AddTerm(1, variables_time_pipeline_flow[i_day, i_pipeline]);
                        netInflow.AddTerm(1, variables_time_pipeline_extraOut[i_day, i_pipeline]);
                    }
                    foreach (int i_pipeline in node.OutPipelines) {
                        netInflow.AddTerm(-1, variables_time_pipeline_flow[i_day, i_pipeline]);
                        netInflow.AddTerm(-1, variables_time_pipeline_extraIn[i_day, i_pipeline]);
                    }
                    int i_supplier = Enumerable.Range(0, Suppliers.Length).FirstOrDefault(i => Suppliers[i].Node == i_node, -1);
                    if (i_supplier == -1) {
                        model.AddConstr(netInflow >= GetDemand(i_day, i_node), $"DemandConstraintT{i_day}N{i_node}");
                    } else {
                        ref Supplier supplier = ref Suppliers[i_supplier];
                        variables_time_supplier[i_day, i_supplier] = model.AddVar(0, supplier.Capacity, default, GRB.CONTINUOUS, $"S{i_supplier}");
                        model.AddConstr(netInflow + variables_time_supplier[i_day, i_supplier] >= GetDemand(i_day, i_node), $"DemandConstraintT{i_day}N{i_node}");
                        dailyCosts[i_day].Add(variables_time_supplier[i_day, i_supplier] * supplier.Cost);
                    }
                }
            }
            for (int i_supplier = 0; i_supplier < Suppliers.Length; i_supplier++) {
                GRBLinExpr totalSupply = new();
                for (int i_day = 0; i_day < DayAmount; i_day++) {
                    totalSupply.AddTerm(1, variables_time_supplier[i_day, i_supplier]);
                }
                model.AddConstr(totalSupply <= supplierLimit, $"SupplierConstraintS{i_supplier}");
            }
            for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                GRBLinExpr totalImbalance = new();
                for (int i_day = 0; i_day < DayAmount; i_day++) {
                    totalImbalance.AddTerm(1, variables_time_pipeline_extraIn[i_day, i_pipeline]);
                    totalImbalance.AddTerm(-1, variables_time_pipeline_extraOut[i_day, i_pipeline]);
                }
                model.AddConstr(totalImbalance == 0, $"ImbalanceContraintP{i_pipeline}");
            }
            GRBLinExpr totalCost = new GRBLinExpr();
            foreach (GRBLinExpr cost in dailyCosts) totalCost.Add(cost);
            model.SetObjective(totalCost, GRB.MINIMIZE);
            model.Optimize();

            if (logMode != LogMode.None) {
                for (int i_day = 0; i_day < DayAmount; i_day++) {
                    Console.WriteLine("_______________");
                    Console.WriteLine($"Day{i_day},Cost:{dailyCosts[i_day].Value}");
                    if (logMode == LogMode.Brief) continue;
                    string format = "{0,-5}{1,-8}{2,-8}{3,-20}{4,-20}{5,-20}";
                    Console.WriteLine(format, "Node", "|Demand", "|Actual", "|Inflows", "|OutFlows", "|Errors");
                    for (int i_node = 0; i_node < Nodes.Length; i_node++) {
                        string[] output;
                        ref Node node = ref Nodes[i_node];
                        IEnumerable<(int, double, double)> inflows = node.InPipelines.Select(i => (Pipelines[i].Node0, variables_time_pipeline_flow[i_day, i].X, variables_time_pipeline_extraOut[i_day, i].X)).ToArray();
                        IEnumerable<(int, double, double)> outflows = node.OutPipelines.Select(i => (Pipelines[i].Node1, variables_time_pipeline_flow[i_day, i].X, variables_time_pipeline_extraIn[i_day, i].X)).ToArray();
                        var inflows_simplified = inflows.Select(t => (t.Item1, Math.Round(t.Item2, 2), Math.Round(t.Item3, 2))).Where(t => t is not { Item2: 0, Item3: 0 }).ToArray();
                        var outflows_simplified = outflows.Select(t => (t.Item1, Math.Round(t.Item2, 2), Math.Round(t.Item3, 2))).Where(t => t is not { Item2: 0, Item3: 0 }).ToArray();
                        output = new string[]{
                            i_node.ToString(),
                            "|" + GetDemand(i_day, i_node),
                            "|" + (inflows.Select(t => t.Item2).Sum() - outflows.Select(t => t.Item2).Sum()),
                            "|" + string.Join(",", inflows_simplified.Select(t => $"{t.Item1}:{t.Item2.ToString("0.##")}+{t.Item3.ToString("0.##")}")),
                            "|" + string.Join(",", outflows_simplified.Select(t => $"{t.Item1}:{t.Item2.ToString("0.##")}+{t.Item3.ToString("0.##")}")),
                            ""
                        };
                        if (inflows_simplified.Select(t => t.Item1).Intersect(outflows_simplified.Select(t => t.Item1)).Any()) {
                            output[^1] = "|ERROR:BidirectionalFlow";
                        }
                        Console.WriteLine(format, output);
                    }
                }
                Console.WriteLine("OptimalCost:" + model.ObjVal);
                Console.WriteLine("OptimalCost:" + dailyCosts.Select(c => c.Value).Sum());
            }
            return model.ObjVal;
        }
    }

    public enum LogMode { None, Brief, Detailed }
}
