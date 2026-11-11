using PerplexPDF.Tests;
using Xunit;

namespace PerplexPDF.UnitTests;

public sealed class CoreTests
{
    [Fact] public void CoordinatesSurviveAllRotations() => CoreScenarios.Rotation();
    [Fact] public void ThirtyCommandsUndoAndRedoWithoutLosingSavedState() => CoreScenarios.UndoRedo();
    [Fact] public void PageRangesAreOrderedAndValidated() => CoreScenarios.Ranges();
    [Fact] public Task ProjectRoundTripRetainsEditableObjects() => CoreScenarios.ProjectRoundTrip();
    [Fact] public Task ArchiveRejectsTraversalSymlinksAndExpansion() => CoreScenarios.HostileArchive();
    [Fact] public Task FailedCommitLeavesDestinationIntact() => CoreScenarios.AtomicCommit();
    [Fact] public void RecoveryIgnoresOtherRunningInstances() => CoreScenarios.Recovery();
}
