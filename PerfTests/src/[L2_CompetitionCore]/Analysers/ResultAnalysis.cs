﻿using System;

using System.Collections.Generic;

using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

using CodeJam.PerfTests.Running.Messages;

using JetBrains.Annotations;

namespace CodeJam.PerfTests.Analysers
{
	/// <summary>Helper class to store competition analysis results.</summary>
	[PublicAPI]
	public class ResultAnalysis : Analysis
	{
		/// <summary>Initializes a new instance of the <see cref="Analysis"/> class.</summary>
		/// <param name="id">The identifier.</param>
		/// <param name="summary">The summary.</param>
		public ResultAnalysis([NotNull] string id, [NotNull] Summary summary)
			: this(id, summary, MessageSource.Analyser) { }

		/// <summary>Initializes a new instance of the <see cref="Analysis"/> class.</summary>
		/// <param name="id">The identifier.</param>
		/// <param name="summary">The summary.</param>
		/// <param name="messageSource">Source for the messages.</param>
		public ResultAnalysis([NotNull] string id, [NotNull] Summary summary, MessageSource messageSource) :
			base(summary.Config, messageSource)
		{
			Code.NotNullNorEmpty(id, nameof(id));
			Id = id;

			Summary = summary;
		}

		#region Properties
		/// <summary>Gets the analysis identifier.</summary>
		/// <value>The analysis identifier.</value>
		[NotNull]
		public string Id { get; }

		/// <summary>The summary.</summary>
		/// <value>The summary.</value>
		[NotNull]
		public Summary Summary { get; }

		/// <summary>Analysis conclusions.</summary>
		/// <value>Analysis conclusions.</value>
		[NotNull]
		public IReadOnlyCollection<Conclusion> Conclusions => ConclusionsList;

		/// <summary>The conclusions list.</summary>
		/// <value>The conclusions list.</value>
		[NotNull]
		protected List<Conclusion> ConclusionsList { get; } = new List<Conclusion>();
		#endregion

		#region Warnings
		/// <summary>Reports test error conclusion.</summary>
		/// <param name="message">Message text.</param>
		/// <param name="report">The report the message belongs to.</param>
		public override void AddTestErrorConclusion(
			string message,
			BenchmarkReport report = null)
		{
			base.AddTestErrorConclusion(message, report);
			ConclusionsList.Add(Conclusion.CreateWarning(Id, message, report));
		}

		/// <summary>Reports test error conclusion.</summary>
		/// <param name="target">Target the message applies for.</param>
		/// <param name="message">Message text.</param>
		/// <param name="report">The report the message belongs to.</param>
		public override void AddTestErrorConclusion(
			Target target,
			string message,
			BenchmarkReport report = null)
		{
			base.AddTestErrorConclusion(target, message, report);
			ConclusionsList.Add(
				Conclusion.CreateWarning(
					Id, $"Target {target.MethodDisplayInfo}. {message}",
					report));
		}

		/// <summary>Reports analyser warning conclusion.</summary>
		/// <param name="message">Message text.</param>
		/// <param name="hint">Hint how to fix the warning.</param>
		/// <param name="report">The report the message belongs to.</param>
		public override void AddWarningConclusion(
			string message,
			string hint,
			BenchmarkReport report = null)
		{
			base.AddWarningConclusion(message, hint, report);
			ConclusionsList.Add(Conclusion.CreateWarning(Id, message, report));
		}

		/// <summary>Reports analyser warning conclusion.</summary>
		/// <param name="target">Target the message applies for.</param>
		/// <param name="message">Message text.</param>
		/// <param name="hint">Hint how to fix the warning.</param>
		/// <param name="report">The report the message belongs to.</param>
		public override void AddWarningConclusion(
			Target target,
			string message,
			string hint,
			BenchmarkReport report = null)
		{
			base.AddWarningConclusion(target, message, hint, report);
			ConclusionsList.Add(
				Conclusion.CreateWarning(
					Id, $"Target {target.MethodDisplayInfo}. {message}",
					report));
		}
		#endregion
	}
}