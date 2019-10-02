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

using BoSSS.Foundation;
using BoSSS.Foundation.Grid;
using BoSSS.Foundation.XDG;
using ilPSP;
using MPI.Wrappers;
using System;
using System.Runtime.Serialization;

namespace BoSSS.Application.FSI_Solver {
    public class Motion_AddedDamping : Motion_Wet {

        /// <summary>
        /// The added damping description of motion including hydrodynamics, for reference: Banks et.al. 2017.
        /// </summary>
        /// <param name="gravity">
        /// The gravity (volume forces) acting on the particle.
        /// </param>
        /// <param name="density">
        /// The density of the particle.
        /// </param>
        /// <param name="underrelaxationParam">
        /// The underrelaxation parameters (convergence limit, prefactor and a bool whether to use addaptive underrelaxation) defined in <see cref="ParticleUnderrelaxationParam"/>.
        /// </param>
        /// <param name="addedDampingCoefficient">
        /// The added damping coefficient is a scaling factor for the model. Should be between 0.5 and 1.5, for reference: Banks et.al. 2017.
        /// </param>
        public Motion_AddedDamping( double[] gravity, double density, ParticleUnderrelaxationParam underrelaxationParam, double addedDampingCoefficient = 1) : base(gravity, density, underrelaxationParam) {
            m_StartingAngle = GetAngle(0);
            m_AddedDampingCoefficient = addedDampingCoefficient;    
            UseAddedDamping = true;
        }

        [NonSerialized]
        private readonly ParticleAddedDamping AddedDamping = new ParticleAddedDamping();
        [DataMember]
        private double[,] m_AddedDampingTensor;
        [DataMember]
        private readonly double m_AddedDampingCoefficient;
        [DataMember]
        private readonly double m_StartingAngle;

        /// <summary>
        /// We are using the added damping model, for reference: Banks et.al. 2017.
        /// </summary>
        [DataMember]
        internal override bool UseAddedDamping { get; } = true;
        
        /// <summary>
        /// Complete added damping tensor, for reference: Banks et.al. 2017.
        /// </summary>
        [DataMember]
        internal override double[,] AddedDampingTensor { get => m_AddedDampingTensor; }

        /// <summary>
        /// Calculate the tensors to implement the added damping model (Banks et.al. 2017)
        /// </summary>
        /// <param name="particle"></param>
        /// <param name="levelSetTracker"></param>
        /// <param name="fluidViscosity"></param>
        /// <param name="fluidDensity"></param>
        /// <param name="dt"></param>
        public override void CalculateDampingTensor(Particle particle, LevelSetTracker levelSetTracker, double fluidViscosity, double fluidDensity, double dt) {
            m_AddedDampingTensor = AddedDamping.IntegrationOverLevelSet(particle, levelSetTracker, fluidViscosity, fluidDensity, dt, GetPosition(0));
            Aux.TestArithmeticException(m_AddedDampingTensor, "particle added damping tensor");
        }

        /// <summary>
        /// Update in every timestep tensors to implement the added damping model (Banks et.al. 2017).
        /// </summary>
        public override void UpdateDampingTensors() {
            m_AddedDampingTensor = AddedDamping.RotateTensor(GetAngle(0), m_StartingAngle, AddedDampingTensor);
            Aux.TestArithmeticException(m_AddedDampingTensor, "particle added damping tensor");
        }

        /// <summary>
        /// MPI exchange of the damping tensors
        /// </summary>
        public override void ExchangeAddedDampingTensors() {
            int NoOfVars = 3;
            double[] StateBuffer = new double[NoOfVars * NoOfVars];
            for (int i = 0; i < NoOfVars; i++) {
                for (int j = 0; j < NoOfVars; j++) {
                    StateBuffer[i + NoOfVars * j] = m_AddedDampingTensor[i, j];
                }
            }
            double[] GlobalStateBuffer = StateBuffer.MPISum();
            for (int i = 0; i < 3; i++) {
                for (int j = 0; j < 3; j++) {
                    m_AddedDampingTensor[i, j] = GlobalStateBuffer[i + NoOfVars * j];
                }
            }
        }

