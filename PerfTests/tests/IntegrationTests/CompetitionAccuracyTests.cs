﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;

using BenchmarkDotNet.Attributes;

using CodeJam.PerfTests.Configs;
using CodeJam.PerfTests.Running.Messages;

using JetBrains.Annotations;

using NUnit.Framework;

using static CodeJam.PerfTests.SelfTestHelpers;

namespace CodeJam.PerfTests.IntegrationTests
{
	[TestFixture(Category = "BenchmarkDotNet")]
	[SuppressMessage("ReSharper", "HeapView.BoxingAllocation")]
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	[SuppressMessage("ReSharper", "ArgumentsStyleLiteral")]
	public static class CompetitionAccuracyTests
	{
		[Test]
		public static void CompetitionTooFastBenchmark()
		{
			var runState = SelfTestCompetition.Run<TooFastBenchmark>();
			var messages = runState.GetMessages();
			var summary = runState.LastRunSummary;
			Assert.AreEqual(summary?.ValidationErrors.Length, 0);
			Assert.AreEqual(runState.RunNumber, 1);
			Assert.AreEqual(runState.RunsLeft, 0);
			Assert.AreEqual(runState.RunLimitExceeded, false);
			Assert.AreEqual(runState.LooksLikeLastRun, true);
			Assert.AreEqual(messages.Length, 1);

			Assert.AreEqual(messages[0].RunNumber, 1);
			Assert.AreEqual(messages[0].RunMessageNumber, 1);
			Assert.AreEqual(messages[0].MessageSeverity, MessageSeverity.Warning);
			Assert.AreEqual(messages[0].MessageSource, MessageSource.Analyser);
			Assert.AreEqual(
				messages[0].MessageText,
				"The benchmark(s) TooFast, TooFastX5 run faster than 0.0015 ms. Results cannot be trusted.");
		}

		[Test]
		public static void CompetitionTooSlowBenchmark()
		{
			var runState = SelfTestCompetition.Run<TooSlowBenchmark>();
			var messages = runState.GetMessages();
			var summary = runState.LastRunSummary;
			Assert.AreEqual(summary?.ValidationErrors.Length, 0);
			Assert.AreEqual(runState.RunNumber, 1);
			Assert.AreEqual(runState.RunsLeft, 0);
			Assert.AreEqual(runState.RunLimitExceeded, false);
			Assert.AreEqual(runState.LooksLikeLastRun, true);
			Assert.AreEqual(messages.Length, 1);

			Assert.AreEqual(messages[0].RunNumber, 1);
			Assert.AreEqual(messages[0].RunMessageNumber, 1);
			Assert.AreEqual(messages[0].MessageSeverity, MessageSeverity.Warning);
			Assert.AreEqual(messages[0].MessageSource, MessageSource.Analyser);
			Assert.AreEqual(
				messages[0].MessageText,
				"The benchmark(s) TooSlow run longer than 0.5 sec." +
					" Consider to rewrite the test as the peek timings will be hidden by averages" +
					" or enable long running benchmarks support in the config.");
		}

		[Test]
		public static void CompetitionTooSlowOk()
		{
			var overrideConfig = CompetitionHelpers
				.CreateConfig(typeof(TooSlowBenchmark))
				.WithLongRunningBenchmarkLimit(TimeSpan.FromMinutes(2));

			var runState = SelfTestCompetition.Run<TooSlowBenchmark>(overrideConfig);
			var messages = runState.GetMessages();
			var summary = runState.LastRunSummary;
			Assert.AreEqual(summary?.ValidationErrors.Length, 0);
			Assert.AreEqual(runState.RunNumber, 1);
			Assert.AreEqual(runState.RunsLeft, 0);
			Assert.AreEqual(runState.RunLimitExceeded, false);
			Assert.AreEqual(runState.LooksLikeLastRun, true);
			Assert.AreEqual(messages.Length, 0);
		}

		[Test]
		public static void CompetitionHighAccuracyBenchmark()
		{
			IgnoreIfDebug();

			var runState = SelfTestCompetition.Run<HighAccuracyBenchmark>();
			var messages = runState.GetMessages();
			if (messages.All(m => m.MessageText != "CompetitionAnalyser: All competition limits are ok."))
			{
				Assert.Ignore("The environment does not provide accurate timings. Test results cannot be trusted.");
			}
		}

		#region Benchmark classes
		[PublicAPI]
		[CompetitionModifier(typeof(CompetitionHighAccuracyModifier))]
		public class TooFastBenchmark
		{
			[Benchmark]
			public int TooFast()
			{
				var a = 0;
				for (var i = 0; i < 10; i++)
				{
					a = a + i;
				}
				return a;
			}

			[Benchmark]
			public int TooFastX5()
			{
				var a = 0;
				for (var i = 0; i < 50; i++)
				{
					a = a + i;
				}
				return a;
			}
		}

		public class TooSlowBenchmark
		{
			[CompetitionBaseline]
			public void TooSlow() => Thread.Sleep(550);
		}

		[CompetitionModifier(typeof(CompetitionHighAccuracyModifier))]
		public class HighAccuracyBenchmark
		{
			private const int Count = 4 * CompetitionHelpers.DefaultCount;

			[CompetitionBaseline]
			public void Baseline() => CompetitionHelpers.Delay(Count);

			[CompetitionBenchmark(1.85, 2.15)]
			public void SlowerX2Run1() => CompetitionHelpers.Delay(2 * Count);

			[CompetitionBenchmark(1.85, 2.15)]
			public void SlowerX2Run2() => CompetitionHelpers.Delay(2 * Count);

			[CompetitionBenchmark(1.85, 2.15)]
			public void SlowerX2Run3() => CompetitionHelpers.Delay(2 * Count);

			[CompetitionBenchmark(4.65, 5.35)]
			public void SlowerX5() => CompetitionHelpers.Delay(5 * Count);
		}

		public class HighAccuracyBenchmarkOutOfProcess
		{
			private const int Count = CompetitionHelpers.DefaultCount;

			[CompetitionBaseline]
			public void Baseline() => CompetitionHelpers.Delay(Count);

			[CompetitionBenchmark(1.85, 2.15)]
			public void SlowerX2Run1() => CompetitionHelpers.Delay(2 * Count);

			[CompetitionBenchmark(1.85, 2.15)]
			public void SlowerX2Run2() => CompetitionHelpers.Delay(2 * Count);

			[CompetitionBenchmark(1.85, 2.15)]
			public void SlowerX2Run3() => CompetitionHelpers.Delay(2 * Count);

			[CompetitionBenchmark(4.65, 5.35)]
			public void SlowerX5() => CompetitionHelpers.Delay(5 * Count);
		}
		#endregion
	}
}