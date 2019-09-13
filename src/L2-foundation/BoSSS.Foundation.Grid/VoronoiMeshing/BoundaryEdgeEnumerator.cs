﻿using BoSSS.Platform;
using BoSSS.Platform.LinAlg;
using System;
using System.Collections.Generic;

namespace BoSSS.Foundation.Grid.Voronoi.Meshing
{
    class BoundaryEdgeEnumerator<T>
    {
        InsideCellEnumerator<T> cells;

        public BoundaryEdgeEnumerator(Mesh<T> mesh)
        {
            cells = new InsideCellEnumerator<T>(mesh);
        }

        public IEnumerable<Edge<T>> Edges(Vector start, int firstCellNodeIndice)
        {
            cells.SetFirstCell(start, firstCellNodeIndice);
            MeshCell<T> firstCell = cells.GetFirstCell();
            return IterativeYieldBoundaryCells(firstCell);
        }

        static IEnumerable<Edge<T>> IterativeYieldBoundaryCells(MeshCell<T> cell)
        {
            Edge<T> currentEdge = FindFirstBoundaryEdge(cell);
            int startID = currentEdge.Start.ID;
            do
            {
                CyclicArray<Edge<T>> edges = EdgeCycleAfterEdge(currentEdge);
                for (int i = 0; i < edges.Length; ++i)
                {
                    currentEdge = edges[i];
                    if (currentEdge.IsBoundary)
                    {
                        yield return currentEdge;
                    }
                    else
                    {
                        currentEdge = currentEdge.Twin;
                        break;
                    }
                }
            }
            while (currentEdge.Start.ID != startID);
        }

        static Edge<T> FindFirstBoundaryEdge(MeshCell<T> cell)
        {
            foreach (Edge<T> edge in cell.Edges)
            {
                if (edge.IsBoundary)
                {
                    return edge;
                }
            }
            throw new Exception("Cell does not neighbor boundary.");
        }

        static CyclicArray<Edge<T>> EdgeCycleAfterEdge(Edge<T> edge)
        {
            Edge<T>[] edges = edge.Cell.Edges;
            CyclicArray<Edge<T>> reorderedEdges = new CyclicArray<Edge<T>>(edges);
            for (int i = 0; i < edges.Length; ++i)
            {
                if (edge.End.ID == edges[i].Start.ID)
                {
                    reorderedEdges.Start = i;
                    return reorderedEdges;
                }
            }
            throw new Exception("Edge not found in edges.");
        }
    }
}
