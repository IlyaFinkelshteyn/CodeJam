﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using BenchmarkDotNet.Helpers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

using CodeJam.Collections;
using CodeJam.PerfTests.Analysers;
using CodeJam.PerfTests.Running.Core;
using CodeJam.PerfTests.Running.Messages;
using CodeJam.Strings;

using JetBrains.Annotations;

namespace CodeJam.PerfTests.Running.SourceAnnotations
{
	/// <summary>
	/// XML annotations storage
	/// </summary>
	/// <seealso cref="CodeJam.PerfTests.Running.SourceAnnotations.IAnnotationStorage" />
	internal class XmlAnnotationStorage : AnnotationStorageBase
	{
		#region Static members, parse from log
		/// <summary>Writes xml annotation document for the competition targets to the log.</summary>
		/// <param name="competitionTargets">The competition targets to log.</param>
		/// <param name="messageLogger">The message logger.</param>
		public static void LogXmlAnnotationDoc(
			[NotNull] IReadOnlyCollection<CompetitionTarget> competitionTargets,
			[NotNull] IMessageLogger messageLogger) =>
				XmlAnnotationHelpers.LogXmlAnnotationDoc(competitionTargets, messageLogger);

		/// <summary>Reads the XML annotation docs from the log.</summary>
		/// <param name="logUri">The log URI.</param>
		/// <param name="messageLogger">The message logger.</param>
		/// <returns>XML annotation docs from the log.</returns>
		[NotNull]
		public static XDocument[] ReadXmlAnnotationDocsFromLog(string logUri, IMessageLogger messageLogger)
		{
			messageLogger.Logger.WriteVerbose($"Reading XML annotation documents from log '{logUri}'.");

			var xmlAnnotationDocs = XmlAnnotationHelpers.TryParseXmlAnnotationDocsFromLog(logUri, messageLogger);

			return xmlAnnotationDocs ?? Array<XDocument>.Empty;
		}

		/// <summary>Retrieves stored info for competition targets from XML annotation docs.</summary>
		/// <param name="competitionTargets">Competition targets the metrics are retrieved for.</param>
		/// <param name="xmlAnnotationDocs">The XML annotation docs.</param>
		/// <param name="analysis">State of the analysis.</param>
		/// <returns>Stored info for competition targets.</returns>
		public static bool TryFillCompetitionTargetsFromLog(
			IReadOnlyCollection<CompetitionTarget> competitionTargets,
			XDocument[] xmlAnnotationDocs,
			Analysis analysis)
		{
			analysis.Logger.WriteVerbose($"Parsing XML annotations ({xmlAnnotationDocs.Length} doc(s)) from log.");

			var updated = false;
			var targetsToFill = competitionTargets.Select(t => t.Target).ToArray();
			var metrics = analysis.Config.GetMetrics().ToArray();

			// TODO: common api to write message for multiple target + metrics.

			foreach (var doc in xmlAnnotationDocs)
			{
				var storedTargets = XmlAnnotationHelpers.TryGetStoredTargets(
					targetsToFill, metrics, doc, true, analysis);

				foreach (var competitionTarget in competitionTargets)
				{
					var storedTarget = storedTargets.GetValueOrDefault(competitionTarget.Target);
					if (storedTarget == null)
						continue;

					var parsedCompetitionTarget = ParseCompetitionTarget(
						competitionTarget.Target,
						metrics, storedTarget, analysis);

					var parsedMetrics = parsedCompetitionTarget.MetricValues.ToDictionary(m => m.Metric);

					var hasAnnotations = false;
					foreach (var metricValue in competitionTarget.MetricValues)
					{
						if (!parsedMetrics.TryGetValue(metricValue.Metric, out var parsedMetricValue))
							continue;

						hasAnnotations = true;

						updated |= metricValue.UnionWith(parsedMetricValue, true);
					}

					if (!hasAnnotations && analysis.SafeToContinue && parsedMetrics.Any())
					{
						analysis.WriteWarningMessage(
							$"No logged XML annotation for {competitionTarget.Target.MethodDisplayInfo} found. Check if the method was renamed.");
					}
				}
			}

			return updated;
		}
		#endregion

