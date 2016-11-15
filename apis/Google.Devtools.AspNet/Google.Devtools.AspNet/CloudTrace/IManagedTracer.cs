﻿// Copyright 2016 Google Inc. All Rights Reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Generic;
using System.Diagnostics;

namespace Google.Devtools.AspNet
{
    /// <summary>
    /// Mangages creating spans for a trace as well as adding meta data to them.
    /// </summary>
    public interface IManagedTracer
    {
        /// <summary>
        /// Starts a new span using the most recent (if any) unfinished span as the parent.
        /// </summary>
        /// <param name="name">The name of the span.</param>
        void StartSpan(string name);

        /// <summary>
        /// Starts a new span using the most recent (if any) unfinished span as the parent.
        /// </summary>
        /// <param name="name">The name of the span.</param>
        /// <param name="options">The span options to override default values.</param>
        void StartSpan(string name, StartSpanOptions options);

        /// <summary>
        /// Ends the current span.
        /// </summary>
        void EndSpan();

        /// <summary>
        /// Annotates the current span with the given labels. 
        /// </summary>
        void AnnotateSpan(Dictionary<string, string> labels);

        /// <summary>
        /// Adds the give StackTrace to the current span.
        /// </summary>
        void SetStackTrace(StackTrace stackTrace);

        /// <summary>
        /// Gets the current trace id or null if none exists.
        /// </summary>
        /// <returns></returns>
        string GetCurrentTraceId();
    }
}