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
using ilPSP;
using ilPSP.LinSolvers;
using BoSSS.Platform;
using ilPSP.Utils;
using BoSSS.Foundation;
using System.Diagnostics;
using BoSSS.Foundation.XDG;
using ilPSP.Tracing;
using BoSSS.Foundation.Grid.Aggregation;
using BoSSS.Foundation.Grid.Classic;
using BoSSS.Foundation.Grid;
using BoSSS.Foundation.Quadrature;

namespace BoSSS.Solution.Multigrid {


    /// <summary>
    /// DG basis on an aggregation grid (<see cref="AggregationGrid"/>).
    /// </summary>
    public class AggregationGridBasis {

        public static void CreateSequence(IEnumerable<AggregationGrid> _agSeq, Basis dgBasis) {
            // check input
            // -----------
            AggregationGrid[] agSeq = _agSeq.ToArray();
            if (agSeq.Length <= 0)
                throw new ArgumentException();
            if (!object.ReferenceEquals(agSeq[0].ParentGrid, agSeq[0].AncestorGrid))
                throw new ArgumentException("Parent and Ancestor of 0th grid level must be equal.");
            GridData baseGrid = (GridData)(agSeq[0].AncestorGrid);
            
            for(int iLevel = 0; iLevel < agSeq.Length; iLevel++) {
                if (agSeq[iLevel].MgLevel != iLevel)
                    throw new ArgumentException("Grid levels must be provided in order.");
                if (!object.ReferenceEquals(agSeq[iLevel].AncestorGrid, baseGrid))
                    throw new ArgumentException("Mismatch in ancestor grid.");
                if(iLevel > 0) {
                    if(!object.ReferenceEquals(agSeq[iLevel].ParentGrid, agSeq[iLevel - 1])) {
                        throw new ArgumentException("Mismatch in parent grid at level " + iLevel + ".");
                    }
                }
            }

            if (baseGrid.CellPartitioning.TotalLength != agSeq[0].CellPartitioning.TotalLength)
                throw new ArgumentException("Mismatch in number of cells for level 0.");

            if (!object.ReferenceEquals(baseGrid, dgBasis.GridDat))
                throw new ArgumentException("Mismatch between DG basis grid and multi-grid ancestor.");

            // Project Bounding-Box basis
            // --------------------------
            var BB = baseGrid.GlobalBoundingBox;
            int Jbase = baseGrid.Cells.NoOfLocalUpdatedCells;
            int p = dgBasis.Degree;
            int Np = dgBasis.Length;
            PolynomialList polyList = dgBasis.Polynomials[0];

            MultidimensionalArray a = MultidimensionalArray.Create(Jbase, Np, Np);
            {
                CellQuadrature.GetQuadrature(new int[2] { Np, Np }, baseGrid,
                    (new CellQuadratureScheme()).Compile(baseGrid, p * 2),
                    delegate (int j0, int Length, QuadRule QR, MultidimensionalArray _EvalResult) {
                        NodeSet nodes = QR.Nodes;
                        int D = nodes.SpatialDimension;
                        NodeSet GlobalNodes = new NodeSet(baseGrid.Grid.RefElements[0], Length * nodes.NoOfNodes, D);
                        baseGrid.TransformLocal2Global(nodes, j0, Length, GlobalNodes.ResizeShallow(Length, nodes.NoOfNodes, D));

                        for (int d = 0; d < D; d++) {
                            var Cd = GlobalNodes.ExtractSubArrayShallow(d, -1);
                            Cd.Scale(2 * (BB.Max[d] - BB.Min[d]));
                            Cd.AccConstant((BB.Max[d] + BB.Min[d]) / (BB.Min[d] - BB.Max[d]));
                        }
#if DEBUG
                    Debug.Assert(GlobalNodes.Min() >= -1.00001);
                        Debug.Assert(GlobalNodes.Max() <= +1.00001);
#endif
                    GlobalNodes.LockForever();

                        var BasisValues = dgBasis.CellEval(nodes, j0, Length);
                        var PolyVals = MultidimensionalArray.Create(GlobalNodes.NoOfNodes, Np);
                        polyList.Evaluate(GlobalNodes, PolyVals);
                        PolyVals = PolyVals.ResizeShallow(Length, nodes.NoOfNodes, Np);

                        _EvalResult.Multiply(1.0, BasisValues, PolyVals, 0.0, "jknm", "jkn", "jkm");
                    },
                    delegate (int i0, int Length, MultidimensionalArray ResultsOfIntegration) { // _SaveIntegrationResults:
                    a.ExtractSubArrayShallow(new[] { i0, 0, 0 }, new[] { i0 + Length - 1, Np - 1, Np - 1 }).Acc(1.0, ResultsOfIntegration);
                    }).Execute();
            }

            // Compute intermediate mass matrix of bounding box basis
            // ------------------------------------------------------
            MultidimensionalArray Mass_Level = MultidimensionalArray.Create(Jbase, Np, Np);
            Mass_Level.Multiply(1.0, a, a, 0.0, "jlk", "jnl", "jnk");

            // Orthonormalize and create injection operator
            // --------------------------------------------

            MultidimensionalArray B_prevLevel, B_Level;

            // level 0
            int Jagg = agSeq[0].iLogicalCells.NoOfLocalUpdatedCells;
            B_Level = MultidimensionalArray.Create(Jagg, Np, Np);
            int[][] Ag2Pt = agSeq[0].iLogicalCells.AggregateCellToParts;
            for(int j = 0; j < Jagg; j++) {
                int jGeom;
                 if(Ag2Pt == null || Ag2Pt[j] == null) {
                    jGeom = j;
                } else {
                    if (Ag2Pt[j].Length != 1)
                        throw new ArgumentException();
                    jGeom = Ag2Pt[j][0];
                }
                if (jGeom != j)
                    throw new NotSupportedException("todo");

                a.ExtractSubArrayShallow(jGeom, -1, -1).InvertTo(B_Level.ExtractSubArrayShallow(j, -1, -1));
            }

            MultidimensionalArray[][] Injectors = new MultidimensionalArray[agSeq.Length][];


            // all other levels
            MultidimensionalArray Mass_prevLevel;
            for(int iLevel = 1; iLevel < agSeq.Length; iLevel++) { // loop over levels...
                B_prevLevel = B_Level;
                Jagg = agSeq[iLevel].iLogicalCells.NoOfLocalUpdatedCells;
                Ag2Pt = agSeq[iLevel].iLogicalCells.AggregateCellToParts;
                int[][] C2F = agSeq[iLevel].jCellCoarse2jCellFine;

                var Injectors_iLevel = new MultidimensionalArray[Jagg];
                Injectors[iLevel] = Injectors_iLevel;

                B_Level = MultidimensionalArray.Create(Jagg, Np, Np);
                Mass_prevLevel = Mass_Level;
                Mass_Level = MultidimensionalArray.Create(Jagg, Np, Np);
                for (int j = 0; j < Jagg; j++) { // loop over aggregate cells

                    var Mass_j = Mass_Level.ExtractSubArrayShallow(j, -1, -1);
                    foreach (int jF in C2F[j]) { // loop over aggregated cells
                        Mass_j.Acc(1.0, Mass_prevLevel.ExtractSubArrayShallow(jF, -1, -1));
                    }

                    // perform ortho-normalization:
                    var B_Level_j = B_Level.ExtractSubArrayShallow(j, -1, -1);
                    Mass_j.SymmetricLDLInversion(B_Level_j, default(double[]));
#if DEBUG
                    // assert that B_Level_j is upper-triangular
                    for(int n = 0; n < Np; n++) {
                        for(int m = n + 1; m < Np; m++) {
                            Debug.Assert(B_Level_j[n, m] == 0.0);
                        }
                    }
#endif
                    // invert 'B' from previous level
                    var invB_prevLevel = MultidimensionalArray.Create(B_prevLevel.Lengths);
                    for(int jP = 0; jP < B_prevLevel.GetLength(0); jP++) {
                        B_prevLevel.ExtractSubArrayShallow(jP, -1, -1).InvertTo(invB_prevLevel.ExtractSubArrayShallow(jP, -1, -1));
                    }
                    
                    // create injector
                    Injectors_iLevel[j] = MultidimensionalArray.Create(C2F[j].Length, Np, Np);
                    for (int l = 0; l < C2F[j].Length; l++) { // loop over aggregated cells
                        int jF = C2F[j][l];

                        var Inj_jl = Injectors_iLevel[j].ExtractSubArrayShallow(l, -1, -1);
                        var invB = invB_prevLevel.ExtractSubArrayShallow(jF, -1, -1);
                        Inj_jl.Multiply(1.0, invB, B_Level_j, 0.0, "nm", "nk", "km");
                    }
                }
            }

            // create basis sequence
            // ---------------------


        }




