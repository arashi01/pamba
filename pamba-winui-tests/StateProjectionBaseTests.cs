// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System.Collections.Generic;
using Xunit;

namespace Pamba.WinUI.Tests;

public sealed class StateProjectionBaseTests
{
  private sealed record TestState(int Count, string Label) : System.IEquatable<TestState>;

  private sealed class TestProjection : StateProjectionBase<TestState>
  {
    public List<int> CountProjections { get; } = [];
    public List<string> LabelProjections { get; } = [];

    public TestProjection()
    {
      Segment(s => s.Count, count => CountProjections.Add(count));
      Segment(s => s.Label, label => LabelProjections.Add(label));
    }
  }

  [Fact]
  public void Project_fires_only_changed_segments()
  {
    TestProjection projection = new();
    TestState oldState = new(1, "hello");
    TestState newState = new(2, "hello"); // Only Count changed

    projection.Project(oldState, newState);

    Assert.Single(projection.CountProjections);
    Assert.Equal(2, projection.CountProjections[0]);
    Assert.Empty(projection.LabelProjections); // Label unchanged, not projected
  }

  [Fact]
  public void Project_fires_all_changed_segments()
  {
    TestProjection projection = new();
    TestState oldState = new(1, "hello");
    TestState newState = new(2, "world"); // Both changed

    projection.Project(oldState, newState);

    Assert.Single(projection.CountProjections);
    Assert.Single(projection.LabelProjections);
  }

  [Fact]
  public void Project_skips_entirely_when_state_unchanged()
  {
    TestProjection projection = new();
    TestState state = new(1, "hello");

    projection.Project(state, state);

    Assert.Empty(projection.CountProjections);
    Assert.Empty(projection.LabelProjections);
  }

  [Fact]
  public void ProjectInitial_default_implementation_projects_all_segments()
  {
    TestProjection projection = new();
    TestState initial = new(5, "start");

    projection.ProjectInitial(initial);

    Assert.Single(projection.CountProjections);
    Assert.Equal(5, projection.CountProjections[0]);
    Assert.Single(projection.LabelProjections);
    Assert.Equal("start", projection.LabelProjections[0]);
  }
}
