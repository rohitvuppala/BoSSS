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
using BoSSS.Foundation.Grid;
using BoSSS.Foundation.IO;
using BoSSS.Solution.Queries;
using ilPSP;
using System.Linq;
using System.Reflection;
using BoSSS.Platform;
using BoSSS.Foundation.Grid.Classic;
using BoSSS.Foundation.XDG;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using System.Runtime.Serialization;
using MPI.Wrappers;
using Mono.CSharp;
using System.Diagnostics;

namespace BoSSS.Solution.Control {

    /// <summary>
    /// Version 2 of the Application control
    /// </summary>
    [Serializable]
    [DataContract]
    public class AppControl {

        /// <summary>
        /// Returns the type of the solver main class;
        /// </summary>
        virtual public Type GetSolverType() {
            throw new NotImplementedException();
        }

        /// <summary>
        /// The generating code as text representation, this string can be used to create the control file
        /// by <see cref="FromCode(string, Type, out AppControl, out AppControl[])"/>
        /// </summary>
        [DataMember]
        public string ControlFileText {
            get;
            private set;
        }

        /// <summary>
        /// True, if the control object was generated by <see cref="FromCode(string, Type, out AppControl, out AppControl[])"/>.
        /// </summary>
        [DataMember]
        public bool GeneratedFromCode {
            get;
            private set;
        }

        /// <summary>
        /// Used, if the <see cref="ControlFileText"/> script returns an enumeration of control files.
        /// </summary>
        [DataMember]
        public int ControlFileText_Index {
            get;
            private set;
        }


        /// <summary>
        /// ctor.
        /// </summary>
        public AppControl() {
            this.FieldOptions = new Dictionary<string, FieldOpts>();
            this.BoundaryValues = new Dictionary<string, BoundaryValueCollection>(new InvariantCultureIgnoreCase_Comparer());
            this.BoundaryValueChanges = new Dictionary<string, string>(new InvariantCultureIgnoreCase_Comparer());
            this.Tags = new List<string>();
            this.m_InitialValues_Evaluators = new Dictionary<string, Func<double[], double>>();
            this.m_InitialValues = new Dictionary<string, IBoundaryAndInitialData>();
            this.NoOfMultigridLevels = 0;
        }

        [Serializable]
        class InvariantCultureIgnoreCase_Comparer : IEqualityComparer<string> {
            public bool Equals(string a, string b) {
                return a.Equals(b, StringComparison.InvariantCultureIgnoreCase);
            }

            public int GetHashCode(string obj) {
                return obj.GetHashCode();
            }
        }


        /// <summary>
        /// DG polynomial degree for fields;
        ///  - key: string, the field identification string
        ///  - value: options for the DG field
        /// </summary>
        [DataMember]
        public IDictionary<string, FieldOpts> FieldOptions {
            get;
            private set;
        }

        /// <summary>
        /// Adds an entry to <see cref="FieldOptions"/>.
        /// </summary>
        /// <param name="Degree">
        /// Polynomial degree of the DG field; if negative, the application choses a default value.
        /// </param>
        /// <param name="DGFieldName"></param>
        /// <param name="SaveOpt"></param>
        public void AddFieldOption(string DGFieldName, int Degree = -1, FieldOpts.SaveToDBOpt SaveOpt = FieldOpts.SaveToDBOpt.TRUE) {
            FieldOptions.Add(DGFieldName, new FieldOpts() {
                Degree = Degree,
                SaveToDB = SaveOpt
            });
        }

        /// <summary>
        /// Utility function for easier user interaction, (should) set all reasonable <see cref="FieldOptions"/>
        /// </summary>
        public virtual void SetDGdegree(int p) {
            throw new NotImplementedException();
        }



