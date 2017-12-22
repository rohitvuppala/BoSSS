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

using BoSSS.Foundation.Grid;
using BoSSS.Platform.Utils.Geom;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BoSSS.Platform;
using ilPSP.Utils;
using BoSSS.Foundation;
using ilPSP.LinSolvers;
using ilPSP;
using ilPSP.Tracing;
using MPI.Wrappers;
using BoSSS.Foundation.Grid.Classic;

namespace BoSSS.Foundation.Grid.Aggregation {

    static public class CoarseningAlgorithms {

        /// <summary>
        /// Creates a sequence of aggregated grids, suitable for a multigrid algorithm
        /// </summary>
        /// <param name="GridDat">original grid</param>
        /// <param name="MaxDepth">maximum number of refinements</param>
        /// <param name="skip">A negative number is mapped to some default behavior.</param>
        /// <returns></returns>
        public static AggregationGrid[] CreateSequence(GridData GridDat, int MaxDepth = -1, int skip = -1) {
            using(new FuncTrace()) {
                int D = GridDat.SpatialDimension;
                skip = skip > 0 ? skip : D;
                MaxDepth = MaxDepth >= 0 ? MaxDepth : int.MaxValue;
                //int cutoff = MaxDepth < 0 ? int.MaxValue : MaxDepth * skip;


                // create sequence of aggregation multigrid grids and basises 
                // ==========================================================

                List<AggregationGrid> aggGrids = new List<AggregationGrid>();
                aggGrids.Add(ZeroAggregation(GridDat));
                while(true) {
                    if( aggGrids.Count >= MaxDepth)
                        break;

                    AggregationGrid grid = aggGrids.Last();
                    //for (int iSkip = 0; iSkip < Math.Max(1, skip); iSkip++) {
                        grid = Coarsen(grid);
                    //}

                    if ((grid.iLogicalCells.NoOfLocalUpdatedCells.MPISum() >= aggGrids.Last().iLogicalCells.NoOfLocalUpdatedCells.MPISum()))
                        // no more refinement possible
                        break;
                    
                    aggGrids.Add(grid);


#if DEBUG
                    int iLevel = aggGrids.Count - 2;
                    int JFine = aggGrids[iLevel].iLogicalCells.NoOfCells;
                    int JCoarse = aggGrids[iLevel + 1].iLogicalCells.NoOfCells;
                    Debug.Assert(aggGrids[iLevel + 1].iLogicalCells.NoOfCells == (aggGrids[iLevel + 1].iLogicalCells.NoOfLocalUpdatedCells + aggGrids[iLevel + 1].iLogicalCells.NoOfExternalCells));

                    //Debugger.Launch();

                    // test that the coarse grid has significantly less cells than the fine grid.
                    double dJfine = aggGrids[iLevel].iLogicalCells.NoOfLocalUpdatedCells;
                    double dJcoarse = aggGrids[iLevel + 1].iLogicalCells.NoOfLocalUpdatedCells;
                    if (JCoarse >= 10)
                        Debug.Assert(dJfine * 0.8 >= dJcoarse);

                    // test the coarse-to-fine map
                    bool[] testMarker = new bool[JFine];
                    int[][] C2F = aggGrids[iLevel + 1].jCellCoarse2jCellFine;
                    Debug.Assert(C2F.Length == JCoarse);
                    for(int jC = 0; jC < JCoarse; jC++) {
                        foreach(int jF in C2F[jC]) {
                            Debug.Assert(testMarker[jF] == false);
                            testMarker[jF] = true;
                        }
                    }
                    for(int jF = 0; jF < JFine; jF++) {
                        Debug.Assert(testMarker[jF] == true);
                    }

                    // test the fine-to-coarse mapping
                    int[] F2C = aggGrids[iLevel + 1].jCellFine2jCellCoarse;
                    Debug.Assert(F2C.Length == JFine);
                    for (int jF = 0; jF < JFine; jF++) {
                        Debug.Assert(C2F[F2C[jF]].Contains(jF));
                    }

#endif
                }


                // return
                return aggGrids.ToArray();
            }
        }

        /// <summary>
        /// creates an initial aggregated grid which is in fact equivalent to <paramref name="g"/>
        /// </summary>
        public static AggregationGrid ZeroAggregation(GridData g) {
            var Cls = g.Cells;
            int J = Cls.NoOfLocalUpdatedCells;
            int D = g.SpatialDimension;

            int[][] AggregateCells = new int[J][];
            for(int j = 0; j < J; j++) {
                AggregateCells[j] = new int[] { j };
            }

            AggregationGrid ret = new AggregationGrid(g, AggregateCells);
            
            return ret;
        }

