using Gurobi;
using MATH3202Assignment1;


Action[] Communications = {
    () => Model.FromFile1().FindOptimal(),
    () => Model.FromFile1().FindOptimal(pipelineCapacity: 313),
    () => Model.FromFile2().FindOptimal(pipelineCapacity: 313, logMode:LogMode.Brief),
    () => Model.FromFile2().FindOptimal(pipelineCapacity: 313, supplierOverallCapacity: 7688, logMode:LogMode.Brief),
    () => Model.FromFile2().FindOptimal(pipelineCapacity: 313, supplierOverallCapacity: 7688, pipelineImbalanceLimit:double.PositiveInfinity),
    () => Model.FromFile3().FindOptimal(supplierUpgrade: true),
    () => Model.FromFile3().FindOptimal(pipelineCapacity: 275, supplierUpgrade: true, pipelineUpgrade: true),
    () => Model.FromFile3().FindOptimal(pipelineCapacity: 275, supplierUpgrade: true, pipelineUpgrade: true, upgradeDelayDiscount:true)
};

Communications[7]();
//Model.FromFile2SingleDay(13).FindOptimal();
//Model.FromFile1Repeat().FindOptimal();
//Model.FromFile3().FindOptimal(pipelineCapacity: 550, supplierUpgrade: true);