        /// <summary>
        /// General checking of the control object.
        /// - all control file options satisfy the conditions
        ///   specified in the corresponding
        ///   <see cref="ControlOptionRequirementAttribute"/>s. 
        /// - a grid is set
        /// - Override this
        ///   method to implement more complicated checks that e.g. depend on the
        ///   values of other attributes. For example, one could verify that an
        ///   LBB-condition is fulfilled.
        /// </summary>
        public virtual void Verify() {
            List<string> Problems = new List<string>();

            // parameter bounds
            // ================
            {
                Type currentType = this.GetType();
                List<Type> types = new List<Type>() { currentType };
                while(currentType.BaseType != null) {
                    currentType = currentType.BaseType;
                    types.Add(currentType);
                }

                foreach(Type type in types) {
                    foreach(FieldInfo field in type.GetFields()) {
                        object value = field.GetValue(this);
                        foreach(var attribute in field.GetCustomAttributes<ControlOptionRequirementAttribute>()) {
                            string errMsg = attribute.Verify(field.Name, value);
                            if(errMsg != null) {
                                Problems.Add(errMsg);
                            }
                        }
                    }
                }
            }

            // check grid
            // ==========
            {
                //if(this.GridFunc == null
                //    && (this.GridGuid == null || this.GridGuid == Guid.Empty)) {
                //    Problems.Add("No Grid specified.");
                //}
            }

            // throw exception
            // ===============

            if(Problems.Count > 0) {
                using(var stw = new StringWriter()) {
                    stw.WriteLine("Found {0} problem(s) in control object:");
                    for(int i = 0; i < Problems.Count; i++) {
                        stw.WriteLine(" #{0}: {1}", i, Problems[i]);
                    }

                    string Error = stw.ToString();
                    Console.WriteLine(Error);
                    throw new Exception(Error);
                }
            }
        }

        /// <summary>
        /// A collection of all boundary values for a specific edge tag,
        /// see <see cref="Evaluators"/>
        /// </summary>
        [Serializable]
        [DataContract]
        sealed public class BoundaryValueCollection {

            /// <summary>
            /// optional string to encode the type of the boundary
            /// condition (Neumann or Dirichlet b.c., ...)
            /// </summary>
            [DataMember]
            public string type;

            /// <summary>
            /// ctor.
            /// </summary>
            public BoundaryValueCollection() {
                this.m_BoundaryValues_Evaluators = new Dictionary<string, Func<double[], double, double>>();
                this.m_BoundaryValues = new Dictionary<string, IBoundaryAndInitialData>();
            }

            /// <summary>
            /// Unfortunately, we cannot serialize delegates.
            /// </summary>
            [NonSerialized]
            [JsonIgnore]
            Dictionary<string, Func<double[], double, double>> m_BoundaryValues_Evaluators;

            /// <summary>
            /// Serializeable representation of boundary data.
            /// </summary>
            [DataMember]
            Dictionary<string, IBoundaryAndInitialData> m_BoundaryValues;

            /// <summary>
            /// Steady-state or instationary boundary values.
            /// - key: a name for the boundary value, e.g. 'VelocityX'<br/>
            /// - value: some function that maps a space coordinate to some function value, i.e.  \f$ (\vec{x},t) \mapsto f(\vec{x},t)\f$ 
            /// </summary>
            /// <remarks>
            /// Adding delegates directly to this dictionary is possible for backward compatibility reasons,
            /// although this limits some functionality - e.g. the control object is usually not serializeable anymore.
            /// </remarks>
            [JsonIgnore]
            public IDictionary<string, Func<double[], double, double>> Evaluators {
                get {
                    if(m_BoundaryValues_Evaluators == null)
                        m_BoundaryValues_Evaluators = new Dictionary<string, Func<double[], double, double>>();

                    foreach(string name in m_BoundaryValues.Keys) {
                        if(!m_BoundaryValues_Evaluators.ContainsKey(name)) {
                            m_BoundaryValues_Evaluators.Add(name, m_BoundaryValues[name].Evaluate);
                        }
                    }
                    return m_BoundaryValues_Evaluators;
                }
            }

            /// <summary>
            /// Steady-state or instationary boundary values.
            /// - key: a name for the boundary value, e.g. 'VelocityX'<br/>
            /// - value: some function that maps a space coordinate to some function value, i.e.  \f$ (\vec{x},t) \mapsto f(\vec{x},t)\f$ 
            /// </summary>
            [JsonIgnore]
            public IDictionary<string, IBoundaryAndInitialData> Values {
                get {
                    return m_BoundaryValues;
                }
            }
        }


