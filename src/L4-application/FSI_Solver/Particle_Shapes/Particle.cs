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
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using BoSSS.Solution.Control;
using System.Runtime.InteropServices;
using BoSSS.Foundation.XDG;
using BoSSS.Foundation.Quadrature;
using ilPSP;
using ilPSP.Utils;
using BoSSS.Foundation;
using BoSSS.Foundation.Grid;

namespace BoSSS.Application.FSI_Solver
{

    /// <summary>
    /// Particle properties (for disk shape and spherical particles only).
    /// </summary>
    [DataContract]
    [Serializable]
    abstract public class Particle : ICloneable {

        #region particle
        public Particle(int Dim, int HistoryLength, double[] startPos = null, double startAngl = 0.0, ParticleShape shape = ParticleShape.spherical) {
            
            m_HistoryLength = HistoryLength;
            m_Dim = Dim;
            
            #region Particle history
            // =============================   
            for (int i = 0; i < HistoryLength; i++) {
                currentIterPos_P.Add(new double[Dim]);
                currentIterAng_P.Add(new double());
                currentIterVel_P.Add(new double[Dim]);
                currentIterRot_P.Add(new double());
                currentIterForces_P.Add(new double[Dim]);
                temporalForces_P.Add(new double[Dim]);
                currentIterTorque_P.Add(new double());
                temporalTorque_P.Add(new double());
            }
            for (int i = 0; i < 4; i++)
            {
                currentTimePos_P.Add(new double[Dim]);
                currentTimeAng_P.Add(new double());
                currentTimeVel_P.Add(new double[Dim]);
                currentTimeRot_P.Add(new double());
                currentTimeForces_P.Add(new double[Dim]);
                currentTimeTorque_P.Add(new double());
            }
            #endregion

            #region Initial values
            // ============================= 
            if (startPos == null)
            {
                if (Dim == 2)
                {
                    startPos = new double[] { 0.0, 0.0 };
                }
                else
                {
                    startPos = new double[] { 0.0, 0.0, 0.0 };
                }
            }
            currentTimePos_P[0] = startPos;
            currentTimePos_P[1] = startPos;
            //From degree to radiant
            currentTimeAng_P[0] = startAngl * 2 * Math.PI / 360;
            currentTimeAng_P[1] = startAngl * 2 * Math.PI / 360;
            //currentTimeVel_P[1][0] = 1e-2;
            //currentTimeVel_P[1][1] = 1e-2;

            UpdateLevelSetFunction();
            #endregion
        }
        #endregion

        #region Particle parameter
        /// <summary>
        /// empty constructor important for serialization
        /// </summary>
        private Particle() {
        }
        
        public bool[] m_collidedWithParticle;
        public bool[] m_collidedWithWall;
        public double[][] m_closeInterfacePointTo;


        public double[] tempPos_P = new double[2];
        public double tempAng_P;
        public int iteration_counter_P = 0;
        public bool underrelaxationFT_constant = true;
        public int underrelaxationFT_exponent = 0;
        public double underrelaxation_factor = 1;
        public int underrelaxationFT_exponent_max = 0;
        public double active_force_correction;

        /// <summary>
        /// Skip calculation of hydrodynamic force and torque if particles are too close
        /// </summary>
        public bool skipForceIntegration = false;

        /// <summary>
        /// Length of history for time, velocity, position etc.
        /// </summary>
        int m_HistoryLength;

        /// <summary>
        /// Dimension
        /// </summary>
        int m_Dim;

        /// <summary>
        /// Density of the particle.
        /// </summary>
        [DataMember]
        public double rho_P;

        /// <summary>
        /// Radius of the particle.
        /// </summary>
        [DataMember]
        public double radius_P;

        /// <summary>
        /// Lenght of an elliptic particle.
        /// </summary>
        [DataMember]
        public double length_P;

        /// <summary>
        /// Thickness of an elliptic particle.
        /// </summary>
        [DataMember]
        public double thickness_P;

        /// <summary>
        /// Thickness of an elliptic particle.
        /// </summary>
        [DataMember]
        public int superEllipsoidExponent;

        /// <summary>
        /// Current position of the particle (the center of mass)
        /// </summary>
        [DataMember]
        public List<double[]> currentIterPos_P = new List<double[]>();

        /// <summary>
        /// Current position of the particle (the center of mass)
        /// </summary>
        [DataMember]
        public List<double[]> currentTimePos_P = new List<double[]>();

        /// <summary>
        /// Current angle of the particle (the center of mass)
        /// </summary>
        [DataMember]
        public List<double> currentIterAng_P = new List<double>();

        /// <summary>
        /// Current angle of the particle (the center of mass)
        /// </summary>
        [DataMember]
        public List<double> currentTimeAng_P = new List<double>();

        /// <summary>
        /// Current translational Velocity of the particle
        /// </summary>
        [DataMember]
        public List<double[]> currentIterVel_P = new List<double[]>();

        /// <summary>
        /// Current translational Velocity of the particle
        /// </summary>
        [DataMember]
        public List<double[]> currentTimeVel_P = new List<double[]>();

        /// <summary>
        /// Current rotational Velocity of the particle
        /// </summary>
        [DataMember]
        public List<double> currentIterRot_P = new List<double>();

        /// <summary>
        /// Current rotational Velocity of the particle
        /// </summary>
        [DataMember]
        public List<double> currentTimeRot_P = new List<double>();

