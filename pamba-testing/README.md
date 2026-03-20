# Pamba.Testing

Test utilities for Pamba MVU programs. Calls `Update`, `Validate`, and
`Subscriptions` in the correct sequence and returns all results for assertion.

No test framework dependency - works with xUnit, NUnit, MSTest, or any assertion library.

## Install

```shell
dotnet add package Pamba.Testing
```

Requires: `Pamba`. Namespace: `Pamba.Testing`.

## MvuTestRunner

### UpdateAndValidate

Calls `Update(msg, state)`, then `Validate` (if present), then
`Subscriptions(newState)`. Returns a `TransitionResult` containing all three
for assertion. If `Validate` throws, the test fails immediately with the
invariant violation message.

```csharp
TransitionResult<AppState, Cmd, Sub> result =
    MvuTestRunner.UpdateAndValidate(program, currentState, new Msg.Increment());

Assert.Equal(1, result.State.Count);
Assert.Single(result.Commands);
Assert.IsType<Cmd.Persist>(result.Commands[0]);
Assert.Empty(result.Subscriptions);
```

### InitAndValidate

Calls `Init()`, then `Validate` (if present), then `Subscriptions(initialState)`.

```csharp
TransitionResult<AppState, Cmd, Sub> result =
    MvuTestRunner.InitAndValidate(program);

Assert.Equal(0, result.State.Count);
Assert.Contains(result.Commands, c => c is Cmd.LoadPreferences);
```

### TransitionResult

```csharp
public sealed record TransitionResult<TState, TCmd, TSub>(
    TState State,
    IReadOnlyList<TCmd> Commands,
    IReadOnlyList<TSub> Subscriptions);
```

All three outputs in a single value. Commands and subscriptions are indexable
lists - use `result.Commands[0]`, `Assert.Single`, `Assert.Empty`,
or pattern matching directly.

## MvuScenario

Fluent API for multi-step flow testing. Dispatches a sequence of messages through
the program, preserving state between steps. Each step optionally accepts an
assertion on the transition result.

```csharp
MvuScenario.For(program)
    .Dispatch(new Msg.Start(), r =>
    {
      Assert.True(r.State.IsRunning);
      Assert.Single(r.Subscriptions);
    })
    .Dispatch(new Msg.Increment(), r => Assert.Equal(1, r.State.Count))
    .Dispatch(new Msg.Increment())
    .AssertState(s => Assert.Equal(2, s.Count));
```

### API

| Method                     | Purpose                                                    |
| -------------------------- | ---------------------------------------------------------- |
| `MvuScenario.For(program)` | Create a runner. Calls `Init` to establish starting state. |
| `.Dispatch(msg)`           | Advance state by one message.                              |
| `.Dispatch(msg, assert)`   | Advance and assert on the `TransitionResult`.              |
| `.AssertState(assert)`     | Assert on the current accumulated state.                   |
| `.State`                   | Current state after all dispatched messages.               |
| `.History`                 | Full list of `TransitionResult` entries, including `Init`. |

All methods return the runner for chaining. `History` includes the `Init` result
at index 0, followed by one entry per `Dispatch` call.

## Related Packages

- **Pamba** - Framework-agnostic core: contracts, dispatch loop, command/subscription infrastructure.
- **Pamba.WinUI** - WinUI 3 integration with `DispatcherQueue`, `StateProjectionBase`, and timer subscription helpers.

## Licence

Apache License 2.0
