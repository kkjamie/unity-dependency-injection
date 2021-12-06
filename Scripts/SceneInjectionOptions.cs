using System;

namespace UnityDependencyInjection
{
	[AttributeUsage(AttributeTargets.Class)]
	public class SceneInjectionOptions : Attribute
	{
		public SceneInjectionControl Control { get; }

		public SceneInjectionOptions(SceneInjectionControl control)
		{
			Control = control;
		}
	}

	public enum SceneInjectionControl
	{
		Ignore,
		OnlyWhenActive,
		Always,
	}
}