        /// <summary>
        /// A mapping from edge tag names to a collection of boundary values.<br/>
        /// key: edge tag names <see cref="GridCommons.EdgeTagNames"/><br/>
        /// value: boundary values for various fields.
        /// </summary>
        [DataMember]
        public IDictionary<string, BoundaryValueCollection> BoundaryValues {
            get;
            private set;
        }

        /// <summary>
        /// Creates an empty boundary condition
        /// </summary>
        /// <param name="EdgeTagName">Name of the boundary condition</param>
        public void AddBoundaryValue(string EdgeTagName) {
            if(!this.BoundaryValues.ContainsKey(EdgeTagName))
                this.BoundaryValues.Add(EdgeTagName, new BoundaryValueCollection());

        }

        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public IDictionary<string, string> BoundaryValueChanges {
            get;
            private set;
        }

        /// <summary>
        /// only for restarts with loaded grid, 
        /// changes a boundary condition in the loaded grid
        /// </summary>
        /// <param name="EdgeTagNames"></param>
        public void ChangeBoundaryCondition(string oldEdgeTagName, string newEdgeTagName) {
            if(!this.BoundaryValueChanges.ContainsKey(oldEdgeTagName))
                this.BoundaryValueChanges.Add(oldEdgeTagName, newEdgeTagName);
        }

        /// <summary>
        /// Adds a time-dependent boundary condition.
        /// </summary>
        /// <param name="EdgeTagName">Name of the boundary condition</param>
        /// <param name="fieldname">Name of the field for which the boundary condition is valid</param>
        /// <param name="value">Function of the boundary condition</param>
        public void AddBoundaryValue(string EdgeTagName, string fieldname, Func<double[], double, double> value) {
            if(!this.BoundaryValues.ContainsKey(EdgeTagName))
                this.BoundaryValues.Add(EdgeTagName, new BoundaryValueCollection());

            if(this.BoundaryValues[EdgeTagName].Evaluators.ContainsKey(fieldname)
                //||  this.BoundaryValues[EdgeTagName].TimedepBoundaryValues.ContainsKey(fieldname) 
                )
                throw new ArgumentException(string.Format("Boundary condition for field '{0}' and edge tag name '{1}' already specified.", EdgeTagName, fieldname));

            this.BoundaryValues[EdgeTagName].Evaluators.Add(fieldname, value);
        }

        /// <summary>
        /// Adds a boundary condition that does not depend on time.
        /// </summary>
        /// <param name="EdgeTagName">Name of the boundary condition</param>
        /// <param name="fieldname">Name of the field for which the boundary condition is valid</param>
        /// <param name="value">Function of the boundary condition</param>
        public void AddBoundaryValue(string EdgeTagName, string fieldname, Func<double[], double> value) {
            AddBoundaryValue(EdgeTagName, fieldname, (X, t) => value(X));
        }

        /// <summary>
        /// Adds a boundary condition, represented as formula text, e.g. <c>(X,t) => Math.Sin(X[1] + t*0.2)</c>.
        /// </summary>
        /// <param name="EdgeTagName">Name of the boundary condition</param>
        /// <param name="fieldname">Name of the field for which the boundary condition is valid</param>
        /// <param name="FormulaText">
        /// Text representation of a delegate, see <see cref="Formula"/>.
        /// </param>
        /// <param name="TimeDependent">
        /// Whether the formula in <paramref name="FormulaText"/> is time-dependent or not, see <see cref="Formula"/>.
        /// </param>
        public void AddBoundaryValue(string EdgeTagName, string fieldname, string FormulaText, bool TimeDependent) {
            if(!this.BoundaryValues.ContainsKey(EdgeTagName))
                this.BoundaryValues.Add(EdgeTagName, new BoundaryValueCollection());

            if(this.BoundaryValues[EdgeTagName].Evaluators.ContainsKey(fieldname))
                throw new ArgumentException(string.Format("Boundary condition for field '{0}' and edge tag name '{1}' already specified.", EdgeTagName, fieldname));

            this.BoundaryValues[EdgeTagName].Values.Add(fieldname, new Formula(FormulaText, TimeDependent));
        }

