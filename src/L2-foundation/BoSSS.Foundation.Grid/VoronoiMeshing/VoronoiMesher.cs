﻿using System.Collections.Generic;
using System;
using BoSSS.Foundation.Grid.Voronoi.Meshing.Converter;

namespace BoSSS.Foundation.Grid.Voronoi.Meshing
{
    public class VoronoiMesher<T>
        where T : ILocatable, IVoronoiNodeCastable, new()
    {
        public class Settings
        {
            public VoronoiBoundary Boundary;

            public int NumberOfLloydIterations = 10;

            public int FirstCellNode_indice = 0;
        }

        internal IMesh<T> mesh;

        MeshingAlgorithm.Settings mesherSettings;

        readonly Settings settings;

        GridConverter<T> gridConverter; 

        public VoronoiMesher(VoronoiBoundary boundary)
        {
            settings = new Settings()
            {
                Boundary = boundary
            };
            Initialize();
        }

        public VoronoiMesher(Settings settings)
        {
            this.settings = settings;
            Initialize();
        }

        void Initialize()
        {
            CreateSetupForMeshingAlgorithm();
            CreateGridConverter();
        }

        void CreateSetupForMeshingAlgorithm()
        {
            mesherSettings = new MeshingAlgorithm.Settings
            {
                Boundary = settings.Boundary.Polygon,
                BoundingBox = settings.Boundary.BoundingBox,
                NumberOfLloydIterations = settings.NumberOfLloydIterations,
                FirstCellNodeIndice = settings.FirstCellNode_indice,
            };
            mesherSettings.PeriodicMap = PeriodicMapGenerator.GeneratePeriodicMap(
                mesherSettings, 
                settings.Boundary);
        }

        void CreateGridConverter()
        {
            if(mesherSettings.PeriodicMap != null)
            {
                gridConverter = new GridConverter<T>(settings.Boundary, mesherSettings.PeriodicMap);
            }
            else
            {
                gridConverter = new GridConverter<T>(settings.Boundary);
            }
        }

        protected void CreateMesh(List<T> nodes)
        {
            mesh = MeshingAlgorithm.ComputeMesh(nodes, mesherSettings);
        }

        public VoronoiGrid CreateGrid(List<T> nodes)
        {
            CreateMesh(nodes); 
            VoronoiGrid grid = gridConverter.ConvertToVoronoiGrid(mesh);
            return grid;
        }
    }
}
