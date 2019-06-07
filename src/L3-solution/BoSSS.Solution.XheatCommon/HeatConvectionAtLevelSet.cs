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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ilPSP;
using ilPSP.Utils;

using BoSSS.Foundation;
using BoSSS.Foundation.XDG;
using BoSSS.Solution.NSECommon;
using System.Collections;

namespace BoSSS.Solution.XheatCommon {


    public class HeatConvectionAtLevelSet : ILevelSetForm {

        LevelSetTracker m_LsTrk;

        bool movingmesh;

        public HeatConvectionAtLevelSet(int _D, LevelSetTracker LsTrk, double _capA, double _capB, double _LFFA, double _LFFB, ThermalMultiphaseBoundaryCondMap _bcmap, bool _movingmesh) {
            m_D = _D;

            capA = _capA;
            capB = _capB;
            m_LsTrk = LsTrk;

            //MaterialInterface = _MaterialInterface;
            movingmesh = _movingmesh;

            NegFlux = new HeatConvectionInBulk(_D, _bcmap, _capA, _capB, _LFFA, double.NaN, LsTrk);
            NegFlux.SetParameter("A", LsTrk.GetSpeciesId("A"));
            PosFlux = new HeatConvectionInBulk(_D, _bcmap, _capA, _capB, double.NaN, _LFFB, LsTrk);
            PosFlux.SetParameter("B", LsTrk.GetSpeciesId("B"));

        }

        //bool MaterialInterface;
        double capA;
        double capB;
        int m_D;

        // Use Fluxes as in Bulk Convection
        HeatConvectionInBulk NegFlux;
        HeatConvectionInBulk PosFlux;

        

        void TransformU(ref double[] U_Neg, ref double[] U_Pos, out double[] U_NegFict, out double[] U_PosFict) {
            //if(this.MaterialInterface) {

                U_NegFict = U_Pos;
                U_PosFict = U_Neg;

            //} else {
            //    throw new NotImplementedException();
            //}
        }


        public double LevelSetForm(ref CommonParamsLs cp, double[] U_Neg, double[] U_Pos, double[,] Grad_uA, double[,] Grad_uB, double v_Neg, double v_Pos, double[] Grad_vA, double[] Grad_vB) {
            double[] U_NegFict, U_PosFict;

            this.TransformU(ref U_Neg, ref U_Pos, out U_NegFict, out U_PosFict);

            //double[] U_LS = new double[] { 0, 1.0 };    // !!! prescribed
            //cp.ParamsNeg[0] = U_LS[0];
            //cp.ParamsNeg[1] = U_LS[1];
            //cp.ParamsNeg[m_D] = U_LS[0];
            //cp.ParamsNeg[m_D + 1] = U_LS[1];
            //cp.ParamsPos[0] = U_LS[0];
            //cp.ParamsPos[1] = U_LS[1];
            //cp.ParamsPos[m_D] = U_LS[0];
            //cp.ParamsPos[m_D + 1] = U_LS[1];

            double[] ParamsNeg = cp.ParamsNeg;
            double[] ParamsPos = cp.ParamsPos;
            double[] ParamsPosFict, ParamsNegFict;
            this.TransformU(ref ParamsNeg, ref ParamsPos, out ParamsNegFict, out ParamsPosFict);
            //Flux for negativ side
            double FlxNeg;
            {

                BoSSS.Foundation.CommonParams inp; // = default(BoSSS.Foundation.InParams);
                inp.Parameters_IN = ParamsNeg;
                inp.Parameters_OUT = ParamsNegFict;
                inp.Normale = cp.n;
                inp.iEdge = int.MinValue;
                inp.GridDat = this.m_LsTrk.GridDat;
                inp.X = cp.x;
                inp.time = cp.time;

                FlxNeg = this.NegFlux.IEF(ref inp, U_Neg, U_NegFict);
                //Console.WriteLine("FlxNeg = {0}", FlxNeg);
            }
            // Flux for positive side
            double FlxPos;
            {

                BoSSS.Foundation.CommonParams inp; // = default(BoSSS.Foundation.InParams);
                inp.Parameters_IN = ParamsPosFict;
                inp.Parameters_OUT = ParamsPos;
                inp.Normale = cp.n;
                inp.iEdge = int.MinValue;
                inp.GridDat = this.m_LsTrk.GridDat;
                inp.X = cp.x;
                inp.time = cp.time;

                FlxPos = this.PosFlux.IEF(ref inp, U_PosFict, U_Pos);
                //Console.WriteLine("FlxPos = {0}", FlxPos);
            }

            if(movingmesh)
                return 0.0;
            else
                return FlxNeg * v_Neg - FlxPos * v_Pos;
        }