        /// <summary>
        /// Adds a boundary condition, represented by a general <see cref="IBoundaryAndInitialData"/>-object.
        /// </summary>
        /// <param name="EdgeTagName">Name of the boundary condition</param>
        /// <param name="fieldname">Name of the field for which the boundary condition is valid</param>
        /// <param name="data">
        /// General provider of initial/boundary data; In order to support full functionality (job management, etc.),
        /// the object must be serializeable.
        /// </param>
        public void AddBoundaryValue(string EdgeTagName, string fieldname, IBoundaryAndInitialData data) {
            if(!this.BoundaryValues.ContainsKey(EdgeTagName))
                this.BoundaryValues.Add(EdgeTagName, new BoundaryValueCollection());

            if(this.BoundaryValues[EdgeTagName].Evaluators.ContainsKey(fieldname))
                throw new ArgumentException(string.Format("Boundary condition for field '{0}' and edge tag name '{1}' already specified.", EdgeTagName, fieldname));

            this.BoundaryValues[EdgeTagName].Values.Add(fieldname, data);
        }

        [NonSerialized]
        Dictionary<string, Func<double[], double>> m_InitialValues_Evaluators;

        /// <summary>
        /// Initial values which are set at the start of the simulation.
        /// - key: identification of the DG field, see <see cref="BoSSS.Foundation.DGField.Identification"/>.
        /// - value: data or mathematical expression which is projected at application startup.
        /// </summary>
        /// <remarks>
        /// Adding delegates directly to this dictionary is possible for backward compatibility reasons,
        /// although this limits some functionality - e.g. the control object is usually not serializeable anymore.
        /// </remarks>
        [JsonIgnore]
        public IDictionary<string, Func<double[], double>> InitialValues_Evaluators {
            get {
                if(m_InitialValues_Evaluators == null)
                    m_InitialValues_Evaluators = new Dictionary<string, Func<double[], double>>();

                foreach(string name in InitialValues.Keys) {
                    if(!m_InitialValues_Evaluators.ContainsKey(name)) {
                        m_InitialValues_Evaluators.Add(name, X => InitialValues[name].Evaluate(X, 0.0));
                    }
                }
                return m_InitialValues_Evaluators;
            }
        }

        [DataMember]
        Dictionary<string, IBoundaryAndInitialData> m_InitialValues;

        /// <summary>
        /// Initial values which are set at the start of the simulation.
        /// - key: identification of the DG field, see <see cref="BoSSS.Foundation.DGField.Identification"/>.
        /// - value: data or mathematical expression which is projected at application startup.
        /// </summary>
        [JsonIgnore]
        public IDictionary<string, IBoundaryAndInitialData> InitialValues {
            get {
                return m_InitialValues;
            }
        }

        /// <summary>
        /// Adds an initial value to <see cref="InitialValues"/>
        /// </summary>
        /// <param name="fieldname">Name of the field for which the boundary condition is valid</param>
        /// <param name="value">Function of the boundary condition</param>
        public void AddInitialValue(string fieldname, IBoundaryAndInitialData value) {
            InitialValues.Add(fieldname, value);
        }




        ///// <summary>
        ///// Adds a time-dependent initial value (e.g. some external force field which may change over time).
        ///// </summary>
        ///// <param name="fieldname">Name of the DG field which should hold the initial value.</param>
        ///// <param name="value">Function of the boundary condition</param>
        //public void AddInitialValue(string fieldname, Func<double[], double> value) {
        //    this.InitialValues_Evaluators.Add(fieldname, value);
        //}

        /// <summary>
        /// Contains a list of queries that shall be evaluated at the end of
        /// the simulation
        /// </summary>
        [NonSerialized]
        [JsonIgnore]
        public readonly IDictionary<string, Query> Queries = new Dictionary<string, Query>();

        /// <summary>
        /// Saves restart Information: GUID of the restarted session and the time-step
        /// If empty, no restart is done.
        /// </summary>
        [DataMember]
        public Tuple<Guid, TimestepNumber> RestartInfo;

        /// <summary>
        /// The GUID of the grid to load. 
        /// The use of this member is exclusive with <see cref="GridFunc"/>, i.e. 
        /// this member may only be set unequal to null if <see cref="GridFunc"/> is null.
        /// </summary>
        [DataMember]
        public Guid GridGuid;

