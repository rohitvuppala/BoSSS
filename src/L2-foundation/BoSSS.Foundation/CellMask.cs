/* =======================================================================
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
using System.Collections;
using MPI.Wrappers;
using ilPSP.Utils;
using System.Diagnostics;
using ilPSP;
using BoSSS.Foundation.Comm;
using BoSSS.Platform;
using System.Globalization;
using System.IO;

namespace BoSSS.Foundation.Grid {


    /// <summary>
    /// masks some cells in a <see cref="GridData"/>-object
    /// </summary>
    public class CellMask : ExecutionMask {

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="grddat"></param>
        /// <param name="mask">
        /// a "true" entry for all cells in grid <paramref name="grddat"/> that
        /// should be in the mask; The length of this array must not exceed
        /// <see cref="GridData.CellData.NoOfLocalUpdatedCells"/>
        /// </param>
        /// <param name="mt">
        /// <see cref="ExecutionMask.MaskType"/>
        /// </param>
        public CellMask(IGridData grddat, BitArray mask, MaskType mt = MaskType.Logical) :
            base(grddat, mask, mt) //
        {
            if (mask.Length != this.GetUpperIndexBound(grddat))
                throw new ArgumentException("Mismatch in number of cells/length of input bitmask.");
        }
        
        /// <summary>
        /// ctor
        /// </summary>
        public CellMask(IGridData grddat, int[] Sequence, MaskType mt = MaskType.Logical) :
            base(grddat, Sequence, mt) 
        { }

        /// <summary>
        /// complement of this mask (all cells that are NOT in this mask);
        /// </summary>
        public CellMask Complement() {
            return base.Complement<CellMask>();
        }

        /// <summary>
        /// compiles a cell mask from a set of chunks
        /// </summary>
        /// <param name="parts">
        /// a list of chunks, which may overlap
        /// </param>
        /// <param name="grddat">
        /// the grid that this mask will be associated with;
        /// </param>
        public CellMask(IGridData grddat, params Chunk[] parts)
            : this(grddat, (IEnumerable<Chunk>)parts, MaskType.Logical) {
        }

        /// <summary>
        /// compiles an quadrature execution mask from a set of chunks
        /// </summary>
        /// <param name="Parts">
        /// a list of chunks, which may overlap
        /// </param>
        /// <param name="grddat">
        /// the grid that this mask will be associated with;
        /// </param>
        /// <param name="mt">
        /// <see cref="ExecutionMask.MaskType"/>
        /// </param>
        public CellMask(IGridData grddat, IEnumerable<Chunk> Parts, MaskType mt = MaskType.Logical)
            : this(grddat, FromChunkEnum(Parts), mt)
        { }

        /// <summary>
        /// Retrieves an empty cell mask;
        /// </summary>
        /// <param name="grdDat">
        /// grid that the returned mask will be assigned to
        /// </param>
        /// <param name="mt">
        /// <see cref="ExecutionMask.MaskType"/>
        /// </param>
        static public CellMask GetEmptyMask(IGridData grdDat, MaskType mt = MaskType.Logical) {
            return new CellMask(grdDat, new int[0], mt);
        }

        /// <summary>
        /// Retrieves a mask containing all cells (i.e. returns the
        /// complement of <see cref="GetEmptyMask"/>)
        /// </summary>
        /// <param name="gridDat">
        /// Grid data that the returned mask will be assigned with
        /// </param>
        /// <returns>A full mask</returns>
        /// <param name="mt">
        /// <see cref="ExecutionMask.MaskType"/>
        /// </param>
        public static CellMask GetFullMask(IGridData gridDat, MaskType mt = MaskType.Logical) {
            int L;
            switch(mt) {
                case MaskType.Logical: L = gridDat.iLogicalCells.NoOfLocalUpdatedCells; break;
                case MaskType.Geometrical: L = gridDat.iGeomCells.Count; break;
                default: throw new NotImplementedException();
            }

            return new CellMask(gridDat, 
                new[] { new Chunk {
                    i0 = 0,
                    Len = L
                } },
                mt);
        }

        /// <summary>
        /// Selects all cells according to their cell centers, where <paramref name="SelectionFunction"/> is true
        /// </summary>
        /// <param name="gridData"></param>
        /// <param name="SelectionFunc"></param>
        /// <returns></returns>
        public static CellMask GetCellMask(Foundation.Grid.Classic.GridData gridDat, Func<double[], bool> SelectionFunc) {
            BitArray CellArray = new BitArray(gridDat.Cells.NoOfLocalUpdatedCells);
            MultidimensionalArray CellCenters = gridDat.Cells.CellCenter;
            for (int i = 0; i < gridDat.Cells.NoOfLocalUpdatedCells; i++) {
                switch (gridDat.SpatialDimension) {
                    case 1: {
                            CellArray[i] = SelectionFunc(new double[] { CellCenters[i, 0] });
                            break;
                        }
                    case 2: {
                            CellArray[i] = SelectionFunc(new double[] { CellCenters[i, 0], CellCenters[i, 1] });
                            break;
                        }
                    case 3: {
                            CellArray[i] = SelectionFunc(new double[] { CellCenters[i, 0], CellCenters[i, 1], CellCenters[i, 2] });
                            break;
                        }
                    default:
                        throw new ArgumentException();
                }
                
            }
            return new CellMask(gridDat, CellArray, MaskType.Logical);
        }

        /// <summary>
        /// like the ctor.
        /// </summary>
        protected override ExecutionMask CreateInstance(BitArray mask, MaskType mt) {
            return new CellMask(base.GridData, mask, mt);
        }

        /// <summary>
        /// see <see cref="ExecutionMask.GetUpperIndexBound"/>
        /// </summary>
        protected override int GetUpperIndexBound(IGridData gridData) {
            if(base.MaskType == MaskType.Logical)
                return gridData.iLogicalCells.NoOfLocalUpdatedCells;
            else if(base.MaskType == MaskType.Geometrical)
                return gridData.iGeomCells.Count;
            else
                throw new NotImplementedException();
        }

        BitArray m_BitMaskWithExternal;

        /// <summary>
        /// returns a bitmask that contains also information about external/ghost cells.
        /// </summary>
        public BitArray GetBitMaskWithExternal() {
            if(base.MaskType != MaskType.Logical)
                throw new NotSupportedException();

            MPICollectiveWatchDog.Watch(csMPI.Raw._COMM.WORLD);
            int JE = this.GridData.iLogicalCells.Count;
            int J = this.GridData.iLogicalCells.NoOfLocalUpdatedCells;
            int MpiSize = this.GridData.CellPartitioning.MpiSize;

            //Debugger.Break();

            if (MpiSize > 1) {
                // more than one MPI process
                // +++++++++++++++++++++++++

                if (m_BitMaskWithExternal == null) {
                    m_BitMaskWithExternal = new BitArray(JE, false);

                    // inner cells
                    foreach (Chunk c in this) {
                        for (int i = 0; i < c.Len; i++) {
                            m_BitMaskWithExternal[i + c.i0] = true;
                        }
                    }

                    m_BitMaskWithExternal.MPIExchange(this.GridData);
                }

                return m_BitMaskWithExternal;
            } else {
                // single - processor mode
                // +++++++++++++++++++++++
                return base.GetBitMask();
            }
        }

        int m_NoOfItemsLocally_WithExternal = -1;

        /// <summary>
        /// local number of cells, including external ones
        /// </summary>
        public int NoOfItemsLocally_WithExternal {
            get {
                if(base.MaskType != MaskType.Logical)
                    throw new NotSupportedException();

                if (m_NoOfItemsLocally_WithExternal < 0) {
                    if (GridData.CellPartitioning.MpiSize <= 1) {
                        m_NoOfItemsLocally_WithExternal = this.NoOfItemsLocally;
                    } else {
                        this.m_NoOfItemsLocally_WithExternal = base.NoOfItemsLocally;
                        int J = this.GridData.iLogicalCells.NoOfLocalUpdatedCells;
                        int JE = this.GridData.iLogicalCells.Count;
                        var mask = this.GetBitMaskWithExternal();

                        for (int j = J; j < JE; j++) {
                            if (mask[j])
                                this.m_NoOfItemsLocally_WithExternal++;
                        }
                    }
                }
                return m_NoOfItemsLocally_WithExternal;
            }
        }

        /// <summary>
        /// returns an enumerable structure that also contains external/ghost cells.
        /// </summary>
        public IEnumerable<Chunk> GetEnumerableWithExternal() {
            if(base.MaskType == MaskType.Geometrical)
                throw new NotSupportedException();

            if (this.GridData.CellPartitioning.MpiSize > 1) {
                var mskExt = GetBitMaskWithExternal();
               
                var R = new List<Chunk>(this);
                int J_update = this.GridData.iLogicalCells.NoOfLocalUpdatedCells;
                int JE = this.GridData.iLogicalCells.Count;
                Debug.Assert(mskExt.Count == JE);
                
                
                for (int j = J_update; j < JE; j++) {

                    if (mskExt[j]) {
                        Chunk ch;
                        ch.i0 = j;
                        ch.Len = 0;

                        while (j < JE && mskExt[j]) {
                            ch.Len++;
                            j++;
                        }

                        R.Add(ch);
                        j--;
                    }
                }

                return R;
            } else {
                return this;
            }
        }

        /// <summary>
        /// writes the center coordinates of all cells in this mask to some text file
        /// </summary>
        public override void SaveToTextFile(string fileName, bool WriteHeader = true, params ItemInfo[] infoFunc) {
            int D = GridData.SpatialDimension;
            int LI = infoFunc.Length;
            using (var file = new StreamWriter(fileName)) {
                if (WriteHeader) {

                    file.Write("Cell");
                    switch (D) {
                        case 1:
                        file.Write("\tx");
                        break;

                        case 2:
                        file.Write("\tx\ty");
                        break;

                        case 3:
                        file.Write("\tx\ty\tz");
                        break;

                        default:
                        throw new Exception();
                    }

                    for (int i = 0; i < LI; i++) {
                        file.Write("\ti(" + i + ")");
                    }
                    file.WriteLine();
                }
                double[] x = new double[D];

                foreach (int jLogCell in this.ItemEnum) { // loop over logical cells...
                    foreach (int jCell in this.GridData.GetGeometricCellIndices(jLogCell)) {
                        NodeSet localCenter = this.GridData.iGeomCells.GetRefElement(jCell).Center;
                        MultidimensionalArray globalCenters = MultidimensionalArray.Create(1, 1, D);
                        GridData.TransformLocal2Global(localCenter, jCell, 1, globalCenters, 0);

                        {
                            file.Write(jCell);
                            for (int d = 0; d < D; d++) {
                                file.Write("\t" + globalCenters[0, 0, d].ToString("e", NumberFormatInfo.InvariantInfo));
                            }

                            if (LI > 0) {
                                for (int d = 0; d < D; d++)
                                    x[d] = globalCenters[0, 0, d];
                                for (int li = 0; li < LI; li++) {
                                    double info_i = infoFunc[li](x, jLogCell, jCell);
                                    file.Write("\t" + info_i.ToString("e", NumberFormatInfo.InvariantInfo));
                                }
                            }

                            file.WriteLine();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// %
        /// </summary>
        public override bool Equals(object obj) {
            if (obj.GetType() != this.GetType())
                return false;
            return base.Equals(obj);
        }


        /// <summary>
        /// %
        /// </summary>
        public override int GetHashCode() {
            return base.GetHashCode();
        }

        /// <summary>
        /// All cells that share at least an edge with a cell in this mask.
        /// </summary>
        public CellMask AllNeighbourCells() {
            if(base.MaskType != MaskType.Logical)
                throw new NotSupportedException();

            int J = this.GridData.iLogicalCells.Count;
            BitArray retMask = new BitArray(J);

            var C2E = this.GridData.iLogicalCells.Cells2Edges;
            var E2C = this.GridData.iLogicalEdges.CellIndices;

            foreach (int jCell in this.ItemEnum) {
                int[] Edges = C2E[jCell];

                foreach (int em in Edges) {
                    int iEdge = Math.Abs(em) - 1;


                    int jOtherCell, ii;
                    if (em > 0) {
                        // jCell is IN => neighbour is OUT
                        jOtherCell = E2C[iEdge, 1];
                        ii = 0;
                    } else {
                        // jCell is OUT => neighbour is IN
                        jOtherCell = E2C[iEdge, 0];
                        ii = 1;
                    }
                    Debug.Assert(E2C[iEdge, ii] == jCell);
                    
                    if(jOtherCell >= 0) // boundary edge !
                        retMask[jOtherCell] = true;
                }
            }

            return new CellMask(this.GridData, retMask, MaskType.Logical);
        }

        /// <summary>
        /// Converts this  
        /// from a logical (<see cref="IGridData.iLogicalCells"/>) mask
        /// to a geometrical (<see cref="IGridData.iGeomCells"/>) mask.
        /// </summary>
        /// <returns></returns>
        public CellMask ToGeometicalMask() {
            if(base.MaskType != MaskType.Logical)
                throw new NotSupportedException();

            if (base.GridData is Grid.Classic.GridData) {
                // logical and geometrical cells are identical - return a clone of this mask
                return new CellMask(base.GridData, base.Sequence, MaskType.Geometrical);
            } else {

                int Jg = GridData.iGeomCells.Count;
                int[][] jl2jg = GridData.iLogicalCells.AggregateCellToParts;
                BitArray ba = new BitArray(Jg);
                foreach (Chunk c in this) { // loop over chunks of logical cells...
                    for (int jl = 0; jl < c.JE; jl++) { // loop over cells in chunk...
                        foreach (int jg in jl2jg[jl]) { // loop over geometrical cells in logical cell 'jl'...
                            ba[jg] = true;
                        }
                    }
                }

                return new CellMask(base.GridData, ba, MaskType.Geometrical);
            }
        }

        /// <summary>
        /// Converts this  
        /// from a geometrical (<see cref="IGridData.iGeomCells"/>) mask
        /// to a  logical (<see cref="IGridData.iLogicalCells"/>) mask.
        /// </summary>
        /// <returns></returns>
        public CellMask ToLogicalMask() {
            if(base.MaskType != MaskType.Geometrical)
                throw new NotSupportedException();

            if (base.GridData is Grid.Classic.GridData || GridData.iGeomCells.GeomCell2LogicalCell == null) {
                // logical and geometrical cells are identical - return a clone of this mask
                return new CellMask(base.GridData, base.Sequence, MaskType.Logical);
            } else {
                int[] jG2jL = GridData.iGeomCells.GeomCell2LogicalCell;

                int Jl = GridData.iLogicalCells.NoOfLocalUpdatedCells;
                BitArray ba = new BitArray(Jl);


                foreach (Chunk c in this) { // loop over chunks of logical cells...
                    for (int jG = 0; jG < c.JE; jG++) { // loop over cells in chunk...
                        int jL = jG2jL[jG];

                        ba[jL] = true;

                    }
                }

                var cmL = new CellMask(base.GridData, ba, MaskType.Logical);
#if DEBUG
                // convert new logical mask back to geometrical and see if both masks are equal
                BitArray shouldBeEqual = (cmL.ToGeometicalMask()).GetBitMask();
                BitArray thisMask = this.GetBitMask();
                Debug.Assert(shouldBeEqual.Length == thisMask.Length);
                for (int j = 0; j < shouldBeEqual.Length; j++) {
                    Debug.Assert(shouldBeEqual[j] == thisMask[j]);
                }

#endif
                return cmL;
            }
        }

    }


}

