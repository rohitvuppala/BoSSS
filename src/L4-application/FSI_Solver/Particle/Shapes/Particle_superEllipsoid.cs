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
using System.Runtime.Serialization;
using ilPSP;
using System.Linq;
using ilPSP.Utils;
using MathNet.Numerics;

namespace BoSSS.Application.FSI_Solver {
    [DataContract]
    [Serializable]
    public class Particle_superEllipsoid : Particle {
        /// <summary>
        /// Empty constructor used during de-serialization
        /// </summary>
        private Particle_superEllipsoid() : base() {

        }

        /// <summary>
        /// Constructor for a superellipsoid.
        /// </summary>
        /// <param name="motionInit">
        /// Initializes the motion parameters of the particle (which model to use, whether it is a dry simulation etc.)
        /// </param>
        /// <param name="length">
        /// The length of the horizontal halfaxis.
        /// </param>
        /// <param name="thickness">
        /// The length of the vertical halfaxis.
        /// </param>
        /// <param name="superEllipsoidExponent">
        /// The exponent of the superellipsoid.
        /// </param>
        /// <param name="startPos">
        /// The initial position.
        /// </param>
        /// <param name="startAngl">
        /// The inital anlge.
        /// </param>
        /// <param name="activeStress">
        /// The active stress excerted on the fluid by the particle. Zero for passive particles.
        /// </param>
        /// <param name="startTransVelocity">
        /// The inital translational velocity.
        /// </param>
        /// <param name="startRotVelocity">
        /// The inital rotational velocity.
        /// </param>
        public Particle_superEllipsoid(ParticleMotionInit motionInit, double length, double thickness, int superEllipsoidExponent, double[] startPos = null, double startAngl = 0, double activeStress = 0, double[] startTransVelocity = null, double startRotVelocity = 0) : base(motionInit, startPos, startAngl, activeStress, startTransVelocity, startRotVelocity) {
            m_Length = length;
            m_Thickness = thickness;
            m_Exponent = superEllipsoidExponent;
            Aux.TestArithmeticException(length, "Particle length");
            Aux.TestArithmeticException(thickness, "Particle thickness");
            Aux.TestArithmeticException(superEllipsoidExponent, "super ellipsoid exponent");

            Motion.GetParticleLengthscale(GetLengthScales().Max());
            Motion.GetParticleArea(Area);
            Motion.GetParticleMomentOfInertia(MomentOfInertia);
        }

        [DataMember]
        private readonly double m_Length;
        [DataMember]
        private readonly double m_Thickness;
        [DataMember]
        private readonly double m_Exponent;

        /// <summary>
        /// Circumference. Approximated with sphere.
        /// </summary>
        protected override double Circumference => (2 * m_Length + 2 * m_Thickness + 2 * Math.PI * m_Thickness) / 2;

        /// <summary>
        /// Area occupied by the particle. 
        /// </summary>
        public override double Area => 4 * m_Length * m_Thickness * (SpecialFunctions.Gamma(1 + 1 / m_Exponent)).Pow2() / SpecialFunctions.Gamma(1 + 2 / m_Exponent);

        /// <summary>
        /// Moment of inertia. 
        /// </summary>
        override public double MomentOfInertia => (1 / 4.0) * Mass_P * (m_Length * m_Length + m_Thickness * m_Thickness);

        /// <summary>
        /// Level set function of the particle.
        /// </summary>
        /// <param name="X">
        /// The current point.
        /// </param>
        public override double LevelSetFunction(double[] X) {
            double alpha = -(Motion.GetAngle(0));
            double r;
            r = -Math.Pow(((X[0] - Motion.GetPosition(0)[0]) * Math.Cos(alpha) - (X[1] - Motion.GetPosition(0)[1]) * Math.Sin(alpha)) / m_Length, m_Exponent)
                - Math.Pow(((X[0] - Motion.GetPosition(0)[0]) * Math.Sin(alpha) + (X[1] - Motion.GetPosition(0)[1]) * Math.Cos(alpha)) / m_Thickness, m_Exponent)
                + 1;
            if (double.IsNaN(r) || double.IsInfinity(r))
                throw new ArithmeticException();
            return r;
        }

        /// <summary>
        /// Returns true if a point is withing the particle.
        /// </summary>
        /// <param name="point">
        /// The point to be tested.
        /// </param>
        /// <param name="minTolerance">
        /// Minimum tolerance length.
        /// </param>
        /// <param name="maxTolerance">
        /// Maximal tolerance length. Equal to h_min if not specified.
        /// </param>
        /// <param name="WithoutTolerance">
        /// No tolerance.
        /// </param>
        public override bool Contains(double[] point, double minTolerance, double maxTolerance = 0, bool WithoutTolerance = false) {
            WithoutTolerance = false;
            // only for rectangular cells
            if (maxTolerance == 0)
                maxTolerance = minTolerance;
            double radiusTolerance = 1;
            double a = !WithoutTolerance ? m_Length + Math.Sqrt(maxTolerance.Pow2() + minTolerance.Pow2()) : m_Length;
            double b = !WithoutTolerance ? m_Thickness + Math.Sqrt(maxTolerance.Pow2() + minTolerance.Pow2()) : m_Thickness;
            double Superellipsoid = Math.Pow(((point[0] - Motion.GetPosition(0)[0]) * Math.Cos(Motion.GetAngle(0)) + (point[1] - Motion.GetPosition(0)[1]) * Math.Sin(Motion.GetAngle(0))) / a, m_Exponent) + (Math.Pow((-(point[0] - Motion.GetPosition(0)[0]) * Math.Sin(Motion.GetAngle(0)) + (point[1] - Motion.GetPosition(0)[1]) * Math.Cos(Motion.GetAngle(0))) / b, m_Exponent));
            if (Superellipsoid < radiusTolerance)
                return true;
            else
                return false;
        }