        static GridData GetGridData(AggregationGrid ag) {
            if(ag.ParentGrid is GridData) {
                return ((GridData)ag.ParentGrid);
            } else if(ag.ParentGrid is AggregationGrid) {
                return GetGridData((AggregationGrid)ag.ParentGrid);
            } else {
                throw new NotSupportedException();
            }
        }


        public AggregationGridBasis ParentBasis {
            get;
            private set;
        }


        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="b">DG basis on original grid</param>
        /// <param name="ag"></param>
        public AggregationGridBasis(Basis b, AggregationGridBasis parentBasis, AggregationGrid ag) {
            using (new FuncTrace()) {
                if (!object.ReferenceEquals(b.GridDat, GetGridData(ag)))
                    throw new ArgumentException("mismatch in grid data object.");
                this.DGBasis = b;
                this.AggGrid = ag;

                if(!object.ReferenceEquals(ag.ParentGrid, parentBasis.AggGrid))
                    throw new ArgumentException("mismatch in parent grid.");

                ParentBasis = parentBasis;

                SetupProlongationOperator();
            }
        }

        MultidimensionalArray[] m_ProlongationOperator;


        /// <summary>
        /// Prolongation operator to finer grid level
        /// - array index: aggregate cell index; 
        /// - 1st index into <see cref="MultidimensionalArray"/>: index within aggregation basis, correlates with 2nd index into <see cref="AggregationGrid.jCellCoarse2jCellFine"/>
        /// - 2nd index into <see cref="MultidimensionalArray"/>: row
        /// - 3rd index into <see cref="MultidimensionalArray"/>: column
        /// - content: local cell index into the original grid, see <see cref="Foundation.Grid.ILogicalCellData.AggregateCellToParts"/>
        /// </summary>
        public MultidimensionalArray[] ProlongationOperator {
            get {
                return m_ProlongationOperator;
            }
        }