        /// <summary>
        /// Force acting on the particle
        /// </summary>
        [DataMember]
        public List<double[]> currentIterForces_P = new List<double[]>();

        /// <summary>
        /// Force acting on the particle
        /// </summary>
        [DataMember]
        public List<double[]> currentTimeForces_P = new List<double[]>();

        /// <summary>
        /// Force acting on the particle
        /// </summary>
        [DataMember]
        public List<double[]> temporalForces_P = new List<double[]>();

        /// <summary>
        /// Torque acting on the particle
        /// </summary>
        [DataMember]
        public List<double> currentIterTorque_P = new List<double>();

        /// <summary>
        /// Torque acting on the particle
        /// </summary>
        [DataMember]
        public List<double> currentTimeTorque_P = new List<double>();

        /// <summary>
        /// Torque acting on the particle
        /// </summary>
        [DataMember]
        public List<double> temporalTorque_P = new List<double>();

        /// <summary>
        /// Level set function describing the particle
        /// </summary>       
        public Func<double[], double, double> phi_P;

        /// <summary>
        /// Set true if the particle should be an active particle, i.e. self driven
        /// </summary>
        [DataMember]
        public bool active_P = false;

        /// <summary>
        /// Set true if the particle should be an active particle, i.e. self driven
        /// </summary>
        [DataMember]
        public bool includeGravity = true;

        /// <summary>
        /// Active stress on the current particle
        /// </summary>
        public double stress_magnitude_P;
        
        /// <summary>
        /// heaviside function depending on arclength 
        /// </summary>
        public double C_v = 0.5;

        /// <summary>
        /// heaviside function depending on arclength 
        /// </summary>
        public double velResidual_ConvergenceCriterion = 1e-6;

        /// <summary>
        /// heaviside function depending on arclength 
        /// </summary>
        public double forceAndTorque_convergence = 1e-8;

        /// <summary>
        /// heaviside function depending on arclength 
        /// </summary>
        public double MaxParticleVelIterations = 100;

        /// <summary>
        /// Active stress on the current particle
        /// </summary>
        abstract public double active_stress_P
        {
            get;
        }
        
        /// <summary>
        /// Mass of the current particle
        /// </summary>
        [DataMember]
        public double Mass_P {
            get
            {
                return Area_P * rho_P;
            }
        }

        /// <summary>
        /// Area of the current particle
        /// </summary>
        [DataMember]
        abstract public double Area_P
        {
            get;
        }

        /// <summary>
        /// Moment of inertia of the current particle
        /// </summary>
        [DataMember]
        abstract public double MomentOfInertia_P
        {
            get;
        }
        #endregion

        #region Particle history
        /// <summary>
        /// Clean all Particle histories until a certain length
        /// </summary>
        /// <param name="length"></param>
        // iteration
        public void CleanHistoryIter() {
            if (currentIterPos_P.Count > m_HistoryLength)
            {
                for (int j = currentIterPos_P.Count; j > m_HistoryLength; j--)
                {
                    int tempPos = j - 1;
                    currentIterPos_P.RemoveAt(tempPos);
                    currentIterVel_P.RemoveAt(tempPos);
                    currentIterForces_P.RemoveAt(tempPos);
                    currentIterRot_P.RemoveAt(tempPos);
                    currentIterTorque_P.RemoveAt(tempPos);
                    temporalForces_P.RemoveAt(tempPos);
                    temporalTorque_P.RemoveAt(tempPos);
                }
            }
        }
        // time
        public void CleanHistory()
        {
            if (currentTimePos_P.Count > 4)
            {
                for (int j = currentTimePos_P.Count; j > 4; j--)
                {
                    int tempPos = j - 1;
                    currentTimePos_P.RemoveAt(tempPos);
                    currentTimeAng_P.RemoveAt(tempPos);
                    currentTimeVel_P.RemoveAt(tempPos);
                    currentTimeForces_P.RemoveAt(tempPos);
                    currentTimeRot_P.RemoveAt(tempPos);
                    currentTimeTorque_P.RemoveAt(tempPos);
                }
            }
        }
        #endregion

        #region Move particle with current velocity
        /// <summary>
        /// Move particle with current velocity
        /// </summary>
        /// <param name="dt"></param>
        public void UpdateParticlePosition(double dt) {
            
            // Position
            var tempPos = currentIterPos_P[0].CloneAs();
            tempPos.AccV(dt, currentIterVel_P[0]);
            //currentIterPos_P.Insert(0, tempPos);
            //currentIterPos_P.Remove(currentIterPos_P.Last());
            currentIterPos_P[0] = tempPos;

            // Angle
            //var tempAng = currentIterAng_P[0] + dt * currentIterRot_P[0];
            //currentIterAng_P.Insert(0, tempAng);
            //currentIterAng_P.Remove(currentIterAng_P.Last());
            currentIterAng_P[0] = currentTimeAng_P[1] + dt * currentIterRot_P[0];

            currentTimePos_P[0] = currentIterPos_P[0];
            currentTimeAng_P[0] = currentIterAng_P[0];

            //Console.WriteLine("Current angle speed is " + currentIterRot_P[0]);// *360/Math.PI);
            //Console.WriteLine("Current angle is " + currentIterAng_P[0] + " rad");

            UpdateLevelSetFunction();
        }

