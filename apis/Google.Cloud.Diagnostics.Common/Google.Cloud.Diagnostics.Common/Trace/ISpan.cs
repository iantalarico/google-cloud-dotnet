﻿// Copyright 2017 Google Inc. All Rights Reserved.
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

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Google.Cloud.Diagnostics.Common
{
    /// <summary>
    /// A trace span.
    /// <remark>
    /// The functions here, aside from <see cref="IDisposable.Dispose"/>, do not need to be used in most cases. 
    /// They need to be used when updating the current span or starting a new span where you would like the 
    /// current span to be the parent in a disjoint thread. For example:
    /// <code>
    /// public void DoSomething(IManagedTracer tracer)
    /// {
    ///     ISpan span1 = null;
    ///     Thread t = new Thread(() => 
    ///     {
    ///         span1 = tracer.StartSpan(nameof(DoSomething))
    ///     });
    ///     Thread t2 = new Thread(() =>
    ///     {
    ///         var tracer2 = span.CreateManagedTracer();
    ///         // This span ('span2'_ will be a child of 'span1'.
    ///         using (tracer2.StartSpan("thread"))
    ///         {
    ///             ...
    ///         }
    ///         span1.AnnotateSpan(new Dictionary&lt;string, string&gt;  { { "new", "label"} });
    ///         span1.Dispose();
    ///     });
    ///     
    ///     t.Start();
    ///     Thread.Sleep(TimeSpan.FromSeconds(1));
    ///     t2.Start();
    /// }
    /// </code>
    /// </remark>
    /// </summary>
    public interface ISpan : IDisposable
    {
        /// <summary>
        /// True if the span has been disposed and ended.
        /// </summary>
        bool Disposed();

        /// <summary>
        /// Annotates the current span with the given labels. 
        /// </summary>
        void AnnotateSpan(Dictionary<string, string> labels);

        /// <summary>
        /// Adds the given StackTrace to the current span.
        /// </summary>
        void SetStackTrace(StackTrace stackTrace);

        /// <summary>
        /// Gets span's id.
        /// </summary>
        ulong SpanId();

        /// <summary>
        /// Creates an <see cref="IManagedTracer"/> where the parent of all root spans is this <see cref="ISpan"/>.
        /// </summary>
        IManagedTracer CreateManagedTracer();
    }
}
