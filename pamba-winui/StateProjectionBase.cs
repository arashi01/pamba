// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;

namespace Pamba.WinUI;

/// <summary>
/// Base class for state-to-UI projection with automatic diffing.
/// Subclass and register domain-specific projection segments in the constructor.
/// Each segment fires only when its selected sub-state changes by value equality.
/// </summary>
/// <typeparam name="TState">Immutable application state.</typeparam>
public abstract class StateProjectionBase<TState>
    where TState : IEquatable<TState>
{
  private readonly List<Action<TState, TState>> _segments;

  /// <summary>
  /// Initialises the projection base. Subclasses register segments via
  /// <see cref="Segment{TSegment}"/> in their constructor.
  /// </summary>
  protected StateProjectionBase()
  {
    _segments = [];
  }

  /// <summary>
  /// Register a projection segment: a selector extracting a sub-state,
  /// and an action to update UI when that sub-state changes.
  /// </summary>
  /// <typeparam name="TSegment">The sub-state type. Must implement value equality.</typeparam>
  /// <param name="selector">Extracts the sub-state from the full state.</param>
  /// <param name="project">Updates the UI for the changed sub-state. Called on the UI thread.</param>
  protected void Segment<TSegment>(
      Func<TState, TSegment> selector,
      Action<TSegment> project)
      where TSegment : IEquatable<TSegment>
  {
    _segments.Add((oldState, newState) =>
    {
      TSegment oldSegment = selector(oldState);
      TSegment newSegment = selector(newState);

      if (!EqualityComparer<TSegment>.Default.Equals(oldSegment, newSegment))
      {
        project(newSegment);
      }
    });
  }

  /// <summary>
  /// Called on the UI thread after every state transition.
  /// Evaluates all registered segments and invokes those whose sub-state changed.
  /// </summary>
  /// <param name="oldState">State before the transition.</param>
  /// <param name="newState">State after the transition.</param>
  public void Project(TState oldState, TState newState)
  {
    if (oldState.Equals(newState))
    {
      return;
    }

    foreach (Action<TState, TState> segment in _segments)
    {
      segment(oldState, newState);
    }
  }

  /// <summary>
  /// Force-project all segments from a default/initial state.
  /// Called once during startup to set initial UI state.
  /// </summary>
  /// <param name="initialState">The initial state to project.</param>
  public abstract void ProjectInitial(TState initialState);
}