        void SetupProlongationOperator() {
            int Jagg = this.AggGrid.iLogicalCells.NoOfLocalUpdatedCells;

            if(object.ReferenceEquals(AggGrid.ParentGrid, AggGrid.AncestorGrid)) {
                // +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
                // top level - aggregation grid will just be lots of Id's - we should not store them
                // +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
#if DEBUG
                // we assume that the top level of the aggregation grid is equal to the ancestor grid.
                // (maybe there is a different MPI partition)

                var topAgg = AggGrid.iLogicalCells;
                
                Debug.Assert(AggGrid.CellPartitioning.TotalLength == AggGrid.AncestorGrid.CellPartitioning.TotalLength);

                for(int j = 0; j < topAgg.NoOfLocalUpdatedCells; j++) {
                    Debug.Assert(topAgg.AggregateCellToParts[j].Length == 1);
                }
#endif
            } else {
                // +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
                // some other level
                // +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

                m_ProlongationOperator = new MultidimensionalArray[Jagg];

                for(int j = 0; j < Jagg; j++) { // loop over aggregate cells



                    

                }
            }


            //m_ProlongationOperator = new MultidimensionalArray


        }



        /// <summary>
        /// restricts/projects a vector from the full grid (<see cref="BoSSS.Foundation.Grid.Classic.GridData"/>)
        /// to the aggregated grid (<see cref="AggGrid"/>).
        /// </summary>
        /// <param name="FullGridVector">input;</param>
        /// <param name="AggGridVector">output;</param>
        virtual public void RestictFromFullGrid<T, V>(T FullGridVector, V AggGridVector)
            where T : IList<double>
            where V : IList<double> //
        {
            var fullMapping = new UnsetteledCoordinateMapping(this.DGBasis);
            if (FullGridVector.Count != fullMapping.LocalLength)
                throw new ArgumentException("mismatch in vector length", "FullGridVector");
            int L = this.LocalDim;
            if (AggGridVector.Count != L)
                throw new ArgumentException("mismatch in vector length", "AggGridVector");

            var ag = this.AggGrid;
            var agCls = ag.iLogicalCells.AggregateCellToParts;
            int JAGG = ag.iLogicalCells.NoOfLocalUpdatedCells;
            int N = this.DGBasis.Length;

            var Buffer = MultidimensionalArray.Create(agCls.Max(cc => cc.Length), N);
            var Aggcoords = MultidimensionalArray.Create(N);

            for (int jAgg = 0; jAgg < JAGG; jAgg++) { // loop over all aggregated cells...
                int[] agCl = agCls[jAgg];
                int K = agCl.Length;

                MultidimensionalArray coords = (K * N == Buffer.GetLength(0)) ? Buffer : Buffer.ExtractSubArrayShallow(new int[] { 0, 0 }, new int[] { K - 1, N - 1 });

                for (int k = 0; k < K; k++) { // loop over the cells which form the aggregated cell...
                    int jCell = agCl[k];
                    int j0 = fullMapping.LocalUniqueCoordinateIndex(0, jCell, 0);
                    for (int n = 0; n < N; n++) {
                        coords[k, n] = FullGridVector[n + j0];
                    }
                }

                Aggcoords.Clear();
                Aggcoords.Multiply(1.0, this.CompositeBasis[jAgg], coords, 0.0, "n", "kmn", "km");

                int i0 = jAgg * N;
                for (int n = 0; n < N; n++)
                    AggGridVector[n + i0] = Aggcoords[n];
            }
        }