        /// <summary>
        /// Returns a grid object that should be used for the simulation.
        /// The use of this member is exclusive with <see cref="GridGuid"/>, i.e. 
        /// this member may only be set unequal to null if <see cref="GridGuid"/> is null.
        /// </summary>
        [NonSerialized]
        [JsonIgnore]
        public Func<IGrid> GridFunc;

        /// <summary>
        /// Sets <see cref="GridGuid"/>
        /// </summary>
        /// <param name="grd"></param>
        public void SetGrid(IGridInfo grd) {
            this.GridGuid = grd.ID;

            if(grd.Database == null || grd.Database.Path == null) {
                Console.WriteLine("Warning: grid seems not to be saved in a database");
            } else {
                if(this.DbPath == null) {
                    this.DbPath = grd.Database.Path;
                    Console.WriteLine("Info: setting database path to: " + this.DbPath);
                } else {
                    if(!this.DbPath.Equals(grd.Database.Path)) {
                        Console.WriteLine("Warning: database mismatch! (Grid is saved at {0}, while DbPath of control object is {2})", grd.Database.Path, this.DbPath);
                    }
                }
            }
        }


        /// <summary>
        /// Algorithm for grid partitioning.
        /// </summary>
        [DataMember]
        public GridPartType GridPartType = GridPartType.METIS;

        /// <summary>
        /// grid partitioning options
        /// </summary>
        [DataMember]
        public string GridPartOptions;

        /// <summary>
        /// tags/keywords to describe a solver run, to aid sorting/search for specific 
        /// solver runs in the database.
        /// </summary>
        [DataMember]
        public ICollection<string> Tags {
            get;
            private set;
        }

        /// <summary>
        /// Optional session name (for better identification).
        /// </summary>
        [DataMember]
        public string SessionName;

        /// <summary>
        /// Mandatory project name.
        /// </summary>
        [DataMember]
        public string ProjectName;

        /// <summary>
        /// optional project description.
        /// </summary>
        [DataMember]
        public string ProjectDescription;

        /// <summary>
        /// number of time-steps that the solver should perform;
        /// A negative value indicates that this value is not set within the control file;
        /// </summary>
        [DataMember]
        public int NoOfTimesteps = -1;

        /// <summary>
        /// physical time at which the solver terminates;
        /// </summary>
        [DataMember]
        public double Endtime = double.MaxValue;

        /// <summary>
        /// interval in which "restart files" should be written to the database
        /// </summary>
        [DataMember]
        public int saveperiod = 1;

        /// <summary>
        /// A number of previous timesteps which are always saved in case of a simulation crash.
        /// </summary>
        [DataMember]
        public int rollingSaves = 0;



        /// <summary>
        /// lower threshold for the time-step
        /// </summary>
        /// <remarks>
        /// A negative value indicates that this is not initialized;
        /// </remarks>
        [DataMember]
        public double dtMin = -1;

        /// <summary>
        /// upper threshold for the time-step;
        /// A negative value indicates that this is not initialized;
        /// </summary>
        /// <remarks>
        /// A negative value indicates that this is not initialized;
        /// </remarks>
        public double dtMax = -1;

        /// <summary>
        /// Sets/Gets a fixed time-step size.
        /// </summary>
        //[JsonIgnore]  
        [DataMember]
        public double dtFixed {
            get {
                if(dtMin != dtMax) {
                    return double.NaN;
                }
                return dtMin;
            }
            set {
                dtMin = value;
                dtMax = value;
            }
        }

        /// <summary>
        /// Checks if a fixed time-step size has been set and returns this value.
        /// </summary>
        public double GetFixedTimestep() {
            if(dtMin != dtMax) {
                throw new ApplicationException("Fixed time-step required; minimum and maximum time-step size must be set to same value.");
            }
            return dtMin;
        }


        /// <summary>
        /// See <see cref="CompMode"/>.
        /// </summary>
        public enum _CompMode {

            /// <summary>
            /// Instationary/Transient simulation.
            /// </summary>
            Transient,

