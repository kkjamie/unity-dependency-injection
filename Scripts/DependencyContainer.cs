using System;
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

		private readonly Dictionary<Type, object> dependencies = new Dictionary<Type, object>();

		public DependencyContainer()
		{
			Add(this);
		}

		public void Add(object obj)
		{
			var type = obj.GetType();

			if (dependencies.ContainsKey(type))
			{
				throw new Exception("Object of this type already exists in the dependency container");
			}

			dependencies.Add(type, obj);
		}

		public T GetDependency<T>() where T : class
		{
			return GetDependency(typeof(T)) as T;
		}

		public object GetDependency(Type type)
		{
			if (dependencies.ContainsKey(type))
			{
				return dependencies[type];
			}

			return dependencies.FirstOrDefault(kvp => type.IsAssignableFrom(kvp.Key)).Value;
		}

		public IEnumerable<T> GetDependencies<T>() where T : class
		{
			var result = new List<T>();

			foreach (var dependency in dependencies)
			{
				var typedDependency = dependency.Value as T;
				if (typedDependency != null)
				{
					result.Add(typedDependency);
				}
			}

			return result;
		}

		public void SelfInject()
		{
			foreach (var dependency in dependencies.Values)
			{
				InjectTo(dependency);
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

		public void InjectToSceneObjects(bool includeInactiveObjects = true)
		{
			foreach (var injectable in injectableMonoBehaviours)
			{
				if (injectable.IgnoreSceneInjection) continue;

				var injectableObjects = Object.FindObjectsOfType(injectable.MonoBehaviourType, includeInactiveObjects);
				foreach (var injectableObject in injectableObjects)
				{
					if (injectableObject.GetType() != injectable.MonoBehaviourType) continue;
					// Special case to stop re-injecting to things in the pool
					if (dependencies.Values.Contains(injectableObject)) continue;

					injectable.Inject(injectableObject, this);

					var handler = injectableObject as IDependencyInjectionCompleteHandler;
					handler?.HandleDependencyInjectionComplete();
				}
			}
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
					if (dependencies.Values.Contains(injectableObject)) continue;

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
			foreach (var dependency in dependencies.Values)
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
			public bool IgnoreSceneInjection { get; }

			public InjectableMonoBehaviour(Type monoBehaviourType, IEnumerable<FieldInfo> injectableFields)
			{
				IgnoreSceneInjection = monoBehaviourType.GetCustomAttribute<IgnoreSceneInjectionAttribute>() != null;
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