        public IList<string> ArgumentOrdering {
            get {
                return new string[] { VariableNames.Temperature };
            }
        }

        public IList<string> ParameterOrdering {
            get {
                return ArrayTools.Cat(VariableNames.VelocityVector(m_D), (new string[] { "VelocityX_Mean", "VelocityY_Mean", "VelocityZ_Mean" }).GetSubVector(0, m_D));
            }
        }

        public int LevelSetIndex {
            get { return 0; }
        }

        public SpeciesId PositiveSpecies {
            get { return this.m_LsTrk.GetSpeciesId("B"); }
        }

        public SpeciesId NegativeSpecies {
            get { return this.m_LsTrk.GetSpeciesId("A"); }
        }

        public TermActivationFlags LevelSetTerms {
            get {
                return TermActivationFlags.UxV;
            }
        }
    }


    public class HeatConvectionAtLevelSet_Divergence : ILevelSetForm, ILevelSetEquationComponentCoefficient {

        LevelSetTracker m_lsTrk;

        public HeatConvectionAtLevelSet_Divergence(int _D, LevelSetTracker lsTrk, double _capA, double _capB, double _rhoA, double _rhoB,
            double vorZeichen, bool RescaleConti, double _kA, double _kB, double _hVapA, double _Rint, double _Tsat, double _sigma, double _pc) {
            this.D = _D;
            this.rhoA = _rhoA;
            this.rhoB = _rhoB;
            this.capA = _capA;
            this.capB = _capB;

            this.m_lsTrk = lsTrk;

            scaleA = vorZeichen;
            scaleB = vorZeichen;

            if (RescaleConti) {
                scaleA /= rhoA;
                scaleB /= rhoB;
            }

            //this.M = _M;
            this.kA = _kA;
            this.kB = _kB;
            this.hVapA = _hVapA;
            this.Rint = _Rint;
            //this.TintMin = _TintMin;
            this.Tsat = _Tsat;
            this.sigma = _sigma;
            this.pc = _pc;
        }

        int D;
        double rhoA;
        double rhoB;
        double capA;
        double capB;

        double scaleA;
        double scaleB;

        double kA;
        double kB;
        double hVapA;   // for the identification of the liquid phase
        double Rint;
        //double TintMin;
        double Tsat;
        double sigma;
        double pc;


        //double M;


        public TermActivationFlags LevelSetTerms {
            get {
                return TermActivationFlags.UxV;
            }
        }


        private double ComputeEvaporationMass_Macro(double[] GradT_A, double[] GradT_B, double[] n) {

            double hVap = 0.0;
            double qEvap = 0.0;
            if (hVapA > 0) {
                hVap = hVapA;
                for (int d = 0; d < D; d++)
                    qEvap += (kA * GradT_A[d] - kB * GradT_B[d]) * n[d];
            } else {
                hVap = -hVapA;
                for (int d = 0; d < D; d++)
                    qEvap += (kB * GradT_B[d] - kA * GradT_A[d]) * n[d];
            }

            return qEvap / hVap;
        }

        private double ComputeEvaporationMass_Micro(double T_A, double T_B, double curv, double p_disp) {

            if (hVapA == 0.0)
                return 0.0;

            double pc0 = (pc < 0.0) ? sigma * curv + p_disp : pc;      // augmented capillary pressure (without nonlinear evaporative masss part)

            double TintMin = 0.0;
            double hVap = 0.0;
            double qEvap = 0.0;
            if (hVapA > 0) {
                hVap = hVapA;
                TintMin = Tsat * (1 + (pc0 / (hVap * rhoA)));
                if (T_A > TintMin)
                    qEvap = -(T_A - TintMin) / Rint;
            } else if (hVapA < 0) {
                hVap = -hVapA;
                TintMin = Tsat * (1 + (pc0 / (hVap * rhoB)));
                if (T_B > TintMin)
                    qEvap = (T_B - TintMin) / Rint;
            }

            return qEvap / hVap;
        }