            /// <summary>
            /// Steady-State calculation.
            /// </summary>
            Steady
        }


        /// <summary>
        /// For solvers which support both, stationary as well as transient simulations, the corresponding switch.
        /// </summary>
        [DataMember]
        public _CompMode CompMode;


        /// <summary>
        /// Immediate plot period: This variable controls immediate
        /// plotting, i.e. plotting during the solver run.
        /// A positive value indicates that
        /// <see cref="Application{T}.PlotCurrentState(double, TimestepNumber, int)"/>"/> will be called every
        /// <see cref="ImmediatePlotPeriod"/>-th time-step.
        /// A negative value turns immediate plotting off;
        /// </summary>
        [DataMember]
        public int ImmediatePlotPeriod = -1;

        /// <summary>
        /// Super sampling: This option controls whether a finer grid
        /// resolution shall be used in the plots created if <see cref="ImmediatePlotPeriod"/> is set positive.
        /// </summary>
        [DataMember]
        public int SuperSampling = 0;

        /// <summary>
        /// true if information should be written to the database, false
        /// if "passive io" (only reading grids, <see cref="BoSSS.Foundation.IO.IFileSystemDriver"/>)
        /// should be used;
        /// </summary>
        [DataMember]
        public bool savetodb = true;

        /// <summary>
        /// Activates tracing (record of method calls and returns) for selected namespaces, e.g. "BoSSS.Foundation,BoSSS.Solution".
        /// If not null and not empty, trace-files are written for every MPI rank.
        /// The created trace files are zipped,
        /// they can be viewed e.g. by 'gunzip -c trace.0.txt'.
        /// </summary>
        /// <seealso cref="BoSSS.Foundation.IO.IDatabaseDriver.InitTraceFile(SessionInfo)"/>
        [DataMember]
        public string TracingNamespaces = null;

        /// <summary>
        /// File system path to database.
        /// </summary>
        [DataMember]
        public string DbPath = null;

        /// <summary>
        /// Sets <see cref="DbPath"/>.
        /// </summary>
        public void SetDatabase(IDatabaseInfo dbi) {
            DbPath = dbi.Path;
        }
        

        /// <summary>
        /// location where the result files of a parameter study should be saved
        /// </summary>
        [DataMember]
        public string logFileDirectory = ".";

        /// <summary>
        /// If a parameter study is run, this member helps to distinct different cases (aka. sessions or runs) 
        /// of the study, which should be described by an enumeration of
        ///  parameter - name/value - pairs.
        /// </summary>
        [DataMember]
        public IEnumerable<Tuple<string, object>> Paramstudy_CaseIdentification = null;

        /// <summary>
        /// Continue parameter study if one case (aka. sessions or run) throws an exception.
        /// </summary>
        [DataMember]
        public bool Paramstudy_ContinueOnError = true;

        /// <summary>
        /// Number of aggregation multi-grid levels, <see cref="Application{T}.MultigridLevels"/>.
        /// </summary>
        [DataMember]
        public int NoOfMultigridLevels {
            get;
            set;
        }

        /// <summary>
        /// If true, a redistribution will be attempted BEFORE the first
        /// time-step starts
        /// </summary>
        [DataMember]
        public bool DynamicLoadBalancing_RedistributeAtStartup = false;

        /// <summary>
        /// A method that creates a new estimator for the runtime cost of individual cells
        /// </summary>
        [JsonIgnore]
        public List<Func<IApplication, int, ICellCostEstimator>> DynamicLoadBalancing_CellCostEstimatorFactories =
            new List<Func<IApplication, int, ICellCostEstimator>>();

        /// <summary>
        /// Number of time-steps, after which dynamic load balancing is performed; if negative, dynamic load balancing is turned off.
        /// </summary>
        [DataMember]
        public int DynamicLoadBalancing_Period = -1;

        /// <summary>
        /// Relative value, which is compared against the relative computation imbalance
        /// \f[ 
        /// \frac{\text{maximum runtime difference over all MPI processors}}{\text{maximum runtime over all MPI processors}} .
        /// \f]
        /// Dynamic load balancing is suppressed if the relative computation imbalance is below this value.
        /// </summary>
        [DataMember]
        public double DynamicLoadBalancing_ImbalanceThreshold = 0.12;

