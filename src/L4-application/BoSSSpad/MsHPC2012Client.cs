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
using Microsoft.Hpc.Scheduler;
using System.IO;
using Microsoft.Hpc.Scheduler.Properties;
using BoSSS.Platform;
using ilPSP;
using System.Runtime.Serialization;
using ilPSP.Tracing;

namespace BoSSS.Application.BoSSSpad {


    /// <summary>
    /// A <see cref="BatchProcessorClient"/>-implementation which uses a Microsoft HPC 2012 server.
    /// </summary>
    [DataContract]
    [Serializable]
    public class MsHPC2012Client : BatchProcessorClient {

        /// <summary>
        /// Empty Constructor for de-serialization
        /// </summary>
        private MsHPC2012Client() {
            //Console.WriteLine("MsHPC2012Client: empty ctor");
        }

        /// <summary>
        /// Ctor.
        /// </summary>
        /// <param name="DeploymentBaseDirectory">
        /// A directory location which must be accessible from both, the HPC server as well as the local machine.
        /// </param>
        /// <param name="ServerName">
        /// Name of the HPC server.
        /// </param>
        /// <param name="Username">
        /// Can be null for the local user.
        /// </param>
        /// <param name="Password">
        /// Password for user <paramref name="Username"/>, can be null if the user is the local user.
        /// </param>
        /// <param name="ComputeNodes">
        /// </param>
        /// <param name="DeployRuntime">
        /// See <see cref="BatchProcessorClient.DeployRuntime"/>.
        /// </param>
        public MsHPC2012Client(string DeploymentBaseDirectory, string ServerName, string Username = null, string Password = null, string[] ComputeNodes = null, bool DeployRuntime = true) {
            base.DeploymentBaseDirectory = DeploymentBaseDirectory;
            base.DeployRuntime = DeployRuntime;


            this.Username = Username;
            this.Password = Password;
            this.ComputeNodes = ComputeNodes;
            this.ServerName = ServerName;

            if (!Directory.Exists(base.DeploymentBaseDirectory))
                Directory.CreateDirectory(base.DeploymentBaseDirectory);

            if (this.Username == null)
                this.Username = System.Security.Principal.WindowsIdentity.GetCurrent().Name;

           
        }

        [NonSerialized]
        IScheduler m__scheduler;

        [DataMember]
        string Username;

        [DataMember]
        string Password;

        [DataMember]
        string ServerName;

        [DataMember]
        string[] ComputeNodes;

        /// <summary>
        /// Jobs are forced to run on a single node.
        /// </summary>
        [DataMember]
        public bool SingleNode = true;

        /// <summary>
        /// Access to the Microsoft HPC job scheduler interface.
        /// </summary>
        IScheduler Scheduler {
            get {
                if (m__scheduler == null) {
                    m__scheduler = new Scheduler();
                    m__scheduler.Connect(ServerName);
                }
                return m__scheduler;
            }
        }


