using NetMaster.Core;

namespace NetMaster.Core.Tests;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void MissingWindowsTaskIsAbsentRatherThanAStorageFailure()
    {
        // Read the real scheduler without registering or changing any tasks.
        Assert.Null(StartupRegistration.FindTask("NetMaster_Missing_Test_" + Guid.NewGuid().ToString("N")));
    }
}
