using System;

namespace UnityDependencyInjection
{
	[AttributeUsage(AttributeTargets.Class)]
	public class IgnoreSceneInjectionAttribute : Attribute
	{
	}
}