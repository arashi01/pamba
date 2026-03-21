// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Diagnostics;
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
  private readonly DispatcherQueue _dispatcherQueue;
  private readonly DispatcherQueueTimer _timer;
  private readonly Func<TCmd, Exception, TMsg> _onError;

  private TCmd? _pendingCommand;
  private Dispatch<TMsg>? _pendingDispatch;
  private CancellationTokenSource? _pendingCts;
  private bool _flushed;

  /// <summary>
  /// Create a debouncer wrapping an inner command executor.
  /// </summary>
  /// <param name="delay">Debounce delay. Each new command resets the timer.</param>
  /// <param name="inner">The actual command executor to invoke after the delay.</param>
  /// <param name="dispatcherQueue">WinUI dispatcher queue for the timer.</param>
  /// <param name="onError">Maps a failed command and its exception to a message for dispatch.</param>
  public CommandDebouncer(
      TimeSpan delay,
      CommandExecutor<TCmd, TMsg> inner,
      DispatcherQueue dispatcherQueue,
      Func<TCmd, Exception, TMsg> onError)
  {
    ArgumentNullException.ThrowIfNull(dispatcherQueue);
    ArgumentNullException.ThrowIfNull(onError);
    _delay = delay;
    _inner = inner;
    _dispatcherQueue = dispatcherQueue;
    _onError = onError;
    _timer = dispatcherQueue.CreateTimer();
    _timer.Interval = delay;
    _timer.IsRepeating = false;
    _timer.Tick += OnTimerTick;
  }

  /// <summary>
  /// Schedule a command for debounced execution.
  /// Cancels any previously pending command.
  /// No-ops after <see cref="FlushAsync"/> or <see cref="Dispose"/>.
  /// </summary>
  public async ValueTask Execute(TCmd command, Dispatch<TMsg> dispatch, CancellationToken ct)
  {
    Debug.Assert(
        _dispatcherQueue.HasThreadAccess,
        "CommandDebouncer.Execute must be called on the dispatcher thread.");

    if (_flushed)
    {
      return;
    }

    _timer.Stop();

    if (_pendingCts is not null)
    {
      // ConfigureAwait(true): continuation must return to UI thread for DispatcherQueueTimer access
      await _pendingCts.CancelAsync().ConfigureAwait(true);
      _pendingCts.Dispose();
    }

    _pendingCommand = command;
    _pendingDispatch = dispatch;
    _pendingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

    _timer.Interval = _delay;
    _timer.Start();
  }

  /// <summary>
  /// Immediately execute any pending debounced command and stop the timer.
  /// Call during graceful shutdown to ensure pending work completes before disposal.
  /// After flushing, further <see cref="Execute"/> calls are rejected.
  /// </summary>
  public async ValueTask FlushAsync()
  {
    _timer.Stop();
    _flushed = true;

    if (_pendingCommand is null || _pendingDispatch is null || _pendingCts is null)
    {
      return;
    }

    TCmd cmd = _pendingCommand;
    Dispatch<TMsg> dispatch = _pendingDispatch;
    CancellationTokenSource cts = _pendingCts;

    _pendingCommand = default;
    _pendingDispatch = null;
    _pendingCts = null;

    if (!cts.Token.IsCancellationRequested)
    {
      try
      {
        await _inner(cmd, dispatch, cts.Token).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
      {
        // Expected cancellation
      }
#pragma warning disable CA1031 // Runtime boundary: flush must route errors as typed messages via onError
      catch (Exception ex)
      {
        dispatch(_onError(cmd, ex));
      }
#pragma warning restore CA1031
    }

    cts.Dispose();
  }

  /// <inheritdoc />
  public void Dispose()
  {
    _timer.Stop();
    _flushed = true;
    _pendingCts?.Cancel();
    _pendingCts?.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <remarks>
  /// <c>async void</c> is required here: <see cref="DispatcherQueueTimer.Tick"/> is
  /// <c>EventHandler&lt;object&gt;</c> and cannot return <see cref="Task"/> or <see cref="ValueTask"/>.
  /// All exceptions from the inner executor are caught and routed via <c>_onError</c>.
  /// </remarks>
  private async void OnTimerTick(DispatcherQueueTimer sender, object args)
  {
    _timer.Stop();

    if (_pendingCommand is null || _pendingDispatch is null || _pendingCts is null)
    {
      return;
    }

    TCmd cmd = _pendingCommand;
    Dispatch<TMsg> dispatch = _pendingDispatch;
    CancellationTokenSource cts = _pendingCts;

    _pendingCommand = default;
    _pendingDispatch = null;
    _pendingCts = null;

    if (!cts.Token.IsCancellationRequested)
    {
      try
      {
        await _inner(cmd, dispatch, cts.Token).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
      {
        // Expected cancellation during debounce reset or disposal
      }
#pragma warning disable CA1031 // Runtime boundary: async void event handler; exception routed as typed message via onError
      catch (Exception ex)
      {
        dispatch(_onError(cmd, ex));
      }
#pragma warning restore CA1031
    }

    cts.Dispose();
  }
}
