# Pamba

Framework-agnostic MVU (Model-View-Update) runtime for .NET 10 / C# 14.

Provides the dispatch loop, command execution infrastructure, subscription lifecycle
management, and the contracts that define an MVU programme. Zero UI dependencies -
wire to any framework by providing a thread dispatcher.

## Install

```shell
dotnet add package Pamba
```

Requires .NET 10 / C# 14. Namespace: `Pamba`.

## Key Concepts

### Programme

An MVU programme is five functions packaged as a single record:

```csharp
MvuProgramme<TState, TMsg, TCmd, TSub>
```

| Function         | Signature                                         | Purpose                                                                   |
| ---------------- | ------------------------------------------------- | ------------------------------------------------------------------------- |
| `Init`           | `() -> (TState, IReadOnlyList<TCmd>)`             | Initial state and startup commands                                        |
| `Update`         | `(TMsg, TState) -> (TState, IReadOnlyList<TCmd>)` | State transition - no side effects, returns new state and commands        |
| `Subscriptions`  | `(TState) -> IReadOnlyList<TSub>`                 | Declares which ongoing effects should be active for the current state     |
| `OnCommandError` | `(TCmd, Exception) -> TMsg`                       | Routes command execution failures back into the loop as typed messages    |
| `Validate`       | `(TState) -> TState` (optional)                   | Invariant check after every transition - runs in all build configurations |

### Type Parameters

| Parameter | Constraint                                | Role                                                                             |
| --------- | ----------------------------------------- | -------------------------------------------------------------------------------- |
| `TState`  | `IEquatable<TState>`                      | Immutable application state. C# records satisfy this automatically.              |
| `TMsg`    | `notnull`                                 | Message hierarchy - typically a sealed record hierarchy.                         |
| `TCmd`    | `notnull`                                 | Command hierarchy - sealed records describing effects to perform.                |
| `TSub`    | `IEquatable<TSub>`, `ISubscription<TMsg>` | Subscription hierarchy - sealed records with a `Key` used for lifecycle diffing. |

### Commands

`Update` does not execute side effects directly. Instead, it returns command values describing
what should happen. The runtime executes them asynchronously via a
`CommandExecutor<TCmd, TMsg>` you provide. If a command throws, the exception is caught
and routed through `OnCommandError` back into the loop as a typed message.

### Subscriptions

`Subscriptions` returns a list describing which ongoing effects (timers, listeners,
connections) should be active for the current state. The runtime diffs this list against
the previous one by `ISubscription<TMsg>.Key`: new keys are started via
`SubscriptionStarter<TSub, TMsg>`; removed keys are disposed; unchanged keys continue running.

## Contracts

```csharp
// Dispatch a message into the loop. Thread-safe, FIFO ordering.
public delegate void Dispatch<in TMsg>(TMsg message);

// Execute a command. Returns a Task because commands typically perform I/O.
public delegate Task CommandExecutor<in TCmd, TMsg>(
    TCmd command, Dispatch<TMsg> dispatch, CancellationToken cancellationToken);

// Start a subscription. Returns IDisposable - the runtime calls Dispose to cancel.
public delegate IDisposable SubscriptionStarter<in TSub, TMsg>(
    TSub subscription, Dispatch<TMsg> dispatch)
    where TSub : ISubscription<TMsg>;

// Subscription identity for lifecycle diffing.
public interface ISubscription<out TMsg>
{
    public string Key { get; }
}
```

## Runtime

Construct via the stepped builder. Each step returns a narrower interface requiring
the next dependency. `Start()` is only available when all dependencies are provided -
misconfiguration is a compile error, not a runtime error.

```csharp
MvuRuntime<TState, TMsg, TCmd, TSub> runtime = MvuRuntimeBuilder
    .Create(programme)
    .WithCommandExecutor(executor)
    .WithSubscriptionStarter(starter)
    .WithDispatcher(
        enqueue: action => action(),            // synchronous (e.g. for tests)
        onInit: state => { },                   // optional: called once with initial state
        onStateChanged: (old, @new) => { })     // optional: called after every state change
    .Start();
```

### WithDispatcher Overloads

| Overload | Parameters                            | Use Case                                    |
| -------- | ------------------------------------- | ------------------------------------------- |
| 1        | `enqueue`                             | No callbacks needed                         |
| 2        | `enqueue`, `onStateChanged`           | React to every state change                 |
| 3        | `enqueue`, `onInit`, `onStateChanged` | React to initial state + every state change |

The `enqueue` parameter is the thread dispatcher. For WinUI:
`DispatcherQueue.TryEnqueue`. For tests: `action => action()`.
For Avalonia: `Dispatcher.UIThread.Post`.

### Dispatch Mechanics

1. `Dispatch(msg)` enqueues via the thread dispatcher. Thread-safe; returns immediately.
2. On the dispatching thread, messages process sequentially (FIFO):
   - `Update(msg, state)` computes new state and commands.
   - `Validate` runs if present.
   - If state is unchanged (`oldState.Equals(newState)`): subscription diff and `onStateChanged` are skipped.
   - Otherwise: subscriptions are diffed, `onStateChanged` is invoked.
   - Commands execute asynchronously. Failures route via `OnCommandError`.
3. Messages from commands or subscriptions re-enter at step 1.

## Usage

### Define Types

```csharp
public sealed record AppState(int Count);

public abstract record Msg
{
  public sealed record Increment : Msg;
  public sealed record SaveFailed(string Detail) : Msg;
}

public abstract record Cmd
{
  public sealed record Persist(int Value) : Cmd;
}

public sealed record Sub(string Key) : ISubscription<Msg>;
```

### Define Programme

```csharp
public static readonly MvuProgramme<AppState, Msg, Cmd, Sub> Programme = new()
{
  Init = () => (new AppState(0), []),
  Update = (msg, state) => msg switch
  {
    Msg.Increment => (state with { Count = state.Count + 1 },
                      [new Cmd.Persist(state.Count + 1)]),
    Msg.SaveFailed => (state, []),
    _ => (state, [])
  },
  Subscriptions = _ => [],
  OnCommandError = (cmd, ex) => new Msg.SaveFailed(ex.Message)
};
```

### Wire Runtime

```csharp
using MvuRuntime<AppState, Msg, Cmd, Sub> runtime = MvuRuntimeBuilder
    .Create(Programme)
    .WithCommandExecutor(myExecutor)
    .WithSubscriptionStarter(myStarter)
    .WithDispatcher(dispatcherQueue.TryEnqueue)
    .Start();

runtime.Dispatch(new Msg.Increment());
```

## Disposal

`MvuRuntime` implements `IDisposable`. Disposing cancels all active subscriptions,
cancels in-flight commands via `CancellationToken`, and causes subsequent
`Dispatch` calls to no-op.

```csharp
// Recommended: using declaration ensures disposal
using MvuRuntime<AppState, Msg, Cmd, Sub> runtime = MvuRuntimeBuilder
    .Create(programme)
    .WithCommandExecutor(executor)
    .WithSubscriptionStarter(starter)
    .WithDispatcher(enqueue)
    .Start();
```

## Related Packages

- **Pamba.WinUI** - WinUI 3 integration with `DispatcherQueue`, `StateProjectionBase`
  for segment-based UI diffing, and timer subscription helpers.
- **Pamba.Testing** - `MvuTestRunner.UpdateAndValidate` and `MvuScenario` for multi-step flow testing.

## Licence

Apache License 2.0