        /// <summary>
        /// coarsens level <paramref name="ag"/>
        /// </summary>
        public static AggregationGrid Coarsen(IGridData ag) {
            using(new FuncTrace()) {
                int J = ag.iLogicalCells.NoOfLocalUpdatedCells;
                int D = ag.SpatialDimension;

                // sort cells of parent grid by size:
                // we want to aggregate the smallest cells at first.
                int[] Perm = J.ForLoop(j => j).OrderBy(j => ag.iLogicalCells.GetCellVolume(j)).ToArray();

                BitArray UsedCellMarker = new BitArray(J);
                
                List<int[]> Coarsened_ComositeCells = new List<int[]>();
                //List<double> Coarsened_CompositeVolume = new List<double>();
                //List<BoundingBox> Coarsened_CompositeCellBB = new List<BoundingBox>();
                int[] Parent2Child = new int[J];
                List<int[]> child2Parrent = new List<int[]>();

                
                // loop over aggregated cells of parent grid...
                int[][] Neighbourship = ag.iLogicalCells.CellNeighbours;
                for(int i = 0; i < J; i++) {
                    int jCell = Perm[i];
                    Debug.Assert(Neighbourship[jCell].Contains(jCell) == false);
                    if(!UsedCellMarker[jCell]) {

                        // list of all neighbor cells which were not already aggregated to some other cell:
                        int[] UnusedNeighs = Neighbourship[jCell].Where(j => j < J && !UsedCellMarker[j]).ToArray();
                        int NN = UnusedNeighs.Length;

                        if(NN > 0) {
                            double[] sizes = new double[NN];
                            BoundingBox[] aggBB = new BoundingBox[NN];
                            double[] aggBBaspect = new double[NN];
                            for(int iNeig = 0; iNeig < NN; iNeig++) {
                                int jCellNeigh = UnusedNeighs[iNeig];

                                aggBB[iNeig] = new BoundingBox(D); //ag.CompositeCellBB[jCell].CloneAs();
                                ag.iLogicalCells.GetCellBoundingBox(jCell, aggBB[iNeig]);

                                BoundingBox NeighBB = new BoundingBox(D);
                                ag.iLogicalCells.GetCellBoundingBox(jCellNeigh, NeighBB);

                                aggBB[iNeig].AddBB(NeighBB);
                                sizes[iNeig] = ag.iLogicalCells.GetCellVolume(jCell) + ag.iLogicalCells.GetCellVolume(jCellNeigh);
                                aggBBaspect[iNeig] = aggBB[iNeig].AspectRatio;
                            }

                            double[] RelSizes = sizes.CloneAs(); RelSizes.ScaleV(1.0 / sizes.Max());
                            double[] RelAspects = aggBBaspect.CloneAs(); RelAspects.ScaleV(1.0 / aggBBaspect.Max());

                            double[] Quality = new double[NN];
                            Quality.AccV(0.7, RelSizes);
                            Quality.AccV(0.3, RelAspects);

                            int iChoice = Quality.IndexOfMin(q => q);
                            int jNeigChoice = UnusedNeighs[iChoice];

                            //int[] CCC = ArrayTools.Cat(ag.AggregateCells[jCell], ag.AggregateCells[jNeigChoice]);
                            int[] CCC = new int[] { jCell, jNeigChoice };
                            Coarsened_ComositeCells.Add(CCC);
                            //Coarsened_CompositeCellBB.Add(aggBB[iChoice]);
                            //Coarsened_CompositeVolume.Add(sizes[iChoice]);
                            Debug.Assert(jCell != jNeigChoice);
                            child2Parrent.Add(new int[] { jCell, jNeigChoice });
                            Parent2Child[jCell] = Coarsened_ComositeCells.Count - 1;
                            Parent2Child[jNeigChoice] = Coarsened_ComositeCells.Count - 1;


                            UsedCellMarker[jNeigChoice] = true;
                            UsedCellMarker[jCell] = true;
                        } else {
                            // No neighbour available => unable to coarsen

                            Coarsened_ComositeCells.Add(new int[] { jCell });
                            //Coarsened_ComositeCells.Add(ag.AggregateCells[jCell]);
                            //Coarsened_CompositeCellBB.Add(ag.CompositeCellBB[jCell].CloneAs());
                            //Coarsened_CompositeVolume.Add(ag.CompositeVolume[jCell]);
                            child2Parrent.Add(new int[] { jCell });
                            Parent2Child[jCell] = Coarsened_ComositeCells.Count - 1;

                            UsedCellMarker[jCell] = true;
                        }
                    } else {
                        // cell already done.
                        continue;
                    }
                }
                Debug.Assert(UsedCellMarker.ToBoolArray().Where(b => !b).Count() == 0, "some cell was not processed.");

                // return
                // ======
                return new AggregationGrid(ag, Coarsened_ComositeCells.ToArray());
            }
        }
        

       
        /// <summary>
        /// assigns the cell index of the aggregated cell <em>j</em> to all (fine) grid cells that 
        /// the aggregated cell <em>j</em> consists of.
        /// </summary>
        static public void ColorDGField(this AggregationGrid ag, DGField f) {
            if(!object.ReferenceEquals(f.GridDat, ag.AncestorGrid))
                throw new ArgumentException("mismatch in base grid.");


            f.Clear();
            int J = ag.iLogicalCells.NoOfLocalUpdatedCells;
            for(int j = 0; j < J; j++) { // loog over logical/aggregate cells
                int[] Neighs = ag.iLogicalCells.CellNeighbours[j];
                var NeighColors = Neighs.Select(jNeigComp => (int)Math.Round(f.GetMeanValue(ag.iLogicalCells.AggregateCellToParts[jNeigComp][0])));
                int iCol = 1;
                for(iCol = 1; iCol < 2 * J; iCol++) {
                    if(!NeighColors.Contains(iCol))
                        break;
                }
                
                foreach(int jGeom in ag.iLogicalCells.AggregateCellToParts[j]) {
                    
                    f.SetMeanValue(jGeom, iCol);
                    //f.SetMeanValue(jGeom, j);
                }
            }
        }
        

/*    
        /// <summary>
        /// Number of composite/aggregated cell in the aggregated grid.
        /// </summary>
        public int NoOfAggregateCells {
            get {
                int Jcomp = AggregateCells.Length;
                Debug.Assert(Jcomp == Neighbourship.Length);
                Debug.Assert(Jcomp == CompositeVolume.Length);
                Debug.Assert(Jcomp == CompositeCellBB.Length);
                return Jcomp;
            }
        }

        public int NoOfGhostCells {
            private set;
            get;
        }

        /// <summary>
        /// Neighborhood relations in the aggregation grid;<br/>
        /// - 1st index: MPI-local cell index of composite cell <em>j</em><br/>
        /// - 2nd index: enumeration; local composite cell indices of all neighbor cells of composite cell <em>j</em>
        /// </summary>
        public int[][] Neighbourship;
        */

