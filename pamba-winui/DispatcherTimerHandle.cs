// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using Microsoft.UI.Dispatching;

namespace Pamba.WinUI;

/// <summary>
/// Disposable handle that stops a <see cref="DispatcherQueueTimer"/> on disposal.
/// Shared by <see cref="TimerSubscription"/> and <see cref="DelayedSubscription"/>.
/// </summary>
internal sealed class DispatcherTimerHandle(DispatcherQueueTimer timer) : IDisposable
{
  public void Dispose() => timer.Stop();
}
