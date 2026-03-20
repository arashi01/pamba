// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;

namespace Pamba.WinUI;

/// <summary>
/// Disposable handle that invokes an unsubscribe action on disposal.
/// Used by event-based subscription helpers to detach event handlers.
/// </summary>
internal sealed class EventSubscriptionHandle(Action unsubscribe) : IDisposable
{
  public void Dispose() => unsubscribe();
}