        private double ComputeEvaporationMass(double[] paramsNeg, double[] paramsPos, double[] N, bool microRegion) {

            double M = 0.0;
            if (microRegion) {
                M = ComputeEvaporationMass_Micro(paramsNeg[D], paramsPos[D], paramsNeg[D + 1], paramsNeg[D + 2]);
            } else {
                M = ComputeEvaporationMass_Macro(paramsNeg.GetSubVector(0, D), paramsPos.GetSubVector(0, D), N);
            }

            return M;

        }


        public double LevelSetForm(ref Foundation.XDG.CommonParamsLs cp,
            double[] U_Neg, double[] U_Pos, double[,] Grad_uA, double[,] Grad_uB,
            double vA, double vB, double[] Grad_vA, double[] Grad_vB) {

            Debug.Assert(cp.ParamsPos[D] == cp.ParamsNeg[D], "curvature must be continuous across interface");
            Debug.Assert(cp.ParamsPos[D + 1] == cp.ParamsNeg[D + 1], "disjoining pressure must be continuous across interface");

            //double M = ComputeEvaporationMass_Macro(cp.ParamsNeg.GetSubVector(0, D), cp.ParamsPos.GetSubVector(0, D), cp.n);
            //double M = ComputeEvaporationMass_Micro(cp.ParamsNeg[D], cp.ParamsPos[D], cp.ParamsNeg[D + 1], cp.ParamsNeg[D + 2]);
            double M = -0.1; // ComputeEvaporationMass(cp.ParamsNeg, cp.ParamsPos, cp.n, evapMicroRegion[cp.jCell]);
            if (M == 0.0)
                return 0.0;

            //Console.WriteLine("mEvap - GeneralizedDivergenceAtLevelSet: {0}", M);

            double uAxN = -M * (1 / rhoA) * U_Neg[0];
            double uBxN = -M * (1 / rhoB) * U_Pos[0];

            // transform from species B to A: we call this the "A-fictitious" value
            double uAxN_fict;
            //uAxN_fict = (1 / rhoA) * (rhoB * uBxN);
            uAxN_fict = uBxN;

            // transform from species A to B: we call this the "B-fictitious" value
            double uBxN_fict;
            //uBxN_fict = (1 / rhoB) * (rhoA * uAxN);
            uBxN_fict = uAxN;


            // compute the fluxes: note that for the continuity equation, we use not a real flux,
            // but some kind of penalization, therefore the fluxes have opposite signs!
            double FlxNeg = -Flux(uAxN, uAxN_fict); // flux on A-side
            double FlxPos = +Flux(uBxN_fict, uBxN);  // flux on B-side

            FlxNeg *= capA;
            FlxPos *= capB;

            double Ret = FlxNeg * vA - FlxPos * vB;

            return -Ret;
        }


        /// <summary>
        /// the penalty flux
        /// </summary>
        static double Flux(double UxN_in, double UxN_out) {
            return 0.5 * (UxN_in - UxN_out);
        }


        BitArray evapMicroRegion;

        public void CoefficientUpdate(CoefficientSet csA, CoefficientSet csB, int[] DomainDGdeg, int TestDGdeg) {

            if (csA.UserDefinedValues.Keys.Contains("EvapMicroRegion"))
                evapMicroRegion = (BitArray)csA.UserDefinedValues["EvapMicroRegion"];

        }


        public IList<string> ArgumentOrdering {
            get {
                return new string[] { VariableNames.Temperature };
            }
        }


        public IList<string> ParameterOrdering {
            get {
                return ArrayTools.Cat(new string[] { "GradTemp0_X", "GradTemp0_Y", "GradTemp0_Z" }.GetSubVector(0, D), "Curvature", "DisjoiningPressure"); //;
            }
        }


        public int LevelSetIndex {
            get { return 0; }
        }

        public SpeciesId PositiveSpecies {
            get { return this.m_lsTrk.GetSpeciesId("B"); }
        }