            /*
        /// <summary>
        /// Cell volume of each composite cell.
        /// </summary>
        public double[] CompositeVolume;
        */

            /*

        /// <summary>
        /// Mapping from composite/aggegated cells to base grid cells.<br/>
        /// 1st index: composite/aggregated cell index <em>j</em> <br/>
        /// 2nd index: list of cells which from the aggregated cell <em>j</em>. <br/>
        /// </summary>
        public int[][] AggregateCells;


        int[] m_jCellToAggCell = null;
        int[] m_jCellToAggk = null;

        /// <summary>
        /// mapping from base grid cells to composite cells<br/>
        /// index: local cell index.
        /// </summary>
        public int[] jCellToAggCell {
            get {
                InitInverseMapping();
                return m_jCellToAggCell;
            }
        }

        /// <summary>
        /// mapping from base grid cells to cell index within aggregate cells<br/>
        /// index: local cell index.
        /// </summary>
        public int[] jCellToAggk {
            get {
                InitInverseMapping();
                return m_jCellToAggk;
            }
        }

        private void InitInverseMapping() {
            if(m_jCellToAggCell == null) {
                m_jCellToAggCell = new int[this.GridDat.Cells.NoOfLocalUpdatedCells];
                m_jCellToAggk = new int[this.GridDat.Cells.NoOfLocalUpdatedCells];
#if DEBUG
                m_jCellToAggCell.SetAll(-1234);
                m_jCellToAggk.SetAll(-123450);

#endif
                int JAGG = this.AggregateCells.Length;
                for(int jagg = 0; jagg < JAGG; jagg++) {
                    int[] AggCell = AggregateCells[jagg];

                    for(int k = AggCell.Length - 1; k >= 0; k--) {
                        int jCell = AggCell[k];
                        Debug.Assert(m_jCellToAggCell[jCell] < 0);
                        m_jCellToAggCell[jCell] = jagg;
                        m_jCellToAggk[jCell] = k;
                    }
                }
#if DEBUG
                foreach(int jagg in m_jCellToAggCell) {
                    Debug.Assert(jagg >= 0 && jagg < JAGG);
                }

                for(int j = 0; j < m_jCellToAggCell.Length; j++) {
                    int jAgg = m_jCellToAggCell[j];
                    int k = m_jCellToAggk[j];
                    Debug.Assert(this.AggregateCells[jAgg][k] == j);
                }
#endif
            }
        }


        /// <summary>
        /// Bounding boxes of the composite/aggregated cells.
        /// 
        /// Index: composite/aggregated cell index, correlates with first index of <see cref="AggregateCells"/>.
        /// </summary>
        public BoundingBox[] CompositeCellBB;


        */


    }
}
