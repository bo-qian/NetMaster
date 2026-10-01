using NetMaster.Core;

namespace NetMaster.Core.Tests;

public sealed class ConfigurationSessionTests
{
    [Fact]
    public void NewSessionHasNoSavedCandidateAndCannotTestOrSave()
    {
        var flow = new ConfigurationSession(); flow.Begin(); flow.AwaitLogin();
        Assert.True(flow.Active); Assert.Null(flow.Candidate);
        Assert.False(flow.CanValidate); Assert.False(flow.Validate()); Assert.False(flow.Save());
    }

    [Fact]
    public void AlreadyOnlineDoesNotConfirmNewInformation()
    {
        var flow = new ConfigurationSession(); flow.Begin(); flow.Receive(StorageTests.FakeProfile with { Confirmed = false });
        Assert.True(flow.Validate()); flow.Authentication(new("alreadyOnline", "fixture"));
        Assert.False(flow.CanSave); Assert.False(flow.Candidate!.Confirmed); Assert.Equal(ConfigurationStage.Captured, flow.Stage);
    }

    [Fact]
    public void SaveFailureRetainsVerifiedCandidateForRetry()
    {
        var flow = new ConfigurationSession(); flow.Begin(); flow.Receive(StorageTests.FakeProfile);
        Assert.True(flow.Save()); Assert.False(flow.CanValidate); Assert.False(flow.Save());
        flow.Fail("fixture save failure");
        Assert.Equal(ConfigurationStage.SaveFailed, flow.Stage); Assert.True(flow.CanSave); Assert.Equal(StorageTests.FakeProfile, flow.Candidate);
        Assert.True(flow.Save()); flow.End(); Assert.Null(flow.Candidate); Assert.False(flow.Active);
    }

    [Fact]
    public void NewLoginAndCancellationDiscardPreviousCandidateAndFeedback()
    {
        var flow = new ConfigurationSession(); flow.Begin(); flow.Receive(StorageTests.FakeProfile); flow.Fail("fixture");
        flow.AwaitLogin(); Assert.Null(flow.Candidate); Assert.Null(flow.Message); Assert.False(flow.Error);
        flow.Receive(StorageTests.FakeProfile); flow.End(); flow.Begin();
        Assert.Null(flow.Candidate); Assert.Null(flow.Message); Assert.False(flow.CanSave);
    }

    [Fact]
    public void ExplicitRejectionRevokesConfirmationButAmbiguousRetestDoesNot()
    {
        var flow = new ConfigurationSession(); flow.Begin(); flow.Receive(StorageTests.FakeProfile);
        flow.Validate(); flow.Authentication(new("unknown", "fixture unknown")); Assert.True(flow.CanSave);
        flow.Validate(); flow.Authentication(new("rejected", "fixture rejection")); Assert.False(flow.CanSave);
        Assert.Null(flow.Candidate!.VerifiedAt);
    }
}
