﻿/* =======================================================================
Copyright 2017 Technische Universitaet Darmstadt, Fachgebiet fuer Stroemungsdynamik (chair of fluid dynamics)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using System;
using System.Collections.Generic;
using BoSSS.Platform;
using BoSSS.Solution.Control;
using BoSSS.Foundation.Grid;
using System.Diagnostics;
using BoSSS.Solution.AdvancedSolvers;
using ilPSP.Utils;
using BoSSS.Foundation.Grid.Classic;
using ilPSP;
using BoSSS.Solution.XdgTimestepping;

namespace BoSSS.Application.FSI_Solver
{
    public class HardcodedControl_straightChannel : IBM_Solver.HardcodedTestExamples
    {
        public static FSI_Control ActiveRod_noBackroundFlow(int k = 2, double stressM = 1e5, double cellAgg = 0.2, double muA = 1e5, double timestepX = 1e-3)
        {
            FSI_Control C = new FSI_Control();


            // General scaling parameter
            // =============================
            const double BaseSize = 1.0;


            // basic database options
            // =============================
            C.DbPath = @"\\hpccluster\hpccluster-scratch\deussen\cluster_db\straightChannel";
            C.savetodb = false;
            C.saveperiod = 1;
            C.ProjectName = "activeRod_noBackroundFlow";
            C.ProjectDescription = "Active";
            C.SessionName = C.ProjectName;
            C.Tags.Add("with immersed boundary method");


            // DG degrees
            // =============================
            C.SetDGdegree(k);


            // Grid 
            // =============================
            //Generating grid
            C.GridFunc = delegate
            {

                int q = new int(); // #Cells in x-dircetion + 1
                int r = new int(); // #Cells in y-dircetion + 1

                q = 60;
                r = 30;

                double[] Xnodes = GenericBlas.Linspace(-2.5 * BaseSize, 12.5 * BaseSize, q);
                double[] Ynodes = GenericBlas.Linspace(-3.75 * BaseSize, 3.75 * BaseSize, r);

                var grd = Grid2D.Cartesian2DGrid(Xnodes, Ynodes, periodicX: true, periodicY: false);

                grd.EdgeTagNames.Add(1, "Wall_left");
                grd.EdgeTagNames.Add(2, "Wall_right");
                grd.EdgeTagNames.Add(3, "Wall_lower");
                grd.EdgeTagNames.Add(4, "Wall_upper");


                grd.DefineEdgeTags(delegate (double[] X)
                {
                    byte et = 0;
                    if (Math.Abs(X[0] - (-2.5 * BaseSize)) <= 1.0e-8)
                        et = 1;
                    if (Math.Abs(X[0] + (-12.5 * BaseSize)) <= 1.0e-8)
                        et = 2;
                    if (Math.Abs(X[1] - (-3.75 * BaseSize)) <= 1.0e-8)
                        et = 3;
                    if (Math.Abs(X[1] + (-3.75 * BaseSize)) <= 1.0e-8)
                        et = 4;

                    Debug.Assert(et != 0);
                    return et;
                });

                Console.WriteLine("Cells:" + grd.NumberOfCells);

                return grd;
            };


            // Mesh refinement
            // =============================
            C.AdaptiveMeshRefinement = false;
            C.RefinementLevel = 2;
            C.maxCurvature = 2;


            // Boundary conditions
            // =============================
            C.AddBoundaryValue("Wall_left");//, "VelocityX", X => 0.0);
            C.AddBoundaryValue("Wall_right");//, "VelocityX", X => 0.0);
            C.AddBoundaryValue("Wall_lower");
            C.AddBoundaryValue("Wall_upper");
            

            // Fluid Properties
            // =============================
            C.PhysicalParameters.rho_A = 1;//pg/(mum^3)
            C.PhysicalParameters.mu_A = muA;//pg(mum*s)
            C.PhysicalParameters.Material = true;


            // Particle Properties
            // =============================   
            // Defining particles
            C.Particles = new List<Particle>();
            int numOfParticles = 1;
            for (int d = 0; d < numOfParticles; d++)
            {
                C.Particles.Add(new Particle_Ellipsoid(new double[] { 1 + 8 * d, 0 }, startAngl: 45 - 180 * d)
                {
                    particleDensity = 1,
                    ActiveParticle = true,
                    ActiveStress = stressM,
                    thickness_P = 0.4 * BaseSize,
                    length_P = 2 * BaseSize,
                    AddaptiveUnderrelaxation = true,
                    underrelaxation_factor = 0.01,
                    ClearSmallValues = true,
                    neglectAddedDamping = false
                });
            }


            // Quadrature rules
            // =============================   
            C.CutCellQuadratureType = Foundation.XDG.XQuadFactoryHelper.MomentFittingVariants.Saye;


            //Initial Values
            // =============================   
            //C.InitialValues_Evaluators.Add("Phi", X => phiComplete(X, 0));
            C.InitialValues_Evaluators.Add("VelocityX", X => 0);
            C.InitialValues_Evaluators.Add("VelocityY", X => 0);


            // For restart
            // =============================  
            //C.RestartInfo = new Tuple<Guid, TimestepNumber>(new Guid("42c82f3c-bdf1-4531-8472-b65feb713326"), 400);
            //C.GridGuid = new Guid("f1659eb6 -b249-47dc-9384-7ee9452d05df");


            // Physical Parameters
            // =============================  
            C.PhysicalParameters.IncludeConvection = false;


            // misc. solver options
            // =============================  
            C.AdvancedDiscretizationOptions.PenaltySafety = 4;
            C.AdvancedDiscretizationOptions.CellAgglomerationThreshold = cellAgg;
            C.LevelSetSmoothing = false;
            C.NonLinearSolver.MaxSolverIterations = 1000;
            C.NonLinearSolver.MinSolverIterations = 1;
            C.LinearSolver.NoOfMultigridLevels = 1;
            C.LinearSolver.MaxSolverIterations = 1000;
            C.LinearSolver.MinSolverIterations = 1;
            C.ForceAndTorque_ConvergenceCriterion = 1e2;
            C.LSunderrelax = 1.0;
            

            // Coupling Properties
            // =============================
            C.Timestepper_LevelSetHandling = LevelSetHandling.FSI_LieSplittingFullyCoupled;
            C.LSunderrelax = 1;
            C.max_iterations_fully_coupled = 100000;
            C.includeRotation = true;
            C.includeTranslation = true;


            // Timestepping
            // =============================  
            C.instationarySolver = true;
            C.Timestepper_Scheme = IBM_Solver.IBM_Control.TimesteppingScheme.BDF2;
            double dt = timestepX;
            C.dtMax = dt;
            C.dtMin = dt;
            C.Endtime = 1000000000;
            C.NoOfTimesteps = 1000000000;
            
            // haben fertig...
            // ===============

            return C;
        }

        public static FSI_Control ActiveRod_withBackroundFlow(int k = 2, double stressM = 1e5, double cellAgg = 0.2, double muA = 1e4, double timestepX = 1e-3)
        {
            FSI_Control C = new FSI_Control();


            // General scaling parameter
            // =============================
            const double BaseSize = 1.0;


            // basic database options
            // =============================
            C.DbPath = @"\\hpccluster\hpccluster-scratch\deussen\cluster_db\straightChannel";
            C.savetodb = false;
            C.saveperiod = 1;
            C.ProjectName = "activeRod_noBackroundFlow";
            C.ProjectDescription = "Active";
            C.SessionName = C.ProjectName;
            C.Tags.Add("with immersed boundary method");


            // DG degrees
            // =============================
            C.SetDGdegree(k);


            // Grid 
            // =============================
            //Generating grid
            C.GridFunc = delegate
            {

                int q = new int(); // #Cells in x-dircetion + 1
                int r = new int(); // #Cells in y-dircetion + 1

                q = 75;
                r = 30;

                double[] Xnodes = GenericBlas.Linspace(-2 * BaseSize, 13 * BaseSize, q);
                double[] Ynodes = GenericBlas.Linspace(-3 * BaseSize, 3 * BaseSize, r);

                var grd = Grid2D.Cartesian2DGrid(Xnodes, Ynodes, periodicX: false, periodicY: false);

                grd.EdgeTagNames.Add(1, "Velocity_Inlet_left");
                grd.EdgeTagNames.Add(2, "Pressure_Outlet_right");
                grd.EdgeTagNames.Add(3, "Wall_lower");
                grd.EdgeTagNames.Add(4, "Wall_upper");


                grd.DefineEdgeTags(delegate (double[] X)
                {
                    byte et = 0;
                    if (Math.Abs(X[0] - (-2 * BaseSize)) <= 1.0e-8)
                        et = 1;
                    if (Math.Abs(X[0] + (-13 * BaseSize)) <= 1.0e-8)
                        et = 2;
                    if (Math.Abs(X[1] - (-3 * BaseSize)) <= 1.0e-8)
                        et = 3;
                    if (Math.Abs(X[1] + (-3 * BaseSize)) <= 1.0e-8)
                        et = 4;

                    Debug.Assert(et != 0);
                    return et;
                });

                Console.WriteLine("Cells:" + grd.NumberOfCells);

                return grd;
            };


            // Mesh refinement
            // =============================
            C.AdaptiveMeshRefinement = true;
            C.RefinementLevel = 2;
            C.maxCurvature = 2;


            // Boundary conditions
            // =============================
            C.AddBoundaryValue("Velocity_Inlet_left", "VelocityX", X => 0.2);
            C.AddBoundaryValue("Pressure_Outlet_right");//, "VelocityX", X => 0.0);
            C.AddBoundaryValue("Wall_lower");
            C.AddBoundaryValue("Wall_upper");


            // Fluid Properties
            // =============================
            C.PhysicalParameters.rho_A = 1;//pg/(mum^3)
            C.PhysicalParameters.mu_A = muA;//pg(mum*s)
            C.PhysicalParameters.Material = true;


            // Particle Properties
            // =============================   
            // Defining particles
            C.Particles = new List<Particle>();
            int numOfParticles = 1;
            for (int d = 0; d < numOfParticles; d++)
            {
                C.Particles.Add(new Particle_Ellipsoid(new double[] { 1 + 2 * d, 0.25 }, startAngl: -8 + 12 * d)
                {
                    particleDensity = 1,
                    ActiveParticle = false,
                    ActiveStress = stressM,
                    thickness_P = 0.5 * BaseSize,
                    length_P = 2.5 * BaseSize,
                    AddaptiveUnderrelaxation = true,// set true if you want to define a constant underrelaxation (not recommended)
                    underrelaxation_factor = 0.2,// underrelaxation with [factor * 10^exponent]
                    ClearSmallValues = true,
                    neglectAddedDamping = false
                });
            }
            //Define level-set
            double phiComplete(double[] X, double t)
            {
                //Generating the correct sign
                int exp = C.Particles.Count - 1;
                double ret = Math.Pow(-1, exp);
                //Level-set function depending on # of particles
                for (int i = 0; i < C.Particles.Count; i++)
                {
                    ret *= C.Particles[i].Phi_P(X);
                }
                return ret;
            }


            // Quadrature rules
            // =============================   
            C.CutCellQuadratureType = Foundation.XDG.XQuadFactoryHelper.MomentFittingVariants.Saye;


            //Initial Values
            // =============================   
            C.InitialValues_Evaluators.Add("Phi", X => phiComplete(X, 0));
            C.InitialValues_Evaluators.Add("VelocityX", X => 0);
            C.InitialValues_Evaluators.Add("VelocityY", X => 0);


            // For restart
            // =============================  
            //C.RestartInfo = new Tuple<Guid, TimestepNumber>(new Guid("42c82f3c-bdf1-4531-8472-b65feb713326"), 400);
            //C.GridGuid = new Guid("f1659eb6 -b249-47dc-9384-7ee9452d05df");


            // Physical Parameters
            // =============================  
            C.PhysicalParameters.IncludeConvection = false;


            // misc. solver options
            // =============================  
            C.AdvancedDiscretizationOptions.PenaltySafety = 4;
            C.AdvancedDiscretizationOptions.CellAgglomerationThreshold = cellAgg;
            C.LevelSetSmoothing = false;
            C.NonLinearSolver.MaxSolverIterations = 1000;
            C.NonLinearSolver.MinSolverIterations = 1;
            C.LinearSolver.NoOfMultigridLevels = 1;
            C.LinearSolver.MaxSolverIterations = 1000;
            C.LinearSolver.MinSolverIterations = 1;
            C.ForceAndTorque_ConvergenceCriterion = 100;
            C.LSunderrelax = 1.0;


            // Coupling Properties
            // =============================
            C.Timestepper_LevelSetHandling = LevelSetHandling.LieSplitting;
            C.LSunderrelax = 1;
            C.max_iterations_fully_coupled = 10000;
            C.includeRotation = true;
            C.includeTranslation = true;


            // Timestepping
            // =============================  
            C.instationarySolver = true;
            C.Timestepper_Scheme = FSI_Solver.FSI_Control.TimesteppingScheme.BDF2;
            double dt = timestepX;
            C.dtMax = dt;
            C.dtMin = dt;
            C.Endtime = 1000000;
            C.NoOfTimesteps = 10000;

            // haben fertig...
            // ===============

            return C;
        }
    }
}