        public SpeciesId NegativeSpecies {
            get { return this.m_lsTrk.GetSpeciesId("A"); }
        }

    }


    public class HeatConvectionAtEvapLevelSet : ILevelSetForm
    {

        LevelSetTracker m_LsTrk;

        bool movingmesh;

        public HeatConvectionAtEvapLevelSet(int _D, LevelSetTracker LsTrk, double _capA, double _capB, double _LFFA, double _LFFB, ThermalMultiphaseBoundaryCondMap _bcmap, 
            double _hVapA, double _Rint, double _Tsat, double _rho, double _sigma, double _pc, double _kA, double _kB,bool _movingmesh)
        {
            m_D = _D;

            capA = _capA;
            capB = _capB;
            m_LsTrk = LsTrk;

            //MaterialInterface = _MaterialInterface;
            movingmesh = _movingmesh;

            NegFlux = new HeatConvectionInBulk(_D, _bcmap, _capA, _capB, _LFFA, double.NaN, LsTrk);
            NegFlux.SetParameter("A", LsTrk.GetSpeciesId("A"));
            PosFlux = new HeatConvectionInBulk(_D, _bcmap, _capA, _capB, double.NaN, _LFFB, LsTrk);
            PosFlux.SetParameter("B", LsTrk.GetSpeciesId("B"));


            this.hVapA = _hVapA;
            this.Rint = _Rint;
            this.Tsat = _Tsat;
            this.rho = _rho;
            this.sigma = _sigma;
            this.pc = _pc;

            this.kA = _kA;
            this.kB = _kB;

        }

        //bool MaterialInterface;
        double capA;
        double capB;
        int m_D;

        // Use Fluxes as in Bulk Convection
        HeatConvectionInBulk NegFlux;
        HeatConvectionInBulk PosFlux;


        double hVapA;   // for the identification of the liquid phase
        double Rint;
        double Tsat;
        double rho;     // density of liquid phase 
        double sigma;
        double pc;

        double kA;
        double kB;

        private double ComputeHeatFlux_Macro(double[] GradT_A, double[] GradT_B, double[] n) {

            double hVap = 0.0;
            double qEvap = 0.0;
            if (hVapA > 0) {
                hVap = hVapA;
                for (int d = 0; d < m_D; d++)
                    qEvap += (kA * GradT_A[d] - kB * GradT_B[d]) * n[d];
            } else {
                hVap = -hVapA;
                for (int d = 0; d < m_D; d++)
                    qEvap += (kB * GradT_B[d] - kA * GradT_A[d]) * n[d];
            }

            return qEvap;
        }

        private double ComputeHeatFlux_Micro(double T_A, double T_B, double curv, double p_disp) {

            if (hVapA == 0.0)
                return 0.0;

            double pc0 = (pc < 0.0) ? sigma * curv + p_disp : pc;      // augmented capillary pressure (without nonlinear evaporative masss part)

            double TintMin = 0.0;
            double hVap = 0.0;
            double qEvap = 0.0;
            if (hVapA > 0) {
                hVap = hVapA;
                TintMin = Tsat * (1 + (pc0 / (hVap * rho)));
                if (T_A > TintMin)
                    qEvap = -(T_A - TintMin) / Rint;
            } else if (hVapA < 0) {
                hVap = -hVapA;
                TintMin = Tsat * (1 + (pc0 / (hVap * rho)));
                if (T_B > TintMin)
                    qEvap = (T_B - TintMin) / Rint;
            }

            return qEvap;
        }

        private double ComputeHeatFlux(double[] paramsNeg, double[] paramsPos, double[] N, bool microRegion) {

            double qEvap = 0.0;
            if (microRegion) {
                qEvap = ComputeHeatFlux_Micro(paramsNeg[m_D], paramsPos[m_D], paramsNeg[m_D + 1], paramsNeg[m_D + 2]);
            } else {
                qEvap = ComputeHeatFlux_Macro(paramsNeg.GetSubVector(0, m_D), paramsPos.GetSubVector(0, m_D), N);
            }

            return qEvap;

        }