        /// <summary>
        /// Prolongates/injects a vector from the full grid (<see cref="BoSSS.Foundation.Grid.GridData"/>)
        /// to the aggregated grid (<see cref="AggGrid"/>).
        /// </summary>
        /// <param name="FullGridVector">output;</param>
        /// <param name="AggGridVector">input;</param>
        virtual public void ProlongateToFullGrid<T, V>(T FullGridVector, V AggGridVector)
            where T : IList<double>
            where V : IList<double> //
        {
            var fullMapping = new UnsetteledCoordinateMapping(this.DGBasis);
            if(FullGridVector.Count != fullMapping.LocalLength)
                throw new ArgumentException("mismatch in vector length", "FullGridVector");
            int L = this.LocalDim;
            if(AggGridVector.Count != L)
                throw new ArgumentException("mismatch in vector length", "AggGridVector");


            var ag = this.AggGrid;
            var agCls = ag.iLogicalCells.AggregateCellToParts;
            int JAGG = ag.iLogicalCells.NoOfLocalUpdatedCells;
            int N = this.DGBasis.Length;

            var FulCoords = new double[N];
            var AggCoords = new double[N];

            //MultidimensionalArray Trf = MultidimensionalArray.Create(N, N);

            for(int jAgg = 0; jAgg < JAGG; jAgg++) {
                int[] agCl = agCls[jAgg];
                int K = agCl.Length;

                int i0 = jAgg * N;
                for(int n = 0; n < N; n++)
                    AggCoords[n] = AggGridVector[n + i0];

                for(int k = 0; k < K; k++) { // loop over the cells wich form the aggregated cell...
                    int jCell = agCl[k];
                    int j0 = fullMapping.LocalUniqueCoordinateIndex(0, jCell, 0);

                    //Trf.Clear();
                    //Trf.Acc(1.0, this.CompositeBasis[jAgg].ExtractSubArrayShallow(k, -1, -1));
                    var Trf = this.CompositeBasis[jAgg].ExtractSubArrayShallow(k, -1, -1);

                    //Trf.Solve(FulCoords, AggCoords);
                    Trf.gemv(1.0, AggCoords, 0.0, FulCoords);

                    for(int n = 0; n < N; n++) {
                        FullGridVector[j0 + n] = FulCoords[n];
                    }
                }
            }
        }


