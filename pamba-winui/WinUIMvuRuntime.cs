// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using Microsoft.UI.Dispatching;

namespace Pamba.WinUI;

/// <summary>
/// Creates an <see cref="MvuRuntime{TState, TMsg, TCmd, TSub}"/> wired to
/// WinUI's <see cref="DispatcherQueue"/> for FIFO message ordering on the UI thread.
/// </summary>
public static class WinUIMvuRuntime
{
  /// <summary>
  /// Begin constructing a WinUI-backed MVU runtime.
  /// Pre-wires the thread dispatcher to <paramref name="dispatcherQueue"/>.
  /// </summary>
  public static IWinUIRuntimeWithProgram<TState, TMsg, TCmd, TSub>
      Create<TState, TMsg, TCmd, TSub>(
          MvuProgram<TState, TMsg, TCmd, TSub> program,
          DispatcherQueue dispatcherQueue)
      where TState : IEquatable<TState>
      where TMsg : notnull
      where TCmd : notnull
      where TSub : IEquatable<TSub>, ISubscription<TMsg>
  {
    ArgumentNullException.ThrowIfNull(dispatcherQueue);
    return new WinUIBuilder<TState, TMsg, TCmd, TSub>(program, dispatcherQueue);
  }

  private sealed class WinUIBuilder<TState, TMsg, TCmd, TSub>
      : IWinUIRuntimeWithProgram<TState, TMsg, TCmd, TSub>,
        IWinUIRuntimeWithExecutor<TState, TMsg, TCmd, TSub>,
        IWinUIRuntimeWithSubscriptions<TState, TMsg, TCmd, TSub>,
        IWinUIRuntimeReady<TState, TMsg, TCmd, TSub>
      where TState : IEquatable<TState>
      where TMsg : notnull
      where TCmd : notnull
      where TSub : IEquatable<TSub>, ISubscription<TMsg>
  {
    private readonly MvuProgram<TState, TMsg, TCmd, TSub> _program;
    private readonly DispatcherQueue _dispatcherQueue;
    private CommandExecutor<TCmd, TMsg>? _commandExecutor;
    private SubscriptionStarter<TSub, TMsg>? _subscriptionStarter;
    private Action<TState>? _onInit;
    private Action<TState, TState>? _onStateChanged;

    internal WinUIBuilder(
        MvuProgram<TState, TMsg, TCmd, TSub> program,
        DispatcherQueue dispatcherQueue)
    {
      _program = program;
      _dispatcherQueue = dispatcherQueue;
    }

    public IWinUIRuntimeWithExecutor<TState, TMsg, TCmd, TSub>
        WithCommandExecutor(CommandExecutor<TCmd, TMsg> executor)
    {
      ArgumentNullException.ThrowIfNull(executor);
      _commandExecutor = executor;
      return this;
    }

    public IWinUIRuntimeWithSubscriptions<TState, TMsg, TCmd, TSub>
        WithSubscriptionStarter(SubscriptionStarter<TSub, TMsg> starter)
    {
      ArgumentNullException.ThrowIfNull(starter);
      _subscriptionStarter = starter;
      return this;
    }

    public IWinUIRuntimeReady<TState, TMsg, TCmd, TSub>
        WithProjection(Action<TState, TState> onStateChanged)
    {
      ArgumentNullException.ThrowIfNull(onStateChanged);
      _onStateChanged = onStateChanged;
      return this;
    }

    public IWinUIRuntimeReady<TState, TMsg, TCmd, TSub>
        WithProjection(
            Action<TState> onInit,
            Action<TState, TState> onStateChanged)
    {
      ArgumentNullException.ThrowIfNull(onInit);
      ArgumentNullException.ThrowIfNull(onStateChanged);
      _onInit = onInit;
      _onStateChanged = onStateChanged;
      return this;
    }

    public MvuRuntime<TState, TMsg, TCmd, TSub> Start()
    {
      // TryEnqueue returns bool: true if the action was enqueued, false if the queue has shut down.
      // The bool is propagated to MvuRuntime which routes false as PambaError.DispatchRejected.
      Func<Action, bool> enqueue = action => _dispatcherQueue.TryEnqueue(() => action());

      IRuntimeWithSubscriptions<TState, TMsg, TCmd, TSub> withSubs = MvuRuntimeBuilder
          .Create(_program)
          .WithCommandExecutor(_commandExecutor!)
          .WithSubscriptionStarter(_subscriptionStarter!);

      IRuntimeReady<TState, TMsg, TCmd, TSub> ready;

      if (_onInit is not null)
      {
        ready = withSubs.WithDispatcher(enqueue, _onInit, _onStateChanged!);
      }
      else if (_onStateChanged is not null)
      {
        ready = withSubs.WithDispatcher(enqueue, _onStateChanged);
      }
      else
      {
        ready = withSubs.WithDispatcher(enqueue);
      }

      return ready.Start();
    }
  }
}

/// <summary>Step 1: program and dispatcher provided, needs command executor.</summary>
public interface IWinUIRuntimeWithProgram<TState, TMsg, TCmd, TSub>
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  /// <summary>Provide the command executor (Shell concern).</summary>
  public IWinUIRuntimeWithExecutor<TState, TMsg, TCmd, TSub>
      WithCommandExecutor(CommandExecutor<TCmd, TMsg> executor);
}

/// <summary>Step 2: executor provided, needs subscription starter.</summary>
public interface IWinUIRuntimeWithExecutor<TState, TMsg, TCmd, TSub>
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  /// <summary>Provide the subscription starter (Shell concern).</summary>
  public IWinUIRuntimeWithSubscriptions<TState, TMsg, TCmd, TSub>
      WithSubscriptionStarter(SubscriptionStarter<TSub, TMsg> starter);
}

/// <summary>Step 3: subscriptions provided, dispatcher pre-wired. Optionally add projection.</summary>
public interface IWinUIRuntimeWithSubscriptions<TState, TMsg, TCmd, TSub>
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  /// <summary>Provide a state change callback for projection.</summary>
  public IWinUIRuntimeReady<TState, TMsg, TCmd, TSub>
      WithProjection(Action<TState, TState> onStateChanged);

  /// <summary>Provide init and state change callbacks for projection.</summary>
  public IWinUIRuntimeReady<TState, TMsg, TCmd, TSub>
      WithProjection(
          Action<TState> onInit,
          Action<TState, TState> onStateChanged);

  /// <summary>Start without projection.</summary>
  public MvuRuntime<TState, TMsg, TCmd, TSub> Start();
}

/// <summary>Step 4: projection provided, ready to start.</summary>
public interface IWinUIRuntimeReady<TState, TMsg, TCmd, TSub>
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  /// <summary>
  /// Start the runtime. Calls Init, projects initial state, and executes startup commands.
  /// </summary>
  public MvuRuntime<TState, TMsg, TCmd, TSub> Start();
}
