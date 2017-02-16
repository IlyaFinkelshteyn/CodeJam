﻿using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

using BenchmarkDotNet.Attributes;

using CodeJam.Arithmetic;
using CodeJam.PerfTests;
using CodeJam.PerfTests.Configs;

using NUnit.Framework;

namespace CodeJam.DesignDecisions
{
	/// <summary>
	/// Proofs that Expression.Compile() is the fastest way to emit less than comparison
	/// (at least for Int32)
	/// IMPORTANT: DO NOT trust the perftest if your code do something except simple arithmetic.
	/// See http://stackoverflow.com/questions/4211418/why-is-func-created-from-expressionfunc-slower-than-func-declared-direct
	/// See http://stackoverflow.com/questions/5053032/performance-of-compiled-to-delegate-expression
	/// </summary>
	[TestFixture(Category = CompetitionHelpers.PerfTestCategory + ": Design decisions")]
	[CompetitionBurstMode]
	[CompetitionMeasureAllocations]
	public class DecisionOperatorsEmitOrExpressionsPerfTest
	{
		#region Benchmark helpers
		private static readonly Func<int, int, bool> _lessThanDelegate;
		private static readonly Func<int, int, bool> _lessThanOperators;
		private static readonly Func<int, int, bool> _lessThanExpression;
		private static readonly Func<int, int, bool> _lessThanDynamicMethod;
		private static readonly Func<int, int, bool> _lessThanDynamicMethodAssociated;
		private static readonly Func<int, int, bool> _lessThanTypeBuilder;
		private static readonly Func<int, int, bool> _lessThanTbExpression;

		private static void EmitLessThan(ILGenerator g)
		{
			g.Emit(OpCodes.Ldarg_0);
			g.Emit(OpCodes.Ldarg_1);
			g.Emit(OpCodes.Clt);
			g.Emit(OpCodes.Ret);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool LessThan(int a, int b) => a < b;

		static DecisionOperatorsEmitOrExpressionsPerfTest()
		{
			_lessThanDelegate = LessThan;
			_lessThanOperators = Operators<int>.LessThan;

			Expression<Func<int, int, bool>> expr = (a, b) => a < b;
			_lessThanExpression = expr.Compile();

			var dynMethod = new DynamicMethod("LessThan", typeof(bool), new[] { typeof(int), typeof(int) });
			EmitLessThan(dynMethod.GetILGenerator());
			_lessThanDynamicMethod = (Func<int, int, bool>)dynMethod.CreateDelegate(typeof(Func<int, int, bool>));

			dynMethod = new DynamicMethod("LessThanAssoc", typeof(bool), new[] { typeof(int), typeof(int) }, typeof(int));
			EmitLessThan(dynMethod.GetILGenerator());
			_lessThanDynamicMethodAssociated = (Func<int, int, bool>)dynMethod.CreateDelegate(typeof(Func<int, int, bool>));

			var ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Ops"), AssemblyBuilderAccess.RunAndCollect);
			var mod = ab.DefineDynamicModule("Ops");
			var tb = mod.DefineType("Ops" + nameof(Int32));

			var mLessThan = tb.DefineMethod(
				"LessThan",
				MethodAttributes.Public | MethodAttributes.Static,
				typeof(bool), new[] { typeof(int), typeof(int) });
			EmitLessThan(mLessThan.GetILGenerator());

			var mLessThanExpr = tb.DefineMethod(
				"LessThanExpr",
				MethodAttributes.Public | MethodAttributes.Static,
				typeof(bool), new[] { typeof(int), typeof(int) });
			expr.CompileToMethod(mLessThanExpr);

			var t2 = tb.CreateType();
			_lessThanTypeBuilder = (Func<int, int, bool>)t2.GetMethod("LessThan").CreateDelegate(typeof(Func<int, int, bool>));
			_lessThanTbExpression =
				(Func<int, int, bool>)t2.GetMethod("LessThanExpr").CreateDelegate(typeof(Func<int, int, bool>));
		}

		private const int N = 1001;
		private int[] _a, _b, _c;

		[Setup]
		public void Setup()
		{
			var rnd = new Random(0);
			_a = new int[N];
			_b = new int[N];
			_c = new int[N];
			for (int i = 0; i < N; i++)
			{
				_a[i] = rnd.Next();
				_b[i] = rnd.Next();
			}
		}
		#endregion

		[Test]
		public void RunDecisionOperatorsEmitOrExpressionsPerfTest() => Competition.Run(this);

		[CompetitionBaseline, GcAllocations(0)]
		public void MinDelegate()
		{
			for (int i = 0; i < N; i++)
			{
				int x = _a[i], y = _b[i];
				_c[i] = _lessThanDelegate(x, y) ? x : y;
			}
		}

		[CompetitionBenchmark(0.53, 0.61), GcAllocations(0)]
		public void MinMethod()
		{
			for (int i = 0; i < N; i++)
			{
				int x = _a[i], y = _b[i];
				_c[i] = LessThan(x, y) ? x : y;
			}
		}

		[CompetitionBenchmark(0.37, 0.43), GcAllocations(0)]
		public void MinHardcoded()
		{
			for (int i = 0; i < N; i++)
			{
				int x = _a[i], y = _b[i];
				_c[i] = x < y ? x : y;
			}
		}

		[CompetitionBenchmark(0.56, 0.65), GcAllocations(0)]
		public void MinOperators()
		{
			for (int i = 0; i < N; i++)
			{
				int x = _a[i], y = _b[i];
				_c[i] = _lessThanOperators(x, y) ? x : y;
			}
		}

		[CompetitionBenchmark(0.56, 0.71), GcAllocations(0)]
		public void MinExpression()
		{
			for (int i = 0; i < N; i++)
			{
				int x = _a[i], y = _b[i];
				_c[i] = _lessThanExpression(x, y) ? x : y;
			}
		}

		[CompetitionBenchmark(0.95, 1.06), GcAllocations(0)]
		public void MinDynamicMethod()
		{
			for (int i = 0; i < N; i++)
			{
				int x = _a[i], y = _b[i];
				_c[i] = _lessThanDynamicMethod(x, y) ? x : y;
			}
		}

		[CompetitionBenchmark(0.91, 1.05), GcAllocations(0)]
		public void MinDynamicMethodAssociated()
		{
			for (int i = 0; i < N; i++)
			{
				int x = _a[i], y = _b[i];
				_c[i] = _lessThanDynamicMethodAssociated(x, y) ? x : y;
			}
		}

		[CompetitionBenchmark(0.92, 1.05), GcAllocations(0)]
		public void MinTypeBuilder()
		{
			for (int i = 0; i < N; i++)
			{
				int x = _a[i], y = _b[i];
				_c[i] = _lessThanTypeBuilder(x, y) ? x : y;
			}
		}

		[CompetitionBenchmark(0.94, 1.06), GcAllocations(0)]
		public void MinTbExpression()
		{
			for (int i = 0; i < N; i++)
			{
				int x = _a[i], y = _b[i];
				_c[i] = _lessThanTbExpression(x, y) ? x : y;
			}
		}
	}
}