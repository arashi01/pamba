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
  }

  private abstract record TestCmd
  {
    internal sealed record Save(int Value) : TestCmd;
  }

  private sealed record TestSub(string Key) : ISubscription<TestMsg>;

  private sealed class TrackingDisposable : IDisposable
  {
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
  }

  private static MvuProgramme<TestState, TestMsg, TestCmd, TestSub> CreateProgramme(
      Func<TestState, TestState>? validate = null)
  {
    return new MvuProgramme<TestState, TestMsg, TestCmd, TestSub>
    {
      Init = () => (new TestState(0, false), []),
      Update = (msg, state) => msg switch
      {
        TestMsg.Increment => (new TestState(state.Count + 1, state.SubActive), [new TestCmd.Save(state.Count + 1)]),
        TestMsg.ActivateSub => (state with { SubActive = true }, []),
        TestMsg.DeactivateSub => (state with { SubActive = false }, []),
        TestMsg.SetValue v => (new TestState(v.Value, state.SubActive), []),
        TestMsg.CommandErrored => (state, []),
        _ => (state, [])
      },
      Subscriptions = state => state.SubActive
          ? [new TestSub("ticker")]
          : [],
      OnCommandError = (cmd, ex) => new TestMsg.CommandErrored(ex.Message),
      Validate = validate
    };
  }

  private static MvuRuntime<TestState, TestMsg, TestCmd, TestSub> StartRuntime(
      MvuProgramme<TestState, TestMsg, TestCmd, TestSub> programme,
      CommandExecutor<TestCmd, TestMsg> executor,
      SubscriptionStarter<TestSub, TestMsg> starter,
      Action<TestState>? onInit = null,
      Action<TestState, TestState>? onStateChanged = null)
  {
    IRuntimeWithSubscriptions<TestState, TestMsg, TestCmd, TestSub> withSubs = MvuRuntimeBuilder
        .Create(programme)
        .WithCommandExecutor(executor)
        .WithSubscriptionStarter(starter);

    if (onInit is not null && onStateChanged is not null)
    {
      return withSubs.WithDispatcher(action => action(), onInit, onStateChanged).Start();
    }

    if (onStateChanged is not null)
    {
      return withSubs.WithDispatcher(action => action(), onStateChanged).Start();
    }

    return withSubs.WithDispatcher(action => action()).Start();
  }

  private static Task NoOpExecutor(TestCmd cmd, Dispatch<TestMsg> dispatch, CancellationToken ct) =>
      Task.CompletedTask;

  private static IDisposable NoOpStarter(TestSub sub, Dispatch<TestMsg> dispatch) =>
      new TrackingDisposable();

  [Fact]
  public void Init_sets_state_and_executes_startup_commands()
  {
    List<TestCmd> executedCmds = [];
    Task TrackingExecutor(TestCmd cmd, Dispatch<TestMsg> dispatch, CancellationToken ct)
    {
      executedCmds.Add(cmd);
      return Task.CompletedTask;
    }

    MvuProgramme<TestState, TestMsg, TestCmd, TestSub> programme = new()
    {
      Init = () => (new TestState(42, false), [new TestCmd.Save(42)]),
      Update = (_, state) => (state, []),
      Subscriptions = _ => [],
      OnCommandError = (cmd, ex) => new TestMsg.CommandErrored(ex.Message)
    };

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(programme, TrackingExecutor, NoOpStarter);

    Assert.Equal(42, runtime.State.Count);
    Assert.Single(executedCmds);
    Assert.Equal(new TestCmd.Save(42), executedCmds[0]);
  }

  [Fact]
  public void Dispatch_processes_message_and_updates_state()
  {
    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgramme(), NoOpExecutor, NoOpStarter);

    runtime.Dispatch(new TestMsg.Increment());

    Assert.Equal(1, runtime.State.Count);
  }

  [Fact]
  public void Dispatch_executes_returned_commands()
  {
    List<TestCmd> executedCmds = [];
    Task TrackingExecutor(TestCmd cmd, Dispatch<TestMsg> dispatch, CancellationToken ct)
    {
      executedCmds.Add(cmd);
      return Task.CompletedTask;
    }

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgramme(), TrackingExecutor, NoOpStarter);

    runtime.Dispatch(new TestMsg.Increment());

    Assert.Single(executedCmds);
    Assert.Equal(new TestCmd.Save(1), executedCmds[0]);
  }

  [Fact]
  public void Dispatch_invokes_state_change_callback_with_old_and_new_state()
  {
    List<(TestState Old, TestState New)> callbacks = [];

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime = StartRuntime(
        CreateProgramme(),
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
    MvuProgramme<TestState, TestMsg, TestCmd, TestSub> programme = CreateProgramme(validate: state =>
    {
      validateCallCount++;
      return state;
    });

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(programme, NoOpExecutor, NoOpStarter);

    Assert.Equal(1, validateCallCount);

    runtime.Dispatch(new TestMsg.Increment());

    Assert.Equal(2, validateCallCount);
  }

  [Fact]
  public void Dispatch_starts_subscriptions_when_state_activates_them()
  {
    List<string> startedKeys = [];
    IDisposable TrackingStarter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      startedKeys.Add(sub.Key);
      return new TrackingDisposable();
    }

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgramme(), NoOpExecutor, TrackingStarter);

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
      handles[sub.Key] = handle;
      return handle;
    }

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgramme(), NoOpExecutor, TrackingStarter);

    runtime.Dispatch(new TestMsg.ActivateSub());
    Assert.False(handles["ticker"].IsDisposed);

    runtime.Dispatch(new TestMsg.DeactivateSub());
    Assert.True(handles["ticker"].IsDisposed);
  }

  [Fact]
  public void Command_executor_can_dispatch_messages_back_into_loop()
  {
    Task DispatchingExecutor(TestCmd cmd, Dispatch<TestMsg> dispatch, CancellationToken ct)
    {
      if (cmd is TestCmd.Save s)
      {
        dispatch(new TestMsg.SetValue(s.Value * 10));
      }

      return Task.CompletedTask;
    }

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgramme(), DispatchingExecutor, NoOpStarter);

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
        StartRuntime(CreateProgramme(), NoOpExecutor, NoOpStarter);

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
      handles[sub.Key] = handle;
      return handle;
    }

    MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime =
        StartRuntime(CreateProgramme(), NoOpExecutor, TrackingStarter);

    runtime.Dispatch(new TestMsg.ActivateSub());
    Assert.False(handles["ticker"].IsDisposed);

    runtime.Dispose();
    Assert.True(handles["ticker"].IsDisposed);
  }

  [Fact]
  public void Command_error_is_dispatched_as_message()
  {
    MvuProgramme<TestState, TestMsg, TestCmd, TestSub> programme = new()
    {
      Init = () => (new TestState(0, false), [new TestCmd.Save(0)]),
      Update = (msg, state) => msg switch
      {
        TestMsg.CommandErrored e => (new TestState(-1, state.SubActive), []),
        _ => (state, [])
      },
      Subscriptions = _ => [],
      OnCommandError = (cmd, ex) => new TestMsg.CommandErrored(ex.Message)
    };

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime = StartRuntime(
        programme,
        (cmd, dispatch, ct) => Task.FromException(new InvalidOperationException("DB connection failed")),
        NoOpStarter);

    Assert.Equal(-1, runtime.State.Count);
  }

  [Fact]
  public void Dispatch_skips_subscriptions_and_projection_when_state_unchanged()
  {
    List<(TestState Old, TestState New)> callbacks = [];
    int subscriptionsCalled = 0;

    MvuProgramme<TestState, TestMsg, TestCmd, TestSub> programme = new()
    {
      Init = () => (new TestState(0, false), []),
      Update = (msg, state) => (state, []),
      Subscriptions = state =>
      {
        subscriptionsCalled++;
        return [];
      },
      OnCommandError = (cmd, ex) => new TestMsg.CommandErrored(ex.Message)
    };

    using MvuRuntime<TestState, TestMsg, TestCmd, TestSub> runtime = StartRuntime(
        programme,
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
        CreateProgramme(),
        NoOpExecutor,
        NoOpStarter,
        onInit: state => capturedInit = state,
        onStateChanged: (_, _) => { });

    Assert.NotNull(capturedInit);
    Assert.Equal(0, capturedInit.Count);
    Assert.False(capturedInit.SubActive);
  }
}