        void TransformU(ref double[] U_Neg, ref double[] U_Pos, out double[] U_NegFict, out double[] U_PosFict)
        {
            //if(this.MaterialInterface) {

            U_NegFict = U_Pos;
            U_PosFict = U_Neg;

            //} else {
            //    throw new NotImplementedException();
            //}
        }


        public double LevelSetForm(ref CommonParamsLs cp, double[] U_Neg, double[] U_Pos, double[,] Grad_uA, double[,] Grad_uB, double v_Neg, double v_Pos, double[] Grad_vA, double[] Grad_vB)
        {
            double[] U_NegFict, U_PosFict;

            this.TransformU(ref U_Neg, ref U_Pos, out U_NegFict, out U_PosFict);

            double[] ParamsNeg = cp.ParamsNeg;
            double[] ParamsPos = cp.ParamsPos;
            double[] ParamsPosFict, ParamsNegFict;
            this.TransformU(ref ParamsNeg, ref ParamsPos, out ParamsNegFict, out ParamsPosFict);

            // introducing kind of penalty flux
            // TODO: need to be checked with additional velocity convection
            ParamsNegFict[0] = -ParamsNegFict[0];
            ParamsNegFict[1] = -ParamsNegFict[1];
            ParamsNegFict[m_D] = -ParamsNegFict[m_D];
            ParamsNegFict[m_D+1] = -ParamsNegFict[m_D+1];
            ParamsPosFict[0] = -ParamsPosFict[0];
            ParamsPosFict[1] = -ParamsPosFict[1];
            ParamsPosFict[m_D] = -ParamsPosFict[m_D];
            ParamsPosFict[m_D+1] = -ParamsPosFict[m_D+1];

            //Flux for negativ side
            double FlxNeg;
            {

                BoSSS.Foundation.CommonParams inp; // = default(BoSSS.Foundation.InParams);
                inp.Parameters_IN = ParamsNeg;
                inp.Parameters_OUT = ParamsNegFict;
                inp.Normale = cp.n;
                inp.iEdge = int.MinValue;
                inp.GridDat = this.m_LsTrk.GridDat;
                inp.X = cp.x;
                inp.time = cp.time;

                //FlxNeg = this.NegFlux.IEF(ref inp, U_Neg, U_NegFict);
                FlxNeg = U_Neg[0] * (ParamsNeg[0] * inp.Normale[0] + ParamsNeg[1] * inp.Normale[1]);
            }
            // Flux for positive side
            double FlxPos;
            {

                BoSSS.Foundation.CommonParams inp; // = default(BoSSS.Foundation.InParams);
                inp.Parameters_IN = ParamsPosFict;
                inp.Parameters_OUT = ParamsPos;
                inp.Normale = cp.n;
                inp.iEdge = int.MinValue;
                inp.GridDat = this.m_LsTrk.GridDat;
                inp.X = cp.x;
                inp.time = cp.time;

                //FlxPos = this.PosFlux.IEF(ref inp, U_PosFict, U_Pos); 
                FlxPos = U_Pos[0] * (ParamsPos[0] * inp.Normale[0] + ParamsPos[1] * inp.Normale[1]); 
            }


            if (movingmesh)
                return 0.0;
            else
                return FlxNeg * v_Neg - FlxPos * v_Pos;
        }

        public IList<string> ArgumentOrdering {
            get {
                return new string[] { VariableNames.Temperature };
            }
        }

        public IList<string> ParameterOrdering {
            get {
                return ArrayTools.Cat(VariableNames.VelocityVector(m_D), (new string[] { "VelocityX_Mean", "VelocityY_Mean", "VelocityZ_Mean" }).GetSubVector(0, m_D),
                    new string[] { "GradTemp0_X", "GradTemp0_Y", "GradTemp0_Z" }.GetSubVector(0, m_D), "Temperature0", "Curvature", "DisjoiningPressure");
            }
        }

        public int LevelSetIndex {
            get { return 0; }
        }

        public SpeciesId PositiveSpecies {
            get { return this.m_LsTrk.GetSpeciesId("B"); }
        }

        public SpeciesId NegativeSpecies {
            get { return this.m_LsTrk.GetSpeciesId("A"); }
        }

        public TermActivationFlags LevelSetTerms {
            get {
                return TermActivationFlags.UxV;
            }
        }
    }

}
