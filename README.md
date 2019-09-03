# unity-dependency-injection

## Motivation
Allow references to high level objects (Typically managers or services), without using singletons or spaghetti serialization, or endless constructor parameters.

## How to use it
1. Create a `DependencyContainer` and fill it with objects you want to inject to other places.

```c#
var container = new DependencyContainer();
container.Add(new PlayerManager());
container.Add(new BulletManager());
container.Add(new EnemyManager());
```

2. Self inject. This injects each dependency into all the others

```c#
container.SelfInject();
```

3/ Use `[Inject]` attribute on private fields that you want to be populated by dependencies from the container
```c#
class PlayerManager
{
    // when self inject is called this field will be populated.
    [Inject]
    private readonly BulletManager bulletManager = null;
    
    private void HandleOnPlayerFired(Player player)
    {
        // now we have access to the bullet manager
        bulletManager.SpawnBullet(player.Weapon.SpawnPoint);
    }
}
```

## Other uses

### Inject to a single object
`container.InjectTo(targetObject)`

### Inject to a game object in the scene
`container.InjectToGameObject(gameObject, includeChildObjects)`

### Inject to all scene objects
`container.InjectToSceneObjects()`
Note: this will field all mono behaviour types with injectable fields and attempt to find all of them in the scene. This can be an expensive operation depending on how many objects and how many injectable mono behaviours you have. Not recommended during game play *See below

### DependencyContainer is also injected
```c#
[Inject]
private DependencyContainer dependencyContainer = null;
```
This can be useful if you want to spawn objects later on and inject to them.

### Manually obtain a dependency from the container
```c#
container.GetDependency<Type>();
container.GetDependency(type);
```

### Be notified when injection has completed
Implement `IDependencyInjectionCompleteHandler`

Use this like a constructor or an Awake/Start, and know when your dependencies have been injected
```c#
class MyInjectionTarget : IDependencyInjectionCompleteHandler
{
    void IDependencyInjectionCompleteHandler.HandleDependencyInjectionComplete()
    {
        // dependencies should now be present.
    }
}
```

### Destroy the container
`container.Destroy()`.
This is just a notification mechanism
If your dependencies need to be notified when the container is destroyed then implement `IDependencyDestructionHandler` and the function `HandleDependenciesDestroyed` will be called on each implementing object in the container

## Best practises

### Use it in your loading screen.
For best performance it's best to create your dependencies & container and inject everything after scenes have been loaded but before you start the game.
It's also easier to reason about. Sometimes runtime injection is necessary such as injecting to objects that are spawned during the course of a game. This can be circumvented by using pooling, and instantiating and injecting up front.

### Inject by a more abstract type.
The type of an injected field doesn't have to be the exact type of the dependency. You can protect parts of your API using more abstracted types. In the best case an interface.

EG: A BulletManager could do multiple things. But if your weapon only needs to create bullets, don't give it the full API access.
Instead of injection BulletManager inject an interface with a slimmer API.

```c#
class BulletManager : IBulletCreationService, IBulletDestructionService, IBulletAccessService {}

class Weapon
{
    [Inject]
    private IBulletCreationService bulletCreator = null;
    
    public void Fire()
    {
        var bullet = bulletCreator.CreateBullet();
        // I can only create bullets here, I can't destroy them or access all the bullets or anything freaky.
    }
}
```` 