        /// <summary>
        /// Switch for turning dynamic load balancing on/off
        /// </summary>
        [DataMember]
        public bool DynamicLoadBalancing_On = false;

        /// <summary>
        /// switch for activating adaptive mesh refinement
        /// </summary>
        [DataMember]
        public bool AdaptiveMeshRefinement = false;

        /// <summary>
        /// Actual type of cut cell quadrature to use; If no XDG, is used, resp. no cut cells are present,
        /// this setting has no effect.
        /// </summary>
        [DataMember]
        public XQuadFactoryHelper.MomentFittingVariants CutCellQuadratureType = XQuadFactoryHelper.MomentFittingVariants.Saye;

        /// <summary>
        /// Calculation is not stopped if an I/O exception is thrown in <see cref="Application{T}.SaveToDatabase(TimestepNumber, double)"/>.
        /// </summary>
        [DataMember]
        public bool ContinueOnIoError = true;

        /// <summary>
        /// Used for control objects in work-flow management, 
        /// Converts object to a serializable text.
        /// </summary>
        public string Serialize() {
            JsonSerializer formatter = new JsonSerializer() {
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Auto,
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
                Formatting = Formatting.Indented
//                ObjectCreationHandling = ObjectCreationHandling.
            };
                        
            using(var tw = new StringWriter()) {
                tw.WriteLine(this.GetType().AssemblyQualifiedName);
                using(JsonWriter writer = new JsonTextWriter(tw)) {  // Alternative: binary writer: BsonWriter
                    formatter.Serialize(writer, this);
                }

                string Ret = tw.ToString();
                return Ret;
            }
            
            /*
            using(var ms = new MemoryStream()) {
                using(JsonWriter writer = new BsonWriter(ms)) {  // Alternative: binary writer: JsonTextWriter
                    formatter.Serialize(writer, this);
                }

                byte[] buffer = ms.GetBuffer();
                return buffer;
            }
            */
        }

        /// <summary>
        /// Used for control objects in work-flow management, 
        /// re-loads  an object from memory.
        /// </summary>
        public static AppControl Deserialize(string Str) {
            JsonSerializer formatter = new JsonSerializer() {
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Auto,
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                ReferenceLoopHandling = ReferenceLoopHandling.Error
            };

            
            using(var tr = new StringReader(Str)) {
                string typeName = tr.ReadLine();
                Type ControlObjectType = Type.GetType(typeName);
                using(JsonReader reader = new JsonTextReader(tr)) {
                    var obj = formatter.Deserialize(reader, ControlObjectType);

                    AppControl ctrl = (AppControl)obj;
                    return ctrl;
                }
              
            }
            
            
            /*
            using(var ms = new MemoryStream(buffer)) {
                using(JsonReader reader = new BsonReader(ms)) {
                    var obj = formatter.Deserialize(reader, ControlObjectType);

                    AppControl ctrl = (AppControl)obj;
                    return ctrl;
                }
              
            }
            */
        }

        /// <summary>
        /// Read control object from a script file.
        /// This is a driver routine for <see cref="FromCode(string, Type, out AppControl, out AppControl[])"/>.
        /// </summary>
        static public AppControl FromFile(string fileName, Type t = null, int Index = 0) {
            return FromCode(File.ReadAllText(fileName), t, Index);
        }

        /// <summary>
        /// Read control object from a string. 
        /// This is a driver routine for <see cref="FromCode(string, Type, out AppControl, out AppControl[])"/>.
        /// </summary>
        static public AppControl FromCode(string ctrlFileContent, Type t = null, int Index = 0) {
            FromCode(ctrlFileContent, t, out AppControl ctrl, out AppControl[] ctrl_ParamStudy);
            if(ctrl != null) {
                if (Index != 0)
                    throw new IndexOutOfRangeException();
                return ctrl;
            } else {
                return ctrl_ParamStudy[Index];
            }
        }

