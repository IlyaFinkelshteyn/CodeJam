﻿#if LESSTHAN_NET40
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
// BASEDON: https://github.com/dotnet/coreclr/blob/1a9781b68b8ec66c223bb96f44ae1d2704ce6fdf/src/mscorlib/shared/System/Runtime/Versioning/TargetFrameworkAttribute.cs

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

/*============================================================
**
**
**
** Purpose: Identifies which SKU and version of the .NET
**   Framework that a particular library was compiled against.
**   Emitted by VS, and can help catch deployment problems.
**
===========================================================*/

using System;
using System.Diagnostics.Contracts;

namespace System.Runtime.Versioning
{
	[AttributeUsageAttribute(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
	public sealed class TargetFrameworkAttribute : Attribute
	{
		private String _frameworkName;  // A target framework moniker
		private String _frameworkDisplayName;

		// The frameworkName parameter is intended to be the string form of a FrameworkName instance.
		public TargetFrameworkAttribute(String frameworkName)
		{
			if (frameworkName == null)
				throw new ArgumentNullException(nameof(frameworkName));
			_frameworkName = frameworkName;
		}

		// The target framework moniker that this assembly was compiled against.
		// Use the FrameworkName class to interpret target framework monikers.
		public String FrameworkName
		{
			get { return _frameworkName; }
		}

		public String FrameworkDisplayName
		{
			get { return _frameworkDisplayName; }
			set { _frameworkDisplayName = value; }
		}
	}
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
#endif