        /// <summary>
        /// Returns an array with points on the surface of the particle.
        /// </summary>
        /// <param name="hMin">
        /// Minimal cell length. Used to specify the number of surface points.
        /// </param>
        override public MultidimensionalArray GetSurfacePoints(double hMin) {
            if (SpatialDim != 2)
                throw new NotImplementedException("Only two dimensions are supported at the moment");

            int NoOfSurfacePoints = Convert.ToInt32(1000 * Circumference / hMin);
            int QuarterSurfacePoints = NoOfSurfacePoints / 4;
            MultidimensionalArray SurfacePoints = MultidimensionalArray.Create(NoOfSubParticles, 4 * QuarterSurfacePoints - 2, SpatialDim);
            double[] Infinitisemalangle = GenericBlas.Linspace(0, Math.PI / 2, QuarterSurfacePoints + 2);
            if (Math.Abs(10 * Circumference / hMin + 1) >= int.MaxValue)
                throw new ArithmeticException("Error trying to calculate the number of surface points, overflow");
            for (int j = 0; j < QuarterSurfacePoints; j++) {
                SurfacePoints[0, j, 0] = (Math.Pow(Math.Cos(Infinitisemalangle[j]), 2 / m_Exponent) * m_Length * Math.Cos(Motion.GetAngle(0)) - Math.Pow(Math.Sin(Infinitisemalangle[j]), 2 / m_Exponent) * m_Thickness * Math.Sin(Motion.GetAngle(0))) + Motion.GetPosition(0)[0];
                SurfacePoints[0, j, 1] = (Math.Pow(Math.Cos(Infinitisemalangle[j]), 2 / m_Exponent) * m_Length * Math.Sin(Motion.GetAngle(0)) + Math.Pow(Math.Sin(Infinitisemalangle[j]), 2 / m_Exponent) * m_Thickness * Math.Cos(Motion.GetAngle(0))) + Motion.GetPosition(0)[1];
                SurfacePoints[0, 2 * QuarterSurfacePoints + j - 1, 0] = (-(Math.Pow(Math.Cos(Infinitisemalangle[j]), 2 / m_Exponent) * m_Length) * Math.Cos(Motion.GetAngle(0)) + Math.Pow(Math.Sin(Infinitisemalangle[j]), 2 / m_Exponent) * m_Thickness * Math.Sin(Motion.GetAngle(0))) + Motion.GetPosition(0)[0];
                SurfacePoints[0, 2 * QuarterSurfacePoints + j - 1, 1] = (-(Math.Pow(Math.Cos(Infinitisemalangle[j]), 2 / m_Exponent) * m_Length) * Math.Sin(Motion.GetAngle(0)) - Math.Pow(Math.Sin(Infinitisemalangle[j]), 2 / m_Exponent) * m_Thickness * Math.Cos(Motion.GetAngle(0))) + Motion.GetPosition(0)[1]; ;
            }
            for (int j = 1; j < QuarterSurfacePoints; j++) {
                SurfacePoints[0, 2 * QuarterSurfacePoints - j - 1, 0] = (-(Math.Pow(Math.Cos(Infinitisemalangle[j]), 2 / m_Exponent) * m_Length) * Math.Cos(Motion.GetAngle(0)) - Math.Pow(Math.Sin(Infinitisemalangle[j]), 2 / m_Exponent) * m_Thickness * Math.Sin(Motion.GetAngle(0))) + Motion.GetPosition(0)[0];
                SurfacePoints[0, 2 * QuarterSurfacePoints - j - 1, 1] = (-(Math.Pow(Math.Cos(Infinitisemalangle[j]), 2 / m_Exponent) * m_Length) * Math.Sin(Motion.GetAngle(0)) + Math.Pow(Math.Sin(Infinitisemalangle[j]), 2 / m_Exponent) * m_Thickness * Math.Cos(Motion.GetAngle(0))) + Motion.GetPosition(0)[1];
                SurfacePoints[0, 4 * QuarterSurfacePoints - j - 2, 0] = (Math.Pow(Math.Cos(Infinitisemalangle[j]), 2 / m_Exponent) * m_Length * Math.Cos(Motion.GetAngle(0)) + Math.Pow(Math.Sin(Infinitisemalangle[j]), 2 / m_Exponent) * m_Thickness * Math.Sin(Motion.GetAngle(0))) + Motion.GetPosition(0)[0];
                SurfacePoints[0, 4 * QuarterSurfacePoints - j - 2, 1] = (Math.Pow(Math.Cos(Infinitisemalangle[j]), 2 / m_Exponent) * m_Length * Math.Sin(Motion.GetAngle(0)) - Math.Pow(Math.Sin(Infinitisemalangle[j]), 2 / m_Exponent) * m_Thickness * Math.Cos(Motion.GetAngle(0))) + Motion.GetPosition(0)[1];
            }
            return SurfacePoints;
        }

        /// <summary>
        /// Returns the legnthscales of a particle.
        /// </summary>
        override public double[] GetLengthScales() {
            return new double[] { m_Length, m_Thickness };
        }
    }
}

