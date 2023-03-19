using Gurobi;
using MATH3202Assignment1;


Action[] Communications = {
    () => Model.FromFile1().FindOptimal(),
    () => Model.FromFile1().FindOptimal(pipelineLimit: 313),
    () => Model.FromFile2().FindOptimal(pipelineLimit: 313),
    () => Model.FromFile2().FindOptimal(pipelineLimit: 313, supplierLimit: 7688)
};

Communications[3]();
//Model.FromFile2SingleDay(13).FindOptimal();
//Model.FromFile1Repeat().FindOptimal();