        public void ResetParticlePosition()
        {
            // save position of the last timestep
            if (iteration_counter_P == 1)
            {
                currentTimePos_P.Insert(1, currentTimePos_P[0]);
                currentTimePos_P.Remove(currentTimePos_P.Last());
                currentTimeAng_P.Insert(1, currentTimeAng_P[0]);
                currentTimeAng_P.Remove(currentTimeAng_P.Last());
                //currentTimePos_P[1] = currentTimePos_P[0];
                //currentTimeAng_P[1] = currentTimeAng_P[0];
            }

            // Position
            currentIterPos_P.Insert(1, currentIterPos_P[0]);
            currentIterPos_P.Remove(currentIterPos_P.Last());
            currentIterPos_P[0] = currentTimePos_P[1];

            // Angle
            currentIterAng_P.Insert(1, currentTimeAng_P[0]);
            currentIterAng_P.Remove(currentIterAng_P.Last());
            currentIterAng_P[0] = currentTimeAng_P[1];

            UpdateLevelSetFunction();
        }
        #endregion

        #region Update Level-set
        abstract public void UpdateLevelSetFunction();
        #endregion

        #region Update translational velocity
        /// <summary>
        /// Calculate the new translational velocity of the particle using explicit Euler scheme.
        /// </summary>
        /// <param name="D"></param>
        /// <param name="dt"></param>
        /// <param name="oldTransVelocity"></param>
        /// <param name="particleMass"></param>
        /// <param name="force"></param>
        /// <returns></returns>
        public void UpdateTransVelocity(double dt, double rho_Fluid = 1, bool includeTranslation = true, bool LiftAndDrag = true) {

            // save velocity of the last timestep
            if (iteration_counter_P == 1)
            {
                currentTimeVel_P.Insert(1, currentTimeVel_P[0]);
                currentTimeVel_P.Remove(currentTimeVel_P.Last());
                //currentTimeVel_P[1] = currentIterVel_P[0];
            }

            // no translation
            // =============================
            if (includeTranslation == false)
            {
                currentIterVel_P[0][0] = 0.0;
                currentIterVel_P[0][1] = 0.0;
                return;
            }

            double[] temp = new double[2];
            double[] old_temp = new double[2];
            double[] tempForceTemp = new double[2];
            double[] tempForceNew = new double[2];
            double[] tempForceOld = new double[2];
            double[] gravity = new double[2];
            double massDifference = (rho_P - rho_Fluid) * (Area_P);
            double[] previous_vel = new double[2];

            if (iteration_counter_P == 1)
            {
                previous_vel = currentIterVel_P[0];
            }

            #region virtual force model
            // Virtual force model (Schwarz et al. - 2015 A temporal discretization scheme to compute the motion of light particles in viscous flows by an immersed boundary")
            // =============================
            double[] f_vTemp = new double[2];
            double[] f_vNew = new double[2];
            double[] f_vOld = new double[2];
            double[] k_1 = new double[2];
            double[] k_2 = new double[2];
            double[] k_3 = new double[2];
            double[] C_v_mod = new double[2];
            C_v_mod[0] = C_v;
            C_v_mod[1] = C_v;
            double[] c_a = new double[2];
            double[] c_u = new double[2];
            //double vel_iteration_counter = 0;
            double[] test = new double[2];
            // 2nd order Adam Bashford
            //for (int i = 0; i < 2; i++)
            //{
            //    f_vNew[i] = c_a * (3 * currentIterVel_P[0][i] - 4 * currentIterVel_P[1][i] + currentIterVel_P[2][i]) / (2 * dt);
            //    f_vOld[i] = c_a * (3 * currentIterVel_P[1][i] - 4 * currentIterVel_P[2][i] + currentIterVel_P[3][i]) / (2 * dt);
            //    tempForceNew[i] = (forces_P[0][i] + massDifference * gravity[i]) * (c_u) + f_vNew[i];
            //    tempForceOld[i] = (forces_P[1][i] + massDifference * gravity[i]) * (c_u) + f_vOld[i];
            //    temp[i] = currentIterVel_P[0][i] + (3 * tempForceNew[i] - tempForceOld[i]) * dt / 2;
            //}

            // implicit Adams Moulton (modified)
            //for (double velResidual = 1; velResidual > velResidual_ConvergenceCriterion;)
            //{
            //    for (int i = 0; i < 2; i++)
            //    {
            //        C_v_mod[i] = 0.1;// * Math.Abs(forces_P[0][i] / (forces_P[0][i] + forces_P[1][i] + 1e-30));
            //        c_a[i] = (C_v_mod[i] * rho_Fluid) / (rho_P + C_v_mod[i] * rho_Fluid);
            //        c_u[i] = 1 / (area_P * (rho_P + C_v_mod[i] * rho_P));
            //        f_vTemp[i] = (C_v_mod[i]) / (1 + C_v_mod[i]) * (11 * temp[i] - 18 * currentIterVel_P[0][i] + 9 * currentIterVel_P[1][i] - 2 * currentIterVel_P[2][i]) / (8 * dt);
            //        f_vNew[i] = (C_v_mod[i]) / (1 + C_v_mod[i]) * (11 * currentIterVel_P[0][i] - 18 * currentIterVel_P[1][i] + 9 * currentIterVel_P[2][i] - 2 * currentIterVel_P[3][i]) / (6 * dt);
            //        f_vOld[i] = (C_v_mod[i]) / (1 + C_v_mod[i]) * (11 * currentIterVel_P[1][i] - 18 * currentIterVel_P[2][i] + 9 * currentIterVel_P[3][i] - 2 * currentIterVel_P[4][i]) / (6 * dt);
            //        tempForceTemp[i] = (forces_P[0][i] + massDifference * gravity[i]) * (c_u[i]) + f_vTemp[i];
            //        tempForceNew[i] = (forces_P[1][i] + massDifference * gravity[i]) * (c_u[i]) + f_vNew[i];
            //        tempForceOld[i] = (forces_P[2][i] + massDifference * gravity[i]) * (c_u[i]) + f_vOld[i];
            //        old_temp[i] = temp[i];
            //        temp[i] = previous_vel[i] + (1 * tempForceTemp[i] + 4 * tempForceNew[i] + 1 * tempForceOld[i]) * dt / 6;
            //    }
            //    vel_iteration_counter += 1;
            //    if (vel_iteration_counter == MaxParticleVelIterations)
            //    {
            //        throw new ApplicationException("no convergence in particle velocity calculation");
            //    }
            //    velResidual = Math.Sqrt((temp[0] - old_temp[0]).Pow2() + (temp[1] - old_temp[1]).Pow2());

            //    //Console.WriteLine("Current velResidual:  " + velResidual);
            //}
            //Console.WriteLine("Number of Iterations for translational velocity calculation:  " + vel_iteration_counter);
            //Console.WriteLine("C_v_mod:  " + C_v_mod[0]);
            #endregion

            //Crank Nicolson
            // =============================
            for (int d = 0; d < m_Dim; d++)
            {
                gravity[d] = 0;
                if (includeGravity == true)
                {
                    gravity[1] = -9.81;
                }
                double tempForces = (currentIterForces_P[0][d] + currentTimeForces_P[1][d]) / 2;
                temp[d] = currentTimeVel_P[1][d] * Mass_P + dt * (tempForces + massDifference * gravity[d]);
                temp[d] = temp[d] / Mass_P;
                //if (Math.Abs(temp[d]) < 1e-14)
                //{
                //    temp[d] = 0;
                //}
            }

            // Save new velocity
            // =============================
            currentIterVel_P.Insert(0, temp);
            currentIterVel_P.Remove(currentIterVel_P.Last());
            currentTimeVel_P[0] = currentIterVel_P[0];

            return;
        }
        #endregion

