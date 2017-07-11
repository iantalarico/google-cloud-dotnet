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

using Google.Api.Gax;
using Google.Cloud.Trace.V1;
using Google.Protobuf.WellKnownTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using TraceProto = Google.Cloud.Trace.V1.Trace;

namespace Google.Cloud.Diagnostics.Common
{
    /// <summary>
    /// A simple implementation of the <see cref="IManagedTracer"/> that handles spans in a stack.
    /// </summary>
    internal sealed class SimpleManagedTracer : IManagedTracer
    {
        /// <summary>
        /// A class that represents a running trace span.
        /// </summary>
        private class Span : IDisposable
        {
            public bool Disposed { get; private set; }

            public TraceSpan TraceSpan { get; private set; }

            private readonly SimpleManagedTracer _tracer;
            
            public Span(SimpleManagedTracer tracer, TraceSpan traceSpan)
            {
                _tracer = GaxPreconditions.CheckNotNull(tracer, nameof(tracer));
                TraceSpan = GaxPreconditions.CheckNotNull(traceSpan, nameof(traceSpan));
            }

            /// <summary> Ends the current span.</summary>
            public void Dispose()
            {
                Disposed = true;
                TraceSpan.EndTime = Timestamp.FromDateTime(DateTime.UtcNow);
                _tracer.EndSpan(this);
            }
        }

        /// <summary>The trace consumer to push the trace to when completed.</summary>
        private readonly IConsumer<TraceProto> _consumer;

        /// <summary>The current trace.</summary>
        private TraceProto _trace;

        /// <summary>The id of the current trace.</summary>
        private readonly string _traceId;

        /// <summary>The Google Cloud Platform project ID.</summary>
        private readonly string _projectId;

        private readonly object _traceLock = new object();

        /// <summary>The number of spans currently open on any thread.</summary>
        private int _openSpanCount;

        /// <summary>The span id factory to generate new span ids.</summary>
        private readonly SpanIdFactory _spanIdFactory;

        /// <summary>The span id of the parent span of the root span of this trace.</summary>
        private readonly ulong? _rootSpanParentId;

        private SimpleManagedTracer(IConsumer<TraceProto> consumer, string projectId, string traceId, ulong? rootSpanParentId = null)
        {
            _consumer = GaxPreconditions.CheckNotNull(consumer, nameof(consumer));
            _traceId = GaxPreconditions.CheckNotNull(traceId, nameof(traceId));
            _projectId = GaxPreconditions.CheckNotNull(projectId, nameof(projectId));
            _trace = CreateTraceProto();
            _spanIdFactory = SpanIdFactory.Create();
            _rootSpanParentId = rootSpanParentId;
        }

        /// <summary>
        /// Creates a <see cref="SimpleManagedTracer"/>>
        /// </summary>
        /// <param name="consumer">The consumer to push finished traces to. Cannot be null.</param>
        /// <param name="projectId">The Google Cloud Platform project ID. Cannot be null.</param>
        /// <param name="traceId">The id of the current trace. Cannot be null.</param>
        /// <param name="rootSpanParentId">Optional, the parent span id of the root span of the passed in trace.</param>
        public static SimpleManagedTracer Create(IConsumer<TraceProto> consumer, string projectId,
            string traceId, ulong? rootSpanParentId = null)
            => new SimpleManagedTracer(consumer, projectId, traceId, rootSpanParentId);