		#region .ctor & properties
		/// <summary>Initializes a new instance of the <see cref="XmlAnnotationStorage"/> class.</summary>
		/// <param name="resourcePath">The relative path to the resource containing xml document with source annotations.</param>
		/// <param name="useFullTypeName">Use full type name in XML annotations.</param>
		/// <param name="resourceName">The name of the resource.</param>
		public XmlAnnotationStorage(
			[NotNull] string resourceName,
			[CanBeNull] string resourcePath,
			bool useFullTypeName)
		{
			Code.NotNullNorEmpty(resourceName, nameof(resourceName));
			ResourceName = resourceName;
			ResourcePath = resourcePath;
			UseFullTypeName = useFullTypeName;
		}

		/// <summary>Gets the name of the resource.</summary>
		/// <value>The name of the resource.</value>
		public string ResourceName { get; }

		/// <summary>
		/// Gets path to the resource containing xml document with source annotations.
		/// Should be relative to the source file the attribute is applied to.
		/// If not set then path to the resource should be same as path to the source file (resource's extension should be '.xml').
		/// </summary>
		/// <value>The relative path to the resource containing xml document with source annotations.</value>
		[CanBeNull]
		public string ResourcePath { get; }

		/// <summary>Use full type name to search for the XML annotation.</summary>
		/// <value><c>true</c> if full type name should be used in annotations; otherwise, <c>false</c>.</value>
		public bool UseFullTypeName { get; }
		#endregion

		private ResourceKey GetResourceKey(Type benchmarkType) =>
			new ResourceKey(benchmarkType.Assembly, ResourceName);

		#region Parse
		/// <summary>Retrieves stored info for competition targets.</summary>
		/// <param name="targets">Competition targets the metrics are retrieved for.</param>
		/// <param name="analysis">State of the analysis.</param>
		/// <returns>Stored info for competition targets.</returns>
		protected override IReadOnlyDictionary<Target, StoredTargetInfo> GetStoredTargets(
			Target[] targets, Analysis analysis)
		{
			var resourceKey = GetResourceKey(analysis.RunState.BenchmarkType);

			var xmlAnnotationDoc = XmlAnnotationHelpers.TryParseXmlAnnotationDoc(resourceKey, analysis);

			if (xmlAnnotationDoc == null)
				return new Dictionary<Target, StoredTargetInfo>();

			var metrics = analysis.Config.GetMetrics();
			var result = XmlAnnotationHelpers.TryGetStoredTargets(
				targets, metrics,
				xmlAnnotationDoc,
				UseFullTypeName,
				analysis);

			return result;
		}
		#endregion

		#region Save
		/// <summary>Saves stored metrics from competition targets.</summary>
		/// <param name="competitionTargets">Competition targets with metrics to save.</param>
		/// <param name="annotationContext">The annotation context.</param>
		/// <param name="analysis">State of the analysis.</param>
		/// <returns>
		/// <c>true</c>, if metrics were saved successfully.
		/// </returns>
		protected override CompetitionTarget[] TrySaveAnnotationsCore(
			IReadOnlyCollection<CompetitionTarget> competitionTargets, AnnotationContext annotationContext, ResultAnalysis analysis)
		{
			var benchmarkType = analysis.RunState.BenchmarkType;
			var targetKey = new AnnotationTargetKey(benchmarkType.TypeHandle);

			var annotationFile = annotationContext.TryGetDocument(targetKey);
			if (annotationFile == null)
			{
				var origin = TryGetAnnotationLocation(analysis.Summary, analysis);
				if (origin == null)
				{
					annotationFile = annotationContext.GetUnknownOriginDocument();
				}
				else
				{
					annotationFile = annotationContext.TryGetDocument(origin);
					if (annotationFile == null)
					{
						annotationFile = ParseAnnotationFile(benchmarkType, origin, analysis);
						annotationContext.AddDocument(annotationFile);
					}
				}
				annotationContext.AddTargetKey(annotationFile, targetKey);
			}

			if (!annotationFile.Parsed)
			{
				analysis.WriteSetupErrorMessage(
					$"Could not find XML annotation file {annotationFile.Origin} for the benchmark. Annotations were not saved.");
				return Array<CompetitionTarget>.Empty;
			}

			var result = new List<CompetitionTarget>();

			var xmlAnnotationFile = (XmlAnnotationFile)annotationFile;
			foreach (var targetToAnnotate in competitionTargets)
			{
				var target = targetToAnnotate.Target;

				var metrics = targetToAnnotate.MetricValues.Where(m => m.HasUnsavedChanges).ToArray();
				if (metrics.Length == 0)
					continue;

				result.Add(targetToAnnotate);

				foreach (var metricValue in metrics)
				{
					analysis.Logger.WriteVerbose(
						$"Method {target.MethodDisplayInfo}: updating metric {metricValue.Metric} {metricValue}.");
				}
			}

			analysis.Logger.WriteVerboseHint(
				$"Annotating resource file '{annotationFile.Origin}'.");

			XmlAnnotationHelpers.AddOrUpdateXmlAnnotation(
				// ReSharper disable once AssignNullToNotNullAttribute
				xmlAnnotationFile.XmlAnnotationDoc,
				result,
				analysis.RunState.BenchmarkType,
				UseFullTypeName);

			foreach (var targetToAnnotate in competitionTargets)
			{
				var target = targetToAnnotate.Target;
				var metrics = targetToAnnotate.MetricValues.Where(m => m.HasUnsavedChanges).ToArray();

				foreach (var metricValue in metrics)
				{
					analysis.Logger.WriteVerboseHint(
						$"Method {target.MethodDisplayInfo}: metric {metricValue.Metric} {metricValue} updated.");
				}
			}

			return result.ToArray();
		}