        virtual public void GetRestrictionMatrix(BlockMsrMatrix rest, MultigridMapping mgMap, int iFld) {
            if(!object.ReferenceEquals(mgMap.AggBasis[iFld], this))
                throw new ArgumentException();

            UnsetteledCoordinateMapping fullmap = mgMap.ProblemMapping;
            int[] degree = mgMap.DgDegree;

            foreach(var b in fullmap.BasisS) {
                if(!b.IsSubBasis(this.DGBasis))
                    throw new ArgumentException("");
                if(b.MaximalLength != b.MinimalLength)
                    throw new NotSupportedException();
            }
            if(fullmap.BasisS.Count != degree.Length)
                throw new ArgumentException();
            int NoFld = degree.Length;

            int[] FullLength = fullmap.BasisS.Select(b => b.Length).ToArray();
            int[] RestLength = NoFld.ForLoop(
                l => fullmap.BasisS[l].Polynomials[0].Where(poly => poly.AbsoluteDegree <= degree[l]).Count());
            int[] RestOffset = new int[NoFld];
            for(int f = 1; f < NoFld; f++) {
                RestOffset[f] = RestOffset[f - 1] + RestLength[f - 1];
            }

            int NR = RestLength.Sum();
            int JAGG = this.AggGrid.iLogicalCells.NoOfLocalUpdatedCells;
            var ag = this.AggGrid;
            var agCls = ag.iLogicalCells.AggregateCellToParts;


            //Partitioning RowMap = new Partitioning(JAGG * NR);
            int MpiOffset_row = rest.RowPartitioning.i0;
            
            for(int jagg = 0; jagg < JAGG; jagg++) {
                int[] agCl = agCls[jagg];
                int K = agCl.Length;

                for (int k = 0; k < K; k++) { // loop over the cells which form the aggregated cell...
                    int jCell = agCl[k];

                    int M = RestLength[iFld];
                    int N = FullLength[iFld];
                    var Block = this.CompositeBasis[jagg].ExtractSubArrayShallow(new int[] { k, 0, 0 }, new int[] { k - 1, N - 1, M - 1 });

                    //for(int f = 0; f < NoFld; f++) { // loop over DG fields in mapping...

                    int i0Col = fullmap.GlobalUniqueCoordinateIndex(iFld, jCell, 0);
                    int i0Row = jagg * NR + RestOffset[iFld] + MpiOffset_row;

                    //rest.AccBlock(i0Row, i0Col, 1.0, Block);
                    for (int m = 0; m < M; m++) {
                        for (int n = 0; n < N; n++) {
                            rest[i0Row + m, i0Col + n] = Block[n, m];
                        }
                    }

                }
            }
        }

        
        public AggregationGrid AggGrid {
            get;
            private set;
        }

        public Basis DGBasis {
            get;
            private set;
        }

        public virtual int MaximalLength {
            get {
                return this.DGBasis.MaximalLength;
            }
        }

        public virtual int MinimalLength {
            get {
                return this.DGBasis.MinimalLength;
            }
        }


        int[] m_Lengths;

        public virtual int GetMaximalLength(int p) {
            Debug.Assert(this.DGBasis.MaximalLength == this.DGBasis.MinimalLength);
            return this.GetLength(0, p);
        }

        public virtual int GetMinimalLength(int p) {
            Debug.Assert(this.DGBasis.MaximalLength == this.DGBasis.MinimalLength);
            return this.GetLength(0, p);
        }
        
        public virtual int GetLength(int jCell, int p) {
            if(m_Lengths == null) {
                m_Lengths = new int[this.DGBasis.Degree + 1];
                for(int pp = 0; pp < m_Lengths.Length; pp++) {
                    m_Lengths[pp] = this.DGBasis.Polynomials[0].Where(poly => poly.AbsoluteDegree <= pp).Count();
                }
            }

            return m_Lengths[p];
        }

        /// <summary>
        /// Local vector-space dimension.
        /// </summary>
        virtual public int LocalDim {
            get {
                return this.AggGrid.iLogicalCells.NoOfLocalUpdatedCells * this.DGBasis.Length;
            }
        }

        /// <summary>
        /// The projector in the L2 Norm, from the space defined by the basis <see cref="DGBasis"/> on the original,
        /// onto the DG space on the aggregate grid.
        /// - array index: aggregate cell index; 
        /// - 1st index into <see cref="MultidimensionalArray"/>: index within aggregation basis 
        /// - 2nd index into <see cref="MultidimensionalArray"/>: row
        /// - 3rd index into <see cref="MultidimensionalArray"/>: column
        /// - content: local cell index into the original grid, see <see cref="Foundation.Grid.ILogicalCellData.AggregateCellToParts"/>
        /// </summary>
        public MultidimensionalArray[] CompositeBasis {
            get {
                if(m_CompositeBasis == null) {
                    SetupCompositeBasis();
                }
                return m_CompositeBasis;
            }
        }
        
        MultidimensionalArray[] m_CompositeBasis;