        /// <summary>
        /// Calculates the translational acceleration of the particle using the added damping model.
        /// </summary>
        /// <param name="dt">Timestep</param>
        protected override double[] CalculateTranslationalAcceleration(double dt) {
            double[,] coefficientMatrix = CalculateCoefficientMatrix(dt);
            double denominator = CalculateDenominator(coefficientMatrix);

            double[] l_Acceleration = new double[2];
            l_Acceleration[0] = GetHydrodynamicForces(0)[0] * (coefficientMatrix[1, 1] * coefficientMatrix[2, 2] - coefficientMatrix[1, 2] * coefficientMatrix[2, 1]);
            l_Acceleration[0] += GetHydrodynamicForces(0)[1] * (-coefficientMatrix[0, 1] * coefficientMatrix[2, 2] + coefficientMatrix[0, 2] * coefficientMatrix[2, 1]);
            l_Acceleration[0] += GetHydrodynamicTorque(0) * (coefficientMatrix[0, 1] * coefficientMatrix[1, 2] - coefficientMatrix[0, 2] * coefficientMatrix[1, 1]);
            l_Acceleration[0] = l_Acceleration[0] / denominator;

            l_Acceleration[1] = GetHydrodynamicForces(0)[0] * (-coefficientMatrix[1, 0] * coefficientMatrix[2, 2] + coefficientMatrix[1, 2] * coefficientMatrix[2, 0]);
            l_Acceleration[1] += GetHydrodynamicForces(0)[1] * (coefficientMatrix[0, 0] * coefficientMatrix[2, 2] - coefficientMatrix[0, 2] * coefficientMatrix[2, 0]);
            l_Acceleration[1] += GetHydrodynamicTorque(0) * (-coefficientMatrix[0, 0] * coefficientMatrix[1, 2] + coefficientMatrix[0, 2] * coefficientMatrix[1, 0]);
            l_Acceleration[1] = l_Acceleration[1] / denominator;
            Aux.TestArithmeticException(l_Acceleration, "particle translational acceleration");
            return l_Acceleration;
        }

        /// <summary>
        /// Calculates the rotational acceleration of the particle using the added damping model.
        /// </summary>
        /// <param name="dt">Timestep</param>
        protected override double CalculateRotationalAcceleration(double dt) {
            double[,] coefficientMatrix = CalculateCoefficientMatrix(dt);
            double denominator = CalculateDenominator(coefficientMatrix);

            double l_Acceleration = GetHydrodynamicForces(0)[0] * (coefficientMatrix[1, 0] * coefficientMatrix[2, 1] - coefficientMatrix[1, 1] * coefficientMatrix[2, 0]);
            l_Acceleration += GetHydrodynamicForces(0)[1] * (coefficientMatrix[0, 1] * coefficientMatrix[2, 0] - coefficientMatrix[0, 0] * coefficientMatrix[2, 1]);
            l_Acceleration += GetHydrodynamicTorque(0) * (coefficientMatrix[0, 0] * coefficientMatrix[1, 1] - coefficientMatrix[0, 1] * coefficientMatrix[1, 0]);
            l_Acceleration /= denominator;
            Aux.TestArithmeticException(l_Acceleration, "particle rotational acceleration");
            return l_Acceleration;
        }

        /// <summary>
        /// Calculates the coefficient matrix for the acceleration constituted of the mass matrix and the added damping tensor.
        /// </summary>
        /// <param name="dt">Timestep</param>
        private double[,] CalculateCoefficientMatrix(double dt) {
            double[,] massMatrix = GetMassMatrix();
            double[,] coefficientMatrix = massMatrix.CloneAs();
            for (int i = 0; i < 3; i++) {
                for (int j = 0; j < 3; j++) {
                    coefficientMatrix[i, j] = massMatrix[i, j] + dt * m_AddedDampingCoefficient * AddedDampingTensor[i, j];
                }
            }
            return coefficientMatrix;
        }

        /// <summary>
        /// Calculates the mass matrix of the particle.
        /// </summary>
        private double[,] GetMassMatrix() {
            double[,] MassMatrix = new double[3, 3];
            MassMatrix[0, 0] = MassMatrix[1, 1] = ParticleArea * Density;
            MassMatrix[2, 2] = MomentOfInertia;
            return MassMatrix;
        }

        /// <summary>
        /// Calculates the denominator necessary for the calculation of the acceleration of the particle.
        /// </summary>
        /// <param name="coefficientMatrix">The matrix calculated in <see cref="CalculateCoefficientMatrix"></see>/></param>
        private double CalculateDenominator(double[,] coefficientMatrix) {
            double denominator = coefficientMatrix[0, 0] * coefficientMatrix[1, 1] * coefficientMatrix[2, 2];
            denominator -= coefficientMatrix[0, 0] * coefficientMatrix[1, 2] * coefficientMatrix[2, 1];
            denominator -= coefficientMatrix[0, 1] * coefficientMatrix[1, 0] * coefficientMatrix[2, 2];
            denominator += coefficientMatrix[0, 1] * coefficientMatrix[1, 2] * coefficientMatrix[2, 0];
            denominator += coefficientMatrix[0, 2] * coefficientMatrix[1, 0] * coefficientMatrix[2, 1];
            denominator -= coefficientMatrix[0, 2] * coefficientMatrix[1, 1] * coefficientMatrix[2, 0];
            return denominator;
        }

