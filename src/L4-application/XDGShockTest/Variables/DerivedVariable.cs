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

using BoSSS.Foundation;
using BoSSS.Foundation.Grid;
using BoSSS.Foundation.XDG;
using BoSSS.Solution.CompressibleFlowCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XDGShockTest {
    public class DerivedVariable<T> : Variable {

        /// <summary>
        /// The update function to be invoked after each (sub-)time-step
        /// </summary>
        public Action<T, Program> UpdateFunction;

        public DerivedVariable(string name, VariableTypes type, Action<T, Program> updateFunction) : base(name, type) {
            this.UpdateFunction = updateFunction;
        }
    }
}