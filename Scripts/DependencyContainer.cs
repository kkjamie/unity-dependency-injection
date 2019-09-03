﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityDependencyInjection
{
	public interface IDependencyProvider
	{
		object GetDependency(Type t);
	}

	public class DependencyContainer : IDependencyProvider
	{
		private static readonly InjectableMonoBehaviour[] injectableMonoBehaviours;

		static DependencyContainer()
		{
			injectableMonoBehaviours = AppDomain.CurrentDomain.GetAssemblies()
				// Find all types
				.SelectMany(a => a.GetTypes())
				// that are subclasses of mono behaviours
				.Where(t => t.IsSubclassOf(typeof(MonoBehaviour)))
				// for each one return a new Injectable or null
				.Select(t =>
				{
					// get all fields on this type that have [Inject] Attribute
					var injectableFields = GetInjectableFields(t);
					// if there are none, return null, otherwise create the new injectable object
					if (!injectableFields.Any()) return null;
					return new InjectableMonoBehaviour(t, injectableFields);
				})
				// filter out non injectable types
				.Where(i => i != null)
				.ToArray();
		}

		private readonly List<object> dependencies = new List<object>();

		public DependencyContainer()
		{
			dependencies.Add(this);
		}

		public DependencyContainer Add(params object[] dependenciesToAdd)
		{
			for (var i = 0; i < dependenciesToAdd.Length; i++)
			{
				var dependency = dependenciesToAdd[i];
				if (dependency == null)
				{
					throw new Exception($"Cannot add a null dependency to the container, index {i}");
				}

				dependencies.Add(dependency);
			}

			return this;
		}

		public object GetDependency(Type type)
		{
			foreach (var dependency in dependencies)
			{
				if (type.IsInstanceOfType(dependency))
				{
					return dependency;
				}
			}

			return null;
		}

		public T GetDependency<T>()
		{
			return (T)GetDependency(typeof(T));
		}

		public void SelfInject()
		{
			foreach (var dependency in dependencies)
			{
				InjectTo(dependency);
			}
		}

		public void InjectToSceneObjects()
		{
			foreach (var injectable in injectableMonoBehaviours)
			{
				var injectableObjects = Object.FindObjectsOfType(injectable.MonoBehaviourType);
				foreach (var injectableObject in injectableObjects)
				{
					if (injectableObject.GetType() != injectable.MonoBehaviourType) continue;
					if (dependencies.Contains(injectableObject)) continue;

					injectable.Inject(injectableObject, this);

					var handler = injectableObject as IDependencyInjectionCompleteHandler;
					handler?.HandleDependencyInjectionComplete();
				}
			}
		}

		public void InjectTo(params object[] targetObjects)
		{
			foreach (var targetObject in targetObjects)
			{
				InjectTo(targetObject);
			}
		}

		public void InjectTo(object targetObject)
		{
			var targetObjectType = targetObject.GetType();
			var injectableFields = GetInjectableFields(targetObjectType);

			foreach (var field in injectableFields)
			{
				var dependency = GetDependency(field.FieldType);
				if (dependency == null)
				{
					Debug.LogWarning(
						$"Unmet dependency for {targetObjectType.Name}.{field.Name} ({field.FieldType})");
				}

				field.SetValue(targetObject, dependency);
			}

			var handler = targetObject as IDependencyInjectionCompleteHandler;
			handler?.HandleDependencyInjectionComplete();
		}

		public void InjectToGameObject(GameObject gameObject, bool includeChildren = true)
		{
			foreach (var injectable in injectableMonoBehaviours)
			{
				var type = injectable.MonoBehaviourType;

				var components = includeChildren ?
					gameObject.GetComponentsInChildren(type) :
					gameObject.GetComponents(type);

				foreach (var injectableObject in components)
				{
					if (dependencies.Contains(injectableObject)) continue;

					injectable.Inject(injectableObject, this);

					var handler = injectableObject as IDependencyInjectionCompleteHandler;
					handler?.HandleDependencyInjectionComplete();
				}
			}
		}

		public static IEnumerable<FieldInfo> GetInjectableFields(Type type)
		{
			var bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic;

			var fieldInfos = type.GetFields(bindingFlags).ToList();

			var currentType = type;
			while (currentType.BaseType != typeof(object))
			{
				if (currentType.BaseType == null) break;
				fieldInfos.AddRange(currentType.BaseType.GetFields(bindingFlags));
				currentType = currentType.BaseType;
			}

			return fieldInfos.Where(f => f.GetCustomAttribute<InjectAttribute>() != null);
		}

		public void Destroy()
		{
			foreach (var dependency in dependencies)
			{
				var destructionHandler = dependency as IDependencyDestructionHandler;
				destructionHandler?.HandleDependenciesDestroyed();
			}

			dependencies.Clear();
		}

		private class InjectableMonoBehaviour
		{
			public Type MonoBehaviourType { get; private set; }
			public IEnumerable<FieldInfo> InjectableFields { get; }

			public InjectableMonoBehaviour(Type monoBehaviourType, IEnumerable<FieldInfo> injectableFields)
			{
				MonoBehaviourType = monoBehaviourType;
				InjectableFields = injectableFields;
			}

			public void Inject(object obj, IDependencyProvider container)
			{
				foreach (var field in InjectableFields)
				{
					var dependency = container.GetDependency(field.FieldType);
					if (dependency == null)
					{
						Debug.LogWarning($"Unmet dependency for {MonoBehaviourType.Name}.{field.Name} ({field.FieldType})");
					}
					field.SetValue(obj, dependency);
				}
			}

			public override string ToString()
			{
				return MonoBehaviourType.Name;
			}
		}
	}
}