        void SetupCompositeBasis() {
            using(new FuncTrace()) {
                Basis b = this.DGBasis;
                AggregationGrid ag = this.AggGrid;
                Debug.Assert(object.ReferenceEquals(b.GridDat, GetGridData(ag)));
                int N = b.Length;
                

                int JAGG = ag.iLogicalCells.NoOfLocalUpdatedCells;
                m_CompositeBasis = new MultidimensionalArray[JAGG];

                for(int jAgg = 0; jAgg < JAGG; jAgg++) { // loop over agglomerated cells...
                    var compCell = ag.iLogicalCells.AggregateCellToParts[jAgg];


                    if(compCell.Length == 1) {
                        m_CompositeBasis[jAgg] = MultidimensionalArray.Create(1, N, N);
                        for(int n = 0; n < N; n++) {
                            m_CompositeBasis[jAgg][0, n, n] = 1.0;
                        }

                    } else {
                        // compute extrapolation basis
                        // ===========================

                        int I = compCell.Length - 1;
                        int[,] CellPairs = new int[I, 2];

                        for(int i = 0; i < I; i++) {
                            CellPairs[i, 0] = compCell[0];
                            CellPairs[i, 1] = compCell[i + 1];
                        }
                        var ExpolMtx = MultidimensionalArray.Create(I + 1, N, N);
                        b.GetExtrapolationMatrices(CellPairs, ExpolMtx.ExtractSubArrayShallow(new int[] { 1, 0, 0 }, new int[] { I, N - 1, N - 1 }));
                        for(int n = 0; n < N; n++) {
                            ExpolMtx[0, n, n] = 1.0;
                        }

                        // compute mass matrix
                        // ===================

                        var MassMatrix = MultidimensionalArray.Create(N, N); // intermediate mass matrix
                        MassMatrix.Multiply(1.0, ExpolMtx, ExpolMtx, 0.0, "lm", "kim", "kil");


                        // change to orthonormal basis
                        // ===========================
                        MultidimensionalArray B = MultidimensionalArray.Create(N, N);
                        MassMatrix.SymmetricLDLInversion(B, default(double[]));


                        m_CompositeBasis[jAgg] = MultidimensionalArray.Create(ExpolMtx.Lengths);
                        m_CompositeBasis[jAgg].Multiply(1.0, ExpolMtx, B, 0.0, "imn", "imk", "kn");

                        // check
                        // =====
#if DEBUG
                        MassMatrix.Clear();
                        for(int k = 0; k <= I; k++) {
                            for(int l = 0; l < N; l++) { // over rows of mass matrix ...
                                for(int m = 0; m < N; m++) { // over columns of mass matrix ...

                                    double mass_lm = 0.0;

                                    for(int i = 0; i < N; i++) {
                                        mass_lm += m_CompositeBasis[jAgg][k, i, m] * m_CompositeBasis[jAgg][k, i, l];
                                    }

                                    MassMatrix[l, m] += mass_lm;
                                }
                            }
                        }

                        MassMatrix.AccEye(-1.0);
                        Debug.Assert(MassMatrix.InfNorm() < 1.0e-9);
#endif
                    }
                }
            }
        }




        /// <summary>
        /// for XDG, the cell mode index <paramref name="n"/> may not be equal
        /// in the full and the aggregated grid. This method performs the transformation.
        /// </summary>
        virtual internal int N_Murks(int j, int n, int N) {
            Debug.Assert(j >= 0 && j < this.DGBasis.GridDat.iLogicalCells.NoOfCells);
            Debug.Assert(n >= 0 && n < this.DGBasis.GetLength(j));
            Debug.Assert(n < N);
            Debug.Assert(this.AggGrid.iLogicalCells.NoOfLocalUpdatedCells == this.DGBasis.GridDat.iLogicalCells.NoOfCells);
            return n;
        }

        virtual internal bool ReqModeIndexTrafo {
            get {
                return false;
            }
        }

        int[][] m_ModeIndexForDegree;

        virtual public int[] ModeIndexForDegree(int j, int p, int Pmax) {
            if(m_ModeIndexForDegree == null) {
                int Psupermax = this.DGBasis.Degree;
                m_ModeIndexForDegree = new int[Psupermax + 1][];
                for(int _p = 0; _p <= Psupermax; _p++) {
                    m_ModeIndexForDegree[_p] = new int[0];
                }

                Debug.Assert(this.DGBasis.Polynomials.Count == 1);
                var polys = this.DGBasis.Polynomials[0];

                for(int n = 0; n < polys.Count; n++) {
                    n.AddToArray(ref m_ModeIndexForDegree[polys[n].AbsoluteDegree]);
                }
            }
            return m_ModeIndexForDegree[p];
        }
        
    }

}
