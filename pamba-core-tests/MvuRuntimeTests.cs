// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Pamba.Tests;

/// <summary>
/// Integration tests for <see cref="MvuRuntime{TState, TMsg, TCmd, TSub}"/>
/// exercising the full dispatch loop via the <see cref="MvuRuntimeBuilder"/>.
/// Uses a synchronous dispatcher so messages process immediately.
/// </summary>
public sealed class MvuRuntimeTests
{
  private sealed record TestState(int Count, bool SubActive);

  private abstract record TestMsg
  {
    internal sealed record Increment : TestMsg;
    internal sealed record ActivateSub : TestMsg;
    internal sealed record DeactivateSub : TestMsg;
    internal sealed record SetValue(int Value) : TestMsg;
    internal sealed record CommandErrored(string Detail) : TestMsg;
    internal sealed record RuntimeErrored(string Detail) : TestMsg;
    internal sealed record ValidationRejected : TestMsg;
  }

  private abstract record TestCmd
  {
    internal sealed record Save(int Value) : TestCmd;
  }

  private sealed record TestSub(SubscriptionKey Key) : ISubscription<TestMsg>;

  private sealed class TrackingDisposable : IDisposable
  {
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
  }

  private static MvuProgram<TestState, TestMsg, TestCmd, TestSub> CreateProgram(
      Func<TestState, ValidationResult<TestState, TestMsg>>? validate = null)
  {
    return new MvuProgram<TestState, TestMsg, TestCmd, TestSub>
    {
      Init = () => (new TestState(0, false), []),
      Update = (msg, state) => msg switch
      {
        TestMsg.Increment => (new TestState(state.Count + 1, state.SubActive), [new TestCmd.Save(state.Count + 1)]),
        TestMsg.ActivateSub => (state with { SubActive = true }, []),
        TestMsg.DeactivateSub => (state with { SubActive = false }, []),
        TestMsg.SetValue v => (new TestState(v.Value, state.SubActive), []),
        TestMsg.CommandErrored => (state, []),
        TestMsg.RuntimeErrored => (state, []),
        TestMsg.ValidationRejected => (state, []),
        _ => (state, [])
      },
      Subscriptions = state => state.SubActive
          ? [new TestSub(new SubscriptionKey { Value = "ticker" })]
          : [],
      OnCommandError = (_, ex) => new TestMsg.CommandErrored(ex.Message),
      OnRuntimeError = err => new TestMsg.RuntimeErrored(err.ToString()),
      Validate = validate
    };
  }

  private static MvuRuntime<TestState, TestMsg, TestCmd, TestSub> StartRuntime(
      MvuProgram<TestState, TestMsg, TestCmd, TestSub> program,
      CommandExecutor<TestCmd, TestMsg> executor,
      SubscriptionStarter<TestSub, TestMsg> starter,
      Action<TestState>? onInit = null,
      Action<TestState, TestState>? onStateChanged = null)
  {
    Func<Action, bool> dispatcher = action => { action(); return true; };

    IRuntimeNeedsDispatcher<TestState, TestMsg, TestCmd, TestSub> withSubs = MvuRuntimeBuilder
        .Create(program)
        .WithCommandExecutor(executor)
        .WithSubscriptionStarter(starter);

    if (onInit is not null && onStateChanged is not null)
    {
      return withSubs.WithDispatcher(dispatcher, onInit, onStateChanged).Start();
    }

    if (onStateChanged is not null)
    {
      return withSubs.WithDispatcher(dispatcher, onStateChanged).Start();
    }

    return withSubs.WithDispatcher(dispatcher).Start();
  }

  private static ValueTask NoOpExecutor(TestCmd cmd, Dispatch<TestMsg> dispatch, CancellationToken ct) =>
      ValueTask.CompletedTask;

  private static IDisposable NoOpStarter(TestSub sub, Dispatch<TestMsg> dispatch) =>
      new TrackingDisposable();

  [Fact]
  public void Init_sets_state_and_executes_startup_commands()
  {
    List<TestCmd> executedCmds = [];
    ValueTask TrackingExecutor(TestCmd cmd, Dispatch<TestMsg> dispatch, CancellationToken ct)
    {
      executedCmds.Add(cmd);
      return ValueTask.CompletedTask;
    }

    MvuProgram<TestState, TestMsg, TestCmd, TestSub> program = new()
    {
      Init = () => (new TestState(42, false), [new TestCmd.Save(42)]),
      Update = (_, state) => (state, []),
      Subscriptions = _ => [],
      OnCommandError = (_, ex) => new TestMsg.CommandErrored(ex.Message),
      OnRuntimeError = err => new TestMsg.RuntimeErrored(err.ToString())
    };

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(program, TrackingExecutor, NoOpStarter);

    Assert.Equal(42, runtime.State.Count);
    Assert.Single(executedCmds);
    Assert.Equal(new TestCmd.Save(42), executedCmds[0]);
  }

