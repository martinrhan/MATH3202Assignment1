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
            bool supplierUpgrade = false, bool pipelineUpgrade = false, bool upgradeDelayDiscount = false, double[]? uncertianDemandMultipliers = null, bool undiscountedUpgradeCostSecondPeriodNotMoreThanFirstPeriodTwice = false) {

            if (uncertianDemandMultipliers is null) uncertianDemandMultipliers = new double[] { 1 };
            else {
                if (upgradeDelayDiscount == false) throw new ArgumentException("Cannot specify uncertianDemandMultipliers when upgradeDelayDiscount is false");
                if (uncertianDemandMultipliers[0] != 1) throw new ArgumentException("The specified uncertianDemandMultipliers should have 1 as first element");
            }

            GRBModel model = new GRBModel(Env);

            bool IsDelayDay(int day) => day != 0;

            #region Variables and Constraints
            //For early days (non delay days), corresponding slots in the array should be null.
            GRBVar[,,] variables_time_pipeline_uncert_flow = new GRBVar[DayAmount, Pipelines.Length, uncertianDemandMultipliers.Length];
            GRBVar[,,] variables_time_supplier_uncert_supply = new GRBVar[DayAmount, Suppliers.Length, uncertianDemandMultipliers.Length];
            GRBVar[,,] variables_time_pipeline_uncert_extraIn = new GRBVar[DayAmount, Pipelines.Length, uncertianDemandMultipliers.Length];
            GRBVar[,,] variables_time_pipeline_uncert_extraOut = new GRBVar[DayAmount, Pipelines.Length, uncertianDemandMultipliers.Length];
            GRBVar[,] variables_supplier_upgradeOption_immediate = new GRBVar[Suppliers.Length, UpgradeOptionCount];
            GRBVar[,,] variables_supplier_upgradeOption_uncert_delay = new GRBVar[Suppliers.Length, UpgradeOptionCount, uncertianDemandMultipliers.Length];
            if (supplierUpgrade) {
                for (int i_supplier = 0; i_supplier < Suppliers.Length; i_supplier++) {
                    for (int i_option = 0; i_option < UpgradeOptionCount; i_option++) {
                        variables_supplier_upgradeOption_immediate[i_supplier, i_option] = model.AddVar(0, 1, default, GRB.BINARY, $"Supplier{i_supplier}UpgradeOption{i_option}ChosenImmediate");
                    }
                    for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                        GRBLinExpr exclusiveOptionSum = new GRBLinExpr();
                        for (int i_option = 0; i_option < UpgradeOptionCount; i_option++) {
                            GRBVar immediate = variables_supplier_upgradeOption_immediate[i_supplier, i_option];
                            exclusiveOptionSum.AddTerm(1, immediate);
                            GRBVar delay = model.AddVar(0, 1, default, GRB.BINARY, $"Supplier{i_supplier}UpgradeOption{i_option}ChosenDelayed");
                            variables_supplier_upgradeOption_uncert_delay[i_supplier, i_option, i_uncert] = delay;
                            exclusiveOptionSum.AddTerm(1, delay);
                        }
                        model.AddConstr(exclusiveOptionSum <= 1, $"Supplier{i_supplier}Uncertain{i_uncert}ExclusiveOptionsConstraint");
                    }
                }
            }
            GRBVar[] variables_pipeline_upgradeImmediate = new GRBVar[Pipelines.Length];
            GRBVar[,] variables_pipeline_upgradeDelay = new GRBVar[Pipelines.Length, uncertianDemandMultipliers.Length];
            if (pipelineUpgrade) {
                if (pipelineCapacity == double.PositiveInfinity) {
                    throw new ArgumentException("When pipelineUpgrade is true, pipelineCapacity should be finite");
                }
                for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                    GRBVar upgradeNotDelay = model.AddVar(0, 1, default, GRB.BINARY, $"Pipeline{i_pipeline}UpgradeImmediate");
                    variables_pipeline_upgradeImmediate[i_pipeline] = upgradeNotDelay;
                    for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                        GRBLinExpr immediateUpgradeCostSum = new GRBLinExpr();
                        GRBLinExpr delayedUpgradeCostSum_undiscounted = new GRBLinExpr();
                        GRBVar upgradeDelay = model.AddVar(0, 1, default, GRB.BINARY, $"Pipeline{i_pipeline}UpgradeDelayed");
                        variables_pipeline_upgradeDelay[i_pipeline, i_uncert] = upgradeDelay;
                        model.AddConstr(upgradeNotDelay + upgradeDelay <= 1, $"Pipeline{i_pipeline}Uncertain{i_uncert}ExclusiveOptionsConstraint");
                    }
                }
            }
            if (undiscountedUpgradeCostSecondPeriodNotMoreThanFirstPeriodTwice) {
                for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                    GRBLinExpr immediateUpgradeCostSum = new GRBLinExpr();
                    GRBLinExpr delayedUpgradeCostSum_undiscounted = new GRBLinExpr();
                    if (supplierUpgrade) {
                        for (int i_supplier = 0; i_supplier < Suppliers.Length; i_supplier++) {
                            for (int i_option = 0; i_option < UpgradeOptionCount; i_option++) {
                                double cost = UpgradeOptions[i_supplier, i_option].Cost;
                                immediateUpgradeCostSum.AddTerm(cost, variables_supplier_upgradeOption_immediate[i_supplier, i_option]);
                                delayedUpgradeCostSum_undiscounted.AddTerm(cost, variables_supplier_upgradeOption_uncert_delay[i_supplier, i_option, i_uncert]);
                            }
                        }
                    }
                    if (pipelineUpgrade) {
                        for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                            double cost = Pipelines[i_pipeline].Length * 200000;
                            immediateUpgradeCostSum.AddTerm(cost, variables_pipeline_upgradeImmediate[i_pipeline]);
                            delayedUpgradeCostSum_undiscounted.AddTerm(cost, variables_pipeline_upgradeDelay[i_pipeline, i_uncert]);
                        }
                    }
                    model.AddConstr(delayedUpgradeCostSum_undiscounted <= immediateUpgradeCostSum * 2, $"Uncertain{i_uncert}UndiscountedUpgradeCostSecondPeriodNotMoreThanFirstPeriodTwiceConstraint");
                }
            }
            GRBLinExpr[,] expressions_time_uncert_dailyCost = new GRBLinExpr[DayAmount, uncertianDemandMultipliers.Length];
            for (int i_day = 0; i_day < DayAmount; i_day++) {
                for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                    expressions_time_uncert_dailyCost[i_day, i_uncert] = new GRBLinExpr();
                    for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                        ref Pipeline pipeline = ref Pipelines[i_pipeline];
                        GRBVar flow = model.AddVar(0, double.PositiveInfinity, default, GRB.CONTINUOUS, $"Time{i_day}Uncertain{i_uncert}Pipeline{i_pipeline}:Node{pipeline.Node0}->Node{pipeline.Node1}");
                        variables_time_pipeline_uncert_flow[i_day, i_pipeline, i_uncert] = flow;
                        expressions_time_uncert_dailyCost[i_day, i_uncert].AddTerm(0.01 * Pipelines[i_pipeline].Length, flow);
                        GRBVar extraIn = model.AddVar(0, pipelineImbalanceLimit, default, GRB.CONTINUOUS, $"Time{i_day}Uncertain{i_uncert}Pipeline{i_pipeline}ExtraIn:Node{pipeline.Node0}->Node{pipeline.Node1}");
                        variables_time_pipeline_uncert_extraIn[i_day, i_pipeline, i_uncert] = extraIn;
                        expressions_time_uncert_dailyCost[i_day, i_uncert].AddTerm(0.1 + 0.01 * Pipelines[i_pipeline].Length, extraIn);
                        GRBVar extraOut = model.AddVar(0, pipelineImbalanceLimit, default, GRB.CONTINUOUS, $"Time{i_day}Uncertain{i_uncert}Pipeline{i_pipeline}ExtraOut:Node{pipeline.Node0}->Node{pipeline.Node1}");
                        variables_time_pipeline_uncert_extraOut[i_day, i_pipeline, i_uncert] = extraOut;
                        expressions_time_uncert_dailyCost[i_day, i_uncert].AddTerm(0.1 + 0.01 * Pipelines[i_pipeline].Length, extraOut);
                        if (pipelineUpgrade) {
                            GRBLinExpr additionalCapacity = new GRBLinExpr();
                            additionalCapacity.AddTerm(pipelineCapacity, variables_pipeline_upgradeImmediate[i_pipeline]);
                            if (upgradeDelayDiscount && IsDelayDay(i_day)) {
                                additionalCapacity.AddTerm(pipelineCapacity, variables_pipeline_upgradeDelay[i_pipeline, i_uncert]);
                            }
                            model.AddConstr(flow + extraIn + extraOut <= additionalCapacity + pipelineCapacity, $"Pipeline{i_pipeline}Uncertain{i_uncert}CapacityConstrant");
                        } else {
                            model.AddConstr(flow + extraIn + extraOut <= pipelineCapacity, $"Pipeline{i_pipeline}Uncertain{i_uncert}CapacityConstrant");
                        }
                    }
                    for (int i_node = 0; i_node < Nodes.Length; i_node++) {
                        ref Node node = ref Nodes[i_node];
                        GRBLinExpr netInflow = new();
                        foreach (int i_pipeline in node.InPipelines) {
                            netInflow.AddTerm(1, variables_time_pipeline_uncert_flow[i_day, i_pipeline, i_uncert]);
                            netInflow.AddTerm(1, variables_time_pipeline_uncert_extraOut[i_day, i_pipeline, i_uncert]);
                        }
                        foreach (int i_pipeline in node.OutPipelines) {
                            netInflow.AddTerm(-1, variables_time_pipeline_uncert_flow[i_day, i_pipeline, i_uncert]);
                            netInflow.AddTerm(-1, variables_time_pipeline_uncert_extraIn[i_day, i_pipeline, i_uncert]);
                        }
                        int i_supplier = Enumerable.Range(0, Suppliers.Length).FirstOrDefault(i => Suppliers[i].Node == i_node, -1);
                        if (i_supplier == -1) {
                            model.AddConstr(netInflow >= GetDemand(i_day, i_node) * uncertianDemandMultipliers[i_uncert], $"Time{i_day}Uncertain{i_uncert}Node{i_node}DemandConstraint");
                        } else {
                            ref Supplier supplier = ref Suppliers[i_supplier];
                            GRBVar suppliedAmount = model.AddVar(0, double.PositiveInfinity, default, GRB.CONTINUOUS, $"Time{i_day}Uncertain{i_uncert}Supplier{i_supplier}SuppliedAmount");
                            variables_time_supplier_uncert_supply[i_day, i_supplier, i_uncert] = suppliedAmount;
                            if (supplierUpgrade) {
                                GRBLinExpr additionalCapacity = new GRBLinExpr();
                                for (int i_option = 0; i_option < UpgradeOptionCount; i_option++) {
                                    double capacity = UpgradeOptions[i_supplier, i_option].AdditionalCapacity;
                                    additionalCapacity.AddTerm(capacity, variables_supplier_upgradeOption_immediate[i_supplier, i_option]);
                                    if (upgradeDelayDiscount && IsDelayDay(i_day)) {
                                        additionalCapacity.AddTerm(capacity, variables_supplier_upgradeOption_uncert_delay[i_supplier, i_option, i_uncert]);
                                    }
                                }
                                model.AddConstr(suppliedAmount <= additionalCapacity + supplier.Capacity, $"Time{i_day}Uncertain{i_uncert}Supplier{i_supplier}CapacityConstraint");
                            } else {
                                model.AddConstr(suppliedAmount <= supplier.Capacity, $"Time{i_day}Uncertain{i_uncert}Supplier{i_supplier}CapacityConstraint");
                            }
                            model.AddConstr(netInflow + variables_time_supplier_uncert_supply[i_day, i_supplier, i_uncert] >= GetDemand(i_day, i_node) * uncertianDemandMultipliers[i_uncert], $"Time{i_day}Uncertain{i_uncert}Node{i_node}DemandConstraint");
                            expressions_time_uncert_dailyCost[i_day, i_uncert].Add(variables_time_supplier_uncert_supply[i_day, i_supplier, i_uncert] * supplier.Cost);
                        }
                    }
                    if (!IsDelayDay(i_day)) break; // Execute only once if this is an early day, no matter how many Uncertain are there.
                }
            }
            for (int i_supplier = 0; i_supplier < Suppliers.Length; i_supplier++) {
                GRBLinExpr[] expressions_uncert_totalSupplied = new GRBLinExpr[uncertianDemandMultipliers.Length];
                for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                    expressions_uncert_totalSupplied[i_uncert] = new();
                    for (int i_day = 0; i_day < DayAmount; i_day++) {
                        expressions_uncert_totalSupplied[i_uncert].AddTerm(1, variables_time_supplier_uncert_supply[i_day, i_supplier, IsDelayDay(i_day) ? i_uncert : 0]);
                    }
                    model.AddConstr(expressions_uncert_totalSupplied[i_uncert] <= supplierOverallCapacity, $"Supplier{i_supplier}Uncertain{i_uncert}OverallCapacityConstraint");
                }
            }
            for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                GRBLinExpr[] exprssions_uncert_totalImbalance = new GRBLinExpr[uncertianDemandMultipliers.Length];
                for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                    exprssions_uncert_totalImbalance[i_uncert] = new();
                    for (int i_day = 0; i_day < DayAmount; i_day++) {
                        int i_uncert_ = IsDelayDay(i_day) ? i_uncert : 0;
                        exprssions_uncert_totalImbalance[i_uncert].AddTerm(1, variables_time_pipeline_uncert_extraIn[i_day, i_pipeline, i_uncert_]);
                        exprssions_uncert_totalImbalance[i_uncert].AddTerm(-1, variables_time_pipeline_uncert_extraOut[i_day, i_pipeline, i_uncert_]);
                    }
                    model.AddConstr(exprssions_uncert_totalImbalance[i_uncert] == 0, $"Pipeline{i_pipeline}Uncertain{i_uncert}ImbalanceContraint");
                }
            }
            #endregion

            #region Objective
            GRBLinExpr[] expressions_uncert_totalCost = new GRBLinExpr[uncertianDemandMultipliers.Length];
            for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                expressions_uncert_totalCost[i_uncert] = new GRBLinExpr();
                for (int i_day = 0; i_day < DayAmount; i_day++) {
                    expressions_uncert_totalCost[i_uncert].Add(expressions_time_uncert_dailyCost[i_day, IsDelayDay(i_day) ? i_uncert : 0]);
                }
            }
            GRBLinExpr expression_suppliersImmediateUpgradeCost = new GRBLinExpr();
            GRBLinExpr[] expressions_uncert_suppliersDelayedUpgradeCost = new GRBLinExpr[uncertianDemandMultipliers.Length];
            if (supplierUpgrade) {
                for (int i_supplier = 0; i_supplier < Suppliers.Length; i_supplier++) {
                    for (int i_option = 0; i_option < UpgradeOptionCount; i_option++) {
                        double cost = UpgradeOptions[i_supplier, i_option].Cost;
                        expression_suppliersImmediateUpgradeCost.AddTerm(cost, variables_supplier_upgradeOption_immediate[i_supplier, i_option]);
                    }
                }
                for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                    expressions_uncert_totalCost[i_uncert].Add(expression_suppliersImmediateUpgradeCost);
                    if (upgradeDelayDiscount) {
                        expressions_uncert_suppliersDelayedUpgradeCost[i_uncert] = new GRBLinExpr();
                        for (int i_supplier = 0; i_supplier < Suppliers.Length; i_supplier++) {
                            for (int i_option = 0; i_option < UpgradeOptionCount; i_option++) {
                                double cost = UpgradeOptions[i_supplier, i_option].Cost;
                                expressions_uncert_suppliersDelayedUpgradeCost[i_uncert].AddTerm(cost * 0.7, variables_supplier_upgradeOption_uncert_delay[i_supplier, i_option, i_uncert]);
                            }
                        }
                        expressions_uncert_totalCost[i_uncert].Add(expressions_uncert_suppliersDelayedUpgradeCost[i_uncert]);
                    }
                }
            }
            GRBLinExpr expression_pipelinesImmediateUpgradeCost = new GRBLinExpr();
            GRBLinExpr[] expressions_uncert_pipelinesDelayedUpgradeCost = new GRBLinExpr[uncertianDemandMultipliers.Length];
            if (pipelineUpgrade) {
                for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                    double cost = 200000 * Pipelines[i_pipeline].Length;
                    expression_pipelinesImmediateUpgradeCost.AddTerm(cost, variables_pipeline_upgradeImmediate[i_pipeline]);
                }
                for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                    expressions_uncert_totalCost[i_uncert].Add(expression_pipelinesImmediateUpgradeCost);
                    if (upgradeDelayDiscount) {
                        expressions_uncert_pipelinesDelayedUpgradeCost[i_uncert] = new GRBLinExpr();
                        for (int i_pipeline = 0; i_pipeline < Pipelines.Length; i_pipeline++) {
                            double cost = 200000 * Pipelines[i_pipeline].Length;
                            expressions_uncert_pipelinesDelayedUpgradeCost[i_uncert].AddTerm(cost * 0.7, variables_pipeline_upgradeDelay[i_pipeline, i_uncert]);
                        }
                        expressions_uncert_totalCost[i_uncert].Add(expressions_uncert_pipelinesDelayedUpgradeCost[i_uncert]);
                    }
                }
            }

            GRBLinExpr objective = new GRBLinExpr();
            for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                objective.Add(expressions_uncert_totalCost[i_uncert] * (1d / uncertianDemandMultipliers.Length));
            }
            model.SetObjective(objective, GRB.MINIMIZE);
            #endregion

            model.Optimize();

            #region Logging
            if (logMode != LogMode.None) {

                string format_upgrades = "{0, -20}{1, -50}{2}";

                void LogUpgrades(bool isDelay, int i_uncert = default) {
                    Console.WriteLine();
                    Console.WriteLine(isDelay ? $"DelayedUncertain{i_uncert}" : "Immediate");
                    Console.WriteLine(format_upgrades, new string[] { "Upgrade", "|Description", "|Cost" });
                    if (supplierUpgrade) {
                        Console.WriteLine(
                            format_upgrades, new string[] {
                                "Suppliers",
                                "|" + string.Join(
                                    ", ", Enumerable.Range(0, Suppliers.Length).
                                    Select(i_supplier =>
                                        $"{Suppliers[i_supplier].Node}:" +
                                        string.Concat(
                                            Enumerable.Range(0, UpgradeOptionCount).
                                            Select<int, int>(
                                                isDelay ?
                                                i_option => (int)variables_supplier_upgradeOption_uncert_delay[i_supplier, i_option, i_uncert].X :
                                                i_option => (int)variables_supplier_upgradeOption_immediate[i_supplier, i_option].X
                                            )
                                        )
                                    )
                                ),
                                "|" + (isDelay ? expressions_uncert_suppliersDelayedUpgradeCost[i_uncert] : expression_suppliersImmediateUpgradeCost).Value.ToString()
                            }
                        );
                    }
                    if (pipelineUpgrade) {
                        int[] upgradedPipelines =
                            Enumerable.Range(0, Pipelines.Length).
                            Where<int>(
                                isDelay ?
                                i_pipeline => variables_pipeline_upgradeDelay[i_pipeline, i_uncert].X != 0 :
                                i_pipeline => variables_pipeline_upgradeImmediate[i_pipeline].X != 0
                            ).
                            ToArray();
                        Console.WriteLine(
                            format_upgrades, new string[] {
                                "Pipelines",
                                "|" + string.Join(
                                    ", ",
                                    upgradedPipelines.Select(i_pipeline => $"{i_pipeline}({Pipelines[i_pipeline].Node0},{Pipelines[i_pipeline].Node1})")
                                ),
                                "|" + (isDelay ? expressions_uncert_pipelinesDelayedUpgradeCost[i_uncert] : expression_pipelinesImmediateUpgradeCost).Value.ToString()
                            }

                        );
                    }
                    if (supplierUpgrade && pipelineUpgrade) {
                        Console.WriteLine(
                            format_upgrades, new string[] {
                                "Total",
                                "|" + "",
                                "|" + (isDelay ?
                                    (expressions_uncert_suppliersDelayedUpgradeCost[i_uncert].Value + expressions_uncert_pipelinesDelayedUpgradeCost[i_uncert].Value) :
                                    (expression_suppliersImmediateUpgradeCost.Value + expression_pipelinesImmediateUpgradeCost.Value))
                            }
                        );
                        Console.WriteLine();
                    }
                }

                string format_days = "{0,-5}{1,-10}{2,-20}{3,-40}{4,-40}{5}";

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
                            for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                                LogUpgrades(true, i_uncert);
                            }
                            delayUpgradeLogged = true;
                        }
                    }
                    for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                        Console.WriteLine();
                        Console.WriteLine($"Day{i_day},{(IsDelayDay(i_day) ? $"Uncertain{i_uncert}" : "")},Cost:{expressions_time_uncert_dailyCost[i_day, i_uncert].Value}");
                        if (logMode == LogMode.Brief) continue;
                        Console.WriteLine(format_days, "Node", "|Demand", "|Actual", "|Inflows", "|Outflows", "|Errors");
                        List<LogLine> lines = new List<LogLine>();
                        for (int i_node = 0; i_node < Nodes.Length; i_node++) {
                            ref Node node = ref Nodes[i_node];
                            var outflows = node.OutPipelines.Select(i_pipeline => (Pipelines[i_pipeline].Node1, variables_time_pipeline_uncert_flow[i_day, i_pipeline, i_uncert].X, variables_time_pipeline_uncert_extraIn[i_day, i_pipeline, i_uncert].X)).ToArray();
                            var inflows = node.InPipelines.Select(i_pipeline => (Pipelines[i_pipeline].Node0, variables_time_pipeline_uncert_flow[i_day, i_pipeline, i_uncert].X, variables_time_pipeline_uncert_extraOut[i_day, i_pipeline, i_uncert].X)).ToArray();
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
                        foreach (LogLine line in lines) line.Write(format_days);

                        if (!IsDelayDay(i_day)) break;
                    }
                }
                Console.WriteLine();

                string format_uncertains = "{0, -15}{1, -30}{2, -30}{3}";
                Console.WriteLine(format_uncertains, new string[] { "Unicertainity", "|TotalUpgradeCost", "|DailyCostsSum", "|TotalCost" });

                double[] totalUpgradeCosts =
                    Enumerable.Range(0, uncertianDemandMultipliers.Length).
                    Select(i_uncert =>
                        expression_suppliersImmediateUpgradeCost.Value + expressions_uncert_suppliersDelayedUpgradeCost[i_uncert].Value +
                        expression_pipelinesImmediateUpgradeCost.Value + expressions_uncert_pipelinesDelayedUpgradeCost[i_uncert].Value
                    ).
                    ToArray();

                double[] dailyCostSums =
                    Enumerable.Range(0, uncertianDemandMultipliers.Length).
                    Select(i_uncert =>
                        Enumerable.Range(1, DayAmount - 1).
                        Select(i_day => expressions_time_uncert_dailyCost[i_day, i_uncert].Value).
                        Sum() +
                        expressions_time_uncert_dailyCost[0, 0].Value
                    ).
                    ToArray();

                for (int i_uncert = 0; i_uncert < uncertianDemandMultipliers.Length; i_uncert++) {
                    string[] toWrite = new string[4];
                    toWrite[0] = i_uncert.ToString();
                    if (supplierUpgrade || pipelineUpgrade) {
                        toWrite[1] = "|" + totalUpgradeCosts[i_uncert].ToString();
                    } else {
                        toWrite[1] = "|";
                    }
                    toWrite[2] = "|" + dailyCostSums[i_uncert].ToString();
                    toWrite[3] = "|" + expressions_uncert_totalCost[i_uncert].Value.ToString();
                    Console.WriteLine(format_uncertains, toWrite);
                }
                string[] toWrite_average = new string[4];
                toWrite_average[0] = "Average";
                toWrite_average[1] = "|" + totalUpgradeCosts.Average().ToString();
                toWrite_average[2] = "|" + dailyCostSums.Average().ToString();
                toWrite_average[3] = "|" + model.ObjVal;
                //toWrite_average[3] = "|" + expressions_uncert_totalCost.Select(x => x.Value).Average().ToString();
                Console.WriteLine(format_uncertains, toWrite_average);
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
