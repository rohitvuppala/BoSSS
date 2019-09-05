﻿/* =======================================================================
Copyright 2019 Technische Universitaet Darmstadt, Fachgebiet fuer Stroemungsdynamik (chair of fluid dynamics)

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
    public class HardcodedControlDeriabina : IBM_Solver.HardcodedTestExamples
    {
        public static FSI_Control DeriabinaHefezelleWORefinement(int k = 2)
        {
            FSI_Control C = new FSI_Control();


            const double BaseSize = 1.0;


            // basic database options
            // ======================

            C.DbPath = @"\\hpccluster\hpccluster-scratch\deussen\cluster_db\Deriabina";
            C.savetodb = false;
            C.saveperiod = 1;
            C.ProjectName = "ParticleCollisionTest";
            C.ProjectDescription = "Gravity";
            C.SessionName = C.ProjectName;
            C.Tags.Add("with immersed boundary method");
            C.AdaptiveMeshRefinement = false;
            C.SessionName = "abc";
            C.RefinementLevel = 3;

            C.pureDryCollisions = false;
            C.SetDGdegree(k);

            // grid and boundary conditions
            // ============================

            C.GridFunc = delegate
            {

                int q = new int();
                int r = new int();

                q = 35;
                r = 140;

                double[] Xnodes = GenericBlas.Linspace(-1.0 * BaseSize, 1.0 * BaseSize, q + 1);
                double[] Ynodes = GenericBlas.Linspace(2 * BaseSize, 10 * BaseSize, r + 1);

                var grd = Grid2D.Cartesian2DGrid(Xnodes, Ynodes, periodicX: false, periodicY: false);

                grd.EdgeTagNames.Add(1, "Wall_left");
                grd.EdgeTagNames.Add(2, "Wall_right");
                grd.EdgeTagNames.Add(3, "Pressure_Outlet");
                grd.EdgeTagNames.Add(4, "Wall_upper");


                grd.DefineEdgeTags(delegate (double[] X)
                {
                    byte et = 0;
                    if (Math.Abs(X[0] - (-1.0 * BaseSize)) <= 1.0e-8)
                        et = 1;
                    if (Math.Abs(X[0] + (-1.0 * BaseSize)) <= 1.0e-8)
                        et = 2;
                    if (Math.Abs(X[1] - (2 * BaseSize)) <= 1.0e-8)
                        et = 3;
                    if (Math.Abs(X[1] + (-10 * BaseSize)) <= 1.0e-8)
                        et = 4;


                    return et;
                });

                Console.WriteLine("Cells:" + grd.NumberOfCells);

                return grd;
            };

            C.GridPartType = GridPartType.Hilbert;

            C.AddBoundaryValue("Wall_left");
            C.AddBoundaryValue("Wall_right");
            C.AddBoundaryValue("Pressure_Outlet");
            C.AddBoundaryValue("Wall_upper");



            // Initial Values
            // ==============

            // Coupling Properties
            C.Timestepper_LevelSetHandling = LevelSetHandling.FSI_LieSplittingFullyCoupled;

            // Fluid Properties
            C.PhysicalParameters.rho_A = 1.0;
            C.PhysicalParameters.mu_A = 0.01;
            C.CoefficientOfRestitution = 1;


            C.Particles.Add(new Particle_Sphere( 0.1, new double[] { 0.0, 9.5 })
            {
                particleDensity = 1.01,
                GravityVertical = -9.81,
                useAddaptiveUnderrelaxation = true,
                underrelaxation_factor = 3.0,
                clearSmallValues = true,
                UseAddedDamping = true
            });

            C.Particles.Add(new Particle_Sphere( 0.1, new double[] { 0.0, 9.1 })
            {
                particleDensity = 1.01,
                GravityVertical = -9.81,
                useAddaptiveUnderrelaxation = true,
                underrelaxation_factor = 3.0,
                clearSmallValues = true,
                UseAddedDamping = true
            });

            C.InitialValues_Evaluators.Add("VelocityX", X => 0);
            C.InitialValues_Evaluators.Add("VelocityY", X => 0);


            // Physical Parameters
            // ===================

            C.PhysicalParameters.IncludeConvection = false;

            // misc. solver options
            // ====================

            C.AdvancedDiscretizationOptions.PenaltySafety = 4;
            C.AdvancedDiscretizationOptions.CellAgglomerationThreshold = 0.2;
            C.LevelSetSmoothing = false;
            C.LinearSolver.MaxSolverIterations = 10;
            C.NonLinearSolver.MaxSolverIterations = 10;
            C.LinearSolver.NoOfMultigridLevels = 1;
            C.forceAndTorqueConvergenceCriterion = 5e-3;


            // Timestepping
            // ============

            //C.Timestepper_Mode = FSI_Control.TimesteppingMode.Splitting;
            C.Timestepper_Scheme = FSI_Solver.FSI_Control.TimesteppingScheme.BDF2;
            double dt = 5e-4;
            C.dtMax = dt;
            C.dtMin = dt;
            C.Endtime = 1000000.0;
            C.NoOfTimesteps = 1000000000;

            // haben fertig...
            // ===============

            return C;
        }

        public static FSI_Control DeriabinaHefezelleWRefinement(int k = 2)
        {
            FSI_Control C = new FSI_Control();


            const double BaseSize = 1.0;


            // basic database options
            // ======================

            C.DbPath = @"D:\BoSSS_databases\wetParticleCollision";
            C.saveperiod = 1;
            C.ProjectName = "ParticleCollisionTest";
            C.ProjectDescription = "Gravity";
            C.SessionName = C.ProjectName;
            C.Tags.Add("with immersed boundary method");
            C.AdaptiveMeshRefinement = true;
            C.SessionName = "fjkfjksdfhjk";
            C.RefinementLevel = 3;

            C.pureDryCollisions = false;
            C.SetDGdegree(k);

            // grid and boundary conditions
            // ============================

            C.GridFunc = delegate
            {

                int q = new int();
                int r = new int();

                r = 80;
                q = r / 4;

                double[] Xnodes = GenericBlas.Linspace(-1.0 * BaseSize, 1.0 * BaseSize, q + 1);
                double[] Ynodes = GenericBlas.Linspace(2 * BaseSize, 10 * BaseSize, r + 1);

                var grd = Grid2D.Cartesian2DGrid(Xnodes, Ynodes, periodicX: false, periodicY: false);

                grd.EdgeTagNames.Add(1, "Wall_left");
                grd.EdgeTagNames.Add(2, "Wall_right");
                grd.EdgeTagNames.Add(3, "Pressure_Outlet");
                grd.EdgeTagNames.Add(4, "Wall_upper");


                grd.DefineEdgeTags(delegate (double[] X)
                {
                    byte et = 0;
                    if (Math.Abs(X[0] - (-1.0 * BaseSize)) <= 1.0e-8)
                        et = 1;
                    if (Math.Abs(X[0] + (-1.0 * BaseSize)) <= 1.0e-8)
                        et = 2;
                    if (Math.Abs(X[1] - (2 * BaseSize)) <= 1.0e-8)
                        et = 3;
                    if (Math.Abs(X[1] + (-10 * BaseSize)) <= 1.0e-8)
                        et = 4;


                    return et;
                });

                Console.WriteLine("Cells:" + grd.NumberOfCells);

                return grd;
            };

            C.GridPartType = GridPartType.Hilbert;

            C.AddBoundaryValue("Wall_left");
            C.AddBoundaryValue("Wall_right");
            C.AddBoundaryValue("Pressure_Outlet");
            C.AddBoundaryValue("Wall_upper");



            // Initial Values
            // ==============

            // Coupling Properties
            C.Timestepper_LevelSetHandling = LevelSetHandling.FSI_LieSplittingFullyCoupled;

            // Fluid Properties
            C.PhysicalParameters.rho_A = 1.0;
            C.PhysicalParameters.mu_A = 0.01;
            C.CoefficientOfRestitution = 1;


            C.Particles.Add(new Particle_Sphere( 0.1, new double[] { 0.0, 9.5 })
            {
                particleDensity = 1.01,
                GravityVertical = -9.81,
                useAddaptiveUnderrelaxation = true,
                underrelaxation_factor = 3.0,
                clearSmallValues = true,
                UseAddedDamping = true
            });

            C.Particles.Add(new Particle_Sphere( 0.1, new double[] { 0.0, 9.1 })
            {
                particleDensity = 1.01,
                GravityVertical = -9.81,
                useAddaptiveUnderrelaxation = true,
                underrelaxation_factor = 3.0,
                clearSmallValues = true,
                UseAddedDamping = true
            });

            C.InitialValues_Evaluators.Add("VelocityX", X => 0);
            C.InitialValues_Evaluators.Add("VelocityY", X => 0);


            // Physical Parameters
            // ===================

            C.PhysicalParameters.IncludeConvection = false;

            // misc. solver options
            // ====================

            C.AdvancedDiscretizationOptions.PenaltySafety = 4;
            C.AdvancedDiscretizationOptions.CellAgglomerationThreshold = 0.2;
            C.LevelSetSmoothing = false;
            C.LinearSolver.MaxSolverIterations = 10;
            C.NonLinearSolver.MaxSolverIterations = 10;
            C.LinearSolver.NoOfMultigridLevels = 1;
            C.forceAndTorqueConvergenceCriterion = 2e-3;


            // Timestepping
            // ============

            //C.Timestepper_Mode = FSI_Control.TimesteppingMode.Splitting;
            C.Timestepper_Scheme = FSI_Solver.FSI_Control.TimesteppingScheme.BDF2;
            double dt = 1e-3;
            C.dtMax = dt;
            C.dtMin = dt;
            C.Endtime = 1000000.0;
            C.NoOfTimesteps = 1000000000;

            // haben fertig...
            // ===============

            return C;
        }


    }
}