        /// <inheritdoc />
        public IDisposable StartSpan(string name, StartSpanOptions options = null)
        {
            GaxPreconditions.CheckNotNull(name, nameof(name));
            options = options ?? StartSpanOptions.Create();

            var currentStack = TraceStack;

            var parentSpanId = currentStack.IsEmpty ? _rootSpanParentId : currentStack.Peek().TraceSpan.SpanId;//GetCurrentSpanId(currentStack).GetValueOrDefault();
            //var parentSpanId = GetCurrentSpanId(currentStack).GetValueOrDefault();

            Span spanOut = null;
            while (!currentStack.IsEmpty && currentStack.Peek().Disposed)
            {
                currentStack = currentStack.Pop(out spanOut);
            }
            //var parentSpanId = GetCurrentSpanId(currentStack).GetValueOrDefault();


            var traceSpan = new TraceSpan
            {
                SpanId = _spanIdFactory.NextId(),
                Kind = options.SpanKind.Convert(),
                Name = name,
                StartTime = Timestamp.FromDateTime(DateTime.UtcNow),
                ParentSpanId = parentSpanId ?? 0
            };
            AnnotateSpan(traceSpan, options.Labels);

            var span = new Span(this, traceSpan);
            TraceStack = currentStack.Push(span);

            Interlocked.Increment(ref _openSpanCount);
            return span;
        }

        /// <inheritdoc />
        public void RunInSpan(Action action, string name, StartSpanOptions options = null)
        {
            using (StartSpan(name, options))
            {
                try
                {
                    action();
                }
                catch (Exception e) when (SetStackTraceAndReturnFalse(e))
                {
                }
            }
        }

        /// <inheritdoc />
        public T RunInSpan<T>(Func<T> func, string name, StartSpanOptions options = null)
        {
            using (StartSpan(name, options))
            {
                try
                {
                    return func();
                }
                catch (Exception e) when (SetStackTraceAndReturnFalse(e))
                {
                    // This will never return as the condition above will always be false.
                    return default(T);
                }
            }
        }

        /// <inheritdoc />
        public async Task<T> RunInSpanAsync<T>(Func<Task<T>> func, string name, StartSpanOptions options = null)
        {
            using (StartSpan(name, options))
            {
                try
                {
                    return await func().ConfigureAwait(false);
                }
                catch (Exception e) when (SetStackTraceAndReturnFalse(e))
                {
                    // This will never return as the condition above will always be false.
                    return default(T);
                }
            }
        }

        /// <inheritdoc />
        public void EndSpan()
        {
            EndSpan(null);
        }

        private void EndSpan(Span span2)
        {
            var currentStack = TraceStack;
            CheckStackNotEmpty(currentStack);

            //TraceSpan span;
            //currentStack = currentStack.Pop(out span);
            //TraceStack = currentStack;
            //span.EndTime = Timestamp.FromDateTime(DateTime.UtcNow);


            Span spanOut = null;
            while (!currentStack.IsEmpty && currentStack.Peek().Disposed)
            {
                currentStack = currentStack.Pop(out spanOut);
            }
            if (spanOut == null)
            {
                spanOut = currentStack.Peek();
                if (spanOut.TraceSpan.SpanId == span2.TraceSpan.SpanId)
                {
                    TraceStack = currentStack.Pop(out spanOut);
                }
                else
                {
                    TraceStack = currentStack;
                }
            }
            else
            {
                TraceStack = currentStack;
            }
            //spanOut = spanOut ?? currentStack.Peek();

            lock (_traceLock)
            {
                _trace.Spans.Add(spanOut.TraceSpan);

                var newOpenSpanCount = Interlocked.Decrement(ref _openSpanCount);
                Debug.Assert(newOpenSpanCount >= 0, "Invalid open span count");
                if (newOpenSpanCount <= 0)
                {
                    Flush();
                }
            }
        }

        /// <inheritdoc />
        public void AnnotateSpan(Dictionary<string, string> labels)
        {
            GaxPreconditions.CheckNotNull(labels, nameof(labels));

            var currentStack = TraceStack;
            CheckStackNotEmpty(currentStack);

            AnnotateSpan(currentStack.Peek().TraceSpan, labels);
        }

        /// <summary>
        /// Annotates the specified span with the given labels. 
        /// </summary>
        private void AnnotateSpan(TraceSpan span, Dictionary<string, string> labels)
        {
            foreach (var l in labels)
            {
                span.Labels.Add(l.Key, l.Value);
            }
        }

