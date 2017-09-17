﻿using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

namespace CodeJam
{
	/// <summary>Switches for features depending on platform targeting</summary>
	internal static class PlatformDependent
	{
		public static readonly string TargetPlatform =
			typeof(PlatformDependent).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

		/// <summary>MethodImplOptions.AggressiveInlining or 0, if not supported by target platform</summary>
		public const MethodImplOptions AggressiveInlining =
#if LESSTHAN_NET45
			0;
#else
			MethodImplOptions.AggressiveInlining;
#endif
	}
}