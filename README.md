# Pamba

*Pamba* (Swahili: to decorate, to adorn) - type-safe Model-View-Update (MVU) runtime
for .NET 10 / C# 14 desktop applications.

Pamba provides the dispatch loop, subscription lifecycle management, and projection
infrastructure. You provide the pure functions. The result is a GUI architecture
where all business logic is synchronously testable without mocks, UI frameworks,
or async coordination.

## Prerequisites

- .NET 10 SDK
- C# 14

## Why MVU

In MVU, `Update` is a pure function: `(Msg, State) -> (State, Cmd[])`. Side effects are returned as data,
not executed inline. This means every state transition - including the effects it requests - is testable
with a direct function call and an assertion on the return value:

```csharp
var (newState, cmds) = Update(new Msg.LoginRequested(), state);
Assert.IsType<AuthPhase.Acquiring>(newState.Auth);
Assert.IsType<Cmd.AcquireToken>(cmds[0]);
```

No mocks. No async. No UI framework. One function call, one assertion.

### How It Compares

| Architecture       | Drawbacks                                                                                                                                                                                                                            |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **MVVM**           | Testing requires mocking `ICommand` and `INotifyPropertyChanged`. Effects are scattered across setters, command handlers, and Rx chains. State is mutable (`ObservableCollection`, `set` properties).                                |
| **MVI / Cycle.js** | Testing requires Observable test fixtures and subscribe-then-assert patterns. Composition through streams hides state flow.                                                                                                          |
| **Signals**        | State is held in mutable cells. Assertions are order-dependent because updates propagate through a dependency graph rather than returning values.                                                                                    |
| **Actor Model**    | Testing cross-actor interactions requires async coordination. Adds concurrency complexity unnecessary for single-process desktop applications.                                                                                       |
| **Immediate Mode** | State is mutable and imperative. The entire UI is re-executed every frame. No separation between state transitions and rendering.                                                                                                    |
| **Code-behind**    | Business logic entangled with UI event handlers. Untestable without UI automation.                                                                                                                                                   |
| **MVU**            | All state transitions are sequential (no concurrent processing). Indirect side effects via commands add boilerplate compared to inline async. Large state records may need careful decomposition to avoid unwieldy update functions. |

## Architecture

An MVU application separates into two layers:

- **Core** (framework-free): state types, message types, command types, subscription types,
  `Init`, `Update`, `Subscriptions`, validators. All pure functions operating on immutable data.
  Testable with `Pamba.Testing`.
- **Shell** (framework-specific): runtime wiring, state-to-UI projection, command execution (I/O),
  subscription management. References your UI framework.

Core and Shell are separate assemblies. Core cannot accidentally depend on UI framework types
because it has zero framework references.

```text
Init() -> (State, Cmd[])
            |
            v
          Projection (state -> UI updates)
            |
            v (user interaction)
          Msg dispatched
            |
            v
          Update(Msg, State) -> (State, Cmd[])
            |                        |
            v                        v
          New State              Runtime executes Cmds
            |                        |
            v                        v
          Projection updated       Msg dispatched back
            |
            v
          Subscriptions(State) -> Sub[]
            |
            v
          Runtime diffs and manages active subscriptions
```

## Packages

| Package         | TFM               | Dependencies           | Purpose                                                               |
| --------------- | ----------------- | ---------------------- | --------------------------------------------------------------------- |
| `Pamba`         | `net10.0`         | None                   | Contracts, dispatch loop, command/subscription infrastructure         |
| `Pamba.WinUI`   | `net10.0-windows` | `Pamba`, WindowsAppSDK | `DispatcherQueue`-based runtime, projection base, timer subscriptions |
| `Pamba.Testing` | `net10.0`         | `Pamba`                | `MvuTestRunner`, `MvuScenario` - works with xUnit, NUnit, MSTest      |

## Quick Start

### 1. Define types in Core (net10.0, references `Pamba`)

```csharp
public sealed record AppState(int Count);

public abstract record Msg
{
  public sealed record Increment : Msg;
  public sealed record Decrement : Msg;
  public sealed record SaveFailed(string Detail) : Msg;
}

public abstract record Cmd
{
  public sealed record Persist(int Value) : Cmd;
}

public sealed record Sub(string Key) : ISubscription<Msg>;
```

