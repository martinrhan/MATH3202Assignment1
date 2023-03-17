using Gurobi;
using MATH3202Assignment1;


Action[] Communications = {
    () => Model.FromFile1().FindOptimal(false),
    () => Model.FromFile1().FindOptimal(),
    () => Model.FromFile2().FindOptimal(),

};

Communications[2]();
//Model.FromFile1Repeat().FindOptimal();