        /// <summary>
        /// Loads a control object, resp. a series of control objects (in the case of a parameter study)
        /// form a C#-script.
        /// </summary>
        /// <param name="ctrlfileContent">the script.</param>
        /// <param name="t">something derived from <see cref="AppControl"/></param>
        static public void FromCode(string ctrlfileContent, Type t, out AppControl ctrl, out AppControl[] ctrl_ParamStudy) {

            // try to get type from first line comment (a hack).
            if (t == null) {
                string FirstLine = null;
                using (StringReader strR1 = new StringReader(ctrlfileContent)) {
                    FirstLine = strR1.ReadLine();
                }
                if (FirstLine != null && FirstLine.StartsWith("//")) {
                    FirstLine = FirstLine.Substring(2);
                    try {
                        Type t2 = Type.GetType(FirstLine);

                        if (t2 != null)
                            t = t2;

                    } catch (Exception) {

                    }
                }
               
            }

            if (t == null)
                t = typeof(AppControl);



            var Settings = new CompilerSettings();
#if DEBUG
            Settings.Optimize = false;
#else
            Settings.Optimize = false;
#endif
            CompilerContext cmpCont = new CompilerContext(Settings, new ConsoleReportPrinter());
            Evaluator eval = new Evaluator(cmpCont);
            eval.InteractiveBaseClass = t;

            // Start from entry assembly and _not_
            // - don't use typeof(T).Assembly since T might be located different assembly than the control file
            // - don't use Assembly.GetEntryAssembly() as it is undefined if called by Nunit
            StackTrace stackTrace = new StackTrace();
            Assembly entryAssembly = stackTrace.GetFrame(1).GetMethod().DeclaringType.Assembly;
            var allAssis = Application.GetAllAssemblies();
            foreach (var assi in allAssis) {
                eval.ReferenceAssembly(assi);
            }

            object controlObj = null;

            using (StringReader strR = new StringReader(ctrlfileContent)) {

                bool result_set = false;
                string incompleteStatement = null;
                int lineno = 0;
                for (string line = strR.ReadLine(); line != null; line = strR.ReadLine()) {
                    lineno++;

                    // Remove any trailing multiline delimiters (for
                    // compatibility with older control files)
                    line = line.TrimEnd().TrimEnd('\\');

                    string statement;
                    if (incompleteStatement == null) {
                        statement = line;
                    } else {
                        statement = incompleteStatement + "\n" + line;
                    }

                    try {
                        incompleteStatement = eval.Evaluate(statement, out controlObj, out result_set);
                    } catch (Exception e) {
                        string message = String.Format(
                            "'{0}' during the interpretation of control file code, line {1}",
                            e.GetType().Name,
                            lineno);
                        throw new AggregateException(message, e);
                    }

                    if (cmpCont.Report.Errors > 0) {
                        throw new ApplicationException(
                            "Syntax error in control file line " + lineno + ": \n" + statement);
                    }
                }

                if (incompleteStatement != null) {
                    throw new ApplicationException(String.Format(
                        "Reached end of control file before statement starting with '{0}' was complete",
                        incompleteStatement.Substring(0, Math.Min(incompleteStatement.Length, 20))));
                }

                if (controlObj == null) {
                    throw new ApplicationException(
                        "Unable to create a control object from cs-script file.");
                }

                // return
                if (controlObj is System.Collections.IEnumerable) {
                    var enu = (System.Collections.IEnumerable)controlObj;

                    List<AppControl> _ctrl_ParameterStudy = new List<AppControl>();
                    //ctrl_ParameterStudy = new AppControl[enu.];
                    int i = 0;
                    foreach (object o in enu) {
                        AppControl c = (AppControl)o;
                        c.ControlFileText = ctrlfileContent;
                        c.GeneratedFromCode = true;
                        c.ControlFileText_Index = i;
                        _ctrl_ParameterStudy.Add(c);
                        i++;
                    }

                    ctrl_ParamStudy = _ctrl_ParameterStudy.ToArray();
                    ctrl = null;
                } else {
                    ctrl_ParamStudy = null;
                    ctrl = (AppControl)controlObj;
                    ctrl.ControlFileText = ctrlfileContent;
                    ctrl.ControlFileText_Index = 0;
                    ctrl.GeneratedFromCode = true;
                }
            }

        }

        
    }
}