### 2. Define the programme

```csharp
public static readonly MvuProgramme<AppState, Msg, Cmd, Sub> Programme = new()
{
  Init = () => (new AppState(0), []),
  Update = (msg, state) => msg switch
  {
    Msg.Increment => (state with { Count = state.Count + 1 },
                      [new Cmd.Persist(state.Count + 1)]),
    Msg.Decrement => (state with { Count = state.Count - 1 }, []),
    Msg.SaveFailed => (state, []),
    _ => (state, [])
  },
  Subscriptions = _ => [],
  OnCommandError = (cmd, ex) => new Msg.SaveFailed(ex.Message),
  Validate = state => state.Count >= 0
      ? state
      : throw new InvalidOperationException($"Count must be non-negative: {state.Count}")
};
```

### 3. Wire the runtime in Shell (net10.0-windows, references `Pamba.WinUI`)

```csharp
var projection = new AppProjection(mainWindow);

_runtime = WinUIMvuRuntime
    .Create(Programme, mainWindow.DispatcherQueue)
    .WithCommandExecutor(commandExecutor.Execute)
    .WithSubscriptionStarter(subscriptionStarter.Start)
    .WithProjection(projection.ProjectInitial, projection.Project)
    .Start();
```

### 4. Test in Core tests (references `Pamba.Testing`)

```csharp
[Fact]
public void Increment_increases_count_and_persists()
{
  TransitionResult<AppState, Cmd, Sub> result =
      MvuTestRunner.UpdateAndValidate(Programme, new AppState(0), new Msg.Increment());

  Assert.Equal(1, result.State.Count);
  Assert.Single(result.Commands);
  Assert.IsType<Cmd.Persist>(result.Commands[0]);
}

[Fact]
public void Scenario_increments_accumulate()
{
  MvuScenario.For(Programme)
      .Dispatch(new Msg.Increment(), r => Assert.Equal(1, r.State.Count))
      .Dispatch(new Msg.Increment(), r => Assert.Equal(2, r.State.Count))
      .Dispatch(new Msg.Decrement())
      .AssertState(s => Assert.Equal(1, s.Count));
}
```

## Application Structure

```text
my-app/
  my-app-core/              (net10.0, references Pamba)
    Model/                   State types (sealed records)
    Messages/                Msg hierarchy
    Commands/                Cmd hierarchy
    Subscriptions/           Sub hierarchy (implement ISubscription<Msg>)
    Update/                  Update function + sub-updaters
    Programme.cs             MvuProgramme definition

  my-app/                    (net10.0-windows, references Pamba.WinUI + my-app-core)
    Shell/
      AppProjection.cs       Extends StateProjectionBase<AppState>
      AppCommandExecutor.cs  Implements CommandExecutor<Cmd, Msg>
      AppSubscriptionStarter.cs
    MainWindow.xaml/.cs
    App.xaml/.cs             Wires WinUIMvuRuntime

  my-app-core-tests/         (net10.0, references Pamba.Testing + my-app-core)
    UpdateTests.cs           Pure function tests
    ScenarioTests.cs         Multi-step flow tests
```

## Runtime Guarantees

- **FIFO message ordering.** Messages are never processed concurrently.
  Every transition sees the result of all prior transitions.
- **Command error routing.** Exceptions from command executors are caught and routed back
  as typed messages via `OnCommandError`.
  `OperationCanceledException` during disposal is silently absorbed.
- **Subscription lifecycle correctness.** Started exactly once per unique key,
  cancelled exactly once when removed, all cancelled on dispose.
- **Validation on every transition.** When `Validate` is provided,
  it runs in all build configurations - not only during testing.
- **State-unchanged optimisation.** When `oldState.Equals(newState)`, subscription diffing
  and projection callbacks are skipped. Commands are still executed.
- **Thread safety.** `Dispatch` is safe to call from any thread.
  Processing occurs on the dispatcher thread.
- **Disposal.** `MvuRuntime` implements `IDisposable`. Disposing cancels all active
  subscriptions and causes subsequent `Dispatch` calls to no-op.

## Licence

Apache License 2.0
