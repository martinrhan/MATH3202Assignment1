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

        internal static IEnumerable<string> ReadLines(string fileName) {
            StreamReader reader;
            string? line;
            reader = new(Directory + @"\" + fileName);
            reader.ReadLine();
            while ((line = reader.ReadLine()) is not null) {
                yield return line;
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
}
