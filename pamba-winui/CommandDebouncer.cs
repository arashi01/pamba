// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace Pamba.WinUI;

/// <summary>
/// Debounces high-frequency command execution.
/// Each new invocation cancels the previous pending execution.
/// Uses <see cref="DispatcherQueueTimer"/> for UI-thread-safe debouncing.
/// </summary>
/// <typeparam name="TCmd">Command type.</typeparam>
/// <typeparam name="TMsg">Message type.</typeparam>
public sealed class CommandDebouncer<TCmd, TMsg> : IDisposable
    where TCmd : notnull
{
  private readonly TimeSpan _delay;
  private readonly CommandExecutor<TCmd, TMsg> _inner;
  private readonly DispatcherQueueTimer _timer;

  private TCmd? _pendingCommand;
  private Dispatch<TMsg>? _pendingDispatch;
  private CancellationTokenSource? _pendingCts;

  /// <summary>
  /// Create a debouncer wrapping an inner command executor.
  /// </summary>
  /// <param name="delay">Debounce delay. Each new command resets the timer.</param>
  /// <param name="inner">The actual command executor to invoke after the delay.</param>
  /// <param name="dispatcherQueue">WinUI dispatcher queue for the timer.</param>
  public CommandDebouncer(
      TimeSpan delay,
      CommandExecutor<TCmd, TMsg> inner,
      DispatcherQueue dispatcherQueue)
  {
    ArgumentNullException.ThrowIfNull(dispatcherQueue);
    _delay = delay;
    _inner = inner;
    _timer = dispatcherQueue.CreateTimer();
    _timer.Interval = delay;
    _timer.IsRepeating = false;
    _timer.Tick += OnTimerTick;
  }

  /// <summary>
  /// Schedule a command for debounced execution.
  /// Cancels any previously pending command.
  /// </summary>
  public async Task Execute(TCmd command, Dispatch<TMsg> dispatch, CancellationToken ct)
  {
    _timer.Stop();

    if (_pendingCts is not null)
    {
      await _pendingCts.CancelAsync().ConfigureAwait(false);
      _pendingCts.Dispose();
    }

    _pendingCommand = command;
    _pendingDispatch = dispatch;
    _pendingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

    _timer.Interval = _delay;
    _timer.Start();
  }

  /// <inheritdoc />
  public void Dispose()
  {
    _timer.Stop();
    _pendingCts?.Cancel();
    _pendingCts?.Dispose();
  }

  private async void OnTimerTick(DispatcherQueueTimer sender, object args)
  {
    _timer.Stop();

    if (_pendingCommand is null || _pendingDispatch is null || _pendingCts is null)
    {
      return;
    }

    var cmd = _pendingCommand;
    var dispatch = _pendingDispatch;
    var cts = _pendingCts;

    _pendingCommand = default;
    _pendingDispatch = null;
    _pendingCts = null;

    if (!cts.Token.IsCancellationRequested)
    {
      await _inner(cmd, dispatch, cts.Token).ConfigureAwait(false);
    }

    cts.Dispose();
  }
}