        #region Update angular velocity
        /// <summary>
        /// Calculate the new angular velocity of the particle using explicit Euler scheme.
        /// </summary>
        /// <param name="D"></param>
        /// <param name="dt"></param>
        /// <param name="oldAngularVelocity"></param>
        /// <param name="Radius"></param>
        /// <param name="ParticleMass"></param>
        /// <param name="Torque"></param>
        /// <returns></returns>
        public void UpdateAngularVelocity(double dt, double rho_Fluid = 1, bool includeRotation = true, int noOfSubtimesteps = 1) {

            // save rotation of the last timestep
            if (iteration_counter_P == 1)
            {
                currentTimeRot_P.Insert(1, currentTimeRot_P[0]);
                currentTimeRot_P.Remove(currentTimeRot_P.Last());
            }

            // no rotation
            // =============================
            if (includeRotation == false) {
                currentIterRot_P.Insert(0, 0.0);
                currentIterRot_P.Remove(currentIterRot_P.Last());
                return;
            }

            double newAngularVelocity = 0;
            double oldAngularVelocity = new double();
            double subtimestep;
            noOfSubtimesteps = 1;
            subtimestep = dt / noOfSubtimesteps;
            
            for (int i = 1; i <= noOfSubtimesteps; i++) {
                newAngularVelocity = currentTimeRot_P[1] + (dt / MomentOfInertia_P) * (currentIterTorque_P[0] + currentTimeTorque_P[1]) / 2; // for 2D

                oldAngularVelocity = newAngularVelocity;

            }
            //if (Math.Abs(newAngularVelocity) < 1e-14)
            //{
            //    newAngularVelocity = 0;
            //}
            currentIterRot_P.Insert(0, newAngularVelocity);
            currentIterRot_P.Remove(currentIterRot_P.Last());
            currentTimeRot_P[0] = currentIterRot_P[0];
        }
        #endregion
        
        #region Cloning
        /// <summary>
        /// clone
        /// </summary>
        public object Clone() {
            throw new NotImplementedException("Currently cloning of a particle is not available");
        }
        #endregion

