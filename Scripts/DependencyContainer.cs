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
		private static readonly Dictionary<Type, InjectableTypeInfo> injectableTypes;

		static DependencyContainer()
		{
			// build up a cache of all injectable types
			injectableTypes = AppDomain.CurrentDomain.GetAssemblies()
				// Find all types
				.SelectMany(a => a.GetTypes())
				// for each one return a new Injectable or null
				.Select(t =>
				{
					// get all fields on this type that have [Inject] Attribute
					var injectableFields = InjectableTypeInfo.GetInjectableFields(t);
					// if there are none, return null, otherwise create the new injectable object
					if (!injectableFields.Any()) return null;
					return new InjectableTypeInfo(t, injectableFields);
				})
				// filter out non injectable types
				.Where(i => i != null)
				.ToDictionary(t => t.Type, t => t);
		}

		public bool EnableLogging { get; set; }

		private readonly Dictionary<Type, object> dependencies = new Dictionary<Type, object>();

		public DependencyContainer()
		{
			Add(this);
		}

		public void Add(object obj)
		{
			if (obj == null) throw new ArgumentNullException(nameof(obj));

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
			if (EnableLogging)
			{
				Log("Performing SelfInject()");
			}

			foreach (var dependency in dependencies.Values)
			{
				InjectTo(dependency);
			}
		}

		public void InjectTo(params object[] targetObjects)
		{
			if (targetObjects == null) throw new ArgumentNullException(nameof(targetObjects));

			foreach (var targetObject in targetObjects)
			{
				if (targetObject == null) throw new ArgumentNullException(nameof(targetObject));

				InjectTo(targetObject);
			}
		}

		public void InjectTo(object targetObject)
		{
			if (targetObject == null) throw new ArgumentNullException(nameof(targetObject));

			var targetObjectType = targetObject.GetType();

			if (!injectableTypes.TryGetValue(targetObjectType, out var injectableType)) return;

			if (EnableLogging)
			{
				Log($"Performing InjectTo({targetObject})", targetObject);
			}

			var injectableFields = injectableType.InjectableFields;

			foreach (var field in injectableFields)
			{
				var dependency = GetDependency(field.FieldType);
				if (dependency == null)
				{
					LogWarning(
						$"Unmet dependency for {targetObjectType.Name}.{field.Name} ({field.FieldType})");
				}
				else if (EnableLogging)
				{
					Log($"Injecting to {targetObjectType}.{field.Name}; Type=({dependency.GetType().Name}) Value={dependency}({field.FieldType})",
						dependency);
				}

				field.SetValue(targetObject, dependency);
			}

			try
			{
				if (EnableLogging)
				{
					Log($"Completed injection to {targetObject}", targetObject);
				}
				var handler = targetObject as IDependencyInjectionCompleteHandler;
				handler?.HandleDependencyInjectionComplete();
			}
			catch (Exception e)
			{
				LogError(e.Message);
				LogError(e.StackTrace);
			}
		}

		public void InjectToSceneObjects(bool includeInactiveObjects = true)
		{
			if (EnableLogging) Log($"Performing InjectToSceneObjects({includeInactiveObjects})");

			foreach (var injectableTypeInfo in injectableTypes.Values)
			{
				if (!injectableTypeInfo.IsMonoBehaviour) continue;

				var injectableObjects = Object.FindObjectsOfType(injectableTypeInfo.Type, includeInactiveObjects);
				foreach (var injectableObject in injectableObjects)
				{
					// Ignore scene injection
					if (injectableTypeInfo.SceneInjectionControl == SceneInjectionControl.Ignore) continue;

					// Ignore if the object is inactive
					if (includeInactiveObjects &&
					    injectableTypeInfo.SceneInjectionControl == SceneInjectionControl.OnlyWhenActive &&
					    injectableObject is Component obj &&
					    !obj.gameObject.activeInHierarchy)
					{
						continue;
					}

					// We only want the exact type here, not subclasses
					// - since we will also look for them later and we don't want double injections.
					// - There's a potential optimization opportunity here: see the end of the function...
					if (injectableObject.GetType() != injectableTypeInfo.Type) continue;

					// Special case to stop re-injecting to things in the pool
					if (dependencies.Values.Contains(injectableObject)) continue;

					InjectTo(injectableObject);
				}
			}

			// OPTIMIZATION OPPORTUNITY:
			// The algorithm will search the scene for all injectable types, extending from MonoBehaviour.
			// If any injectables are inherited, that will result in multiple searches.
			// Example:  `SubClass : BaseClass`
			// even if subclass has no injected fields of it's own, it's included since it has injected fields in the base class.
			// The guard in the above function stops us from performing multiple injections
			// and we won't injection occur when iterating the BaseClass type.
			// We currently only inject for exact type matches.

			// The optimization opportunity: Detect inheritance hierarchies and cache them in the InjectableTypeInfo datastructure.
			// When we search for injectables using Object.FindObjectsOfType - we only need to search for the base class
			// as this will find any instances of any subclasses.
			// When we found those instances we can use the InjectableTypeInfo object of the base type to traverse and find
			// the InjectableTypeInfo representing the actual type and use that to inject. This will result in less "Find" calls.
		}

		public void InjectToGameObject(GameObject gameObject, bool includeChildren = true)
		{
			if (EnableLogging) Log($"Performing InjectToGameObject({gameObject}, {includeChildren})", gameObject);

			foreach (var injectable in injectableTypes.Values)
			{
				// OPTIMIZATION OPPORTUNITY: Same as the above function
				var type = injectable.Type;

				var components = includeChildren
					? gameObject.GetComponentsInChildren(type)
					: gameObject.GetComponents(type);

				foreach (var injectableObject in components)
				{
					if (dependencies.Values.Contains(injectableObject)) continue;

					InjectTo(injectableObject);
				}
			}
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

		private static void Log(string value, object context = null) => Debug.Log($"[DependencyContainer]: {value}", context as Object);
		private static void LogWarning(string value, object context = null) => Debug.LogWarning($"[DependencyContainer]: {value}", context as Object);
		private static void LogError(string value, object context = null) => Debug.LogError($"[DependencyContainer]: {value}", context as Object);

		private class InjectableTypeInfo
		{
			public bool IsMonoBehaviour { get; }
			public Type Type { get; }
			public IEnumerable<FieldInfo> InjectableFields { get; }
			public SceneInjectionControl SceneInjectionControl { get; } = SceneInjectionControl.Always;

			public InjectableTypeInfo(Type type, IEnumerable<FieldInfo> injectableFields)
			{
				IsMonoBehaviour = type.IsSubclassOf(typeof(MonoBehaviour));
				var sceneInjectionControl = type.GetCustomAttribute<SceneInjectionOptions>();
				if (sceneInjectionControl != null)
				{
					SceneInjectionControl = sceneInjectionControl.Control;
				}

				Type = type;
				InjectableFields = injectableFields;
			}

			public override string ToString()
			{
				return Type.Name;
			}

			public static IEnumerable<FieldInfo> GetInjectableFields(Type type)
			{
				const BindingFlags BINDING_FLAGS = BindingFlags.Instance | BindingFlags.NonPublic;

				var fieldInfos = type.GetFields(BINDING_FLAGS).ToList();

				var currentType = type;
				while (currentType.BaseType != typeof(object))
				{
					if (currentType.BaseType == null) break;
					fieldInfos.AddRange(currentType.BaseType.GetFields(BINDING_FLAGS));
					currentType = currentType.BaseType;
				}

				return fieldInfos.Where(f => f.GetCustomAttribute<InjectAttribute>() != null);
			}
		}
	}
}