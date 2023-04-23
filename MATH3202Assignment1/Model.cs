using Gurobi;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;
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

        internal static Model FromFile3() {
            IEnumerable<string[]> lines = ReadLines("nodes3.csv").Select(str => str.Split(',')).ToArray();
            var nodes = lines.Select(strs => new Node() { X = double.Parse(strs[1]), Y = double.Parse(strs[2]) }).ToArray();
            var demands = lines.SelectMany(strs => strs[3..]).Select(double.Parse).ToArray();
            return new(nodes, demands.ToArray(), 2);
        }

        internal static Model FromFile3TenYearsOnly() {
            IEnumerable<string[]> lines = ReadLines("nodes3.csv").Select(str => str.Split(',')).ToArray();
            var nodes = lines.Select(strs => new Node() { X = double.Parse(strs[1]), Y = double.Parse(strs[2]) }).ToArray();
            var demands = lines.Select(strs => strs[4]).Select(double.Parse).ToArray();
            return new(nodes, demands.ToArray(), 1);
        }

        Node[] Nodes { get; }
        double[] demands;
        double GetDemand(int day, int node) => demands[node * DayAmount + day];
        int DayAmount { get; }

        internal double FindOptimal(LogMode logMode = LogMode.Detailed,
            double pipelineCapacity = double.PositiveInfinity, double supplierOverallCapacity = double.PositiveInfinity, double pipelineImbalanceLimit = 0,
            bool supplierUpgrade = false, bool pipelineUpgrade = false, bool upgradeDelayDiscount = false, double[]? demandUncertainity = null) {

            GRBModel model = new GRBModel(Env);

            #region Variables
            GRBVar[,] variables_time_pipeline_flow = new GRBVar[DayAmount, Pipelines.Length];
            GRBVar[,] variables_time_supplier_supply = new GRBVar[DayAmount, Suppliers.Length];
            GRBVar[,] variables_time_pipeline_extraIn = new GRBVar[DayAmount, Pipelines.Length];
            GRBVar[,] variables_time_pipeline_extraOut = new GRBVar[DayAmount, Pipelines.Length];
            GRBVar[,] variables_supplier_upgradeOption_notDelay = new GRBVar[Suppliers.Length, UpgradeOptionCount];
            GRBVar[,] variables_supplier_upgradeOption_isDelay = new GRBVar[Suppliers.Length, UpgradeOptionCount];
            if (supplierUpgrade) {
                for (int i_supplier = 0; i_supplier < Suppliers.Length; i_supplier++) {
                    GRBLinExpr chosenBooleanSum = new GRBLinExpr();
                    for (int i_option = 0; i_option < UpgradeOptionCount; i_option++) {
                        GRBVar notDelay = model.AddVar(0, 1, default, GRB.BINARY, $"S{i_supplier}U{i_option}");
                        variables_supplier_upgradeOption_notDelay[i_supplier, i_option] = notDelay;
                        chosenBooleanSum.AddTerm(1, notDelay);
                        if (upgradeDelayDiscount) {
                            GRBVar isDelay = model.AddVar(0, 1, default, GRB.BINARY, $"S{i_supplier}U{i_option}D");
                            variables_supplier_upgradeOption_isDelay[i_supplier, i_option] = isDelay;
                            chosenBooleanSum.AddTerm(1, isDelay);
                        }
                    }
                    model.AddConstr(chosenBooleanSum <= 1, $"ExclusiveOptionsConstraintS{i_supplier}");
                }
            }
            GRBVar[] variables_pipeline_upgradeNotDelay = new GRBVar[Pipelines.Length];
            GRBVar[] variables_pipeline_upgradeDelay = new GRBVar[Pipelines.Length];
            if (pipelineUpgrade) {
                if (pipelineCapacity == double.PositiveInfinity) {
                    throw new ArgumentException("When pipelineUpgrade is true, pipelineCapacity should be finite");
                }
                for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                    GRBVar upgradeNotDelay = model.AddVar(0, 1, default, GRB.BINARY, $"P{i_pipeline}U");
                    variables_pipeline_upgradeNotDelay[i_pipeline] = upgradeNotDelay;
                    if (upgradeDelayDiscount) {
                        GRBVar upgradeDelay = model.AddVar(0, 1, default, GRB.BINARY, $"P{i_pipeline}UD");
                        variables_pipeline_upgradeDelay[i_pipeline] = upgradeDelay;
                        model.AddConstr(upgradeNotDelay - upgradeDelay <= 1, $"ExclusiveOptionsConstraintP{i_pipeline}");
                    }
                }
            }
            #endregion

            bool IsDelayDay(int day) => day != 0;

            #region Constraints
            GRBLinExpr[] dailyCosts = new GRBLinExpr[DayAmount];
            for (int i_day = 0; i_day < DayAmount; i_day++) {
                dailyCosts[i_day] = new GRBLinExpr();
                for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                    ref Pipeline pipeline = ref Pipelines[i_pipeline];
                    GRBVar flow = model.AddVar(0, double.PositiveInfinity, default, GRB.CONTINUOUS, $"T{i_day}P{pipeline}:{pipeline.Node0}->{pipeline.Node1}");
                    variables_time_pipeline_flow[i_day, i_pipeline] = flow;
                    dailyCosts[i_day].AddTerm(0.01 * Pipelines[i_pipeline].Length, flow);
                    GRBVar extraIn = model.AddVar(0, pipelineImbalanceLimit, default, GRB.CONTINUOUS, $"T{i_day}P{pipeline}ExI:{pipeline.Node0}->{pipeline.Node1}");
                    variables_time_pipeline_extraIn[i_day, i_pipeline] = extraIn;
                    dailyCosts[i_day].AddTerm(0.1 + 0.01 * Pipelines[i_pipeline].Length, extraIn);
                    GRBVar extraOut = model.AddVar(0, pipelineImbalanceLimit, default, GRB.CONTINUOUS, $"T{i_day}P{pipeline}ExO:{pipeline.Node0}->{pipeline.Node1}");
                    variables_time_pipeline_extraOut[i_day, i_pipeline] = extraOut;
                    dailyCosts[i_day].AddTerm(0.1 + 0.01 * Pipelines[i_pipeline].Length, extraOut);
                    if (pipelineUpgrade) {
                        GRBLinExpr additionalCapacity = new GRBLinExpr();
                        additionalCapacity.AddTerm(pipelineCapacity, variables_pipeline_upgradeNotDelay[i_pipeline]);
                        if (upgradeDelayDiscount && IsDelayDay(i_day)) {
                            additionalCapacity.AddTerm(pipelineCapacity, variables_pipeline_upgradeDelay[i_pipeline]);
                        }
                        model.AddConstr(flow + extraIn + extraOut <= additionalCapacity + pipelineCapacity,
                            $"CapacityConstrantP{i_pipeline}"
                        );
                    } else {
                        model.AddConstr(flow + extraIn + extraOut <= pipelineCapacity, $"CapacityConstrantP{i_pipeline}");
                    }
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
                        GRBVar suppliedAmount = model.AddVar(0, double.PositiveInfinity, default, GRB.CONTINUOUS, $"S{i_supplier}");
                        variables_time_supplier_supply[i_day, i_supplier] = suppliedAmount;
                        if (supplierUpgrade) {
                            GRBLinExpr additionalCapacity = new GRBLinExpr();
                            for (int i_option = 0; i_option < UpgradeOptionCount; i_option++) {
                                double capacity = UpgradeOptions[i_supplier, i_option].AdditionalCapacity;
                                additionalCapacity.AddTerm(capacity, variables_supplier_upgradeOption_notDelay[i_supplier, i_option]);
                                if (upgradeDelayDiscount && IsDelayDay(i_day)) {
                                    additionalCapacity.AddTerm(capacity, variables_supplier_upgradeOption_isDelay[i_supplier, i_option]);
                                }
                            }
                            model.AddConstr(suppliedAmount <= additionalCapacity + supplier.Capacity, $"CapacityConstraintS{i_supplier}");
                        } else {
                            model.AddConstr(suppliedAmount <= supplier.Capacity, $"CapacityConstraintS{i_supplier}");
                        }
                        model.AddConstr(netInflow + variables_time_supplier_supply[i_day, i_supplier] >= GetDemand(i_day, i_node), $"DemandConstraintT{i_day}N{i_node}");
                        dailyCosts[i_day].Add(variables_time_supplier_supply[i_day, i_supplier] * supplier.Cost);
                    }
                }
            }
            for (int i_supplier = 0; i_supplier < Suppliers.Length; i_supplier++) {
                GRBLinExpr totalSupply = new();
                for (int i_day = 0; i_day < DayAmount; i_day++) {
                    totalSupply.AddTerm(1, variables_time_supplier_supply[i_day, i_supplier]);
                }
                model.AddConstr(totalSupply <= supplierOverallCapacity, $"SupplierConstraintS{i_supplier}");
            }
            for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                GRBLinExpr totalImbalance = new();
                for (int i_day = 0; i_day < DayAmount; i_day++) {
                    totalImbalance.AddTerm(1, variables_time_pipeline_extraIn[i_day, i_pipeline]);
                    totalImbalance.AddTerm(-1, variables_time_pipeline_extraOut[i_day, i_pipeline]);
                }
                model.AddConstr(totalImbalance == 0, $"ImbalanceContraintP{i_pipeline}");
            }
            #endregion

            #region Objective
            GRBLinExpr totalCost = new GRBLinExpr();
            foreach (GRBLinExpr cost in dailyCosts) totalCost.Add(cost);
            GRBLinExpr suppliersImmediateUpgradeCost = new GRBLinExpr();
            GRBLinExpr suppliersDelayedUpgradeCost = new GRBLinExpr();
            if (supplierUpgrade) {
                for (int i_supplier = 0; i_supplier < Suppliers.Length; i_supplier++) {
                    for (int i_option = 0; i_option < UpgradeOptionCount; i_option++) {
                        double cost = UpgradeOptions[i_supplier, i_option].Cost;
                        suppliersImmediateUpgradeCost.AddTerm(cost, variables_supplier_upgradeOption_notDelay[i_supplier, i_option]);
                    }
                }
                totalCost.Add(suppliersImmediateUpgradeCost);
                if (upgradeDelayDiscount) {
                    for (int i_supplier = 0; i_supplier < Suppliers.Length; i_supplier++) {
                        for (int i_option = 0; i_option < UpgradeOptionCount; i_option++) {
                            double cost = UpgradeOptions[i_supplier, i_option].Cost;
                            suppliersDelayedUpgradeCost.AddTerm(cost * 0.7, variables_supplier_upgradeOption_isDelay[i_supplier, i_option]);
                        }
                    }
                    totalCost.Add(suppliersDelayedUpgradeCost);
                }
            }
            GRBLinExpr pipelinesImmediateUpgradeCost = new GRBLinExpr();
            GRBLinExpr pipelinesDelayedUpgradeCost = new GRBLinExpr();
            if (pipelineUpgrade) {
                for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                    double cost = 200000 * Pipelines[i_pipeline].Length;
                    pipelinesImmediateUpgradeCost.AddTerm(cost, variables_pipeline_upgradeNotDelay[i_pipeline]);
                }
                totalCost.Add(pipelinesImmediateUpgradeCost);
                if (upgradeDelayDiscount) {
                    for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                        double cost = 200000 * Pipelines[i_pipeline].Length;
                        pipelinesDelayedUpgradeCost.AddTerm(cost * 0.7, variables_pipeline_upgradeDelay[i_pipeline]);
                    }
                    totalCost.Add(pipelinesDelayedUpgradeCost);
                }
            }
            model.SetObjective(totalCost, GRB.MINIMIZE);
            #endregion

            model.Optimize();

            #region Logging
            if (logMode != LogMode.None) {
                string format = "{0,-5}{1,-8}{2,-8}{3,-20}{4,-20}{5,-20}";

                void LogUpgrades(bool isDelay) {
                    Console.WriteLine();
                    if (supplierUpgrade) {
                        Console.WriteLine(
                            $"Supplier{(isDelay ? "Delayed" : "Immediate")}Upgrades| " +
                            string.Join(
                                ", ", Enumerable.Range(0, Suppliers.Length).
                                Select(i_supplier =>
                                    $"{Suppliers[i_supplier].Node}:" +
                                    string.Concat(
                                        Enumerable.Range(0, UpgradeOptionCount).
                                        Select<int,int>(
                                            isDelay ?
                                            i_option => (int)variables_supplier_upgradeOption_isDelay[i_supplier, i_option].X :
                                            i_option => (int)variables_supplier_upgradeOption_notDelay[i_supplier, i_option].X
                                        )
                                    )
                                )
                            )
                        );
                        Console.WriteLine(isDelay ? $"SuppliersDelayedUpgradeCost: {suppliersDelayedUpgradeCost.Value}" : $"SuppliersImmediateUpgradeCost: {suppliersImmediateUpgradeCost.Value}");
                    }
                    if (pipelineUpgrade) {
                        int[] upgradedPipelines = 
                            Enumerable.Range(0, Pipelines.Length).
                            Where<int>(
                                isDelay ? 
                                i_pipeline => variables_pipeline_upgradeDelay[i_pipeline].X != 0 :
                                i_pipeline => variables_pipeline_upgradeNotDelay[i_pipeline].X != 0
                            ).
                            ToArray();
                        Console.WriteLine(
                            $"Pipeline{(isDelay ? "Delayed" : "Immediate")}Upgrades| " +
                            string.Join(
                                ", ",
                                upgradedPipelines.Select(i_pipeline => $"{i_pipeline}({Pipelines[i_pipeline].Node0},{Pipelines[i_pipeline].Node1})")
                            )
                        );
                        Console.WriteLine(isDelay ? $"PipelinesDelayedUpgradeCost:{pipelinesDelayedUpgradeCost.Value}" : $"PipelinesImmediateUpgradeCost:{pipelinesImmediateUpgradeCost.Value}");
                    }
                    if (supplierUpgrade && pipelineUpgrade) {
                        Console.WriteLine(
                            isDelay? 
                            $"TotalDelayedUpgradeCost:{suppliersDelayedUpgradeCost.Value + pipelinesDelayedUpgradeCost.Value}" : 
                            $"TotalImmediateUpgradeCost:{suppliersImmediateUpgradeCost.Value + pipelinesImmediateUpgradeCost.Value}"
                        );
                    }
                }

                bool immediateUpgradeLogged = false;
                bool delayUpgradeLogged = false;
                for (int i_day = 0; i_day < DayAmount; i_day++) {
                    if (!IsDelayDay(i_day)) {
                        if (!immediateUpgradeLogged) {
                            LogUpgrades(false);
                            immediateUpgradeLogged = true;
                        }
                    } else {
                        if (!delayUpgradeLogged) {
                            LogUpgrades(true);
                            delayUpgradeLogged = true;
                        }
                    }
                    Console.WriteLine();
                    Console.WriteLine($"Day{i_day},Cost:{dailyCosts[i_day].Value}");
                    if (logMode == LogMode.Brief) continue;
                    Console.WriteLine(format, "Node", "|Demand", "|Actual", "|Inflows", "|Outflows", "|Errors");
                    List<LogLine> lines = new List<LogLine>();
                    for (int i_node = 0; i_node < Nodes.Length; i_node++) {
                        ref Node node = ref Nodes[i_node];
                        var outflows = node.OutPipelines.Select(i => (Pipelines[i].Node1, variables_time_pipeline_flow[i_day, i].X, variables_time_pipeline_extraIn[i_day, i].X)).ToArray();
                        var inflows = node.InPipelines.Select(i => (Pipelines[i].Node0, variables_time_pipeline_flow[i_day, i].X, variables_time_pipeline_extraOut[i_day, i].X)).ToArray();
                        var inflows_rounded = inflows.Select(t => (t.Item1, Math.Round(t.Item2, 2), Math.Round(t.Item3, 2))).Where(t => t is not { Item2: 0, Item3: 0 }).ToArray();
                        var outflows_rounded = outflows.Select(t => (t.Item1, Math.Round(t.Item2, 2), Math.Round(t.Item3, 2))).Where(t => t is not { Item2: 0, Item3: 0 }).ToArray();
                        LogLine line = new LogLine() {
                            Node = i_node,
                            Demand = GetDemand(i_day, i_node),
                            Actual = inflows.Select(t => t.Item2 + t.Item3).Sum() - outflows.Select(t => t.Item2 + t.Item3).Sum(),
                            Inflows = inflows,
                            Inflows_Rounded = inflows_rounded,
                            Outflows = outflows,
                            Outflows_Rounded = outflows_rounded
                        };
                        if (inflows_rounded.Select(t => t.Item1).Intersect(outflows_rounded.Select(t => t.Item1)).Any()) {
                            line.Errors.Add("BidirectionalFlow");
                        }
                        lines.Add(line);
                    }
                    foreach (LogLine line in lines) {
                        foreach (var tuple in line.Inflows) {
                            if (tuple.Item3 == 0) continue;
                            var tuple_ = lines[tuple.Item1].Outflows.First(t => t.Item1 == line.Node);
                            if (tuple_.Item3 != 0) line.Errors.Add($"ExInFromN{tuple.Item1}AndExOutAtN{tuple_.Item1}");
                        }
                    }
                    foreach (LogLine line in lines) line.Write("{0,-5}{1,-8}{2,-8}{3,-20}{4,-20}{5,-20}");
                }
                Console.WriteLine();
                if (supplierUpgrade || pipelineUpgrade) 
                    Console.WriteLine(
                        "TotalUpgradeCost:" + 
                        (suppliersImmediateUpgradeCost.Value + suppliersDelayedUpgradeCost.Value + pipelinesImmediateUpgradeCost.Value + pipelinesDelayedUpgradeCost.Value)
                    );
                Console.WriteLine("DailyCostsSum:" + dailyCosts.Select(c => c.Value).Sum());
                Console.WriteLine("TotalCost:" + model.ObjVal);
            }
            #endregion

            return model.ObjVal;
        }
    }

    public enum LogMode { None, Brief, Detailed }

    public class LogLine {
        public int Node;
        public double Demand;
        public double Actual;
        public required (int, double, double)[] Inflows { get; init; }
        public required (int, double, double)[] Inflows_Rounded { get; init; }
        public required (int, double, double)[] Outflows { get; init; }
        public required (int, double, double)[] Outflows_Rounded { get; init; }
        public List<string> Errors { get; } = new List<string>();

        public void Write(string format) {
            var output = new string[]{
                Node.ToString(),
                "|" + Demand,
                "|" + Actual,
                "|" + string.Join(",", Inflows_Rounded.Select(t => $"{t.Item1}:{t.Item2.ToString("0.##")}+{t.Item3.ToString("0.##")}")),
                "|" + string.Join(",", Outflows_Rounded.Select(t => $"{t.Item1}:{t.Item2.ToString("0.##")}+{t.Item3.ToString("0.##")}")),
                "|" + string.Join(",", Errors)
            };
            Console.WriteLine(format, output);
        }
    }
}
