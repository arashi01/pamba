// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;
using Xunit;

namespace Pamba.WinUI.Tests;

public sealed class StateProjectionBaseTests
{
  private sealed record TestState(int Count, string Label) : IEquatable<TestState>;

  private sealed class TestProjection : StateProjectionBase<TestState>
  {
    public List<int> CountProjections { get; } = [];
    public List<string> LabelProjections { get; } = [];

    public TestProjection()
    {
      Segment(s => s.Count, count => CountProjections.Add(count));
      Segment(s => s.Label, label => LabelProjections.Add(label));
    }

    public override void ProjectInitial(TestState initialState)
    {
      CountProjections.Add(initialState.Count);
      LabelProjections.Add(initialState.Label);
    }
  }

  [Fact]
  public void Project_fires_only_changed_segments()
  {
    var projection = new TestProjection();
    var oldState = new TestState(1, "hello");
    var newState = new TestState(2, "hello"); // Only Count changed

    projection.Project(oldState, newState);

    Assert.Single(projection.CountProjections);
    Assert.Equal(2, projection.CountProjections[0]);
    Assert.Empty(projection.LabelProjections); // Label unchanged, not projected
  }

  [Fact]
  public void Project_fires_all_changed_segments()
  {
    var projection = new TestProjection();
    var oldState = new TestState(1, "hello");
    var newState = new TestState(2, "world"); // Both changed

    projection.Project(oldState, newState);

    Assert.Single(projection.CountProjections);
    Assert.Single(projection.LabelProjections);
  }

  [Fact]
  public void Project_skips_entirely_when_state_unchanged()
  {
    var projection = new TestProjection();
    var state = new TestState(1, "hello");

    projection.Project(state, state);

    Assert.Empty(projection.CountProjections);
    Assert.Empty(projection.LabelProjections);
  }

  [Fact]
  public void ProjectInitial_projects_all_segments()
  {
    var projection = new TestProjection();
    var initial = new TestState(5, "start");

    projection.ProjectInitial(initial);

    Assert.Single(projection.CountProjections);
    Assert.Equal(5, projection.CountProjections[0]);
    Assert.Single(projection.LabelProjections);
    Assert.Equal("start", projection.LabelProjections[0]);
  }
}
