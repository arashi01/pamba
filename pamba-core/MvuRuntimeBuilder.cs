// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;

namespace Pamba;

/// <summary>
/// Entry point for constructing an <see cref="MvuRuntime{TState, TMsg, TCmd, TSub}"/>.
/// </summary>
public static class MvuRuntimeBuilder
{
  /// <summary>
  /// Begin constructing a runtime for the given programme.
  /// </summary>
  public static IRuntimeWithProgramme<TState, TMsg, TCmd, TSub>
      Create<TState, TMsg, TCmd, TSub>(
          MvuProgramme<TState, TMsg, TCmd, TSub> programme)
      where TState : IEquatable<TState>
      where TMsg : notnull
      where TCmd : notnull
      where TSub : IEquatable<TSub>, ISubscription<TMsg>
  =>
      new Builder<TState, TMsg, TCmd, TSub>(programme);

  private sealed class Builder<TState, TMsg, TCmd, TSub>
      : IRuntimeWithProgramme<TState, TMsg, TCmd, TSub>,
        IRuntimeWithExecutor<TState, TMsg, TCmd, TSub>,
        IRuntimeWithSubscriptions<TState, TMsg, TCmd, TSub>,
        IRuntimeReady<TState, TMsg, TCmd, TSub>
      where TState : IEquatable<TState>
      where TMsg : notnull
      where TCmd : notnull
      where TSub : IEquatable<TSub>, ISubscription<TMsg>
  {
    private readonly MvuProgramme<TState, TMsg, TCmd, TSub> _programme;
    private CommandExecutor<TCmd, TMsg>? _commandExecutor;
    private SubscriptionStarter<TSub, TMsg>? _subscriptionStarter;
    private Action<Action>? _enqueue;
    private Action<TState>? _onInit;
    private Action<TState, TState>? _onStateChanged;

    internal Builder(MvuProgramme<TState, TMsg, TCmd, TSub> programme)
    {
      _programme = programme;
    }

    public IRuntimeWithExecutor<TState, TMsg, TCmd, TSub>
        WithCommandExecutor(CommandExecutor<TCmd, TMsg> executor)
    {
      _commandExecutor = executor;
      return this;
    }

    public IRuntimeWithSubscriptions<TState, TMsg, TCmd, TSub>
        WithSubscriptionStarter(SubscriptionStarter<TSub, TMsg> starter)
    {
      _subscriptionStarter = starter;
      return this;
    }

    public IRuntimeReady<TState, TMsg, TCmd, TSub>
        WithDispatcher(Action<Action> enqueue)
    {
      _enqueue = enqueue;
      return this;
    }

    public IRuntimeReady<TState, TMsg, TCmd, TSub>
        WithDispatcher(Action<Action> enqueue, Action<TState, TState> onStateChanged)
    {
      _enqueue = enqueue;
      _onStateChanged = onStateChanged;
      return this;
    }

    public IRuntimeReady<TState, TMsg, TCmd, TSub>
        WithDispatcher(
            Action<Action> enqueue,
            Action<TState> onInit,
            Action<TState, TState> onStateChanged)
    {
      _enqueue = enqueue;
      _onInit = onInit;
      _onStateChanged = onStateChanged;
      return this;
    }

    public MvuRuntime<TState, TMsg, TCmd, TSub> Start()
    {
      return new MvuRuntime<TState, TMsg, TCmd, TSub>(
          _programme,
          _commandExecutor!,
          _subscriptionStarter!,
          _enqueue!,
          _onInit,
          _onStateChanged);
    }
  }
}

/// <summary>Step 1: programme provided, needs command executor.</summary>
public interface IRuntimeWithProgramme<TState, TMsg, TCmd, TSub>
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  /// <summary>Provide the command executor (Shell concern).</summary>
  public IRuntimeWithExecutor<TState, TMsg, TCmd, TSub>
      WithCommandExecutor(CommandExecutor<TCmd, TMsg> executor);
}

/// <summary>Step 2: executor provided, needs subscription starter.</summary>
public interface IRuntimeWithExecutor<TState, TMsg, TCmd, TSub>
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  /// <summary>Provide the subscription starter (Shell concern).</summary>
  public IRuntimeWithSubscriptions<TState, TMsg, TCmd, TSub>
      WithSubscriptionStarter(SubscriptionStarter<TSub, TMsg> starter);
}

/// <summary>Step 3: subscriptions provided, needs thread dispatcher.</summary>
public interface IRuntimeWithSubscriptions<TState, TMsg, TCmd, TSub>
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  /// <summary>Provide the thread dispatcher for FIFO message ordering.</summary>
  public IRuntimeReady<TState, TMsg, TCmd, TSub>
      WithDispatcher(Action<Action> enqueue);

  /// <summary>Provide the thread dispatcher with a state change callback for projection.</summary>
  public IRuntimeReady<TState, TMsg, TCmd, TSub>
      WithDispatcher(Action<Action> enqueue, Action<TState, TState> onStateChanged);

  /// <summary>Provide the thread dispatcher with init and state change callbacks for projection.</summary>
  public IRuntimeReady<TState, TMsg, TCmd, TSub>
      WithDispatcher(
          Action<Action> enqueue,
          Action<TState> onInit,
          Action<TState, TState> onStateChanged);
}

/// <summary>Step 4: all dependencies provided, ready to start.</summary>
public interface IRuntimeReady<TState, TMsg, TCmd, TSub>
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  /// <summary>
  /// Start the runtime. Calls Init, validates, projects initial state, and executes startup commands.
  /// </summary>
  public MvuRuntime<TState, TMsg, TCmd, TSub> Start();
}