        #region Update forces and torque
        /// <summary>
        /// Update forces and torque acting from fluid onto the particle
        /// </summary>
        /// <param name="U"></param>
        /// <param name="P"></param>
        /// <param name="LsTrk"></param>
        /// <param name="muA"></param>
        public void UpdateForcesAndTorque(VectorField<SinglePhaseField> U, SinglePhaseField P,
            LevelSetTracker LsTrk,
            double muA) {

            if (skipForceIntegration) {
                skipForceIntegration = false;
                return;
            }
            // save forces and torque of the last timestep
            if (iteration_counter_P == 1)
            {
                currentTimeForces_P.Insert(1, currentTimeForces_P[0]);
                currentTimeForces_P.Remove(currentTimeForces_P.Last());
                currentTimeTorque_P.Insert(1, currentTimeTorque_P[0]);
                currentTimeTorque_P.Remove(currentTimeTorque_P.Last());
            }
            int D = LsTrk.GridDat.SpatialDimension;
            // var UA = U.Select(u => u.GetSpeciesShadowField("A")).ToArray();
            var UA = U.ToArray();

            int RequiredOrder = U[0].Basis.Degree * 3 + 2;
            //int RequiredOrder = LsTrk.GetXQuadFactoryHelper(momentFittingVariant).GetCachedSurfaceOrders(0).Max();
            //Console.WriteLine("Order reduction: {0} -> {1}", _RequiredOrder, RequiredOrder);

            //if (RequiredOrder > agg.HMForder)
            //    throw new ArgumentException();

            Console.WriteLine("Forces coeff: {0}, order = {1}", LsTrk.CutCellQuadratureType, RequiredOrder);


            ConventionalDGField pA = null;

            //pA = P.GetSpeciesShadowField("A");
            pA = P;

            #region Force
            double[] forces = new double[D];
            for (int d = 0; d < D; d++) {
                ScalarFunctionEx ErrFunc = delegate (int j0, int Len, NodeSet Ns, MultidimensionalArray result) {
                    int K = result.GetLength(1); // No nof Nodes
                    MultidimensionalArray Grad_UARes = MultidimensionalArray.Create(Len, K, D, D); ;
                    MultidimensionalArray pARes = MultidimensionalArray.Create(Len, K);

                    // Evaluate tangential velocity to level-set surface
                    var Normals = LsTrk.DataHistories[0].Current.GetLevelSetNormals(Ns, j0, Len);


                    for (int i = 0; i < D; i++) {
                        UA[i].EvaluateGradient(j0, Len, Ns, Grad_UARes.ExtractSubArrayShallow(-1, -1, i, -1), 0, 1);
                    }


                    pA.Evaluate(j0, Len, Ns, pARes);

                    if (LsTrk.GridDat.SpatialDimension == 2) {

                        for (int j = 0; j < Len; j++) {
                            for (int k = 0; k < K; k++) {
                                // defining variables
                                double acc = 0.0;
                                double c = 0.0;
                                double[] integrand = new double[4];
                                double sum = 0;
                                double naiveSum = 0;

                                // choosing direction 
                                switch (d) {
                                    case 0:
                                        c = 0.0;
                                        naiveSum = 0;
                                        // integration with Neumaier algorithm, Neumaier is used to prevent rounding errors
                                        integrand[0] = -2 * Grad_UARes[j, k, 0, 0] * Normals[j, k, 0];
                                        integrand[1] = -Grad_UARes[j, k, 0, 1] * Normals[j, k, 1];
                                        integrand[2] = -Grad_UARes[j, k, 1, 0] * Normals[j, k, 1];
                                        integrand[3] = pARes[j, k] * Normals[j, k, 0];
                                        // Neumaier velocity gradient
                                        sum = integrand[0];
                                        for (int i = 1; i < integrand.Length - 1; i++)
                                        {
                                            naiveSum = sum + integrand[i];
                                            if (Math.Abs(sum) >= integrand[i])
                                            {
                                                c += (sum - naiveSum) + integrand[i];
                                            }
                                            else
                                            {
                                                c += (integrand[i] - naiveSum) + sum;
                                            }
                                            sum = naiveSum;
                                        }
                                        sum *= muA;
                                        c *= muA;
                                        // Neumaier pressure term
                                        naiveSum = sum + integrand[3];
                                        if (Math.Abs(sum) >= integrand[3])
                                        {
                                            c += (sum - naiveSum) + integrand[3];
                                        }
                                        else
                                        {
                                            c += (integrand[3] - naiveSum) + sum;
                                        }
                                        sum = naiveSum;
                                        acc = sum + c;
                                        break;

                                    case 1:
                                        c = 0.0;
                                        naiveSum = 0;
                                        // integration with Neumaier algorithm
                                        integrand[0] = -2 * Grad_UARes[j, k, 1, 1] * Normals[j, k, 1];
                                        integrand[1] = -Grad_UARes[j, k, 1, 0] * Normals[j, k, 0];
                                        integrand[2] = -Grad_UARes[j, k, 0, 1] * Normals[j, k, 0];
                                        integrand[3] = pARes[j, k] * Normals[j, k, 1];
                                        // Neumaier velocity gradient
                                        sum = integrand[0];
                                        for (int i = 1; i < integrand.Length - 1; i++)
                                        {
                                            naiveSum = sum + integrand[i];
                                            if (Math.Abs(sum) >= integrand[i])
                                            {
                                                c += (sum - naiveSum) + integrand[i];
                                            }
                                            else
                                            {
                                                c += (integrand[i] - naiveSum) + sum;
                                            }
                                            sum = naiveSum;
                                        }
                                        sum *= muA;
                                        c *= muA;
                                        // Neumaier pressure term
                                        naiveSum = sum + integrand[3];
                                        if (Math.Abs(sum) >= integrand[3])
                                        {
                                            c += (sum - naiveSum) + integrand[3];
                                        }
                                        else
                                        {
                                            c += (integrand[3] - naiveSum) + sum;
                                        }
                                        sum = naiveSum;
                                        acc = sum + c;
                                        break;
                                    default:
                                        throw new NotImplementedException();
                                }

                                result[j, k] = acc;
                            }
                        }
                    }
                    else {
                        for (int j = 0; j < Len; j++) {
                            for (int k = 0; k < K; k++) {
                                double acc = 0.0;

                                // pressure
                                switch (d) {
                                    case 0:
                                        acc += pARes[j, k] * Normals[j, k, 0];
                                        acc -= (2 * muA) * Grad_UARes[j, k, 0, 0] * Normals[j, k, 0];
                                        acc -= (muA) * Grad_UARes[j, k, 0, 2] * Normals[j, k, 2];
                                        acc -= (muA) * Grad_UARes[j, k, 0, 1] * Normals[j, k, 1];
                                        acc -= (muA) * Grad_UARes[j, k, 1, 0] * Normals[j, k, 1];
                                        acc -= (muA) * Grad_UARes[j, k, 2, 0] * Normals[j, k, 2];
                                        break;
                                    case 1:
                                        acc += pARes[j, k] * Normals[j, k, 1];
                                        acc -= (2 * muA) * Grad_UARes[j, k, 1, 1] * Normals[j, k, 1];
                                        acc -= (muA) * Grad_UARes[j, k, 1, 2] * Normals[j, k, 2];
                                        acc -= (muA) * Grad_UARes[j, k, 1, 0] * Normals[j, k, 0];
                                        acc -= (muA) * Grad_UARes[j, k, 0, 1] * Normals[j, k, 0];
                                        acc -= (muA) * Grad_UARes[j, k, 2, 1] * Normals[j, k, 2];
                                        break;
                                    case 2:
                                        acc += pARes[j, k] * Normals[j, k, 2];
                                        acc -= (2 * muA) * Grad_UARes[j, k, 2, 2] * Normals[j, k, 2];
                                        acc -= (muA) * Grad_UARes[j, k, 2, 0] * Normals[j, k, 0];
                                        acc -= (muA) * Grad_UARes[j, k, 2, 1] * Normals[j, k, 1];
                                        acc -= (muA) * Grad_UARes[j, k, 0, 2] * Normals[j, k, 0];
                                        acc -= (muA) * Grad_UARes[j, k, 1, 2] * Normals[j, k, 1];
                                        break;
                                    default:
                                        throw new NotImplementedException();
                                }

                                result[j, k] = acc;
                            }
                        }
                    }

                };

                var SchemeHelper = LsTrk.GetXDGSpaceMetrics(new[] { LsTrk.GetSpeciesId("A") }, RequiredOrder, 1).XQuadSchemeHelper;
                //var SchemeHelper = new XQuadSchemeHelper(LsTrk, momentFittingVariant, );

                //CellQuadratureScheme cqs = SchemeHelper.GetLevelSetquadScheme(0, LsTrk.Regions.GetCutCellMask());
                CellQuadratureScheme cqs = SchemeHelper.GetLevelSetquadScheme(0, this.cutCells_P(LsTrk));

                double forceNaiveSum = 0.0;
                double forceSum = 0.0;
                double forceC = 0.0;
                CellQuadrature.GetQuadrature(new int[] { 1 }, LsTrk.GridDat,
                    cqs.Compile(LsTrk.GridDat, RequiredOrder), //  agg.HMForder),
                    delegate (int i0, int Length, QuadRule QR, MultidimensionalArray EvalResult) {
                        ErrFunc(i0, Length, QR.Nodes, EvalResult.ExtractSubArrayShallow(-1, -1, 0));
                    },
                    delegate (int i0, int Length, MultidimensionalArray ResultsOfIntegration) {
                        for (int i = 0; i < Length; i++)
                        {
                            //forces[d] += ResultsOfIntegration[i, 0];
                            forceNaiveSum = forceSum + ResultsOfIntegration[i, 0];
                            if (Math.Abs(forceSum) >= Math.Abs(ResultsOfIntegration[i, 0]))
                            {
                                forceC += (forceSum - forceNaiveSum) + ResultsOfIntegration[i, 0];
                            }
                            else
                            {
                                forceC += (ResultsOfIntegration[i, 0] - forceNaiveSum) + forceSum;
                            }
                            forceSum = forceNaiveSum;
                        }
                        forces[d] = forceSum + forceC;
                    }
                ).Execute();
            }
            #endregion

            #region Torque
            double torque = 0;
            ScalarFunctionEx ErrFunc2 = delegate (int j0, int Len, NodeSet Ns, MultidimensionalArray result) {
                int K = result.GetLength(1); // No nof Nodes
                MultidimensionalArray Grad_UARes = MultidimensionalArray.Create(Len, K, D, D); ;
                MultidimensionalArray pARes = MultidimensionalArray.Create(Len, K);

                // Evaluate tangential velocity to level-set surface
                var Normals = LsTrk.DataHistories[0].Current.GetLevelSetNormals(Ns, j0, Len);

                for (int i = 0; i < D; i++) {
                    UA[i].EvaluateGradient(j0, Len, Ns, Grad_UARes.ExtractSubArrayShallow(-1, -1, i, -1), 0, 1);
                }

                //var trafo = LsTrk.GridDat.Edges.Edge2CellTrafos;
                //var trafoIdx = LsTrk.GridDat.TransformLocal2Global(Ns)
                //var transFormed = trafo[trafoIdx].Transform(Nodes);
                //var newVertices = transFormed.CloneAs();
                //GridData.TransformLocal2Global(transFormed, newVertices, jCell);


                MultidimensionalArray tempArray = Ns.CloneAs();

                LsTrk.GridDat.TransformLocal2Global(Ns, tempArray, j0);

                pA.Evaluate(j0, Len, Ns, pARes);

                for (int j = 0; j < Len; j++) {
                    for (int k = 0; k < K; k++) {
                        
                        double[] integrand = new double[4];
                        double[] integrand2 = new double[4];
                        double naiveSum = 0.0;
                        double c = 0.0;
                        double sum = 0.0;
                        double sum2 = 0.0;
                        double naiveSum2 = 0.0;
                        double c2 = 0.0;

                        // Calculate the torque around a circular particle with a given radius (Paper Wan and Turek 2005)
                        integrand[0] = -2 * Grad_UARes[j, k, 0, 0] * Normals[j, k, 0];
                        integrand[1] = -Grad_UARes[j, k, 0, 1] * Normals[j, k, 1];
                        integrand[2] = -Grad_UARes[j, k, 1, 0] * Normals[j, k, 1];
                        integrand[3] = (pARes[j, k] * Normals[j, k, 0]);
                        sum = integrand[0];
                        for (int i = 1; i < integrand.Length - 1; i++)
                        {
                            naiveSum = sum + integrand[i];
                            if (Math.Abs(sum) >= integrand[i])
                            {
                                c += (sum - naiveSum) + integrand[i];
                            }
                            else
                            {
                                c += (integrand[i] - naiveSum) + sum;
                            }
                            sum = naiveSum;
                        }
                        sum *= muA;
                        c *= muA;
                        naiveSum = sum + integrand[3];
                        if (Math.Abs(sum) >= integrand[3])
                        {
                            c += (sum - naiveSum) + integrand[3];
                        }
                        else
                        {
                            c += (integrand[3] - naiveSum) + sum;
                        }
                        sum *= -Normals[j, k, 1] * (this.currentIterPos_P[0][1] - tempArray[k, 1]).Abs();
                        c *= -Normals[j, k, 1] * (this.currentIterPos_P[0][1] - tempArray[k, 1]).Abs();
                        
                        integrand2[0] = -2 * Grad_UARes[j, k, 1, 1] * Normals[j, k, 1];
                        integrand2[1] = -Grad_UARes[j, k, 1, 0] * Normals[j, k, 0];
                        integrand2[2] = -Grad_UARes[j, k, 0, 1] * Normals[j, k, 0];
                        integrand2[3] = pARes[j, k] * Normals[j, k, 1];
                        sum2 = integrand2[0];
                        for (int i = 1; i < integrand2.Length - 1; i++)
                        {
                            naiveSum2 = sum2 + integrand2[i];
                            if (Math.Abs(sum2) >= integrand2[i])
                            {
                                c2 += (sum2 - naiveSum2) + integrand2[i];
                            }
                            else
                            {
                                c2 += (integrand2[i] - naiveSum2) + sum2;
                            }
                            sum2 = naiveSum2;
                        }
                        sum2 *= muA;
                        c2 *= muA;
                        naiveSum2 = sum2 + integrand2[3];
                        if (Math.Abs(sum2) >= integrand2[3])
                        {
                            c2 += (sum2 - naiveSum2) + integrand2[3];
                        }
                        else
                        {
                            c2 += (integrand2[3] - naiveSum2) + sum2;
                        }
                        sum2 *= Normals[j, k, 0] * (this.currentIterPos_P[0][0] - tempArray[k, 0]).Abs();
                        c2 *= Normals[j, k, 0] * (this.currentIterPos_P[0][0] - tempArray[k, 0]).Abs();
                        sum += sum2;
                        c += c2;

                        result[j, k] = sum + c;
                    }  
                }
            };

            var SchemeHelper2 = LsTrk.GetXDGSpaceMetrics(new[] { LsTrk.GetSpeciesId("A") }, RequiredOrder, 1).XQuadSchemeHelper;
            //var SchemeHelper = new XQuadSchemeHelper(LsTrk, momentFittingVariant, );
            //CellQuadratureScheme cqs2 = SchemeHelper2.GetLevelSetquadScheme(0, LsTrk.Regions.GetCutCellMask());
            CellQuadratureScheme cqs2 = SchemeHelper2.GetLevelSetquadScheme(0, this.cutCells_P(LsTrk));
            double torqueNaiveSum = 0.0;
            double torqueSum = 0.0;
            double torqueC = 0.0;
            CellQuadrature.GetQuadrature(new int[] { 1 }, LsTrk.GridDat,
                cqs2.Compile(LsTrk.GridDat, RequiredOrder),
                delegate (int i0, int Length, QuadRule QR, MultidimensionalArray EvalResult) {
                    ErrFunc2(i0, Length, QR.Nodes, EvalResult.ExtractSubArrayShallow(-1, -1, 0));
                },
                delegate (int i0, int Length, MultidimensionalArray ResultsOfIntegration) {
                    //for (int i = 0; i < Length; i++)
                    //{
                    //    torque += ResultsOfIntegration[i, 0];
                    //}
                    for (int i = 0; i < Length; i++)
                    {
                        torqueNaiveSum = torqueSum + ResultsOfIntegration[i, 0];
                        if (Math.Abs(torqueSum) >= Math.Abs(ResultsOfIntegration[i, 0]))
                        {
                            torqueC += (torqueSum - torqueNaiveSum) + ResultsOfIntegration[i, 0];
                        }
                        else
                        {
                            torqueC += (ResultsOfIntegration[i, 0] - torqueNaiveSum) + torqueSum;
                        }
                        torqueSum = torqueNaiveSum;
                    }
                    torque = torqueSum + torqueC;
                }

            ).Execute();

            // determine underrelaxation factor (URF)
            // =============================
            temporalForces_P.Insert(0, forces);
            temporalForces_P.Remove(temporalForces_P.Last());
            temporalTorque_P.Insert(0, torque);
            temporalTorque_P.Remove(temporalTorque_P.Last());
            double[] temp_underR = new double[D + 1];
            double averageDistance = (length_P + thickness_P) / 2;
            double averageForce = (Math.Abs(forces[0]) + Math.Abs(forces[1]) + Math.Abs(torque) / averageDistance) / 3;
            for (int k = 0; k < D + 1; k++)
            {
                temp_underR[k] = underrelaxation_factor;
            }
            // first iteration, set URF to 1
            if (iteration_counter_P == 1)
            {
                for (int k = 0; k < D; k++)
                {
                    temp_underR[k] = 1;
                    for (int t = 0; t < m_HistoryLength; t++)
                    {
                        currentIterForces_P[t][k] = currentTimeForces_P[1][k];
                        currentIterTorque_P[t] = currentTimeTorque_P[1];
                    }
                }
                temp_underR[D] = 1;
            }
            // constant predefined URF
            else if (underrelaxationFT_constant == true)
            {
                for (int k = 0; k < D + 1; k++)
                {
                    temp_underR[k] = underrelaxation_factor * Math.Pow(10, underrelaxationFT_exponent);
                }
            }
            // calculation of URF for adaptive underrelaxation
            else if (underrelaxationFT_constant == false)
            {
                // forces
                bool underrelaxation_ok = false;
                for (int j = 0; j < D; j++)
                {
                    underrelaxation_ok = false;
                    temp_underR[j] = underrelaxation_factor;
                    underrelaxationFT_exponent = 0;
                    for (int i = 0; underrelaxation_ok == false; i++)
                    {
                        if (Math.Abs(temp_underR[j] * forces[j]) > 0.9 * Math.Abs(currentIterForces_P[0][j]))
                        {
                            underrelaxationFT_exponent -= 1;
                            temp_underR[j] = underrelaxation_factor * Math.Pow(10, underrelaxationFT_exponent);
                        }
                        else
                        {
                            underrelaxation_ok = true;
                            if (Math.Abs(temp_underR[j] * forces[j]) < forceAndTorque_convergence * 1000 && 100 * Math.Abs(forces[j]) > averageForce)
                            {
                                temp_underR[j] = forceAndTorque_convergence * 1000;
                            }
                            if (temp_underR[j] >= 0.9)
                            {
                                temp_underR[j] = 0.9;
                            }
                        }
                    }
                }
                // torque
                underrelaxation_ok = false;
                temp_underR[D] = underrelaxation_factor;
                underrelaxationFT_exponent = 0;
                for (int i = 0; underrelaxation_ok == false; i++)
                {
                    if (Math.Abs(temp_underR[D] * torque) > 0.9 * Math.Abs(currentIterTorque_P[0]))
                    {
                        underrelaxationFT_exponent -= 1;
                        temp_underR[D] = underrelaxation_factor * Math.Pow(10, underrelaxationFT_exponent);
                    }
                    else
                    {
                        underrelaxation_ok = true;
                        if (Math.Abs(temp_underR[D] * torque) < forceAndTorque_convergence * 1000 && 100 * Math.Abs(torque) > averageForce)
                        {
                            temp_underR[D] = forceAndTorque_convergence * 1000;
                        }
                        if (underrelaxationFT_exponent > -1)
                        {
                            underrelaxationFT_exponent = -1;
                            temp_underR[D] = underrelaxation_factor * Math.Pow(10, underrelaxationFT_exponent);
                        }
                    }
                }
            }
            Console.WriteLine("tempunderR[0]  " + temp_underR[0] + ", temp_underR[1]: " + temp_underR[1] + ", temp_underR[D] " + temp_underR[D]);
            Console.WriteLine("tempfForces[0]  " + forces[0] + ", temp_Forces[1]: " + forces[1] + ", tempTorque " + torque);

            // calculation of forces and torque with underrelaxation
            // =============================
            // forces
            double[] forces_underR = new double[D];
            for (int i = 0; i < D; i++)
            {
                forces_underR[i] = temp_underR[i] * forces[i] + (1 - temp_underR[i]) * currentIterForces_P[0][i];
            }
            double torque_underR = temp_underR[D] * torque + (1 - temp_underR[D]) * currentIterTorque_P[0];
            // update forces and torque
            this.currentIterForces_P.Insert(0, forces_underR);
            currentIterForces_P.Remove(currentIterForces_P.Last());
            this.currentIterTorque_P.Remove(currentIterTorque_P.Last());
            this.currentIterTorque_P.Insert(0, torque_underR);
            currentTimeForces_P[0] = currentIterForces_P[0];
            currentTimeTorque_P[0] = currentIterTorque_P[0];

            #endregion
        }
        #endregion

        #region Particle reynolds number
        /// <summary>
        /// Calculating the particle reynolds number according to paper Turek and testcase ParticleUnderGravity
        /// </summary>
        /// <param name="currentVelocity"></param>
        /// <param name="Radius"></param>
        /// <param name="particleDensity"></param>
        /// <param name="viscosity"></param>
        /// <returns></returns>
        abstract public double ComputeParticleRe(double mu_Fluid);
        #endregion

        #region Cut cells
        /// <summary>
        /// get cut cells describing the boundary of this particle
        /// </summary>
        /// <param name="LsTrk"></param>
        /// <returns></returns>
        abstract public CellMask cutCells_P(LevelSetTracker LsTrk); 

        /// <summary>
        /// Gives a bool whether the particle contains a certain point or not
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        abstract public bool Contains(double[] point, LevelSetTracker LsTrk); 
        #endregion

        #region Particle shape
        public enum ParticleShape {
            spherical = 0,

            elliptic = 1,

            hippopede = 2,

            bean = 3,

            squircle = 4
        }
        #endregion
    }

}