        /// <inheritdoc />
        public void SetStackTrace(StackTrace stackTrace)
        {
            GaxPreconditions.CheckNotNull(stackTrace, nameof(stackTrace));
            var currentStack = TraceStack;
            CheckStackNotEmpty(currentStack);

            AnnotateSpan(currentStack.Peek().TraceSpan, TraceLabels.FromStackTrace(stackTrace));
        }

        /// <inheritdoc />
        public string GetCurrentTraceId()
        {
            return _trace.TraceId;
        }

        /// <inheritdoc />
        public ulong? GetCurrentSpanId() => GetCurrentSpanId(TraceStack);

        /// <summary>
        /// Gets the current span id of the specified stack or null if none exists.
        /// </summary>
        private ulong? GetCurrentSpanId(ImmutableStack<Span> traceStack)
        {
            if (traceStack.IsEmpty)
            {
                return _rootSpanParentId;
            }

            Span spanOut = null;
            while (!traceStack.IsEmpty && traceStack.Peek().Disposed)
            {
                traceStack = traceStack.Pop(out spanOut);
            }

            if (!traceStack.IsEmpty)
            {
                return traceStack.Peek().TraceSpan.SpanId;
            }
            return  _rootSpanParentId;
        }


        /// <summary>
        /// Sets a <see cref="StackTrace"/> on the current span for the given exception and
        /// returns false.  This is used for exception handling to ensure no data is lost
        /// in the stacktrace.
        /// </summary>
        private bool SetStackTraceAndReturnFalse(Exception e)
        {
            SetStackTrace(new StackTrace(e, true));
            return false;
        }

        private void CheckStackNotEmpty(ImmutableStack<Span> traceStack)
        {
            GaxPreconditions.CheckState(!traceStack.IsEmpty, "No available span.");
        }

        private void Flush()
        {
            var old = _trace;
            _trace = CreateTraceProto();
            _consumer.Receive(new[] { old });
        }

        /// <summary>
        /// Creates a new <see cref="TraceProto"/> with the project id and trace id set.
        /// </summary>
        private TraceProto CreateTraceProto() =>
             new TraceProto { TraceId = _traceId, ProjectId = _projectId };

        /// <summary>
        /// The stack of trace spans for the current logical call context. Note that this is logically cloned when each async
        /// block is entered and each thread is spawned, so it will contain the spans which were open previously and those spans
        /// will never be removed in the context of that async block/thread. Do not rely on the stack contents to know the state
        /// of things on other threads. It may contain previously closed spans.
        /// </summary>
#if NET45
        private readonly string _callContextName = Guid.NewGuid().ToString("N");
        private ImmutableStack<Span> TraceStack
        {
            get
            {
                var ret = System.Runtime.Remoting.Messaging.CallContext.LogicalGetData(_callContextName) as ImmutableStack<Span>;
                return ret ?? ImmutableStack<Span>.Empty;
            }
            set
            {
                System.Runtime.Remoting.Messaging.CallContext.LogicalSetData(_callContextName, value);
            }
        }
#else
        private readonly AsyncLocal<ImmutableStack<Span>> _traceStack = new AsyncLocal<ImmutableStack<Span>>();
        private ImmutableStack<Span> TraceStack
        {
            get
            {
                return _traceStack.Value ?? ImmutableStack<Span>.Empty;
            }
            set
            {
                _traceStack.Value = value;
            }
        }
#endif

        private sealed class ImmutableStack<T>
        {
            public static readonly ImmutableStack<T> Empty = new ImmutableStack<T>(default(T), null);

            private readonly ImmutableStack<T> _previous;
            private readonly T _value;

            private ImmutableStack(T value, ImmutableStack<T> previous)
            {
                _value = value;
                _previous = previous;
            }

            public bool IsEmpty => this == Empty;

            public T Peek()
            {
                GaxPreconditions.CheckState(!IsEmpty, "The stack is empty");
                return _value;
            }

            public ImmutableStack<T> Pop(out T value)
            {
                GaxPreconditions.CheckState(!IsEmpty, "The stack is empty");
                value = _value;
                return _previous;
            }

            public ImmutableStack<T> Push(T value) => new ImmutableStack<T>(value, this);
         }
    }
}
