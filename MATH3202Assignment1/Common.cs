using Gurobi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace MATH3202Assignment1 {
    internal static class Common {
        internal static string Directory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;

        internal static GRBEnv Env = new GRBEnv();

        internal static Pipeline[] Pipelines { get; }

        internal static Supplier[] Suppliers { get; } = {
            new(18, 334, 80),
            new(21, 746, 80),
            new(37, 767, 79),
            new(47, 537, 64)
        };

        internal const int UpgradeOptionCount = 3;
        internal static UpgradeOption[,] UpgradeOptions { get; } = {
            {new(70, 7078000), new(172, 16121000), new (332, 31947000)},
            {new(147, 14790000), new(368, 34998000), new (735, 70057000)},
            {new(158, 15742000), new(381, 38909000), new (771, 71020000)},
            {new(104, 10640000), new(266, 24392000), new (532, 51764000)}
        };

        internal static IEnumerable<string> ReadLines(string fileName) {
            StreamReader reader;
            string? line;
            reader = new(Directory + @"\" + fileName);
            reader.ReadLine();
            while ((line = reader.ReadLine()) is not null) {
                yield return line;
            }
        }

        internal static IEnumerable<(int, int)> EnumberableRangeTuple(int x, int y) {
            for (int i = 0; i < x; i++) {
                for (int j = 0; j < y; j++) {
                    yield return (i, j);
                }
            }
        }

        static Common() {
            Pipelines =
                ReadLines("pipelines.csv").
                Select(str => {
                    string[] strs = str.Split(',');
                    return new Pipeline() { Node0 = int.Parse(strs[1]), Node1 = int.Parse(strs[2]) };
                }).
                ToArray();
        }
    }
    internal struct Pipeline {
        public int Node0;
        public int Node1;
        public double Length;
    }

    internal struct Node {
        public double X;
        public double Y;
        public int[] OutPipelines;
        public int[] InPipelines;
    }

    internal struct Supplier {
        public Supplier(int node, double capacity, double cost) {
            Node = node;
            Capacity = capacity;
            Cost = cost;
        }
        public int Node;
        public double Capacity;
        public double Cost;
    }

    internal struct UpgradeOption {
        public UpgradeOption(double capacity, double cost) {
            AdditionalCapacity = capacity;
            Cost = cost;
        }
        public double AdditionalCapacity { get; }
        public double Cost { get; }
    }
}