		[CanBeNull]
		private string TryGetAnnotationLocation(
			[NotNull] Summary summary,
			[NotNull] IMessageLogger messageLogger)
		{
			var benchmarkType = summary.Benchmarks[0].Target.Type;
			var sameTypeTarget = summary.GetExecutionOrderBenchmarks()
				.Select(b => b.Target)
				.FirstOrDefault(t => t.Method.DeclaringType == benchmarkType);

			if (sameTypeTarget == null)
			{
				messageLogger.WriteSetupErrorMessage(
					$"Cannot find source file location as {benchmarkType.Name} has no benchmark methods in it. ",
					$"Add a benchmark method to the type {benchmarkType.FullName}.");

				return null;
			}

			var sourcePath = SymbolHelper.TryGetSourcePath(sameTypeTarget, messageLogger);
			if (sourcePath == null)
				return null;

			return GetResourcePath(sourcePath, ResourcePath);
		}

		[NotNull]
		private static string GetResourcePath(
			[NotNull] string sourcePath,
			[CanBeNull] string resourcePath)
		{
			if (resourcePath.IsNullOrEmpty())
				return Path.ChangeExtension(sourcePath, ".xml");

			return Path.IsPathRooted(resourcePath)
				? resourcePath
				: Path.Combine(
					Path.GetDirectoryName(sourcePath),
					resourcePath);
		}

		[NotNull]
		private XmlAnnotationFile ParseAnnotationFile(
			[NotNull] Type benchmarkType,
			[NotNull] string resourceFileName,
			[NotNull] IMessageLogger messageLogger)
		{
			var resourceKey = GetResourceKey(benchmarkType);
			var resourceChecksum = AnnotationHelpers.TryGetChecksum(resourceKey, ChecksumAlgorithm.Sha1);
			var fileChecksum = AnnotationHelpers.TryGetChecksum(resourceFileName, ChecksumAlgorithm.Sha1);

			if (!resourceChecksum.EqualsTo(fileChecksum))
			{
				var expected = resourceChecksum.ToHexString();
				var actual = fileChecksum.ToHexString();
				messageLogger.WriteSetupErrorMessage(
					$"{ChecksumAlgorithm.Sha1} checksum validation failed. File '{resourceFileName}'." +
						$"{Environment.NewLine}\tActual: 0x{actual}" +
						$"{Environment.NewLine}\tExpected: 0x{expected}");

				return new XmlAnnotationFile(resourceFileName, null);
			}

			var xmlAnnotationDoc = XmlAnnotationHelpers.TryParseXmlAnnotationDoc(resourceFileName, messageLogger);
			return new XmlAnnotationFile(resourceFileName, xmlAnnotationDoc);
		}
		#endregion
	}
}