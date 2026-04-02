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

Calls `Update(msg, state)`, then `Validate`, then
`Subscriptions(newState)`. Returns a `TransitionResult` containing all outputs
for assertion. When `Validate` returns `Invalid`, the state reverts, commands are
dropped, and `CorrectionMessage` is set on the result. Does not throw.

```csharp
TransitionResult<AppState, Msg, Cmd, Sub> result =
    MvuTestRunner.UpdateAndValidate(program, currentState, new Msg.Increment());

Assert.Equal(1, result.State.Count);
Assert.Single(result.Commands);
Assert.IsType<Cmd.Persist>(result.Commands[0]);
Assert.Empty(result.Subscriptions);
```

### InitAndValidate

Calls `Init()`, then `Validate`, then `Subscriptions(initialState)`.

```csharp
TransitionResult<AppState, Msg, Cmd, Sub> result =
    MvuTestRunner.InitAndValidate(program);

Assert.Equal(0, result.State.Count);
Assert.Contains(result.Commands, c => c is Cmd.LoadPreferences);
```

### TransitionResult

```csharp
public sealed record TransitionResult<TState, TMsg, TCmd, TSub>(
    TState State,
    TMsg? Message,
    TMsg? CorrectionMessage,
    ImmutableArray<TCmd> Commands,
    ImmutableArray<TSub> Subscriptions);
```

All outputs in a single value. `CorrectionMessage` is non-null when the validator rejected the transition.
Commands and subscriptions are indexable - use `result.Commands[0]`, `Assert.Single`, `Assert.Empty`,
or the ergonomic extension properties `WasRejected`, `WasAccepted`, `HasCommands`, `HasSubscriptions`.

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

| Method                                       | Purpose                                                                       |
| -------------------------------------------- | ----------------------------------------------------------------------------- |
| `MvuScenario.For(program)`                   | Create a runner. Calls `Init` to establish starting state.                    |
| `MvuScenario.For(program, assertInit)`       | Create a runner and assert on the Init result.                                |
| `.Dispatch(msg)`                             | Advance state by one message.                                                 |
| `.Dispatch(msg, assert)`                     | Advance and assert on the `TransitionResult`.                                 |
| `.DispatchAll(msgs...)`                      | Advance by multiple messages in sequence (per-message validation, not batch). |
| `.DispatchWithCorrections(msg, maxDepth)`    | Advance and auto-process corrective messages from validation rejection.       |
| `.DispatchWithCorrections(msg, assert, max)` | Same with assertion on first transition.                                      |
| `.AssertState(assert)`                       | Assert on the current accumulated state.                                      |
| `.AssertHistory(assert)`                     | Assert on the full transition history.                                        |
| `.AssertLastTransition(assert)`              | Assert on the most recent transition.                                         |
| `.State`                                     | Current state after all dispatched messages.                                  |
| `.History`                                   | Immutable array of `TransitionResult` entries, including `Init`.              |

All methods return the runner for chaining. `History` includes the `Init` result
at index 0, followed by one entry per `Dispatch` call.

## Related Packages

- **Pamba** - Framework-agnostic core: contracts, dispatch loop, command/subscription infrastructure.
- **Pamba.WinUI** - WinUI 3 integration with `DispatcherQueue`, `StateProjectionBase`,
  timer/event subscription helpers, and command debouncer.

## Licence

Apache License 2.0
