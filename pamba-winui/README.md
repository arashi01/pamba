# Pamba.WinUI

WinUI 3 integration for the Pamba MVU runtime.

Provides a `DispatcherQueue`-backed runtime factory,
`StateProjectionBase<TState>` for segment-based UI diffing,
and timer/delay subscription helpers.

## Install

```shell
dotnet add package Pamba.WinUI
```

Requires .NET 10 / C# 14, `Pamba`, `Microsoft.WindowsAppSDK`. Minimum platform: Windows 11 22H2.

Namespace: `Pamba.WinUI`.

## Runtime Factory

`WinUIMvuRuntime` is the WinUI-specific entry point. It captures the
`DispatcherQueue` at creation and wires it as the thread dispatcher automatically.

```csharp
MvuRuntime<AppState, Msg, Cmd, Sub> runtime = WinUIMvuRuntime
    .Create(program, mainWindow.DispatcherQueue)
    .WithCommandExecutor(commandExecutor.Execute)
    .WithSubscriptionStarter(subscriptionStarter.Start)
    .WithProjection(projection.ProjectInitial, projection.Project)
    .Start();
```

### Builder Steps

| Step | Method                                             | Returns                          |
| ---- | -------------------------------------------------- | -------------------------------- |
| 1    | `WinUIMvuRuntime.Create(program, dispatcherQueue)` | `IWinUIRuntimeWithProgram`       |
| 2    | `.WithCommandExecutor(executor)`                   | `IWinUIRuntimeWithExecutor`      |
| 3    | `.WithSubscriptionStarter(starter)`                | `IWinUIRuntimeWithSubscriptions` |
| 4a   | `.WithProjection(onStateChanged)`                  | `IWinUIRuntimeReady`             |
| 4b   | `.WithProjection(onInit, onStateChanged)`          | `IWinUIRuntimeReady`             |
| 4c   | `.Start()`                                         | `MvuRuntime` (no projection)     |
| 5    | `.Start()`                                         | `MvuRuntime`                     |

Projection is optional. Call `Start()` directly after step 3 if you do not need state-to-UI projection.

## StateProjectionBase

Abstract base class for mapping state changes to UI updates with automatic diffing.
Subclass it and register segments in the constructor. Each segment pairs a state
selector with a UI update action. On each transition, only segments whose selected
value has changed (by value equality) are invoked.

```csharp
public sealed class AppProjection : StateProjectionBase<AppState>
{
  public AppProjection(MainWindow window)
  {
    Segment(s => s.Auth, auth => ProjectAuth(window, auth));
    Segment(s => s.CurrentModule, mod => ProjectNavigation(window, mod));
    Segment(s => s.Items, items => ProjectItems(window, items));
  }

  public override void ProjectInitial(AppState initialState)
  {
    // Set all UI elements to match the initial state.
    // Called once during startup via the onInit callback.
  }
}
```

### Wiring

Pass `ProjectInitial` and `Project` as the builder's projection callbacks:

```csharp
var projection = new AppProjection(mainWindow);

WinUIMvuRuntime
    .Create(program, mainWindow.DispatcherQueue)
    .WithCommandExecutor(executor)
    .WithSubscriptionStarter(starter)
    .WithProjection(projection.ProjectInitial, projection.Project)
    .Start();
```

`Project(oldState, newState)` is called on the UI thread after every state change
where `!oldState.Equals(newState)`. It evaluates each registered segment and invokes
only those whose selected value differs.

The selector return type must implement `IEquatable<TSegment>`. C# records satisfy this automatically.

## Timer Subscriptions

Pre-built helpers for timer-based subscriptions. Use them inside your
`SubscriptionStarter` delegate to handle specific subscription types:

```csharp
IDisposable StartSubscription(Sub subscription, Dispatch<Msg> dispatch) =>
    subscription switch
    {
      Sub.RefreshTimer t => TimerSubscription.Start(
          interval: t.Interval,
          createMessage: () => new Msg.RefreshTick(),
          dispatch: dispatch,
          dispatcherQueue: _dispatcherQueue),

      Sub.SearchDebounce d => DelayedSubscription.Start(
          delay: d.Delay,
          createMessage: () => new Msg.DebounceComplete(),
          dispatch: dispatch,
          dispatcherQueue: _dispatcherQueue),

      _ => throw new InvalidOperationException($"Unknown subscription: {subscription}")
    };
```

Both return `IDisposable`. The runtime manages their lifecycle via subscription
diffing - you do not need to dispose them manually.

## Command Debouncer

Wraps a `CommandExecutor` to debounce high-frequency commands. Each invocation cancels the previous pending execution.

```csharp
var debounced = new CommandDebouncer<Cmd, Msg>(
    delay: TimeSpan.FromMilliseconds(300),
    inner: actualExecutor,
    dispatcherQueue: dispatcherQueue);

// Pass debounced.Execute as the command executor
```

## Related Packages

- **Pamba** - Framework-agnostic core: contracts, dispatch loop, command/subscription infrastructure.
- **Pamba.Testing** - Test utilities: `MvuTestRunner.UpdateAndValidate` and `MvuScenario` for multi-step flow testing.

## Licence

Apache License 2.0
