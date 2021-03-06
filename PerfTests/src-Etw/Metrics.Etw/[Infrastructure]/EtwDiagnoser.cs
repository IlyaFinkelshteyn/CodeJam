﻿using System;

using BenchmarkDotNet.Running;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Validators;

using CodeJam.Collections;
using CodeJam.Strings;
using CodeJam.PerfTests.Running.Core;
using CodeJam.Ranges;

using JetBrains.Annotations;

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace CodeJam.PerfTests.Metrics.Etw
{
	/// <summary>
	/// Infrastructure diagnoser for <see cref="IEtwMetricValueProvider"/> providers.
	/// </summary>
	/// <seealso cref="IDiagnoser" />
	/// <seealso cref="IEtwMetricValueProvider" />
	public class EtwDiagnoser : IDiagnoser
	{
		#region Helper types & static members
		private class DiagnoserState
		{
			public EtwDiagnoserAnalysis Analysis { get; set; }
		}

		private static readonly RunState<DiagnoserState> _diagnoserState =
			new RunState<DiagnoserState>(clearBeforeEachRun: true);

		/// <summary>The instance ow the ETW diagnoser.</summary>
		public static readonly EtwDiagnoser Instance = new EtwDiagnoser();
		#endregion

		#region Fields & .ctor()
		// TODO: remove the field after the Stop() method will have access to the _diagnoserState.
		[CanBeNull]
		private EtwDiagnoserAnalysis _analysis;

		/// <summary>Prevents a default instance of the <see cref="EtwDiagnoser"/> class from being created.</summary>
		private EtwDiagnoser() { }
		#endregion

		#region State manipulation
		private EtwDiagnoserAnalysis CreateAnalysis(Benchmark benchmark, IConfig config, IEtwMetricValueProvider[] metricProviders)
		{
			var diagnoserState = _diagnoserState[config];
			Code.BugIf(diagnoserState.Analysis != null, "runState.Analysis != null");
			Code.BugIf(_analysis != null, "_analysis != null");

			var analysis = new EtwDiagnoserAnalysis(benchmark, config, metricProviders);
			if (analysis.Config.KeepBenchmarkFiles)
			{
				analysis.TraceFile.SuppressDelete();
			}
			diagnoserState.Analysis = analysis;
			_analysis = analysis;
			return analysis;
		}

		private void CompleteTraceSession(EtwDiagnoserAnalysis analysis)
		{
			var traceSession = analysis.TraceSession;
			if (traceSession != null)
			{
				traceSession.Flush();
				traceSession.Dispose();
			}
		}

		private void DisposeAnalysis(EtwDiagnoserAnalysis analysis)
		{
			var config = analysis.Config;

			analysis.TraceSession?.Dispose();
			analysis.TraceFile.Dispose();

			var runState = _diagnoserState[config];

			Code.BugIf(runState.Analysis != analysis, "runState.Analysis != analysis");
			runState.Analysis = null;

			Code.BugIf(_analysis != analysis, "_analysis != analysis");
			_analysis = null;
		}
		#endregion

		#region Core logic
		private void StartTraceSession(DiagnoserActionParameters parameters)
		{
			var metricProviders = CompetitionCore.RunState[parameters.Config].Config
				.GetMetrics()
				.Select(m => m.ValuesProvider)
				.OfType<IEtwMetricValueProvider>()
				.Distinct()
				.ToArray();

			if (metricProviders.Length == 0)
			{
				_analysis = null;
				return;
			}

			var analysis = CreateAnalysis(parameters.Benchmark, parameters.Config, metricProviders);

			EtwHelpers.WorkaroundEnsureNativeDlls();

			bool allOk = false;
			try
			{
				BuildTraceSession(analysis);
				allOk = true;
			}
			finally
			{
				if (!allOk)
				{
					CompleteTraceSession(analysis);
					DisposeAnalysis(analysis);
				}
			}
		}

		private static void BuildTraceSession(EtwDiagnoserAnalysis analysis)
		{
			var traceSession = new TraceEventSession(analysis.RunGuid.ToString(), analysis.TraceFile.Path)
			{
				StopOnDispose = true
			};
			analysis.TraceSession = traceSession;

			var metricsByProvider = analysis.MetricProviders.ToLookup(p => p.ProviderGuid);

			// Kernel etw provider must be the first one.
			if (metricsByProvider.Contains(KernelTraceEventParser.ProviderGuid))
			{
				var metricValueProviders = metricsByProvider[KernelTraceEventParser.ProviderGuid];
				var flags = metricValueProviders
					.Aggregate(0UL, (value, p) => value | p.ProviderKeywords);

				try
				{
					traceSession.EnableKernelProvider((KernelTraceEventParser.Keywords)flags);
				}
				catch (UnauthorizedAccessException)
				{
					var kernelMetrics =analysis.Config
						.GetMetrics()
						.Where(m => m.ValuesProvider is IEtwMetricValueProvider p && p.IsKernelMetric);

					analysis.WriteSetupErrorMessage(
						analysis.Benchmark.Target,
						$"The config contains kernel metric(s) {kernelMetrics.Select(m => m.DisplayName).Join(", ")} and therefore requires elevated run.",
						"Run the competition with elevated permissions (as administrator).");

					throw;
				}
			}

			traceSession.EnableProvider(DiagnoserEventSource.SourceGuid);

			// Handle all other providers
			foreach (var metricsGroup in metricsByProvider)
			{
				// Already registered
				if (metricsGroup.Key == KernelTraceEventParser.ProviderGuid)
					continue;
				if (metricsGroup.Key == DiagnoserEventSource.SourceGuid)
					continue;

				var eventLevel = TraceEventLevel.Always;
				var keywords = 0UL;
				foreach (var metricValueProvider in metricsGroup)
				{
					if (eventLevel < metricValueProvider.EventLevel)
						eventLevel = metricValueProvider.EventLevel;
					keywords |= metricValueProvider.ProviderKeywords;
				}
				traceSession.EnableProvider(metricsGroup.Key, eventLevel, keywords);
			}
		}

		private static void ProcessCapturedEvents(EtwDiagnoserAnalysis analysis)
		{
			var events = CollectDiagnoserEvents(analysis);
			var allProcesses = events.Select(e => e.ProcessId).ToHashSet();

			var timeRange = events.ToCompositeRange(
					e => e.Started ?? TimeSpan.MinValue,
					e => e.Stopped ?? TimeSpan.MaxValue);

			// ReSharper disable once ConvertToLocalFunction
			Func<TraceEvent, bool> timeAndProcessFilter = e =>
			{
				if (!allProcesses.Contains(e.ProcessID))
					return false;

				var t = TimeSpan.FromMilliseconds(e.TimeStampRelativeMSec);
				return timeRange.Intersect(t, t).SubRanges.Any(r => r.Key.ProcessId == e.ProcessID);
			};

			using (var eventSource = new ETWTraceEventSource(analysis.TraceFile.Path))
			{
				if (eventSource.EventsLost > 0)
				{
					analysis.WriteWarningMessage(
						analysis.Benchmark.Target,
						$"The analysis session contains {eventSource.EventsLost} lost event(s). Metric results may be inaccurate.",
						"Consider to collect less events or to place the benchmark working directory on drive with least load.");
				}

				var allHandlers = new List<IDisposable>();
				foreach (var metricProvider in analysis.MetricProviders)
				{
					// Already processed
					if (metricProvider.ProviderGuid == DiagnoserEventSource.SourceGuid)
						continue;

					var handler = metricProvider.Subscribe(eventSource, analysis.Benchmark, analysis.Config, timeAndProcessFilter);
					allHandlers.Add(handler);
				}

				eventSource.Process();
				allHandlers.DisposeAll();
			}
		}

		private static DiagnoserTraceScopeEvent[] CollectDiagnoserEvents(EtwDiagnoserAnalysis analysis)
		{
			// ReSharper disable once ConvertToLocalFunction
			Func<TraceEvent, bool> currentRunFilter = e => (Guid)e.PayloadByName(DiagnoserEventSource.RunIdPayload) == analysis.RunGuid;

			var provider = new DiagnoserTimesProvider();
			using (var eventSource = new ETWTraceEventSource(analysis.TraceFile.Path))
			using (provider.Subscribe(eventSource, analysis.Benchmark, analysis.Config, currentRunFilter))
			{
				eventSource.Process();
			}
			return provider.GetEvents(analysis.Benchmark, analysis.Config).ToArray();
		}
		#endregion

		#region Implementation of IDiagnoser
		/// <summary>Gets the column provider.</summary>
		/// <returns>The column provider.</returns>
		public IColumnProvider GetColumnProvider() => new CompositeColumnProvider();

		/// <summary>Called before jitting, warmup</summary>
		/// <param name="parameters">The diagnoser action parameters</param>
		public void BeforeAnythingElse(DiagnoserActionParameters parameters)
		{
			// Nothing to do here
		}

		/// <summary>Called after globalSetup, before run</summary>
		/// <param name="parameters">The diagnoser action parameters</param>
		public void AfterGlobalSetup(DiagnoserActionParameters parameters)
		{
			StartTraceSession(parameters);
		}

		/// <summary>Called after globalSetup, warmup and pilot but before the main run</summary>
		/// <param name="parameters">The diagnoser action parameters</param>
		public void BeforeMainRun(DiagnoserActionParameters parameters)
		{
			var analysis = _diagnoserState[parameters.Config].Analysis;
			if (analysis == null) return;

			analysis.IterationGuid = Guid.NewGuid();
			// Ensure delay before analysis start
			Thread.Sleep(100);
			DiagnoserEventSource.Instance.TraceStarted(analysis.RunGuid, analysis.IterationGuid);
		}

		/// <summary>Called after run, before global cleanup</summary>
		public void BeforeGlobalCleanup()
		{
			var analysis = _analysis;
			if (analysis == null) return;

			DiagnoserEventSource.Instance.TraceStopped(analysis.RunGuid, analysis.IterationGuid);
			// Ensure delay after analysis stop
			Thread.Sleep(100);
			CompleteTraceSession(analysis);
		}

		/// <summary>Processes the results.</summary>
		/// <param name="benchmark">The benchmark.</param>
		/// <param name="report">The report.</param>
		public void ProcessResults(Benchmark benchmark, BenchmarkReport report)
		{
			var analysis = _analysis;
			if (analysis == null) return;

			try
			{
				ProcessCapturedEvents(analysis);
			}
			finally
			{
				DisposeAnalysis(analysis);
			}
		}

		/// <summary>Displays the results.</summary>
		/// <param name="logger">The logger.</param>
		public void DisplayResults(ILogger logger)
		{
			// Nothing to do here.
		}

		/// <summary>Validates the specified validation parameters.</summary>
		/// <param name="validationParameters">The validation parameters.</param>
		/// <returns></returns>
		public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters) => Array.Empty<ValidationError>();

		/// <summary>Gets the diagnoser ids.</summary>
		/// <value>The diagnoser ids.</value>
		public IEnumerable<string> Ids => new[] { nameof(EtwDiagnoser) };
		#endregion
	}
}