        /// <summary>
        /// Calls the calculation of the hydrodynamics
        /// </summary>
        /// <param name="U"></param>
        /// <param name="P"></param>
        /// <param name="levelSetTracker"></param>
        /// <param name="fluidViscosity"></param>
        public override void UpdateForcesAndTorque(VectorField<SinglePhaseField> U, SinglePhaseField P, LevelSetTracker levelSetTracker, CellMask cutCells, double fluidViscosity, double fluidDensity, bool firstIteration, double dt) {
            double[] tempForces = CalculateHydrodynamicForces(U, P, levelSetTracker, cutCells, fluidViscosity, fluidDensity, dt);
            double tempTorque = CalculateHydrodynamicTorque(U, P, levelSetTracker, cutCells, fluidViscosity, dt);
            HydrodynamicsPostprocessing(tempForces, tempTorque, firstIteration);
        }

        /// <summary>
        /// Update Forces acting from fluid onto the particle
        /// </summary>
        /// <param name="U"></param>
        /// <param name="P"></param>
        /// <param name="levelSetTracker"></param>
        /// <param name="cutCells"></param>
        /// <param name="fluidViscosity"></param>
        /// <param name="dt"></param>
        protected override double[] CalculateHydrodynamicForces(VectorField<SinglePhaseField> U, SinglePhaseField P, LevelSetTracker levelSetTracker, CellMask cutCells, double fluidViscosity, double fluidDensity, double dt) {
            int requiredOrder = U[0].Basis.Degree * 3 + 2;
            SinglePhaseField[] uA = U.ToArray();
            ConventionalDGField pA = P;
            double[] tempForces = ForcesIntegration(uA, pA, levelSetTracker, cutCells, requiredOrder, fluidViscosity);
            Force_MPISum(ref tempForces);
            for (int d = 0; d < spatialDim; d++) {
                tempForces[d] += (Density - fluidDensity) * ParticleArea * Gravity[d];
            }
            ForceAddedDamping(ref tempForces, dt);
            return tempForces;
        }

        /// <summary>
        /// Calculates the added damping effects on the hydrodynamic forces
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="forces"></param>
        private void ForceAddedDamping(ref double[] forces, double dt) {
            for (int d = 0; d < spatialDim; d++) {
                forces[d] += m_AddedDampingCoefficient * dt * (AddedDampingTensor[0, d] * GetTranslationalAcceleration(0)[0] + AddedDampingTensor[1, d] * GetTranslationalAcceleration(0)[1] + AddedDampingTensor[d, 2] * GetRotationalAcceleration(0));
            }
        }

        /// <summary>
        /// Update Torque acting from fluid onto the particle.
        /// </summary>
        /// <param name="U"></param>
        /// <param name="P"></param>
        /// <param name="levelSetTracker"></param>
        /// <param name="cutCells"></param>
        /// <param name="fluidViscosity"></param>
        /// <param name="dt"></param>
        protected override double CalculateHydrodynamicTorque(VectorField<SinglePhaseField> U, SinglePhaseField P, LevelSetTracker levelSetTracker, CellMask cutCells, double fluidViscosity, double dt) {
            int requiredOrder = U[0].Basis.Degree * 3 + 2;
            SinglePhaseField[] UA = U.ToArray();
            ConventionalDGField pA = P;
            double tempTorque = TorqueIntegration(UA, pA, levelSetTracker, cutCells, requiredOrder, fluidViscosity);
            Torque_MPISum(ref tempTorque);
            TorqueAddedDamping(ref tempTorque, dt);
            return tempTorque;
        }

        /// <summary>
        /// Calculates the added damping effects on the hydrodynamic torque.
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="torque"></param>
        private void TorqueAddedDamping(ref double torque, double dt) {
            torque += m_AddedDampingCoefficient * dt * (AddedDampingTensor[2, 0] * GetTranslationalAcceleration(0)[0] + AddedDampingTensor[2, 1] * GetTranslationalAcceleration(0)[1] + AddedDampingTensor[2, 2] * GetRotationalAcceleration(0));
        }
    }
}