        /// <summary>
        /// Job status.
        /// </summary>
        public override void EvaluateStatus(string idToken, object optInfo, string DeployDir, out bool isRunning, out bool isTerminated, out int ExitCode) {
            using (var tr = new FuncTrace()) {
                int id = int.Parse(idToken);



                ISchedulerJob JD;
                //if (optInfo != null && optInfo is ISchedulerJob _JD) {
                //    JD = _JD;
                //} else {
                using (new BlockTrace("Scheduler.OpenJob", tr)) {
                    JD = Scheduler.OpenJob(id);
                }
                
                //}
                /*
                 * the following seems really slow:
                 * 
                 * 
                List<SchedulerJob> allFoundJobs = new List<SchedulerJob>();
                ISchedulerCollection allJobs;
                using (new BlockTrace("Scheduler.GetJobList", tr)) {
                    allJobs = Scheduler.Get
                }
                int cc = allJobs.Count;
                Console.WriteLine("MsHpcClient: " + cc + " jobs.");
                tr.Logger.Info("list of " + cc + " jobs.");
                using (new BlockTrace("ID_FILTERING", tr)) {
                    foreach (SchedulerJob sJob in allJobs) {
                        if (sJob.Id != id)
                            continue;
                        allFoundJobs.Add(sJob);
                    }

                    if (allFoundJobs.Count <= 0) {
                        // some weird state
                        isRunning = false;
                        isTerminated = false;
                        ExitCode = int.MinValue;
                        return;
                    }
                }

                SchedulerJob JD;
                using (new BlockTrace("SORTING", tr)) {
                    JD = allFoundJobs.ElementAtMax(MsHpcJob => MsHpcJob.SubmitTime);
                }
                */


                using (new BlockTrace("TASK_FILTERING", tr)) {
                    ISchedulerCollection tasks = JD.GetTaskList(null, null, false);
                    ExitCode = int.MinValue;
                    foreach (ISchedulerTask t in tasks) {
                        DeployDir = t.WorkDirectory;
                        ExitCode = t.ExitCode;
                    }
                }

                using (new BlockTrace("STATE_EVAL", tr)) {
                    switch (JD.State) {
                        case JobState.Configuring:
                        case JobState.Submitted:
                        case JobState.Validating:
                        case JobState.ExternalValidation:
                        case JobState.Queued:
                            isRunning = false;
                            isTerminated = false;
                            break;

                        case JobState.Running:
                        case JobState.Finishing:
                            isRunning = true;
                            isTerminated = false;
                            break;

                        case JobState.Finished:
                            isRunning = false;
                            isTerminated = true;
                            break;

                        case JobState.Failed:
                        case JobState.Canceled:
                        case JobState.Canceling:
                            isRunning = false;
                            isTerminated = true;
                            break;

                        default:
                            throw new NotImplementedException("Unknown job state: " + JD.State);
                    }
                }
            }
        }


        /// <summary>
        /// Path to standard error file.
        /// </summary>
        public override string GetStderrFile(Job myJob) {
            string fp = Path.Combine(myJob.DeploymentDirectory, "stderr.txt");
            return fp;
        }
        /// <summary>
        /// Path to standard output file.
        /// </summary>
        public override string GetStdoutFile(Job myJob) {
            string fp = Path.Combine(myJob.DeploymentDirectory, "stdout.txt");
            return fp;
            
        }

        /// <summary>
        /// Submits the job to the Microsoft HPC server.
        /// </summary>
        public override (string id, object optJobObj) Submit(Job myJob) {
            using (new FuncTrace()) {
                string PrjName = InteractiveShell.WorkflowMgm.CurrentProject;

                ISchedulerJob MsHpcJob = null;
                ISchedulerTask task = null;

                // Create a job and add a task to the job.
                MsHpcJob = Scheduler.CreateJob();

                MsHpcJob.Name = myJob.Name;
                MsHpcJob.Project = PrjName;
                MsHpcJob.MaximumNumberOfCores = myJob.NumberOfMPIProcs;
                MsHpcJob.MinimumNumberOfCores = myJob.NumberOfMPIProcs;
                MsHpcJob.SingleNode = this.SingleNode;

                MsHpcJob.UserName = Username;

                task = MsHpcJob.CreateTask();
                task.MaximumNumberOfCores = myJob.NumberOfMPIProcs;
                task.MinimumNumberOfCores = myJob.NumberOfMPIProcs;
                
                task.WorkDirectory = myJob.DeploymentDirectory;

                using (var str = new StringWriter()) {
                    str.Write("mpiexec ");
                    str.Write(Path.GetFileName(myJob.EntryAssembly.Location));
                    foreach (string arg in myJob.CommandLineArguments) {
                        str.Write(" ");
                        str.Write(arg);
                    }

                    task.CommandLine = str.ToString();
                }
                foreach (var kv in myJob.EnvironmentVars) {
                    string name = kv.Key;
                    string valu = kv.Value;
                    task.SetEnvironmentVariable(name, valu);
                }

                task.StdOutFilePath = Path.Combine(myJob.DeploymentDirectory, "stdout.txt");
                task.StdErrFilePath = Path.Combine(myJob.DeploymentDirectory, "stderr.txt");

                if (ComputeNodes != null) {
                    foreach (string node in ComputeNodes)
                        MsHpcJob.RequestedNodes.Add(node);
                }


                MsHpcJob.AddTask(task);

                // Start the job.
                Scheduler.SubmitJob(MsHpcJob, Username != null ? Username : null, Password);

                return (MsHpcJob.Id.ToString(), MsHpcJob);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public override string ToString() {
            return $"MS HPC client {this.ServerName}, @{this.DeploymentBaseDirectory}";
        }
    }
}
