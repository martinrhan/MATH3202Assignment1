using Gurobi;
using MATH3202Assignment1;


Action[] Communications = {
    () => Model.FromFile1().FindOptimal(false),
    () => Model.FromFile1().FindOptimal(),
    () => Console.WriteLine(Model.FromFile2().Select(model => model.FindOptimal()).Sum())

};
Communications[2]();