  [Fact]
  public void Dispatch_processes_message_and_updates_state()
  {
    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgram(), NoOpExecutor, NoOpStarter);

    runtime.Dispatch(new TestMsg.Increment());

    Assert.Equal(1, runtime.State.Count);
  }

  [Fact]
  public void Dispatch_executes_returned_commands()
  {
    List<TestCmd> executedCmds = [];
    ValueTask TrackingExecutor(TestCmd cmd, Dispatch<TestMsg> dispatch, CancellationToken ct)
    {
      executedCmds.Add(cmd);
      return ValueTask.CompletedTask;
    }

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgram(), TrackingExecutor, NoOpStarter);

    runtime.Dispatch(new TestMsg.Increment());

    Assert.Single(executedCmds);
    Assert.Equal(new TestCmd.Save(1), executedCmds[0]);
  }

  [Fact]
  public void Dispatch_invokes_state_change_callback_with_old_and_new_state()
  {
    List<(TestState Old, TestState New)> callbacks = [];

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime = StartRuntime(
        CreateProgram(),
        NoOpExecutor,
        NoOpStarter,
        onStateChanged: (oldState, newState) => callbacks.Add((oldState, newState)));

    runtime.Dispatch(new TestMsg.Increment());

    Assert.Single(callbacks);
    Assert.Equal(0, callbacks[0].Old.Count);
    Assert.Equal(1, callbacks[0].New.Count);
  }

  [Fact]
  public void Validate_is_called_on_init_and_every_transition()
  {
    int validateCallCount = 0;
    MvuProgram<TestState, TestMsg, TestCmd, TestSub> program = CreateProgram(validate: state =>
    {
      validateCallCount++;
      return new ValidationResult<TestState, TestMsg>.Valid(state);
    });

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(program, NoOpExecutor, NoOpStarter);

    Assert.Equal(1, validateCallCount);

    runtime.Dispatch(new TestMsg.Increment());

    Assert.Equal(2, validateCallCount);
  }

  [Fact]
  public void Validate_returns_Invalid_reverts_state_and_dispatches_corrective_message()
  {
    MvuProgram<TestState, TestMsg, TestCmd, TestSub> program = CreateProgram(
        validate: state => state.Count < 0
            ? new ValidationResult<TestState, TestMsg>.Invalid(new TestMsg.ValidationRejected())
            : new ValidationResult<TestState, TestMsg>.Valid(state));

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(program, NoOpExecutor, NoOpStarter);

    // SetValue(-1) triggers validation rejection; state should revert to 0
    runtime.Dispatch(new TestMsg.SetValue(-1));

    Assert.Equal(0, runtime.State.Count);
  }

  [Fact]
  public void Dispatch_starts_subscriptions_when_state_activates_them()
  {
    List<string> startedKeys = [];
    IDisposable TrackingStarter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      startedKeys.Add(sub.Key.Value);
      return new TrackingDisposable();
    }

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgram(), NoOpExecutor, TrackingStarter);

    Assert.Empty(startedKeys);

    runtime.Dispatch(new TestMsg.ActivateSub());

    Assert.Single(startedKeys);
    Assert.Equal("ticker", startedKeys[0]);
  }

  [Fact]
  public void Dispatch_cancels_subscriptions_when_state_deactivates_them()
  {
    Dictionary<string, TrackingDisposable> handles = [];
    IDisposable TrackingStarter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      TrackingDisposable handle = new();
      handles[sub.Key.Value] = handle;
      return handle;
    }

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgram(), NoOpExecutor, TrackingStarter);

    runtime.Dispatch(new TestMsg.ActivateSub());
    Assert.False(handles["ticker"].IsDisposed);

    runtime.Dispatch(new TestMsg.DeactivateSub());
    Assert.True(handles["ticker"].IsDisposed);
  }

  [Fact]
  public void Command_executor_can_dispatch_messages_back_into_loop()
  {
    ValueTask DispatchingExecutor(TestCmd cmd, Dispatch<TestMsg> dispatch, CancellationToken ct)
    {
      if (cmd is TestCmd.Save s)
      {
        dispatch(new TestMsg.SetValue(s.Value * 10));
      }

      return ValueTask.CompletedTask;
    }

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgram(), DispatchingExecutor, NoOpStarter);

    runtime.Dispatch(new TestMsg.Increment());

    // Increment: Count=0 -> Count=1, produces Save(1)
    // Save(1) dispatches SetValue(10)
    // SetValue(10): Count -> 10
    Assert.Equal(10, runtime.State.Count);
  }

  [Fact]
  public void Dispatch_after_dispose_is_noop()
  {
    MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgram(), NoOpExecutor, NoOpStarter);

    runtime.Dispatch(new TestMsg.Increment());
    Assert.Equal(1, runtime.State.Count);

    runtime.Dispose();

    runtime.Dispatch(new TestMsg.Increment());
    Assert.Equal(1, runtime.State.Count);
  }

  [Fact]
  public void Dispose_cancels_all_active_subscriptions()
  {
    Dictionary<string, TrackingDisposable> handles = [];
    IDisposable TrackingStarter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      TrackingDisposable handle = new();
      handles[sub.Key.Value] = handle;
      return handle;
    }

    MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgram(), NoOpExecutor, TrackingStarter);

    runtime.Dispatch(new TestMsg.ActivateSub());
    Assert.False(handles["ticker"].IsDisposed);

    runtime.Dispose();
    Assert.True(handles["ticker"].IsDisposed);
  }

  [Fact]
  public void Command_error_is_dispatched_as_message()
  {
    MvuProgram<TestState, TestMsg, TestCmd, TestSub> program = new()
    {
      Init = () => (new TestState(0, false), [new TestCmd.Save(0)]),
      Update = (msg, state) => msg switch
      {
        TestMsg.CommandErrored => (new TestState(-1, state.SubActive), []),
        _ => (state, [])
      },
      Subscriptions = _ => [],
      OnCommandError = (_, ex) => new TestMsg.CommandErrored(ex.Message),
      OnRuntimeError = err => new TestMsg.RuntimeErrored(err.ToString())
    };

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime = StartRuntime(
        program,
        (_, _, _) => ValueTask.FromException(new InvalidOperationException("DB connection failed")),
        NoOpStarter);

    Assert.Equal(-1, runtime.State.Count);
  }

  [Fact]
  public void SubscriptionStarter_exception_routes_runtime_error_via_OnRuntimeError()
  {
    List<string> runtimeErrors = [];

    MvuProgram<TestState, TestMsg, TestCmd, TestSub> program = new()
    {
      Init = () => (new TestState(0, true), []),
      Update = (msg, state) => msg switch
      {
        TestMsg.RuntimeErrored e => (state with { SubActive = false }, []),
        _ => (state, [])
      },
      Subscriptions = state => state.SubActive
          ? [new TestSub(new SubscriptionKey { Value = "ticker" })]
          : [],
      OnCommandError = (_, ex) => new TestMsg.CommandErrored(ex.Message),
      OnRuntimeError = err => { runtimeErrors.Add(err.ToString()); return new TestMsg.RuntimeErrored(err.ToString()); }
    };

    IDisposable ThrowingStarter(TestSub sub, Dispatch<TestMsg> dispatch) =>
        throw new InvalidOperationException("Timer init failed");

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(program, NoOpExecutor, ThrowingStarter);

    Assert.Single(runtimeErrors);
    Assert.False(runtime.State.SubActive);
  }

  [Fact]
  public void Dispatch_skips_subscriptions_and_projection_when_state_unchanged()
  {
    List<(TestState Old, TestState New)> callbacks = [];
    int subscriptionsCalled = 0;

    MvuProgram<TestState, TestMsg, TestCmd, TestSub> program = new()
    {
      Init = () => (new TestState(0, false), []),
      Update = (_, state) => (state, []),
      Subscriptions = state =>
      {
        subscriptionsCalled++;
        return [];
      },
      OnCommandError = (_, ex) => new TestMsg.CommandErrored(ex.Message),
      OnRuntimeError = err => new TestMsg.RuntimeErrored(err.ToString())
    };

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime = StartRuntime(
        program,
        NoOpExecutor,
        NoOpStarter,
        onStateChanged: (old, @new) => callbacks.Add((old, @new)));

    int subscriptionsAfterInit = subscriptionsCalled;

    runtime.Dispatch(new TestMsg.Increment());

    Assert.Empty(callbacks);
    Assert.Equal(subscriptionsAfterInit, subscriptionsCalled);
  }

  [Fact]
  public void OnInit_callback_fires_with_initial_state()
  {
    TestState? capturedInit = null;

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime = StartRuntime(
        CreateProgram(),
        NoOpExecutor,
        NoOpStarter,
        onInit: state => capturedInit = state,
        onStateChanged: (_, _) => { });

    Assert.NotNull(capturedInit);
    Assert.Equal(0, capturedInit.Count);
    Assert.False(capturedInit.SubActive);
  }

  [Fact]
  public void Validate_rejection_corrective_message_effects_are_preserved()
  {
    // C1/C2 regression: corrective message that changes state must not be overwritten
    MvuProgram<TestState, TestMsg, TestCmd, TestSub> program = new()
    {
      Init = () => (new TestState(0, false), []),
      Update = (msg, state) => msg switch
      {
        TestMsg.SetValue v => (new TestState(v.Value, state.SubActive), []),
        TestMsg.ValidationRejected => (new TestState(99, state.SubActive), []),
        _ => (state, [])
      },
      Subscriptions = _ => [],
      OnCommandError = (_, ex) => new TestMsg.CommandErrored(ex.Message),
      OnRuntimeError = err => new TestMsg.RuntimeErrored(err.ToString()),
      Validate = state => state.Count < 0
          ? new ValidationResult<TestState, TestMsg>.Invalid(new TestMsg.ValidationRejected())
          : new ValidationResult<TestState, TestMsg>.Valid(state)
    };

    using var runtime = StartRuntime(program, NoOpExecutor, NoOpStarter);

    // SetValue(-1) triggers validation rejection; corrective handler sets Count=99
    runtime.Dispatch(new TestMsg.SetValue(-1));

    Assert.Equal(99, runtime.State.Count);
  }

  [Fact]
  public void Dispatch_rejected_does_not_corrupt_state()
  {
    // C3 regression: when enqueue returns false, state must not be mutated
    MvuProgram<TestState, TestMsg, TestCmd, TestSub> program = CreateProgram();
    List<string> runtimeErrors = [];

    MvuProgram<TestState, TestMsg, TestCmd, TestSub> programWithTracking = new()
    {
      Init = program.Init,
      Update = program.Update,
      Subscriptions = program.Subscriptions,
      OnCommandError = program.OnCommandError,
      OnRuntimeError = err =>
      {
        runtimeErrors.Add(err.ToString());
        return new TestMsg.RuntimeErrored(err.ToString());
      },
      Validate = program.Validate
    };

    // Use a dispatcher that can be switched to rejecting mode
    bool rejectAll = false;
    Func<Action, bool> rejectingDispatcher = action =>
    {
      if (rejectAll)
      {
        return false;
      }

      action();
      return true;
    };

    using var runtime = MvuRuntimeBuilder
        .Create(programWithTracking)
        .WithCommandExecutor(NoOpExecutor)
        .WithSubscriptionStarter(NoOpStarter)
        .WithDispatcher(rejectingDispatcher)
        .Start();

    // Switch to rejecting mode after startup
    rejectAll = true;
    runtime.Dispatch(new TestMsg.Increment());

    // OnRuntimeError should have been called with DispatchRejected
    Assert.Single(runtimeErrors);
  }

  [Fact]
  public void Subscription_parameter_change_restarts_subscription()
  {
    // C4 regression: same key, different data must restart subscription
    Dictionary<string, List<int>> startedValues = [];

    // Use a sub type that carries data beyond the key
    MvuProgram<TestState, TestMsg, TestCmd, TestSubWithData> programWithData = new()
    {
      Init = () => (new TestState(0, false), []),
      Update = (msg, state) => msg switch
      {
        TestMsg.Increment => (new TestState(state.Count + 1, true), []),
        _ => (state, [])
      },
      Subscriptions = state => state.SubActive
          ? [new TestSubWithData(new SubscriptionKey { Value = "ticker" }, state.Count)]
          : [],
      OnCommandError = (_, ex) => new TestMsg.CommandErrored(ex.Message),
      OnRuntimeError = err => new TestMsg.RuntimeErrored(err.ToString())
    };

    Func<Action, bool> dispatcher = action => { action(); return true; };

    using var runtime = MvuRuntimeBuilder
        .Create(programWithData)
        .WithCommandExecutor(
            (TestCmd cmd, Dispatch<TestMsg> dispatch, CancellationToken ct) => ValueTask.CompletedTask)
        .WithSubscriptionStarter(
            (TestSubWithData sub, Dispatch<TestMsg> dispatch) =>
            {
              if (!startedValues.TryGetValue(sub.Key.Value, out var list))
              {
                list = [];
                startedValues[sub.Key.Value] = list;
              }

              list.Add(sub.Interval);
              return new TrackingDisposable();
            })
        .WithDispatcher(dispatcher)
        .Start();

    // First Increment: Count=1, starts subscription with Interval=1
    runtime.Dispatch(new TestMsg.Increment());
    Assert.Single(startedValues["ticker"]);
    Assert.Equal(1, startedValues["ticker"][0]);

    // Second Increment: Count=2, same key but different Interval=2 -> restart
    runtime.Dispatch(new TestMsg.Increment());
    Assert.Equal(2, startedValues["ticker"].Count);
    Assert.Equal(2, startedValues["ticker"][1]);
  }

  [Fact]
  public void OnCommandError_throwing_escalates_to_OnRuntimeError_with_ErrorHandlerFailed()
  {
    List<PambaError> runtimeErrors = [];

    MvuProgram<TestState, TestMsg, TestCmd, TestSub> program = new()
    {
      Init = () => (new TestState(0, false), [new TestCmd.Save(0)]),
      Update = (msg, state) => msg switch
      {
        TestMsg.RuntimeErrored => (new TestState(-1, state.SubActive), []),
        _ => (state, [])
      },
      Subscriptions = _ => [],
      OnCommandError = (_, _) => throw new InvalidOperationException("Handler bug"),
      OnRuntimeError = err =>
      {
        runtimeErrors.Add(err);
        return new TestMsg.RuntimeErrored(err.ToString());
      }
    };

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime = StartRuntime(
        program,
        (_, _, _) => ValueTask.FromException(new InvalidOperationException("Command failed")),
        NoOpStarter);

    // OnCommandError threw, so ErrorHandlerFailed should have been routed via OnRuntimeError
    Assert.Single(runtimeErrors);
    Assert.IsType<PambaError.ErrorHandlerFailed>(runtimeErrors[0]);
    Assert.Equal(-1, runtime.State.Count);
  }

  [Fact]
  public void OnRuntimeError_throwing_triggers_debug_fail_but_runtime_survives()
  {
    MvuProgram<TestState, TestMsg, TestCmd, TestSub> program = new()
    {
      Init = () => (new TestState(0, true), []),
      Update = (msg, state) => msg switch
      {
        _ => (state with { SubActive = false }, [])
      },
      Subscriptions = state => state.SubActive
          ? [new TestSub(new SubscriptionKey { Value = "ticker" })]
          : [],
      OnCommandError = (_, _) => throw new InvalidOperationException("Handler bug"),
      OnRuntimeError = _ => throw new InvalidOperationException("Runtime handler bug")
    };

    IDisposable ThrowingStarter(TestSub sub, Dispatch<TestMsg> dispatch) =>
        throw new InvalidOperationException("Starter failed");

    // Temporarily suppress Debug.Fail so it does not throw DebugAssertException in the test host
    using var _ = new SuppressDebugAsserts();

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(program, NoOpExecutor, ThrowingStarter);

    Assert.Equal(0, runtime.State.Count);
  }

  /// <summary>
  /// Temporarily replaces trace listeners to suppress Debug.Fail
  /// assertions during a test, restoring the original listeners on dispose.
  /// </summary>
  private sealed class SuppressDebugAsserts : IDisposable
  {
    private readonly System.Diagnostics.TraceListener[] _original;

    public SuppressDebugAsserts()
    {
      _original = new System.Diagnostics.TraceListener[System.Diagnostics.Trace.Listeners.Count];
      System.Diagnostics.Trace.Listeners.CopyTo(_original, 0);
      System.Diagnostics.Trace.Listeners.Clear();
    }

    public void Dispose()
    {
      System.Diagnostics.Trace.Listeners.Clear();
      System.Diagnostics.Trace.Listeners.AddRange(_original);
    }
  }

  private sealed record TestSubWithData(SubscriptionKey Key, int Interval) : ISubscription<TestMsg>;
}
